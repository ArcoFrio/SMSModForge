using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SMSModForge.View;

/// <summary>
/// Typing in a dropdown narrows it to the names that contain what you typed.
/// <para/>
/// Not a separate control an author has to be given: it is attached by the
/// implicit ComboBox style in App.xaml, so every dropdown in the editor has it
/// and none of them had to be changed. Type <c>na</c> in the speaker box and
/// Anna is there, which prefix matching alone never offered.
/// <para/>
/// WHAT COUNTS AS TYPED. An editable ComboBox already completes as you type:
/// enter <c>an</c> and WPF puts <c>Anna</c> in the box with <c>na</c>
/// selected. Filtering on the box's text would then filter on a word nobody
/// wrote, and the list would collapse to the one item the completion happened
/// to land on. The typed part is everything BEFORE the selection — the caret
/// sits exactly where the person stopped — so that is what this reads, and
/// completion goes on working on top of it.
/// <para/>
/// HOW THE LIST NARROWS. By hiding item containers, not by filtering the
/// collection. A ComboBox bound to <c>ActorOptions</c> shares one default view
/// with every other ComboBox bound to it — and this editor has several, some of
/// them a row apiece inside a list — so a filter set on that view would narrow
/// every one of them at once. Container visibility belongs to the one dropdown
/// and touches nothing anybody else can see. It leaves the selection alone too,
/// which a view filter does not: filtering out the selected item is how a
/// bound value gets written back as empty.
/// </summary>
public static class ComboBoxSearch
{
    /// <summary>
    /// What the person actually typed, as opposed to what completion added.
    /// Read by <see cref="Converters.SearchMatchConverter"/> from each item
    /// container, so a change here re-decides the whole list at once.
    /// </summary>
    public static readonly DependencyProperty TypedProperty =
        DependencyProperty.RegisterAttached(
            "Typed", typeof(string), typeof(ComboBoxSearch),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.Inherits));

    public static string GetTyped(DependencyObject o) => (string)o.GetValue(TypedProperty);
    public static void SetTyped(DependencyObject o, string value) => o.SetValue(TypedProperty, value);

    /// <summary>Switches the behaviour on. Set by the implicit style, so it is
    /// on everywhere without a single call site knowing.</summary>
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(ComboBoxSearch),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ComboBox box) return;

        box.Loaded -= OnLoaded;
        box.DropDownClosed -= OnDropDownClosed;

        if (!(bool)e.NewValue) return;

        box.Loaded += OnLoaded;
        box.DropDownClosed += OnDropDownClosed;
        if (box.IsLoaded) OnLoaded(box, new RoutedEventArgs());
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox box) return;

        // Only an editable one has somewhere to type. The rest keep WPF's own
        // jump-to-first-letter and are left exactly as they were.
        if (!box.IsEditable) return;

        // Built now rather than found. A dropdown that is collapsed when it
        // loads has not applied its template - that waits for its first measure
        // - so the text box was not there yet, the search never attached, and
        // nothing tried again when the dropdown was shown. Which is every
        // dropdown on a row that appears once a type or a mode is chosen: the
        // quest pickers on a condition switched to QuestState, the variable a
        // counter follows, the targets of an action switched to Set Active.
        box.ApplyTemplate();

        if (box.Template?.FindName("PART_EditableTextBox", box) is not TextBox typing) return;

        typing.TextChanged -= OnTextChanged;
        typing.TextChanged += OnTextChanged;
        typing.LostKeyboardFocus -= OnLostFocus;
        typing.LostKeyboardFocus += OnLostFocus;
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox typing) return;

        // Read the caret AFTER this round of input has settled, never during
        // it. Two things move it, and both move it late: a keystroke advances
        // the caret alongside the change, and WPF's completion puts the whole
        // word in first and selects the part it added second. Reading here
        // would catch the text without the caret that explains it - which is
        // how "An" reads as nothing typed at all.
        typing.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => Settled(typing)));
    }

    private static void Settled(TextBox typing)
    {
        var box = Owner(typing);
        if (box == null) return;

        // Everything before the selection. After completion the caret sits at
        // the end of what was typed and the rest is selected, so this is the
        // typed part; with no selection it is the whole box, which is the same
        // thing.
        int stopped = Math.Max(0, Math.Min(typing.SelectionStart, typing.Text.Length));
        string typed = typing.Text.Substring(0, stopped);

        if (GetTyped(box) == typed) return;
        SetTyped(box, typed);

        // Nothing to see if the list is not showing. Opening it is the whole
        // point of typing: an author who types two letters wants the shortlist,
        // not to have to reach for the arrow afterwards.
        if (typed.Length > 0 && !box.IsDropDownOpen && typing.IsKeyboardFocusWithin)
        {
            // Opening an editable dropdown selects everything in its box. After
            // the first letter that is the letter plus whatever completion added
            // - so the second letter replaced all of it and the first one was
            // gone. The caret and the completed part go back where they were.
            int start = typing.SelectionStart, length = typing.SelectionLength;
            box.IsDropDownOpen = true;
            if (typing.SelectionStart != start || typing.SelectionLength != length)
                typing.Select(start, length);
        }
    }

    /// <summary>The list is whole again once it closes, so opening it next time
    /// shows everything rather than the last thing somebody searched for.</summary>
    private static void OnDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox box) return;
        SetTyped(box, "");
        ReturnToChoice(box);
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox typing || Owner(typing) is not ComboBox box) return;
        SetTyped(box, "");
        ReturnToChoice(box);
    }

    /// <summary>
    /// A dropdown whose value can only be one of its options - its choice bound
    /// through SelectedValue, and nothing bound to the typed text - puts back
    /// what is chosen when somebody searches and then leaves without picking.
    /// <para/>
    /// Searching means typing, and typing something that is not an option
    /// clears the selection. The choice itself is kept (the view model ignores
    /// the empty write), so without this the box would go on showing half a
    /// search over a value it no longer displays. A dropdown that accepts free
    /// text binds Text as well, and is left alone: what was typed there IS the
    /// value.
    /// <para/>
    /// Deferred, so a click on an option has landed before this looks.
    /// </summary>
    private static void ReturnToChoice(ComboBox box)
    {
        box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (box.IsDropDownOpen || box.IsKeyboardFocusWithin || box.SelectedItem != null) return;
            if (System.Windows.Data.BindingOperations.GetBindingExpression(box, ComboBox.TextProperty) != null) return;
            var choice = System.Windows.Data.BindingOperations.GetBindingExpression(box, Selector.SelectedValueProperty);
            if (choice == null) return;
            choice.UpdateTarget();

            // A search that matched nothing empties the selection but can leave
            // SelectedValue holding the chosen value, and then refreshing it
            // from the source changes nothing - so the option is found and
            // selected directly, which puts its text back in the box without
            // writing anything back to the source.
            if (box.SelectedItem == null && box.SelectedValue != null)
            {
                foreach (var item in box.Items)
                {
                    if (Equals(ValueOf(item, box.SelectedValuePath), box.SelectedValue))
                    {
                        box.SelectedItem = item;
                        break;
                    }
                }
            }

            // Nothing was chosen before the search either: leave the box as
            // empty as the choice is, not holding the leftover search.
            if (box.SelectedItem == null) box.Text = "";
        }));
    }

    /// <summary>An option's value by a SelectedValuePath of one plain property
    /// name - the only shape this editor uses - or the option itself.</summary>
    private static object? ValueOf(object? item, string? path)
    {
        if (item == null || string.IsNullOrEmpty(path)) return item;
        return item.GetType().GetProperty(path)?.GetValue(item);
    }

    private static ComboBox? Owner(DependencyObject from)
    {
        while (from != null)
        {
            if (from is ComboBox box) return box;
            from = System.Windows.Media.VisualTreeHelper.GetParent(from)
                   ?? LogicalTreeHelper.GetParent(from);
        }
        return null;
    }
}
