using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Shows a button's empty Music field as <see cref="Label"/> and stores that
/// choice back as empty.
/// <para/>
/// The field has always meant "leave the music alone" when it is blank, but a
/// blank row in a dropdown reads as an unfinished field rather than as a
/// decision — there is nothing to tell "I have not chosen" from "I chose no
/// change". This gives that state a name without giving it a value: nothing is
/// written to the pack, so the manifest and the runtime see exactly what they
/// saw before.
/// <para/>
/// Deliberately not applied to the SwitchMusic action's own music param, where
/// an empty value is not a choice but a mistake, and validation says so.
/// </summary>
public sealed class DefaultMusicConverter : IValueConverter
{
    public static readonly DefaultMusicConverter Instance = new();

    /// <summary>What the "no change" row reads as, and the entry the pickers
    /// put at the top of their list.</summary>
    public const string Label = "Default";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Label : (string)value!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim() ?? "";
        // A track genuinely called Default would be indistinguishable, so the
        // label is only treated as the sentinel when it is exactly that word.
        return string.Equals(s, Label, StringComparison.Ordinal) ? "" : s;
    }
}
