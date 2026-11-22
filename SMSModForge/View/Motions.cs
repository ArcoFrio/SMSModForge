using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SMSModForge.Services;

namespace SMSModForge.View;

/// <summary>
/// The controls' own small movements (1.10.0, the author: "do a full pass on the app to look for everywhere else we
/// could add small and subtle performance-friendly animations, to make the UI feel more modern ... the animations
/// should not be the same between Classic and EX").
/// <para/>
/// Each is set in a template, on the part that moves, bound to what moves it: a button's press, a tree item's
/// expansion, a tab's selection, a tick, a count. Opacity and render transforms only - nothing laid out again,
/// nothing that waits: a click in the middle of one lands at once. Classic is quick and plain (ease out, short,
/// fades); EX a little longer, with an overshoot where something springs, a slide where something appears and its
/// glow coming up. All of it off with <see cref="Motion.Enabled"/> (Windows' animation effects, Options ▸
/// Animations, the test harness), where every part simply takes its end state.
/// <para/>
/// What a person did moves; what the editor did does not: a tick, an arrow or a fold changes on a row a list
/// recycled as it scrolled too, and a list full of ticks popping as it scrolls is noise. Those move only on the
/// control under the pointer or with the keyboard (<see cref="Acted"/>).
/// </summary>
public static class Motions
{
    private static bool Ex => ThemeManager.Current.IsEx;
    private static bool Moving(DependencyObject d) => Motion.Enabled && d is FrameworkElement { IsLoaded: true };
    private static Duration Ms(double classic, double ex) => new(TimeSpan.FromMilliseconds((Ex ? ex : classic) * SlowForTests));

