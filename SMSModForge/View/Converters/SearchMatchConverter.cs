using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Whether one row of a dropdown survives what the person has typed into it.
/// <para/>
/// Matched anywhere in the name rather than only at the front, which is the
/// whole reason this exists: WPF's own completion finds Anna from <c>An</c> and
/// never from <c>na</c>, and half the names in this editor are found by their
/// middle — <c>Anna_GoldenBikini</c>, <c>S_CrazyOldMan</c>, <c>drfrost</c>.
/// <para/>
/// Nothing typed means everything shows. That is not a special case so much as
/// the ordinary state of a dropdown.
/// </summary>
public sealed class SearchMatchConverter : IMultiValueConverter
{
    public static readonly SearchMatchConverter Instance = new();

    /// <summary>
    /// values[0] is the item, values[1] what was typed, values[2] the ComboBox.
    /// <para/>
    /// The typed text arrives as a bound VALUE rather than being fetched off
    /// the ComboBox here, and that is the difference between a list that
    /// narrows and one that is decided once: a converter that reaches for a
    /// property itself gives WPF nothing to notice changing.
    /// </summary>
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[2] is not ComboBox box) return Visibility.Visible;

        string typed = values[1] as string ?? "";
        if (typed.Length == 0) return Visibility.Visible;

        return Matches(values[0], box, typed) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Whether this item, or anything under it, answers to what was typed.
    /// <para/>
    /// A group heading is handed the group rather than an item, and survives
    /// exactly as long as something under it does — otherwise a search leaves
    /// headings standing over nothing.
    /// </summary>
    private static bool Matches(object? item, ComboBox box, string typed)
    {
        if (item is System.Windows.Data.CollectionViewGroup group)
            return group.Items.Any(child => Matches(child, box, typed));

        return TextOf(item, box).IndexOf(typed, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    /// <summary>
    /// The text this row shows, asked for the way the ComboBox itself asks.
    /// <para/>
    /// Reading <c>ToString()</c> alone would search the type name of every
    /// option that is an object rather than a string, and match everything or
    /// nothing.
    /// </summary>
    private static string TextOf(object? item, ComboBox box)
    {
        if (item == null) return "";

        string path = !string.IsNullOrEmpty(box.DisplayMemberPath)
            ? box.DisplayMemberPath
            : TextSearch.GetTextPath(box) ?? "";

        if (path.Length > 0)
        {
            var property = item.GetType().GetProperty(path);
            if (property != null) return property.GetValue(item)?.ToString() ?? "";
        }

        return item.ToString() ?? "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
