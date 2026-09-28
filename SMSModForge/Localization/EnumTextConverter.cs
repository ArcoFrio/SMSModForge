using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// A choice that is a value in code, shown as words:
/// <c>WeatherType.Inside</c> reads <c>enum.WeatherType.Inside</c>.
/// <para/>
/// A list bound straight to an enum shows the member's name, which is English
/// that no translation can reach - it is the program's spelling, not a text.
/// </summary>
public sealed class EnumTextConverter : IValueConverter
{
    public static readonly EnumTextConverter Instance = new();

    public static string KeyOf(Enum value) => "enum." + value.GetType().Name + "." + value;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Enum e ? Loc.T(KeyOf(e)) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
