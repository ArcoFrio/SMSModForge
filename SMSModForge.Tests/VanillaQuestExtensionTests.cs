using System;
using System.Linq;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack saying something about one of the game's own quests: the paragraph
/// the journal shows, how that paragraph changes as the game's tasks are done,
/// and what runs when one of them is.
/// <para/>
/// Nothing here changes the quest's shape - adding and taking out tasks is
/// <see cref="VanillaQuestTreeTests"/>. These hold the editor's half of what a
/// pack says about the quest: what is written into the manifest, and what the
/// tab offers. The plugin's half is read off the game's own IL and cannot run
/// here.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class VanillaQuestExtensionTests
{
    private readonly ITestOutputHelper _out;
    public VanillaQuestExtensionTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    /// <summary>One of the game's quests with tasks to hang things on. Taken
    /// from the catalogue rather than written out, so regenerating it from a
    /// newer dump cannot leave these testing a quest that is gone.</summary>
    private static VanillaQuests.VanillaQuest Theirs
        => VanillaQuests.All.First(q => q.Tasks.Count >= 3);

    /// <summary>
    /// Another of the game's quests, sharing no task id with the first.
    /// <para/>
    /// Not just "a different quest": the game reuses four task ids across
    /// twenty-seven of its quests, so two quests picked at random very likely
    /// have ids in common - and an entry keyed by one of those would survive
    /// being pointed from one to the other, which is not what the pruning is
    /// about.
    /// </summary>
    private static VanillaQuests.VanillaQuest Another
        => VanillaQuests.All.First(q => q.Tasks.Count >= 2
                                        && !string.Equals(q.Name, Theirs.Name, StringComparison.Ordinal)
                                        && q.Tasks.All(t => Theirs.Task(t.Id) == null));

    /// <summary>An extension of one of the game's quests, on a pack.</summary>
    private static (ModPack Pack, QuestViewModel Quest) Extension()
    {
        var pack = new ModPack();
        var def = new QuestDef { Key = "theirs", Title = "", Source = Theirs.Name };
        pack.Quests.Add(def);
        return (pack, new QuestViewModel(def));
    }

    // ── What it stores ────────────────────────────────────────────────

    [Fact]
    public void ItSavesWhatThePackSaysAndNothingOfTheGames()
    {
        var (pack, quest) = Extension();
        quest.Description = "Something is off about all this.";
        var row = quest.VanillaTaskRows[1];
        row.QuestDescription = "Marco knows more than he says.";
        row.Actions.Add();

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json[json.IndexOf("\"quests\"", StringComparison.Ordinal)..]);

        Assert.Contains($"\"source\": \"{Theirs.Name}\"", json);
        Assert.Contains($"\"task\": \"{row.Token}\"", json);
        Assert.Contains("Marco knows more than he says.", json);

        // None of the game's own text is copied into the pack: only the id of
        // the task, so a patch that rewrites its line changes nothing here.
        Assert.DoesNotContain(Theirs.Description, json);
        Assert.DoesNotContain(quest.VanillaTaskRows[1].Name, json);

        var back = PackRepository.Deserialize(json)!.Quests.Single();
        Assert.True(back.IsVanillaExtension);
        Assert.Equal(Theirs.Name, back.Source);
        var hook = Assert.Single(back.VanillaTasks);
        Assert.Equal(row.Token, hook.Task);
        Assert.Single(hook.Actions);
    }

    [Fact]
    public void ARowNobodyWroteOnIsNotInTheManifest()
    {
        // Clicking through the game's tasks must not grow the file: an entry
        // exists only while it says something.
        var (pack, quest) = Extension();
        var row = quest.VanillaTaskRows[0];

        Assert.Empty(quest.Model.VanillaTasks);

        row.QuestDescription = "Now I have the letter.";
        Assert.Single(quest.Model.VanillaTasks);

        row.QuestDescription = "";
        Assert.Empty(quest.Model.VanillaTasks);

        var action = row.Actions.Add();
        Assert.Single(quest.Model.VanillaTasks);

        row.Actions.Remove(action);
        _out.WriteLine($"entries left: {quest.Model.VanillaTasks.Count}");
        Assert.Empty(quest.Model.VanillaTasks);
    }

    [Fact]
    public void TheEntriesFollowTheGamesOwnTaskOrder()
    {
        // The runtime reads them in order and lets the furthest-down done task
        // win, so the order in the file has to be the journal's.
        var (pack, quest) = Extension();
        var rows = quest.VanillaTaskRows;
        Assert.True(rows.Count >= 3, "this quest has too few tasks to prove an order");

        rows[2].QuestDescription = "third";
        rows[0].QuestDescription = "first";
        rows[1].QuestDescription = "second";

        var written = quest.Model.VanillaTasks.Select(h => h.Task).ToList();
        _out.WriteLine(string.Join(" | ", written));
        Assert.Equal(new[] { rows[0].Token, rows[1].Token, rows[2].Token }, written);
    }

    [Fact]
    public void PointingItAtAnotherQuestDropsWhatCannotApply()
    {
        // An entry names a task by id, and the game reuses ids between quests -
        // so what survives is what the new quest also has, and nothing else.
        var (pack, quest) = Extension();
        var gone = Theirs.Tasks.First(t => Another.Task(t.Id) == null);
        var row = quest.VanillaTaskRows.Single(
            r => r.Token == gone.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        row.QuestDescription = "for the first quest";
        Assert.Single(quest.Model.VanillaTasks);

        quest.Source = Another.Name;

        _out.WriteLine($"now {quest.Source}, {quest.Model.VanillaTasks.Count} entr(ies), "
                       + $"{quest.VanillaTaskRows.Count} row(s)");
        Assert.DoesNotContain(quest.Model.VanillaTasks, h => h.Task == row.Token);
        Assert.All(quest.Model.VanillaTasks, h => Assert.NotNull(Another.Task(h.Task)));
        Assert.Equal(Another.Tasks.Count, quest.VanillaTaskRows.Count);
    }

    [Fact]
    public void TheRowsAreTheGamesTasksAsTheJournalListsThem()
    {
        var (_, quest) = Extension();
        Assert.Equal(Theirs.Tasks.Count, quest.VanillaTaskRows.Count);

        for (int i = 0; i < Theirs.Tasks.Count; i++)
        {
            var task = Theirs.Tasks[i];
            var row = quest.VanillaTaskRows[i];
            Assert.Equal(task.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Token);
            Assert.Equal(Theirs.DepthOf(task), row.Depth);
            if (!string.IsNullOrWhiteSpace(task.Name)) Assert.Equal(task.Name, row.Name);
        }

        // The row says what the pack has added to it, so a list of thirty is
        // readable at a glance.
        var one = quest.VanillaTaskRows[0];
        Assert.DoesNotContain("(", one.RowText);
        one.QuestDescription = "x";
        Assert.Contains("(description)", one.RowText);
        one.Actions.Add();
        Assert.Contains("(description, actions)", one.RowText);
    }

    [Fact]
    public void TheKeyFollowsTheQuestThatWasPicked()
    {
        var vm = new MainViewModel();
        vm.AddVanillaQuestCommand.Execute(null);
        var quest = vm.SelectedQuest!;
        Assert.True(quest.WantsVanilla);
        Assert.True(quest.ShowsVanillaPanel);

        quest.Source = Theirs.Name;

        _out.WriteLine($"key '{quest.Key}' for '{quest.Source}'");
        Assert.True(quest.IsVanillaExtension);
        Assert.NotEqual("", quest.Key);
        Assert.DoesNotContain(" ", quest.Key);
        Assert.Contains("the game's", quest.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExtensionIsNotOfferedAsAQuestOfThePacks()
    {
        // A row pointing at the game's quest names it on the Vanilla side, by
        // the game's own name. Listing the extension among the pack's quests
        // would be a second way to name the same thing that resolves to nothing.
        var vm = new MainViewModel();
        vm.AddQuestCommand.Execute(null);
        string mine = vm.SelectedQuest!.Key;
        vm.AddVanillaQuestCommand.Execute(null);
        vm.SelectedQuest!.Source = Theirs.Name;
        string extension = vm.SelectedQuest.Key;

        var ps = new System.Collections.Generic.Dictionary<string, string>();
        var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: true);
        var offered = picker.QuestOptions.Select(o => o.Token).ToList();

        _out.WriteLine("pack quests offered: " + string.Join(", ", offered));
        Assert.Contains(mine, offered);
        Assert.DoesNotContain(extension, offered);
    }

    // ── What Validate says ────────────────────────────────────────────

    private static System.Collections.Generic.List<Validation.ValidationIssue> Check(ModPack pack)
        => Validation.PackValidator.Validate(pack, "");

    private static QuestDef NewExtension(ModPack pack, string key, string source)
    {
        var def = new QuestDef { Key = key, Title = "", Source = source, Description = "changed" };
        pack.Quests.Add(def);
        return def;
    }

    [Fact]
    public void AQuestTheGameDoesNotHaveIsWorthSaying()
    {
        var pack = new ModPack();
        NewExtension(pack, "ext", "Not A Quest");

        var one = Assert.Single(Check(pack), i => i.Code == "quest.unknownVanillaSource");
        _out.WriteLine(one.Message);
        Assert.Equal(Validation.Severity.Warning, one.Severity);
    }

    [Fact]
    public void AnEntryForATaskThatQuestDoesNotHaveIsWorthSaying()
    {
        var pack = new ModPack();
        var def = NewExtension(pack, "ext", Theirs.Name);
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = "123456", QuestDescription = "never" });

        var one = Assert.Single(Check(pack), i => i.Code == "quest.unknownVanillaTask");
        _out.WriteLine(one.Message);

        // ...and the same entry on a task it DOES have says nothing.
        def.VanillaTasks[0].Task = Theirs.Tasks[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.unknownVanillaTask");
    }

    [Fact]
    public void AnEntryThatChangesNothingIsWorthSaying()
    {
        var pack = new ModPack();
        var def = NewExtension(pack, "ext", Theirs.Name);
        def.Description = "";

        var one = Assert.Single(Check(pack), i => i.Code == "quest.extensionSaysNothing");
        _out.WriteLine(one.Message);

        def.Description = "now it says something";
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.extensionSaysNothing");
    }

    [Fact]
    public void TwoEntriesForOneQuestAreWorthSaying()
    {
        var pack = new ModPack();
        NewExtension(pack, "aaa", Theirs.Name);
        NewExtension(pack, "bbb", Theirs.Name);

        var one = Assert.Single(Check(pack), i => i.Code == "quest.twoExtensions");
        _out.WriteLine(one.Message);
    }

    [Fact]
    public void TasksOfItsOwnOnAnExtensionAreWorthSaying()
    {
        // A hand-edited manifest, or a pack quest somebody pointed at one of
        // the game's: its tasks are not added to the game's quest, and nothing
        // reads them.
        var pack = new ModPack();
        var def = NewExtension(pack, "ext", Theirs.Name);
        def.Tasks.Add(new QuestTaskDef { Key = "mine", Name = "Mine" });

        var one = Assert.Single(Check(pack), i => i.Code == "quest.extensionHasOwnTasks");
        _out.WriteLine(one.Message);

        // The checks a quest of the pack's own gets do not fire on it: it has
        // no title and no tasks of its own by design.
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.noTitle" || i.Code == "quest.noTasks");
    }

    // ── The rest of the editor sees those actions ─────────────────────

    [Fact]
    public void TheActionsOnTheGamesTasksAreWalkedLikeEveryOther()
    {
        var pack = new ModPack();
        pack.Variables.Add(new PackVariableDef { Name = "clue" });
        var def = NewExtension(pack, "ext", Theirs.Name);
        var hook = new VanillaTaskHookDef
        {
            Task = Theirs.Tasks[0].Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var action = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        action.Params["name"] = "clue";
        action.Params["value"] = "found";
        hook.Actions.Add(action);
        def.VanillaTasks.Add(hook);

        // The walker, which the renamers and half the checks run on.
        var walked = Services.PackWalk.Actions(pack).Select(a => a.Action).ToList();
        Assert.Contains(action, walked);

        // ...so renaming the variable reaches it.
        int renamed = Services.VariableRenamer.RenameReferences(pack, "clue", "lead");
        _out.WriteLine($"{renamed} reference(s) renamed; now '{action.Params["name"]}'");
        Assert.True(renamed > 0);
        Assert.Equal("lead", action.Params["name"]);

        // ...and the places that report a variable's use count it.
        pack.Variables[0].Name = "lead";
        var where = Services.VariableRenamer.FindReferences(pack, "lead");
        _out.WriteLine("used by: " + string.Join(", ", where));
        Assert.Contains(where, h => h.Contains("ext", StringComparison.Ordinal));
    }

    // ── On screen ─────────────────────────────────────────────────────

    [Fact]
    public void TheTabShowsTheGamesQuestInsteadOfThePacksOwnFields()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            WindowHarness.Pump();

            vm.AddVanillaQuestCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var vanillaPanel = (GroupBox)window.FindName("VanillaQuestPanel");
            var titleBox = (TextBox)window.FindName("QuestTitleBox");
            var picker = (ComboBox)window.FindName("QuestSourcePicker");

            _out.WriteLine($"vanilla panel {vanillaPanel.IsVisible}, title box {titleBox.IsVisible}");
            Assert.True(vanillaPanel.IsVisible, "the panel for one of the game's quests is not on screen");
            Assert.False(titleBox.IsVisible, "the pack quest's own fields are on screen for an extension");

            // Every quest ModForge knows is on offer, by the name the game uses.
            var offered = picker.ItemsSource.Cast<NavigatorTargetOption>().Select(o => o.Token).ToList();
            Assert.Equal(VanillaQuests.All.Count, offered.Count);
            Assert.Contains(Theirs.Name, offered);

            // Choosing one fills the panel in.
            picker.Text = Theirs.Name;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var shown = (TextBox)window.FindName("VanillaQuestDescriptionShown");
            var list = (ListBox)window.FindName("VanillaTaskList");
            _out.WriteLine($"the game says: '{shown.Text[..Math.Min(40, shown.Text.Length)]}...', {list.Items.Count} task row(s)");
            Assert.Equal(Theirs.Description, shown.Text);
            Assert.True(shown.IsReadOnly, "the game's own description can be typed over");
            Assert.Equal(Theirs.Tasks.Count, list.Items.Count);

            // ...and what is typed reaches the pack.
            var describe = (TextBox)window.FindName("VanillaQuestDescriptionBox");
            describe.Text = "Something is off about all this.";
            var task = (TextBox)window.FindName("VanillaTaskDescriptionBox");
            Assert.True(task.IsVisible, "no box for the selected task of the game's");
            task.Text = "The letter was signed M.";
            WindowHarness.Pump();

            var def = vm.Pack.Quests.Single();
            Assert.Equal(Theirs.Name, def.Source);
            Assert.Equal("Something is off about all this.", def.Description);
            var hook = Assert.Single(def.VanillaTasks);
            Assert.Equal("The letter was signed M.", hook.QuestDescription);
            Assert.Equal(vm.SelectedQuest!.VanillaTaskRows[0].Token, hook.Task);
        });
    }

    [Fact]
    public void ThePacksOwnQuestStillShowsItsOwnFields()
    {
        // The control for the panel above: + Quest is unchanged.
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddQuestCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            Assert.True(((TextBox)window.FindName("QuestTitleBox")).IsVisible);
            Assert.False(((GroupBox)window.FindName("VanillaQuestPanel")).IsVisible);
            Assert.False(((GroupBox)window.FindName("VanillaTasksPanel")).IsVisible);
        });
    }
}
