using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Maps <see cref="bool"/> to <see cref="Visibility"/>:
/// <list type="bullet">
///   <item>true → Visible</item>
///   <item>false → Collapsed</item>
/// </list>
/// Unlike WPF's built-in <c>BooleanToVisibilityConverter</c> this one
/// collapses (removing layout space) rather than hiding (which leaves
/// a gap). Both directions are reversible so it's safe on two-way
/// bindings.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Inverse of <see cref="BoolToVisibilityConverter"/>:
/// <list type="bullet">
///   <item>true → Collapsed</item>
///   <item>false → Visible</item>
/// </list>
/// Used for "show this section when the flag is false" patterns —
/// e.g. the Integration tab's picker view (visible when not in code
/// mode).
/// </summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => !(value is Visibility v && v == Visibility.Visible);
}
