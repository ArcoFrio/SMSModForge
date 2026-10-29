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

/// <summary>
/// Inverts a <see cref="bool"/> value: true → false, false → true.
/// Used for binding a False RadioButton's IsChecked to the same BoolValue
/// that the True RadioButton binds to directly.
/// <para/>
/// It has to invert BOTH ways. It did not: <c>ConvertBack</c> returned the
/// value unchanged, so checking a False radio wrote <c>true</c> — and because
/// WPF unchecks the outgoing radio first, whose own binding had just written
/// the right answer, the wrong one landed second and the click did nothing at
/// all. That is three places at once: the True/False pair on a Variable
/// condition, on a Set-variable action, and on a Bool variable's default.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    /// <summary>
    /// An indeterminate box writes nothing rather than guessing: these pairs
    /// are two-state, and a null on the way back means "no answer", which is
    /// not the same as "false".
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : Binding.DoNothing;
}

/// <summary>
/// A radio button's answer, for a pair (or more) bound to one bool: only the
/// button being picked writes. <see cref="Invert"/> for the button that
/// stands for false.
/// <para/>
/// Two-way, the button the group lets go of wrote too - WPF unchecks the
/// other one, and its "false" went back into the value as an answer of its
/// own. When the pair moved to another item it landed on whichever item the
/// binding still held: an author's Bool default flipped to true on opening
/// another variable, and an Int's 0 became "true" from the hidden pair
/// (reported after 1.8.0). The value reaches the other button through its
/// own binding, so letting go needs to say nothing.
/// </summary>
public sealed class PickedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && (Invert ? !b : b);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? !Invert : Binding.DoNothing;
}
