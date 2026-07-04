using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// One-way <see cref="IValueConverter"/> that maps <c>null</c> to
/// <see cref="Visibility.Collapsed"/> and any non-null value to
/// <see cref="Visibility.Visible"/>. Used by the Places tab to switch
/// between the Place editor and the VanillaExtension editor based on which
/// of the two mutually-exclusive selections is set.
/// <para/>
/// Pass <c>ConverterParameter="Invert"</c> to flip the mapping
/// (non-null → Collapsed, null → Visible).
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public static readonly NullToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        bool nonNull = value != null;
        if (invert) nonNull = !nonNull;
        return nonNull ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
