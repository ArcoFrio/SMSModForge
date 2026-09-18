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
using S = SMSModForge.Shared.QuestTreeEdits;

namespace SMSModForge.Tests;

/// <summary>
/// A pack changing the SHAPE of one of the game's quests: tasks of its own
/// among the game's, the game's taken out or hidden, and what the game tells a
/// player loading a save those changes might not suit.
/// <para/>
/// The placement rule, the repair and the warning's decision are shared with
/// the plugin (<see cref="QuestTreeEdits"/>), so they are tested here as the
/// plugin runs them. What the plugin does with the game's objects is read off
/// the game's IL and cannot run here.
/// </summary>
public sealed class VanillaQuestTreeTests
{
    private readonly ITestOutputHelper _out;
    public VanillaQuestTreeTests(ITestOutputHelper o) => _out = o;

    private const int TabQuests = 13;

    private static string Token(int id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>A game quest with a top-level task that has at least two
    /// subtasks - somewhere to put a subtask between the game's.</summary>
    private static VanillaQuests.VanillaQuest WithSubtasks
        => VanillaQuests.All.First(q => q.Tasks.Any(t => t.IsTopLevel && q.Tasks.Count(c => c.Parent == t.Id) >= 2));

    /// <summary>A game quest with a top-level task that has no subtasks.</summary>
    private static VanillaQuests.VanillaQuest WithLeafStep
        => VanillaQuests.All.First(q => q.Tasks.Any(t => t.IsTopLevel && q.Tasks.All(c => c.Parent != t.Id)));

    private static (ModPack Pack, QuestViewModel Quest) Extension(VanillaQuests.VanillaQuest game)
    {
        var pack = new ModPack();
        var def = new QuestDef { Key = "ext", Title = "", Source = game.Name };
        pack.Quests.Add(def);
        return (pack, new QuestViewModel(def));
    }

    private void Show(QuestViewModel quest) => _out.WriteLine(quest.DescribeExtensionRows());

    // ── Where an added task goes (shared with the plugin) ─────────────

    [Fact]
    public void AnAddedTaskSitsJustBeforeTheGameTaskItNames()
    {
        var game = new[] { "1", "2", "3" };
        var added = new[] { ("a", "2"), ("b", ""), ("c", "1"), ("d", "2"), ("e", "99") };
        var order = S.Arrange(game, g => g, added, a => a.Item2)
            .Select(p => p.IsAdded ? p.Added.Item1 : p.Game).ToList();

        _out.WriteLine(string.Join(" ", order));
        // Before 1: c. Before 2: a then d, in the pack's order. After all: b,
        // and e, whose game task is not there any more - kept, at the end.
        Assert.Equal(new[] { "c", "1", "a", "d", "2", "3", "b", "e" }, order);
    }

    [Fact]
    public void WritingBackAnOrderGivesTheSameOrderWhenReadAgain()
    {
        // The editor stores "before" from where a row sits; reading that back
        // has to put every row where it was, for any arrangement.
        var rng = new Random(7);
        for (int round = 0; round < 200; round++)
        {
            var items = new List<(bool Added, string Name)>();
            int games = rng.Next(0, 5), adds = rng.Next(0, 5);
            for (int i = 0; i < games; i++) items.Add((false, "g" + i));
            for (int i = 0; i < adds; i++) items.Insert(rng.Next(items.Count + 1), (true, "a" + i));

            var placed = items.Select(x => new S.Placed<string, string>
            {
                Game = x.Added ? null! : x.Name,
                Added = x.Added ? x.Name : null!,
                IsAdded = x.Added,
            }).ToList();
            var before = new Dictionary<string, string>();
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].IsAdded) before[placed[i].Added] = S.BeforeFor(placed, i, g => g);

            var game = items.Where(x => !x.Added).Select(x => x.Name).ToList();
            var added = items.Where(x => x.Added).Select(x => x.Name).ToList();
            var again = S.Arrange(game, g => g, added, a => before[a])
                .Select(p => p.IsAdded ? p.Added : p.Game).ToList();

            Assert.Equal(items.Select(x => x.Name).ToList(), again);
        }
    }

    [Fact]
    public void AnAddedTasksIdIsItsOwnAndStable()
    {
        int one = QuestIds.AddedTaskId("pack", "Secrets (Adrian)", "Ask");
        Assert.Equal(one, QuestIds.AddedTaskId("pack", "Secrets (Adrian)", "Ask"));
        Assert.NotEqual(one, QuestIds.AddedTaskId("pack", "Secrets (Adrian)", "Ask2"));
        Assert.NotEqual(one, QuestIds.AddedTaskId("other", "Secrets (Adrian)", "Ask"));
        // Not the id a quest of the pack's own with that name would give it.
        Assert.NotEqual(one, QuestIds.TaskId("pack", "Secrets (Adrian)", "Ask"));
        Assert.NotEqual(0, one);
        Assert.NotEqual(QuestIds.NoNode, one);
    }

    // ── Putting a save back on its feet (shared with the plugin) ──────

    private static S.Node N(int id, S.Completion c = S.Completion.InOrder, params S.Node[] children)
    {
        var n = new S.Node { Id = id, Completion = c };
        n.Children.AddRange(children);
        return n;
    }

    private static Func<int, S.TaskState> States(params (int Id, S.TaskState State)[] states)
    {
        var map = states.ToDictionary(s => s.Id, s => s.State);
        return id => map.TryGetValue(id, out var s) ? s : S.TaskState.Inactive;
    }

    private const S.TaskState Active = S.TaskState.Active;
    private const S.TaskState Done = S.TaskState.Completed;

    [Fact]
    public void AHealthyQuestIsLeftAlone()
    {
        // The control: a quest the game is running normally has something in
        // progress, and nothing to repair.
        var roots = new[] { N(1, S.Completion.InOrder, N(10), N(11)), N(2) };
        var step = S.NextRepair(roots, true, States((1, Active), (10, Done), (11, Active)));
        Assert.Equal(S.RepairKind.None, step.Kind);

        // ...and a quest not in progress is not touched at all.
        step = S.NextRepair(roots, false, States((1, Active), (10, Done), (11, Done)));
        Assert.Equal(S.RepairKind.None, step.Kind);
    }

    [Fact]
    public void AStepWhoseSubtasksAreAllDoneIsCompleted()
    {
        // The pack's subtask 12 was the one in progress; it is gone, and the
        // step is left waiting on subtasks that are all done.
        var roots = new[] { N(1, S.Completion.InOrder, N(10), N(11)), N(2) };
        var step = S.NextRepair(roots, true, States((1, Active), (10, Done), (11, Done)));
        Assert.Equal(S.RepairKind.CompleteTask, step.Kind);
        Assert.Equal(1, step.TaskId);

        // The same under "any order", and under "any one" with one done.
        roots = new[] { N(1, S.Completion.AnyOrder, N(10), N(11)) };
        Assert.Equal(1, S.NextRepair(roots, true, States((1, Active), (10, Done), (11, Done))).TaskId);
        roots = new[] { N(1, S.Completion.AnyOne, N(10), N(11)) };
        Assert.Equal(S.RepairKind.CompleteTask,
                     S.NextRepair(roots, true, States((1, Active), (10, Done), (11, Active))).Kind);
    }

    [Fact]
    public void AStepFinishedByAnActionIsNeverCompletedByTheRepair()
    {
        var roots = new[] { N(1, S.Completion.ByAction, N(10)) };
        var step = S.NextRepair(roots, true, States((1, Active), (10, Done)));
        Assert.Equal(S.RepairKind.None, step.Kind);
    }

    [Fact]
    public void AnInOrderListWithNothingInProgressStartsItsNextTask()
    {
        // The task that was in progress is gone: the one after it never
        // started, because only finishing the one before starts it.
        var roots = new[] { N(1, S.Completion.InOrder, N(10), N(11), N(12)) };
        var step = S.NextRepair(roots, true, States((1, Active), (10, Done)));
        Assert.Equal(S.RepairKind.ActivateTask, step.Kind);
        Assert.Equal(11, step.TaskId);

        // The same at the top level.
        roots = new[] { N(1), N(2), N(3) };
        step = S.NextRepair(roots, true, States((1, Done)));
        Assert.Equal(S.RepairKind.ActivateTask, step.Kind);
        Assert.Equal(2, step.TaskId);

        // A task not started, before one in progress - a task the pack adds,
        // placed before the one the save is on - is not started beside it.
        roots = new[] { N(1, S.Completion.InOrder, N(10), N(11), N(12)) };
        step = S.NextRepair(roots, true, States((1, Active), (10, Done), (12, Active)));
        Assert.Equal(S.RepairKind.None, step.Kind);
        roots = new[] { N(1), N(2), N(3) };
        Assert.Equal(S.RepairKind.None, S.NextRepair(roots, true, States((1, Done), (3, Active))).Kind);

        // A failed task in the way is the game's business: nothing is started past it.
        roots = new[] { N(1, S.Completion.InOrder, N(10), N(11)) };
        step = S.NextRepair(roots, true, States((1, Active), (10, S.TaskState.Failed)));
        Assert.Equal(S.RepairKind.None, step.Kind);
    }

    [Fact]
    public void AQuestWhoseStepsAreAllDoneIsCompletedAndTheDeepestGoesFirst()
    {
        var roots = new[] { N(1), N(2) };
        Assert.Equal(S.RepairKind.EvaluateQuest, S.NextRepair(roots, true, States((1, Done), (2, Done))).Kind);

        // Deepest first: the subtask list is fixed before the step above it.
        roots = new[] { N(1, S.Completion.InOrder, N(10, S.Completion.InOrder, N(100), N(101))) };
        var step = S.NextRepair(roots, true, States((1, Active), (10, Active), (100, Done), (101, Done)));
        Assert.Equal(S.RepairKind.CompleteTask, step.Kind);
        Assert.Equal(10, step.TaskId);
    }

    // ── What it stores ────────────────────────────────────────────────

    [Fact]
    public void AddedTasksAndTheGamesTasksChangesSurviveASave()
    {
        var pack = new ModPack();
        var game = WithSubtasks;
        var def = new QuestDef { Key = "ext", Title = "", Source = game.Name };
        var parent = game.Tasks.First(t => t.IsTopLevel && game.Tasks.Count(c => c.Parent == t.Id) >= 2);
        var sub = new AddedTaskDef { Key = "Look", Name = "Look closer", Under = Token(parent.Id) };
        var top = new AddedTaskDef { Key = "Ask", Name = "Ask around", Before = Token(game.Tasks[0].Id) };
        top.Subtasks.Add(new QuestTaskDef { Key = "AskMarco", Name = "Ask Marco", HideUntilStarted = true });
        def.AddedTasks.Add(sub);
        def.AddedTasks.Add(top);
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(parent.Id), Removed = true });
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(game.Tasks[0].Id), Visibility = S.Hidden });
        pack.Quests.Add(def);

        string json = PackRepository.SerializeAsSaved(pack);
        _out.WriteLine(json[json.IndexOf("\"quests\"", StringComparison.Ordinal)..]);
        Assert.Contains("\"addedTasks\"", json);
        Assert.Contains($"\"under\": \"{parent.Id}\"", json);
        Assert.Contains("\"removed\": true", json);
        Assert.Contains("\"visibility\": \"hidden\"", json);
        // A top-level task says nothing about "under", and an unplaced one
        // nothing about "before".
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"under\""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"before\""));

        var back = PackRepository.Deserialize(json)!.Quests.Single();
        Assert.True(back.ChangesTheGamesTasks);
        Assert.Equal(new[] { "Look", "Ask" }, back.AddedTasks.Select(a => a.Key));
        Assert.Equal(Token(parent.Id), back.AddedTasks[0].Under);
        Assert.Equal(Token(game.Tasks[0].Id), back.AddedTasks[1].Before);
        Assert.Equal("AskMarco", back.AddedTasks[1].Subtasks.Single().Key);
        Assert.True(back.VanillaTasks[0].Removed);
        Assert.True(back.VanillaTasks[1].IsHidden);

        // Every walker sees the added tasks as tasks of the pack's.
        Assert.Equal(new[] { "Look", "Ask", "AskMarco" }, back.AllTasks().Select(t => t.Key));
    }

    [Fact]
    public void AnEntryThatOnlySaysThingsDoesNotChangeTheGamesTasks()
    {
        // The control for the orange warning and the game's: descriptions,
        // hiding and actions do not change when the quest finishes.
        var def = new QuestDef { Key = "ext", Source = WithSubtasks.Name, Description = "x" };
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(WithSubtasks.Tasks[0].Id), Visibility = S.Hidden });
        Assert.False(def.ChangesTheGamesTasks);
        def.VanillaTasks[0].Removed = true;
        Assert.True(def.ChangesTheGamesTasks);
    }

    // ── The list on the tab ───────────────────────────────────────────

    [Fact]
    public void ANewTaskGoesJustAfterTheSelectedTopLevelTask()
    {
        var game = VanillaQuests.All.First(q => q.Tasks.Count(t => t.IsTopLevel) >= 2);
        var (_, quest) = Extension(game);
        var tops = game.Tasks.Where(t => t.IsTopLevel).ToList();

        // Selecting a subtask of the first step still means "after that step".
        var firstSub = quest.VanillaTaskRows.FirstOrDefault(r => r.Depth == 1 && game.Task(r.Game.Parent)?.Id == tops[0].Id);
        quest.SelectedExtensionRow = (object?)firstSub ?? quest.VanillaTaskRows.First(r => r.Token == Token(tops[0].Id));
        quest.AddTaskCommand.Execute(null);
        Show(quest);

        var added = Assert.Single(quest.Model.AddedTasks);
        Assert.True(added.IsTopLevel);
        Assert.Equal(Token(tops[1].Id), added.Before);
        Assert.Same(added, ((QuestTaskViewModel)quest.SelectedExtensionRow!).Model);

        // In the list: after everything under the first step, before the second.
        var rows = quest.ExtensionRows.ToList();
        int mine = rows.IndexOf(quest.SelectedExtensionRow!);
        int second = rows.FindIndex(r => r is VanillaTaskRowViewModel g && g.Token == Token(tops[1].Id));
        Assert.Equal(second - 1, mine);
        Assert.Equal(0, ((QuestTaskViewModel)rows[mine]).Depth);

        // Nothing selected: at the end.
        quest.SelectedExtensionRow = null;
        quest.AddTaskCommand.Execute(null);
        Assert.Equal("", quest.Model.AddedTasks.Last().Before);
        Assert.IsType<QuestTaskViewModel>(quest.ExtensionRows.Last());
    }

    [Fact]
    public void ASubtaskCanGoUnderAGameStepThatHasNone()
    {
        var game = WithLeafStep;
        var (_, quest) = Extension(game);
        var leaf = game.Tasks.First(t => t.IsTopLevel && game.Tasks.All(c => c.Parent != t.Id));
        var row = quest.VanillaTaskRows.Single(r => r.Token == Token(leaf.Id));
        Assert.DoesNotContain("subtasks", row.Note);

        quest.SelectedExtensionRow = row;
        Assert.True(quest.AddSubtaskCommand.CanExecute(null));
        quest.AddSubtaskCommand.Execute(null);
        Show(quest);

        var added = Assert.Single(quest.Model.AddedTasks);
        Assert.Equal(Token(leaf.Id), added.Under);
        var mine = (QuestTaskViewModel)quest.SelectedExtensionRow!;
        Assert.Equal(1, mine.Depth);
        Assert.True(mine.IsSubtask);
        Assert.True(mine.IsAddedToGameQuest);

        // The game's step now finishes through its subtasks, and says so.
        var again = quest.VanillaTaskRows.Single(r => r.Token == Token(leaf.Id));
        _out.WriteLine(again.Note);
        Assert.Contains("subtasks", again.Note);
    }

    [Fact]
    public void ASubtaskBesideAGameSubtaskGoesJustAfterIt()
    {
        var game = WithSubtasks;
        var (_, quest) = Extension(game);
        var parent = game.Tasks.First(t => t.IsTopLevel && game.Tasks.Count(c => c.Parent == t.Id) >= 2);
        var kids = game.Tasks.Where(t => t.Parent == parent.Id).ToList();

        quest.SelectedExtensionRow = quest.VanillaTaskRows.Single(r => r.Token == Token(kids[0].Id));
        quest.AddSubtaskCommand.Execute(null);
        Show(quest);

        var added = Assert.Single(quest.Model.AddedTasks);
        Assert.Equal(Token(parent.Id), added.Under);
        Assert.Equal(Token(kids[1].Id), added.Before);

        // Moved up, it passes the game's first subtask and now comes first.
        Assert.True(quest.MoveTaskUpCommand.CanExecute(null));
        quest.MoveTaskUpCommand.Execute(null);
        Show(quest);
        Assert.Equal(Token(kids[0].Id), added.Before);
        Assert.False(quest.MoveTaskUpCommand.CanExecute(null), "the first of its siblings can still move up");

        // The list and the stored placement agree, the way the plugin reads it.
        var tree = ExtensionTree.Build(game, quest.Model);
        var listed = tree.Where(n => ReferenceEquals(n.Parent?.Game, parent)).Select(n => n.Token).ToList();
        _out.WriteLine(string.Join(" ", listed));
        Assert.Equal(new[] { added.Key, Token(kids[0].Id), Token(kids[1].Id) }, listed.Take(3));
    }

    [Fact]
    public void TheGamesOwnTasksCannotBeMoved()
    {
        var (_, quest) = Extension(WithSubtasks);
        quest.SelectedExtensionRow = quest.VanillaTaskRows[1];
        Assert.False(quest.MoveTaskUpCommand.CanExecute(null));
        Assert.False(quest.MoveTaskDownCommand.CanExecute(null));
    }

    [Fact]
    public void RemovingAGameTaskTakesItOutAndCanBeUndone()
    {
        var game = WithSubtasks;
        var (_, quest) = Extension(game);
        var parent = game.Tasks.First(t => t.IsTopLevel && game.Tasks.Count(c => c.Parent == t.Id) >= 2);
        var row = quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id));

        quest.SelectedExtensionRow = row;
        Assert.True(quest.RemoveTaskCommand.CanExecute(null));
        quest.RemoveTaskCommand.Execute(null);
        Show(quest);

        var hook = Assert.Single(quest.Model.VanillaTasks);
        Assert.True(hook.Removed);
        Assert.True(quest.ChangesTheGamesTasks);

        var removed = quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id));
        Assert.True(removed.IsRemoved);
        Assert.True(removed.IsTakenOut);
        Assert.Contains("taken out", removed.RowText);
        Assert.False(removed.CanChooseVisibility);

        // Its subtasks go with it, and nothing can be added under it.
        var child = quest.VanillaTaskRows.First(r => game.Task(r.Game.Parent)?.Id == parent.Id);
        Assert.True(child.RemovedWithParent);
        Assert.False(child.CanChooseRemoved);
        Assert.Contains("taken out with its task", child.RowText);
        quest.SelectedExtensionRow = quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id));
        Assert.False(quest.AddSubtaskCommand.CanExecute(null));
        Assert.False(quest.RemoveTaskCommand.CanExecute(null), "a task already taken out can be removed again");

        // Put back: the entry says nothing any more and leaves the manifest.
        quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id)).IsRemoved = false;
        Assert.Empty(quest.Model.VanillaTasks);
        Assert.False(quest.ChangesTheGamesTasks);
        Assert.False(quest.VanillaTaskRows.First(r => game.Task(r.Game.Parent)?.Id == parent.Id).RemovedWithParent);
    }

    [Fact]
    public void ClearingTheQuestClosesTheTaskOfThePacksThatWasOpen()
    {
        var (_, quest) = Extension(WithSubtasks);
        quest.SelectedExtensionRow = null;
        quest.AddTaskCommand.Execute(null);
        Assert.NotNull(quest.SelectedTask);

        quest.Source = "";
        Assert.Empty(quest.ExtensionRows);
        Assert.Null(quest.SelectedTask);
    }

    [Fact]
    public void RemovingATaskOfThePacksDeletesIt()
    {
        var (_, quest) = Extension(WithSubtasks);
        quest.SelectedExtensionRow = null;
        quest.AddTaskCommand.Execute(null);
        quest.AddSubtaskCommand.Execute(null);
        var top = Assert.Single(quest.Model.AddedTasks);
        Assert.Single(top.Subtasks);

        // The subtask is selected: removing it leaves the task.
        quest.RemoveTaskCommand.Execute(null);
        Assert.Empty(top.Subtasks);
        Assert.Same(top, ((QuestTaskViewModel)quest.SelectedExtensionRow!).Model);

        quest.RemoveTaskCommand.Execute(null);
        Assert.Empty(quest.Model.AddedTasks);
        Assert.DoesNotContain(quest.ExtensionRows, r => r is QuestTaskViewModel);
        Assert.Empty(quest.Model.VanillaTasks);
    }

    [Fact]
    public void TheJournalChoiceFollowsWhereTheTaskSits()
    {
        var game = WithSubtasks;
        var (_, quest) = Extension(game);
        var top = quest.VanillaTaskRows.First(r => r.IsTopLevel);
        var sub = quest.VanillaTaskRows.First(r => !r.IsTopLevel);

        // The journal already leaves out a top-level task that has not started.
        Assert.DoesNotContain(VanillaTaskRowViewModel.ShownOnceStarted, top.VisibilityOptions);
        Assert.Contains(VanillaTaskRowViewModel.ShownOnceStarted, sub.VisibilityOptions);

        sub.Visibility = VanillaTaskRowViewModel.ShownOnceStarted;
        Assert.Equal(S.HiddenUntilStarted, Assert.Single(quest.Model.VanillaTasks).Visibility);
        Assert.Contains("hidden until it starts", sub.RowText);
        Assert.False(sub.IsOutOfSight);

        top.Visibility = VanillaTaskRowViewModel.NeverShown;
        Assert.True(top.IsOutOfSight);
        Assert.False(top.IsTakenOut);
        Assert.True(top.CanChooseVisibility, "an always-hidden task cannot be shown again");
        Assert.False(quest.ChangesTheGamesTasks);

        top.Visibility = VanillaTaskRowViewModel.ShownAsTheGameHasIt;
        sub.Visibility = VanillaTaskRowViewModel.ShownAsTheGameHasIt;
        Assert.Empty(quest.Model.VanillaTasks);
    }

    [Fact]
    public void AnAddedTasksNotesReadTheGameTaskAboveIt()
    {
        var game = VanillaQuests.All.First(q => q.Tasks.Any(t => t.Completion == TaskCompletion.SubtasksInSequence
                                                                  && t.IsTopLevel && q.Tasks.Count(c => c.Parent == t.Id) >= 1));
        var parent = game.Tasks.First(t => t.Completion == TaskCompletion.SubtasksInSequence && t.IsTopLevel
                                           && game.Tasks.Any(c => c.Parent == t.Id));
        var (_, quest) = Extension(game);
        quest.SelectedExtensionRow = quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id));
        quest.AddSubtaskCommand.Execute(null);
        var mine = (QuestTaskViewModel)quest.SelectedExtensionRow!;

        mine.HideUntilStarted = true;
        _out.WriteLine(mine.HideUntilStartedNote);
        Assert.Contains("once the subtask before it is done", mine.HideUntilStartedNote);

        // Moved to the front of its task's subtasks, it is the first one.
        // Bounded: a move that does not take would otherwise spin here forever.
        for (int i = 0; i < 20 && quest.MoveTaskUpCommand.CanExecute(null); i++) quest.MoveTaskUpCommand.Execute(null);
        Assert.False(quest.MoveTaskUpCommand.CanExecute(null), "moving up never reached the top");
        _out.WriteLine(mine.HideUntilStartedNote);
        Assert.Contains("first subtask", mine.HideUntilStartedNote);
    }

    [Fact]
    public void PointingTheEntryElsewhereKeepsTheAuthorsTasks()
    {
        var game = WithSubtasks;
        var (_, quest) = Extension(game);
        var parent = game.Tasks.First(t => t.IsTopLevel && game.Tasks.Count(c => c.Parent == t.Id) >= 2);
        quest.SelectedExtensionRow = quest.VanillaTaskRows.Single(r => r.Token == Token(parent.Id));
        quest.AddSubtaskCommand.Execute(null);
        var added = Assert.Single(quest.Model.AddedTasks);

        var other = VanillaQuests.All.First(q => q.Name != game.Name && q.Task(parent.Id) == null);
        quest.Source = other.Name;
        Show(quest);

        Assert.Same(added, Assert.Single(quest.Model.AddedTasks));
        Assert.True(added.IsTopLevel, "a task placed under a task the new quest lacks was not moved to the top level");
        Assert.Equal("", added.Before);
        Assert.Contains(quest.ExtensionRows, r => r is QuestTaskViewModel t && ReferenceEquals(t.Model, added));
    }

    // ── Rows naming the added tasks ───────────────────────────────────

    [Fact]
    public void QuestRowsCanNameTheTasksAPackAdded()
    {
        var game = WithSubtasks;
        var pack = new ModPack();
        var def = new QuestDef { Key = "ext", Source = game.Name };
        def.AddedTasks.Add(new AddedTaskDef { Key = "Ask", Name = "Ask around", Before = Token(game.Tasks[0].Id) });
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(game.Tasks[0].Id), Removed = true });
        pack.Quests.Add(def);

        var tasks = QuestReferences.TasksOf(pack.Quests, vanilla: true, game.Name)!;
        _out.WriteLine(string.Join(" | ", tasks.Select(t => $"{t.Token}{(t.Added ? "+" : "")}{(t.Removed ? "-" : "")}")));
        Assert.Equal("Ask", tasks[0].Token);
        Assert.True(tasks[0].Added);
        Assert.True(tasks.Single(t => t.Token == Token(game.Tasks[0].Id)).Removed);
        Assert.Equal(game.Tasks.Count + 1, tasks.Count);

        // Everything under a task taken out goes with it.
        var under = game.Tasks.Where(t => t.Parent == game.Tasks[0].Id).Select(t => Token(t.Id)).ToList();
        Assert.NotEmpty(under);
        Assert.All(tasks.Where(t => under.Contains(t.Token)), t => Assert.True(t.Removed, t.Token + " stayed in"));
        Assert.All(tasks.Where(t => !under.Contains(t.Token) && t.Token != Token(game.Tasks[0].Id)),
                   t => Assert.False(t.Removed, t.Token + " was taken out with a task it is not under"));

        // Without the pack's entry the game's quest is as the game has it.
        var plain = QuestReferences.TasksOf(null, vanilla: true, game.Name)!;
        Assert.Equal(game.Tasks.Count, plain.Count);
        Assert.DoesNotContain(plain, t => t.Added || t.Removed);

        // The picker labels them.
        QuestPickerViewModel.PackQuests = () => pack.Quests;
        try
        {
            var ps = new Dictionary<string, string> { ["source"] = "vanilla", ["quest"] = game.Name };
            var picker = new QuestPickerViewModel(ps, () => { }, offersWholeQuest: false);
            Assert.Contains(picker.TaskOptions, o => o.Token == "Ask" && o.Label.Contains("[yours]"));
            Assert.Contains(picker.TaskOptions, o => o.Label.Contains("[taken out]"));
            // An extension is still not one of the pack's own quests.
            ps.Remove("source");
            Assert.DoesNotContain(picker.QuestOptions, o => o.Token == "ext");
        }
        finally { QuestPickerViewModel.PackQuests = null; }
    }

    [Fact]
    public void RenamingAnAddedTaskFollowsTheRowsThatNameIt()
    {
        var pack = new ModPack();
        var mine = new NodeActionDef { Type = NodeActionTypes.Quest };
        mine.Params["source"] = "vanilla";
        mine.Params["quest"] = WithSubtasks.Name;
        mine.Params["task"] = "Ask";
        var ownQuests = new NodeActionDef { Type = NodeActionTypes.Quest };
        ownQuests.Params["quest"] = WithSubtasks.Name;
        ownQuests.Params["task"] = "Ask";
        pack.IntegrationRules.Add(new UpdateRuleDef { Key = "r" });
        pack.IntegrationRules[0].Actions.Add(mine);
        pack.IntegrationRules[0].Actions.Add(ownQuests);

        int n = Services.ReferenceRenamer.RenameQuestTask(pack, WithSubtasks.Name, "Ask", "AskAround", vanilla: true);
        Assert.Equal(1, n);
        Assert.Equal("AskAround", mine.Params["task"]);
        // A row on the Pack side naming a quest of the same name is another quest.
        Assert.Equal("Ask", ownQuests.Params["task"]);
    }

    // ── What Validate says ────────────────────────────────────────────

    private static List<Validation.ValidationIssue> Check(ModPack pack) => Validation.PackValidator.Validate(pack, "");

    private static (ModPack Pack, QuestDef Def) Entry(VanillaQuests.VanillaQuest game)
    {
        var pack = new ModPack();
        var def = new QuestDef { Key = "ext", Source = game.Name };
        pack.Quests.Add(def);
        return (pack, def);
    }

    private static AddedTaskDef Finished(string key, string under = "")
    {
        var t = new AddedTaskDef { Key = key, Name = key, Under = under };
        var c = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        c.Params["name"] = "done";
        t.Conditions.Add(c);
        return t;
    }

    [Fact]
    public void AnAddedTaskNothingFinishesIsWorthSaying()
    {
        var (pack, def) = Entry(WithSubtasks);
        def.AddedTasks.Add(new AddedTaskDef { Key = "Ask", Name = "Ask around" });

        var one = Assert.Single(Check(pack), i => i.Code == "quest.taskNeverCompleted");
        _out.WriteLine(one.Where + ": " + one.Message);
        Assert.Contains("addedTasks[Ask]", one.Where);

        // A Quest action completing it, named on the Vanilla side, is enough.
        var action = new NodeActionDef { Type = NodeActionTypes.Quest };
        action.Params["source"] = "vanilla";
        action.Params["quest"] = WithSubtasks.Name;
        action.Params["operation"] = QuestVocabulary.CompleteTask;
        action.Params["task"] = "Ask";
        pack.IntegrationRules.Add(new UpdateRuleDef { Key = "r" });
        pack.IntegrationRules[0].Actions.Add(action);
        var issues = Check(pack);
        Assert.DoesNotContain(issues, i => i.Code == "quest.taskNeverCompleted");
        // ...and that row is not reported as naming a task the quest lacks.
        Assert.DoesNotContain(issues, i => i.Code == "quest.unknownTask");
    }

    [Fact]
    public void AnAddedTaskNamedLikeAGameTaskIsAnError()
    {
        var (pack, def) = Entry(WithSubtasks);
        def.AddedTasks.Add(Finished("12345"));
        var one = Assert.Single(Check(pack), i => i.Code == "quest.addedTaskNumericKey");
        Assert.Equal(Validation.Severity.Error, one.Severity);

        def.AddedTasks[0].Key = "Task12345";
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.addedTaskNumericKey");
    }

    [Fact]
    public void TwoAddedTasksWithOneNameAreAnError()
    {
        var (pack, def) = Entry(WithSubtasks);
        def.AddedTasks.Add(Finished("Ask"));
        def.AddedTasks.Add(Finished("Ask"));
        Assert.Single(Check(pack), i => i.Code == "quest.duplicateTaskKey" && i.Severity == Validation.Severity.Error);
    }

    [Fact]
    public void WhereAnAddedTaskSitsIsChecked()
    {
        var game = WithSubtasks;
        var (pack, def) = Entry(game);
        var parent = game.Tasks.First(t => t.IsTopLevel && game.Tasks.Any(c => c.Parent == t.Id));

        // Under a task the quest does not have.
        def.AddedTasks.Add(Finished("Lost", under: "999999"));
        Assert.Single(Check(pack), i => i.Code == "quest.addedTaskUnknownParent");

        // A subtask of a subtask.
        var deep = Finished("Deep", under: Token(parent.Id));
        deep.Subtasks.Add(new QuestTaskDef { Key = "Deeper", Name = "Deeper" });
        def.AddedTasks.Add(deep);
        var tooDeep = Assert.Single(Check(pack), i => i.Code == "quest.addedTaskTooDeep");
        Assert.Contains("Deeper", tooDeep.Where);

        // Under a task taken out.
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(parent.Id), Removed = true });
        Assert.Contains(Check(pack), i => i.Code == "quest.addedUnderRemoved" && i.Where.Contains("Deep"));

        // A well-placed one says none of that.
        var (pack2, def2) = Entry(game);
        def2.AddedTasks.Add(Finished("Fine", under: Token(parent.Id)));
        def2.AddedTasks.Add(Finished("Top"));
        var clean = Check(pack2).Where(i => i.Code.StartsWith("quest.added", StringComparison.Ordinal)).ToList();
        foreach (var i in clean) _out.WriteLine(i.Code + " " + i.Message);
        Assert.Empty(clean);
    }

    [Fact]
    public void AnAddedTaskUnderAStepFinishedByAnActionIsWorthSaying()
    {
        var game = VanillaQuests.All.FirstOrDefault(q => q.Tasks.Any(t => t.Completion == TaskCompletion.Manual));
        Assert.NotNull(game);
        var manual = game!.Tasks.First(t => t.Completion == TaskCompletion.Manual);
        var (pack, def) = Entry(game);
        def.AddedTasks.Add(Finished("Note", under: Token(manual.Id)));
        var one = Assert.Single(Check(pack), i => i.Code == "quest.addedUnderByAction");
        _out.WriteLine(one.Message);
        // It never starts, so it is not also reported as holding the quest up.
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.taskNeverCompleted");
    }

    [Fact]
    public void TakingEveryStepOutIsWorthSaying()
    {
        var game = WithSubtasks;
        var (pack, def) = Entry(game);
        foreach (var top in game.Tasks.Where(t => t.IsTopLevel))
            def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(top.Id), Removed = true });
        Assert.Single(Check(pack), i => i.Code == "quest.everyTaskRemoved");

        // One of the pack's own at the top level keeps the quest going...
        def.AddedTasks.Add(Finished("Mine"));
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.everyTaskRemoved");

        // ...and so does one of the game's left in.
        def.AddedTasks.Clear();
        def.VanillaTasks.RemoveAt(0);
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.everyTaskRemoved");
    }

    [Fact]
    public void AVisibilityTheRuntimeDoesNotKnowIsWorthSaying()
    {
        var (pack, def) = Entry(WithSubtasks);
        def.VanillaTasks.Add(new VanillaTaskHookDef { Task = Token(WithSubtasks.Tasks[0].Id), Visibility = "invisible" });
        Assert.Single(Check(pack), i => i.Code == "quest.badVisibility");
        def.VanillaTasks[0].Visibility = S.HiddenUntilStarted;
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.badVisibility");
    }

    [Fact]
    public void AddedTasksAloneAreSomethingToSay()
    {
        var (pack, def) = Entry(WithSubtasks);
        Assert.Contains(Check(pack), i => i.Code == "quest.extensionSaysNothing");
        def.AddedTasks.Add(Finished("Mine"));
        Assert.DoesNotContain(Check(pack), i => i.Code == "quest.extensionSaysNothing");
    }

    [Fact]
    public void AQuestActionOnTheGamesTasksIsCheckedToo()
    {
        // The actions hung on the game's tasks name quests like any others.
        var (pack, def) = Entry(WithSubtasks);
        var hook = new VanillaTaskHookDef { Task = Token(WithSubtasks.Tasks[0].Id) };
        var action = new NodeActionDef { Type = NodeActionTypes.Quest };
        action.Params["quest"] = "NoSuchQuest";
        hook.Actions.Add(action);
        def.VanillaTasks.Add(hook);

        var one = Assert.Single(Check(pack), i => i.Code == "quest.unknownQuest");
        Assert.Contains("vanillaTasks", one.Where);
    }

    // ── On screen ─────────────────────────────────────────────────────

    [Fact]
    public void TheTabListsThePacksTasksAmongTheGamesAndOpensTheRightPanel()
    {
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = TabQuests;
            vm.AddVanillaQuestCommand.Execute(null);
            vm.SelectedQuest!.Source = WithSubtasks.Name;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            var quest = vm.SelectedQuest!;
            var warning = (FrameworkElement)window.FindName("QuestTreeChangeWarning");
            var gamePanel = (GroupBox)window.FindName("VanillaTaskPanel");
            var taskName = (TextBox)window.FindName("QuestTaskNameBox");
            var list = (ListBox)window.FindName("VanillaTaskList");
            var addTask = (Button)window.FindName("AddExtensionTaskButton");

            Assert.False(warning.IsVisible, "the orange warning shows on an entry that changes no task");
            Assert.True(gamePanel.IsVisible, "the first of the game's tasks is not open");
            Assert.False(taskName.IsVisible, "the pack task panel is open with none of the pack's tasks selected");
            Assert.True(addTask.IsVisible && addTask.IsEnabled, "+ Task is not offered on the game's quest");

            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(addTask)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();

            _out.WriteLine($"{list.Items.Count} row(s); selected {list.SelectedItem}");
            Assert.Equal(WithSubtasks.Tasks.Count + 1, list.Items.Count);
            Assert.IsType<QuestTaskViewModel>(list.SelectedItem);
            Assert.True(warning.IsVisible, "the orange warning is missing on an entry that adds a task");
            Assert.True(taskName.IsVisible, "the pack task panel did not open for the new task");
            Assert.False(gamePanel.IsVisible, "the game's task panel stayed open over a task of the pack's");
            Assert.True(((FrameworkElement)window.FindName("AddedTaskNote")).IsVisible);

            // Typed text reaches the added task.
            taskName.Text = "Ask around the docks";
            WindowHarness.Pump();
            var added = Assert.Single(vm.Pack.Quests.Single().AddedTasks);
            Assert.Equal("Ask around the docks", added.Name);

            // The row is drawn as the pack's.
            var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
            var texts = FindAll<TextBlock>(container).Select(t => t.Text).ToList();
            _out.WriteLine("row: " + string.Join(" | ", texts));
            Assert.Contains("yours", texts);

            // Picking one of the game's tasks swaps the panels back, and its
            // controls are there.
            list.SelectedItem = quest.VanillaTaskRows[0];
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            Assert.True(gamePanel.IsVisible);
            Assert.False(taskName.IsVisible);
            var removedBox = (CheckBox)window.FindName("VanillaTaskRemovedBox");
            var visibility = (ComboBox)window.FindName("VanillaTaskVisibilityPicker");
            Assert.True(removedBox.IsVisible && visibility.IsVisible);

            removedBox.IsChecked = true;
            WindowHarness.Pump();
            window.UpdateLayout();
            WindowHarness.Pump();
            Assert.True(vm.Pack.Quests.Single().VanillaTasks.Single().Removed);
            Assert.False(visibility.IsEnabled, "the journal choice stays open on a task taken out");
        });
    }

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
