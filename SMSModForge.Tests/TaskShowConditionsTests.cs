using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Keeping a task out of the journal until conditions of the pack's own pass -
/// on the pack's tasks and on the game's. Neither the game nor ModForge had
/// this: the game's task has a plain hidden flag its journal reads, and nothing
/// sets it.
/// <para/>
/// Once shown, a task stays shown for the rest of that save unless the author
/// ticks "Hide it again when they stop passing". The runtime half - the
/// remembered list in the pack's save data - cannot run here.
/// </summary>
public sealed class TaskShowConditionsTests
{
    private readonly ITestOutputHelper _out;
    public TaskShowConditionsTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    private static VanillaQuests.VanillaQuest Game
        => VanillaQuests.All.First(q => q.Tasks.Any(t => t.IsTopLevel && q.Tasks.Count(c => c.Parent == t.Id) >= 2));

    private static NodeConditionDef Condition(string variable = "met")
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        c.Params["name"] = variable;
        c.Params["value"] = "true";
        return c;
    }

    private static QuestTaskViewModel OwnTask(out QuestViewModel quest, out ModPack pack)
    {
        pack = new ModPack();
        var def = new QuestDef { Key = "mine", Title = "Mine" };
        pack.Quests.Add(def);
        quest = new QuestViewModel(def);
        quest.AddTaskCommand.Execute(null);
        return quest.SelectedTask!;
    }

    // ── What it stores ────────────────────────────────────────────────

    [Fact]
    public void APacksTaskSavesItsShowConditions()
    {
        var task = OwnTask(out _, out var pack);
        string plain = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("showConditions", plain);
        Assert.DoesNotContain("hideUntilConditions", plain);

        task.HideUntilConditions = true;
        task.ShowConditions.Add();
        task.ShowConditionsLive = true;

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json[json.IndexOf("\"quests\"", StringComparison.Ordinal)..]);
        Assert.Contains("\"hideUntilConditions\": true", json);
        Assert.Contains("\"showConditions\"", json);
        Assert.Contains("\"showConditionsLive\": true", json);

        var back = PackRepository.Deserialize(json)!.Quests.Single().Tasks.Single();
        Assert.True(back.HideUntilConditions);
        Assert.Single(back.ShowConditions);
        Assert.True(back.ShowConditionsLive);

        // Switched off, its conditions are kept for switching back on.
        task.HideUntilConditions = false;
        json = PackRepository.SerializeAsSaved(pack);
        Assert.DoesNotContain("hideUntilConditions", json);
        Assert.Contains("\"showConditions\"", json);
    }

    [Fact]
    public void AGameTaskSavesItsShowConditions()
    {
        var pack = new ModPack();
        var def = new QuestDef { Key = "ext", Source = Game.Name };
        pack.Quests.Add(def);
        var quest = new QuestViewModel(def);
        var row = quest.VanillaTaskRows[0];

        Assert.Contains(VanillaTaskRowViewModel.ShownOnceConditionsPass, row.VisibilityOptions);
        Assert.Contains(VanillaTaskRowViewModel.ShownOnceConditionsPass,
                        quest.VanillaTaskRows.First(r => !r.IsTopLevel).VisibilityOptions);
        Assert.False(row.IsHiddenUntilConditions);

        row.Visibility = VanillaTaskRowViewModel.ShownOnceConditionsPass;
        Assert.True(row.IsHiddenUntilConditions);
        Assert.Contains("hidden until conditions pass", row.RowText);
        Assert.False(row.IsOutOfSight, "a task that can be shown is drawn as never shown");
        var hook = Assert.Single(def.VanillaTasks);
        Assert.Equal(QuestTreeEdits.HiddenUntilConditions, hook.Visibility);

        row.ShowConditions.Add();
        Assert.Single(hook.ShowConditions);

        // Back to the game's way: the conditions are kept, so the entry is too.
        row.Visibility = VanillaTaskRowViewModel.ShownAsTheGameHasIt;
        Assert.False(row.IsHiddenUntilConditions);
        Assert.Same(hook, Assert.Single(def.VanillaTasks));

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json[json.IndexOf("\"vanillaTasks\"", StringComparison.Ordinal)..]);
        Assert.Contains("\"showConditions\"", json);
        Assert.DoesNotContain("\"visibility\"", json);

        // Emptied: nothing left to keep.
        row.ShowConditions.Remove(row.ShowConditions.Items[0]);
        Assert.Empty(def.VanillaTasks);

        // Hiding it again changes nothing about how the quest runs.
        row.Visibility = VanillaTaskRowViewModel.ShownOnceConditionsPass;
        Assert.False(def.ChangesTheGamesTasks);
    }

    [Fact]
    public void TheNoteSaysWhatTheChoicesDo()
    {
        var task = OwnTask(out _, out _);
        task.HideUntilConditions = true;
        _out.WriteLine(task.ShowConditionsNote);
        Assert.Contains("shown at once", task.ShowConditionsNote);

        task.ShowConditions.Add();
        _out.WriteLine(task.ShowConditionsNote);
        Assert.Contains("for the rest of that save", task.ShowConditionsNote);

        task.ShowConditionsLive = true;
        _out.WriteLine(task.ShowConditionsNote);
        Assert.Contains("hidden again whenever they stop", task.ShowConditionsNote);
    }

    // ── What Validate says ────────────────────────────────────────────

    private static List<Validation.ValidationIssue> Check(ModPack pack) => Validation.PackValidator.Validate(pack, "");

    [Fact]
    public void HiddenUntilNoConditionsIsWorthSaying()
    {
        var task = OwnTask(out _, out var pack);
        task.HideUntilConditions = true;
        var one = Assert.Single(Check(pack), i => i.Code == "quest.showConditionsEmpty");
        _out.WriteLine(one.Where + ": " + one.Message);

        task.Model.ShowConditions.Add(Condition());
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.showConditionsEmpty");

        // The same on one of the game's tasks, and the new word is a known one.
        var pack2 = new ModPack();
        var def = new QuestDef { Key = "ext", Source = Game.Name };
        var hook = new VanillaTaskHookDef
        {
            Task = Game.Tasks[0].Id.ToString(CultureInfo.InvariantCulture),
            Visibility = QuestTreeEdits.HiddenUntilConditions,
        };
        def.VanillaTasks.Add(hook);
        pack2.Quests.Add(def);
        var issues = Check(pack2);
        Assert.Single(issues, i => i.Code == "quest.showConditionsEmpty");
        Assert.DoesNotContain(issues, i => i.Code == "quest.badVisibility");
        hook.ShowConditions.Add(Condition());
        Assert.DoesNotContain(Check(pack2), i => i.Code == "quest.showConditionsEmpty");
    }

    [Fact]
    public void ShowConditionsAreCheckedAndRenamedLikeEveryOtherList()
    {
        var pack = new ModPack();
        pack.Variables.Add(new PackVariableDef { Name = "met" });
        var def = new QuestDef { Key = "ext", Source = Game.Name };
        var hook = new VanillaTaskHookDef
        {
            Task = Game.Tasks[0].Id.ToString(CultureInfo.InvariantCulture),
            Visibility = QuestTreeEdits.HiddenUntilConditions,
        };
        var onGame = Condition();
        hook.ShowConditions.Add(onGame);
        def.VanillaTasks.Add(hook);
        var added = new AddedTaskDef { Key = "Ask", Name = "Ask", HideUntilConditions = true };
        var onMine = Condition();
        added.ShowConditions.Add(onMine);
        def.AddedTasks.Add(added);
        pack.Quests.Add(def);

        var walked = Services.PackWalk.Conditions(pack).Select(c => c.Condition).ToList();
        Assert.Contains(onGame, walked);
        Assert.Contains(onMine, walked);

        int renamed = Services.VariableRenamer.RenameReferences(pack, "met", "seen");
        Assert.Equal(2, renamed);
        Assert.Equal("seen", onGame.Params["name"]);
        Assert.Equal("seen", onMine.Params["name"]);
        pack.Variables[0].Name = "seen";
        Assert.Contains(Services.VariableRenamer.FindReferences(pack, "seen"), h => h.Contains("ext"));

        // A quest condition inside them is checked like any other.
        var bad = new NodeConditionDef { Type = NodeConditionTypes.QuestState };
        bad.Params["quest"] = "NoSuchQuest";
        hook.ShowConditions.Add(bad);
        var one = Assert.Single(Check(pack), i => i.Code == "quest.unknownQuest");
        _out.WriteLine(one.Where);
        Assert.Contains("vanillaTasks", one.Where);
    }

    // ── On screen ─────────────────────────────────────────────────────

    [Fact]
    public void BothPanelsOfferIt()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;

            // A top-level task of the pack's own: the tick is there even
            // though "Hidden until it starts" is not.
            vm.AddQuestCommand.Execute(null);
            vm.SelectedQuest!.AddTaskCommand.Execute(null);
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var untilStarted = (CheckBox)window.FindName("QuestTaskHiddenBox");
            var untilConditions = (CheckBox)window.FindName("QuestTaskHiddenUntilConditionsBox");
            var list = (FrameworkElement)window.FindName("QuestTaskShowConditions");
            Assert.False(untilStarted.IsVisible, "a top-level task offers 'Hidden until it starts'");
            Assert.True(untilConditions.IsVisible, "a top-level task does not offer 'Hidden until conditions pass'");
            Assert.False(list.IsVisible);

            untilConditions.IsChecked = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            Assert.True(list.IsVisible, "ticking it does not show its conditions");
            Assert.True(((CheckBox)window.FindName("QuestTaskShowLiveBox")).IsVisible);
            Assert.True(vm.SelectedQuest.SelectedTask!.Model.HideUntilConditions);
            Assert.False(((CheckBox)window.FindName("QuestTaskShowLiveBox")).IsChecked == true,
                         "a task follows its conditions live without being asked to");

            // One of the game's tasks.
            vm.AddVanillaQuestCommand.Execute(null);
            vm.SelectedQuest!.Source = Game.Name;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var picker = (ComboBox)window.FindName("VanillaTaskVisibilityPicker");
            var gameList = (FrameworkElement)window.FindName("VanillaTaskShowConditions");
            Assert.False(gameList.IsVisible);
            picker.SelectedItem = VanillaTaskRowViewModel.ShownOnceConditionsPass;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            Assert.True(gameList.IsVisible, "choosing it does not show the conditions");
            Assert.Equal(QuestTreeEdits.HiddenUntilConditions, vm.Pack.Quests.Last().VanillaTasks.Single().Visibility);
        });
    }
}
