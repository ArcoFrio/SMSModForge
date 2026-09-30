using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;
using SMSModForge.Localization;

namespace SMSModForge.View;

/// <summary>
/// Per-outfit / per-NPC mask painter. Holds three independent 8-bit channels (R/G/B)
/// in a <see cref="MaskBuffer"/>, composites them with the diffuse reference
/// for the in-editor canvas, and republishes a BGRA snapshot through
/// <see cref="OutfitViewModel.LiveMaskBgra"/> so the main JigglePreview
/// renders strokes live without touching disk. Save is explicit (Ctrl+S).
/// </summary>
public partial class MaskEditorWindow : Window
{
    private readonly IMaskEditorHost _host;
    private readonly string _packRoot;
    private readonly MaskBuffer _mask;
    private readonly MaskHistory _history = new();
    private readonly WriteableBitmap _viewBitmap;
    private readonly byte[] _liveBgra = new byte[MaskBuffer.Size * MaskBuffer.Size * 4];
    private byte[]? _diffuse;

    // Layer / tool state
    private bool _showR = true, _showG = true, _showB = true, _showDiffuse = true;
    private int _activeChannel;          // 0 = R, 1 = G, 2 = B
    private bool _eraser;

    // Zoom state
    private double _zoom = 1.0;
    private const double ZoomMin = 0.25;
    private const double ZoomMax = 8.0;
    private const double ZoomStep = 0.25;

    /// <summary>Canvas size at 100%. Twice the mask resolution, so a 256-px mask
    /// is big enough to paint on before zooming. Must match the Width/Height the
    /// XAML gives CanvasHost, or the first zoom would jump.</summary>
    private const double BaseDisplaySize = MaskBuffer.Size * 2;

    /// <summary>
    /// Width over height of the art being painted on, for the CANVAS only.
    /// <para/>
    /// The buffer stays square, and must: the shader samples a mask in UV
    /// space, so a square mask stretched over a 16:9 quad already lands
    /// correctly, and reshaping the buffer would change nothing but the file.
    /// What the square cost was the VIEW — a level squeezed into it reads as
    /// cropped and paints nothing like it looks. So the canvas takes the art's
    /// shape while the pixels behind it stay square. 1.0 for a bust, which is
    /// what every previous mask was, so nothing there moves.
    /// </summary>
    private double _referenceAspect = 1.0;

    /// <summary>The reference PNG's own proportions, read from its header
    /// rather than from the loaded buffer — that has already been squashed to
    /// the square, which is precisely the information needed back.</summary>
    private static double ReadAspect(string absPath)
    {
        try
        {
            using var stream = File.OpenRead(absPath);
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                stream, System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            if (frame.PixelHeight > 0 && frame.PixelWidth > 0)
                return frame.PixelWidth / (double)frame.PixelHeight;
        }
        catch { /* unreadable — fall back to square */ }
        return 1.0;
    }

    // Stroke state
    private bool _drawing;
    private bool _dirty;
    private double _lastX, _lastY;       // last stamp in texture-space pixels
    private float[]? _strokeBuffer;      // per-stroke max-contribution buffer
    private byte[]? _strokeOriginal;     // channel snapshot at stroke start

