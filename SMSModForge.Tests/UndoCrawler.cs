using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Walks what can be edited on the tab that is open - every box, list, tick,
/// slider and button the page shows - and holds undo and redo to each of them
/// (1.10.0, the author: "every single action I take within the modpack can be
/// undone and redone"). Each is edited the way a person edits it, and then:
/// <list type="bullet">
/// <item><b>Ctrl+Z where the keyboard is</b> - still in the box that was typed
/// in - takes back exactly that edit: the pack is what it was, the history as
/// deep as it was, and every box on the page shows what it showed.</item>
/// <item><b>Ctrl+Y</b> brings it back: the pack as edited, the page as it was
/// after the edit.</item>
/// <item><b>Left, come back to, Ctrl+Z</b>: the same again - a box's own
/// memory of what was typed in it must not answer in place of the editor's.</item>
/// <item><b>Edited again</b>, the pack changes again: what is on screen after
/// an undo is still the pack's, not a record the undo replaced (the lights'
/// panel beside the place, 1.10.0, was).</item>
/// </list>
/// Nothing here knows what a control is for. The keys are pressed as WPF
/// routes them: to the box with the keyboard first, when it says it can undo,
/// and to the window's own Ctrl+Z otherwise.
/// </summary>
internal sealed class UndoCrawler
{
    public sealed class Target
    {
        public FrameworkElement Control = null!;
        public string Kind = "";
        public string Signature = "";
    }

    private readonly MainWindow _window;
    private readonly MainViewModel _vm;
    private readonly TabControl _tabs;
    private readonly ITestOutputHelper _out;
    private readonly string _dir;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private Action _setup = () => { };
    private string _where = "";
    private int _tab = -1;

    public readonly List<string> Failures = new();
    public int Edited, Unchanged, Skipped;
    /// <summary>How each undo was put back (MainViewModel.LastRestore), counted.</summary>
    public readonly SortedDictionary<string, int> Ways = new(StringComparer.Ordinal);
    /// <summary>Buttons not to press: what leaves the pack or the editor (saving, exporting, another pack).</summary>
    public Func<Target, bool> Leave = _ => false;
    /// <summary>Only list what would be edited.</summary>
    public bool CensusOnly;

    // A press that never comes back is a window nobody can click (CLAUDE.md): said, and the run ended, rather
    // than left to look like a hang.
    private volatile string? _busyWith;
    private DateTime _busySince;
    private readonly System.Threading.Timer _watch;

    public UndoCrawler(MainWindow window, string dir, ITestOutputHelper output)
    {
        _window = window;
        _vm = (MainViewModel)window.DataContext;
        _tabs = (TabControl)window.FindName("MainTabs");
        _dir = dir;
        _out = output;
        _watch = new System.Threading.Timer(_ =>
        {
            string? busy = _busyWith;
            if (busy != null && DateTime.UtcNow - _busySince > TimeSpan.FromSeconds(120))
                Environment.FailFast("UndoCrawler: no answer for two minutes from: " + busy);
        }, null, 5000, 5000);
    }

    public void Done() => _watch.Dispose();

    /// <summary>The window walked: for a walk that picks something in it as a person clicks (the place preview).</summary>
    public MainWindow Window => _window;

    private static void Pump()
    {
        WindowHarness.Pump();
        // Whether a button can be pressed is asked again at every move of the mouse and press of a key; here
        // there are none, and a button greyed while a list was built again would stay grey.
        CommandManager.InvalidateRequerySuggested();
        WindowHarness.Pump();
        // And what the editor leaves for when nothing else is waiting (a list of names filled in late).
        var idle = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => idle.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(idle);
    }

    private string State() => PackRepository.Serialize(_vm.Pack);

    // ── What is on the page ─────────────────────────────────────────────

    public List<Target> Targets()
    {
        var list = new List<Target>();
        var host = _tabs.Template?.FindName("PART_SelectedContentHost", _tabs) as DependencyObject;
        if (host != null) Walk(host, list);
        return list;
    }

