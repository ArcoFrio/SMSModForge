using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// Visible for a dropdown whose notes are variables' (the game's hints, the
/// pack's descriptions: <see cref="OptionNotes.IsVariableKind"/>), collapsed
/// for every other: the toggle at the top of an open list (1.9.0) belongs to
/// those lists only.
/// </summary>
public sealed class VariableListToVisibilityConverter : IValueConverter
{
    public static readonly VariableListToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => OptionNotes.IsVariableKind(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