    public MaskEditorWindow(IMaskEditorHost host, string packRoot)
    {
        InitializeComponent();
        PointerOnArea = e => e.GetPosition(CanvasArea);
        _host = host;
        _packRoot = packRoot;
        // A level mask is one intensity plane authored in ALPHA; a bust mask is
        // three planes in R/G/B. The buffer has to know which before anything
        // loads into it.
        _mask = new MaskBuffer(host.MaskKind);
        if (_mask.ChannelCount == 1)
        {
            // A level mask has one plane, so the other two layer panels go
            // entirely — not just their radio buttons, or their swatches and
            // show/hide toggles are left behind advertising channels that do
            // nothing here.
            LayerG.Visibility = Visibility.Collapsed;
            LayerB.Visibility = Visibility.Collapsed;

            // Restyle the surviving panel rather than replacing its content:
            // dropping a bare string into the RadioButton loses the label's
            // styling and leaves it near-unreadable against the panel. The red
            // is dropped too — it meant "the R channel", which this mask has no
            // concept of, so the panel goes neutral.
            ActiveRLabel.Text = Loc.T("maskEditor.intensity");
            ActiveRLabel.Foreground = Brushes.White;
            ShowRDot.Foreground = Brushes.White;
            ShowRToggle.ToolTip = Loc.T("maskEditor.showMask.tip");
            LayerR.BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            LayerR.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
            _activeChannel = 0;
        }
        _viewBitmap = new WriteableBitmap(MaskBuffer.Size, MaskBuffer.Size, 96, 96, PixelFormats.Bgra32, null);
    }

    // ───────────────────────── lifecycle

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        MaskImage.Source = _viewBitmap;

        var basePath = Path.Combine(_packRoot, Normalize(_host.PoseSpritePath));
        // A mask borrowed from one of the game's busts starts from the copy of
        // it shipped with the editor; saved, it becomes a file of the pack's.
        var maskPath = Shared.GameArt.IsBorrowed(_host.MaskPath)
            ? Rendering.VanillaArtResolver.GameArtFile(_host.MaskPath, "Mask.PNG") ?? ""
            : Path.Combine(_packRoot, Normalize(_host.MaskPath));
        if (File.Exists(basePath))
        {
            _diffuse = BustComposer.LoadPng(basePath);
            _referenceAspect = ReadAspect(basePath);
            ApplyZoomSize();
        }
        if (!string.IsNullOrWhiteSpace(_host.MaskPath) && File.Exists(maskPath))
            _mask.FromBgra(BustComposer.LoadPng(maskPath));

        // Publish the live BGRA buffer so JigglePreview switches over to it.
        _mask.ToBgra(_liveBgra);
        _host.LiveMaskBgra = _liveBgra;
        _host.LiveMaskRevision++;

        OfferTheOtherOutfits();

        RecomposeRect(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        UpdateStatus();
        CanvasHost.Focus();
    }

    // ──────────────────────── copying another outfit's layers

    /// <summary>What the list is showing, in the order it shows them.</summary>
    private IReadOnlyList<ViewModel.MaskSource> _otherMasks =
        System.Array.Empty<ViewModel.MaskSource>();

    /// <summary>
    /// Fill the copy-from list, or hide it.
    /// <para/>
    /// Once, on load: an outfit's siblings do not change while a painter window
    /// is open, and re-reading them on every click would be a list that flickers
    /// for no reason.
    /// </summary>
    private void OfferTheOtherOutfits()
    {
        _otherMasks = _host.OtherMasks ?? System.Array.Empty<ViewModel.MaskSource>();
        if (_otherMasks.Count == 0) return;

        CopyFromList.ItemsSource = _otherMasks.Select(m => m.Label).ToList();
        CopyFromPanel.Visibility = Visibility.Visible;
    }

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

