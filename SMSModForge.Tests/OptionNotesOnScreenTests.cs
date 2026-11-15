using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The notes beside dropdown items and the last lists put under headings
/// (the author, 1.7.0), as the window draws them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class OptionNotesOnScreenTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-notes-" + Guid.NewGuid().ToString("N"));

    public OptionNotesOnScreenTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private MainViewModel OnALine(MainWindow window, ModPack pack)
    {
        PackRepository.Save(pack, _dir);
        var vm = (MainViewModel)window.DataContext;
        vm.OpenPackFromPath(_dir);
        var tabs = (TabControl)window.FindName("MainTabs");
        for (int i = 0; i < tabs.Items.Count; i++)
            if (tabs.Items[i] is TabItem t && (t.Header as string) == "Dialogues") { tabs.SelectedIndex = i; break; }
        WindowHarness.Pump();
        vm.AddDialogueCommand.Execute(null);
        WindowHarness.Pump();
        if (vm.SelectedDialogue!.Nodes.Count == 0) { vm.AddDialogueRootNodeCommand.Execute(null); WindowHarness.Pump(); }
        vm.SelectedNode = vm.SelectedDialogue.Nodes.First();
        WindowHarness.Pump();
        return vm;
    }

    /// <summary>The visible box on the row whose kind of note is <paramref name="kind"/>.</summary>
    private static ComboBox Box(MainWindow window, object row, string kind)
    {
        window.UpdateLayout();
        WindowHarness.Pump();
        return Descendants<ComboBox>(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, row)
                                                        && OptionNotes.GetKind(c) == kind);
    }

    /// <summary>What the open list draws beside an item: the item's container's
    /// texts, as laid out.</summary>
    private List<string> Drawn(ComboBox box, object item)
    {
        box.IsDropDownOpen = true;
        WindowHarness.Pump();
        box.UpdateLayout();
        var container = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(item);
        Assert.NotNull(container);
        var texts = Descendants<TextBlock>(container).Where(t => t.IsVisible).Select(t => t.Text).ToList();
        _out.WriteLine($"  {item}: {string.Join(" | ", texts)}");
        box.IsDropDownOpen = false;
        WindowHarness.Pump();
        return texts;
    }

    [Fact]
    public void TheActionTypesSayWhatEachDoes_InTheOpenListOnly()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("notes.pack"));
            var action = vm.SelectedNode!.AddActionOnFinish();
            WindowHarness.Pump();
            var box = Box(window, action, OptionNotes.ActionType);

            var texts = Drawn(box, NodeActionTypes.CharacterFocus);
            Assert.Contains(SMSModForge.Localization.Loc.T("note.action.characterFocus"), texts);

            // The closed box shows the type and nothing else.
            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.DoesNotContain(Descendants<TextBlock>(box).Where(t => t.IsVisible).Select(t => t.Text),
                                  t => t == SMSModForge.Localization.Loc.T("note.action.setActive"));
        });
    }

    [Fact]
    public void TypingANotesWordsFindsTheItem()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("notes.pack"));
            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.EmitSignal;
            WindowHarness.Pump();
            var box = Box(window, action.ParamRows.First(), OptionNotes.Signal);
            box.IsDropDownOpen = true;
            WindowHarness.Pump();

            // "camera" is in no signal's name - only in flash's note.
            ComboBoxSearch.SetTyped(box, "camera");
            WindowHarness.Pump();
            ComboBoxItem Of(string s) => (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(s);
            var flash = Of("flash").Visibility;
            var kiss = Of("kiss").Visibility;
            ComboBoxSearch.SetTyped(box, "");
            Assert.Contains(SMSModForge.Localization.Loc.T("note.signal.flash"), Drawn(box, "flash"));
            Assert.Equal(Visibility.Visible, flash);
            Assert.Equal(Visibility.Collapsed, kiss);
        });
    }

    [Fact]
    public void TheSetActiveBustsSayWhoseBustEachIs()
    {
        WindowHarness.Run(window =>
        {
            var game = VanillaBusts.All.First();
            var pack = PackRepository.CreateEmpty("notes.pack");
            var elf = new CharacterDef { Key = "elf", DisplayName = "Elf" };
            elf.Outfits.Add(new OutfitDef { Key = "party", GameObjectName = "Elf_Party" });
            pack.Characters.Add(elf);
            var vm = OnALine(window, pack);
            if (!vm.BustNameOnlyOptions.Contains(game.GoName)) vm.BustNameOnlyOptions.Add(game.GoName);

            var action = vm.SelectedNode!.AddActionOnFinish();
            action.DisplayType = NodeActionTypes.SetGameObjectActive;
            action.Category = NodeActionViewModel.CatBust;
            WindowHarness.Pump();
            var box = Box(window, action, OptionNotes.Target);

            Assert.Contains("Elf - party", Drawn(box, "Elf_Party"));
            Assert.Contains(game.Character, Drawn(box, game.GoName));
        });
    }

    /// <summary>A Variable condition on a line, reading the game's store or the pack's.</summary>
    private static NodeConditionViewModel VariableCondition(MainViewModel vm, bool vanilla)
    {
        var condition = vm.SelectedNode!.AddCondition();
        condition.DisplayType = NodeConditionViewModel.VariableFamilyType;
        condition.VarSource = vanilla ? "Vanilla" : "Pack";
        WindowHarness.Pump();
        return condition;
    }

    /// <summary>The first item the open list has drawn, and its container.</summary>
    private static (string item, ComboBoxItem container) FirstDrawn(ComboBox box)
    {
        foreach (var item in box.Items)
            if (box.ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem c && c.IsVisible)
                return ((string)item, c);
        throw new Xunit.Sdk.XunitException("the open list drew nothing");
    }

    /// <summary>
    /// The hint under each of the game's variables (1.9.0), measured as drawn:
    /// its text is the variable's own, it sits BELOW the name rather than
    /// beside it, it wraps at a reading width, and it is in the open list only.
    /// The pack's own variables, in the same box switched to the pack, show
    /// none (the control).
    /// </summary>
    [Fact]
    public void TheGamesVariablesSayWhatTheGameDoesWithEach_UnderTheName()
    {
        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("notes.pack");
            pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool, DefaultValue = "false" });
            var vm = OnALine(window, pack);
            var condition = VariableCondition(vm, vanilla: true);
            var box = Box(window, condition, OptionNotes.GameVariable);

            box.IsDropDownOpen = true;
            WindowHarness.Pump();
            box.UpdateLayout();
            var (item, container) = FirstDrawn(box);
            string hint = SMSModForge.Model.VanillaVariableHints.For(item);
            Assert.NotEqual("", hint);
            var texts = Descendants<TextBlock>(container).Where(t => t.IsVisible).ToList();
            _out.WriteLine($"  {item}: {string.Join(" | ", texts.Select(t => t.Text))}");
            var name = texts.First(t => t.Text == item);
            var under = texts.First(t => t.Text == hint);
            double nameBottom = name.TranslatePoint(new Point(0, name.ActualHeight), container).Y;
            var underTop = under.TranslatePoint(new Point(0, 0), container);
            _out.WriteLine($"  name bottom {nameBottom:0.0}, hint top {underTop.Y:0.0} left {underTop.X:0.0}, hint {under.ActualWidth:0} wide");
            Assert.True(underTop.Y >= nameBottom - 0.5, "the hint is drawn under the name, not beside it");
            Assert.True(under.ActualWidth <= 441, "the hint wraps at a reading width");

            // A picture of the open list for a person to look at, when asked for
            // (SMSMODFORGE_SNAPSHOTS, as ThemeSnapshotTests): one per theme.
            string? shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SNAPSHOTS");
            if (!string.IsNullOrWhiteSpace(shots)
                && box.Template.FindName("PART_Popup", box) is System.Windows.Controls.Primitives.Popup { Child: FrameworkElement list })
            {
                Directory.CreateDirectory(shots);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)Math.Ceiling(list.ActualWidth), (int)Math.Ceiling(Math.Min(list.ActualHeight, 600)), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(list);
                ThemeSnapshotTests.Save(bmp, Path.Combine(shots, "variable-hints-" +
                    (Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_THEME") ?? "Light") + ".png"));
            }

            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.DoesNotContain(Descendants<TextBlock>(box).Where(t => t.IsVisible), t => t.Text == hint);

            // The control: the same box on the pack's side has no game hints - a
            // variable of the pack's with no description of its own is its name alone.
            condition.VarSource = "Pack";
            WindowHarness.Pump();
            window.UpdateLayout();
            var packBox = Descendants<ComboBox>(window).First(c => c.IsVisible && ReferenceEquals(c.DataContext, condition)
                                                                  && c.IsEditable && c.Items.Contains("met"));
            Assert.Equal(OptionNotes.PackVariable, OptionNotes.GetKind(packBox));
            packBox.IsDropDownOpen = true;
            WindowHarness.Pump();
            packBox.UpdateLayout();
            var met = (ComboBoxItem)packBox.ItemContainerGenerator.ContainerFromItem("met");
            var metTexts = Descendants<TextBlock>(met).Where(t => t.IsVisible && t.Text.Length > 0).Select(t => t.Text).ToList();
            _out.WriteLine("  met: " + string.Join(" | ", metTexts));
            Assert.Equal(new[] { "met" }, metTexts);
            packBox.IsDropDownOpen = false;
            WindowHarness.Pump();
        });
    }

    /// <summary>
    /// Typing words from a hint finds the variable it describes (the author,
    /// 1.9.0), and only those: a variable whose name and hint both lack the
    /// words is hidden.
    /// </summary>
    [Fact]
    public void TypingAHintsWordsFindsTheGamesVariable()
    {
        WindowHarness.Run(window =>
        {
            var vm = OnALine(window, PackRepository.CreateEmpty("notes.pack"));
            var condition = VariableCondition(vm, vanilla: true);
            var box = Box(window, condition, OptionNotes.GameVariable);
            box.IsDropDownOpen = true;
            WindowHarness.Pump();
            box.UpdateLayout();
            var (first, _) = FirstDrawn(box);

            // A word of Mei-corrupted's hint that is in neither its name nor the first item's.
            const string subject = "Mei-corrupted";
            string hint = SMSModForge.Model.VanillaVariableHints.For(subject);
            string firstHint = SMSModForge.Model.VanillaVariableHints.For(first);
            string word = hint.Split(' ', ',', '.', '(', ')', '\'', ':', ';')
                              .Where(w => w.Length >= 5)
                              .First(w => subject.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0
                                          && first.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0
                                          && firstHint.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0);
            _out.WriteLine($"typed '{word}' (from: {hint}); first item {first}");

            ComboBoxSearch.SetTyped(box, word);
            WindowHarness.Pump();
            box.UpdateLayout();
            ComboBoxItem? Of(string s) => box.ItemContainerGenerator.ContainerFromItem(s) as ComboBoxItem;
            var found = Of(subject)?.Visibility;
            var other = Of(first)?.Visibility;
            ComboBoxSearch.SetTyped(box, "");
            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.Equal(Visibility.Visible, found);
            Assert.Equal(Visibility.Collapsed, other);
        });
    }

    /// <summary>What an open list draws for one item: its visible texts, and
    /// whether the second (the note) sits below the first (the name).</summary>
    private (List<string> texts, bool under) DrawnUnder(ComboBox box, string item)
    {
        box.IsDropDownOpen = true;
        WindowHarness.Pump();
        box.UpdateLayout();
        var container = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(item);
        Assert.NotNull(container);
        var blocks = Descendants<TextBlock>(container).Where(t => t.IsVisible && t.Text.Length > 0).ToList();
        bool under = false;
        if (blocks.Count >= 2)
        {
            double nameBottom = blocks[0].TranslatePoint(new Point(0, blocks[0].ActualHeight), container).Y;
            under = blocks[1].TranslatePoint(new Point(0, 0), container).Y >= nameBottom - 0.5;
        }
        var texts = blocks.Select(t => t.Text).ToList();
        _out.WriteLine($"  {item}: {string.Join(" | ", texts)}{(under ? "  (note under the name)" : "")}");
        box.IsDropDownOpen = false;
        WindowHarness.Pump();
        return (texts, under);
    }

    /// <summary>
    /// The pack's own variables say what they are for (the author, 1.9.0): the
    /// Description from the Variables tab, under the name, in every kind of list
    /// of them - the Variable condition's box, a list parameter, a variable
    /// parameter - as drawn. Controls: a variable with no description is its
    /// name alone; the description drawn after an edit is the NEW one, in a
    /// list that was opened before the edit; a search for the description's
    /// words hides a variable without them.
    /// </summary>
    [Fact]
    public void ThePacksVariablesShowTheirDescriptions_UnderTheName_AndKeepUpWithAnEdit()
    {
        const string metNote = "Set once the player has met Anna at the beach.";
        const string metEdited = "True after the lighthouse picnic, whichever way it went.";
        const string seenNote = "Every room the player has looked into tonight.";
        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("notes.pack");
            pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool, DefaultValue = "false", Description = metNote });
            pack.Variables.Add(new PackVariableDef { Name = "count", Type = PackVariableType.Int, DefaultValue = "0" });
            pack.Variables.Add(new PackVariableDef { Name = "seen", Type = PackVariableType.List, Description = seenNote });
            var vm = OnALine(window, pack);

            // The Variable condition's box, on the pack's side.
            var condition = VariableCondition(vm, vanilla: false);
            var box = Box(window, condition, OptionNotes.PackVariable);
            var (met, metUnder) = DrawnUnder(box, "met");
            Assert.Equal(new[] { "met", metNote }, met);
            Assert.True(metUnder, "the description is drawn under the name, not beside it");
            Assert.Equal(new[] { "count" }, DrawnUnder(box, "count").texts);     // no description: the name alone

            // Closed, the box shows the name it holds and no description.
            condition.VarName = "met";
            WindowHarness.Pump();
            Assert.DoesNotContain(Descendants<TextBlock>(box).Where(t => t.IsVisible), t => t.Text == metNote);

            // Edited in the Variables tab after this list was opened: the list draws the new words.
            vm.Variables.First(v => v.Name == "met").Description = metEdited;
            WindowHarness.Pump();
            Assert.Equal(new[] { "met", metEdited }, DrawnUnder(box, "met").texts);

            // A word of the description finds the variable; one without it is hidden.
            box.IsDropDownOpen = true;
            WindowHarness.Pump();
            ComboBoxSearch.SetTyped(box, "lighthouse");
            WindowHarness.Pump();
            box.UpdateLayout();
            var found = ((ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem("met")).Visibility;
            var other = ((ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem("count")).Visibility;
            ComboBoxSearch.SetTyped(box, "");
            box.IsDropDownOpen = false;
            WindowHarness.Pump();
            Assert.Equal(Visibility.Visible, found);
            Assert.Equal(Visibility.Collapsed, other);

            // A list parameter (Add to list) and a variable parameter (Play dialogue's "when it ends, set").
            var add = vm.SelectedNode!.AddActionOnFinish();
            add.DisplayType = NodeActionTypes.AddToList;
            WindowHarness.Pump();
            var listBox = Box(window, add.ParamRows.First(r => r.Key == "list"), OptionNotes.PackVariable);
            Assert.Equal(new[] { "seen", seenNote }, DrawnUnder(listBox, "seen").texts);

            var play = vm.SelectedNode!.AddActionOnFinish();
            play.DisplayType = NodeActionTypes.PlayDialogue;
            WindowHarness.Pump();
            var afterBox = Box(window, play.ParamRows.First(r => r.Key == "afterName"), OptionNotes.PackVariable);
            Assert.Equal(new[] { "met", metEdited }, DrawnUnder(afterBox, "met").texts);
            Assert.Equal(new[] { "count" }, DrawnUnder(afterBox, "count").texts);
        });
    }

    /// <summary>
    /// The toggle at the top of an open list of variables (the author, 1.9.0):
    /// one tick box that shows or hides every variable's note and, with it,
    /// whether typing searches the notes - measured as drawn, in the game's
    /// list and the pack's. Remembered. Controls: on, the note is drawn and a
    /// word of it finds the variable; a list that is not of variables has no
    /// toggle at all.
    /// </summary>
    [Fact]
    public void TheNotesToggle_HidesTheNotesAndTheirSearch_AndIsOnlyOnListsOfVariables()
    {
        const string said = "True after the lighthouse picnic, whichever way it went.";
        WindowHarness.Run(window =>
        {
            try
            {
                VariableNotes.Instance.Show = true;
                var pack = PackRepository.CreateEmpty("notes.pack");
                pack.Variables.Add(new PackVariableDef { Name = "met", Type = PackVariableType.Bool, DefaultValue = "false", Description = said });
                pack.Variables.Add(new PackVariableDef { Name = "count", Type = PackVariableType.Int, DefaultValue = "0" });
                var vm = OnALine(window, pack);
                var condition = VariableCondition(vm, vanilla: false);
                var box = Box(window, condition, OptionNotes.PackVariable);

                CheckBox Toggle()
                {
                    box.IsDropDownOpen = true;
                    WindowHarness.Pump();
                    box.UpdateLayout();
                    var t = (CheckBox)box.Template.FindName("NotesToggle", box);
                    Assert.NotNull(t);
                    return t;
                }
                Visibility Found(string word, string item)
                {
                    box.IsDropDownOpen = true;
                    WindowHarness.Pump();
                    ComboBoxSearch.SetTyped(box, word);
                    WindowHarness.Pump();
                    box.UpdateLayout();
                    var v = ((ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem(item)).Visibility;
                    ComboBoxSearch.SetTyped(box, "");
                    box.IsDropDownOpen = false;
                    WindowHarness.Pump();
                    return v;
                }

                // On: drawn at the top of the list, ticked; the note drawn; its words found.
                var toggle = Toggle();
                Assert.True(toggle.IsVisible, "no toggle on a list of variables");
                Assert.Equal(true, toggle.IsChecked);
                Assert.Equal(SMSModForge.Localization.Loc.T("dropdown.variableNotes"), toggle.Content);
                var container = (ComboBoxItem)box.ItemContainerGenerator.ContainerFromItem("met");
                double toggleBottom = toggle.TranslatePoint(new Point(0, toggle.ActualHeight), container).Y;
                Assert.True(toggleBottom <= 0.5, "the toggle is not above the list's rows");
                box.IsDropDownOpen = false;
                WindowHarness.Pump();
                Assert.Equal(new[] { "met", said }, DrawnUnder(box, "met").texts);
                Assert.Equal(Visibility.Visible, Found("lighthouse", "met"));

                // Off, ticked as a person ticks it: names only, and typing matches names only.
                toggle = Toggle();
                ((System.Windows.Automation.Provider.IToggleProvider)
                    new System.Windows.Automation.Peers.CheckBoxAutomationPeer(toggle).GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)).Toggle();
                WindowHarness.Pump();
                Assert.True(box.IsDropDownOpen, "ticking the toggle closed the list");
                box.IsDropDownOpen = false;
                WindowHarness.Pump();
                Assert.False(VariableNotes.Instance.Show);
                Assert.False(SMSModForge.Services.EditorPrefs.ShowVariableNotes, "the choice is not remembered");
                Assert.Equal(new[] { "met" }, DrawnUnder(box, "met").texts);
                Assert.Equal(Visibility.Collapsed, Found("lighthouse", "met"));
                Assert.Equal(Visibility.Visible, Found("me", "met"));

                // The game's list follows the same toggle.
                condition.VarSource = "Vanilla";
                WindowHarness.Pump();
                var gameBox = Box(window, condition, OptionNotes.GameVariable);
                gameBox.IsDropDownOpen = true;
                WindowHarness.Pump();
                gameBox.UpdateLayout();
                var (first, firstContainer) = FirstDrawn(gameBox);
                var firstTexts = Descendants<TextBlock>(firstContainer).Where(t => t.IsVisible && t.Text.Length > 0).Select(t => t.Text).ToList();
                _out.WriteLine($"  off, the game's {first}: {string.Join(" | ", firstTexts)}");
                Assert.Equal(new[] { first }, firstTexts);
                Assert.True(((CheckBox)gameBox.Template.FindName("NotesToggle", gameBox)).IsVisible);
                gameBox.IsDropDownOpen = false;
                WindowHarness.Pump();

                // On again: the note is back.
                VariableNotes.Instance.Show = true;
                WindowHarness.Pump();
                condition.VarSource = "Pack";
                WindowHarness.Pump();
                box = Box(window, condition, OptionNotes.PackVariable);
                Assert.Equal(new[] { "met", said }, DrawnUnder(box, "met").texts);

                // Control: a list that is not of variables has no toggle.
                var action = vm.SelectedNode!.AddActionOnFinish();
                WindowHarness.Pump();
                var types = Box(window, action, OptionNotes.ActionType);
                types.IsDropDownOpen = true;
                WindowHarness.Pump();
                types.UpdateLayout();
                var none = (CheckBox)types.Template.FindName("NotesToggle", types);
                Assert.False(none.IsVisible, "a list of action types has the variables' toggle");
                types.IsDropDownOpen = false;
                WindowHarness.Pump();
            }
            finally { VariableNotes.Instance.Show = true; }
        });
    }

    /// <summary>
    /// Under one of the game's busts on the Characters tab, why it is drawn
    /// softer (the author, 1.9.0). Control: a character of the pack's own,
    /// whose art is the pack's, has no such note.
    /// </summary>
    [Fact]
    public void AGameBustSaysWhyItIsSmaller_APacksOwnDoesNot()
    {
        WindowHarness.Run(window =>
        {
            var pack = PackRepository.CreateEmpty("notes.pack");
            var mine = new CharacterDef { Key = "sarah", Name = "Sarah", DisplayName = "Sarah" };
            mine.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Sarah_Casual" });
            pack.Characters.Add(mine);
            PackRepository.Save(pack, _dir);
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            var tabs = (TabControl)window.FindName("MainTabs");
            for (int i = 0; i < tabs.Items.Count; i++)
                if (tabs.Items[i] is TabItem t && (t.Header as string) == "Characters") { tabs.SelectedIndex = i; break; }
            WindowHarness.Pump();

            var game = vm.Characters.First(c => c.Model.IsVanillaCharacter && c.Model.BustSource == BustSource.Vanilla && c.Outfits.Count > 0);
            vm.SelectedCharacter = game;
            WindowHarness.Pump();
            window.UpdateLayout();
            var note = (TextBlock)window.FindName("BustGameArtNote");
            _out.WriteLine($"{game.Display}: bust {vm.VanillaPreviewBustKey}, note shown {note.IsVisible}");
            Assert.False(string.IsNullOrEmpty(vm.VanillaPreviewBustKey));
            Assert.True(note.IsVisible, "no note under the game's bust");
            Assert.Equal(SMSModForge.Localization.Loc.T("preview.gameArt.bust"), note.Text);

            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "sarah");
            WindowHarness.Pump();
            window.UpdateLayout();
            _out.WriteLine($"Sarah: bust {vm.VanillaPreviewBustKey ?? "(the pack's)"}, note shown {note.IsVisible}");
            Assert.False(note.IsVisible, "the note shows under the pack's own character");
        });
    }

    private static List<string> Headings(ComboBox box)
        => box.Items.Groups?.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList() ?? new();

    [Fact]
    public void TheGamesConversationsAreUnderTheirFolders()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var view = vm.AvailableVanillaDialoguesGrouped;
            var headings = view.Groups!.Cast<CollectionViewGroup>().Select(g => (string)g.Name).ToList();
            _out.WriteLine($"{headings.Count} folders, e.g. {string.Join(", ", headings.Take(5))}");
            Assert.True(headings.Count > 5);
            foreach (var g in view.Groups!.Cast<CollectionViewGroup>())
                Assert.All(g.Items.Cast<VanillaDialogueCatalog.Entry>(), e => Assert.Equal((string)g.Name, e.Folder));
        });
    }

    [Fact]
    public void ComponentsAreUnderWhatTheyAre()
    {
        var row = new ComponentRowViewModel(new ComponentDef { Type = PackComponentType.FadeInSprite }, _ => { });
        WindowHarness.Run(_ =>
        {
            var groups = row.ComponentTypesGrouped.Groups!.Cast<CollectionViewGroup>().ToList();
            foreach (var g in groups) _out.WriteLine($"{g.Name}: {g.ItemCount}");
            Assert.Equal(SMSModForge.Localization.Loc.T("componentGroup.pack"), (string)groups[0].Name);
            Assert.Equal(PackComponentType.BuiltIn, groups[0].Items.Cast<string>());
            if (VanillaComponentCatalog.All.Any(e => e.IsEngineComponent))
                Assert.Equal(SMSModForge.Localization.Loc.T("componentGroup.unity"), (string)groups[^1].Name);
        });
    }
}
