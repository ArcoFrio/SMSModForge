using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Sorts a dropdown's items under headings, from a lookup the view model
/// supplies.
/// <para/>
/// The general form of <see cref="MusicOriginConverter"/>, which answers one
/// fixed question. Most of the lists worth grouping ask a different question
/// each — which of the game's 64 variable lists a name belongs to, which folder
/// an author filed a variable in, whether a level is theirs or the game's — and
/// each of those answers lives in the view model, not here.
/// <para/>
/// Used as a <see cref="PropertyGroupDescription"/> converter with a null
/// property name, so it is handed the item itself.
/// </summary>
public sealed class HeadingConverter : IValueConverter
{
    private readonly Func<object?, string> _heading;

    public HeadingConverter(Func<object?, string> heading) => _heading = heading;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => _heading(value) ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Headings are read-only.");
}
