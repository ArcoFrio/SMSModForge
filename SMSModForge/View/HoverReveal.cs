using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SMSModForge.View;

/// <summary>
/// Rest the mouse on an element for half a second and another part of it opens, gently; move away and it folds
/// again (1.10.0, the author: "hovering over a tutorial for 0.5s should expand the tutorial card with a subtle
/// animation to show a bit more detail about it and a very short 1-line version of each of its steps"). Set
/// <c>Reveals</c> on what is hovered, to the element that opens - collapsed until then.
/// <para/>
/// The animation is the editor's own motion (<see cref="Services.Motion"/>): off in Options, or with Windows'
/// animations off, the part simply appears.
/// </summary>
public static class HoverReveal
{
    /// <summary>How long the mouse rests before the part opens.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    public static readonly DependencyProperty RevealsProperty =
        DependencyProperty.RegisterAttached("Reveals", typeof(FrameworkElement), typeof(HoverReveal),
                                            new PropertyMetadata(null, OnReveals));

    public static FrameworkElement? GetReveals(DependencyObject o) => (FrameworkElement?)o.GetValue(RevealsProperty);
    public static void SetReveals(DependencyObject o, FrameworkElement? value) => o.SetValue(RevealsProperty, value);

    private static readonly DependencyProperty TimerProperty =
        DependencyProperty.RegisterAttached("Timer", typeof(DispatcherTimer), typeof(HoverReveal), new PropertyMetadata(null));

    private static void OnReveals(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host) return;
        host.MouseEnter -= Entered;
        host.MouseLeave -= Left;
        if (e.NewValue is FrameworkElement part)
        {
            part.Visibility = Visibility.Collapsed;
            host.MouseEnter += Entered;
            host.MouseLeave += Left;
        }
    }

    private static void Entered(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var host = (FrameworkElement)sender;
        if (host.GetValue(TimerProperty) is not DispatcherTimer timer)
        {
            timer = new DispatcherTimer(DispatcherPriority.Input, host.Dispatcher) { Interval = Delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (host.IsMouseOver) Open(host);
            };
            host.SetValue(TimerProperty, timer);
        }
        timer.Stop();
        timer.Start();
    }

    private static void Left(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var host = (FrameworkElement)sender;
        (host.GetValue(TimerProperty) as DispatcherTimer)?.Stop();
        Close(host);
    }

    /// <summary>Open the part: what the half-second's rest does. Tests call it, as the timer does.</summary>
    internal static void Open(FrameworkElement host)
    {
        if (GetReveals(host) is not FrameworkElement part || part.Visibility == Visibility.Visible && part.Opacity >= 1) return;
        part.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        part.BeginAnimation(UIElement.OpacityProperty, null);
        part.Visibility = Visibility.Visible;
        if (!Services.Motion.Enabled || Services.TestMode.Active)
        {
            part.MaxHeight = double.PositiveInfinity;
            part.Opacity = 1;
            return;
        }
        // To the height it wants at the width it has, then no limit, so a wrapped line is never cut.
        part.MaxHeight = double.PositiveInfinity;
        part.Measure(new Size(part.ActualWidth > 0 ? part.ActualWidth : (host.ActualWidth > 0 ? host.ActualWidth : double.PositiveInfinity),
                              double.PositiveInfinity));
        double tall = Math.Max(1, part.DesiredSize.Height);
        var time = new Duration(TimeSpan.FromMilliseconds(220));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0, tall, time) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        grow.Completed += (_, _) => part.MaxHeight = double.PositiveInfinity;
        part.MaxHeight = tall;
        part.BeginAnimation(FrameworkElement.MaxHeightProperty, grow);
        part.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, time) { EasingFunction = ease });
    }

    /// <summary>Fold the part away again.</summary>
    internal static void Close(FrameworkElement host)
    {
        if (GetReveals(host) is not FrameworkElement part || part.Visibility != Visibility.Visible) return;
        part.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
        part.BeginAnimation(UIElement.OpacityProperty, null);
        if (!Services.Motion.Enabled || Services.TestMode.Active)
        {
            part.Visibility = Visibility.Collapsed;
            return;
        }
        var time = new Duration(TimeSpan.FromMilliseconds(150));
        var shrink = new DoubleAnimation(part.ActualHeight, 0, time) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        shrink.Completed += (_, _) =>
        {
            if (host.IsMouseOver) return;   // back over it while it folded: leave it to the next rest
            part.Visibility = Visibility.Collapsed;
            part.BeginAnimation(FrameworkElement.MaxHeightProperty, null);
            part.MaxHeight = double.PositiveInfinity;
        };
        part.BeginAnimation(FrameworkElement.MaxHeightProperty, shrink);
        part.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(part.Opacity, 0, time));
    }
}
