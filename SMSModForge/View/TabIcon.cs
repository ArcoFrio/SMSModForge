using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// A main tab's icon - a glyph of Windows' own icon font (Segoe MDL2 Assets).
/// Shown where a theme's tab strip has room for one: the EX themes' rail down
/// the side of the window (1.8.0). A Normal theme's tabs are words alone and
/// do not show it.
/// </summary>
public static class TabIcon
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(TabIcon), new PropertyMetadata(""));

    public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string value) => o.SetValue(GlyphProperty, value);

    /// <summary>How many records the tab holds, small under its name on the
    /// EX rail (the author, 1.8.0); none shown at 0.</summary>
    public static readonly DependencyProperty CountProperty =
        DependencyProperty.RegisterAttached("Count", typeof(int), typeof(TabIcon), new PropertyMetadata(0));

    public static int GetCount(DependencyObject o) => (int)o.GetValue(CountProperty);
    public static void SetCount(DependencyObject o, int value) => o.SetValue(CountProperty, value);

    /// <summary>A number worth noticing - the ModForge tab's issues - as a
    /// badge on the icon; none shown at 0.</summary>
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(int), typeof(TabIcon), new PropertyMetadata(0));

    public static int GetBadge(DependencyObject o) => (int)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, int value) => o.SetValue(BadgeProperty, value);
}