    private void Walk(DependencyObject d, List<Target> into)
    {
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is UIElement u && !u.IsVisible) continue;
            if (child is FrameworkElement fe)
            {
                if (!fe.IsEnabled) continue;
                string? kind = KindOf(fe, out bool into2);
                if (kind != null) { into.Add(new Target { Control = fe, Kind = kind, Signature = SignatureOf(fe, kind) }); continue; }
                if (!into2) continue;
            }
            Walk(child, into);
        }
    }

    /// <summary>What a control is to edit, or null - and then whether what is inside it is looked at.</summary>
    private static string? KindOf(FrameworkElement fe, out bool lookInside)
    {
        lookInside = false;
        switch (fe)
        {
            case MarkupTextBox m: return m.IsReadOnly || m.HidesTags ? null : "markup";
            case TextBox t: return t.IsReadOnly || t.TemplatedParent is ComboBox ? null : "text";
            case TextBoxBase: return null;
            case ComboBox: return "combo";
            case Slider: return "slider";
            case ScrollBar or Thumb or GridSplitter or RepeatButton: return null;
            case RadioButton r: return r.TemplatedParent is Control ? null : "radio";
            case ToggleButton t: return t.TemplatedParent is Control ? null : "toggle";
            case ButtonBase b: return b.TemplatedParent is Control ? null : "button";
        }
        lookInside = true;
        return null;
    }

    private static string PathOf(FrameworkElement fe, DependencyProperty property)
    {
        var binding = BindingOperations.GetBindingBase(fe, property);
        return binding switch
        {
            Binding b => b.Path?.Path ?? ".",
            null => "",
            _ => binding.GetType().Name,
        };
    }

    private static string WordsOf(FrameworkElement fe)
    {
        object? content = fe is ContentControl c ? c.Content : null;
        string words = content switch
        {
            string s => s,
            TextBlock t => t.Text,
            Panel p => string.Join(" ", p.Children.OfType<TextBlock>().Select(t => t.Text)),
            _ => "",
        };
        string tip = fe.ToolTip as string ?? (fe.ToolTip as ToolTip)?.Content as string ?? "";
        if (tip.Length > 40) tip = tip.Substring(0, 40);
        return words + (tip.Length > 0 ? " (" + tip + ")" : "");
    }

    private static string SignatureOf(FrameworkElement fe, string kind)
    {
        var sb = new StringBuilder(kind);
        if (!string.IsNullOrEmpty(fe.Name)) sb.Append(" #").Append(fe.Name);
        string path = kind switch
        {
            "text" => PathOf(fe, TextBox.TextProperty),
            "markup" => PathOf(fe, MarkupTextBox.TextProperty),
            "combo" => new[] { PathOf(fe, Selector.SelectedItemProperty), PathOf(fe, Selector.SelectedValueProperty), PathOf(fe, ComboBox.TextProperty), PathOf(fe, Selector.SelectedIndexProperty) }
                           .FirstOrDefault(p => p.Length > 0) ?? "",
            "toggle" or "radio" => PathOf(fe, ToggleButton.IsCheckedProperty),
            "slider" => PathOf(fe, RangeBase.ValueProperty),
            _ => PathOf(fe, ButtonBase.CommandProperty),
        };
        sb.Append(" [").Append(path).Append(']');
        object? dc = fe.DataContext;
        if (dc != null)
        {
            sb.Append(" of ").Append(dc.GetType().Name);
            object? type = null;
            try { type = dc.GetType().GetProperty("Type")?.GetValue(dc); } catch (TargetInvocationException) { }
            if (type is string or Enum) sb.Append('<').Append(type).Append('>');
            if (dc is ParamRowViewModel row) sb.Append(' ').Append(row.Key);
        }
        if (kind is "button" or "toggle" or "radio") sb.Append(" \"").Append(WordsOf(fe)).Append('"');
        return sb.ToString();
    }

    private static string Shown(Target t) => t.Control switch
    {
        MarkupTextBox m => m.Text,
        TextBox b => b.Text,
        ComboBox { IsEditable: true } c => c.Text,
        ComboBox c => c.SelectedIndex.ToString(CultureInfo.InvariantCulture) + "|" + c.Text,
        Slider s => Math.Round(s.Value, 4).ToString(CultureInfo.InvariantCulture),
        ToggleButton g => g.IsChecked?.ToString() ?? "null",
        _ => "",
    };

    private static List<string> Snap(List<Target> targets) => targets.Select(t => t.Signature + " = " + Shown(t)).ToList();

    private Target? Find(string signature) => Targets().FirstOrDefault(t => t.Signature == signature);

    // ── The page held still ─────────────────────────────────────────────

    /// <summary>Where a control was drawn in the window as a key was pressed, and whether a person saw it there.</summary>
    private sealed class Sight
    {
        public bool Seen;
        public Point? At;
        public string Scrolled = "";
        public List<(double Offset, double Range)> Scrolls = new();
        public string Above = "";
    }

    /// <summary>What is laid out before the control, as far as the scrolled area it is in: each panel on the way
    /// up, and the size of what comes before it there - what grew or shrank when the control was moved.</summary>
    private static string AboveOf(FrameworkElement? fe)
    {
        var parts = new List<string>();
        DependencyObject? child = fe;
        for (var at = fe == null ? null : VisualTreeHelper.GetParent(fe); at != null && at is not ScrollContentPresenter; child = at, at = VisualTreeHelper.GetParent(at))
        {
            if (at is not Panel panel) continue;
            var before = new List<string>();
            foreach (UIElement c in panel.Children)
            {
                if (ReferenceEquals(c, child)) break;
                if (c is FrameworkElement f && f.IsVisible) before.Add(f.GetType().Name + " " + f.ActualHeight.ToString("0", CultureInfo.InvariantCulture));
            }
            if (before.Count > 0) parts.Insert(0, panel.GetType().Name + ": " + string.Join(", ", before));
        }
        return string.Join(" | ", parts);
    }

    /// <summary>How far each scrolled area the control is in is scrolled, and how far it can be, outermost first.</summary>
    private static List<(double Offset, double Range)> ScrollsOf(FrameworkElement? fe)
    {
        var scrolls = new List<(double, double)>();
        for (DependencyObject? at = fe == null ? null : VisualTreeHelper.GetParent(fe); at != null; at = VisualTreeHelper.GetParent(at))
            if (at is ScrollViewer sv) scrolls.Insert(0, (sv.VerticalOffset, sv.ScrollableHeight));
        return scrolls;
    }

    private static string ScrolledAt(FrameworkElement? fe)
        => string.Join(" > ", ScrollsOf(fe).Where(s => s.Range > 0).Select(s => s.Offset.ToString("0", CultureInfo.InvariantCulture) + "/" + s.Range.ToString("0", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Nothing was scrolled by the press: each area is where it was, or came up only as far as its content now
    /// ends (what was below the view went away). A control drawn elsewhere then is the step's own doing - a line
    /// above it that the value shows or hides ("differs from the game's") - not the page taken somewhere.
    /// </summary>
    private static bool NotScrolled(List<(double Offset, double Range)> pressed, List<(double Offset, double Range)> after)
    {
        if (pressed.Count != after.Count) return false;
        for (int i = 0; i < pressed.Count; i++)
        {
            if (Math.Abs(pressed[i].Offset - after[i].Offset) < 0.5) continue;
            if (after[i].Offset < pressed[i].Offset && Math.Abs(after[i].Offset - after[i].Range) < 0.5) continue;
            return false;
        }
        return true;
    }

    private Point? At(FrameworkElement? fe)
        => fe != null && fe.IsVisible && PresentationSource.FromVisual(fe) != null && fe.IsDescendantOf(_window) ? fe.TranslatePoint(new Point(0, 0), _window) : null;

    /// <summary>The control being held, as it was before it was edited.</summary>
    private Sight? _untouched;

    private Sight Look(FrameworkElement? fe) => new() { Seen = fe != null && _window.InView(fe), At = At(fe), Scrolled = ScrolledAt(fe), Scrolls = ScrollsOf(fe), Above = AboveOf(fe) };

    private static bool Near(Point? a, Point? b) => a is { } p && b is { } q && Math.Abs(p.X - q.X) < 1.5 && Math.Abs(p.Y - q.Y) < 1.5;

    /// <summary>The control itself while it is on the page, else the one of its signature that took its place.</summary>
    private FrameworkElement? Alive(FrameworkElement fe, string signature)
        => fe.IsVisible && PresentationSource.FromVisual(fe) != null ? fe : Find(signature)?.Control;

    /// <summary>
    /// The page where it was: the control edited - on screen as the key was pressed, showing what the step
    /// changes - drawn where it was then, or where the edit or the step itself put it (the rows it changed above
    /// it). An undo that moved the page from what it was changing in front of the author was the author's
    /// finding after 1.10.0: Ctrl+Z on an object dragged in the place preview "brings into view the fields for
    /// the GOs at the bottom instead of staying on the preview". A button shows nothing of what it changes; its
    /// step may go to what it changed.
    /// </summary>
    private string? Held(Target t, Sight pressed, FrameworkElement? after, params Point?[] alsoRight)
    {
        if (t.Kind == "button" || !pressed.Seen || pressed.At is not { } was) return null;
        if (At(after) is not { } now) return null;   // gone from the page: the page's own check says so
        if (Near(now, was) || alsoRight.Any(r => Near(now, r))) return null;
        if (NotScrolled(pressed.Scrolls, ScrollsOf(after))) return null;
        return $"the page moved: the control, on screen as the key was pressed, was drawn {was.Y:0} px from the window's top and is drawn {now.Y:0} px from it"
               + (_window.InView(after!) ? "" : ", out of sight")
               + (alsoRight.Length > 0 ? " (it was drawn at " + string.Join(" and ", alsoRight.Select(r => r is { } p ? p.Y.ToString("0", CultureInfo.InvariantCulture) : "nowhere")) + " before and once edited)" : "")
               + $"; scrolled {pressed.Scrolled} as the key was pressed, {ScrolledAt(after)} after"
               + (AboveOf(after) is var above && above != pressed.Above ? $"; before it as the key was pressed: {pressed.Above}; after: {above}" + (_untouched != null && _untouched.Above != pressed.Above ? $"; before the edit: {_untouched.Above}, scrolled {_untouched.Scrolled}" : "") : "")
               + "; marked: " + string.Join(", ", _window.LastMarked.Select(m => m.GetType().Name + (_window.InView(m) ? "" : " (out of sight)")));
    }

    // ── What the page shows must be the pack's ──────────────────────────

    private static readonly Dictionary<Type, PropertyInfo[]> Props = new();

    private HashSet<object> PackObjects()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var todo = new Stack<object>();
        todo.Push(_vm.Pack);
        while (todo.Count > 0)
        {
            object o = todo.Pop();
            if (o is string) continue;
            if (o is IDictionary map) { foreach (object? v in map.Values) if (v != null) todo.Push(v); continue; }
            if (o is IEnumerable list) { foreach (object? v in list) if (v != null && !v.GetType().IsValueType) todo.Push(v); continue; }
            var type = o.GetType();
            if (type.IsValueType || type.Namespace == null || !type.Namespace.StartsWith("SMSModForge", StringComparison.Ordinal)) continue;
            if (!seen.Add(o)) continue;
            if (!Props.TryGetValue(type, out var props))
                Props[type] = props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !p.PropertyType.IsValueType && p.PropertyType != typeof(string)).ToArray();
            foreach (var p in props)
            {
                object? v;
                try { v = p.GetValue(o); } catch (TargetInvocationException) { continue; }
                if (v != null) todo.Push(v);
            }
        }
        return seen;
    }

    /// <summary>The controls showing a record (their view's Model) that is not one of the pack's.</summary>
    private HashSet<string> NotOfThePack(List<Target> targets)
    {
        var live = PackObjects();
        var dead = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in targets)
        {
            object? dc = t.Control.DataContext;
            if (dc == null || dc is MainViewModel) continue;
            object? model = null;
            try { model = dc.GetType().GetProperty("Model")?.GetValue(dc); } catch (TargetInvocationException) { }
            if (model == null || model.GetType().IsValueType || model is string) continue;
            if (!live.Contains(model)) dead.Add(t.Signature);
        }
        return dead;
    }

    // ── The keys, as WPF routes them ────────────────────────────────────

    private static readonly MethodInfo CommitPending = typeof(MainWindow).GetMethod("CommitPendingEdits", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Click = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>How many presses a box answered itself, with its own memory of what was typed in it.</summary>
    public int AnsweredByTheBox;

    /// <summary>What the last press left the pack as before the page settled, when settling changed it: an undo
    /// that was right and was then written over by what was on screen.</summary>
    private string _settling = "";

    private void Press(Key key)
    {
        _settling = "";
        string who = (Keyboard.FocusedElement as FrameworkElement)?.GetType().Name ?? "nothing";
        int back = _vm.Undo.Depth, forward = _vm.Undo.RedoDepth;
        // A box that remembers its own typing takes the key itself, behind the editor's back.
        bool theBoxWould = Keyboard.FocusedElement switch
        {
            SMSModForge.View.Controls.MarkupTextBox line => key == Key.Z ? line.CanUndoLine : line.CanRedoLine,
            TextBoxBase box => key == Key.Z ? box.CanUndo : box.CanRedo,
            _ => false,
        };
        if (theBoxWould)
        {
            AnsweredByTheBox++;
            _settling = " (the press was answered by the " + who + " with the keyboard, not by the editor)";
        }
        // The key itself, down WPF's own route from whatever has the keyboard (RealKeys): running the window's
        // shortcut by hand said nothing about whether the key got to it - and from a text box it did not.
        RealKeys.Press(_window, key, ModifierKeys.Control);
        if (!theBoxWould && back == _vm.Undo.Depth && forward == _vm.Undo.RedoDepth)
            _settling = " (the editor had nothing to " + (key == Key.Z ? "undo" : "redo") + ", or the key never reached it)";
        _settling += $" (history before the press: {back} back, {forward} forward; after: {_vm.Undo.Depth} back, {_vm.Undo.RedoDepth} forward)";
        string straight = State();
        Pump();
        string settled = State();
        if (straight != settled) _settling += " (right as the press returned, then changed as the page settled: " + FirstDifference(straight, settled) + "; the keyboard was in a " + who + ")";
    }

    /// <summary>The keyboard off the control and somewhere else in the window (RealKeys.Elsewhere).</summary>
    private void LeaveTheControl()
    {
        RealKeys.Elsewhere(_window);
        Pump();
    }

    private static void Focus(FrameworkElement fe)
    {
        if (fe is ComboBox { IsEditable: true } combo && combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox inner) fe = inner;
        if (!fe.Focusable) return;
        fe.Focus();
        Keyboard.Focus(fe);
    }

    // ── Editing, as a person does ───────────────────────────────────────

    private static readonly Regex Colour = new("^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", RegexOptions.Compiled);

    private static string Another(string text)
    {
        if (Colour.IsMatch(text))
        {
            string next = text.StartsWith("#336699", StringComparison.OrdinalIgnoreCase) ? "#996633" : "#336699";
            return text.Length == 9 ? next + text.Substring(7) : next;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !text.Contains('e') && !text.Contains('E'))
        {
            int decimals = text.Contains('.') ? text.Length - text.IndexOf('.') - 1 : 0;
            return (number + 1).ToString("F" + decimals, CultureInfo.InvariantCulture);
        }
        return text + "x";
    }

    /// <summary>Edit the control; false when there is nothing to change it to.</summary>
    private bool Edit(Target t)
    {
        switch (t.Control)
        {
            case MarkupTextBox m:
                Focus(m);
                m.CaretPosition = m.Document.ContentEnd;
                m.CaretPosition.InsertTextInRun("x");
                return true;
            case TextBox b:
                Focus(b);
                b.SelectAll();
                b.SelectedText = Another(b.Text);
                return true;
            case ComboBox c:
            {
                if (c.Items.Count < 2 && !c.IsEditable)
                {
                    // A list filled as it opens.
                    WindowHarness.SetOpen(c, true);
                    WindowHarness.SetOpen(c, false);
                }
                int count = c.Items.Count;
                if (count >= 2)
                {
                    int next = -1;
                    for (int k = 1; k < count && next < 0; k++)
                    {
                        int i = (Math.Max(c.SelectedIndex, 0) + k) % count;
                        object? item = c.Items[i];
                        if (item is Separator || item is ComboBoxItem { IsEnabled: false } || i == c.SelectedIndex) continue;
                        next = i;
                    }
                    if (next < 0) return false;
                    Focus(c);
                    c.SetCurrentValue(Selector.SelectedIndexProperty, next);
                    return true;
                }
                if (c.IsEditable && c.Template?.FindName("PART_EditableTextBox", c) is TextBox inner)
                {
                    Focus(c);
                    inner.SelectAll();
                    inner.SelectedText = Another(inner.Text);
                    return true;
                }
                return false;
            }
            case Slider s:
            {
                double span = s.Maximum - s.Minimum;
                if (span <= 0) return false;
                double to = s.Minimum + span * ((s.Value - s.Minimum) / span < 0.5 ? 0.75 : 0.25);
                if (s.IsSnapToTickEnabled && s.TickFrequency > 0) to = s.Minimum + Math.Round((to - s.Minimum) / s.TickFrequency) * s.TickFrequency;
                if (Math.Abs(to - s.Value) < 1e-9) return false;
                Focus(s);
                // The thumb dragged - its value moving through the binding - and let go.
                s.SetCurrentValue(RangeBase.ValueProperty, (s.Value + to) / 2);
                s.SetCurrentValue(RangeBase.ValueProperty, to);
                s.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                return true;
            }
            case RadioButton r:
                if (r.IsChecked == true) return false;
                Focus(r);
                Click.Invoke(r, null);
                return true;
            case ButtonBase b:
                Focus(b);
                Click.Invoke(b, null);
                return true;
        }
        return false;
    }

    /// <summary>A control that edits nothing of the pack, back to what it showed: what it chose was the
    /// page's to forget, and the next control is held to the page as it was.</summary>
    private void PutBack(Target t, string shown)
    {
        if (!t.Control.IsVisible || Shown(t) == shown) return;
        switch (t.Control)
        {
            case TextBox b: b.SelectAll(); b.SelectedText = shown; break;
            case ComboBox { IsEditable: false } c when int.TryParse(shown.Split('|')[0], out int was) && was < c.Items.Count:
                c.SetCurrentValue(Selector.SelectedIndexProperty, was);
                break;
            case Slider s when double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out double value):
                s.SetCurrentValue(RangeBase.ValueProperty, value);
                break;
            case ToggleButton g and not RadioButton: Click.Invoke(g, null); break;
        }
        Pump();
    }

    private void Commit()
    {
        CommitPending.Invoke(null, null);
        Pump();
    }

    // ── The walk ────────────────────────────────────────────────────────

    /// <summary>Open the tab, pick what <paramref name="select"/> picks, and hold undo to everything the page
    /// then shows that has not been held to it already on this crawl.</summary>
    public void Explore(int tab, string label, Action select)
    {
        _setup = () =>
        {
            _tabs.SelectedIndex = tab;
            Pump();
            select();
            Pump();
            OpenEveryFold();
            _window.UpdateLayout();
            Pump();
        };
        _where = "tab " + tab + ", " + label;
        _tab = tab;
        _setup();
        for (int guard = 0; guard < 800 && Failures.Count < 80; guard++)
        {
            // A button that showed another tab: back to the page being walked.
            if (_tabs.SelectedIndex != tab) _setup();
            var targets = Targets();
            var next = targets.FirstOrDefault(t => !_done.Contains(t.Signature));
            if (next == null) break;
            _done.Add(next.Signature);
            if (CensusOnly) { _out.WriteLine(_where + ": " + next.Signature + " = " + Shown(next)); continue; }
            if (Leave(next)) { Skipped++; continue; }
            _busyWith = _where + ": " + next.Signature;
            _busySince = DateTime.UtcNow;
            try { Hold(next, targets); }
            finally { _busyWith = null; }
        }
    }

    /// <summary>What has not been met yet on the page as it is now - for a walk that sets the page up itself.</summary>
    public void ExploreHere(string label)
    {
        var before = _setup;
        string where = _where;
        _setup = () => { };
        _where = where + ", " + label;
        try
        {
            for (int guard = 0; guard < 400 && Failures.Count < 80; guard++)
            {
                var targets = Targets();
                var next = targets.FirstOrDefault(t => !_done.Contains(t.Signature));
                if (next == null) break;
                _done.Add(next.Signature);
                if (CensusOnly) { _out.WriteLine(_where + ": " + next.Signature + " = " + Shown(next)); continue; }
                if (Leave(next)) { Skipped++; continue; }
                _busyWith = _where + ": " + next.Signature;
                _busySince = DateTime.UtcNow;
                try { Hold(next, targets); }
                finally { _busyWith = null; }
            }
        }
        finally { _setup = before; _where = where; }
    }

    private void OpenEveryFold()
    {
        for (int pass = 0; pass < 6; pass++)
        {
            bool any = false;
            var host = _tabs.Template?.FindName("PART_SelectedContentHost", _tabs) as DependencyObject;
            if (host == null) return;
            foreach (var fold in Descendants(host).OfType<Expander>().Where(e => e.IsVisible && !e.IsExpanded).ToList())
            {
                fold.SetCurrentValue(Expander.IsExpandedProperty, true);
                any = true;
            }
            if (!any) return;
            Pump();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static string FirstDifference(string a, string b)
    {
        var la = a.Split('\n');
        var lb = b.Split('\n');
        for (int i = 0; i < Math.Min(la.Length, lb.Length); i++)
            if (la[i] != lb[i]) return $"line {i + 1}: \"{Short(la[i].Trim())}\" is \"{Short(lb[i].Trim())}\"";
        return la.Length == lb.Length ? "nothing" : $"{la.Length} lines are {lb.Length}";
    }

    private static string FirstDifference(List<string> a, List<string> b)
    {
        // And everything that went and came: the first difference alone says where, not what.
        string Whole()
        {
            var gone = a.Except(b).Take(8).Select(Short).ToList();
            var came = b.Except(a).Take(8).Select(Short).ToList();
            return (gone.Count > 0 ? "; gone: " + string.Join(" | ", gone) : "") + (came.Count > 0 ? "; new: " + string.Join(" | ", came) : "");
        }
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (a[i] != b[i]) return $"\"{Short(a[i])}\" is \"{Short(b[i])}\"" + Whole();
        return a.Count == b.Count ? "nothing" : $"{a.Count} controls are {b.Count}" + (a.Count > b.Count ? " (gone: " + Short(a[b.Count]) + ")" : " (new: " + Short(b[a.Count]) + ")") + Whole();
    }

    private static string Short(string s) => s.Length <= 150 ? s : s.Substring(0, 150) + "...";

    private sealed class Was
    {
        public string State = "";
        public int Depth;
        public List<string> Page = new();
        public HashSet<string> NotOfThePack = new();
    }

    private Was Now(List<Target>? targets = null)
    {
        targets ??= Targets();
        return new Was { State = State(), Depth = _vm.Undo.Depth, Page = Snap(targets), NotOfThePack = NotOfThePack(targets) };
    }

    /// <summary>Hold the editor to <paramref name="was"/>; what differs, or null.</summary>
    private string? Differs(Was was, bool page = true)
    {
        string state = State();
        if (state != was.State) return "the pack differs: " + FirstDifference(was.State, state);
        if (_vm.Undo.Depth != was.Depth) return $"the history is {_vm.Undo.Depth} steps deep, was {was.Depth}";
        var targets = Targets();
        if (page)
        {
            var snap = Snap(targets);
            if (!snap.SequenceEqual(was.Page)) return "the page differs: " + FirstDifference(was.Page, snap);
        }
        var dead = NotOfThePack(targets);
        dead.ExceptWith(was.NotOfThePack);
        if (dead.Count > 0) return "shows a record that is no longer the pack's: " + Short(dead.First());
        return null;
    }

    private void Fail(Target t, string what)
    {
        string line = $"{_where}: {t.Signature}: {what}{_settling} [put back by: {_vm.LastRestore}]";
        Failures.Add(line);
        _out.WriteLine("FAILED  " + line);
        Recover();
    }

    /// <summary>The pack as it is on disk, and the page as the walk set it up.</summary>
    public void Recover()
    {
        LeaveTheControl();
        typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_vm, null);
        _vm.OpenPackFromPath(_dir);
        Pump();
        _setup();
    }

    private void Hold(Target t, List<Target> targets)
    {
        // Whatever the walk itself did to get here is behind it.
        _vm.Undo.Checkpoint();
        var before = Now(targets);
        string shown = Shown(t);
        // Where it is once it has the keyboard, as a person sees it when they start editing it.
        if (t.Kind != "button") { Focus(t.Control); Pump(); }
        var untouched = _untouched = Look(t.Control);

        if (!Edit(t)) { Skipped++; return; }
        Pump();
        Commit();
        string after = State();
        if (after == before.State)
        {
            // Not an edit of the pack: a view opened, something picked. No step may have been taken for it.
            PutBack(t, shown);
            LeaveTheControl();
            _vm.Undo.Checkpoint();
            Unchanged++;
            if (_vm.Undo.Depth != before.Depth) Fail(t, $"nothing of the pack changed, and the history grew by {_vm.Undo.Depth - before.Depth}");
            else _out.WriteLine("        " + _where + ": " + t.Signature + ": not an edit of the pack");
            return;
        }
        Edited++;
        var edited = Now();
        edited.Depth = before.Depth + 1;
        // The control as it is once edited: a list that picks what kind of row its row is, is that kind's now -
        // and a button that takes its own row away is gone.
        string editedSignature = t.Control.IsVisible && PresentationSource.FromVisual(t.Control) != null ? SignatureOf(t.Control, t.Kind) : t.Signature;
        string shownEdited = Find(editedSignature) is { } nowShown ? Shown(nowShown) : "";

        // 1. Ctrl+Z where the keyboard is.
        var editedControl = Alive(t.Control, editedSignature);
        var pressed = Look(editedControl);
        var onceEdited = pressed.At;
        Press(Key.Z);
        string way = _vm.LastRestore;
        string? wrong = Differs(before);
        wrong ??= Held(t, pressed, Alive(t.Control, t.Signature), untouched.At, onceEdited);
        if (wrong != null) { Fail(t, "Ctrl+Z straight after the edit: " + wrong); return; }

        // 2. Ctrl+Y: the pack as edited, and the control showing what it showed. (Not the whole page: a row
        // that was "just added" when the edit was made - its name not yet settled, say - is an ordinary row
        // once it has been put back.)
        var undone = Alive(t.Control, t.Signature);
        pressed = Look(undone);
        Press(Key.Y);
        wrong = Differs(edited, page: false);
        if (wrong == null && t.Kind != "button")
        {
            var redone = Find(editedSignature);
            if (redone == null) wrong = "the control is gone from the page";
            else if (Shown(redone) != shownEdited) wrong = $"it shows \"{Short(Shown(redone))}\", showed \"{Short(shownEdited)}\" once edited";
            else wrong = Held(t, pressed, Alive(undone ?? t.Control, editedSignature), untouched.At, onceEdited);
        }
        if (wrong != null) { Fail(t, "Ctrl+Y after that: " + wrong); return; }

        // 3. Left, come back to (where it is still there: a button that took its own row away is not), Ctrl+Z.
        LeaveTheControl();
        var again = Find(editedSignature);
        if (again == null && t.Kind != "button") { Fail(t, "after undo and redo the control is gone from the page"); return; }
        if (again != null) Focus(again.Control);
        Pump();
        pressed = Look(again?.Control);
        Press(Key.Z);
        wrong = Differs(before);
        if (wrong == null && again != null) wrong = Held(t, pressed, Alive(again.Control, t.Signature), untouched.At, onceEdited);
        if (wrong != null) { Fail(t, "left, come back to, Ctrl+Z: " + wrong); return; }

        // 4. Edited again: the pack changes again.
        again = Find(t.Signature);
        if (again == null) { Fail(t, "after the second undo the control is gone from the page"); return; }
        if (Shown(again) != shown) { Fail(t, $"after undo it shows \"{Short(Shown(again))}\", was \"{Short(shown)}\""); return; }
        var beforeAgain = Look(again.Control);
        if (!Edit(again)) { Fail(t, "after undo it cannot be edited again"); return; }
        Pump();
        Commit();
        if (State() == before.State) { Fail(t, "edited again after undo, nothing of the pack changed: what is on screen is not the pack's"); return; }
        LeaveTheControl();
        var editedAgain = Alive(again.Control, editedSignature);
        pressed = Look(editedAgain);
        Press(Key.Z);
        wrong = Differs(before, page: false);
        wrong ??= Held(t, pressed, Alive(editedAgain ?? again.Control, t.Signature), beforeAgain.At, untouched.At);
        if (wrong != null) { Fail(t, "the second edit undone with the keyboard elsewhere: " + wrong); return; }

        Ways[way] = Ways.TryGetValue(way, out int n) ? n + 1 : 1;
        _out.WriteLine("ok      " + _where + ": " + t.Signature + " [" + way + "]");
    }
}
