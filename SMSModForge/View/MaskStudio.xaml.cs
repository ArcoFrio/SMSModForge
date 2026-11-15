using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SMSModForge.Localization;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// The mask painter, inside the main window (the author, 1.9.0: a window of its
/// own over the editor, whose preview had to be watched behind it, felt the
/// wrong way round). Painting on the left, the live preview of the shader on the
/// right; the main window collapses everything else while it is open, so the
/// tabs' previews stop drawing.
/// <para/>
/// Holds three independent 8-bit channels (R/G/B; one in alpha for a level) in a
/// <see cref="MaskBuffer"/>, composites them with the art for the canvas, and
/// republishes a BGRA snapshot through <see cref="IMaskEditorHost.LiveMaskBgra"/>
/// so the preview renders strokes live without touching disk. Save is explicit.
/// </summary>
public partial class MaskStudio : UserControl
{
    private readonly IMaskEditorHost _host;
    private readonly string _packRoot;
    private readonly MaskBuffer _mask;
    private readonly MaskHistory _history = new();
    private readonly WriteableBitmap _viewBitmap;
    private readonly byte[] _liveBgra = new byte[MaskBuffer.Size * MaskBuffer.Size * 4];
    private byte[]? _diffuse;
    private bool _loadedOnce;
    private bool _closed;

    // Layer / tool state
    private bool _showR = true, _showG = true, _showB = true, _showDiffuse = true;
    private int _activeChannel;          // 0 = R, 1 = G, 2 = B
    /// <summary>What a stroke does: paints, erases, or softens (1.9.0).</summary>
    internal enum Tool { Brush, Eraser, Soften }
    private Tool _tool;

    /// <summary>The layer blurred, taken when a soften stroke begins: what it
    /// pulls towards.</summary>
    private byte[]? _strokeBlurred;
    private bool _mirror;

    // Zoom state
    private double _zoom = 1.0;
    private const double ZoomMin = 0.25;
    private const double ZoomMax = 8.0;
    /// <summary>Whether the view still follows the room it has (fitted) or was
    /// zoomed by hand and stays where it was put.</summary>
    private bool _fitted = true;

    /// <summary>Canvas size at 100%: twice the mask's resolution, so a 256-px mask
    /// is big enough to paint on.</summary>
    private const double BaseDisplaySize = MaskBuffer.Size * 2;

    /// <summary>Width over height of the art painted on, for the CANVAS only: the
    /// buffer stays square (the shader samples it in UV space), the view takes the
    /// art's shape. 1.0 for a bust.</summary>
    private double _referenceAspect = 1.0;

    // Stroke state
    private bool _drawing;
    private bool _dirty;
    private double _lastX, _lastY;       // last stamp in texture-space pixels
    private float[]? _strokeBuffer;      // per-stroke max-contribution buffer
    private byte[]? _strokeOriginal;     // channel snapshot at stroke start

    /// <summary>What is being painted, for the header.</summary>
    public string Title { get; }

    /// <summary>The host whose mask this is.</summary>
    internal IMaskEditorHost Host => _host;

    /// <summary>Whether there are strokes not yet saved.</summary>
    internal bool IsDirty => _dirty;

    /// <summary>Raised once the studio has closed (saved, discarded or clean);
    /// the main window takes it away and puts the tabs back.</summary>
    public event EventHandler? Closed;

    public MaskStudio(IMaskEditorHost host, string packRoot, string title, string subtitle, FrameworkElement? preview)
    {
        InitializeComponent();
        PointerOnArea = e => e.GetPosition(CanvasArea);
        _host = host;
        _packRoot = packRoot;
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        PreviewSlot.Content = preview;
        _mask = new MaskBuffer(host.MaskKind);
        if (_mask.ChannelCount == 1)
        {
            // A level mask has one plane, in alpha: the other two layers go, and the
            // one left goes neutral - its red meant "the R channel".
            LayerG.Visibility = Visibility.Collapsed;
            LayerB.Visibility = Visibility.Collapsed;
            ActiveRLabel.Text = Loc.T("maskEditor.intensity");
            ActiveRLabel.Foreground = Brushes.White;
            ShowRDot.Foreground = Brushes.White;
            ShowRToggle.ToolTip = Loc.T("maskEditor.showMask.tip");
            LayerR.BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            LayerR.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            _activeChannel = 0;
        }
        _viewBitmap = new WriteableBitmap(MaskBuffer.Size, MaskBuffer.Size, 96, 96, PixelFormats.Bgra32, null);

        // The split between canvas and preview, as it was left.
        double split = Services.EditorPrefs.MaskStudioSplit;
        CanvasColumn.Width = new GridLength(split, GridUnitType.Star);
        PreviewColumn.Width = new GridLength(1 - split, GridUnitType.Star);
    }

    private static double ReadAspect(string absPath)
    {
        try
        {
            using var stream = File.OpenRead(absPath);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
            if (frame.PixelHeight > 0 && frame.PixelWidth > 0)
                return frame.PixelWidth / (double)frame.PixelHeight;
        }
        catch { /* unreadable - fall back to square */ }
        return 1.0;
    }

    // ───────────────────────── lifecycle

    private void Studio_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded comes again whenever the studio is put back in the tree; the
        // mask is read once.
        if (_loadedOnce) { CanvasHost.Focus(); return; }
        _loadedOnce = true;
        MaskImage.Source = _viewBitmap;

