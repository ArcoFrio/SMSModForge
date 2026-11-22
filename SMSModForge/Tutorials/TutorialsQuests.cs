using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Localization;
using SMSModForge.ViewModel;

namespace SMSModForge.Tutorials;

/// <summary>
/// The Quests tab: a quest of the pack's own in the game's journal, moving
/// along by its own conditions.
/// <para/>
/// Kept on one tab. A quest here needs nothing from any other tab to start,
/// progress and finish, which is the point of the tab - the Quest action and
/// the quest conditions are mentioned for what they add, not taught.
/// </summary>
internal static class TutorialsQuests
{
    private const int TabQuests = MainTab.Quests;

    /// <summary>The story's quest (tutorial 16). English on purpose: the pack's words.</summary>
    internal const string QuestTitle = "Make her a star (Dummy)";

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "a-quest",
            Group = "tutorial.group.quests",
            Title = "tutorial.aQuest.title",
            Summary = "tutorial.aQuest.summary",
            Makes = "tutorial.aQuest.makes",
            Level = 17,
            // 2 (1.10.0): "Dummy Days" - "Make her a star"; short steps with fold-outs.
            Revision = 2,
            Leaves = vm => vm.Quests.Any(q => q.Title.Trim() == QuestTitle),
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aQuest.journalGameS.title",
                    Body = "tutorial.aQuest.journalGameS.body",
                    More = "tutorial.aQuest.journalGameS.more",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.startOne.title",
                    Body = "tutorial.aQuest.startOne.body",
                    More = "tutorial.aQuest.startOne.more",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuest",
                    AlsoAllow = new[] { "panel:questDetail" },
                    OnEnter = (vm, s) => s.Set("quests", vm.Quests.Count),
                    IsDone = (vm, s) => s.GrewSince("quests", vm.Quests.Count),
                    Solve = (vm, s) => vm.AddQuestCommand.Execute(null),
                    Hint = "tutorial.aQuest.startOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.callSomething.title",
                    Body = "tutorial.aQuest.callSomething.body",
                    More = "tutorial.aQuest.callSomething.more",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Title.Trim().Length > 0 && q.Title != Loc.T("quests.newName"),
                    Solve = (vm, s) => vm.SelectedQuest!.Title = QuestTitle,
                    Hint = "tutorial.aQuest.callSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.sayWhenStarts.title",
                    Body = "tutorial.aQuest.sayWhenStarts.body",
                    More = "tutorial.aQuest.sayWhenStarts.more",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questStart",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q && q.Model.StartConditions.Count > 0,
                    Solve = (vm, s) =>
                    {
                        var c = vm.SelectedQuest!.StartConditions.Add();
                        c.DisplayType = ViewModel.NodeConditionViewModel.VariableFamilyType;
                        c.VarName = "KeptHerSecret";   // English on purpose: the story's variable (tutorial 12).
                        c.VarValue = "true";
                    },
                    Hint = "tutorial.aQuest.sayWhenStarts.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.giveSteps.title",
                    Body = "tutorial.aQuest.giveSteps.body",
                    More = "tutorial.aQuest.giveSteps.more",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuestTask",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.Tasks.Count >= 2
                                        && q.Model.Tasks.All(t => t.Name.Trim().Length > 0),
                    Solve = (vm, s) =>
                    {
                        var q = vm.SelectedQuest!;
                        // English on purpose: the story pack's words.
                        q.AddTaskCommand.Execute(null);
                        // English on purpose: the story pack's words.
                        q.SelectedTask!.Name = "Visit her until she's sure of herself.";
                        q.AddTaskCommand.Execute(null);
                        // English on purpose: the story pack's words.
                        q.SelectedTask!.Name = "Take her first Starmaker photo.";
                    },
                    Hint = "tutorial.aQuest.giveSteps.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.sayFinishesStep.title",
                    Body = "tutorial.aQuest.sayFinishesStep.body",
                    More = "tutorial.aQuest.sayFinishesStep.more",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questTaskCompletion",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.AllTasks().Any(t => t.Conditions.Count > 0),
                    Solve = (vm, s) =>
                    {
                        var q = vm.SelectedQuest!;
                        q.SelectedTask = q.TaskRows[0];
                        var c = q.SelectedTask.CompletionConditions.Add();
                        c.DisplayType = ViewModel.NodeConditionViewModel.VariableFamilyType;
                        c.VarComparison = "greater or equal";   // English on purpose: a value the code compares.
                        c.VarName = "Confidence";   // English on purpose: the story's variable (tutorial 13).
                        c.VarValue = "5";
                    },
                    // Task 1 chosen for the reader: its completion box is the one the step points at.
                    // Task 1 always: adding the second left it chosen, with its own Completes when under the ring (review of 2026-11-22).
                    OnShown = vm => { if (vm.SelectedQuest is { } q && q.TaskRows.Count > 0) q.SelectedTask = q.TaskRows[0]; },
                    Hint = "tutorial.aQuest.sayFinishesStep.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.stepsInsideSteps.title",
                    Body = "tutorial.aQuest.stepsInsideSteps.body",
                    More = "tutorial.aQuest.stepsInsideSteps.more",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.conversationToo.title",
                    Body = "tutorial.aQuest.conversationToo.body",
                    More = "tutorial.aQuest.conversationToo.more",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.beforeRelease.title",
                    Body = "tutorial.aQuest.beforeRelease.body",
                    More = "tutorial.aQuest.beforeRelease.more",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
            },
        },
    };
}
