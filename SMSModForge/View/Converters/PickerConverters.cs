using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SMSModForge.Localization;

namespace SMSModForge.View.Converters;

/// <summary>An expression icon's id to what it is called, in the editor's
/// language (<see cref="Model.ExpressionIcons"/>).</summary>
public sealed class ExpressionIconNameConverter : IValueConverter
{
    public static readonly ExpressionIconNameConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string icon ? Loc.T(Model.ExpressionIcons.NameKey(icon)) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>A count to <see cref="Visibility.Visible"/> when there is
/// anything to show, <see cref="Visibility.Collapsed"/> when it is 0.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public static readonly CountToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
