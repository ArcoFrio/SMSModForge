using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.Localization;

namespace SMSModForge.View.Controls;

/// <summary>
/// The frame rate a live preview is drawing at, in its top left corner, while
/// Options ▸ Show preview frame rate is on (1.9.1, the author: "a toggle to
/// show preview FPS, that defaults to false"). Counted from the pictures the
/// preview really put on screen in the last half second - what the author
/// sees, whatever the quality or the renderer asked for.
/// </summary>
internal sealed class FpsBadge : Adorner
{
    private static readonly List<WeakReference<Action>> Syncs = new();

    /// <summary>Whether the badges are shown; remembered between sessions.</summary>
    public static bool Shown
    {
        get => Services.EditorPrefs.ShowPreviewFps;
        set
        {
            Services.EditorPrefs.ShowPreviewFps = value;
            lock (Syncs)
            {
                Syncs.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Syncs) if (w.TryGetTarget(out var sync)) sync();
            }
        }
    }

    /// <summary>Give <paramref name="preview"/> a badge whenever badges are
    /// shown and it is on screen; <paramref name="frames"/> counts the
    /// pictures it has put there.</summary>
    public static void Attach(FrameworkElement preview, Func<int> frames)
    {
        FpsBadge? badge = null;
        void Sync()
        {
            if (!preview.Dispatcher.CheckAccess()) { preview.Dispatcher.BeginInvoke((Action)Sync); return; }
            bool want = Shown && preview.IsLoaded && preview.IsVisible;
            if (want && badge == null)
            {
                var layer = AdornerLayer.GetAdornerLayer(preview);
                if (layer == null) return;
                badge = new FpsBadge(preview, frames);
                layer.Add(badge);
            }
            else if (!want && badge != null)
            {
                badge.Stop();
                AdornerLayer.GetAdornerLayer(preview)?.Remove(badge);
                badge = null;
            }
        }
        preview.Loaded += (_, _) => Sync();
        preview.Unloaded += (_, _) => Sync();
        preview.IsVisibleChanged += (_, _) => Sync();
        // Held weakly by the switch, strongly by the preview: a preview gone takes it along.
        Action sync = Sync;
        Keep(preview, sync);
        lock (Syncs) Syncs.Add(new WeakReference<Action>(sync));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, List<Action>> Kept = new();

    private static void Keep(FrameworkElement preview, Action sync) => Kept.GetOrCreateValue(preview).Add(sync);

    private readonly Func<int> _frames;
    private readonly DispatcherTimer _timer;
    private int _last;
    private long _lastAt;
    private double _rate = double.NaN;

    /// <summary>The rate shown now - for tests.</summary>
    internal double Rate => _rate;

    private FpsBadge(UIElement preview, Func<int> frames) : base(preview)
    {
        _frames = frames;
        IsHitTestVisible = false;
        _last = frames();
        _lastAt = System.Diagnostics.Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Count();
        _timer.Start();
    }

    private void Stop() => _timer.Stop();

    private void Count()
    {
        int now = _frames();
        long at = System.Diagnostics.Stopwatch.GetTimestamp();
        double seconds = (at - _lastAt) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (seconds > 0) _rate = (now - _last) / seconds;
        _last = now; _lastAt = at;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (double.IsNaN(_rate)) return;
        string text = Loc.F("preview.fps", "rate", Math.Round(_rate).ToString(CultureInfo.CurrentCulture));
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var shape = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                                      new Typeface("Segoe UI"), 11, Brushes.White, dpi);   // English on purpose: a font family name, not words
        // Its own size whatever the preview is scaled to.
        var layer = VisualTreeHelper.GetParent(this) as Visual;
        var scale = layer != null ? AdornedElement.TransformToVisual(layer).TransformBounds(new Rect(0, 0, 1, 1)) : new Rect(0, 0, 1, 1);
        double sx = scale.Width > 0 ? 1 / scale.Width : 1, sy = scale.Height > 0 ? 1 / scale.Height : 1;
        dc.PushTransform(new ScaleTransform(sx, sy));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), null,
                                new Rect(4, 4, shape.Width + 8, shape.Height + 2), 3, 3);
        dc.DrawText(shape, new Point(8, 5));
        dc.Pop();
    }
}
