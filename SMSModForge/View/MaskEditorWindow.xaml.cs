using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SMSModForge.Rendering;
using SMSModForge.ViewModel;

namespace SMSModForge.View;

/// <summary>
/// Per-outfit mask painter. Holds three independent 8-bit channels (R/G/B)
/// in a <see cref="MaskBuffer"/>, composites them with the diffuse reference
/// for the in-editor canvas, and republishes a BGRA snapshot through
/// <see cref="OutfitViewModel.LiveMaskBgra"/> so the main JigglePreview
/// renders strokes live without touching disk. Save is explicit (Ctrl+S).
/// </summary>
public partial class MaskEditorWindow : Window
{
    private readonly OutfitViewModel _outfit;
    private readonly string _packRoot;
    private readonly MaskBuffer _mask = new();
    private readonly MaskHistory _history = new();
    private readonly WriteableBitmap _viewBitmap;
    private readonly byte[] _liveBgra = new byte[MaskBuffer.Size * MaskBuffer.Size * 4];
    private byte[]? _diffuse;

    // Layer / tool state
    private bool _showR = true, _showG = true, _showB = true, _showDiffuse = true;
    private int _activeChannel;          // 0 = R, 1 = G, 2 = B
    private bool _eraser;

    // Stroke state
    private bool _drawing;
    private bool _dirty;
    private double _lastX, _lastY;       // last stamp in texture-space pixels
    private float[]? _strokeBuffer;      // per-stroke max-contribution buffer
    private byte[]? _strokeOriginal;     // channel snapshot at stroke start

    public MaskEditorWindow(OutfitViewModel outfit, string packRoot)
    {
        InitializeComponent();
        _outfit = outfit;
        _packRoot = packRoot;
        _viewBitmap = new WriteableBitmap(MaskBuffer.Size, MaskBuffer.Size, 96, 96, PixelFormats.Bgra32, null);
    }

    // ───────────────────────── lifecycle

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        MaskImage.Source = _viewBitmap;

        var basePath = Path.Combine(_packRoot, Normalize(_outfit.Model.BaseSprite));
        var maskPath = Path.Combine(_packRoot, Normalize(_outfit.Model.MaskSprite));
        if (File.Exists(basePath))
            _diffuse = BustComposer.LoadPng(basePath);
        if (!string.IsNullOrWhiteSpace(_outfit.Model.MaskSprite) && File.Exists(maskPath))
            _mask.FromBgra(BustComposer.LoadPng(maskPath));

        // Publish the live BGRA buffer so JigglePreview switches over to it.
        _mask.ToBgra(_liveBgra);
        _outfit.LiveMaskBgra = _liveBgra;
        _outfit.LiveMaskRevision++;

        RecomposeRect(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        UpdateStatus();
        CanvasHost.Focus();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty)
        {
            var r = MessageBox.Show(this,
                "You have unsaved mask changes. Save before closing?",
                "Mask Editor",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; return; }
            if (r == MessageBoxResult.Yes && !Save()) { e.Cancel = true; return; }
        }
        // Hand the preview back to the file-loaded mask.
        _outfit.LiveMaskBgra = null;
        _outfit.LiveMaskRevision++;
    }

    private static string Normalize(string p) => p?.Replace('/', Path.DirectorySeparatorChar) ?? "";

    // ───────────────────────── keyboard

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't steal keys away from a focused TextBox / slider value editor.
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.B: SetTool(eraser: false); e.Handled = true; break;
            case Key.E: SetTool(eraser: true);  e.Handled = true; break;
            case Key.D1: case Key.NumPad1: ActiveR.IsChecked = true; e.Handled = true; break;
            case Key.D2: case Key.NumPad2: ActiveG.IsChecked = true; e.Handled = true; break;
            case Key.D3: case Key.NumPad3: ActiveB.IsChecked = true; e.Handled = true; break;
            case Key.OemOpenBrackets:
                SizeSlider.Value = Math.Max(SizeSlider.Minimum, SizeSlider.Value - 2); e.Handled = true; break;
            case Key.OemCloseBrackets:
                SizeSlider.Value = Math.Min(SizeSlider.Maximum, SizeSlider.Value + 2); e.Handled = true; break;
        }
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

    private void ClearChannel_Click(object sender, RoutedEventArgs e)
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        _mask.ClearChannel(_activeChannel);
        UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        _dirty = true; UpdateStatus();
    }

    private void FillChannel_Click(object sender, RoutedEventArgs e)
    {
        var ch = _mask.Channel(_activeChannel);
        _history.Snapshot(_activeChannel, ch, _mask.A);
        Array.Fill(ch, (byte)255);
        UpdateLiveAndView(0, 0, MaskBuffer.Size - 1, MaskBuffer.Size - 1);
        _dirty = true; UpdateStatus();
    }

    // ───────────────────────── drawing

    private (double fx, double fy) ScreenToTex(Point p)
    {
        double sx = CanvasHost.ActualWidth, sy = CanvasHost.ActualHeight;
        return (p.X / sx * MaskBuffer.Size, p.Y / sy * MaskBuffer.Size);
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
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
        double scale = CanvasHost.ActualWidth / MaskBuffer.Size;
        double radius = SizeSlider.Value * scale;
        double hardR = radius * HardnessSlider.Value;

        BrushCursor.Width = BrushCursor.Height = radius * 2;
        Canvas.SetLeft(BrushCursor, p.X - radius);
        Canvas.SetTop(BrushCursor, p.Y - radius);
        BrushCursor.Visibility = Visibility.Visible;

        if (HardnessSlider.Value < 0.995)
        {
            BrushCursorHard.Width = BrushCursorHard.Height = hardR * 2;
            Canvas.SetLeft(BrushCursorHard, p.X - hardR);
            Canvas.SetTop(BrushCursorHard, p.Y - hardR);
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

                float mr = _showR ? _mask.R[srcIdx] / 255f : 0;
                float mg = _showG ? _mask.G[srcIdx] / 255f : 0;
                float mb = _showB ? _mask.B[srcIdx] / 255f : 0;
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
        _outfit.LiveMaskRevision++;
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
        string relPath = _outfit.Model.MaskSprite;
        if (string.IsNullOrWhiteSpace(relPath))
        {
            var dialog = new SaveFileDialog
            {
                InitialDirectory = _packRoot,
                Filter = "PNG (*.png)|*.png",
                FileName = $"{_outfit.Model.Key}_mask.PNG",
                Title = "Save mask PNG",
            };
            if (dialog.ShowDialog(this) != true) return false;
            string abs = dialog.FileName;
            try
            {
                relPath = Path.GetRelativePath(_packRoot, abs).Replace('\\', '/');
            }
            catch
            {
                MessageBox.Show(this, "Mask must be saved inside the pack folder.",
                                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            _outfit.MaskSprite = relPath;
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
            MessageBox.Show(this, ex.Message, "Save failed",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
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
        string chName = _activeChannel switch { 0 => "Bounce", 1 => "Sway", _ => "Wave" };
        string tool = _eraser ? "Eraser" : "Brush";
        string state = _dirty ? " — unsaved" : "";
        StatusText.Text = $"{tool} → {chName}{state}" + (note != null ? $"  ·  {note}" : "");
    }
}
