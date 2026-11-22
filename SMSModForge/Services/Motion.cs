using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMSModForge.Services;

/// <summary>
/// The editor's small movements (1.10.0, the author: "Small performance-friendly animations should be in order for
/// the UI to look more modern and clean, where they fit ... nice, quick, and non-intrusive, and look different
/// according to the theme ... an animation for switching between tabs").
/// <para/>
/// A page that comes in fades in - in the Normal looks only that, quick; in EX a little longer, rising a few
/// pixels as it does. Opacity and a render transform only: what the composition already does every frame, no
/// layout, nothing measured again. Gone in a fraction of a second, and nothing waits for it: a click or a key in
/// the middle lands on the page as it will be.
/// <para/>
/// Off when Windows' own "Animation effects" are off (Settings ▸ Accessibility ▸ Visual effects), off when the
/// author turns them off (Options ▸ Animations), and off under the test harness, where a page half faded in would
/// be measured as it was half way.
/// </summary>
public static class Motion
{
    /// <summary>For tests of the movements themselves: on under the harness too.</summary>
    internal static bool ForTests;

    public static bool Enabled
        => (ForTests || !TestMode.Active) && EditorPrefs.Animations && SystemParameters.ClientAreaAnimation;

    /// <summary>How long a page takes to come in, for the look in use.</summary>
    public static TimeSpan Duration => ThemeManager.Current.IsEx ? TimeSpan.FromMilliseconds(170) : TimeSpan.FromMilliseconds(120);

    /// <summary>How far a page rises as it comes in, in EX; none in the Normal looks.</summary>
    public static double Rise => ThemeManager.Current.IsEx ? 10 : 0;

