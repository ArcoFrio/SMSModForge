using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SMSModForge.View;

/// <summary>
/// Whether a row of a tree hangs from a folder - so EX draws the short branch
/// from the folder's line to it (the author, 1.8.0) - or stands at the top of
/// the list, where there is no line to branch from. Read off the row itself,
/// which every row has, rather than off a parent row a top-level one does not:
/// a binding that fails on every top-level row would fill the binding log.
/// </summary>
public sealed class TreeBranch : IValueConverter
{
    public static readonly TreeBranch Visibility = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TreeViewItem row && ItemsControl.ItemsControlFromItemContainer(row) is TreeViewItem
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
