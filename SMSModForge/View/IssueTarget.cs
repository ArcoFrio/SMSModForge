using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Tags an editor control with the validation <c>Where</c> field token it
/// edits (e.g. <c>defaultBustKey</c>, <c>baseSprite</c>, <c>jiggle.strength</c>,
/// <c>roomTalk</c>, <c>actor</c>). Double-clicking a validation issue walks the
/// active tab for the control whose token matches the issue's field suffix and
/// flashes that exact field — see <c>MainWindow.NavigateToIssue</c>.
/// <para/>
/// Tokens are matched leniently: a tag of <c>mouth</c> matches a
/// <c>mouth[2]</c> suffix and <c>jiggle</c> matches <c>jiggle.strength</c>,
/// so a control can stand in for a family of indexed/sub-fields.
/// </summary>
public static class IssueTarget
{
    public static readonly DependencyProperty FieldProperty =
        DependencyProperty.RegisterAttached(
            "Field", typeof(string), typeof(IssueTarget), new PropertyMetadata(null));

    public static void SetField(DependencyObject o, string value) => o.SetValue(FieldProperty, value);
    public static string? GetField(DependencyObject o) => (string?)o.GetValue(FieldProperty);
}