    /// <summary>Every movement this many times longer: a test reads what is drawn part way through one rather than
    /// racing a tenth of a second against a frame that may take as long.</summary>
    internal static double SlowForTests = 1;
    private static IEasingFunction Out => new CubicEase { EasingMode = EasingMode.EaseOut };
    private static IEasingFunction Spring => Ex ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } : Out;

    /// <summary>Whether the control this part is drawn for is the one being used: under the pointer, or holding the
    /// keyboard (the control, or the one it is part of - a tree item's arrow is part of the tree item).</summary>
    internal static bool Acted(DependencyObject d)
    {
        var owner = (d as FrameworkElement)?.TemplatedParent as UIElement;
        if (owner == null) return d is UIElement { IsMouseOver: true } || d is UIElement { IsKeyboardFocusWithin: true };
        if (owner.IsMouseOver || owner.IsKeyboardFocusWithin) return true;
        return (owner as FrameworkElement)?.TemplatedParent is UIElement { IsKeyboardFocused: true };
    }

    /// <summary>The part's own scale to move, made the first time; null where the part already turns some other
    /// way (a transform somebody else set), which is left alone.</summary>
    private static ScaleTransform? ScaleOf(UIElement el)
    {
        switch (el.RenderTransform)
        {
            case ScaleTransform { IsFrozen: false } s: return s;
            case null:
            case MatrixTransform { Matrix.IsIdentity: true }:
                var made = new ScaleTransform(1, 1);
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = made;
                return made;
            default: return null;
        }
    }

    private static void Scale(ScaleTransform scale, AnimationTimeline? a)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Comes in from clear; EX also slides down into place from a few pixels up.</summary>
    private static void FadeSlide(UIElement el, double classicMs, double exMs, double slide)
    {
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(classicMs, exMs)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        if (!Ex || slide == 0) return;
        var before = el.RenderTransform;
        if (before != null && before is not MatrixTransform { Matrix.IsIdentity: true }) return;
        var shift = new TranslateTransform(0, -slide);
        el.RenderTransform = shift;
        var a = new DoubleAnimation(-slide, 0, Ms(classicMs, exMs)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop };
        a.Completed += (_, _) => { if (ReferenceEquals(el.RenderTransform, shift)) el.RenderTransform = before; };
        shift.BeginAnimation(TranslateTransform.YProperty, a);
    }

    // ── Press: a button gives a little under the pointer (EX more, and springs back) ──────────────────

    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
        "Press", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnPress));
    public static bool GetPress(DependencyObject d) => (bool)d.GetValue(PressProperty);
    public static void SetPress(DependencyObject d, bool v) => d.SetValue(PressProperty, v);

    private static void OnPress(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if (!Moving(d))
        {
            if (el.RenderTransform is ScaleTransform { IsFrozen: false } held) { Scale(held, null); held.ScaleX = held.ScaleY = 1; }
            return;
        }
        if (ScaleOf(el) is not { } scale) return;
        bool down = (bool)e.NewValue;
        // By the same few pixels whatever the size: a row across the window pressed by a button's 3 % fell in by 18.
        double size = Math.Max(1, Math.Max(el.RenderSize.Width, el.RenderSize.Height));
        double to = down ? 1 - Math.Min(Ex ? 0.05 : 0.03, (Ex ? 4.0 : 2.5) / size) : 1.0;
        Scale(scale, new DoubleAnimation(to, down ? Ms(60, 70) : Ms(110, 230)) { EasingFunction = down ? Out : Spring });
    }

    // ── FadeIn: a part that appears comes up from clear (EX's glow round what is pointed at) ──────────

    public static readonly DependencyProperty FadeInProperty = DependencyProperty.RegisterAttached(
        "FadeIn", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnFadeIn));
    public static bool GetFadeIn(DependencyObject d) => (bool)d.GetValue(FadeInProperty);
    public static void SetFadeIn(DependencyObject d, bool v) => d.SetValue(FadeInProperty, v);

    private static void OnFadeIn(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || !(bool)e.NewValue || !Moving(d)) return;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(120, 200)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
    }

    // ── Reveal: what a fold opens comes in (EX also slides down into place) ───────────────────────────

    public static readonly DependencyProperty RevealProperty = DependencyProperty.RegisterAttached(
        "Reveal", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnReveal));
    public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject d, bool v) => d.SetValue(RevealProperty, v);

    private static void OnReveal(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || !(bool)e.NewValue || !Moving(d) || !Acted(d)) return;
        FadeSlide(el, 130, 190, 6);
    }

    // ── Turn: a tree item's arrow turns as it opens (EX turns past and settles) ───────────────────────

    public static readonly DependencyProperty TurnProperty = DependencyProperty.RegisterAttached(
        "Turn", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnTurn));
    public static bool GetTurn(DependencyObject d) => (bool)d.GetValue(TurnProperty);
    public static void SetTurn(DependencyObject d, bool v) => d.SetValue(TurnProperty, v);

    private static void OnTurn(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if (el.RenderTransform is not RotateTransform { IsFrozen: false } rotate)
        {
            rotate = new RotateTransform((el.RenderTransform as RotateTransform)?.Angle ?? 0);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = rotate;
        }
        double to = (bool)e.NewValue ? 90 : 0;
        if (!Moving(d) || !Acted(d)) { rotate.BeginAnimation(RotateTransform.AngleProperty, null); rotate.Angle = to; return; }
        rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, Ms(110, 220)) { EasingFunction = Spring });
    }

    // ── Pop: a tick or a dot that comes on (Classic fades it in, EX springs it up from small) ─────────

    /// <summary>Bound to a tick box's IsChecked, which may be neither: only true makes it come on.</summary>
    public static readonly DependencyProperty PopProperty = DependencyProperty.RegisterAttached(
        "Pop", typeof(object), typeof(Motions), new PropertyMetadata(null, OnPop));
    public static object GetPop(DependencyObject d) => d.GetValue(PopProperty);
    public static void SetPop(DependencyObject d, object v) => d.SetValue(PopProperty, v);

    private static void OnPop(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || !true.Equals(e.NewValue) || !Moving(d) || !Acted(d)) return;
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(100, 110)) { EasingFunction = Out, FillBehavior = FillBehavior.Stop });
        if (Ex && ScaleOf(el) is { } scale)
            Scale(scale, new DoubleAnimation(0.25, 1, Ms(100, 220)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 }, FillBehavior = FillBehavior.Stop });
    }

    // ── Grow: a selection's bar grows out from its middle (across, or down EX's rail) ─────────────────

    public static readonly DependencyProperty GrowProperty = DependencyProperty.RegisterAttached(
        "Grow", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnGrow));
    public static bool GetGrow(DependencyObject d) => (bool)d.GetValue(GrowProperty);
    public static void SetGrow(DependencyObject d, bool v) => d.SetValue(GrowProperty, v);

    /// <summary>"X" to grow across, "Y" to grow down.</summary>
    public static readonly DependencyProperty GrowAxisProperty = DependencyProperty.RegisterAttached(
        "GrowAxis", typeof(string), typeof(Motions), new PropertyMetadata("X"));
    public static string GetGrowAxis(DependencyObject d) => (string)d.GetValue(GrowAxisProperty);
    public static void SetGrowAxis(DependencyObject d, string v) => d.SetValue(GrowAxisProperty, v);

    /// <summary>Grow only on a control being used (a list row's bar): a row a list recycles as it scrolls is
    /// selected again with nobody choosing it.</summary>
    public static readonly DependencyProperty GrowOnUseProperty = DependencyProperty.RegisterAttached(
        "GrowOnUse", typeof(bool), typeof(Motions), new PropertyMetadata(false));
    public static bool GetGrowOnUse(DependencyObject d) => (bool)d.GetValue(GrowOnUseProperty);
    public static void SetGrowOnUse(DependencyObject d, bool v) => d.SetValue(GrowOnUseProperty, v);

    private static void OnGrow(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || !(bool)e.NewValue || !Moving(d) || GetGrowOnUse(d) && !Acted(d) || ScaleOf(el) is not { } scale) return;
        var axis = GetGrowAxis(d) == "Y" ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
        scale.BeginAnimation(axis, new DoubleAnimation(0, 1, Ms(160, 240)) { EasingFunction = Spring, FillBehavior = FillBehavior.Stop });
    }

    // ── Pulse: a count that goes up says so (the issue count; EX also flashes) ────────────────────────

    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(int), typeof(Motions), new PropertyMetadata(0, OnPulse));
    public static int GetPulse(DependencyObject d) => (int)d.GetValue(PulseProperty);
    public static void SetPulse(DependencyObject d, int v) => d.SetValue(PulseProperty, v);

    private static void OnPulse(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el || (int)e.NewValue <= (int)e.OldValue || !Moving(d) || ScaleOf(el) is not { } scale) return;
        var bump = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = Ms(260, 380) };
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(Ex ? 1.28 : 1.16, KeyTime.FromPercent(0.35), Out));
        bump.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), Spring));
        Scale(scale, bump);
        if (!Ex) return;
        var flash = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop, Duration = Ms(260, 380) };
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.2)));
        flash.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), Out));
        el.BeginAnimation(UIElement.OpacityProperty, flash);
    }

    // ── Appear: what opens in a window of its own comes in each time it opens (a tooltip) ─────────────

    public static readonly DependencyProperty AppearProperty = DependencyProperty.RegisterAttached(
        "Appear", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnAppear));
    public static bool GetAppear(DependencyObject d) => (bool)d.GetValue(AppearProperty);
    public static void SetAppear(DependencyObject d, bool v) => d.SetValue(AppearProperty, v);

    private static void OnAppear(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el) return;
        el.Loaded -= Appeared;
        if ((bool)e.NewValue) el.Loaded += Appeared;
    }

    private static void Appeared(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement el && Motion.Enabled) FadeSlide(el, 110, 160, 4);
    }

    // ── Wash: a row a person chooses takes its colour in rather than at once ──────────────────────────

    /// <summary>
    /// Set on the part a row's triggers colour (its border), bound to whether the row is chosen. The triggers give
    /// it the theme's selection brush at once, as before; this lays a brush of the row's own over it for a moment,
    /// the same colour from clear to full, and takes it off again - so the words never fade, the theme's brush is
    /// what stays, and what a contrast check reads afterwards is the theme's own.
    /// </summary>
    public static readonly DependencyProperty WashProperty = DependencyProperty.RegisterAttached(
        "Wash", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnWash));
    public static bool GetWash(DependencyObject d) => (bool)d.GetValue(WashProperty);
    public static void SetWash(DependencyObject d, bool v) => d.SetValue(WashProperty, v);

    /// <summary>The brush a wash under way laid on the part, so a row let go of in the middle drops it at once.</summary>
    private static readonly DependencyProperty WashingProperty = DependencyProperty.RegisterAttached(
        "Washing", typeof(SolidColorBrush), typeof(Motions), new PropertyMetadata(null));

    private static DependencyProperty? BackgroundOf(DependencyObject d) => d switch
    {
        System.Windows.Controls.Border => System.Windows.Controls.Border.BackgroundProperty,
        Controls.ChamferBorder => Controls.ChamferBorder.BackgroundProperty,
        System.Windows.Controls.Panel => System.Windows.Controls.Panel.BackgroundProperty,
        _ => null,
    };

    private static void OnWash(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || BackgroundOf(d) is not { } background) return;
        if (!(bool)e.NewValue)
        {
            if (el.GetValue(WashingProperty) != null) { el.ClearValue(WashingProperty); el.ClearValue(background); }
            return;
        }
        if (!Moving(d) || !Acted(d)) return;
        // The trigger that colours the row answers this same change; what it gave is read once it has.
        el.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (!GetWash(el) || el.GetValue(WashingProperty) != null) return;
            if (el.GetValue(background) is not SolidColorBrush { Color: var to }) return;
            var brush = new SolidColorBrush(Color.FromArgb(0, to.R, to.G, to.B));
            el.SetValue(WashingProperty, brush);
            el.SetValue(background, brush);
            var a = new ColorAnimation(brush.Color, to, Ms(140, 220)) { EasingFunction = Out };
            a.Completed += (_, _) =>
            {
                if (!ReferenceEquals(el.GetValue(WashingProperty), brush)) return;
                el.ClearValue(WashingProperty);
                el.ClearValue(background);
            };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }));
    }

    // ── Lift: a card pointed at rises a little (Classic's Home tutorial cards; EX's glow comes up instead) ──

    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
        "Lift", typeof(bool), typeof(Motions), new PropertyMetadata(false, OnLift));
    public static bool GetLift(DependencyObject d) => (bool)d.GetValue(LiftProperty);
    public static void SetLift(DependencyObject d, bool v) => d.SetValue(LiftProperty, v);

    private static void OnLift(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        var rise = el.RenderTransform as TranslateTransform;
        if (rise == null || rise.IsFrozen)
        {
            if (el.RenderTransform is not (null or MatrixTransform { Matrix.IsIdentity: true })) return;
            el.RenderTransform = rise = new TranslateTransform();
        }
        double to = (bool)e.NewValue ? -2 : 0;
        if (!Moving(d)) { rise.BeginAnimation(TranslateTransform.YProperty, null); rise.Y = to; return; }
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(to, Ms(140, 140)) { EasingFunction = Out });
    }
}
