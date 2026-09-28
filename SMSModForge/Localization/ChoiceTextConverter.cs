using System;
using System.Globalization;
using System.Windows.Data;
using SMSModForge.Model;

namespace SMSModForge.Localization;

/// <summary>
/// An option a pack stores, shown in words: the parameter is the field it
/// belongs to, so <c>Pressed</c> under <c>phase</c> reads <c>choice.phase.pressed</c>.
/// The value itself never changes; see <see cref="ParamSchema.ChoiceText"/>.
/// </summary>
public sealed class ChoiceTextConverter : IValueConverter
{
    public static readonly ChoiceTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string option && parameter is string field
            ? ParamSchema.ChoiceText(field, option)
            : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
