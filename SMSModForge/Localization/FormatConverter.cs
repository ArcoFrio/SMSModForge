using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// A value put into a sentence in a window's layout:
/// <c>Text="{Binding Issues.Count, Converter={x:Static l:FormatConverter.Instance}, ConverterParameter=home.issuesCount}"</c>
/// reads <c>home.issuesCount</c> ("Issues ({0})") with the value in its gap.
/// <para/>
/// What <c>StringFormat={l:T key}</c> used to do, except that a StringFormat is
/// read once, as the window is built, and this is read every time the binding
/// is - so the sentence changes language along with everything around it.
/// </summary>
public sealed class FormatConverter : IValueConverter
{
    public static readonly FormatConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Format(culture, Loc.T(parameter as string ?? ""), value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
