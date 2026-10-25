using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// A name shown with a capital first letter - "anis" reads "Anis" - while
/// what it names keeps its own case (the author, 1.8.0: actor names in the
/// node editor, "purely aesthetic"). One way only: nothing written through it
/// changes, so a speaker's key is never re-cased in the pack.
/// </summary>
public sealed class CapitalizedConverter : IValueConverter
{
    public static readonly CapitalizedConverter Instance = new();

    /// <summary>The first letter in upper case, the rest as it is. Invariant,
    /// so "i" never becomes a Turkish dotted "İ".</summary>
    public static string Of(string? text)
        => string.IsNullOrEmpty(text) || !char.IsLower(text[0])
            ? text ?? ""
            : char.ToUpperInvariant(text[0]) + text.Substring(1);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s ? Of(s) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
