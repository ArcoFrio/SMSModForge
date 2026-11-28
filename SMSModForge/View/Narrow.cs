using System.Windows;

namespace SMSModForge.View;

/// <summary>
/// Set on a container that shows a record's own template in a narrow column: the
/// place preview's panel beside the picture, which shows the picked object's or
/// character's settings with the very template of its record below (the author,
/// after 1.10.0: "all of the settings that already appear for GOs on their records in the
/// bottom to also appear on the selected GO window on the right, for quick editing").
/// One template, so a setting added to the record is in the panel too.
/// <para/>
/// Inherited, so a template reads it where it stands
/// (<c>{Binding (view:Narrow.Is), RelativeSource={RelativeSource Self}}</c>) and lays
/// itself out for the width: its buttons on a row of their own under its name, and
/// the records nested under it left to their own rows (the hierarchy beside the
/// picture lists them, and picks them).
/// </summary>
public static class Narrow
{
    public static readonly DependencyProperty IsProperty =
        DependencyProperty.RegisterAttached(
            "Is", typeof(bool), typeof(Narrow),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetIs(DependencyObject o, bool value) => o.SetValue(IsProperty, value);
    public static bool GetIs(DependencyObject o) => (bool)o.GetValue(IsProperty);
}
