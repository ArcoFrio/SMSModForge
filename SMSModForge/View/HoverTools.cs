using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SMSModForge.View;

/// <summary>
/// Buttons shown only while the mouse is over what they work on - Copy all,
/// Paste and Overwrite under a list of conditions or actions, which were
/// there, mostly greyed, under every list (the author, 1.8.0: "showing only
/// when hovering sounds like a good idea"). Hidden, they keep their room, so
/// nothing under the list moves when the mouse comes in.
/// <para/>
/// What they work on is the panel holding their row: the list above it and
/// the row itself. They show too while the keyboard is anywhere in it, so a
/// person who tabs through the list reaches them.
/// </summary>
public static class HoverTools
{
    public static readonly DependencyProperty RevealProperty =
        DependencyProperty.RegisterAttached("Reveal", typeof(bool), typeof(HoverTools),
            new PropertyMetadata(false, (d, e) => { if (d is FrameworkElement f && (bool)e.NewValue) Attach(f); }));

    public static bool GetReveal(DependencyObject d) => (bool)d.GetValue(RevealProperty);
    public static void SetReveal(DependencyObject d, bool value) => d.SetValue(RevealProperty, value);

    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(120);

    private static void Attach(FrameworkElement tools)
    {
        tools.Opacity = 0;
        tools.IsHitTestVisible = false;
        UIElement? area = null;
        void Update()
        {
            bool shown = area != null && (area.IsMouseOver || area.IsKeyboardFocusWithin);
            if (shown == tools.IsHitTestVisible) return;
            tools.IsHitTestVisible = shown;
            tools.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(shown ? 1 : 0, SystemParameters.ClientAreaAnimation ? Fade : TimeSpan.Zero));
        }
        void OnMouse(object s, System.Windows.Input.MouseEventArgs e) => Update();
        void OnFocus(object s, DependencyPropertyChangedEventArgs e) => Update();
        tools.Loaded += (_, _) =>
        {
            area = AreaOf(tools);
            if (area == null) return;
            area.MouseEnter += OnMouse;
            area.MouseLeave += OnMouse;
            area.IsKeyboardFocusWithinChanged += OnFocus;
            Update();
        };
        tools.Unloaded += (_, _) =>
        {
            if (area == null) return;
            area.MouseEnter -= OnMouse;
            area.MouseLeave -= OnMouse;
            area.IsKeyboardFocusWithinChanged -= OnFocus;
            area = null;
        };
    }

    /// <summary>The panel holding the row the tools sit in.</summary>
    private static UIElement? AreaOf(DependencyObject tools)
    {
        var row = VisualTreeHelper.GetParent(tools);
        return row == null ? null : VisualTreeHelper.GetParent(row) as UIElement;
    }
}
