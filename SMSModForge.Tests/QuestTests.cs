using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.Shared;
using SMSModForge.Validation;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.Tests;

/// <summary>
/// Pack quests on the editor's side: what a pack stores, what the rows naming a
/// quest offer, what Validate says about them, and what a rename carries along.
/// <para/>
/// The runtime half cannot run here - it is the game's journal - so what these
/// pin is the other half of every contract: the words the plugin switches on,
/// the shape it reads, and the game's rules as the validator states them.
/// </summary>
public sealed class QuestTests
{
    private readonly ITestOutputHelper _out;
    public QuestTests(ITestOutputHelper o) => _out = o;

    // ── Helpers ──────────────────────────────────────────────────────

    private static QuestDef Letters() => new()
    {
        Key = "letters",
        Title = "Lost Letters (Mira)",
        Description = "Mira wants her letters back.",
        Tasks =
        {
            new QuestTaskDef
            {
                Key = "search", Name = "Search the harbour.",
                Subtasks =
                {
                    new QuestTaskDef { Key = "ask", Name = "Ask the fisherman." },
                    new QuestTaskDef { Key = "look", Name = "Look under the pier." },
                },
            },
            new QuestTaskDef { Key = "collect", Name = "Collect the letters.", CountTo = 3 },
        },
    };

    private static NodeActionDef QuestAction(string op, string quest, string task = "", string value = "", bool vanilla = false)
    {
        var a = new NodeActionDef { Type = NodeActionTypes.Quest };
        a.Params[V.OperationParam] = op;
        if (quest.Length > 0) a.Params[V.QuestParam] = quest;
        if (task.Length > 0) a.Params[V.TaskParam] = task;
        if (value.Length > 0) a.Params[V.ValueParam] = value;
        if (vanilla) a.Params[V.SourceParam] = V.Vanilla;
        return a;
    }

