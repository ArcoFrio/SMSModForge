using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
///   <item>While the sound plays, a green line follows it - every frame, from
///   what the speakers have played, and it stays a moment where the sound
///   ended.</item>
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

    // The strip's own colours come from the theme (1.8.0): it was drawn light
    // grey and white whatever the theme, a bright box in every dark one. Read
    // when drawing, and drawn again when the theme changes.
    private Brush Back => Ink(Services.ThemeManager.KeySurfaceAlt);
    private Brush RulerBack => Ink(Services.ThemeManager.KeySurface);
    private Brush PieceFill => Ink(Services.ThemeManager.KeyControl);
    private Brush PieceFillLit => Ink(Services.ThemeManager.KeyAccentSoft);
    private Brush Header => Ink(Services.ThemeManager.KeyBorder);
    private Brush HeaderLit => Ink(Services.ThemeManager.KeyAccent);
    private Pen PieceEdge => Line(Services.ThemeManager.KeyControlBorder, 1);
    private Pen PieceEdgeLit => Line(Services.ThemeManager.KeyAccent, 1.5);
    private Pen Wave => Line(Services.ThemeManager.KeyLink, 1);
    private Pen WaveLit => Line(Services.ThemeManager.KeyAccent, 1);
    private Pen Middle => Line(Services.ThemeManager.KeyControlBorder, 1, 0.45);
    private Pen Tick => Line(Services.ThemeManager.KeyTextMuted, 1);
    private Brush Label => Ink(Services.ThemeManager.KeyTextMuted);
    private Brush StatusInk => Ink(Services.ThemeManager.KeyText);

    private Brush Ink(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

    private Pen Line(string key, double thickness, double opacity = 1)
    {
        Brush brush = Ink(key);
        if (opacity < 1)
        {
            brush = brush.Clone();
            brush.Opacity = opacity;
            brush.Freeze();
        }
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static readonly Pen CursorLine = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0x20, 0x20)), 1.5));
    private static readonly Brush CursorMark = Frozen(new SolidColorBrush(Color.FromRgb(0xD0, 0x20, 0x20)));
    private static readonly Pen PlayheadLine = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x9E, 0x4A)), 1.5));

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Typeface Face = new("Segoe UI");   // English on purpose: a font's name.

    /// <summary>
    /// The playing line, on a layer of its own over the strip.
    /// <para/>
    /// Moved every frame the screen draws (CompositionTarget.Rendering), where
    /// it used to be a timer thirty times a second that redrew the whole
    /// waveform each time to move one line - which looked snappy next to
    /// Audacity's (the author, 2026-10-21). Only this layer is drawn again now.
    /// </summary>
    private readonly DrawingVisual _line = new();
    private bool _following;
    private double? _lineAt;

    /// <summary>How long the output may take to start before "not playing"
    /// means it has finished.</summary>
    private static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(0.5);

    /// <summary>How long the line stays where the sound ended, so the end of
    /// a short one can be seen.</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(0.6);

    public SfxStrip()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        AddVisualChild(_line);
        Unloaded += (_, _) =>
        {
            Unfollow(clear: true);
            Services.ThemeManager.Changed -= InvalidateVisual;
        };
        Loaded += (_, _) =>
        {
            Services.ThemeManager.Changed -= InvalidateVisual;
            Services.ThemeManager.Changed += InvalidateVisual;
        };
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
        => index == 0 ? _line : throw new ArgumentOutOfRangeException(nameof(index));

    private DateTime _started, _endedAt;

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
        _started = DateTime.UtcNow;
        _endedAt = DateTime.MinValue;
        if (_following) return;
        _following = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e) => Step(DateTime.UtcNow);

    /// <summary>One frame of following: the line where the playing is, held
    /// where it ended for a moment once it has, then gone.</summary>
    internal void Step(DateTime now)
    {
        if (Editor?.Playhead is { } at)
        {
            _endedAt = DateTime.MinValue;
            DrawLine(at);
            return;
        }
        if (now - _started < StartGrace) return;
        if (_endedAt == DateTime.MinValue) _endedAt = now;
        if (now - _endedAt < Hold) return;
        Unfollow(clear: true);
    }

    private void Unfollow(bool clear)
    {
        if (_following) CompositionTarget.Rendering -= OnFrame;
        _following = false;
        if (clear) DrawLine(null);
    }

    /// <summary>Where the line is drawn now, in seconds; null when it is not.</summary>
    internal double? LineAt => _lineAt;

    private void DrawLine(double? at)
    {
        _lineAt = at;
        using var dc = _line.RenderOpen();
        if (at is not { } t || Editor == null) return;
        double px = XOf(t);
        if (px >= 0 && px <= ActualWidth) dc.DrawLine(PlayheadLine, new Point(px, RulerHeight), new Point(px, ActualHeight));
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

        // The playing line is a layer of its own (DrawLine); kept where it is
        // when the waveform under it is drawn again - zoomed, scrolled.
        if (_lineAt is { } at) DrawLine(at);
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
            Face, 12, StatusInk, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(10, w - 20), TextAlignment = TextAlignment.Center };
        dc.DrawText(text, new Point(10, Math.Max(RulerHeight + 4, (h - text.Height) / 2)));
    }
}
