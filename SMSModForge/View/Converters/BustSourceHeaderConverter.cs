using System;
using System.Globalization;
using System.Windows.Data;
using SMSModForge.Model;
using SMSModForge.Localization;

namespace SMSModForge.View.Converters;

/// <summary>
/// One-way converter turning a <see cref="BustSource"/> group key into the
/// heading shown above that section of the character tree.
/// <para/>
/// The enum names are what the manifest stores and are deliberately terse;
/// these are what an author should read when scanning the list, which is a
/// different job. Grouping by the raw value would put "Pack" and "None" in the
/// sidebar, neither of which says anything useful about the characters under it.
/// </summary>
public sealed class BustSourceHeaderConverter : IValueConverter
{
    public static readonly BustSourceHeaderConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            BustSource.Pack => Loc.T("characters.group.pack"),
            // Not "vanilla-based" any more: these are the game's own cast,
            // present in every pack to be used rather than described.
            BustSource.Vanilla => Loc.T("characters.group.games"),
            BustSource.None => Loc.T("characters.group.voiceOnly"),
            _ => Loc.T("characters.group.rest"),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
