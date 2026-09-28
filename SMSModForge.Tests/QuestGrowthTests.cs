using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;
using C = SMSModForge.Shared.QuestTreeEdits.Completion;
using G = SMSModForge.Shared.QuestGrowth;
using S = SMSModForge.Shared.QuestTreeEdits.TaskState;

namespace SMSModForge.Tests;

/// <summary>
/// A pack quest that gets tasks after a player finished it: left finished,
/// reopened at the new tasks, or started over, as its author chose - and
/// reopened only when that is a clean addition (the author's rules,
/// 2026-09-23/24).
/// </summary>
public sealed class QuestGrowthTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-growth-" + Guid.NewGuid().ToString("N"));

    public QuestGrowthTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ── Building quests ──────────────────────────────────────────────

    private static G.Step Step(string key, params G.Step[] subtasks) => Step(key, C.InOrder, subtasks);

    private static G.Step Step(string key, C completion, params G.Step[] subtasks)
    {
        var step = new G.Step { Key = key, Completion = completion };
        step.Subtasks.AddRange(subtasks);
        return step;
    }

    /// <summary>Every step done, except the ones named.</summary>
    private static Func<string, S> Done(params string[] notDone)
        => key => notDone.Contains(key) ? S.Inactive : S.Completed;

    private static HashSet<string> Seen(params string[] keys) => new(keys, StringComparer.Ordinal);

    private G.Plan Decide(string choice, IList<G.Step> roots, ICollection<string>? seen, Func<string, S> stateOf,
                          S quest = S.Completed)
    {
        var plan = G.Decide(choice, quest, roots, seen, stateOf);
        _out.WriteLine($"{plan.Outcome} ({plan.Because}); added [{string.Join(", ", plan.Added)}]; "
                       + $"reopen [{string.Join(", ", plan.ReopenSteps)}]; start [{string.Join(", ", plan.Start)}]");
        return plan;
    }

    // ── Nothing to do ────────────────────────────────────────────────

    [Fact]
    public void AQuestNotFinishedIsLeftToRun()
    {
        var roots = new[] { Step("a"), Step("b") };
        foreach (var state in new[] { S.Inactive, S.Active, S.Failed, S.Abandoned })
            Assert.Equal(G.Outcome.Nothing, Decide(G.StartOver, roots, Seen("a"), Done("b"), state).Outcome);
    }

    [Fact]
    public void AFinishedQuestWithNothingNewIsLeftAlone()
        => Assert.Equal(G.Outcome.Nothing,
                        Decide(G.StartOver, new[] { Step("a", Step("a1")), Step("b") }, Seen("a", "a1", "b"), Done()).Outcome);

    [Fact]
    public void LeavingItFinishedIsTheDefault_AndAnythingUnknownMeansThat()
    {
        var roots = new[] { Step("a"), Step("b") };
        var plan = Decide("", roots, Seen("a"), Done("b"));
        Assert.Equal(G.Outcome.LeftFinished, plan.Outcome);
        Assert.Equal(G.Because.Chosen, plan.Because);
        Assert.Equal(new[] { "b" }, plan.Added);

        Assert.Equal(G.LeaveFinished, G.ChoiceOf("something else"));
        Assert.Equal(G.Reopen, G.ChoiceOf("  Reopen At The New Steps "));
    }

    // ── Reopening a clean addition ───────────────────────────────────

    [Fact]
    public void ANewTaskAtTheEndReopensTheQuestThere()
    {
        var plan = Decide(G.Reopen, new[] { Step("a"), Step("b"), Step("c") }, Seen("a", "b"), Done("c"));
        Assert.Equal(G.Outcome.Reopen, plan.Outcome);
        Assert.Empty(plan.ReopenSteps);
        Assert.Equal(new[] { "c" }, plan.Start);
    }

    [Fact]
    public void ANewSubtaskAtTheEndOfTheLastTaskReopensThatTaskToo()
    {
        // The common shape: one task, its steps under it, and a step added at the end.
        var roots = new[] { Step("main", Step("s1"), Step("s2"), Step("s3")) };
        var plan = Decide(G.Reopen, roots, Seen("main", "s1", "s2"), Done("s3"));
        Assert.Equal(G.Outcome.Reopen, plan.Outcome);
        Assert.Equal(new[] { "main" }, plan.ReopenSteps);
        Assert.Equal(new[] { "s3" }, plan.Start);
    }

    [Fact]
    public void UnderAnAnyOrderTask_AllItsNewSubtasksStartTogether()
    {
        var roots = new[] { Step("main", C.AnyOrder, Step("s1"), Step("s2"), Step("s3")) };
        var plan = Decide(G.Reopen, roots, Seen("main", "s1"), Done("s2", "s3"));
        Assert.Equal(G.Outcome.Reopen, plan.Outcome);
        Assert.Equal(new[] { "s2", "s3" }, plan.Start);
    }

    [Fact]
    public void ChoicesNobodyHadToTakeDoNotCountAsUndone()
    {
        // "any one" of two: the player took one, the other was never started.
        var roots = new[] { Step("pick", C.AnyOne, Step("left"), Step("right")), Step("end") };
        var plan = Decide(G.Reopen, roots, Seen("pick", "left", "right"), Done("right", "end"));
        Assert.Equal(G.Outcome.Reopen, plan.Outcome);
        Assert.Equal(new[] { "end" }, plan.Start);
    }

    // ── Not a clean addition: left finished ──────────────────────────

    [Fact]
    public void ANewTaskBeforeOneTheQuestHadLeavesItFinished()
    {
        var roots = new[] { Step("a", Step("a1"), Step("a2")), Step("b") };
        var plan = Decide(G.Reopen, roots, Seen("a", "a1", "b"), Done("a2"));
        Assert.Equal(G.Outcome.LeftFinished, plan.Outcome);
        Assert.Equal(G.Because.AddedEarlier, plan.Because);
    }

    [Fact]
    public void FinishedWithoutDoingEveryTaskLeavesItFinished()
    {
        // The quest's own rules let a task under an in-order task be completed
        // with its subtasks undone.
        var roots = new[] { Step("a", Step("a1"), Step("a2")), Step("b") };
        var plan = Decide(G.Reopen, roots, Seen("a", "a1", "a2"), Done("a2", "b"));
        Assert.Equal(G.Outcome.LeftFinished, plan.Outcome);
        Assert.Equal(G.Because.NotEveryStepDone, plan.Because);
    }

    [Theory]
    [InlineData("under a task that had no subtasks")]
    [InlineData("under an any-one task")]
    [InlineData("under a by-action task")]
    public void ANewTaskNothingWouldLeadToLeavesItFinished(string where)
    {
        var roots = where switch
        {
            "under a task that had no subtasks" => new[] { Step("a"), Step("b", Step("b1")) },
            "under an any-one task" => new[] { Step("a"), Step("b", C.AnyOne, Step("b1"), Step("b2")) },
            _ => new[] { Step("a"), Step("b", C.ByAction, Step("b1"), Step("b2")) },
        };
        var seen = where == "under a task that had no subtasks" ? Seen("a", "b") : Seen("a", "b", "b1");
        var plan = Decide(G.Reopen, roots, seen, Done("b2"));
        Assert.Equal(G.Outcome.LeftFinished, plan.Outcome);
        Assert.Equal(G.Because.NothingToLeadTo, plan.Because);
    }

    // ── Starting over ────────────────────────────────────────────────

    [Fact]
    public void StartingOverDoesNotAskWhetherTheAdditionIsClean()
    {
        var roots = new[] { Step("a", Step("a1"), Step("a2")), Step("b") };
        Assert.Equal(G.Outcome.StartOver, Decide(G.StartOver, roots, Seen("a", "a1", "b"), Done("a2")).Outcome);
    }

    // ── A save with no record of the quest ───────────────────────────

    [Fact]
    public void WithNoRecord_WhatThePlayerNeverDidAfterTheirLastTaskIsNew()
    {
        var roots = new[] { Step("main", Step("s1"), Step("s2"), Step("s3"), Step("s4")) };
        var plan = Decide(G.Reopen, roots, null, Done("s3", "s4"));
        Assert.Equal(G.Outcome.Reopen, plan.Outcome);
        Assert.Equal(new[] { "s3", "s4" }, plan.Added);
        Assert.Equal(new[] { "s3" }, plan.Start);

        // The control: every task done, nothing is new.
        Assert.Equal(G.Outcome.Nothing, Decide(G.Reopen, roots, null, Done()).Outcome);
    }

    [Fact]
    public void WithNoRecord_ChoicesNobodyHadToTakeAreNotNew()
    {
        // The last task is "any one" of two and the player took the first: the
        // second, never started and last in the list, is not a new task.
        var roots = new[] { Step("a"), Step("pick", C.AnyOne, Step("left"), Step("right", Step("right1"))) };
        Assert.Equal(G.Outcome.Nothing, Decide(G.StartOver, roots, null, Done("right", "right1")).Outcome);
    }

    // ── The setting, saved ───────────────────────────────────────────

    [Fact]
    public void TheChoiceIsSavedOnlyWhenItIsNotTheDefault_AndOnlyOnThePacksOwnQuests()
    {
        var pack = PackRepository.CreateEmpty("growth.pack");
        pack.Quests.Add(new QuestDef { Key = "plain" });
        pack.Quests.Add(new QuestDef { Key = "reopens", WhenStepsAdded = G.Reopen });
        pack.Quests.Add(new QuestDef { Key = "restarts", WhenStepsAdded = G.StartOver });
        pack.Quests.Add(new QuestDef { Key = "game", Source = "Debt", WhenStepsAdded = G.Reopen });
        PackRepository.Save(pack, _dir);

        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_dir, "modpack.json")));
        var quests = ((JArray)saved["quests"]!).OfType<JObject>().ToDictionary(q => (string)q["key"]!);
        Assert.Null(quests["plain"][G.Key]);
        Assert.Equal(G.Reopen, (string?)quests["reopens"][G.Key]);
        Assert.Equal(G.StartOver, (string?)quests["restarts"][G.Key]);
        Assert.Null(quests["game"][G.Key]);

        var back = PackRepository.Load(_dir);
        Assert.Equal(G.Reopen, back.Quests.Single(q => q.Key == "reopens").WhenStepsAdded);
        Assert.Equal(G.LeaveFinished, G.ChoiceOf(back.Quests.Single(q => q.Key == "plain").WhenStepsAdded));
    }
}
