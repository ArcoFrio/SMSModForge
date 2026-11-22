using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;

namespace SMSModForge;

/// <summary>
/// One history for the whole window (1.10.0, the author: "ModForge's undo/redo
/// controls are very clunky and non-responsive ... every single action I take
/// within the modpack can be undone and redone"). What made it clunky was found
/// by pressing Ctrl+Z after an edit of every control there is
/// (UndoEveryControlTests):
/// <list type="bullet">
/// <item><b>Two histories.</b> A text box keeps its own memory of what was
/// typed in it, and answered Ctrl+Z itself while it had the keyboard - behind
/// the editor's back. Typed in, left (a step of the editor's), come back to,
/// Ctrl+Z: the box took its text back, which the editor then saw as a new edit;
/// the next Ctrl+Z put the text back again. And with other edits made since,
/// the box undid its old typing instead of the last thing done. The boxes'
/// own memory is off here: Ctrl+Z is the editor's, wherever the keyboard is.</item>
/// <item><b>Steps that were not taken yet.</b> A tick, a list or a slider
/// became a step only when the keyboard next moved; two in a row on the same
/// control were one step, and Ctrl+Z straight after the first of a session did
/// nothing. A step is now taken as the mouse is let go and as a key comes up,
/// and typing is cut into steps where a person would cut it: where the caret
/// is moved, where typing turns to deleting, at a paste.</item>
/// <item><b>The keyboard lost.</b> Putting a step back can build the page
/// again, and the box that was being typed in went with it. The keyboard is
/// put back in the same box, the caret where the change was.</item>
/// </list>
/// </summary>
public partial class MainWindow
{
    private void WireHistory(MainViewModel vm)
    {
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocusForHistory), handledEventsToo: true);

        // Where a step ends. Each of these costs nothing unless the pack has been said to have changed.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) =>
        {
            _lastTextKey = TextKey.None;
            vm.Undo.CheckpointIfTouched();
        }), handledEventsToo: true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => StepOnceSettled(vm)), handledEventsToo: true);
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, e) => OnKeyDownForHistory(vm, e)), handledEventsToo: true);
        AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler((_, _) =>
        {
            // In a box, typing is cut into steps by what is typed (OnKeyDownForHistory), not key by key.
            if (Keyboard.FocusedElement is not TextBoxBase) StepOnceSettled(vm);
        }), handledEventsToo: true);
        // The line's box says where its own steps begin: a run of typing, a tag.
        AddHandler(MarkupTextBox.StepBeginningEvent, new RoutedEventHandler((_, _) => vm.Undo.CheckpointIfTouched()), handledEventsToo: true);

        vm.Undo.Moving += RememberKeyboard;
        vm.HistoryPageShown += NotePage;
        vm.Undo.Moved += RestoreKeyboard;
        Closed += (_, _) => vm.DetachFromCommands();
    }

    /// <summary>Ctrl+Z, Ctrl+Y: what is being typed is in the pack first, then the editor's own undo or redo -
    /// whatever has the keyboard.</summary>
    private void History(RelayCommand command)
    {
        CommitPendingEdits();
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void OnGotKeyboardFocusForHistory(object sender, KeyboardFocusChangedEventArgs e)
    {
        _lastTextKey = TextKey.None;
        switch (e.NewFocus)
        {
            case MarkupTextBox line: line.KeepsOwnHistory = false; break;
            case TextBox { IsUndoEnabled: true } box: box.IsUndoEnabled = false; break;
        }
    }

    // ── Where a step ends ───────────────────────────────────────────────

    private bool _stepQueued;

    /// <summary>
    /// A step, if the pack changed, once what the mouse or the key did has been done: the click's own
    /// handlers, the bindings, the layout. The pack is looked at whatever has been said: a click that takes a
    /// row out of a list, or adds one, says nothing - no view model's value changed - and a step that waits
    /// to be asked for is joined to the next edit.
    /// </summary>
    private void StepOnceSettled(MainViewModel vm)
    {
        if (_stepQueued) return;
        _stepQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _stepQueued = false;
            vm.Undo.Checkpoint();
        }));
    }

    private enum TextKey { None, Typing, Deleting }

    private TextKey _lastTextKey;

    /// <summary>Typing in a box, cut into steps: a run of letters is one, a run of deleting another, and the
    /// caret moved by hand, a new line or a paste ends whichever was running.</summary>
    private void OnKeyDownForHistory(MainViewModel vm, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBoxBase) { _lastTextKey = TextKey.None; return; }
        Key key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        var held = Keyboard.Modifiers;
        bool ctrl = (held & ModifierKeys.Control) != 0;
        TextKey kind;
        bool ends;
        switch (key)
        {
            case Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
                 or Key.LWin or Key.RWin or Key.CapsLock or Key.NumLock or Key.Scroll or Key.None:
                return;
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown
                 or Key.Enter or Key.Tab or Key.Escape:
                kind = TextKey.None;
                ends = true;
                break;
            case Key.Back or Key.Delete:
                kind = TextKey.Deleting;
                ends = _lastTextKey != TextKey.Deleting;
                break;
            case Key.V or Key.X when ctrl:
            case Key.Insert when (held & ModifierKeys.Shift) != 0:
                kind = TextKey.None;
                ends = true;
                break;
            default:
                if (ctrl) return;      // a shortcut, not something typed
                kind = TextKey.Typing;
                ends = _lastTextKey == TextKey.Deleting;
                break;
        }
        _lastTextKey = kind;
        if (ends) vm.Undo.CheckpointIfTouched();
    }

    // ── The keyboard, kept where it was ─────────────────────────────────

    private sealed class KeyboardPlace
    {
        public WeakReference<FrameworkElement> Element = null!;
        public string Identity = "";
        public int Ordinal;
        public int Tab;
        public string? Text;
    }

    private KeyboardPlace? _keyboardWas;

    private DependencyObject? TabPage => MainTabs.Template?.FindName("PART_SelectedContentHost", MainTabs) as DependencyObject;

    /// <summary>The control an edit belongs to: a list for the box inside it.</summary>
    private static FrameworkElement OwnerOf(FrameworkElement focused)
        => focused is TextBox { TemplatedParent: ComboBox combo } ? combo : focused;

    private static string PathOf(FrameworkElement fe, DependencyProperty property)
        => BindingOperations.GetBindingBase(fe, property) is Binding b ? b.Path?.Path ?? "." : "";

    /// <summary>What a control is, as far as the page goes: its kind, its name, what it is bound to and the kind
    /// of record it shows - the same for the control that takes its place when the page is built again.</summary>
    private static string IdentityOf(FrameworkElement fe)
    {
        string path = fe switch
        {
            MarkupTextBox => PathOf(fe, MarkupTextBox.TextProperty),
            TextBox => PathOf(fe, TextBox.TextProperty),
            ComboBox => new[] { PathOf(fe, Selector.SelectedItemProperty), PathOf(fe, Selector.SelectedValueProperty), PathOf(fe, ComboBox.TextProperty), PathOf(fe, Selector.SelectedIndexProperty) }
                            .FirstOrDefault(p => p.Length > 0) ?? "",
            ToggleButton => PathOf(fe, ToggleButton.IsCheckedProperty),
            RangeBase => PathOf(fe, RangeBase.ValueProperty),
            ButtonBase => PathOf(fe, ButtonBase.CommandProperty),
            _ => "",
        };
        return fe.GetType().Name + "|" + fe.Name + "|" + path + "|" + (fe.DataContext?.GetType().Name ?? "");
    }

    private static IEnumerable<FrameworkElement> Under(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { IsVisible: false }) continue;
            if (child is FrameworkElement fe) yield return fe;
            foreach (var deeper in Under(child)) yield return deeper;
        }
    }

    private static bool IsUnder(DependencyObject d, DependencyObject root)
    {
        for (DependencyObject? p = d; p != null; p = p is Visual ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (ReferenceEquals(p, root)) return true;
        return false;
    }

    private void RememberKeyboard()
    {
        _keyboardWas = null;
        if (Keyboard.FocusedElement is not FrameworkElement focused || TabPage is not { } page || !IsUnder(focused, page)) return;
        var owner = OwnerOf(focused);
        string identity = IdentityOf(owner);
        int ordinal = 0;
        foreach (var fe in Under(page))
        {
            if (ReferenceEquals(fe, owner)) break;
            if (fe.GetType() == owner.GetType() && IdentityOf(fe) == identity) ordinal++;
        }
        _keyboardWas = new KeyboardPlace
        {
            Element = new WeakReference<FrameworkElement>(owner),
            Identity = identity,
            Ordinal = ordinal,
            Tab = MainTabs.SelectedIndex,
            Text = (focused as TextBox)?.Text,
        };
    }

    private void RestoreKeyboard()
    {
        var was = _keyboardWas;
        _keyboardWas = null;
        // Once the page is laid out again: what was built for the step that was put back is on it by then.
        var page = _pageBefore;
        _pageBefore = null;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            ListsShowWhatIsBound();
            if (was != null) PutKeyboardBack(was);
            if (page != null) MarkWhatChanged(page);
        }));
    }

    // ── What the step changed, marked ───────────────────────────────────

    private List<string>? _pageBefore;

    /// <summary>The boxes, lists, ticks and sliders the last undo or redo marked - for tests.</summary>
    internal List<FrameworkElement> LastMarked { get; } = new();

    private static bool Shows(FrameworkElement fe)
        => fe is MarkupTextBox { HidesTags: false } or TextBox { TemplatedParent: not ComboBox } or ComboBox or Slider
           || (fe is ToggleButton && fe.TemplatedParent is not Control);

    private static string ShownBy(FrameworkElement fe) => fe switch
    {
        MarkupTextBox m => m.Text,
        TextBox b => b.Text,
        ComboBox { IsEditable: true } c => c.Text,
        ComboBox c => c.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + c.Text,
        Slider s => Math.Round(s.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ToggleButton g => g.IsChecked?.ToString() ?? "",
        _ => "",
    };

    /// <summary>Every control of the page that shows a value, in the page's order.</summary>
    private List<FrameworkElement> ValuesOnPage()
        => TabPage is { } page ? Under(page).Where(fe => fe.IsEnabled && Shows(fe)).ToList() : new List<FrameworkElement>();

    /// <summary>
    /// What the page shows, as an undo or redo is about to put its step back: the tab and the record the step
    /// was made on are up by now (MainViewModel.OnUndoRestore), laid out here if they were not on screen.
    /// </summary>
    private void NotePage()
    {
        MainTabs.UpdateLayout();
        _pageBefore = ValuesOnPage().Select(fe => IdentityOf(fe) + "=" + ShownBy(fe)).ToList();
    }

    /// <summary>
    /// A slight glow on what the step changed (the author, 1.10.0: "Undoing/redoing should show the user
    /// what's actually being undone/redone, maybe even adding a very slight highlight to the field"): the
    /// controls of the page that show another value than before the step was put back, or were not there. The
    /// two pages are held side by side in their order, so a row that came back marks that row and not every
    /// row after it. A page that is another altogether (a record that did not exist) marks nothing: what is
    /// picked says it.
    /// </summary>
    private void MarkWhatChanged(List<string> before)
    {
        LastMarked.Clear();
        var now = ValuesOnPage();
        var keys = now.Select(fe => IdentityOf(fe) + "=" + ShownBy(fe)).ToList();
        // What the two pages start and end with alike is set aside: nearly all of both, nearly always.
        int head = 0;
        while (head < before.Count && head < keys.Count && before[head] == keys[head]) head++;
        int tail = 0;
        while (tail < before.Count - head && tail < keys.Count - head && before[before.Count - 1 - tail] == keys[keys.Count - 1 - tail]) tail++;
        int n = before.Count - head - tail, m = keys.Count - head - tail;
        if (m == 0 || (long)n * m > 1_000_000) return;
        // Of the rest, the longest run of controls the two share, in order: what is left over on this page changed.
        var same = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                same[i, j] = before[head + i] == keys[head + j] ? same[i + 1, j + 1] + 1 : Math.Max(same[i + 1, j], same[i, j + 1]);
        var changed = new List<FrameworkElement>();
        for (int i = 0, j = 0; j < m;)
        {
            if (i < n && before[head + i] == keys[head + j]) { i++; j++; }
            else if (i < n && same[i + 1, j] >= same[i, j + 1]) i++;
            else changed.Add(now[head + j++]);
        }
        if (changed.Count == 0 || changed.Count > 12) return;
        changed[0].BringIntoView();
        foreach (var fe in changed) Glimmer(fe);
        LastMarked.AddRange(changed);
    }

    /// <summary>A soft glow in the theme's accent, gone in under a second.</summary>
    private void Glimmer(UIElement target)
    {
        var accent = TryFindResource(Services.ThemeManager.KeyAccent) as SolidColorBrush;
        var before = target.Effect;
        var glow = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = accent?.Color ?? Color.FromRgb(0x4E, 0xC9, 0xF0),
            ShadowDepth = 0,
            Opacity = 0.75,
            BlurRadius = 0,
        };
        target.Effect = glow;
        var fade = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop };
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(14, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(14, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.35))));
        fade.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
        fade.Completed += (_, _) => { if (ReferenceEquals(target.Effect, glow)) target.Effect = before; };
        glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, fade);
    }

    /// <summary>
    /// A list showing a pick the pack no longer has, after a step was put back: emptied. A list is told what
    /// is picked by the record it shows, and WPF's lists ignore being told something that is not one of their
    /// entries - nothing, most of all. So a pick taken back to nothing stayed on screen: the default outfit
    /// picked, Ctrl+Z, and the box still named the outfit, as if the undo had not happened (1.10.0, found by
    /// the walk over every control).
    /// </summary>
    private void ListsShowWhatIsBound()
    {
        if (TabPage is not { } page) return;
        foreach (var combo in Under(page).OfType<ComboBox>().ToList())
        {
            if (combo.SelectedItem == null) continue;
            var be = combo.GetBindingExpression(Selector.SelectedItemProperty);
            if (be == null || be.Status != BindingStatus.Active || be.ParentBinding.Converter != null) continue;
            if (be.ResolvedSource is not { } source || string.IsNullOrEmpty(be.ResolvedSourcePropertyName)) continue;
            object? bound;
            try { bound = source.GetType().GetProperty(be.ResolvedSourcePropertyName)?.GetValue(source); }
            catch (System.Reflection.TargetInvocationException) { continue; }
            if (Equals(bound, combo.SelectedItem)) continue;
            // One of its entries after all: told again.
            if (bound != null && combo.Items.Contains(bound)) { be.UpdateTarget(); continue; }
            // Emptied without the record hearing of it: it already says what it means.
            var binding = be.ParentBinding;
            BindingOperations.ClearBinding(combo, Selector.SelectedItemProperty);
            combo.SelectedIndex = -1;
            if (combo.IsEditable) combo.Text = bound as string ?? "";
            combo.SetBinding(Selector.SelectedItemProperty, binding);
        }
    }

    private void PutKeyboardBack(KeyboardPlace was)
    {
        if (MainTabs.SelectedIndex != was.Tab || TabPage is not { } page) return;
        FrameworkElement? target = null;
        if (was.Element.TryGetTarget(out var same) && same.IsVisible && same.IsEnabled && IsUnder(same, page)) target = same;
        else
        {
            int seen = 0;
            foreach (var fe in Under(page))
            {
                if (IdentityOf(fe) != was.Identity) continue;
                if (seen++ == was.Ordinal) { target = fe; break; }
            }
        }
        if (target == null || !target.IsEnabled) return;
        if (!target.IsKeyboardFocusWithin)
        {
            FrameworkElement to = target is ComboBox { IsEditable: true } combo && combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox inner ? inner : target;
            if (!to.Focusable) return;
            to.Focus();
            Keyboard.Focus(to);
        }
        // The caret where the change was, as a box of Windows' own leaves it after its own undo.
        if (Keyboard.FocusedElement is TextBox box && was.Text != null && box.Text != was.Text)
            box.CaretIndex = Math.Clamp(MarkupTextBox.CaretFor(box.Text, was.Text), 0, box.Text.Length);
    }
}
