using System;
using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SMSModForge.View;

/// <summary>
/// A field's text in red when it is irrefutably wrong (the author, 1.8.0:
/// "change the field's text to red if it's irrefutably wrong (no
/// assumptions)"), checked as it is typed.
/// <para/>
/// Two kinds, and only two, because only these are certain:
/// <list type="bullet">
///   <item><see cref="CheckProperty"/> - a box whose value must be one of the
///   things its list offers (an actor, a scene, a variable, a tag...) holding
///   text that is none of them.</item>
///   <item><see cref="NumberProperty"/> - a box for a number holding text that
///   is not one.</item>
/// </list>
/// Never red: an empty box (not filled in yet is not wrong), text holding a
/// <c>{placeholder}</c> a rule fills in or a <c>$variable</c> read in game -
/// both resolve later - and a box whose list has not been built (nothing to
/// judge by).
/// </summary>
public static class KnownValue
{
    public static readonly DependencyProperty CheckProperty = DependencyProperty.RegisterAttached(
        "Check", typeof(bool), typeof(KnownValue), new PropertyMetadata(false, (d, _) => Attach(d)));
    public static void SetCheck(DependencyObject o, bool value) => o.SetValue(CheckProperty, value);
    public static bool GetCheck(DependencyObject o) => (bool)o.GetValue(CheckProperty);

    /// <summary>"Int" or "Float".</summary>
    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number", typeof(string), typeof(KnownValue), new PropertyMetadata(null, (d, _) => Attach(d)));
    public static void SetNumber(DependencyObject o, string? value) => o.SetValue(NumberProperty, value);
    public static string? GetNumber(DependencyObject o) => (string?)o.GetValue(NumberProperty);

    /// <summary>Whether the field is showing as wrong now. For tests.</summary>
    public static readonly DependencyProperty IsWrongProperty = DependencyProperty.RegisterAttached(
        "IsWrong", typeof(bool), typeof(KnownValue), new PropertyMetadata(false));
    public static bool GetIsWrong(DependencyObject o) => (bool)o.GetValue(IsWrongProperty);

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(KnownValue), new PropertyMetadata(false));

    private static void Attach(DependencyObject d)
    {
        if (d is not Control control || (bool)control.GetValue(HookedProperty)) return;
        control.SetValue(HookedProperty, true);

        control.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Recheck(control)));
        INotifyCollectionChanged? watched = null;
        void ListChanged(object? s, NotifyCollectionChangedEventArgs e) => Recheck(control);
        control.Loaded += (_, _) =>
        {
            if (control is ItemsControl items && items.ItemsSource is INotifyCollectionChanged list && !ReferenceEquals(list, watched))
            {
                if (watched != null) watched.CollectionChanged -= ListChanged;
                watched = list;
                watched.CollectionChanged += ListChanged;
            }
            Recheck(control);
        };
        control.Unloaded += (_, _) =>
        {
            if (watched != null) watched.CollectionChanged -= ListChanged;
            watched = null;
        };
        if (control is ComboBox combo) combo.DropDownClosed += (_, _) => Recheck(control);
        control.DataContextChanged += (_, _) => control.Dispatcher.BeginInvoke(() => Recheck(control),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private static void Recheck(Control control)
    {
        string text = control switch
        {
            ComboBox c => c.Text ?? "",
            TextBox t => t.Text ?? "",
            _ => "",
        };
        bool wrong = IsWrong(control, text.Trim());
        if (wrong == GetIsWrong(control)) return;
        control.SetValue(IsWrongProperty, wrong);
        if (wrong) control.SetResourceReference(Control.ForegroundProperty, "Theme.Danger");
        else control.ClearValue(Control.ForegroundProperty);
    }

    private static bool IsWrong(Control control, string text)
    {
        if (text.Length == 0 || text.Contains('{') || text.StartsWith('$')) return false;

        if (GetNumber(control) is { } kind)
            return kind == "Int"
                ? !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                : !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        if (!GetCheck(control) || control is not ItemsControl list || list.ItemsSource is not IEnumerable items) return false;
        string? path = (control as Selector)?.SelectedValuePath;
        if (string.IsNullOrEmpty(path)) path = TextSearch.GetTextPath(control);
        bool any = false;
        foreach (var item in items)
        {
            any = true;
            if (string.Equals(ValueOf(item, path), text, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return any;
    }

    private static string? ValueOf(object? item, string? path)
    {
        if (item == null) return null;
        if (item is string s) return s;
        if (!string.IsNullOrEmpty(path))
        {
            var property = item.GetType().GetProperty(path);
            if (property != null) return property.GetValue(item)?.ToString();
        }
        return item.ToString();
    }
}
