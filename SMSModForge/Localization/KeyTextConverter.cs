using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// A choice whose value is a key, shown as the text the key names.
/// <para/>
/// For the lists whose items the code compares - "Quest action" or
/// "Variable" decides where a count comes from - the item has to stay the
/// same whatever language is on screen, so the item is the key and only the
/// template reads it.
/// </summary>
public sealed class KeyTextConverter : IValueConverter
{
    public static readonly KeyTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string key ? Loc.Optional(key) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
