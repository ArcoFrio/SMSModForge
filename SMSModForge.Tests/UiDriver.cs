using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SMSModForge.Tutorials;
using SMSModForge.ViewModel;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The editor driven by what is written on it, as a reader of the guide drives it (1.10.0, the author: "We could also
/// have an agent attempt to follow some of the written tutorials on the Guides tab"). Every step names what is on the
/// screen - a tab, a button's words, the label beside a box, a tick's words, a row's text - and the driver finds it
/// the way a person looking at the window would: only what is shown, by its words and where it stands. No view model,
/// no names from the code: a step the screen does not answer is the guide's mistake, and fails with what the screen
/// does show (<see cref="UiDriverMiss"/>), the way a reader stuck on a step would look round for the words.
/// </summary>
internal sealed class UiDriver
{
    private readonly MainWindow _w;
    private readonly ITestOutputHelper? _log;

    public UiDriver(MainWindow window, ITestOutputHelper? log = null)
    {
        _w = window;
        _log = log;
        RealKeys.Watch(window);
    }

    // ── Getting ready: what the reader has before the first step ─────────────────────────────────────

    /// <summary>A new, empty pack in a folder, as New does, with the tutorials' practice pictures and sounds copied
    /// in (Resources/TutorialAssets: Locations/RoomB.png, Characters/..., Music/...), so a step can type the path of a
    /// picture of its own into a box. The pack's path names are relative to its folder.</summary>
    public void StartNewPack(string folder, string name = "Guide Test Pack")
    {
        var vm = (MainViewModel)_w.DataContext;
        if (!vm.MakePack(folder, name)) throw new InvalidOperationException("could not make a pack in " + folder);
        TutorialAssets.EnsureCopied(vm.PackRoot);
        if (_w.Width < 1400) { _w.Width = 1500; _w.Height = 940; }
        Settle();
        Note("a new pack at " + folder);
    }

    // ── What a reader does ───────────────────────────────────────────────────────────────────────────

    /// <summary>Open a tab of the window by its name ("Variables", "Dialogues").</summary>
    public void Tab(string name)
    {
        var tabs = (TabControl)_w.FindName("MainTabs");
        var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(t => Same(HeaderText(t.Header), name))
                  ?? tabs.Items.OfType<TabItem>().FirstOrDefault(t => Names(HeaderText(t.Header), name))
                  ?? throw Miss($"no tab called \"{name}\"", "tabs: " + string.Join(", ", tabs.Items.OfType<TabItem>().Select(t => HeaderText(t.Header))));
        tabs.SelectedItem = tab;
        Settle();
        Note($"tab {name}");
    }

    /// <summary>Click a button (or a radio button, a menu-less toggle) by its words - its text, else its tooltip -
    /// inside the group headed <paramref name="near"/> when given.</summary>
    public void Click(string label, string? near = null)
    {
        var button = Find<ButtonBase>(label, near, b => new[] { TextOf(b), Tip(b), AutomationProperties.GetName(b) })
                     ?? throw Miss($"no button \"{label}\"{Where(near)}", Screen());
        Press(button);
        Note($"click {label}{Where(near)}");
    }

    /// <summary>Open a folded section by the words of its heading, as a person clicks the heading of a shut one
    /// (an open one is left open). Returns whether it was shut.</summary>
    public bool Open(string heading)
    {
        var section = Shown(_w).OfType<Expander>().FirstOrDefault(e => Same(HeadingWords(e), heading) || Names(HeadingWords(e), heading))
                      ?? throw Miss($"no section headed \"{heading}\"", Screen());
        if (section.IsExpanded) { Note($"{heading} is already open"); return false; }
        var toggle = GuideSectionShots.Below(section).OfType<System.Windows.Controls.Primitives.ToggleButton>().FirstOrDefault(t => t.TemplatedParent == section);
        if (toggle != null) Press(toggle); else { section.IsExpanded = true; Settle(); }
        Note($"open {heading}");
        return true;
    }

    /// <summary>A heading's words as drawn: a header that is data (a list's group) is read off the part of the
    /// template that draws it.</summary>
    private static string HeadingWords(HeaderedContentControl h)
    {
        switch (h.Header)
        {
            case string s: return s;
            case TextBlock t: return Words(t);
            case FrameworkElement f: return TextOf(f);
        }
        var site = GuideSectionShots.Below(h).OfType<FrameworkElement>()
                       .FirstOrDefault(f => f.TemplatedParent == h && (f is System.Windows.Controls.Primitives.ToggleButton || f is ContentPresenter { ContentSource: "Header" }));
        return site != null ? TextOf(site) : HeaderText(h.Header);
    }

    /// <summary>Tick (or untick) a tick box by its words.</summary>
    public void Tick(string label, bool on = true, string? near = null)
    {
        var box = Find<CheckBox>(label, near, c => new[] { TextOf(c), Tip(c) })
                  ?? throw Miss($"no tick box \"{label}\"{Where(near)}", Screen());
        if (box.IsChecked != on) Press(box);
        if (box.IsChecked != on) throw Miss($"the tick box \"{label}\" did not become {(on ? "ticked" : "unticked")}", Screen());
        Note($"{(on ? "tick" : "untick")} {label}");
    }

    /// <summary>Type into the box a label names - the label beside it or over it, or the hint written in it - the
    /// way a person does: click in it, select what is there, type, and leave it.</summary>
    public void Type(string label, string text, string? near = null)
    {
        var box = BoxFor<Control>(label, near, c => c is TextBoxBase || c is ComboBox { IsEditable: true })
                  ?? throw Miss($"no box to type in labelled \"{label}\"{Where(near)}", Screen());
        TypeInto(box, text);
        Note($"type \"{text}\" into {label}");
    }

    /// <summary>Pick an option in the dropdown a label names, by the option's words as the list shows them.</summary>
    public void Pick(string label, string option, string? near = null)
    {
        var combo = BoxFor<ComboBox>(label, near, c => c is ComboBox)
                    ?? throw Miss($"no dropdown labelled \"{label}\"{Where(near)}", Screen());
        PickIn(combo, label, option);
        Note($"pick {option} in {label}");
    }

    /// <summary>Type words into a dropdown one can type in, as a person searches its list, and pick an option of
    /// what it then shows - for a list that opens narrowed, or too long to scroll.</summary>
    public void SearchAndPick(string label, string typed, string option, string? near = null)
    {
        var combo = BoxFor<ComboBox>(label, near, c => c is ComboBox { IsEditable: true })
                    ?? throw Miss($"no dropdown to type in labelled \"{label}\"{Where(near)}", Screen());
        var box = (TextBoxBase?)combo.Template.FindName("PART_EditableTextBox", combo) ?? throw Miss("an editable dropdown with no box in it", Screen());
        box.Focus();
        Settle();
        box.SelectAll();
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, typed));
        Settle();
        PickIn(combo, label, option);
        Note($"type \"{typed}\" into {label} and pick {option}");
    }

    /// <summary>Pick an option in the n-th dropdown of the row that holds the words <paramref name="rowWords"/> -
    /// for a row of a condition or action, whose boxes have no labels of their own ("Variable, GreetedToday,
    /// equals, false": the 1st, 2nd, 3rd... dropdown of that row). Counting from 1, left to right.</summary>
    public void PickInRow(string rowWords, int nth, string option)
    {
        var boxes = RowBoxes(rowWords).OfType<ComboBox>().ToList();
        if (nth < 1 || nth > boxes.Count) throw Miss($"the row with \"{rowWords}\" has {boxes.Count} dropdowns, not {nth}", Screen());
        PickIn(boxes[nth - 1], $"dropdown {nth} of the row \"{rowWords}\"", option);
        Note($"pick {option} in dropdown {nth} of the row \"{rowWords}\"");
    }

    /// <summary>Type into the n-th box to type in of the row holding <paramref name="rowWords"/>, from the left.</summary>
    public void TypeInRow(string rowWords, int nth, string text)
    {
        var boxes = RowBoxes(rowWords).Where(c => c is TextBoxBase || c is ComboBox { IsEditable: true }).ToList();
        if (nth < 1 || nth > boxes.Count) throw Miss($"the row with \"{rowWords}\" has {boxes.Count} boxes to type in, not {nth}", Screen());
        TypeInto(boxes[nth - 1], text);
        Note($"type \"{text}\" into box {nth} of the row \"{rowWords}\"");
    }

    /// <summary>Choose a row of a list or a tree by its words (a dialogue, a line, a variable).</summary>
    public void Select(string rowWords)
    {
        var all = Shown(_w).OfType<Selector>().Where(s => s is not ComboBox).SelectMany(Rows).Concat(Shown(_w).OfType<TreeViewItem>()).ToList();
        // The row that reads just those words first ("Cop", not "Cop's partner"), else one that holds them.
        var row = all.FirstOrDefault(r => Same(RowText(r), rowWords))
                  ?? all.FirstOrDefault(r => RowText(r).Contains(rowWords, StringComparison.OrdinalIgnoreCase))
                  ?? throw Miss($"no row reading \"{rowWords}\"", Screen());
        switch (row)
        {
            case ListBoxItem li: li.IsSelected = true; break;
            case TreeViewItem ti: ti.IsSelected = true; break;
        }
        row.BringIntoView();
        Settle();
        Note($"select the row {rowWords}");
    }

    /// <summary>Choose an item of the window's menu: Menu("File", "Save").</summary>
    public void Menu(string top, params string[] path)
    {
        var menu = Shown(_w).OfType<MenuItem>().FirstOrDefault(m => Same(HeaderText(m.Header), top))
                   ?? throw Miss($"no menu \"{top}\"", Screen());
        MenuItem at = menu;
        foreach (string step in path)
        {
            at.IsSubmenuOpen = true;
            Settle();
            var next = at.Items.OfType<object>().Select(i => i as MenuItem ?? at.ItemContainerGenerator.ContainerFromItem(i) as MenuItem)
                         .OfType<MenuItem>().FirstOrDefault(m => Same(HeaderText(m.Header), step) || Names(HeaderText(m.Header), step))
                       ?? throw Miss($"no item \"{step}\" in the {top} menu", "items: " + string.Join(", ", at.Items.OfType<MenuItem>().Select(m => HeaderText(m.Header))));
            at = next;
        }
        typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(at, null);
        menu.IsSubmenuOpen = false;
        Settle();
        Note($"menu {top} > {string.Join(" > ", path)}");
    }

    /// <summary>Ctrl+S, as a person saves.</summary>
    public void Save()
    {
        RealKeys.Press(_w, Key.S, ModifierKeys.Control);
        Settle();
        Note("save (Ctrl+S)");
    }

    // ── What a reader looks at ───────────────────────────────────────────────────────────────────────

    /// <summary>Whether these words are written anywhere on the window now.</summary>
    public bool Sees(string words)
        => Shown(_w).OfType<TextBlock>().Any(t => t.Text.Contains(words, StringComparison.OrdinalIgnoreCase))
           || Shown(_w).OfType<TextBox>().Any(t => t.Text.Contains(words, StringComparison.OrdinalIgnoreCase))
           || Shown(_w).OfType<ComboBox>().Any(c => TextOf(c).Contains(words, StringComparison.OrdinalIgnoreCase)
                                                    || c.IsEditable && (c.Text ?? "").Contains(words, StringComparison.OrdinalIgnoreCase));

    /// <summary>A picture of the window as it is drawn now, saved as a PNG: to look at what a step faces.</summary>
    public void Picture(string path)
    {
        Settle();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)_w.ActualWidth, (int)_w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(_w);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var file = System.IO.File.Create(path);
        png.Save(file);
    }

    /// <summary>Whether a button with these words (or tooltip) is on the window now, without pressing it.</summary>
    public bool SeesButton(string label, string? near = null)
        => Find<ButtonBase>(label, near, b => new[] { TextOf(b), Tip(b), AutomationProperties.GetName(b) }) != null;

    /// <summary>The rows of the lists and trees on the open tab, top to bottom, each indented by how far in it is
    /// drawn (two spaces per 16 px) - how a person reads which line sits under which.</summary>
    public IReadOnlyList<string> Rows()
    {
        var tabs = (TabControl)_w.FindName("MainTabs");
        var root = (FrameworkElement?)tabs.Template.FindName("PART_SelectedContentHost", tabs) ?? tabs;
        // A strip of choices side by side (Kind: Text | Choice | Random) is not a list of rows.
        bool Strip(Selector s) { var tops = Rows(s).Select(r => Bounds(r)?.Top).Where(t => t != null).Distinct().Count(); return tops <= 1 && Rows(s).Count() > 1; }
        var rows = Shown(root).OfType<Selector>().Where(s => s is not ComboBox && !Strip(s)).SelectMany(Rows).Concat(Shown(root).OfType<TreeViewItem>())
            .Select(r => (r, b: Bounds(r))).Where(x => x.b != null).ToList();
        if (rows.Count == 0) return new List<string>();
        double left = rows.Min(x => FirstWordsLeft(x.r) ?? x.b!.Value.Left);
        return rows.OrderBy(x => x.b!.Value.Top).ThenBy(x => x.b!.Value.Left)
                   .Select(x => new string(' ', 2 * (int)Math.Round(((FirstWordsLeft(x.r) ?? x.b!.Value.Left) - left) / 16)) + RowText(x.r))
                   .Where(t => t.Trim().Length > 0).Distinct().ToList();
    }

    private double? FirstWordsLeft(FrameworkElement row)
        => GuideSectionShots.Below(row).OfType<TextBlock>().Where(t => t.IsVisible && t.Text.Trim().Length > 0)
               .Select(t => Bounds(t)?.Left).FirstOrDefault(l => l != null);

    /// <summary>What the box a label names holds now (a box's text, a dropdown's shown choice, a tick's state).</summary>
    public string ValueOf(string label, string? near = null)
    {
        if (Find<CheckBox>(label, near, k => new[] { TextOf(k) }) is { } tick) return tick.IsChecked == true ? "ticked" : "unticked";
        var c = BoxFor<Control>(label, near, x => x is TextBoxBase || x is ComboBox || x is CheckBox)
                ?? throw Miss($"no box labelled \"{label}\"{Where(near)}", Screen());
        return c switch
        {
            TextBox t => t.Text,
            RichTextBox r => new System.Windows.Documents.TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.TrimEnd('\r', '\n'),
            ComboBox { IsEditable: true } typedIn => typedIn.Text,
            ComboBox combo => TextOf(combo),
            CheckBox k => k.IsChecked == true ? "ticked" : "unticked",
            _ => "",
        };
    }

    /// <summary>The issues Validate lists, as the Issues page shows them (ModForge tab ▸ Issues).</summary>
    public IReadOnlyList<string> Issues()
    {
        Tab("ModForge");
        Click("Validate");
        var page = _w.FindName("HomePageIssues") as FrameworkElement;
        if (page is not { IsVisible: true }) { Click("Issues"); page = _w.FindName("HomePageIssues") as FrameworkElement; }
        var rows = page == null ? new List<string>()
            : Shown(page).OfType<ListBoxItem>().Select(TextOf).Where(t => t.Length > 0).ToList();
        Note($"{rows.Count} issue(s)");
        return rows;
    }

    /// <summary>What a person sees on the window now, as words: the open tab, the group headings, the buttons, the
    /// labels with what the box beside each holds, the tick boxes and the selected rows. What a stuck reader would
    /// read off the screen to find the step's words.</summary>
    public string Screen()
    {
        var lines = new List<string>();
        var tabs = (TabControl)_w.FindName("MainTabs");
        lines.Add("open tab: " + HeaderText((tabs.SelectedItem as TabItem)?.Header));
        var root = (FrameworkElement?)tabs.Template.FindName("PART_SelectedContentHost", tabs) ?? tabs;
        var shown = Shown(root).ToList();
        lines.Add("headings: " + Join(shown.OfType<HeaderedContentControl>().Where(h => h is not TabItem)
                                          .Select(h => HeadingWords(h) + (h is Expander { IsExpanded: false } ? " [shut]" : ""))));
        lines.Add("buttons: " + Join(shown.OfType<ButtonBase>().Where(b => b is not CheckBox and not RadioButton && b.TemplatedParent is not Control)
                                         .Select(b => (TextOf(b) is { Length: > 0 } t ? t : Tip(b) is { Length: > 0 } tip ? "(" + tip + ")" : "")
                                                      is { Length: > 0 } words ? words + (b.IsEnabled ? "" : " [greyed]") : "")));
        lines.Add("tick boxes: " + Join(shown.OfType<CheckBox>().Select(c => TextOf(c) + (c.IsChecked == true ? " [x]" : " [ ]"))));
        lines.Add("choices: " + Join(shown.OfType<RadioButton>().Select(c => TextOf(c) + (c.IsChecked == true ? " (o)" : " ( )"))));
        var labels = shown.OfType<TextBlock>().Where(t => t.Text.Length is > 0 and < 60 && !InsideControl(t)).Select(t => t.Text).Distinct().Take(120);
        lines.Add("words: " + Join(labels));
        lines.Add("boxes: " + Join(shown.OfType<Control>().Where(c => c is TextBox && c.TemplatedParent is not ComboBox || c is ComboBox || c is RichTextBox)
                                       .Select(c => (LabelOf(c) ?? "?") + " = \"" + c switch
                                       {
                                           TextBox t => t.Text,
                                           RichTextBox r => new System.Windows.Documents.TextRange(r.Document.ContentStart, r.Document.ContentEnd).Text.TrimEnd('\r', '\n'),
                                           ComboBox { IsEditable: true } e => e.Text,
                                           _ => TextOf(c),
                                       } + "\"")));
        lines.Add("selected rows: " + Join(Shown(root).OfType<Selector>().Select(s => s.SelectedItem == null ? "" : TextOf(s.ItemContainerGenerator.ContainerFromItem(s.SelectedItem) as FrameworkElement))));
        return string.Join("\n", lines);
    }

    // ── How it finds things ──────────────────────────────────────────────────────────────────────────

    private T? Find<T>(string label, string? near, Func<T, IEnumerable<string?>> words) where T : FrameworkElement
    {
        var scope = Scope(near);
        var all = Shown(scope).OfType<T>().Where(c => c.IsEnabled).ToList();
        return all.FirstOrDefault(c => words(c).Any(w => w != null && Same(w, label)))
               ?? all.FirstOrDefault(c => words(c).Any(w => w != null && Names(w, label)));
    }

    /// <summary>The box a label names: a label's Target; the nearest box to its right on its line; the nearest
    /// under it; or a box whose hint, tooltip or own words are the label.</summary>
    private T? BoxFor<T>(string label, string? near, Func<Control, bool> kind) where T : Control
    {
        var scope = Scope(near);
        var shown = Shown(scope).ToList();
        // Greyed boxes too, for where they stand: a label whose own box is greyed out is not the one meant, and the
        // next box along belongs to another label.
        var boxes = shown.OfType<Control>().Where(c => kind(c) && !(c is TextBox && c.TemplatedParent is ComboBox)).ToList();
        var labelEls = shown.OfType<FrameworkElement>().Where(f => f switch
        {
            TextBlock t => !InsideControl(t) && (Same(t.Text, label) || Names(t.Text, label)),
            Label l => Same(HeaderText(l.Content), label) || Names(HeaderText(l.Content), label),
            _ => false,
        }).OrderBy(f => Same(f is TextBlock t ? t.Text : HeaderText(((Label)f).Content), label) ? 0 : 1).ToList();
        foreach (var l in labelEls)
        {
            if (l is Label { Target: T target } && kind(target)) return target;
            var lr = Bounds(l);
            if (lr == null) continue;
            var beside = boxes.Select(b => (b, r: Bounds(b))).Where(x => x.r is { } r && r.Left >= lr.Value.Left + lr.Value.Width * 0.5
                                  && r.Top < lr.Value.Bottom && r.Bottom > lr.Value.Top)
                              .OrderBy(x => x.r!.Value.Left - lr.Value.Right).Select(x => x.b).FirstOrDefault();
            if (beside != null) { if (beside is T t1 && t1.IsEnabled) return t1; continue; }
            var under = boxes.Select(b => (b, r: Bounds(b))).Where(x => x.r is { } r && r.Top >= lr.Value.Top + 2 && r.Top - lr.Value.Bottom < 40
                                 && r.Right > lr.Value.Left - 8 && r.Left < lr.Value.Right + 200)
                             .OrderBy(x => x.r!.Value.Top).ThenBy(x => Math.Abs(x.r!.Value.Left - lr.Value.Left)).Select(x => x.b).FirstOrDefault();
            if (under is T t2 && t2.IsEnabled) return t2;
        }
        return boxes.OfType<T>().FirstOrDefault(b => b.IsEnabled && (Same(Tip(b), label) || Same(HintOf(b), label) || Same(AutomationProperties.GetName(b), label)));
    }

    /// <summary>The boxes of the row that holds these words, left to right.</summary>
    private List<Control> RowBoxes(string rowWords)
    {
        var tabs = (TabControl)_w.FindName("MainTabs");
        var root = (FrameworkElement?)tabs.Template.FindName("PART_SelectedContentHost", tabs) ?? tabs;
        var shown = Shown(root).ToList();
        var anchor = shown.OfType<FrameworkElement>().FirstOrDefault(f => f is TextBlock t && !InsideControl(t) && t.Text.Contains(rowWords, StringComparison.OrdinalIgnoreCase)
                                                                      || f is ComboBox c && TextOf(c).Contains(rowWords, StringComparison.OrdinalIgnoreCase))
                     ?? throw Miss($"no row with \"{rowWords}\"", Screen());
        var ar = Bounds(anchor)!.Value;
        double mid = ar.Top + ar.Height / 2;
        return shown.OfType<Control>().Where(c => (c is ComboBox || c is TextBoxBase) && !(c is TextBox && c.TemplatedParent is ComboBox) && c.IsEnabled)
                    .Select(c => (c, r: Bounds(c))).Where(x => x.r is { } r && r.Top <= mid && r.Bottom >= mid)
                    .OrderBy(x => x.r!.Value.Left).Select(x => x.c).ToList();
    }

    /// <summary>The part of the window a heading heads. "NPC: > Active when" is the Active when heading that comes
    /// first below the words "NPC:" - for a page that shows the same heading more than once, as a reader told "on the
    /// placement, under Active when" looks below the placement's own words.</summary>
    private FrameworkElement Scope(string? near)
    {
        if (near == null) return _w;
        double below = double.NegativeInfinity;
        int gt = near.IndexOf(" > ", StringComparison.Ordinal);
        if (gt > 0)
        {
            string after = near[..gt];
            near = near[(gt + 3)..];
            var mark = Shown(_w).OfType<TextBlock>().FirstOrDefault(t => !InsideControl(t) && (Same(t.Text, after) || Names(t.Text, after)))
                       ?? throw Miss($"no words \"{after}\" to look below", Screen());
            below = Bounds(mark)?.Top ?? double.NegativeInfinity;
        }
        string heading = near;
        double Top(FrameworkElement f) => Bounds(f)?.Top ?? double.NegativeInfinity;
        var groups = Shown(_w).OfType<HeaderedContentControl>().Where(h => h is not TabItem && Top(h) > below).ToList();
        var group = groups.FirstOrDefault(h => Same(HeaderText(h.Header), heading)) ?? groups.FirstOrDefault(h => Names(HeaderText(h.Header), heading));
        // A heading that is a line of words over its part: the panel that holds the line and what is under it.
        var words = Shown(_w).OfType<TextBlock>().Where(t => !InsideControl(t) && Top(t) > below && (Same(t.Text, heading) || Names(t.Text, heading)))
                             .OrderBy(t => Same(t.Text, heading) ? 0 : 1).ThenBy(Top).FirstOrDefault();
        if (group != null && (words == null || below == double.NegativeInfinity || Top(group) <= Top(words))) return group;
        if (words != null) return PartUnder(words);
        throw Miss($"no group headed \"{heading}\"{(below > double.NegativeInfinity ? " below that" : "")}", Screen());
    }

    /// <summary>The panel a heading line heads: up from the words past the line they stand on (the words and a
    /// button beside them) to the first panel that holds more than that line.</summary>
    private static FrameworkElement PartUnder(TextBlock words)
    {
        FrameworkElement at = words;
        for (var up = VisualTreeHelper.GetParent(words) as FrameworkElement; up != null && up is not Window; up = VisualTreeHelper.GetParent(up) as FrameworkElement)
        {
            at = up;
            if (up is Panel && up.ActualHeight > words.ActualHeight * 2.5) break;
        }
        return at;
    }

    private void PickIn(ComboBox combo, string what, string option)
    {
        WindowHarness.SetOpen(combo, true);
        try
        {
            var rows = Enumerable.Range(0, combo.Items.Count)
                .Select(i => (item: combo.Items[i], row: combo.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem))
                .Where(x => x.row is not { Visibility: not Visibility.Visible }).ToList();
            string Text((object item, ComboBoxItem? row) x) => x.row != null && TextOf(x.row) is { Length: > 0 } t ? t : x.item?.ToString() ?? "";
            var hit = rows.FirstOrDefault(x => Same(Text(x), option));
            if (hit.item == null) hit = rows.FirstOrDefault(x => Names(Text(x), option) || Text(x).StartsWith(option, StringComparison.OrdinalIgnoreCase));
            // A row that holds the words among its own ("New Place" in "This pack — New Place"), when only one does:
            // the row a person looking for those words picks.
            if (hit.item == null && rows.Count(x => Text(x).Contains(option, StringComparison.OrdinalIgnoreCase)) == 1)
                hit = rows.First(x => Text(x).Contains(option, StringComparison.OrdinalIgnoreCase));
            if (hit.item == null)
                throw Miss($"no option \"{option}\" in {what}", "options: " + Join(rows.Select(Text).Take(60))
                                                                  + (combo.IsEditable ? $"\n(the box says \"{combo.Text}\")" : ""));
            if (hit.row != null) hit.row.IsSelected = true;
            else combo.SelectedItem = hit.item;
        }
        finally { WindowHarness.SetOpen(combo, false); }
        Settle();
    }

    private void TypeInto(Control box, string text)
    {
        var target = box is ComboBox { IsEditable: true } combo
            ? (TextBoxBase?)combo.Template.FindName("PART_EditableTextBox", combo) ?? throw Miss("an editable dropdown with no box in it", Screen())
            : (TextBoxBase)box;
        target.Focus();
        Settle();
        target.SelectAll();
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, target, text));
        Settle();
        // Left, as a person leaves a box: the keyboard on to the next control.
        target.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Settle();
    }

    private static readonly MethodInfo OnClick = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private void Press(ButtonBase b)
    {
        b.BringIntoView();
        OnClick.Invoke(b, null);
        Settle();
    }

    private void Settle()
    {
        // A person's mouse and keys make WPF ask every command again whether it can run; the driver's presses are
        // not real input, so it asks for them.
        CommandManager.InvalidateRequerySuggested();
        WindowHarness.Pump();
        _w.UpdateLayout();
        WindowHarness.Pump();
    }

    private void Note(string what) => _log?.WriteLine("  " + what);

    private UiDriverMiss Miss(string what, string screen) => new(what + "\n--- on screen ---\n" + screen);

    private static string Where(string? near) => near == null ? "" : $" under \"{near}\"";

    private static string Join(IEnumerable<string> items) => string.Join(" | ", items.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

    // ── Reading the screen ───────────────────────────────────────────────────────────────────────────

    private static IEnumerable<FrameworkElement> Shown(DependencyObject root)
        => GuideSectionShots.Below(root).OfType<FrameworkElement>().Where(f => f.IsVisible && f.ActualWidth > 1 && f.ActualHeight > 1);

    private static IEnumerable<FrameworkElement> Rows(Selector s)
        => Enumerable.Range(0, s.Items.Count).Select(i => s.ItemContainerGenerator.ContainerFromIndex(i)).OfType<FrameworkElement>().Where(f => f.IsVisible);

    private Rect? Bounds(FrameworkElement f)
    {
        try { return f.TransformToAncestor(_w).TransformBounds(new Rect(f.RenderSize)); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>A control's words as drawn: its text pieces, glyph icons left out.</summary>
    internal static string TextOf(FrameworkElement? f)
    {
        if (f == null) return "";
        if (f is ContentControl { Content: string s }) return s.Trim();
        var parts = GuideSectionShots.Below(f).OfType<TextBlock>()
            .Where(t => t.IsVisible && !(t.FontFamily?.Source ?? "").Contains("MDL2", StringComparison.Ordinal)
                        && !(t.FontFamily?.Source ?? "").Contains("Fluent", StringComparison.Ordinal))
            .Select(t => Words(t).Trim()).Where(t => t.Length > 0);
        return string.Join(" ", parts);
    }

    /// <summary>A row's own words: a tree row's header only, not the rows under it.</summary>
    private static string RowText(FrameworkElement row)
        => row is TreeViewItem t && t.Template?.FindName("PART_Header", t) is FrameworkElement header ? TextOf(header) : TextOf(row);

    /// <summary>A text block's words as drawn: its Text, or, when that reads empty, its runs (a heading built from
    /// bound runs - "THE GAME'S CHARACTERS (118)" - can report an empty Text while drawing them).</summary>
    internal static string Words(TextBlock t)
        => t.Text is { Length: > 0 } s ? s
           : string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));

    private static string Tip(FrameworkElement f) => f.ToolTip switch
    {
        string s => s,
        ToolTip { Content: string s } => s,
        TextBlock t => t.Text,
        _ => "",
    };

    private static string HintOf(Control c)
        => GuideSectionShots.Below(c).OfType<TextBlock>().FirstOrDefault(t => t.IsVisible && t.Opacity < 1 && t.Text.Length > 0)?.Text ?? "";

    /// <summary>The words beside a box, for the dump: the nearest label on its left on its line, else over it.</summary>
    private string? LabelOf(Control box)
    {
        var br = Bounds(box);
        if (br == null) return null;
        var parent = VisualTreeHelper.GetParent(box);
        for (int up = 0; up < 4 && parent != null; up++, parent = VisualTreeHelper.GetParent(parent))
        {
            var near = Shown(parent).OfType<TextBlock>().Where(t => !InsideControl(t) && t.Text.Length is > 0 and < 50)
                .Select(t => (t, r: Bounds(t))).Where(x => x.r is { } r && (r.Right <= br.Value.Left + 4 && r.Top < br.Value.Bottom && r.Bottom > br.Value.Top
                                                                       || r.Bottom <= br.Value.Top + 2 && br.Value.Top - r.Bottom < 30))
                .OrderBy(x => Math.Abs(x.r!.Value.Right - br.Value.Left) + Math.Abs(x.r!.Value.Top - br.Value.Top)).FirstOrDefault();
            if (near.t != null) return near.t.Text;
        }
        return null;
    }

    private static bool InsideControl(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ButtonBase or TextBoxBase or ComboBox or ListBoxItem or TreeViewItem) return true;
            if (p is TabItem or Window) return false;
        }
        return false;
    }

    private static string HeaderText(object? h) => h switch
    {
        string s => s,
        TextBlock t => t.Text,
        ContentControl c => HeaderText(c.Content),
        Panel p => p.Children.OfType<object>().Select(HeaderText).FirstOrDefault(x => x.Length > 0) ?? "",
        _ => "",
    };

    private static string Plain(string a) => StripIcon(a.Replace(" & ", " and ").Replace("…", "...").Replace("−", "-").Replace("_", "")
                                             .Trim().TrimEnd(':').Trim());

    /// <summary>A pictograph in front of the words ("⚒ ModForge") is read past, as a person reads the word.</summary>
    private static string StripIcon(string a)
    {
        int i = 0;
        while (i < a.Length && (char.GetUnicodeCategory(a[i]) == System.Globalization.UnicodeCategory.OtherSymbol || char.IsWhiteSpace(a[i]))) i++;
        return i > 0 && i < a.Length ? a[i..] : a;
    }

    private static bool Same(string? a, string? b) => a != null && b != null && string.Equals(Plain(a), Plain(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Words that start with the label and go on with their own explanation ("Refresh mode (when it resets)").</summary>
    private static bool Names(string? words, string label)
    {
        if (words == null) return false;
        string c = Plain(words), h = Plain(label);
        if (h.Length == 0) return false;
        return c.StartsWith(h, StringComparison.OrdinalIgnoreCase) && c.Length > h.Length && !char.IsLetterOrDigit(c[h.Length]);
    }
}

/// <summary>A step of the guide the screen does not answer: what was looked for, and what the screen shows.</summary>
internal sealed class UiDriverMiss : Exception
{
    public UiDriverMiss(string message) : base(message) { }
}
