using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.View.Converters;

/// <summary>
/// The note beside a dropdown item: values[0] is the item, values[1] the kind
/// of note its ComboBox asked for (<see cref="OptionNotes.KindProperty"/>).
/// </summary>
public sealed class OptionNoteConverter : IMultiValueConverter
{
    public static readonly OptionNoteConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length < 2 ? "" : OptionNotes.NoteFor(values[1] as string, values[0]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException("Notes are read-only.");
}