        // Everything on this canvas goes, painted or not. Worth asking even
        // when nothing is dirty: a mask loaded from the file is still work
        // somebody did, and "it replaced my mask" is not something an undo
        // they have to discover makes better.
        if (!Services.TestMode.Active)
        {
            var answer = MessageBox.Show(PromptOwner,
                Loc.F(_dirty ? "maskEditor.copy.askUnsaved" : "maskEditor.copy.ask", "outfit", from.Label),
                Loc.T("maskEditor.copy.title"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        // Snapshot every channel first, so one Ctrl+Z undoes the whole load
        // rather than one plane of it.
        for (int channel = 0; channel < _mask.ChannelCount; channel++)
            _history.Snapshot(channel, _mask.Channel(channel), _mask.A);

        _mask.FromBgra(BustComposer.LoadPng(path));
        _mask.ToBgra(_liveBgra);
        _host.LiveMaskRevision++;
        _dirty = true;

        RecomposeAll();
        UpdateStatus();
        CanvasHost.Focus();
    }

    /// <summary>
    /// True while this window is closing, so anything it puts on screen is
    /// parented to the MAIN window instead of to a window that is about to
    /// stop existing. See <see cref="WindowOwnership"/>.
    /// </summary>
    private bool _closing;

    /// <summary>Whichever window a prompt from here should belong to.</summary>
    private Window PromptOwner => View.WindowOwnership.ModalOwner(this, _closing);

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // The pan cursor is set for the whole application, so it cannot be
        // left behind with the window.
        if (_panning) EndPan();
        _closing = true;
        try { ConfirmAndTidy(e); }
        finally { if (e.Cancel) _closing = false; }
    }

    private void ConfirmAndTidy(System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty)
        {
            var r = MessageBox.Show(PromptOwner,
                Loc.T("maskEditor.unsaved"),
                Loc.T("mask.title"),
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes && !Save()) { e.Cancel = true; return; }
        }
        // Hand the preview back to the file-loaded mask.
        _host.LiveMaskBgra = null;
        _host.LiveMaskRevision++;
    }

    private static string Normalize(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    // ───────────────────────── keyboard

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't steal keys away from focused text-editing controls.
        // Allow hotkeys when the canvas is focused or when focus is on non-text controls.
        var focused = Keyboard.FocusedElement as FrameworkElement;
        if (focused is TextBox && !IsCanvasFocused()) return;
        if (focused is System.Windows.Controls.Primitives.RangeBase) return; // sliders

        switch (e.Key)
        {
            // ── Tool switching ──────────────────────────────────────
            case Key.B: SetTool(eraser: false); e.Handled = true; break;
            case Key.E: SetTool(eraser: true);  e.Handled = true; break;

            // ── Channel selection ───────────────────────────────────
            // 2 and 3 are inert on a one-plane mask. Collapsing a RadioButton
            // does not stop IsChecked from taking, so without this the key
            // would select a channel that does not exist and Channel() would
            // throw on the next stroke.
            case Key.D1: case Key.NumPad1: ActiveR.IsChecked = true; e.Handled = true; break;
            case Key.D2: case Key.NumPad2:
                if (_mask.ChannelCount > 1) ActiveG.IsChecked = true;
                e.Handled = true; break;
            case Key.D3: case Key.NumPad3:
                if (_mask.ChannelCount > 2) ActiveB.IsChecked = true;
                e.Handled = true; break;

            // ── Channel visibility toggles ──────────────────────────
            case Key.R: _showR = !_showR; ShowRToggle.IsChecked = _showR; RecomposeAll(); e.Handled = true; break;
            case Key.G:
                if (_mask.ChannelCount > 1)
                { _showG = !_showG; ShowGToggle.IsChecked = _showG; RecomposeAll(); }
                e.Handled = true; break;
            case Key.D: _showDiffuse = !_showDiffuse; ShowDiffuseToggle.IsChecked = _showDiffuse; RecomposeAll(); e.Handled = true; break;

            // ── Brush size ──────────────────────────────────────────
            case Key.OemOpenBrackets:
                SizeSlider.Value = Math.Max(SizeSlider.Minimum, SizeSlider.Value - 2); e.Handled = true; break;
            case Key.OemCloseBrackets:
                SizeSlider.Value = Math.Min(SizeSlider.Maximum, SizeSlider.Value + 2); e.Handled = true; break;

            // ── Brush opacity ───────────────────────────────────────
            // Next to the size keys, since they're the pair you reach for
            // together. Shift takes the coarse step, for crossing the range
            // rather than nudging within it.
            case Key.OemComma:  NudgeOpacity(-1); e.Handled = true; break;
            case Key.OemPeriod: NudgeOpacity(+1); e.Handled = true; break;

            // ── View ────────────────────────────────────────────────
            case Key.Home: ResetView(); e.Handled = true; break;

            // ── Channel clear / fill ────────────────────────────────
            // C clears and F fills, matching the legend and the buttons — these
            // were the wrong way round. Both snapshot first: clearing a channel
            // is the single most destructive action here and it was the one
            // action Ctrl+Z could not take back.
            case Key.C: ClearActiveChannel(); e.Handled = true; break;
            case Key.F: FillActiveChannel(); e.Handled = true; break;

            // ── Undo / Redo (already bound via CommandBinding, but
            //    ensure they work when focus is on the canvas) ──────
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                ApplicationCommands.Undo.Execute(null, this); e.Handled = true; break;
            case Key.Y when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                ApplicationCommands.Redo.Execute(null, this); e.Handled = true; break;
            case Key.Z when (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift):
                ApplicationCommands.Redo.Execute(null, this); e.Handled = true; break;

            // ── Save ────────────────────────────────────────────────
            case Key.S when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                Save(); e.Handled = true; break;
        }
    }

