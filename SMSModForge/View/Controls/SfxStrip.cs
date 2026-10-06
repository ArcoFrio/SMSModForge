using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.ViewModel;

namespace SMSModForge.View.Controls;

/// <summary>
/// The SFX tab's strip (the author, 1.7.0): a sound drawn like an Audacity
/// clip, one channel tall, with a ruler of seconds above it.
/// <list type="bullet">
///   <item>The mouse wheel zooms in and out around the pointer, without Ctrl,
///   as asked; Shift and the wheel scroll sideways.</item>
///   <item>A click picks the spot - a red line - and highlights the piece it
///   is in.</item>
///   <item>Each piece is a strip of its own, with a gap between, so a cut
///   reads as a cut; the highlighted one is tinted.</item>
///   <item>While the sound plays, a green line follows it.</item>
/// </list>
/// Everything it shows and changes is the editor's
/// (<see cref="SfxEditorViewModel"/>); this only draws it and turns the mouse
/// into times.
/// </summary>
public sealed class SfxStrip : FrameworkElement
{
    public static readonly DependencyProperty EditorProperty =
        DependencyProperty.Register(nameof(Editor), typeof(SfxEditorViewModel), typeof(SfxStrip),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnEditorChanged));

    public SfxEditorViewModel? Editor
    {
        get => (SfxEditorViewModel?)GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private const double RulerHeight = 18;
    private const double HeaderHeight = 6;
    private const double Gap = 2;

    private static readonly Brush Back = Frozen(new SolidColorBrush(Color.FromRgb(0xE3, 0xE3, 0xE3)));
    private static readonly Brush RulerBack = Frozen(new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF6)));
    private static readonly Brush PieceFill = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Brush PieceFillLit = Frozen(new SolidColorBrush(Color.FromRgb(0xDC, 0xE7, 0xFA)));
    private static readonly Brush Header = Frozen(new SolidColorBrush(Color.FromRgb(0xC2, 0xC8, 0xD2)));
    private static readonly Brush HeaderLit = Frozen(new SolidColorBrush(Color.FromRgb(0x5E, 0x8C, 0xD8)));
    private static readonly Pen PieceEdge = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1));
    private static readonly Pen PieceEdgeLit = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xD6)), 1.5));
    private static readonly Pen Wave = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0x55, 0xB4)), 1));
    private static readonly Pen WaveLit = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x24, 0x3E, 0xA0)), 1));
    private static readonly Pen Middle = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0x3C, 0x55, 0xB4)), 1));
    private static readonly Pen Tick = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 1));
    private static readonly Pen CursorLine = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0x20, 0x20)), 1.5));
    private static readonly Brush CursorMark = Frozen(new SolidColorBrush(Color.FromRgb(0xD0, 0x20, 0x20)));
    private static readonly Pen PlayheadLine = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x4A)), 1.5));
    private static readonly Brush Label = Frozen(new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x50)));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Face = new("Segoe UI");   // English on purpose: a font's name.

    private readonly DispatcherTimer _playhead;

    public SfxStrip()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        _playhead = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _playhead.Tick += (_, _) =>
        {
            InvalidateVisual();
            var now = DateTime.UtcNow;
            if (Editor?.Playhead != null) _lastSeen = now;
            // Stopped, or played to the end: a moment's grace first, since the
            // output takes a little while to start.
            else if (now - _started > TimeSpan.FromSeconds(0.5) && now - _lastSeen > TimeSpan.FromSeconds(0.2))
                _playhead.Stop();
        };
        Unloaded += (_, _) => _playhead.Stop();
    }

    private DateTime _started, _lastSeen;

    private static void OnEditorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var strip = (SfxStrip)d;
        if (e.OldValue is SfxEditorViewModel old)
        {
            old.Redraw -= strip.InvalidateVisual;
            old.Started -= strip.FollowPlaying;
        }
        if (e.NewValue is SfxEditorViewModel now)
        {
            now.Redraw += strip.InvalidateVisual;
            now.Started += strip.FollowPlaying;
        }
    }

    /// <summary>Start following the playing - the editor has just been asked
    /// to play.</summary>
    public void FollowPlaying()
    {
        _started = _lastSeen = DateTime.UtcNow;
        _playhead.Start();
    }

    // ── Times and places ─────────────────────────────────────────────────

    private double PixelsPerSecond
        => Editor is { VisibleSeconds: > 0 } ed && ActualWidth > 0 ? ActualWidth / ed.VisibleSeconds : 0;

    /// <summary>Where a time on the strip is drawn.</summary>
    public double XOf(double time) => Editor == null ? 0 : (time - Editor.ViewStart) * PixelsPerSecond;

    /// <summary>The time at a point across the strip.</summary>
    public double TimeAt(double x)
        => Editor == null || PixelsPerSecond <= 0 ? 0 : Editor.ViewStart + x / PixelsPerSecond;

    // ── Mouse and keys ───────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Editor?.Current == null) return;
        Editor.Cursor = TimeAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // Handled whatever happens: a wheel over the strip zooms it, and must
        // not also scroll the page it is on.
        e.Handled = true;
        if (Editor?.Current == null) return;
        double notches = e.Delta / 120.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Editor.ViewStart -= notches * Editor.VisibleSeconds * 0.15;
            return;
        }
        Editor.ZoomAround(Editor.Zoom * Math.Pow(1.25, notches), TimeAt(e.GetPosition(this).X));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Editor == null) return;
        if (e.Key == Key.Delete && Editor.DeleteCommand.CanExecute(null))
        {
            Editor.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Drawing ──────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(RulerBack, null, new Rect(0, 0, w, RulerHeight));

        var ed = Editor;
        if (ed?.Current == null)
        {
            if (ed?.HasStatus == true) DrawCentred(dc, ed.Status, w, h);
            return;
        }
        double pps = PixelsPerSecond;
        if (pps <= 0) return;

        DrawRuler(dc, ed, pps, w);

        double top = RulerHeight + 3, bottom = h - 3;
        var pieces = ed.Pieces;
        double start = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            double x0 = XOf(start), x1 = XOf(start + piece.Length);
            start += piece.Length;
            if (x1 < 0 || x0 > w) continue;

            bool lit = i == ed.Highlighted;
            var box = new Rect(x0 + Gap / 2, top, Math.Max(1, x1 - x0 - Gap), bottom - top);
            dc.DrawRectangle(lit ? PieceFillLit : PieceFill, lit ? PieceEdgeLit : PieceEdge, box);
            dc.DrawRectangle(lit ? HeaderLit : Header, null, new Rect(box.X, box.Y, box.Width, HeaderHeight));

            double waveTop = box.Y + HeaderHeight + 2, waveBottom = box.Bottom - 2;
            double mid = (waveTop + waveBottom) / 2, half = (waveBottom - waveTop) / 2;
            dc.DrawLine(Middle, new Point(box.Left, mid), new Point(box.Right, mid));

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                double from = Math.Max(Math.Ceiling(box.Left), 0), to = Math.Min(Math.Floor(box.Right), w);
                double pieceStart = start - piece.Length;
                for (double px = from; px < to; px++)
                {
                    double t = TimeAt(px) - pieceStart;
                    double s0 = piece.From + t, s1 = s0 + 1 / pps;
                    var (lo, hi) = ed.Current.Peaks.Range(s0, s1);
                    double yHi = mid - Math.Clamp(hi, -1, 1) * half, yLo = mid - Math.Clamp(lo, -1, 1) * half;
                    if (yLo - yHi < 1) yLo = yHi + 1;
                    g.BeginFigure(new Point(px + 0.5, yHi), false, false);
                    g.LineTo(new Point(px + 0.5, yLo), true, false);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, lit ? WaveLit : Wave, geometry);
        }

        // The spot picked.
        double cx = XOf(ed.Cursor);
        if (cx >= -1 && cx <= w + 1)
        {
            dc.DrawLine(CursorLine, new Point(cx, RulerHeight), new Point(cx, h));
            var mark = new StreamGeometry();
            using (var g = mark.Open())
            {
                g.BeginFigure(new Point(cx - 5, RulerHeight - 7), true, true);
                g.LineTo(new Point(cx + 5, RulerHeight - 7), true, false);
                g.LineTo(new Point(cx, RulerHeight), true, false);
            }
            mark.Freeze();
            dc.DrawGeometry(CursorMark, null, mark);
        }

        // Where the playing has got to.
        if (ed.Playhead is { } at)
        {
            double px = XOf(at);
            if (px >= 0 && px <= w) dc.DrawLine(PlayheadLine, new Point(px, RulerHeight), new Point(px, h));
        }
    }

    private static readonly double[] Steps =
        { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 30, 60 };

    private void DrawRuler(DrawingContext dc, SfxEditorViewModel ed, double pps, double w)
    {
        double step = Steps[^1];
        foreach (double s in Steps)
            if (s * pps >= 70) { step = s; break; }
        double minor = step / 5;
        int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step)));
        string format = decimals == 0 ? "0" : "0." + new string('0', decimals);

        double first = Math.Floor(ed.ViewStart / minor) * minor;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (double t = first; t <= ed.ViewStart + ed.VisibleSeconds + minor; t += minor)
        {
            double x = XOf(t);
            if (x < -1 || x > w + 1) continue;
            bool major = Math.Abs(t / step - Math.Round(t / step)) < 1e-6;
            dc.DrawLine(Tick, new Point(x, RulerHeight - (major ? 7 : 3)), new Point(x, RulerHeight));
            if (!major) continue;
            var text = new FormattedText(Math.Max(0, t).ToString(format, CultureInfo.CurrentCulture) + " s",
                CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face, 10, Label, dpi);
            dc.DrawText(text, new Point(x + 3, 1));
        }
    }

    private void DrawCentred(DrawingContext dc, string message, double w, double h)
    {
        var text = new FormattedText(message, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Face, 12, Label, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(10, w - 20), TextAlignment = TextAlignment.Center };
        dc.DrawText(text, new Point(10, Math.Max(RulerHeight + 4, (h - text.Height) / 2)));
    }
}
