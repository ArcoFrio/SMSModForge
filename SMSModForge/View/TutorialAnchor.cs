using System.Windows;
using System.Windows.Media;

namespace SMSModForge.View;

/// <summary>
/// Marks a control a tutorial step can point at, by a stable id such as
/// <c>tab:characters</c>, <c>btn:addCharacter</c> or <c>field:baseSprite</c>.
/// <para/>
/// Deliberately separate from <see cref="IssueTarget"/>: those tags are
/// validation field tokens, matched leniently against an issue's <c>Where</c>
/// path, and there are only a couple of dozen. A tutorial needs to point at
/// buttons, tabs and toolbars that no validation issue ever names, and wants
/// exact matching rather than prefix matching. Same idea, different vocabulary
/// — sharing one would tie two features to each other's naming for no gain.
/// </summary>
public static class TutorialAnchor
{
    public static readonly DependencyProperty IdProperty =
        DependencyProperty.RegisterAttached(
            "Id", typeof(string), typeof(TutorialAnchor), new PropertyMetadata(null));

    public static void SetId(DependencyObject o, string value) => o.SetValue(IdProperty, value);
    public static string? GetId(DependencyObject o) => (string?)o.GetValue(IdProperty);

    /// <summary>
    /// Depth-first search for the anchor with this id, skipping anything not
    /// currently rendered — a control on an unselected tab has no position to
    /// highlight, and returning it would spotlight empty space.
    /// </summary>
    public static FrameworkElement? Find(DependencyObject root, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe)
            {
                if (GetId(fe) == id && fe.IsVisible && fe.ActualWidth > 0 && fe.ActualHeight > 0)
                    return fe;
            }
            if (Find(child, id) is FrameworkElement found) return found;
        }
        return null;
    }
}