    /// <summary>
    /// Left and Right on a focused slider: one of the units the slider shows,
    /// or five with Shift.
    /// <para/>
    /// The unit is what is written beside the slider - a pixel of brush size,
    /// one percent of hardness or opacity. WPF's own arrow step is the slider's
    /// SmallChange, which was five percent on the two percentage sliders, and
    /// it ignores Shift.
    /// </summary>
    private void Slider_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider) return;
        double unit = ReferenceEquals(slider, SizeSlider) ? 1 : 0.01;
        if (NudgeSlider(slider, e.Key, Keyboard.Modifiers, unit)) e.Handled = true;
    }

    /// <summary>The step itself, apart from the live keyboard so a test can
    /// hold Shift down. Returns whether the key was one of the two.</summary>
    internal static bool NudgeSlider(Slider slider, Key key, ModifierKeys modifiers, double unit)
    {
        int direction = key switch { Key.Left => -1, Key.Right => +1, _ => 0 };
        if (direction == 0) return false;

        double step = unit * ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 5 : 1);
        int digits = unit >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(unit));
        slider.Value = Math.Clamp(Math.Round(slider.Value + direction * step, digits),
                                  slider.Minimum, slider.Maximum);
        return true;
    }

    /// <summary>Step the brush opacity by one notch in <paramref name="direction"/>,
    /// coarse while Shift is held. Clamped to the slider's own range so the
    /// keys and the slider can't disagree.</summary>
    private void NudgeOpacity(int direction)
    {
        bool coarse = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        double step = coarse ? 0.20 : 0.05;
        OpacitySlider.Value = Math.Clamp(OpacitySlider.Value + direction * step,
                                         OpacitySlider.Minimum, OpacitySlider.Maximum);
    }

    /// <summary>Wipe the active channel. Snapshots first so it can be undone.</summary>
    private void ClearActiveChannel()
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        _mask.ClearChannel(_activeChannel);
        UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        _dirty = true;
        UpdateStatus();
    }

    /// <summary>Fill the active channel to full. Snapshots first.</summary>
    private void FillActiveChannel()
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        Array.Fill(ch, (byte)255);
        UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        _dirty = true;
        UpdateStatus();
    }

    private bool IsCanvasFocused()
    {
        var focused = Keyboard.FocusedElement as FrameworkElement;
        return focused == CanvasHost || IsDescendantOf(focused, CanvasHost);
    }

    private bool IsDescendantOf(FrameworkElement? element, FrameworkElement ancestor)
    {
        while (element != null)
        {
            if (element == ancestor) return true;
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }
        return false;
    }

    private void SetTool(bool eraser)
    {
        _eraser = eraser;
        ToolBrushBtn.IsChecked = !eraser;
        ToolEraserBtn.IsChecked = eraser;
        UpdateStatus();
    }

    private void ToolBrush_Click(object sender, RoutedEventArgs e)
    {
        // ToggleButton's IsChecked may be false if the user clicked it while it
        // was checked — re-pin it so we always have exactly one tool active.
        ToolBrushBtn.IsChecked = true;
        SetTool(false);
    }

    private void ToolEraser_Click(object sender, RoutedEventArgs e)
    {
        ToolEraserBtn.IsChecked = true;
        SetTool(true);
    }

    // ───────────────────────── channel / visibility

    private void ActiveR_Checked(object sender, RoutedEventArgs e) { _activeChannel = 0; UpdateStatus(); }
    private void ActiveG_Checked(object sender, RoutedEventArgs e) { _activeChannel = 1; UpdateStatus(); }
    private void ActiveB_Checked(object sender, RoutedEventArgs e) { _activeChannel = 2; UpdateStatus(); }

    private void ShowDiffuse_Click(object sender, RoutedEventArgs e) { _showDiffuse = ShowDiffuseToggle.IsChecked == true; RecomposeAll(); }
    private void ShowR_Click(object sender, RoutedEventArgs e) { _showR = ShowRToggle.IsChecked == true; RecomposeAll(); }
    private void ShowG_Click(object sender, RoutedEventArgs e) { _showG = ShowGToggle.IsChecked == true; RecomposeAll(); }
    private void ShowB_Click(object sender, RoutedEventArgs e) { _showB = ShowBToggle.IsChecked == true; RecomposeAll(); }

    private void ClearChannel_Click(object sender, RoutedEventArgs e) => ClearActiveChannel();

    private void FillChannel_Click(object sender, RoutedEventArgs e) => FillActiveChannel();

    // ───────────────────────── drawing

    private (double fx, double fy) ScreenToTex(Point p)
    {
        // CanvasHost's display size (after zoom) maps to the full texture.
        double sx = CanvasHost.ActualWidth, sy = CanvasHost.ActualHeight;
        return (p.X / sx * MaskBuffer.Size, p.Y / sy * MaskBuffer.Size);
    }

    // ───────────────────────── panning
    //
    // Zooming past the viewport used to put the edges of the mask out of reach,
    // since the canvas is centred in its border with no scroll host. Holding
    // the middle button pans, which keeps the left button free for painting and
    // matches what the wheel already does here (zoom).
    //
    // Handled on the whole working area (CanvasArea), not on the image: the
    // image's own handlers only hear the mouse while it is over the image, so
    // pressing the wheel on the dark area around it - most of the window when
    // zoomed out or panned away - did nothing. A press over the image bubbles
    // up to the area the same way.

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

        // Anchored on the area, which stays put while the image moves.
        _panning = true;
        _panStart = PointerOnArea(e);
        _panStartX = PanTransform.X;
        _panStartY = PanTransform.Y;
        CanvasArea.CaptureMouse();
        // Over the whole window while it lasts: the image asks for no cursor
        // at all, so it can draw its brush ring instead.
        Mouse.OverrideCursor = Cursors.SizeAll;
        BrushCursor.Visibility = Visibility.Hidden;
        BrushCursorHard.Visibility = Visibility.Hidden;
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

    /// <summary>The window lost the pointer mid-pan - switched away from, or
    /// released outside it where the up never arrived.</summary>
    private void CanvasArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_panning) EndPan();
    }

    /// <summary>Put the view back to 100% centred. Panning has no scrollbars to
    /// hint at where the canvas went, so there has to be a way home.</summary>
    private void ResetView()
    {
        _zoom = 1.0;
        ApplyZoomSize();
        PanTransform.X = PanTransform.Y = 0;
        UpdateZoomDisplay();
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // The middle button is the working area's: left to bubble up to it.
        if (e.ChangedButton != MouseButton.Left || _panning) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        CanvasHost.Focus();

        var (fx, fy) = ScreenToTex(e.GetPosition(CanvasHost));
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
        _dirty = true;
        UpdateStatus();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        // Panning is the working area's, which holds the pointer meanwhile.
        if (_panning) return;

        var p = e.GetPosition(CanvasHost);
        UpdateBrushCursor(p);

        if (_drawing && e.LeftButton == MouseButtonState.Pressed && _strokeBuffer != null && _strokeOriginal != null)
        {
            var (fx, fy) = ScreenToTex(p);
            var ch = _mask.Channel(_activeChannel);
            var rect = BrushEngine.StampLine(
                _strokeBuffer, (float)_lastX, (float)_lastY, (float)fx, (float)fy,
                (float)SizeSlider.Value, (float)HardnessSlider.Value,
                (float)OpacitySlider.Value);
            BrushEngine.ApplyStrokeBuffer(ch, _strokeOriginal, _strokeBuffer,
                rect.x0, rect.y0, rect.x1, rect.y1,
                CapToggle.IsChecked == true, _eraser);
            UpdateLiveAndView(rect.x0, rect.y0, rect.x1, rect.y1);
            _lastX = fx; _lastY = fy;
        }
        else if (_drawing && e.LeftButton != MouseButtonState.Pressed)
        {
            _drawing = false;
            _strokeBuffer = null;
            _strokeOriginal = null;
            CanvasHost.ReleaseMouseCapture();
        }
    }

    private void EndPan()
    {
        // Cleared first: releasing the pointer raises LostMouseCapture, which
        // comes back here.
        _panning = false;
        Mouse.OverrideCursor = null;   // back to the drawn brush ring
        if (CanvasArea.IsMouseCaptured) CanvasArea.ReleaseMouseCapture();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drawing)
        {
            _drawing = false;
            _strokeBuffer = null;
            _strokeOriginal = null;
            CanvasHost.ReleaseMouseCapture();
            UpdateStatus();
        }
    }

    private void Canvas_MouseEnter(object sender, MouseEventArgs e)
    {
        UpdateBrushCursor(e.GetPosition(CanvasHost));
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        BrushCursor.Visibility = Visibility.Hidden;
        BrushCursorHard.Visibility = Visibility.Hidden;
    }

    private void StampPoint(byte[] channel, double fx, double fy)
    {
        if (_strokeBuffer == null || _strokeOriginal == null) return;

        BrushEngine.Stamp(_strokeBuffer,
            (int)MathF.Round((float)fx), (int)MathF.Round((float)fy),
            (float)SizeSlider.Value, (float)HardnessSlider.Value,
            (float)OpacitySlider.Value);

        int rad = (int)MathF.Ceiling((float)SizeSlider.Value) + 1;
        int x0 = (int)fx - rad, y0 = (int)fy - rad;
        int x1 = (int)fx + rad, y1 = (int)fy + rad;
        BrushEngine.ApplyStrokeBuffer(channel, _strokeOriginal, _strokeBuffer,
            x0, y0, x1, y1, CapToggle.IsChecked == true, _eraser);
        UpdateLiveAndView(x0, y0, x1, y1);
    }

    private void UpdateBrushCursor(Point p)
    {
        // One texture pixel is this many canvas pixels — separately per axis,
        // because the canvas takes the reference art's shape. The brush is a
        // circle in TEXTURE space, so on a wide canvas it must be drawn as the
        // ellipse that circle actually covers, or the cursor stops describing
        // what the stroke will do. Zoom is deliberately absent: this is drawn
        // in the canvas's own space and the render transform scales it along
        // with everything else.
        double scaleX = CanvasHost.ActualWidth / MaskBuffer.Size;
        double scaleY = CanvasHost.ActualHeight / MaskBuffer.Size;
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
    }

    private void BrushParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CanvasHost.IsMouseOver)
            UpdateBrushCursor(Mouse.GetPosition(CanvasHost));
    }

    // ───────────────────────── view / live buffer

    private void RecomposeAll() => RecomposeRect(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);

    /// <summary>
    /// Repaint the in-editor canvas for the given inclusive rectangle.
    /// Diffuse is rendered at half intensity as a dim reference; the visible
    /// mask channels are added on top as red / green / blue overlays.
    /// </summary>
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

                // A level mask has one plane and it lives in alpha, so it is
                // drawn as a neutral wash rather than through the R/G/B split —
                // tinting it red would imply a "bounce" channel that this
                // shader has no concept of.
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

                // Show channels at face value so partial-opacity strokes are
                // visible immediately; diffuse fades proportionally to channel
                // coverage. The shader's alpha behavior is reflected in the
                // JigglePreview, not here.
                float r = mr, g = mg, b = mb;
                if (_showDiffuse && _diffuse != null)
                {
                    int di = srcIdx * 4;
                    float da = _diffuse[di + 3] / 255f;
                    float inv = 1f - cov;
                    b += _diffuse[di    ] / 255f * da * 0.5f * inv;
                    g += _diffuse[di + 1] / 255f * da * 0.5f * inv;
                    r += _diffuse[di + 2] / 255f * da * 0.5f * inv;
                }

                buf[dstIdx    ] = (byte)(b * 255f);
                buf[dstIdx + 1] = (byte)(g * 255f);
                buf[dstIdx + 2] = (byte)(r * 255f);
                buf[dstIdx + 3] = 255;
            }
        }
        _viewBitmap.WritePixels(new Int32Rect(x0, y0, w, h), buf, w * 4, 0);
    }

    /// <summary>
    /// Refresh the shared <c>_liveBgra</c> buffer (so the JigglePreview picks
    /// up the change on its next tick) and the in-editor canvas, both
    /// restricted to the supplied dirty rect.
    /// </summary>
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
                _liveBgra[dstIdx    ] = _mask.B[srcIdx];
                _liveBgra[dstIdx + 1] = _mask.G[srcIdx];
                _liveBgra[dstIdx + 2] = _mask.R[srcIdx];
                _liveBgra[dstIdx + 3] = _mask.A[srcIdx];
            }
        }
        _host.LiveMaskRevision++;
        RecomposeRect(x0, y0, x1, y1);
    }

    // ───────────────────────── undo / redo / save

    private void UndoRedo_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Command == ApplicationCommands.Undo) e.CanExecute = _history.CanUndo;
        else if (e.Command == ApplicationCommands.Redo) e.CanExecute = _history.CanRedo;
    }

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_history.Undo(_mask))
        {
            UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
            _dirty = true;
            UpdateStatus();
        }
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_history.Redo(_mask))
        {
            UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
            _dirty = true;
            UpdateStatus();
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) { Save(); }
    private void SaveBtn_Click(object sender, RoutedEventArgs e) { Save(); }
    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private bool Save()
    {
        string relPath = _host.MaskPath;
        // Borrowed from the game, the mask has no file of the pack's to write
        // to: asked for, like one that was never saved.
        if (string.IsNullOrWhiteSpace(relPath) || Shared.GameArt.IsBorrowed(relPath))
        {
            var (startIn, proposed) = SaveDefaults();
            var dialog = new SaveFileDialog
            {
                InitialDirectory = startIn,
                Filter = "PNG (*.png)|*.png",   // English on purpose: a file format's name.
                FileName = proposed,
                Title = Loc.T("maskEditor.save.dialogTitle"),
            };
            if (dialog.ShowDialog(this) != true) return false;
            string abs = dialog.FileName;
            try
            {
                relPath = Path.GetRelativePath(_packRoot, abs).Replace('\\', '/');
            }
            catch
            {
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
            UpdateStatus("saved");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(PromptOwner, ex.Message, Loc.T("save.failed.title"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Where the Save-As dialog opens, and the filename it proposes.
    /// <para/>
    /// Both come from the art being painted over: a mask belongs beside the
    /// sprite it was drawn for, and naming it after that sprite is what makes
    /// a folder of them readable later — AnnaBase.png next to AnnaBaseMask.png,
    /// rather than a pile of files whose names only match by memory. The
    /// author can still put it anywhere inside the pack; this only decides
    /// where the dialog starts.
    /// <para/>
    /// Falls back to the pack root and the host key when there is no pose
    /// sprite to take either from.
    /// </summary>
    private (string Directory, string FileName) SaveDefaults()
    {
        string fallback = $"{_host.Key}_mask.PNG";
        string pose = _host.PoseSpritePath ?? "";
        if (string.IsNullOrWhiteSpace(pose)) return (_packRoot, fallback);

        string abs;
        try { abs = Path.Combine(_packRoot, Normalize(pose)); }
        catch { return (_packRoot, fallback); }   // unusable characters in the path

        string dir = Path.GetDirectoryName(abs) ?? _packRoot;
        // A path typed but not yet drawn has no folder on disk; opening the
        // dialog somewhere that does not exist just drops it at the root with
        // no explanation, so choose the root deliberately instead.
        if (!Directory.Exists(dir)) dir = _packRoot;

        string stem = Path.GetFileNameWithoutExtension(abs);
        return (dir, stem.Length == 0 ? fallback : stem + "Mask.png");
    }

    private void SavePng(string absPath)
    {
        _mask.ToBgra(_liveBgra);

        var bmp = new WriteableBitmap(MaskBuffer.Size, MaskBuffer.Size, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(
            new Int32Rect(0, 0, MaskBuffer.Size, MaskBuffer.Size),
            _liveBgra, MaskBuffer.BgraStride, 0);

        using var stream = File.Create(absPath);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        encoder.Save(stream);
    }

    private void UpdateStatus(string? note = null)
    {
        // XAML's RadioButton.IsChecked="True" fires Checked during
        // InitializeComponent — earlier than StatusText is assigned. Guard it.
        if (StatusText is null) return;
        string chName = _mask.ChannelName(_activeChannel);
        string tool = _eraser ? "Eraser" : "Brush";
        string state = _dirty ? " — unsaved" : "";
        StatusText.Text = $"{tool} → {chName}{state}" + (note != null ? $"  ·  {note}" : "");
    }

    // ───────────────────────── zoom

    private void UpdateZoomDisplay()
    {
        if (ZoomDisplay != null)
            ZoomDisplay.Text = $"{(int)(_zoom * 100)}%";
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        // The cursor in the canvas's OWN coordinates, which a render transform
        // leaves untouched — so this is the texture-space point to pin.
        var p = e.GetPosition(CanvasHost);
        double before = _zoom;

        // Multiplicative, like the Places preview: a fixed increment feels
        // coarse zoomed out and glacial zoomed in, and at 25% steps the
        // cursor-anchoring below is hard to perceive at all.
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.15 : 1.0 / 1.15), ZoomMin, ZoomMax);

        ApplyZoomSize();

        // Scaling happens about the canvas's centre, so a point at distance d
        // from that centre moves by d * (new - old). Cancelling that with the
        // pan keeps whatever is under the cursor exactly where it is; without
        // it, zooming in walks the area of interest off screen and every zoom
        // has to be followed by a hunt.
        double dz = _zoom - before;
        PanTransform.X -= (p.X - CanvasHost.Width / 2.0) * dz;
        PanTransform.Y -= (p.Y - CanvasHost.Height / 2.0) * dz;

        // Recompose the bitmap at the new zoom level.
        RecomposeAll();
        UpdateZoomDisplay();
    }

    /// <summary>
    /// Size the canvas for the current zoom.
    /// <para/>
    /// Sized off <see cref="BaseDisplaySize"/>, NOT the mask resolution: the
    /// canvas is shown at twice the mask's pixels so a 256-px mask is workable,
    /// and multiplying the raw 256 by the zoom instead meant the first scroll
    /// UP shrank the canvas from 512 to 320 while the readout claimed 125%.
    /// </summary>
    private void ApplyZoomSize()
    {
        // The LAYOUT size never changes with zoom — see the transform block in
        // the XAML for why. It only tracks the reference art's shape, fitted
        // inside the square so a wide reference gets shorter rather than wider.
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
    }
}
