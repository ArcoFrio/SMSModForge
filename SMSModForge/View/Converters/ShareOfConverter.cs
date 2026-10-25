using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// A share of a length, up to a ceiling: <c>ConverterParameter="0.25,150"</c>
/// gives a quarter of the bound length, never more than 150. For a panel that must
/// stay a minor part of the column it shares however short the window is - a
/// fixed ceiling alone let the dialogue cheatsheet outgrow the tree beside it
/// in a short window once it had more to say (1.8.0).
/// </summary>
public sealed class ShareOfConverter : IValueConverter
{
    public static readonly ShareOfConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bits = (parameter as string ?? "").Split(',');
        double share = bits.Length > 0 && double.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1;
        double most = bits.Length > 1 && double.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : double.PositiveInfinity;
        if (value is not double length || double.IsNaN(length) || length <= 0) return most;
        return Math.Min(most, length * share);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
