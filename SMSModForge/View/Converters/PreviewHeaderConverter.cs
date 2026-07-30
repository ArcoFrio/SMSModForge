using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Labels the character preview according to what is actually being rendered.
/// <para/>
/// A pack-drawn bust runs the live JiggleSprite pass over this pack's sprites;
/// a borrowed one is the game's own art, shown as it ships. Calling both "live
/// JiggleSprite" would claim the second is doing something it is not.
/// </summary>
public sealed class PreviewHeaderConverter : IValueConverter
{
    public static readonly PreviewHeaderConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Preview (live JiggleSprite)" : "Preview (vanilla bust)";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
