using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the game itself does with its quests - what starts them, what
/// completes, counts or fails their tasks - and when each task starts, shown
/// read-only beside a pack's changes to one.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class GameQuestSitesTests
{
    private readonly ITestOutputHelper _out;
    public GameQuestSitesTests(ITestOutputHelper o) => _out = o;

    private const int TabDialogues = 5, TabQuests = 13;

    private const string Himari = "Dinner Boyfriend (Himari)";
    private const string HimariFirstDinner = "1070060591";
    private const string HimariStartTalk = "8_Room_Talk/Entrance/HiramiQuestStart";

    private const string Astrid = "Astrid Quest";
    private const string AstridMeet = "276722473", AstridLair = "1321869430", AstridWaiting = "1890282631",
                         AstridHelp = "1745766144";

    private const string City = "Explore The City";
    private const string CityExplore = "1802849953", CityMall = "-1625034000";

    // ── The file ──────────────────────────────────────────────────────

    [Fact]
    public void TheFileShipsAndNamesOnlyWhatTheCatalogueHas()
    {
        Assert.True(VanillaQuestReferences.IsAvailable, "references.json did not reach the build output");
        var quests = VanillaQuestReferences.Quests.ToList();
        _out.WriteLine(quests.Count + " quest(s)");
        Assert.True(quests.Count >= 30, "only " + quests.Count + " quests have anything recorded");

        foreach (string name in quests)
        {
            var game = VanillaQuests.Find(name);
            Assert.True(game != null, "the file names '" + name + "', which the quest catalogue does not list");
            var ids = game!.Tasks.Select(t => ExtensionTree.Token(t.Id)).ToHashSet();
            foreach (string task in VanillaQuestReferences.For(name)!.Tasks.Keys)
                Assert.True(ids.Contains(task), name + " has no task " + task);
        }
    }

    [Fact]
    public void AQuestOnlyAScriptStartsIsInTheCatalogue()
    {
        // Started from the staging around a conversation rather than from a
        // line of one, which is why an earlier catalogue left it out.
        const string oldFriends = "Old Friends (Charlotte)";
        Assert.NotNull(VanillaQuests.Find(oldFriends));
        var starts = VanillaQuestReferences.For(oldFriends)!.Starts;
        Assert.NotEmpty(starts);
        Assert.All(starts, s => Assert.Equal("InstructionQuestsActivate", s.Step.Type));
    }

    [Fact]
    public void TheCoverageNoteFollowsWhatWasRead()
    {
        // Said while the scene half is missing, and not once it is there.
        Assert.Equal(!VanillaQuestReferences.IsComplete, GameQuestSites.CoverageNote.Length > 0);
    }

    // ── Sites ─────────────────────────────────────────────────────────

    [Fact]
    public void AConversationSiteSaysWhereWhenAndWhatItNeeds()
    {
        var site = Assert.Single(
            GameQuestSites.ForQuest(Himari).Single(g => g.Heading == "Started by").Sites,
            s => s.Site.Dialogue == HimariStartTalk);
        _out.WriteLine(site.What + " | " + site.Where + " | " + site.When + " | " + site.Path);

        Assert.Equal("Starts the quest", site.What);
        Assert.Equal("In the conversation " + HimariStartTalk, site.Where);
        Assert.StartsWith("As this line appears: ", site.When);
        Assert.Contains("Good. Don’t embarrass me.", site.When);
        Assert.StartsWith("Reached through: ", site.Path);
        Assert.Contains("Well? Are you in or not?", site.Path);
        Assert.True(site.CanOpenConversation);
        Assert.Equal("", site.Untranslated);

        // What the room checks before playing it: the room, and its two
        // variables, all at once.
        var gate = Assert.Single(site.Plays).Model;
        Assert.Equal(NodeConditionTypes.GroupAll, gate.Type);
        Assert.Contains(gate.Conditions, c => c.Type == NodeConditionTypes.LevelActive);
        Assert.Contains(gate.Conditions, c => c.Params.GetValueOrDefault("name") == "himari-homequest");
        Assert.Contains(gate.Conditions, c => c.Params.GetValueOrDefault("name") == "himari-first-dialogue-when-rich");
        Assert.All(site.Plays, p => Assert.True(p.IsLocked));
    }

    [Fact]
    public void AScriptSiteSaysWhichScriptAndWhenItRuns()
    {
        var site = GameQuestSites.ForQuest("Cryptids (Emma)").Single(g => g.Heading == "Started by").Sites
            .Single(s => !s.Site.IsDialogue);
        _out.WriteLine(site.Where);

        Assert.Equal("The Conditions script on 8_Room_Talk/Garage, once the conversation "
                     + "8_Room_Talk/Garage/EmmaCryptidHuntStart it plays has finished", site.Where);
        Assert.Equal("", site.When);
        Assert.Empty(site.Plays);
        Assert.Contains(site.Reached, c => c.Model.Params.GetValueOrDefault("name") == "emma-cryptidhuntquest");
        Assert.Contains(site.Reached, c => c.Model.Params.GetValueOrDefault("name") == "Mario-In-Prison");
        Assert.Equal("Only if:", site.ReachedLabel);
        Assert.Equal("8_Room_Talk/Garage/EmmaCryptidHuntStart", site.Conversation);
        Assert.True(site.CanOpenConversation);
    }

    [Fact]
    public void ACounterSaysHowMuchItAdds()
    {
        var group = Assert.Single(GameQuestSites.ForTask(Astrid, AstridHelp));
        Assert.Equal("Counted by", group.Heading);
        Assert.All(group.Sites, s => Assert.Equal("Adds 1 to its count", s.What));
    }

    [Fact]
    public void AStepBetweenTwoConversationsSaysBoth()
    {
        // The Garage plays four of these in a row, and the first task is
        // completed between the second and the third.
        var site = Assert.Single(Assert.Single(GameQuestSites.ForTask("Old Friends (Charlotte)", "1813187919")).Sites);
        _out.WriteLine(site.Where);
        Assert.Equal("The Conditions script on 8_Room_Talk/Garage, once the conversation "
                     + "8_Room_Talk/Garage/CharlotteAgentInsideVent it plays has finished, and just before it plays "
                     + "the conversation 8_Room_Talk/Garage/CharlotteAgentBedroom", site.Where);
        Assert.Equal("8_Room_Talk/Garage/CharlotteAgentInsideVent", site.Conversation);

        // One step before eight conversations is one step, not eight.
        var debt = Assert.Single(GameQuestSites.ForQuest("Debt").Single(g => g.Heading == "Started by").Sites);
        Assert.EndsWith(", just before it plays the conversation 8_Core_Events/Intro/Dialogue1", debt.Where);
        Assert.Equal("8_Core_Events/Intro/Dialogue1", debt.Conversation);
    }

    [Fact]
    public void TheWaitIsReadNotAssumed()
    {
        Assert.Equal(", once the conversation D it plays has finished", Around(true));
        Assert.Equal(", straight after it starts the conversation D, without waiting for it to finish", Around(false));
        Assert.Equal(", after the step that plays the conversation D", Around(null));
        Assert.Equal(", after the step that plays the conversation (one it picks while the game runs)",
                     Around(null, dialogue: null));

        static string Around(bool? waits, string? dialogue = "D")
        {
            var site = new VanillaQuestReferences.Site
            {
                Via = "script", By = "X", Script = "Actions",
                AroundDialogue = new VanillaQuestReferences.Around
                {
                    After = new VanillaQuestReferences.Play { Dialogue = dialogue, Waits = waits },
                },
                Step = new VanillaQuestReferences.Brief { Type = "InstructionQuestsActivate" },
            };
            return new GameQuestSiteViewModel(site).Where.Substring("The Actions script on X".Length);
        }
    }

    [Fact]
    public void TheListsAddUpToWhatTheGameHas()
    {
        // Every quest step in the game's one scene with any, counted by the
        // type name Game Creator writes into the scene file for each
        // (2026-09-17). With the scene read, the lists must come to exactly
        // this - more means a step listed twice, fewer means one missed.
        Assert.True(VanillaQuestReferences.IsComplete, "the shipped file was built without the scene");

        int starts = 0, resets = 0, completes = 0, counts = 0, fails = 0;
        foreach (string name in VanillaQuestReferences.Quests)
        {
            var quest = VanillaQuestReferences.For(name)!;
            starts += quest.Starts.Count;
            resets += quest.Resets.Count;
            foreach (var task in quest.Tasks.Values)
            {
                completes += task.Completes.Count;
                counts += task.Counts.Count;
                fails += task.Fails.Count;
            }
        }
        Assert.Equal((44, 9, 109, 15, 3), (starts, resets, completes, counts, fails));
    }

    [Fact]
    public void AConditionWithNoEquivalentIsStillSaid()
    {
        var site = new VanillaQuestReferences.Site
        {
            Via = "script", By = "X", Script = "Trigger", Event = "EventOnEnable",
            When =
            {
                new VanillaDialogueCatalog.Step { Type = "ConditionSomethingNew", Title = "Is the moon full" },
            },
            Step = new VanillaQuestReferences.Brief { Type = "InstructionQuestsTaskComplete" },
        };
        var row = new GameQuestSiteViewModel(site);
        _out.WriteLine(row.Where + " | " + row.Untranslated);
        Assert.Equal("The Trigger script on X (set off by “On Enable”)", row.Where);
        Assert.Empty(row.Reached);
        Assert.Contains("Is the moon full", row.Untranslated);
        Assert.False(row.CanOpenConversation);
    }

    [Fact]
    public void AnUnknownQuestHasNothingAndSaysSo()
    {
        Assert.Empty(GameQuestSites.ForQuest("No Such Quest"));
        Assert.Empty(GameQuestSites.ForTask(Himari, "12345"));
        Assert.Equal("Nothing ModForge has read starts this quest.", GameQuestSites.QuestNote("No Such Quest"));
        Assert.Equal("", GameQuestSites.QuestNote(Himari));
    }

    // ── Task notes ───────────────────────────────────────────────────

    [Fact]
    public void TheTaskNoteSaysWhatCompletesIt()
    {
        // Completed by the game.
        Assert.Equal("A step here only takes effect while the task is in progress.",
                     GameQuestSites.TaskNote(Himari, HimariFirstDinner, throughSubtasks: true, counts: false));
        // Counted to its end, which completes it.
        Assert.Equal("A step here only takes effect while the task is in progress. "
                     + "A count that reaches its target completes the task.",
                     GameQuestSites.TaskNote(Astrid, AstridHelp, throughSubtasks: true, counts: true));
        // Counted by scripts in the scene - visiting the beach, the mall, the park.
        Assert.Equal("A step here only takes effect while the task is in progress. "
                     + "A count that reaches its target completes the task.",
                     GameQuestSites.TaskNote(City, CityExplore, throughSubtasks: true, counts: true));
        Assert.Equal(3, Assert.Single(GameQuestSites.ForTask(City, CityExplore)).Sites.Count);
        // Nothing names it, and its subtasks finish it.
        Assert.Equal("Nothing ModForge has read completes this task directly: it completes when its subtasks do.",
                     GameQuestSites.TaskNote("Behind The Looking Glass (Alice)", "1813187919",
                                             throughSubtasks: true, counts: false));
        // Nothing names it at all.
        Assert.Equal("Nothing ModForge has read completes this task.",
                     GameQuestSites.TaskNote(Astrid, AstridLair, throughSubtasks: false, counts: false));
    }

    // ── When a task starts ───────────────────────────────────────────

    private static QuestViewModel Extension(string quest, System.Action<QuestDef>? change = null)
    {
        var def = new QuestDef { Key = "ext", Source = quest };
        change?.Invoke(def);
        return new QuestViewModel(def);
    }

    private static string StartsOf(QuestViewModel quest, string token)
        => quest.VanillaTaskRows.Single(r => r.Token == token).StartsNote;

    [Fact]
    public void TasksInOrderStartOneAfterAnother()
    {
        var quest = Extension(Astrid);
        foreach (var row in quest.VanillaTaskRows) _out.WriteLine(row.Token + ": " + row.StartsNote);

        Assert.Equal("Starts when the quest does.", StartsOf(quest, AstridMeet));
        Assert.Equal("Starts once “Meet with Astrid.” is done.", StartsOf(quest, AstridHelp));
        Assert.Equal("Starts when “Meet with Astrid.” does.", StartsOf(quest, AstridLair));
        Assert.Equal("Starts once “Lady Noire's Lair is near the harbor.” is done.", StartsOf(quest, AstridWaiting));
    }

    [Fact]
    public void ATaskTakenOutIsSkippedOnTheWay()
    {
        var quest = Extension(Astrid);
        quest.VanillaTaskRows.Single(r => r.Token == AstridMeet).IsRemoved = true;

        Assert.Equal("Starts when the quest does - “Meet with Astrid.”, which your pack takes out, "
                     + "is skipped on the way.", StartsOf(quest, AstridHelp));
        // Taken out, it says so elsewhere; nor does anything under it start.
        Assert.Equal("", StartsOf(quest, AstridMeet));
        Assert.Equal("", StartsOf(quest, AstridLair));
    }

    [Fact]
    public void SubtasksInAnyOrderStartWithTheirTask()
    {
        var quest = Extension(City);
        Assert.Equal("Starts when “Explore Peak City” does, together with every subtask beside it.",
                     StartsOf(quest, CityMall));
    }

    [Fact]
    public void ThePacksTasksAreReadTheSameWay()
    {
        var quest = Extension(Astrid, def =>
        {
            // Between the first two of the game's, and one completed by an
            // action with a subtask under it.
            def.AddedTasks.Add(new AddedTaskDef { Key = "docks", Name = "Ask around the docks", Before = AstridHelp });
            var manual = new AddedTaskDef { Key = "manual", Name = "Wait for a call", Completion = QuestVocabulary.ByAction };
            manual.Subtasks.Add(new QuestTaskDef { Key = "phone", Name = "Keep the phone on" });
            def.AddedTasks.Add(manual);
        });
        var docks = quest.ExtensionRows.OfType<QuestTaskViewModel>().Single(t => t.Key == "docks");
        var phone = quest.ExtensionRows.OfType<QuestTaskViewModel>().Single(t => t.Key == "phone");

        Assert.Equal("Starts once “Meet with Astrid.” is done.", docks.StartsNote);
        Assert.Equal("Starts once “Ask around the docks” is done.", StartsOf(quest, AstridHelp));
        Assert.Equal("Never starts: “Wait for a call” is completed by an action, and the game starts no "
                     + "subtasks under a task like that.", phone.StartsNote);

        // It follows a change of how its task completes.
        var manualRow = quest.ExtensionRows.OfType<QuestTaskViewModel>().Single(t => t.Key == "manual");
        var raised = new List<string?>();
        phone.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        manualRow.Completion = QuestVocabulary.InOrder;
        Assert.Contains(nameof(QuestTaskViewModel.StartsNote), raised);
        Assert.Equal("Starts when “Wait for a call” does.", phone.StartsNote);

        // A quest of the pack's own says nothing: its panel explains the order.
        var own = new QuestViewModel(new QuestDef { Key = "own", Tasks = { new QuestTaskDef { Key = "a", Name = "A" } } });
        Assert.Equal("", own.TaskRows.Single().StartsNote);
    }

    // ── On screen ─────────────────────────────────────────────────────

    [Fact]
    public void ThePanelsShowTheGamesStepsAndOpenTheConversation()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var tabs = (TabControl)window.FindName("MainTabs");
            tabs.SelectedIndex = TabQuests;
            vm.AddVanillaQuestCommand.Execute(null);
            var quest = vm.SelectedQuest!;
            quest.Source = Himari;
            quest.SelectedExtensionRow = quest.VanillaTaskRows.Single(r => r.Token == HimariFirstDinner);
            Settle(window);

            var questSites = (ItemsControl)window.FindName("GameQuestSiteGroups");
            var taskSites = (ItemsControl)window.FindName("VanillaTaskSiteGroups");
            var starts = (TextBlock)window.FindName("VanillaTaskStartsNote");
            Assert.True(((FrameworkElement)window.FindName("GameQuestSitesPanel")).IsVisible);
            Assert.True(questSites.IsVisible && questSites.Items.Count > 0, "the quest's own steps are not listed");
            Assert.True(taskSites.IsVisible && taskSites.Items.Count > 0, "the task's own steps are not listed");
            Assert.True(starts.IsVisible);
            Assert.Equal("Starts when the quest does.", starts.Text);

            // The conditions are drawn, and cannot be changed.
            var drawn = FindAll<FrameworkElement>(questSites).Where(e => e.DataContext is NodeConditionViewModel).ToList();
            Assert.NotEmpty(drawn);
            Assert.All(drawn, e => Assert.False(e.IsEnabled, "a condition of the game's can be edited"));

            // From the task's site to its conversation.
            var open = FindAll<Button>(taskSites).First(b => b.IsVisible && (string)b.Content == "Open conversation");
            var site = (GameQuestSiteViewModel)open.DataContext;
            _out.WriteLine("opening " + site.Conversation + " at " + site.Site.Node);
            Press(open);
            Settle(window);

            Assert.Equal(TabDialogues, tabs.SelectedIndex);
            var dialogue = Assert.Single(vm.VanillaDialogues);
            Assert.Same(dialogue, vm.SelectedDialogue);
            Assert.Equal(VanillaDialogueCatalog.TokenPrefix + site.Conversation, dialogue.Model.Source);
            Assert.NotNull(vm.SelectedNode);
            Assert.Equal(unchecked((int)site.Site.Node!.Value), vm.SelectedNode!.Id);

            // A second time finds the entry already there.
            tabs.SelectedIndex = TabQuests;
            Settle(window);
            Press(FindAll<Button>(taskSites).First(b => b.IsVisible && (string)b.Content == "Open conversation"));
            Settle(window);
            Assert.Equal(TabDialogues, tabs.SelectedIndex);
            Assert.Single(vm.VanillaDialogues);
            Assert.Single(vm.Pack.Dialogues);
        });
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    private static void Press(Button button)
        => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }
}
