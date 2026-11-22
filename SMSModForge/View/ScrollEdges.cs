using System.Windows;
using System.Windows.Controls;

namespace SMSModForge.View;

/// <summary>
/// Whether a scroll viewer has more above or below what it shows, for a template to draw a sign of it. The EX rail
/// down the window's side (1.10.0): at 900 px high its last tabs were cut off, and its only sign of scrolling was a
/// scroll bar three pixels wide. Set <c>Watch</c> on the viewer; <c>MoreAbove</c> and <c>MoreBelow</c> follow it.
/// </summary>
public static class ScrollEdges
{
    public static readonly DependencyProperty WatchProperty =
        DependencyProperty.RegisterAttached("Watch", typeof(bool), typeof(ScrollEdges), new PropertyMetadata(false, OnWatch));

    public static bool GetWatch(DependencyObject o) => (bool)o.GetValue(WatchProperty);
    public static void SetWatch(DependencyObject o, bool value) => o.SetValue(WatchProperty, value);

    public static readonly DependencyProperty MoreAboveProperty =
        DependencyProperty.RegisterAttached("MoreAbove", typeof(bool), typeof(ScrollEdges), new PropertyMetadata(false));

    public static bool GetMoreAbove(DependencyObject o) => (bool)o.GetValue(MoreAboveProperty);
    public static void SetMoreAbove(DependencyObject o, bool value) => o.SetValue(MoreAboveProperty, value);

    public static readonly DependencyProperty MoreBelowProperty =
        DependencyProperty.RegisterAttached("MoreBelow", typeof(bool), typeof(ScrollEdges), new PropertyMetadata(false));

    public static bool GetMoreBelow(DependencyObject o) => (bool)o.GetValue(MoreBelowProperty);
    public static void SetMoreBelow(DependencyObject o, bool value) => o.SetValue(MoreBelowProperty, value);

    private static void OnWatch(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;
        viewer.ScrollChanged -= Changed;
        if ((bool)e.NewValue) viewer.ScrollChanged += Changed;
    }

    // Less than this out of sight is the content's margin, not more of it: a tab scrolled into view leaves the
    // rail's 4 px under it, and an arrow over the tab it had just shown said there was more.
    private const double Slack = 6;

    // ScrollChanged comes with a change of the content's or the viewport's size too, not only of the offset.
    private static void Changed(object sender, ScrollChangedEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        SetMoreAbove(viewer, viewer.VerticalOffset > Slack);
        SetMoreBelow(viewer, viewer.ScrollableHeight - viewer.VerticalOffset > Slack);
    }
}
