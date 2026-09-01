using System;
using System.Globalization;
using System.Windows.Data;
using SMSModForge.Model;

namespace SMSModForge.View.Converters;

/// <summary>
/// Sorts a music name into the heading it belongs under: the pack's own
/// tracks, or the game's.
/// <para/>
/// The two live in one namespace — both are children of 12_AudioPlayer and
/// either can be named — so nothing in the string itself says where a track
/// came from. The pickers are editable, which rules out the "Vanilla — x" /
/// "This pack — x" labelling used elsewhere: there the label is decoration
/// over a token, here the text IS the stored value and has to stay typable.
/// Grouping puts the distinction in the headings instead, where it costs the
/// value nothing.
/// <para/>
/// Used as a <see cref="System.Windows.Data.PropertyGroupDescription"/>
/// converter with a null property name, so it is handed the item itself.
/// </summary>
public sealed class MusicOriginConverter : IValueConverter
{
    public static readonly MusicOriginConverter Instance = new();

    public const string PackHeading = "This pack";
    public const string GameHeading = "The game's own";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";

        // A pack track sharing a vanilla name is filed under the game. Two
        // objects under one parent then answer to that name and which one a
        // lookup reaches is not something the editor can promise, so the
        // heading names the one that is there in every install — and an author
        // who meant their own track has been told, by the heading, that the
        // name was already taken.
        return VanillaMusic.Contains(s) ? GameHeading : PackHeading;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Grouping only.");
}