    private static NodeConditionDef QuestState(string quest, string task = "", string state = V.InProgress, bool vanilla = false)
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.QuestState };
        c.Params[V.QuestParam] = quest;
        if (task.Length > 0) c.Params[V.TaskParam] = task;
        c.Params[V.StateParam] = state;
        if (vanilla) c.Params[V.SourceParam] = V.Vanilla;
        return c;
    }

    /// <summary>A pack with the quest and one rule carrying the given actions.</summary>
    private static ModPack PackWith(QuestDef quest, params NodeActionDef[] actions)
    {
        var pack = PackRepository.CreateEmpty("questpack");
        pack.Quests.Add(quest);
        var rule = new UpdateRuleDef { Key = "drive" };
        rule.Actions.AddRange(actions);
        pack.IntegrationRules.Add(rule);
        return pack;
    }

    private static List<ValidationIssue> QuestIssues(ModPack pack)
        => PackValidator.Validate(pack, System.IO.Path.GetTempPath())
                        .Where(i => i.Code.StartsWith("quest.")).ToList();

    private void Show(IEnumerable<ValidationIssue> issues)
    {
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} @ {i.Where}: {i.Message}");
    }

    // ── What a pack stores ───────────────────────────────────────────

    [Fact]
    public void AQuestIsSavedInTheShapeThePluginReads()
    {
        var pack = PackRepository.CreateEmpty("questpack");
        var quest = Letters();
        quest.Tasks[0].Completion = V.AnyOrder;
        pack.Quests.Add(quest);

        var json = JObject.Parse(PackRepository.Serialize(pack));
        _out.WriteLine(json["quests"]!.ToString());

        // The keys QuestRuntime.ReadSpecs reads, spelled the same.
        var q = (JObject)json["quests"]![0]!;
        Assert.Equal("letters", (string?)q["key"]);
        Assert.Equal("Lost Letters (Mira)", (string?)q["title"]);
        Assert.Equal("Mira wants her letters back.", (string?)q["description"]);

        var search = (JObject)q["tasks"]![0]!;
        Assert.Equal("search", (string?)search["key"]);
        Assert.Equal("any order", (string?)search["completion"]);
        Assert.Equal(2, ((JArray)search["subtasks"]!).Count);

        var collect = (JObject)q["tasks"]![1]!;
        Assert.Equal(3.0, (double)collect["countTo"]!);

        // Defaults stay out of the file: a task with no subtasks has no
        // completion to state, and one that does not count has no target.
        Assert.Null(collect["completion"]);
        Assert.Null(search["countTo"]);
        Assert.Null(((JObject)search["subtasks"]![0]!)["subtasks"]);
    }

    [Fact]
    public void APackWithoutQuestsGainsNothingInItsFile()
    {
        // The control for the test above: the section exists only when used, so
        // every pack that predates quests saves byte-for-byte as it did.
        var json = JObject.Parse(PackRepository.Serialize(PackRepository.CreateEmpty("plain")));
        Assert.Null(json["quests"]);
        Assert.Null(json["questFolders"]);
    }

    [Fact]
    public void AQuestSurvivesARoundTrip()
    {
        var pack = PackRepository.CreateEmpty("questpack");
        pack.Quests.Add(Letters());

        var back = PackRepository.Deserialize(PackRepository.Serialize(pack))!;
        var q = Assert.Single(back.Quests);
        Assert.Equal(new[] { "search", "ask", "look", "collect" }, q.AllTasks().Select(t => t.Key));
        Assert.Equal(3.0, q.Tasks[1].CountTo);
        Assert.Equal(V.InOrder, q.Tasks[0].Completion);
    }

    // ── The game's quests ────────────────────────────────────────────

    [Fact]
    public void TheGamesQuestsAreListedWithTheirTasks()
    {
        _out.WriteLine($"{VanillaQuests.All.Count} quests, {VanillaQuests.All.Sum(q => q.Tasks.Count)} tasks");
        Assert.True(VanillaQuests.All.Count >= 30);

        // Facts read off the running game, pinned: a pack stores these names
        // and ids, so a regeneration that changed them would break packs.
        var secrets = VanillaQuests.Find("Secrets (Adrian)")!;
        Assert.NotNull(secrets);
        var header = secrets.Tasks[0];
        Assert.True(header.IsTopLevel);
        Assert.Equal(TaskCompletion.Manual, header.Completion);

        var astrid = VanillaQuests.Find("Astrid Quest")!;
        var counted = astrid.Task(1745766144);
        Assert.NotNull(counted);
        Assert.Equal(TaskCounter.Value, counted!.Counter);

        // Asset name and title differ for a third of them, and the name is
        // what is stored.
        Assert.Equal("Into the Dark (Astrid)", astrid.PlainTitle);
    }

    [Fact]
    public void EveryTaskHangsFromARealParent()
    {
        foreach (var q in VanillaQuests.All)
            foreach (var t in q.Tasks)
                Assert.True(t.IsTopLevel || q.Task(t.Parent) != null,
                            $"{q.Name}: task {t.Id} names a parent that is not there");
    }

    [Fact]
    public void GameCreatorsSampleQuestsAreNotOffered()
    {
        // The control for the catalogue: the game ships Game Creator's demo
        // quests beside its own, and the generator leaves out anything nothing
        // in the game starts.
        Assert.Null(VanillaQuests.Find("Quest Simple"));
        Assert.Null(VanillaQuests.Find("Beast_Rat"));
    }

    // ── The picker on a quest row ────────────────────────────────────

    [Fact]
    public void ThePickerListsThePacksQuestsAndTheirTasks()
    {
        var quests = new List<QuestDef> { Letters() };
        QuestPickerViewModel.PackQuests = () => quests;
        try
        {
            var ps = new Dictionary<string, string>();
            var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: false);

            var option = Assert.Single(picker.QuestOptions);
            Assert.Equal("letters", option.Token);
            Assert.Equal("Lost Letters (Mira)", option.DisplayLabel);

            picker.QuestKey = "letters";
            Assert.Equal(new[] { "search", "ask", "look", "collect" }, picker.TaskOptions.Select(o => o.Token));

            picker.TaskKey = "collect";
            Assert.Contains("counts to 3", picker.TaskNote);

            picker.TaskKey = "search";
            Assert.Contains("completes itself", picker.TaskNote);
        }
        finally { QuestPickerViewModel.PackQuests = null; }
    }

    [Fact]
    public void TheTaskFollowsItsQuest()
    {
        var other = new QuestDef { Key = "other", Title = "Other", Tasks = { new QuestTaskDef { Key = "collect", Name = "x" } } };
        var quests = new List<QuestDef> { Letters(), other };
        QuestPickerViewModel.PackQuests = () => quests;
        try
        {
            var ps = new Dictionary<string, string>();
            var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: false)
            {
                QuestKey = "letters",
                TaskKey = "ask",
            };

            // A task the new quest also has is still a real choice and stays.
            picker.TaskKey = "collect";
            picker.QuestKey = "other";
            Assert.Equal("collect", picker.TaskKey);

            // One it does not have goes, rather than sitting there looking chosen.
            picker.QuestKey = "letters";
            picker.TaskKey = "ask";
            picker.QuestKey = "other";
            Assert.Equal("", picker.TaskKey);
            Assert.False(ps.ContainsKey(V.TaskParam));

            // The side changes what a name means, so both go.
            picker.Source = "Vanilla";
            Assert.Equal("", picker.QuestKey);
            Assert.Equal(V.Vanilla, ps[V.SourceParam]);
        }
        finally { QuestPickerViewModel.PackQuests = null; }
    }

    [Fact]
    public void AGameTaskIsStoredByIdAndShownByName()
    {
        var ps = new Dictionary<string, string>();
        var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: true)
        {
            Source = "Vanilla",
            QuestKey = "Astrid Quest",
        };

        var options = picker.TaskOptions;
        Assert.Equal(QuestPickerViewModel.WholeQuestToken, options[0].Token);   // "the quest itself"
        var counted = options.Single(o => o.Token == "1745766144");
        _out.WriteLine(counted.Label);
        Assert.Contains("Astrid", counted.Name);
    }

    [Fact]
    public void TwoTasksWithOneNameAreToldApartByTheirIds()
    {
        // A step and the step under it, both called the same thing. The closed
        // box shows the NAME, so without this the row reads perfectly well and
        // points at whichever of the two happened to be picked - a mistake that
        // leaves nothing behind to find it by.
        var twins = new QuestDef
        {
            Key = "twins",
            Title = "Twins",
            Tasks =
            {
                new QuestTaskDef
                {
                    Key = "visit", Name = "Talk to her.",
                    Subtasks = { new QuestTaskDef { Key = "again", Name = "Talk to her." } },
                },
                new QuestTaskDef { Key = "home", Name = "Go home." },
            },
        };

        var quests = new List<QuestDef> { twins };
        QuestPickerViewModel.PackQuests = () => quests;
        try
        {
            var picker = new QuestPickerViewModel(new Dictionary<string, string>(), () => { },
                                                  offersWholeQuest: false) { QuestKey = "twins" };
            var options = picker.TaskOptions;
            foreach (var o in options) _out.WriteLine($"'{o.Name}'  |  '{o.Label}'");

            Assert.Equal("Talk to her.  (visit)", options.Single(o => o.Token == "visit").Name);
            Assert.Equal("Talk to her.  (again)", options.Single(o => o.Token == "again").Name);

            // Which is also what the list row says, not only the closed box:
            // the indent tells you which is the subtask, but not which of them
            // you just chose.
            Assert.Contains("(again)", options.Single(o => o.Token == "again").Label);

            // The control. A name nothing else shares is left alone - an id
            // beside every task would be noise on every row in the editor.
            Assert.Equal("Go home.", options.Single(o => o.Token == "home").Name);
        }
        finally { QuestPickerViewModel.PackQuests = null; }
    }

    [Fact]
    public void AStoredTaskTheListDoesNotHaveStillShows()
    {
        // Otherwise opening a row whose task was deleted would show an empty box
        // and read as the row having lost its value.
        var quests = new List<QuestDef> { Letters() };
        QuestPickerViewModel.PackQuests = () => quests;
        try
        {
            var ps = new Dictionary<string, string> { [V.QuestParam] = "letters", [V.TaskParam] = "gone" };
            var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: false);
            Assert.Contains(picker.TaskOptions, o => o.Token == "gone" && o.Label.Contains("not found"));
        }
        finally { QuestPickerViewModel.PackQuests = null; }
    }

    [Fact]
    public void TheActionRowSaysWhatItShows()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        var row = new NodeActionViewModel(def) { DisplayType = NodeActionTypes.Quest };

        Assert.True(row.IsQuestFamily);
        Assert.Equal(V.Start, def.Params[V.OperationParam]);   // on screen = in the file
        Assert.False(row.QuestPicker.ShowsTask);

        row.QuestOperation = V.AddToCounter;
        Assert.True(row.QuestPicker.ShowsTask);
        Assert.True(row.QuestTakesValue);
        row.QuestPicker.QuestKey = "letters";
        def.Params[V.TaskParam] = "collect";
        row.QuestValue = "2";

        // Back to an operation on the whole quest: the task and number would
        // otherwise be a second meaning nobody can see.
        row.QuestOperation = V.Start;
        Assert.False(def.Params.ContainsKey(V.TaskParam));
        Assert.False(def.Params.ContainsKey(V.ValueParam));
        Assert.Equal("letters", def.Params[V.QuestParam]);
    }

    // ── Validate ─────────────────────────────────────────────────────

    [Fact]
    public void AQuestNothingStartsIsReportedAndNothingElseAboutIt()
    {
        var issues = QuestIssues(PackWith(Letters()));
        Show(issues);

        Assert.Contains(issues, i => i.Code == "quest.neverStarted" && i.Where == "quests[letters]");
        // One problem, said once: its unfinishable tasks follow from it.
        Assert.DoesNotContain(issues, i => i.Code == "quest.taskNeverCompleted");
    }

    [Fact]
    public void AStartedQuestSaysWhereItStops()
    {
        var pack = PackWith(Letters(),
            QuestAction(V.Start, "letters"),
            QuestAction(V.CompleteTask, "letters", "ask"));
        var issues = QuestIssues(pack);
        Show(issues);

        Assert.DoesNotContain(issues, i => i.Code == "quest.neverStarted");
        var stops = issues.Where(i => i.Code == "quest.taskNeverCompleted").Select(i => i.Where).ToList();
        Assert.Contains("quests[letters].tasks[look]", stops);
        Assert.Contains("quests[letters].tasks[collect]", stops);
        Assert.DoesNotContain("quests[letters].tasks[ask]", stops);
        // The header finishes itself, so it is its subtask that is named.
        Assert.DoesNotContain("quests[letters].tasks[search]", stops);

        // And the quest as a whole says it cannot be finished, naming the first
        // top-level task it stops at - the one every player gets stuck on.
        var whole = Assert.Single(issues, i => i.Code == "quest.cannotComplete");
        Assert.Equal("quests[letters]", whole.Where);
        Assert.Contains("Search the harbour.", whole.Message);
    }

    [Fact]
    public void AQuestEveryTaskOfWhichCanFinishIsClean()
    {
        // The control: doing everything the warnings above ask clears them.
        var pack = PackWith(Letters(),
            QuestAction(V.Start, "letters"),
            QuestAction(V.CompleteTask, "letters", "ask"),
            QuestAction(V.CompleteTask, "letters", "look"),
            QuestAction(V.AddToCounter, "letters", "collect"));
        var issues = QuestIssues(pack);
        Show(issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void AnyOneNeedsOnlyOne()
    {
        var quest = Letters();
        quest.Tasks[0].Completion = V.AnyOne;
        var pack = PackWith(quest,
            QuestAction(V.Start, "letters"),
            QuestAction(V.CompleteTask, "letters", "look"),
            QuestAction(V.SetCounter, "letters", "collect", "3"));
        Assert.DoesNotContain(QuestIssues(pack), i => i.Code == "quest.taskNeverCompleted");
    }

    [Fact]
    public void SubtasksUnderATaskCompletedByActionAreNotes()
    {
        // The game never starts them, so nothing is expected to finish them -
        // but the header itself now needs an action.
        var quest = Letters();
        quest.Tasks[0].Completion = V.ByAction;
        var pack = PackWith(quest,
            QuestAction(V.Start, "letters"),
            QuestAction(V.AddToCounter, "letters", "collect"));
        var stops = QuestIssues(pack).Where(i => i.Code == "quest.taskNeverCompleted").Select(i => i.Where).ToList();

        Assert.Equal(new[] { "quests[letters].tasks[search]" }, stops);
    }

    [Fact]
    public void ATopLevelTaskCanBeCompletedOutright()
    {
        // Read off the game's TaskUtils: under a top-level or in-order parent
        // the completion check walks the task's siblings and answers yes on
        // reaching the task itself, before it looks at the subtasks. The game's
        // own quests complete tasks with unfinished subtasks 29 times, and they
        // work in play. So this is neither refused nor a quest that stops.
        var pack = PackWith(Letters(),
            QuestAction(V.Start, "letters"),
            QuestAction(V.CompleteTask, "letters", "search"),
            QuestAction(V.AddToCounter, "letters", "collect"));
        var issues = QuestIssues(pack);
        Show(issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void UnderAnAnyOrderTaskTheSubtasksAreChecked()
    {
        // The control for the test above: there the game does look at a task's
        // subtasks, so completing the task alone is not enough.
        var quest = new QuestDef
        {
            Key = "nested", Title = "Nested",
            Tasks =
            {
                new QuestTaskDef
                {
                    Key = "outer", Name = "Outer", Completion = V.AnyOrder,
                    Subtasks =
                    {
                        new QuestTaskDef
                        {
                            Key = "inner", Name = "Inner",
                            Subtasks = { new QuestTaskDef { Key = "leaf", Name = "Leaf" } },
                        },
                    },
                },
            },
        };
        var pack = PackWith(quest,
            QuestAction(V.Start, "nested"),
            QuestAction(V.CompleteTask, "nested", "inner"));
        var stops = QuestIssues(pack).Where(i => i.Code == "quest.taskNeverCompleted").Select(i => i.Where).ToList();
        Assert.Equal(new[] { "quests[nested].tasks[leaf]" }, stops);
    }

    [Fact]
    public void TheGamesRefusalsAreSaidBeforeTheyHappen()
    {
        var pack = PackWith(Letters(),
            QuestAction(V.Start, "letters"),
            QuestAction(V.AddToCounter, "letters", "ask"),            // does not count
            QuestAction(V.CompleteTask, "letters", "missing"),        // no such task
            QuestAction(V.Start, "nosuchquest"));                     // no such quest
        var issues = QuestIssues(pack);
        Show(issues);

        Assert.Contains(issues, i => i.Code == "quest.notACounter");
        Assert.Contains(issues, i => i.Code == "quest.unknownTask" && i.Severity == Severity.Error);
        Assert.Contains(issues, i => i.Code == "quest.unknownQuest" && i.Severity == Severity.Error);
    }

    [Fact]
    public void AGameQuestIsCheckedAgainstTheCatalogue()
    {
        var pack = PackWith(new QuestDef { Key = "unused", Title = "Unused" },
            QuestAction(V.CompleteTask, "Astrid Quest", "1745766144", vanilla: true),
            QuestAction(V.Start, "Not A Real Quest", vanilla: true));
        var issues = QuestIssues(pack);
        Show(issues);

        // A real one passes; an unknown one is only a warning, because the
        // runtime looks game quests up in the running game.
        Assert.DoesNotContain(issues, i => i.Where.EndsWith("actions[0]"));
        Assert.Contains(issues, i => i.Code == "quest.unknownVanillaQuest" && i.Severity == Severity.Warning);
    }

    [Fact]
    public void TwoTasksWithOneNameAreAnError()
    {
        // The runtime skips such a quest entirely rather than let two tasks
        // share one save slot, so the editor has to say it first.
        var quest = Letters();
        quest.Tasks[1].Key = "ask";
        var issues = QuestIssues(PackWith(quest, QuestAction(V.Start, "letters")));
        Assert.Contains(issues, i => i.Code == "quest.duplicateTaskKey" && i.Severity == Severity.Error);
    }

    [Fact]
    public void AQuestStartedFromAButtonIsStarted()
    {
        // The shared walk does not reach UI buttons. A check built on it would
        // call this quest never started while a button starts it.
        var pack = PackRepository.CreateEmpty("questpack");
        pack.Quests.Add(Letters());
        var node = new UiNodeDef { Name = "Accept" };
        node.OnClick.Add(QuestAction(V.Start, "letters"));
        pack.Uis.Add(new UiDef { Id = "board", Name = "Board", Nodes = { node } });

        Assert.DoesNotContain(QuestIssues(pack), i => i.Code == "quest.neverStarted");
    }

    // ── Renames ──────────────────────────────────────────────────────

    [Fact]
    public void RenamingAQuestCarriesItsRowsAndLeavesTheGamesAlone()
    {
        var own = QuestAction(V.Start, "letters");
        var branch = new NodeActionDef { Type = NodeActionTypes.DiceRoll };
        branch.Branches.Add(new DiceBranchDef { Chance = 100, Action = QuestAction(V.CompleteTask, "letters", "ask") });
        var game = QuestAction(V.Start, "letters", vanilla: true);   // same word, the game's side
        var pack = PackWith(Letters(), own, branch, game);
        var rule = pack.IntegrationRules[0];
        rule.Conditions.Add(QuestState("letters"));

        int n = ReferenceRenamer.Rename(pack, RefKind.Quest, "letters", "mail");

        Assert.Equal(3, n);
        Assert.Equal("mail", own.Params[V.QuestParam]);
        Assert.Equal("mail", branch.Branches[0].Action.Params[V.QuestParam]);
        Assert.Equal("mail", rule.Conditions[0].Params[V.QuestParam]);
        Assert.Equal("letters", game.Params[V.QuestParam]);
    }

    [Fact]
    public void RenamingATaskCarriesOnlyThatQuestsRows()
    {
        var mine = QuestAction(V.CompleteTask, "letters", "ask");
        var elsewhere = QuestAction(V.CompleteTask, "other", "ask");   // same task key, other quest
        var pack = PackWith(Letters(), mine, elsewhere);

        int n = ReferenceRenamer.RenameQuestTask(pack, "letters", "ask", "askAround");

        Assert.Equal(1, n);
        Assert.Equal("askAround", mine.Params[V.TaskParam]);
        Assert.Equal("ask", elsewhere.Params[V.TaskParam]);
    }

    [Fact]
    public void ANewQuestsRowsFollowItsTitle()
    {
        // A new quest's key follows its title. A row written against it before
        // the title is finished must not be left pointing at the old key: the
        // quest has never shipped under it, so nothing is lost by following.
        var vm = new MainViewModel();
        vm.AddQuestCommand.Execute(null);
        var quest = vm.SelectedQuest!;
        quest.Title = "Lost";
        string early = quest.Key;

        var rule = new UpdateRuleDef { Key = "r" };
        rule.Actions.Add(QuestAction(V.Start, early));
        vm.Pack.IntegrationRules.Add(rule);

        quest.Title = "Lost Letters";
        _out.WriteLine($"{early} -> {quest.Key}");
        Assert.NotEqual(early, quest.Key);
        Assert.Equal(quest.Key, rule.Actions[0].Params[V.QuestParam]);

        // And a task's rows follow its name the same way.
        quest.AddTaskCommand.Execute(null);
        var task = quest.SelectedTask!;
        task.Name = "Ask";
        rule.Actions.Add(QuestAction(V.CompleteTask, quest.Key, task.Key));
        task.Name = "Ask around";
        Assert.Equal(task.Key, rule.Actions[1].Params[V.TaskParam]);
    }

    [Fact]
    public void AQuestLoadedFromDiskKeepsItsKey()
    {
        // The control: a quest that has been saved may be in players' saves
        // under its key, so editing its title must not move it.
        var loaded = new QuestViewModel(Letters());

        loaded.Title = "Something else entirely";
        Assert.Equal("letters", loaded.Key);
    }

    // ── Quests that move on their own ────────────────────────────────

    private static NodeConditionDef VarIs(string name, string value)
    {
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableCompare };
        c.Params["name"] = name;
        c.Params["comparison"] = "equals";
        c.Params["value"] = value;
        return c;
    }

    [Fact]
    public void StartAndCompletionListsAreSavedWhereThePluginReadsThem()
    {
        var pack = PackRepository.CreateEmpty("questpack");
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        var ask = quest.Tasks[0].Subtasks[0];
        ask.Conditions.Add(VarIs("askedFisherman", "true"));
        ask.Actions.Add(new NodeActionDef { Type = NodeActionTypes.SetVariable, Params = { ["name"] = "clue", ["value"] = "pier" } });
        pack.Quests.Add(quest);

        var q = (JObject)JObject.Parse(PackRepository.Serialize(pack))["quests"]![0]!;
        _out.WriteLine(q.ToString());

        // The keys QuestTicker.Build reads.
        Assert.Single((JArray)q["startConditions"]!);
        var askJson = (JObject)q["tasks"]![0]!["subtasks"]![0]!;
        Assert.Single((JArray)askJson["conditions"]!);
        Assert.Single((JArray)askJson["actions"]!);

        // And nothing for the tasks that have none, so an ordinary task stays
        // as short in the file as it was.
        Assert.Null(q["tasks"]![1]!["conditions"]);
        Assert.Null(q["tasks"]![1]!["actions"]);
    }

    [Fact]
    public void AQuestWithStartConditionsIsStarted()
    {
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        var issues = QuestIssues(PackWith(quest));
        Assert.DoesNotContain(issues, i => i.Code == "quest.neverStarted");
    }

    [Fact]
    public void ATasksOwnConditionsFinishIt()
    {
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        foreach (var t in new[] { quest.Tasks[0].Subtasks[0], quest.Tasks[0].Subtasks[1], quest.Tasks[1] })
            t.Conditions.Add(VarIs("done_" + t.Key, "true"));

        var issues = QuestIssues(PackWith(quest));
        Show(issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void ConditionsOnATaskWithSubtasksAreSaidToBeUnused()
    {
        // The runtime only checks tasks with no subtasks, so these would do
        // nothing without a word - and the quest would still stop.
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        quest.Tasks[0].Conditions.Add(VarIs("searched", "true"));

        var issues = QuestIssues(PackWith(quest));
        Show(issues);
        Assert.Contains(issues, i => i.Code == "quest.headerHasCompletion" && i.Where == "quests[letters].tasks[search]");
        Assert.Contains(issues, i => i.Code == "quest.taskNeverCompleted" && i.Where == "quests[letters].tasks[ask]");
    }

    [Fact]
    public void AQuestListIsCheckedLikeEveryOtherList()
    {
        // A list check with no list named is the same mistake in a quest as in
        // a rule, and Validate has to find it there too.
        var quest = Letters();
        quest.Tasks[1].Conditions.Add(new NodeConditionDef { Type = NodeConditionTypes.ListContains });
        var pack = PackWith(quest);

        var issues = PackValidator.Validate(pack, System.IO.Path.GetTempPath());
        Show(issues.Where(i => i.Where.StartsWith("quests")));
        Assert.Contains(issues, i => i.Code == "condition.paramListMissing"
                                     && i.Where.StartsWith("quests[letters].tasks[collect].conditions[0]"));
    }

    [Fact]
    public void ATasksActionsCanStartTheNextQuest()
    {
        // Chaining quests is what completion actions are for, so a quest
        // started that way is started.
        var first = Letters();
        first.StartConditions.Add(VarIs("metMira", "true"));
        first.Tasks[1].Actions.Add(QuestAction(V.Start, "sequel"));
        var sequel = new QuestDef { Key = "sequel", Title = "Sequel", Tasks = { new QuestTaskDef { Key = "a", Name = "A" } } };

        var pack = PackWith(first);
        pack.Quests.Add(sequel);
        Assert.DoesNotContain(QuestIssues(pack), i => i.Code == "quest.neverStarted" && i.Where == "quests[sequel]");
    }

    [Fact]
    public void RenamingAVariableReachesTheQuestLists()
    {
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        quest.Tasks[1].Actions.Add(new NodeActionDef { Type = NodeActionTypes.SetVariable, Params = { ["name"] = "metMira", ["value"] = "false" } });
        var pack = PackWith(quest);
        pack.Variables.Add(new PackVariableDef { Name = "metMira", Type = PackVariableType.Bool });

        int n = VariableRenamer.RenameReferences(pack, "metMira", "metMiraAtHarbour");

        Assert.Equal(2, n);
        Assert.Equal("metMiraAtHarbour", quest.StartConditions[0].Params["name"]);
        Assert.Equal("metMiraAtHarbour", quest.Tasks[1].Actions[0].Params["name"]);
    }

    [Fact]
    public void OnlyATaskWithoutSubtasksOffersItsOwnLists()
    {
        var vm = new QuestViewModel(Letters());
        var search = vm.TaskRows.Single(t => t.Key == "search");
        var ask = vm.TaskRows.Single(t => t.Key == "ask");
        Assert.False(search.HasOwnCompletion);
        Assert.True(ask.HasOwnCompletion);

        // Losing its last subtask gives a task its own lists back.
        vm.SelectedTask = ask;
        vm.RemoveTaskCommand.Execute(null);
        vm.SelectedTask = vm.TaskRows.Single(t => t.Key == "look");
        vm.RemoveTaskCommand.Execute(null);
        Assert.True(vm.TaskRows.Single(t => t.Key == "search").HasOwnCompletion);
    }

    [Fact]
    public void QuestListsAreCheckedContinuously()
    {
        // The runtime asks every frame, so the list offers what a polled host
        // offers: no Timer, which only a rule can restart.
        var task = new QuestTaskViewModel(new QuestTaskDef { Key = "t" }, new QuestViewModel(new QuestDef()));
        var row = task.CompletionConditions.Add();
        Assert.DoesNotContain(NodeConditionTypes.Timer, row.AvailableTypes);
    }

    // ── A counter that follows a variable ────────────────────────────

    private static ModPack CountingPack(string variable, PackVariableType type, bool vanilla = false)
    {
        var quest = Letters();
        quest.StartConditions.Add(VarIs("metMira", "true"));
        quest.Tasks[0].Subtasks[0].Conditions.Add(VarIs("asked", "true"));
        quest.Tasks[0].Subtasks[1].Conditions.Add(VarIs("looked", "true"));
        var collect = quest.Tasks[1];
        collect.CountFrom = V.CountFromVariable;
        collect.CountVariable = variable;
        if (vanilla) collect.CountSource = V.Vanilla;

        var pack = PackWith(quest);
        if (!vanilla) pack.Variables.Add(new PackVariableDef { Name = variable, Type = type });
        return pack;
    }

    [Fact]
    public void ACounterFollowingAVariableIsSavedWhereThePluginReadsIt()
    {
        var pack = CountingPack("lettersFound", PackVariableType.Int, vanilla: false);
        var collect = (JObject)JObject.Parse(PackRepository.Serialize(pack))["quests"]![0]!["tasks"]![1]!;
        _out.WriteLine(collect.ToString());

        // The keys QuestTicker reads.
        Assert.Equal("variable", (string?)collect["countFrom"]);
        Assert.Equal("lettersFound", (string?)collect["countVariable"]);
        Assert.Null(collect["countSource"]);   // the pack's own: nothing to say

        // The control: counting by action writes none of it...
        var plain = JObject.Parse(PackRepository.Serialize(PackWith(Letters())))["quests"]![0]!["tasks"]![1]!;
        Assert.Null(plain["countFrom"]);
        Assert.Null(plain["countVariable"]);

        // ...and neither does a task that no longer counts at all.
        pack.Quests[0].Tasks[1].CountTo = null;
        var stopped = JObject.Parse(PackRepository.Serialize(pack))["quests"]![0]!["tasks"]![1]!;
        Assert.Null(stopped["countFrom"]);
    }

    [Fact]
    public void ACounterOnANumberVariableIsClean()
    {
        var issues = QuestIssues(CountingPack("lettersFound", PackVariableType.Int));
        Show(issues);
        // Nothing completes 'collect' by action, but the variable finishes it.
        Assert.Empty(issues);
    }

    [Fact]
    public void ACounterWithNothingToFollowIsReported()
    {
        Assert.Contains(QuestIssues(CountingPack("", PackVariableType.Int)),
                        i => i.Code == "quest.counterNoVariable" && i.Severity == Severity.Error);

        var undeclared = CountingPack("lettersFound", PackVariableType.Int);
        undeclared.Variables.Clear();
        Assert.Contains(QuestIssues(undeclared), i => i.Code == "quest.counterUnknownVariable");

        Assert.Contains(QuestIssues(CountingPack("lettersFound", PackVariableType.Bool)),
                        i => i.Code == "quest.counterNotANumber");
    }

    [Fact]
    public void ACounterOnTheGamesVariablesIsCheckedAgainstTheCatalogue()
    {
        // "health" is a number in the game's catalogue; "towercombatactive" a yes/no.
        Assert.DoesNotContain(QuestIssues(CountingPack("health", PackVariableType.Int, vanilla: true)),
                              i => i.Code.StartsWith("quest.counter"));
        Assert.Contains(QuestIssues(CountingPack("towercombatactive", PackVariableType.Int, vanilla: true)),
                        i => i.Code == "quest.counterNotANumber");
    }

    [Fact]
    public void AnActionSettingAVariableCounterIsSaidToBeOverwritten()
    {
        var pack = CountingPack("lettersFound", PackVariableType.Int);
        pack.IntegrationRules[0].Actions.Add(QuestAction(V.AddToCounter, "letters", "collect"));
        Assert.Contains(QuestIssues(pack), i => i.Code == "quest.counterFollowsVariable");
    }

    [Fact]
    public void RenamingAVariableCarriesTheCounterFollowingIt()
    {
        var pack = CountingPack("lettersFound", PackVariableType.Int);
        int n = VariableRenamer.RenameReferences(pack, "lettersFound", "lettersRecovered");
        Assert.Equal(1, n);
        Assert.Equal("lettersRecovered", pack.Quests[0].Tasks[1].CountVariable);

        // The control: the game's variable of the same name is another variable.
        var game = CountingPack("health", PackVariableType.Int, vanilla: true);
        Assert.Equal(0, VariableRenamer.RenameReferences(game, "health", "vigour"));
        Assert.Equal("health", game.Quests[0].Tasks[1].CountVariable);
    }

    // ── The words both sides use ─────────────────────────────────────

    [Fact]
    public void ACounterComparesTheWayAVariableDoes()
    {
        Assert.True(V.Compare(3, V.GreaterOrEqual, 3));
        Assert.True(V.Compare(0.1 + 0.2, V.EqualTo, 0.3));   // counts add up in doubles
        Assert.False(V.Compare(2, V.GreaterThan, 2));
        Assert.False(V.Compare(5, "roughly", 5));             // unknown is not met
    }

    [Fact]
    public void AHandWrittenManifestIsReadLeniently()
    {
        Assert.True(V.Is(" Complete Task ", V.CompleteTask));
        Assert.True(V.TakesTask("Set Counter"));
        Assert.False(V.TakesTask(V.Track));
    }
}