        var basePath = Path.Combine(_packRoot, Normalize(_host.PoseSpritePath));
        // A mask borrowed from one of the game's busts starts from the copy of
        // it shipped with the editor; saved, it becomes a file of the pack's.
        var maskPath = Shared.GameArt.IsBorrowed(_host.MaskPath)
            ? VanillaArtResolver.GameArtFile(_host.MaskPath, "Mask.PNG") ?? ""
            : Path.Combine(_packRoot, Normalize(_host.MaskPath));
        if (File.Exists(basePath))
        {
            _diffuse = BustComposer.LoadPng(basePath);
            _referenceAspect = ReadAspect(basePath);
        }
        ApplyZoomSize();
        if (!string.IsNullOrWhiteSpace(_host.MaskPath) && File.Exists(maskPath))
            _mask.FromBgra(BustComposer.LoadPng(maskPath));

        // Publish the live buffer so the preview switches over to it.
        _mask.ToBgra(_liveBgra);
        _host.LiveMaskBgra = _liveBgra;
        _host.LiveMaskRevision++;

        OfferTheOtherOutfits();
        RecomposeAll();
        UpdateStatus();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FitView));
        CanvasHost.Focus();
    }

    /// <summary>
    /// Close, asking first about strokes not saved: Yes saves, No throws them
    /// away, Cancel stays. False when it stays. Hands the preview back to the
    /// mask on disk as it goes.
    /// </summary>
    public bool TryClose()
    {
        if (_closed) return true;
        if (_panning) EndPan();
        if (_dirty)
        {
            var answer = AnswerUnsaved?.Invoke()
                         ?? (Services.TestMode.Active
                             ? MessageBoxResult.No
                             : MessageBox.Show(PromptOwner, Loc.T("maskEditor.unsaved"), Loc.T("mask.title"),
                                               MessageBoxButton.YesNoCancel, MessageBoxImage.Question));
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.Yes && !Save()) return false;
        }
        _closed = true;
        _host.LiveMaskBgra = null;
        _host.LiveMaskRevision++;
        Closed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The answer to "save the mask?" in place of asking - for tests.</summary>
    internal Func<MessageBoxResult>? AnswerUnsaved;

    /// <summary>Where to save a mask that has no file yet, in place of the save
    /// dialog - for tests. Null for "cancelled".</summary>
    internal Func<string?>? AskSavePath;

    private Window PromptOwner => Window.GetWindow(this) ?? Application.Current.MainWindow;

    private static string Normalize(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    // ──────────────────────── copying another outfit's layers

    private IReadOnlyList<MaskSource> _otherMasks = Array.Empty<MaskSource>();

    private void OfferTheOtherOutfits()
    {
        _otherMasks = _host.OtherMasks ?? Array.Empty<MaskSource>();
        if (_otherMasks.Count == 0) return;
        CopyFromList.ItemsSource = _otherMasks.Select(m => m.Label).ToList();
        CopyFromPanel.Visibility = Visibility.Visible;

        // Which of its layers: all of them, or one over the same layer here.
        var layers = new List<string> { Loc.T("maskStudio.copyLayer.all") };
        for (int channel = 0; channel < _mask.ChannelCount; channel++)
            layers.Add(Loc.F("maskStudio.copyLayer.only", "layer", LayerName(channel)));
        CopyLayerList.ItemsSource = layers;
        CopyLayerList.SelectedIndex = 0;
        CopyLayerList.Visibility = _mask.ChannelCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The layer chosen to copy on its own, or -1 for every layer.</summary>
    internal int CopyLayer
    {
        get => _mask.ChannelCount > 1 ? CopyLayerList.SelectedIndex - 1 : -1;
        set => CopyLayerList.SelectedIndex = value + 1;
    }

    /// <summary>Picks the outfit to copy from - for tests.</summary>
    internal void ChooseCopySource(int index) => CopyFromList.SelectedIndex = index;

    /// <summary>Copies from the outfit chosen - for tests.</summary>
    internal void CopyChosen() => CopyFrom_Click(this, new RoutedEventArgs());

    private void CopyFrom_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => CopyFromButton.IsEnabled = CopyFromList.SelectedIndex >= 0;

    private void CopyFrom_Click(object sender, RoutedEventArgs e)
    {
        int at = CopyFromList.SelectedIndex;
        if (at < 0 || at >= _otherMasks.Count) return;

        var from = _otherMasks[at];
        string path = Path.Combine(_packRoot, Normalize(from.MaskPath));
        if (!File.Exists(path))
        {
            if (!Services.TestMode.Active)
                MessageBox.Show(PromptOwner,
                    Loc.F("maskEditor.copy.missing", "outfit", from.Label, "path", from.MaskPath),
                    Loc.T("maskEditor.copy.missing.title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int only = CopyLayer;

        // Everything on this canvas goes (or everything on the one layer),
        // painted or not: asked first.
        if (!Services.TestMode.Active)
        {
            string ask = only < 0
                ? Loc.F(_dirty ? "maskEditor.copy.askUnsaved" : "maskEditor.copy.ask", "outfit", from.Label)
                : Loc.F(_dirty ? "maskStudio.copy.askOneUnsaved" : "maskStudio.copy.askOne",
                        "outfit", from.Label, "layer", LayerName(only));
            var answer = MessageBox.Show(PromptOwner, ask,
                Loc.T("maskEditor.copy.title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        if (only >= 0)
        {
            // One layer: read the other mask the way this one was read, and take
            // that layer alone. The others stay as they are; the layer copied is
            // the one being painted next.
            _history.Snapshot(only, _mask.Channel(only), _mask.A);
            var other = new MaskBuffer(_mask.Kind);
            other.FromBgra(BustComposer.LoadPng(path));
            Buffer.BlockCopy(other.Channel(only), 0, _mask.Channel(only), 0, _mask.Channel(only).Length);
            (only switch { 0 => ActiveR, 1 => ActiveG, _ => ActiveB }).IsChecked = true;
            _mask.ToBgra(_liveBgra);
            _host.LiveMaskRevision++;
            MarkDirty();
            RecomposeAll();
            CanvasHost.Focus();
            return;
        }

        // Every channel snapshotted first, so one Ctrl+Z undoes the whole load.
        for (int channel = 0; channel < _mask.ChannelCount; channel++)
            _history.Snapshot(channel, _mask.Channel(channel), _mask.A);

        _mask.FromBgra(BustComposer.LoadPng(path));
        _mask.ToBgra(_liveBgra);
        _host.LiveMaskRevision++;
        MarkDirty();
        RecomposeAll();
        CanvasHost.Focus();
    }

    // ───────────────────────── keyboard

    private void Studio_KeyDown(object sender, KeyEventArgs e)
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;
        if (focused is TextBox && !IsCanvasFocused()) return;
        if (focused is RangeBase) return; // sliders take their own keys
        if (focused is ComboBox) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        switch (e.Key)
        {
            case Key.B when !ctrl: SetTool(Tool.Brush); e.Handled = true; break;
            case Key.E when !ctrl: SetTool(Tool.Eraser); e.Handled = true; break;

            // 2 and 3 are inert on a one-plane mask.
            case Key.D1: case Key.NumPad1: ActiveR.IsChecked = true; e.Handled = true; break;
            case Key.D2: case Key.NumPad2:
                if (_mask.ChannelCount > 1) ActiveG.IsChecked = true;
                e.Handled = true; break;
            case Key.D3: case Key.NumPad3:
                if (_mask.ChannelCount > 2) ActiveB.IsChecked = true;
                e.Handled = true; break;

            case Key.R when !ctrl: _showR = !_showR; ShowRToggle.IsChecked = _showR; RecomposeAll(); e.Handled = true; break;
            case Key.G when !ctrl:
                if (_mask.ChannelCount > 1) { _showG = !_showG; ShowGToggle.IsChecked = _showG; RecomposeAll(); }
                e.Handled = true; break;
            case Key.D when !ctrl: _showDiffuse = !_showDiffuse; ShowDiffuseToggle.IsChecked = _showDiffuse; RecomposeAll(); e.Handled = true; break;

            case Key.OemOpenBrackets: SizeSlider.Value = Math.Max(SizeSlider.Minimum, SizeSlider.Value - 2); e.Handled = true; break;
            case Key.OemCloseBrackets: SizeSlider.Value = Math.Min(SizeSlider.Maximum, SizeSlider.Value + 2); e.Handled = true; break;

            case Key.OemComma: NudgeOpacity(-1); e.Handled = true; break;
            case Key.OemPeriod: NudgeOpacity(+1); e.Handled = true; break;

            case Key.M when !ctrl: SetMirror(!_mirror); e.Handled = true; break;

            case Key.Home: FitView(); e.Handled = true; break;
            case Key.OemPlus: case Key.Add: ZoomBy(1.25); e.Handled = true; break;
            case Key.OemMinus: case Key.Subtract: ZoomBy(1 / 1.25); e.Handled = true; break;

            case Key.C when !ctrl: ClearActiveChannel(); e.Handled = true; break;
            case Key.F when !ctrl: FillActiveChannel(); e.Handled = true; break;
            case Key.I when !ctrl: InvertActiveChannel(); e.Handled = true; break;
            case Key.S when !ctrl: SetTool(Tool.Soften); e.Handled = true; break;

            case Key.Z when ctrl && shift: ApplicationCommands.Redo.Execute(null, this); e.Handled = true; break;
            case Key.Z when ctrl: ApplicationCommands.Undo.Execute(null, this); e.Handled = true; break;
            case Key.Y when ctrl: ApplicationCommands.Redo.Execute(null, this); e.Handled = true; break;
            case Key.S when ctrl: Save(); e.Handled = true; break;

            case Key.Escape when !_drawing && !_panning: CloseBtn_Click(this, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    /// <summary>Left and Right on a focused slider: one unit, or five with Shift.</summary>
    private void Slider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider) return;
        double unit = ReferenceEquals(slider, SizeSlider) ? 1 : 0.01;
        if (NudgeSlider(slider, e.Key, Keyboard.Modifiers, unit)) e.Handled = true;
    }

    /// <summary>The step itself, apart from the live keyboard so a test can hold
    /// Shift down. Returns whether the key was one of the two.</summary>
    internal static bool NudgeSlider(Slider slider, Key key, ModifierKeys modifiers, double unit)
    {
        int direction = key switch { Key.Left => -1, Key.Right => +1, _ => 0 };
        if (direction == 0) return false;

        double step = unit * ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 5 : 1);
        int digits = unit >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(unit));
        slider.Value = Math.Clamp(Math.Round(slider.Value + direction * step, digits), slider.Minimum, slider.Maximum);
        return true;
    }

    private void NudgeOpacity(int direction)
    {
        bool coarse = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        double step = coarse ? 0.20 : 0.05;
        OpacitySlider.Value = Math.Clamp(OpacitySlider.Value + direction * step, OpacitySlider.Minimum, OpacitySlider.Maximum);
    }

    // ───────────────────────── whole-layer actions (each undoable)

    private void ClearActiveChannel() => ChangeActiveChannel(ch => Array.Clear(ch));

    private void FillActiveChannel() => ChangeActiveChannel(ch => Array.Fill(ch, (byte)255));

    /// <summary>Strong where it was faint and faint where it was strong.</summary>
    internal void InvertActiveChannel() => ChangeActiveChannel(ch =>
    {
        for (int i = 0; i < ch.Length; i++) ch[i] = (byte)(255 - ch[i]);
    });

    /// <summary>
    /// Smooths the layer being painted: each pixel becomes the average of the 3x3
    /// around it (edge pixels of the ones that exist). A mask's edges are where a
    /// shader tears - a hard step from moving to still reads as a crease - and
    /// this is the quick way to take one off without repainting it.
    /// </summary>
    internal void SoftenActiveChannel() => ChangeActiveChannel(ch => Soften(ch, MaskBuffer.Size));

    internal static void Soften(byte[] ch, int size)
    {
        var src = (byte[])ch.Clone();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int sum = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= size) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= size) continue;
                        sum += src[yy * size + xx];
                        n++;
                    }
                }
                ch[y * size + x] = (byte)((sum + n / 2) / n);
            }
    }

    private void ChangeActiveChannel(Action<byte[]> change)
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        change(ch);
        UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        MarkDirty();
    }

    private void ClearChannel_Click(object sender, RoutedEventArgs e) => ClearActiveChannel();
    private void FillChannel_Click(object sender, RoutedEventArgs e) => FillActiveChannel();
    private void Invert_Click(object sender, RoutedEventArgs e) => InvertActiveChannel();
    private void Soften_Click(object sender, RoutedEventArgs e) => SoftenActiveChannel();

    private bool IsCanvasFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        for (var at = focused; at != null; at = VisualTreeHelper.GetParent(at))
            if (ReferenceEquals(at, CanvasHost)) return true;
        return false;
    }

    // ───────────────────────── tools

    internal void SetTool(Tool tool)
    {
        _tool = tool;
        ToolBrushBtn.IsChecked = tool == Tool.Brush;
        ToolEraserBtn.IsChecked = tool == Tool.Eraser;
        ToolSoftenBtn.IsChecked = tool == Tool.Soften;
        UpdateStatus();
    }

    private void ToolBrush_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Brush);
    private void ToolEraser_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Eraser);
    private void ToolSoften_Click(object sender, RoutedEventArgs e) => SetTool(Tool.Soften);

    /// <summary>The stroke so far, onto the layer: painted, erased, or softened
    /// towards the layer's blur.</summary>
    private void ApplyStroke(byte[] channel, int x0, int y0, int x1, int y1)
    {
        if (_strokeBuffer == null || _strokeOriginal == null) return;
        if (_tool == Tool.Soften && _strokeBlurred != null)
            BrushEngine.ApplySoften(channel, _strokeOriginal, _strokeBlurred, _strokeBuffer, x0, y0, x1, y1);
        else
            BrushEngine.ApplyStrokeBuffer(channel, _strokeOriginal, _strokeBuffer, x0, y0, x1, y1,
                                          CapToggle.IsChecked == true, _tool == Tool.Eraser);
    }

    /// <summary>Paint both halves at once, mirrored about the canvas's middle - a
    /// bust is nearly always symmetrical, and a mask painted twice by hand never
    /// quite is.</summary>
    internal void SetMirror(bool on)
    {
        _mirror = on;
        MirrorToggle.IsChecked = on;
        MirrorAxis.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PlaceMirrorAxis();
        if (!on) MirrorCursor.Visibility = Visibility.Hidden;
        UpdateStatus();
    }

    private void Mirror_Click(object sender, RoutedEventArgs e) => SetMirror(MirrorToggle.IsChecked == true);

    private void PlaceMirrorAxis()
    {
        double mid = CanvasHost.Width / 2.0;
        MirrorAxis.X1 = MirrorAxis.X2 = mid;
        MirrorAxis.Y1 = 0;
        MirrorAxis.Y2 = CanvasHost.Height;
    }

    // ───────────────────────── channel / visibility

    private void ActiveR_Checked(object sender, RoutedEventArgs e) { _activeChannel = 0; UpdateStatus(); }
    private void ActiveG_Checked(object sender, RoutedEventArgs e) { _activeChannel = 1; UpdateStatus(); }
    private void ActiveB_Checked(object sender, RoutedEventArgs e) { _activeChannel = 2; UpdateStatus(); }

    private void ShowDiffuse_Click(object sender, RoutedEventArgs e) { _showDiffuse = ShowDiffuseToggle.IsChecked == true; RecomposeAll(); }
    private void ShowR_Click(object sender, RoutedEventArgs e) { _showR = ShowRToggle.IsChecked == true; RecomposeAll(); }
    private void ShowG_Click(object sender, RoutedEventArgs e) { _showG = ShowGToggle.IsChecked == true; RecomposeAll(); }
    private void ShowB_Click(object sender, RoutedEventArgs e) { _showB = ShowBToggle.IsChecked == true; RecomposeAll(); }

    // ───────────────────────── drawing

    private (double fx, double fy) ScreenToTex(Point p)
    {
        double sx = CanvasHost.ActualWidth > 0 ? CanvasHost.ActualWidth : CanvasHost.Width;
        double sy = CanvasHost.ActualHeight > 0 ? CanvasHost.ActualHeight : CanvasHost.Height;
        return (p.X / sx * MaskBuffer.Size, p.Y / sy * MaskBuffer.Size);
    }

    /// <summary>The mirrored twin of a point in texture space.</summary>
    private static double MirrorX(double fx) => MaskBuffer.Size - fx;

    // ───────────────────────── panning

    private bool _panning;
    private Point _panStart;
    private double _panStartX, _panStartY;

    /// <summary>Whether a middle-button pan is under way.</summary>
    internal bool IsPanning => _panning;

    /// <summary>Where the pointer is over the working area. A seam for tests,
    /// which cannot move the real pointer.</summary>
    internal Func<MouseEventArgs, Point> PointerOnArea;

    private void CanvasArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _drawing) return;
        _panning = true;
        _fitted = false;
        _panStart = PointerOnArea(e);
        _panStartX = PanTransform.X;
        _panStartY = PanTransform.Y;
        CanvasArea.CaptureMouse();
        Mouse.OverrideCursor = Cursors.SizeAll;
        BrushCursor.Visibility = Visibility.Hidden;
        BrushCursorHard.Visibility = Visibility.Hidden;
        MirrorCursor.Visibility = Visibility.Hidden;
        e.Handled = true;
    }

    private void CanvasArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        var now = PointerOnArea(e);
        PanTransform.X = _panStartX + (now.X - _panStart.X);
        PanTransform.Y = _panStartY + (now.Y - _panStart.Y);
    }

    private void CanvasArea_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panning || e.ChangedButton != MouseButton.Middle) return;
        EndPan();
        UpdateBrushCursor(e.GetPosition(CanvasHost));
        e.Handled = true;
    }

    private void CanvasArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_panning) EndPan();
    }

    private void EndPan()
    {
        _panning = false;
        Mouse.OverrideCursor = null;
        if (CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _panning) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        CanvasHost.Focus();

        var (fx, fy) = ScreenToTex(e.GetPosition(CanvasHost));

        // Alt-click takes the strength under the pointer as the brush's opacity,
        // to carry on a gradient at the level it has reached.
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
        {
            SampleAt(fx, fy);
            e.Handled = true;
            return;
        }

        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);

        int bufLen = MaskBuffer.Size * MaskBuffer.Size;
        _strokeBuffer = new float[bufLen];
        _strokeOriginal = new byte[bufLen];
        Buffer.BlockCopy(ch, 0, _strokeOriginal, 0, bufLen);

        _drawing = true;
        _lastX = fx; _lastY = fy;
        CanvasHost.CaptureMouse();

        StampPoint(ch, fx, fy);
        MarkDirty();
    }

    /// <summary>The strength of the layer being painted at a texture point, as the
    /// brush's opacity.</summary>
    internal void SampleAt(double fx, double fy)
    {
        int x = Math.Clamp((int)fx, 0, MaskBuffer.Size - 1), y = Math.Clamp((int)fy, 0, MaskBuffer.Size - 1);
        byte v = _mask.Channel(_activeChannel)[y * MaskBuffer.Size + x];
        OpacitySlider.Value = Math.Clamp(v / 255.0, OpacitySlider.Minimum, OpacitySlider.Maximum);
        UpdateStatus(Loc.F("maskStudio.sampled", "value", (v / 255.0).ToString("P0", System.Globalization.CultureInfo.CurrentCulture)));
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panning) return;

        var p = e.GetPosition(CanvasHost);
        UpdateBrushCursor(p);
        var (fx, fy) = ScreenToTex(p);
        ShowCursorValue(fx, fy);

        if (_drawing && e.LeftButton == MouseButtonState.Pressed && _strokeBuffer != null && _strokeOriginal != null)
        {
            StrokeTo(fx, fy);
        }
        else if (_drawing && e.LeftButton != MouseButtonState.Pressed)
        {
            EndStroke();
        }
    }

    /// <summary>Carry the stroke on to a texture point (and its mirrored twin).</summary>
    internal void StrokeTo(double fx, double fy)
    {
        if (_strokeBuffer == null || _strokeOriginal == null) return;
        var ch = _mask.Channel(_activeChannel);
        var rect = BrushEngine.StampLine(_strokeBuffer, (float)_lastX, (float)_lastY, (float)fx, (float)fy,
            (float)SizeSlider.Value, (float)HardnessSlider.Value, (float)OpacitySlider.Value);
        int x0 = rect.x0, y0 = rect.y0, x1 = rect.x1, y1 = rect.y1;
        if (_mirror)
        {
            var twin = BrushEngine.StampLine(_strokeBuffer, (float)MirrorX(_lastX), (float)_lastY, (float)MirrorX(fx), (float)fy,
                (float)SizeSlider.Value, (float)HardnessSlider.Value, (float)OpacitySlider.Value);
            x0 = Math.Min(x0, twin.x0); y0 = Math.Min(y0, twin.y0);
            x1 = Math.Max(x1, twin.x1); y1 = Math.Max(y1, twin.y1);
        }
        ApplyStroke(ch, x0, y0, x1, y1);
        UpdateLiveAndView(x0, y0, x1, y1);
        _lastX = fx; _lastY = fy;
    }

    /// <summary>Begin a stroke at a texture point without a pointer - for tests,
    /// which cannot press the real button.</summary>
    internal void BeginStrokeAt(double fx, double fy)
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        int bufLen = MaskBuffer.Size * MaskBuffer.Size;
        _strokeBuffer = new float[bufLen];
        _strokeOriginal = new byte[bufLen];
        Buffer.BlockCopy(ch, 0, _strokeOriginal, 0, bufLen);
        _strokeBlurred = _tool == Tool.Soften
            ? BrushEngine.Blurred(_strokeOriginal, MaskBuffer.Size, BrushEngine.SoftenRadius)
            : null;
        _drawing = true;
        _lastX = fx; _lastY = fy;
        StampPoint(ch, fx, fy);
        MarkDirty();
    }

    internal void EndStroke()
    {
        _drawing = false;
        _strokeBuffer = null;
        _strokeOriginal = null;
        _strokeBlurred = null;
        if (CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
        UpdateStatus();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing) EndStroke();
    }

    private void Canvas_MouseEnter(object sender, MouseEventArgs e) => UpdateBrushCursor(e.GetPosition(CanvasHost));

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        BrushCursor.Visibility = Visibility.Hidden;
        BrushCursorHard.Visibility = Visibility.Hidden;
        MirrorCursor.Visibility = Visibility.Hidden;
        CursorText.Text = "";
    }

    private void StampPoint(byte[] channel, double fx, double fy)
    {
        if (_strokeBuffer == null || _strokeOriginal == null) return;
        var (x0, y0, x1, y1) = BrushEngine.Stamp(_strokeBuffer, (float)fx, (float)fy,
            (float)SizeSlider.Value, (float)HardnessSlider.Value, (float)OpacitySlider.Value);
        if (_mirror)
        {
            var twin = BrushEngine.Stamp(_strokeBuffer, (float)MirrorX(fx), (float)fy,
                (float)SizeSlider.Value, (float)HardnessSlider.Value, (float)OpacitySlider.Value);
            x0 = Math.Min(x0, twin.x0); y0 = Math.Min(y0, twin.y0);
            x1 = Math.Max(x1, twin.x1); y1 = Math.Max(y1, twin.y1);
        }
        ApplyStroke(channel, x0, y0, x1, y1);
        UpdateLiveAndView(x0, y0, x1, y1);
    }

    private void UpdateBrushCursor(Point p)
    {
        // The brush is a circle in TEXTURE space; on a canvas of another shape it
        // is drawn as the ellipse that circle covers. Zoom is the render
        // transform's, so it is left out here.
        double w = CanvasHost.ActualWidth > 0 ? CanvasHost.ActualWidth : CanvasHost.Width;
        double h = CanvasHost.ActualHeight > 0 ? CanvasHost.ActualHeight : CanvasHost.Height;
        double scaleX = w / MaskBuffer.Size, scaleY = h / MaskBuffer.Size;
        double rx = SizeSlider.Value * scaleX, ry = SizeSlider.Value * scaleY;
        double hx = rx * HardnessSlider.Value, hy = ry * HardnessSlider.Value;

        BrushCursor.Width = rx * 2; BrushCursor.Height = ry * 2;
        Canvas.SetLeft(BrushCursor, p.X - rx);
        Canvas.SetTop(BrushCursor, p.Y - ry);
        BrushCursor.Visibility = Visibility.Visible;

        if (HardnessSlider.Value < 0.995)
        {
            BrushCursorHard.Width = hx * 2; BrushCursorHard.Height = hy * 2;
            Canvas.SetLeft(BrushCursorHard, p.X - hx);
            Canvas.SetTop(BrushCursorHard, p.Y - hy);
            BrushCursorHard.Visibility = Visibility.Visible;
        }
        else BrushCursorHard.Visibility = Visibility.Hidden;

        if (_mirror)
        {
            MirrorCursor.Width = rx * 2; MirrorCursor.Height = ry * 2;
            Canvas.SetLeft(MirrorCursor, w - p.X - rx);
            Canvas.SetTop(MirrorCursor, p.Y - ry);
            MirrorCursor.Visibility = Visibility.Visible;
        }
    }

    private void ShowCursorValue(double fx, double fy)
    {
        int x = (int)Math.Floor(fx), y = (int)Math.Floor(fy);
        if (x < 0 || y < 0 || x >= MaskBuffer.Size || y >= MaskBuffer.Size) { CursorText.Text = ""; return; }
        byte v = _mask.Channel(_activeChannel)[y * MaskBuffer.Size + x];
        CursorText.Text = Loc.F("maskStudio.cursor", "x", x.ToString(), "y", y.ToString(),
                                "value", (v / 255.0).ToString("P0", System.Globalization.CultureInfo.CurrentCulture));
    }

    private void BrushParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasHost != null && CanvasHost.IsMouseOver)
            UpdateBrushCursor(Mouse.GetPosition(CanvasHost));
    }

    // ───────────────────────── view / live buffer

    private void RecomposeAll() => RecomposeRect(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);

    /// <summary>Repaint the canvas for an inclusive rectangle: the art at half
    /// strength as a reference, the visible layers on top in red, green, blue (a
    /// level's single plane as a neutral wash).</summary>
    private void RecomposeRect(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, MaskBuffer.Size - 1);
        y0 = Math.Clamp(y0, 0, MaskBuffer.Size - 1);
        x1 = Math.Clamp(x1, 0, MaskBuffer.Size - 1);
        y1 = Math.Clamp(y1, 0, MaskBuffer.Size - 1);
        if (x1 < x0 || y1 < y0) return;

        int w = x1 - x0 + 1, h = y1 - y0 + 1;
        var buf = new byte[w * h * 4];
        for (int y = y0; y <= y1; y++)
        {
            int srcRowBase = y * MaskBuffer.Size;
            int dstRowBase = (y - y0) * w * 4;
            for (int x = x0; x <= x1; x++)
            {
                int srcIdx = srcRowBase + x;
                int dstIdx = dstRowBase + (x - x0) * 4;
                float mr, mg, mb;
                if (_mask.ChannelCount == 1)
                {
                    mr = mg = mb = _showR ? _mask.A[srcIdx] / 255f : 0;
                }
                else
                {
                    mr = _showR ? _mask.R[srcIdx] / 255f : 0;
                    mg = _showG ? _mask.G[srcIdx] / 255f : 0;
                    mb = _showB ? _mask.B[srcIdx] / 255f : 0;
                }
                float cov = mr > mg ? mr : mg;
                if (mb > cov) cov = mb;

                float r = mr, g = mg, b = mb;
                if (_showDiffuse && _diffuse != null)
                {
                    int di = srcIdx * 4;
                    float da = _diffuse[di + 3] / 255f;
                    float inv = 1f - cov;
                    b += _diffuse[di] / 255f * da * 0.5f * inv;
                    g += _diffuse[di + 1] / 255f * da * 0.5f * inv;
                    r += _diffuse[di + 2] / 255f * da * 0.5f * inv;
                }

                buf[dstIdx] = (byte)(b * 255f);
                buf[dstIdx + 1] = (byte)(g * 255f);
                buf[dstIdx + 2] = (byte)(r * 255f);
                buf[dstIdx + 3] = 255;
            }
        }
        _viewBitmap.WritePixels(new Int32Rect(x0, y0, w, h), buf, w * 4, 0);
    }

    /// <summary>Refresh the shared live buffer (the preview picks it up on its
    /// next frame) and the canvas, for a dirty rectangle.</summary>
    private void UpdateLiveAndView(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Clamp(x0, 0, MaskBuffer.Size - 1);
        y0 = Math.Clamp(y0, 0, MaskBuffer.Size - 1);
        x1 = Math.Clamp(x1, 0, MaskBuffer.Size - 1);
        y1 = Math.Clamp(y1, 0, MaskBuffer.Size - 1);
        if (x1 < x0 || y1 < y0) return;

        for (int y = y0; y <= y1; y++)
        {
            int srcRowBase = y * MaskBuffer.Size;
            for (int x = x0; x <= x1; x++)
            {
                int srcIdx = srcRowBase + x;
                int dstIdx = srcIdx * 4;
                _liveBgra[dstIdx] = _mask.B[srcIdx];
                _liveBgra[dstIdx + 1] = _mask.G[srcIdx];
                _liveBgra[dstIdx + 2] = _mask.R[srcIdx];
                _liveBgra[dstIdx + 3] = _mask.A[srcIdx];
            }
        }
        _host.LiveMaskRevision++;
        RecomposeRect(x0, y0, x1, y1);
    }

    /// <summary>The active layer's bytes - for tests.</summary>
    internal byte[] ActiveChannelBytes => _mask.Channel(_activeChannel);

    /// <summary>A layer's bytes - for tests.</summary>
    internal byte[] LayerBytes(int channel) => _mask.Channel(channel);

    /// <summary>The layer being painted - for tests.</summary>
    internal int ActiveLayer => _activeChannel;

    // ───────────────────────── undo / redo / save

    private void UndoRedo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Undo) e.CanExecute = _history.CanUndo;
        else if (e.Command == ApplicationCommands.Redo) e.CanExecute = _history.CanRedo;
        e.Handled = true;
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_history.Undo(_mask))
        {
            UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
            MarkDirty();
        }
        e.Handled = true;
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_history.Redo(_mask))
        {
            UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
            MarkDirty();
        }
        e.Handled = true;
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) { Save(); e.Handled = true; }
    private void SaveBtn_Click(object sender, RoutedEventArgs e) => Save();
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => TryClose();

    internal bool Save()
    {
        string relPath = _host.MaskPath;
        // Borrowed from the game, or never saved: asked where.
        if (string.IsNullOrWhiteSpace(relPath) || Shared.GameArt.IsBorrowed(relPath))
        {
            string? abs;
            if (AskSavePath != null) abs = AskSavePath();
            else if (Services.TestMode.Active) abs = null;
            else
            {
                var (startIn, proposed) = SaveDefaults();
                var dialog = new SaveFileDialog
                {
                    InitialDirectory = startIn,
                    Filter = "PNG (*.png)|*.png",   // English on purpose: a file format's name.
                    FileName = proposed,
                    Title = Loc.T("maskEditor.save.dialogTitle"),
                };
                abs = dialog.ShowDialog(PromptOwner) == true ? dialog.FileName : null;
            }
            if (abs == null) return false;
            try
            {
                relPath = Path.GetRelativePath(_packRoot, abs).Replace('\\', '/');
            }
            catch
            {
                if (!Services.TestMode.Active)
                    MessageBox.Show(PromptOwner, Loc.T("maskEditor.save.outsidePack"),
                                    Loc.T("save.failed.title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            _host.MaskPath = relPath;
        }

        string absPath = Path.Combine(_packRoot, Normalize(relPath));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absPath)!);
            SavePng(absPath);
            _dirty = false;
            UnsavedMark.Visibility = Visibility.Collapsed;
            UpdateStatus(Loc.T("maskStudio.saved"));
            return true;
        }
        catch (Exception ex)
        {
            if (!Services.TestMode.Active)
                MessageBox.Show(PromptOwner, ex.Message, Loc.T("save.failed.title"), MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Where the save dialog opens and what it proposes: beside the art
    /// the mask was painted for, named after it (AnnaBase.png -> AnnaBaseMask.png).</summary>
    private (string Directory, string FileName) SaveDefaults()
    {
        string fallback = $"{_host.Key}_mask.PNG";
        string pose = _host.PoseSpritePath ?? "";
        if (string.IsNullOrWhiteSpace(pose)) return (_packRoot, fallback);

        string abs;
        try { abs = Path.Combine(_packRoot, Normalize(pose)); }
        catch { return (_packRoot, fallback); }

        string dir = Path.GetDirectoryName(abs) ?? _packRoot;
        if (!Directory.Exists(dir)) dir = _packRoot;

        string stem = Path.GetFileNameWithoutExtension(abs);
        return (dir, stem.Length == 0 ? fallback : stem + "Mask.png");
    }

    private void SavePng(string absPath)
    {
        _mask.ToBgra(_liveBgra);
        var bmp = new WriteableBitmap(MaskBuffer.Size, MaskBuffer.Size, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, MaskBuffer.Size, MaskBuffer.Size), _liveBgra, MaskBuffer.BgraStride, 0);
        using var stream = File.Create(absPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(stream);
    }

    private void MarkDirty()
    {
        _dirty = true;
        UnsavedMark.Visibility = Visibility.Visible;
        UpdateStatus();
    }

    private string LayerName(int channel)
        => _mask.ChannelCount == 1 ? Loc.T("maskEditor.intensity")
         : channel switch { 0 => Loc.T("maskEditor.bounceR"), 1 => Loc.T("maskEditor.waveG"), _ => Loc.T("maskEditor.noiseB") };

    private void UpdateStatus(string? note = null)
    {
        if (StatusText is null || _mask is null) return;
        string text = Loc.F(_tool switch
                            {
                                Tool.Eraser => "maskStudio.status.eraser",
                                Tool.Soften => "maskStudio.status.soften",
                                _ => "maskStudio.status.brush",
                            }, "layer", LayerName(_activeChannel));
        if (_mirror) text += "  ·  " + Loc.T("maskStudio.mirror");
        if (note != null) text += "  ·  " + note;
        StatusText.Text = text;
    }

    // ───────────────────────── zoom

    private void UpdateZoomDisplay()
    {
        if (ZoomDisplay != null) ZoomDisplay.Text = $"{(int)Math.Round(_zoom * 100)}%";
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var p = e.GetPosition(CanvasHost);
        double before = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1.0 / 1.15), ZoomMin, ZoomMax);
        _fitted = false;
        ApplyZoomSize();
        // Keep whatever is under the pointer where it is.
        double dz = _zoom - before;
        PanTransform.X -= (p.X - CanvasHost.Width / 2.0) * dz;
        PanTransform.Y -= (p.Y - CanvasHost.Height / 2.0) * dz;
        UpdateZoomDisplay();
    }

    /// <summary>Zoom about the middle of the view, by a factor.</summary>
    internal void ZoomBy(double factor)
    {
        double before = _zoom;
        _zoom = Math.Clamp(_zoom * factor, ZoomMin, ZoomMax);
        _fitted = false;
        ApplyZoomSize();
        double k = before > 0 ? _zoom / before : 1;
        PanTransform.X *= k;
        PanTransform.Y *= k;
        UpdateZoomDisplay();
    }

    /// <summary>The whole canvas in the room there is, centred - and following
    /// that room until zoomed by hand.</summary>
    internal void FitView()
    {
        ApplyZoomSize();
        double roomW = Math.Max(1, CanvasArea.ActualWidth - 24), roomH = Math.Max(1, CanvasArea.ActualHeight - 24);
        if (CanvasArea.ActualWidth > 0 && CanvasArea.ActualHeight > 0)
            _zoom = Math.Clamp(Math.Min(roomW / CanvasHost.Width, roomH / CanvasHost.Height), ZoomMin, ZoomMax);
        _fitted = true;
        ApplyZoomSize();
        PanTransform.X = PanTransform.Y = 0;
        UpdateZoomDisplay();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(1 / 1.25);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => FitView();

    private void CanvasArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitted && _loadedOnce) FitView();
    }

    /// <summary>The canvas's layout size follows only the art's shape - zoom is the
    /// render transform's.</summary>
    private void ApplyZoomSize()
    {
        double w = BaseDisplaySize;
        double h = w / (_referenceAspect > 0 ? _referenceAspect : 1.0);
        if (h > BaseDisplaySize)
        {
            h = BaseDisplaySize;
            w = h * _referenceAspect;
        }
        CanvasHost.Width = w;
        CanvasHost.Height = h;
        ZoomTransform.ScaleX = ZoomTransform.ScaleY = _zoom;
        PlaceMirrorAxis();
    }

    internal double Zoom => _zoom;

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        double canvas = CanvasColumn.ActualWidth, preview = PreviewColumn.ActualWidth;
        if (canvas + preview > 0) Services.EditorPrefs.MaskStudioSplit = canvas / (canvas + preview);
    }
}
