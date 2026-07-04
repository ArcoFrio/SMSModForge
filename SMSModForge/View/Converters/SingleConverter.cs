using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Two-way converter between a <see cref="float"/> and its text box, always
/// using <see cref="CultureInfo.InvariantCulture"/> so the decimal separator is
/// '.' regardless of the OS/UI culture — matching the on-disk JSON and the
/// string-based action params (which are parsed invariantly at runtime). WPF
/// bindings otherwise default to en-US, and a direct float binding fights the
/// user when typing a decimal point.
/// <para/>
/// <see cref="ConvertBack"/> is lenient: it accepts ',' as a decimal separator
/// too, and returns <see cref="Binding.DoNothing"/> on unparseable input so the
/// field keeps its last good value instead of erroring.
/// </summary>
public sealed class SingleConverter : IValueConverter
{
    public static readonly SingleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is float f ? f.ToString(CultureInfo.InvariantCulture) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim().Replace(',', '.') ?? "";
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : Binding.DoNothing;
    }
}
