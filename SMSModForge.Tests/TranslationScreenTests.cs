using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every word on every screen came through the translation.
/// <para/>
/// The source scans find words typed into a layout or a sentence in code. What
/// they cannot find is a word that is neither: an enum shown by its name, a
/// list of the program's values bound straight to a dropdown, a string some
/// method builds that the scan took for a name. This finds those the only way
/// that is certain - by looking at the screen.
/// <para/>
/// The window is built in the fake language, where every text comes out
/// between ⟦ and ⟧. The walk fills a pack with one of everything, visits every
/// tab with something selected, puts every action and condition type on a
/// row, and reads every text, tooltip, header and menu item it can reach.
/// Anything left without the marks is either the pack's own data, the game's
/// own names, or a word nobody handed to the translation.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public class TranslationScreenTests
{
    private readonly ITestOutputHelper _out;
    public TranslationScreenTests(ITestOutputHelper output) => _out = output;

    private static readonly Regex Words = new(@"\p{L}+", RegexOptions.Compiled);

    [Fact]
    public void EveryWordOnEveryScreenComesThroughTheTranslation()
    {
        // Built in the fake language from the start: the window as it is at
        // startup in any language.
        Walk(switchAfterBuilding: false);
    }

    [Fact]
    public void SwitchingLanguageLeavesNoWordInTheOldOne()
    {
        // Built in English, filled, and THEN switched to the fake language in
        // place - the Language menu's switch, with no restart. Every text on
        // every screen must have followed: a word the switch missed would still
        // be English, and English is exactly what the walk reports.
        Walk(switchAfterBuilding: true);
    }

    private void Walk(bool switchAfterBuilding)
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var marked = new HashSet<string>(StringComparer.Ordinal);
        var data = GameWords();
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        void Listen()
        {
            // A key the English file does not have shows as the key itself, which
            // the walk would take for a name: hear it where it happens instead.
            Loc.Current.Missing = key => missing.Add(key);
            Loc.Current.Unfilled = (key, gap) => missing.Add(key + " {" + gap + "} left unfilled");
        }

        Loc.Use(switchAfterBuilding ? Loc.EnglishCode : PseudoText.Code);
        Listen();
        Loc.T("test.noSuchKey");
        Assert.Equal(new[] { "test.noSuchKey" }, missing);   // the control: the hook hears
        missing.Clear();
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                Fill(vm);
                WindowHarness.Pump();

                if (switchAfterBuilding)
                {
                    // The control for this one: before the switch the first
                    // tab alone is English (94 texts when this was written),
                    // so a switch that changed nothing would be reported.
                    int englishBefore = TextsIn(window).Count(t => !PseudoText.Marks(t.Text) && !Accepted(t.Text, data));
                    _out.WriteLine(englishBefore + " texts in English before the switch");
                    Assert.True(englishBefore > 50, englishBefore + " English texts before the switch");

                    window.SwitchLanguage(PseudoText.Code);
                    Listen();
                    WindowHarness.Pump();
                }

                foreach (string w in PackWords(vm)) data.Add(w);

                // Word by word, a text made only of names the game or the pack
                // uses passes as theirs. That rule cannot tell "Random" the
                // pack's word from "Random" a label left in English - so a text
                // that is, exactly, one of the English file's texts, and not
                // something the pack itself holds, is English left behind.
                void Look(string where)
                {
                    WindowHarness.Pump();
                    var packStrings = PackStrings(vm);   // the loops below change the pack
                    foreach (var (text, what) in TextsIn(window))
                    {
                        if (PseudoText.Marks(text)) marked.Add(text);
                        else if (LeftInEnglish(text, packStrings) && !found.ContainsKey(text))
                            found[text] = where + " / " + what + " (an English text)";
                        else if (!Accepted(text, data) && !found.ContainsKey(text)) found[text] = where + " / " + what;
                    }
                }

                int tabs = ((TabControl)window.FindName("MainTabs")).Items.Count;
                for (int tab = 0; tab < tabs; tab++)
                {
                    vm.SelectedTabIndex = tab;
                    // A line selected, so the node editor is on screen: without
                    // it the walk never read the Kind, Jump and Duration lists,
                    // which showed the program's own names in every language
                    // until the walk after a switch - which does land on a
                    // line - found them.
                    if (tab == TabDialogues)
                    {
                        WindowHarness.Pump();
                        vm.SelectedNode = vm.SelectedDialogue?.Nodes.LastOrDefault();
                    }
                    Look("tab " + tab);
                }

                // Every type an action or a condition can be, on a row of its
                // own, so each one's fields are drawn.
                vm.SelectedTabIndex = TabIntegration;
                var rule = vm.SelectedIntegrationRule!;
                foreach (string type in NodeActionTypes.All)
                {
                    rule.Actions[0].Type = type;
                    Look("action " + type);
                }
                foreach (string type in NodeConditionTypes.All)
                {
                    rule.Conditions[0].Type = type;
                    Look("condition " + type);
                }

                foreach (var (text, what) in MenuTexts(window))
                    if (!Accepted(text, data) && !found.ContainsKey(text)) found[text] = "menu / " + what;
            });
        }
        finally
        {
            Loc.Use(Loc.EnglishCode);
        }

        foreach (var kv in found) _out.WriteLine($"{kv.Value}:  {kv.Key}");
        _out.WriteLine(marked.Count + " different texts read through the translation");
        // A walk that reached nothing would find nothing wrong: it must have
        // read the screens it says it read.
        foreach (string m in missing) _out.WriteLine("missing: " + m);
        Assert.True(marked.Count > 500, marked.Count + " texts read");
        Assert.Empty(missing);
        Assert.Empty(found);
    }

    /// <summary>A pack with one of everything, each selected.</summary>
    private static void Fill(MainViewModel vm)
    {
        void Run(RelayCommand command)
        {
            if (command.CanExecute(null)) command.Execute(null);
        }

        Run(vm.AddCharacterCommand);
        Run(vm.AddOutfitCommand);
        Run(vm.AddNpcCommand);
        Run(vm.AddPlaceCommand);
        Run(vm.AddNavigatorButtonCommand);
        Run(vm.AddGameObjectCommand);
        Run(vm.AddMapButtonCommand);
        Run(vm.AddDialogueCommand);
        Run(vm.AddDialogueRootNodeCommand);
        Run(vm.AddDialogueChildNodeCommand);
        Run(vm.AddNodeConditionCommand);
        Run(vm.AddNodeActionOnStartCommand);
        Run(vm.AddDialogueStartConditionCommand);
        Run(vm.AddSceneCommand);
        Run(vm.AddMusicCommand);
        Run(vm.AddSfxCommand);
        Run(vm.AddWallpaperCommand);
        Run(vm.AddVariableCommand);
        Run(vm.AddIntegrationRuleCommand);
        Run(vm.AddIntegrationConditionCommand);
        Run(vm.AddIntegrationActionCommand);
        Run(vm.AddOwnUiCommand);
        Run(vm.AddQuestCommand);
    }

    /// <summary>Same order as MainWindow's TabItems.</summary>
    private const int TabDialogues = 5;
    private const int TabIntegration = 11;

    /// <summary>
    /// A text that did not come through the translation, but should not have:
    /// no letters at all, a name of the program's own ("SetVariable",
    /// "vanilla:Beach"), or made only of words the pack or the game supplies.
    /// </summary>
    private static bool Accepted(string text, HashSet<string> data)
    {
        if (PseudoText.Marks(text)) return true;
        var words = Words.Matches(text).Select(m => m.Value).ToList();
        if (words.Count == 0 || !words.Any(w => w.Length >= 2)) return true;
        string trimmed = text.Trim();
        // One token with no spaces that is plainly a name: an inner capital,
        // a digit, or the punctuation names are built from.
        if (!trimmed.Any(char.IsWhiteSpace)
            && (trimmed.Skip(1).Any(char.IsUpper) || trimmed.Any(char.IsDigit) || trimmed.IndexOfAny(new[] { '.', ':', '_', '/', '\\', '-' }) >= 0))
            return true;
        return words.All(w => data.Contains(w));
    }

    /// <summary>
    /// One of the game's tokens on its own - {PC}, [PV:name] - which is what
    /// the author types in every language, so every language shows it as it
    /// is. The Dialogues tab lists them with what each stands for; the walk
    /// only met that list once it stopped reading the author's own settings,
    /// which had it folded away (2026-09-27).
    /// </summary>
    private static readonly Regex GameToken = new(@"^(\{[A-Z]+\}|\[PV:[^\]]*\])$", RegexOptions.Compiled);

    /// <summary>Every text of the English file, whole.</summary>
    private static readonly HashSet<string> EnglishTexts =
        new(Loc.English.Entries.Select(e => e.Text).Where(t => t.Length > 0), StringComparer.Ordinal);

    /// <summary>
    /// Exactly one of the English file's texts, and not the pack's own data,
    /// nor a letter on its own ("X"), nor the name of an action or condition
    /// type - which is the program's spelling on every screen, by design, and
    /// "Variable" (the one entry the variable types are folded into) is one.
    /// </summary>
    private static bool LeftInEnglish(string text, HashSet<string> packStrings)
        => EnglishTexts.Contains(text)
           && !packStrings.Contains(text)
           && !GameToken.IsMatch(text)
           && Words.Matches(text).Any(m => m.Value.Length >= 2)
           && !NodeActionTypes.All.Contains(text) && !NodeConditionTypes.All.Contains(text)
           && text != NodeActionViewModel.VariableFamilyType && text != NodeConditionViewModel.VariableFamilyType;

    /// <summary>Every string in the pack's own data, whole, as saved.</summary>
    private static HashSet<string> PackStrings(MainViewModel vm)
    {
        var json = JToken.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        return new HashSet<string>(json.SelectTokens("$..*").OfType<JValue>()
                                       .Where(v => v.Type == JTokenType.String).Select(v => (string)v!),
                                   StringComparer.Ordinal);
    }

    /// <summary>Every word in the pack's own data, as saved.</summary>
    private static IEnumerable<string> PackWords(MainViewModel vm)
    {
        var json = JToken.Parse(PackRepository.SerializeAsSaved(vm.Pack));
        foreach (var value in json.SelectTokens("$..*").OfType<JValue>())
            if (value.Type == JTokenType.String)
                foreach (Match m in Words.Matches((string)value!))
                    yield return m.Value;
    }

    /// <summary>The game's own names, which are shown as the game spells them.</summary>
    private static HashSet<string> GameWords()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            foreach (Match m in Words.Matches(s)) set.Add(m.Value);
        }
        foreach (var p in VanillaPlaces.All) { Add(p.DisplayName); Add(p.GoName); }
        foreach (var v in VanillaGameVariables.All) { Add(v.Name); Add(v.List); Add(v.Type); }
        foreach (var d in WorldMapDistricts.All) { Add(d.DisplayName); Add(d.GoName); }
        foreach (var q in VanillaQuests.All) { Add(q.Name); Add(q.Title); }
        foreach (string n in VanillaMusic.All) Add(n);
        // A language is named as its own speakers write it - "Español",
        // "English" - whatever language the editor is in, so a reader can
        // find theirs. Never translated, on purpose.
        foreach (var language in MainViewModel.AllPackLanguages()) Add(language.Label);
        // The names of action and condition types are the program's own
        // spelling, which the documentation and every pack use as they are.
        // "SetVariable" passes as a name already; "Wait" needs saying.
        foreach (string t in NodeActionTypes.All) Add(t);
        foreach (string t in NodeConditionTypes.All) Add(t);
        // A fixed option with no text of its own is a name the game reads -
        // a Unity property such as "alpha" - and is shown as it is stored.
        foreach (var schemas in ActionSchemas.ByType.Values.Concat(ConditionSchemas.ByType.Values))
            foreach (var p in schemas)
                foreach (string o in p.FixedOptions)
                    if (!Loc.English.Has(ParamSchema.ChoiceKey(p.Key, o))) Add(o);
        return set;
    }

    /// <summary>Every text in the window's visual tree, with what showed it.
    /// What a person types - a text box's contents - is theirs, and skipped.</summary>
    private static IEnumerable<(string Text, string What)> TextsIn(Window window)
    {
        if (!string.IsNullOrEmpty(window.Title)) yield return (window.Title, "window title");
        var stack = new Stack<DependencyObject>();
        stack.Push(window);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is TextBoxBase || d is PasswordBox) continue;
            if (d is FrameworkElement fe)
            {
                if (fe.ToolTip is string tip) yield return (tip, fe.GetType().Name + " tooltip");
                else if (fe.ToolTip is ToolTip { Content: string tipText }) yield return (tipText, fe.GetType().Name + " tooltip");
            }
            switch (d)
            {
                case TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text) && !InsideEditableCombo(tb):
                    yield return (tb.Text, "text");
                    break;
                case AccessText at when !string.IsNullOrWhiteSpace(at.Text):
                    yield return (at.Text, "access text");
                    break;
                case HeaderedContentControl h when h.Header is string header:
                    yield return (header, h.GetType().Name + " header");
                    break;
                case HeaderedItemsControl hi when hi.Header is string header:
                    yield return (header, hi.GetType().Name + " header");
                    break;
            }
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }
    }

    private static bool InsideEditableCombo(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ComboBox { IsEditable: true } || p is TextBoxBase) return true;
        return false;
    }

    /// <summary>The menus, which are not in the visual tree until they open:
    /// read from their items instead.</summary>
    private static IEnumerable<(string Text, string What)> MenuTexts(Window window)
    {
        var menus = new List<ItemsControl>();
        var stack = new Stack<DependencyObject>();
        stack.Push(window);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is Menu m) menus.Add(m);
            if (d is FrameworkElement { ContextMenu: { } cm }) menus.Add(cm);
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
        }

        var items = new Stack<object>(menus.SelectMany(m => m.Items.Cast<object>()));
        while (items.Count > 0)
        {
            if (items.Pop() is not MenuItem item) continue;
            if (item.Header is string h) yield return (h, "menu item");
            if (item.ToolTip is string t) yield return (t, "menu tooltip");
            if (!string.IsNullOrEmpty(item.InputGestureText)) yield return (item.InputGestureText, "shortcut");
            foreach (var child in item.Items) items.Push(child);
        }
    }
}
