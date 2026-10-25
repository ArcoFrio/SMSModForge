using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Services;

namespace SMSModForge.View.Converters;

/// <summary>"#RRGGBB" (or with alpha) as a brush to paint a swatch with;
/// nothing for text that is not a colour.</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public static readonly HexBrushConverter Instance = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => ColorMath.TryParse(value as string, out byte r, out byte g, out byte b, out byte a)
            ? new SolidColorBrush(Color.FromArgb(a, r, g, b))
            : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