    /// <summary>Bring <paramref name="page"/> in: from clear to as it is, and (EX) from a few pixels lower.
    /// Nothing when the movements are off.</summary>
    public static void Enter(UIElement? page)
    {
        if (page == null || !Enabled) return;
        var duration = new Duration(Duration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        page.BeginAnimation(UIElement.OpacityProperty, fade);
        double rise = Rise;
        if (rise <= 0) return;
        // A transform of its own for the rise, put back when it is done: the page's own (if any) is left as it was.
        var before = page.RenderTransform;
        var shift = new TranslateTransform(0, rise);
        page.RenderTransform = before is { } kept && !ReferenceEquals(kept, Transform.Identity)
            ? new TransformGroup { Children = { kept, shift } }
            : shift;
        var up = new DoubleAnimation(rise, 0, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        up.Completed += (_, _) =>
        {
            if (page.RenderTransform is TranslateTransform t && ReferenceEquals(t, shift)) page.RenderTransform = before;
            else if (page.RenderTransform is TransformGroup g && g.Children.Contains(shift)) page.RenderTransform = before;
        };
        shift.BeginAnimation(TranslateTransform.YProperty, up);
    }

    /// <summary>
    /// One panel slides in sideways as the one it replaces slides out the other way - the mask studio over the tabs
    /// and back (1.10.0, the author: "animations for the mask editors ... when opening them, with them sliding
    /// horizontally into view and out, different from the individual tabs"). <paramref name="fromRight"/>: the
    /// incoming one comes from the right (opening); otherwise from the left (going back).
    /// <para/>
    /// Classic slides a third of the way and fades, quick; EX pushes the whole width across, the one going out
    /// leaving by the other edge, a little longer and decelerating hard. The one going out takes no clicks while it
    /// goes, and <paramref name="whenOut"/> runs once it is gone - at once when the movements are off, so what the
    /// caller hides is hidden exactly as before. Their shared parent clips while they move, so neither slides over
    /// its neighbours; only then, so nothing at its edges (a glow) is cut off the rest of the time.
    /// </summary>
    public static void Slide(FrameworkElement incoming, FrameworkElement? outgoing, bool fromRight, Action? whenOut)
    {
        if (!Enabled)
        {
            whenOut?.Invoke();
            return;
        }
        bool ex = ThemeManager.Current.IsEx;
        var parent = VisualTreeHelper.GetParent(incoming) as FrameworkElement;
        double width = parent?.ActualWidth is > 0 and var w ? w : 900;
        double travel = ex ? width : width / 3;
        double sign = fromRight ? 1 : -1;
        var duration = new Duration(TimeSpan.FromMilliseconds((ex ? 300 : 210) * View.Motions.SlowForTests));
        IEasingFunction ease = ex ? new QuinticEase { EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut };

        // Each one's own parent clips: the two need not share one (the studio stands beside the pack's area, the tabs
        // inside it), and either sliding unclipped would cross what is docked beside it (a tutorial's coach).
        Action? unclip = parent != null ? Clip(parent) : null;
        if (outgoing != null && VisualTreeHelper.GetParent(outgoing) is FrameworkElement other && !ReferenceEquals(other, parent))
            unclip = Clip(other) is { } second ? unclip + second : unclip;
        int left = outgoing == null ? 1 : 2;
        void OneDone() { if (--left == 0) unclip?.Invoke(); }

        Move(incoming, sign * travel, 0, duration, ease, ex ? null : (0.0, 1.0), takesClicks: true, OneDone);
        if (outgoing == null) { whenOut?.Invoke(); return; }
        Move(outgoing, 0, -sign * travel, duration, ease, ex ? null : (1.0, 0.0), takesClicks: false, () =>
        {
            whenOut?.Invoke();
            OneDone();
        });
    }

    /// <summary>What a panel was before it started moving, kept from its first move until its last one ends: a move
    /// begun while another is under way must not take the first one's half-way place for the panel's own.</summary>
    private sealed class Rest
    {
        public Transform? Transform;
        public bool HitTest;
        public TranslateTransform? Shift;
    }

    private static readonly System.Collections.Generic.Dictionary<FrameworkElement, Rest> Resting = new();
    private static readonly System.Collections.Generic.Dictionary<FrameworkElement, int> Clipping = new();

    /// <summary>Clip the panels' parent while they move; the returned action lets go (the last one out unclips).
    /// A parent that clips by itself is left alone.</summary>
    private static Action? Clip(FrameworkElement parent)
    {
        if (Clipping.TryGetValue(parent, out int n)) Clipping[parent] = n + 1;
        else if (parent.ClipToBounds) return null;
        else { Clipping[parent] = 1; parent.ClipToBounds = true; }
        return () =>
        {
            if (!Clipping.TryGetValue(parent, out int k)) return;
            if (k > 1) { Clipping[parent] = k - 1; return; }
            Clipping.Remove(parent);
            parent.ClipToBounds = false;
        };
    }

    /// <summary>A sideways move on a transform of the panel's own, put back when it ends (and the fade with it).</summary>
    private static void Move(FrameworkElement panel, double from, double to, Duration duration, IEasingFunction ease,
                             (double From, double To)? fade, bool takesClicks, Action done)
    {
        if (!Resting.TryGetValue(panel, out var rest))
            Resting[panel] = rest = new Rest { Transform = panel.RenderTransform, HitTest = panel.IsHitTestVisible };
        var shift = new TranslateTransform(from, 0);
        rest.Shift = shift;
        panel.RenderTransform = shift;
        panel.IsHitTestVisible = takesClicks && rest.HitTest;
        var slide = new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        slide.Completed += (_, _) =>
        {
            // Only the latest move puts the panel back; an earlier one overtaken by it just reports in.
            if (ReferenceEquals(rest.Shift, shift))
            {
                panel.RenderTransform = rest.Transform;
                panel.IsHitTestVisible = rest.HitTest;
                Resting.Remove(panel);
            }
            done();
        };
        if (fade is { } f)
            panel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(f.From, f.To, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        else
            panel.BeginAnimation(UIElement.OpacityProperty, null);
        shift.BeginAnimation(TranslateTransform.XProperty, slide);
    }
}
