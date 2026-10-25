using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Whether a panel's help is unfolded, kept on the panel (its GroupBox) so
/// every help line in it and the panel's own "?" (EX) read and set the same
/// thing, and remembered per panel between sessions (the author, 1.8.0: help
/// an author reads once took a screen of room on every visit). Set up by the
/// help itself - see <see cref="Controls.HelpText"/>.
/// </summary>
public static class PanelHelp
{
    /// <summary>Whether the panel has help to show: EX draws its "?" then.</summary>
    public static readonly DependencyProperty HasHelpProperty =
        DependencyProperty.RegisterAttached("HasHelp", typeof(bool), typeof(PanelHelp), new PropertyMetadata(false));

    public static bool GetHasHelp(DependencyObject d) => (bool)d.GetValue(HasHelpProperty);
    public static void SetHasHelp(DependencyObject d, bool value) => d.SetValue(HasHelpProperty, value);

    /// <summary>The name the panel's choice is remembered by.</summary>
    public static readonly DependencyProperty KeyProperty =
        DependencyProperty.RegisterAttached("Key", typeof(string), typeof(PanelHelp),
            new PropertyMetadata("", (d, e) => d.SetCurrentValue(OpenProperty, Services.EditorPrefs.GetHelpOpen((string)e.NewValue))));

    public static string GetKey(DependencyObject d) => (string)d.GetValue(KeyProperty);
    public static void SetKey(DependencyObject d, string value) => d.SetValue(KeyProperty, value);

    /// <summary>Unfolded. Folded until opened, and remembered.</summary>
    public static readonly DependencyProperty OpenProperty =
        DependencyProperty.RegisterAttached("Open", typeof(bool), typeof(PanelHelp),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, e) =>
            {
                string key = GetKey(d);
                if (key.Length > 0 && Services.EditorPrefs.GetHelpOpen(key) != (bool)e.NewValue)
                    Services.EditorPrefs.SetHelpOpen(key, (bool)e.NewValue);
            }));

    public static bool GetOpen(DependencyObject d) => (bool)d.GetValue(OpenProperty);
    public static void SetOpen(DependencyObject d, bool value) => d.SetValue(OpenProperty, value);
}
