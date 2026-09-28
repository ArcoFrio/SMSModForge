using System;
using System.Globalization;
using System.Windows.Data;

namespace SMSModForge.Localization;

/// <summary>
/// A count in a window's layout, said in the form its number takes:
/// <c>Text="{Binding Issues.Count, Converter={x:Static l:PluralConverter.Instance}, ConverterParameter=statusBar.issues}"</c>
/// reads <c>statusBar.issues.one</c> or <c>.other</c> (or whichever forms the
/// language has) with <c>{count}</c> filled in.
/// <para/>
/// "3 issue(s)" is the English way round this, and only English's: a language
/// whose plural changes the word's ending, or whose "few" differs from its
/// "many", has no brackets to put it in.
/// </summary>
public sealed class PluralConverter : IValueConverter
{
    public static readonly PluralConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long n = value switch
        {
            int i => i,
            long l => l,
            double d => (long)Math.Round(d),
            _ => 0,
        };
        return Loc.P(parameter as string ?? "", n);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
