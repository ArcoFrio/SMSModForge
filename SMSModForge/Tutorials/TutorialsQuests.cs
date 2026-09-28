using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Localization;

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
    private const int TabQuests = 13;

    internal static IReadOnlyList<TutorialDef> All { get; } = new[]
    {
        new TutorialDef
        {
            Id = "a-quest",
            Group = "tutorial.group.quests",
            Title = "tutorial.aQuest.title",
            Summary = "tutorial.aQuest.summary",
            Level = 16,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "tutorial.aQuest.journalGameS.title",
                    Body = "tutorial.aQuest.journalGameS.body",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.startOne.title",
                    Body = "tutorial.aQuest.startOne.body",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuest",
                    AlsoAllow = new[] { "panel:questDetail" },
                    OnEnter = (vm, s) => s.Set("quests", vm.Quests.Count),
                    IsDone = (vm, s) => s.GrewSince("quests", vm.Quests.Count),
                    Hint = "tutorial.aQuest.startOne.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.callSomething.title",
                    Body = "tutorial.aQuest.callSomething.body",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Title.Trim().Length > 0 && q.Title != Loc.T("quests.newName"),
                    Hint = "tutorial.aQuest.callSomething.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.sayWhenStarts.title",
                    Body = "tutorial.aQuest.sayWhenStarts.body",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questStart",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q && q.Model.StartConditions.Count > 0,
                    Hint = "tutorial.aQuest.sayWhenStarts.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.giveSteps.title",
                    Body = "tutorial.aQuest.giveSteps.body",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuestTask",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.Tasks.Count >= 2
                                        && q.Model.Tasks.All(t => t.Name.Trim().Length > 0),
                    Hint = "tutorial.aQuest.giveSteps.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.sayFinishesStep.title",
                    Body = "tutorial.aQuest.sayFinishesStep.body",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questTaskCompletion",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.AllTasks().Any(t => t.Conditions.Count > 0),
                    Hint = "tutorial.aQuest.sayFinishesStep.hint",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.stepsInsideSteps.title",
                    Body = "tutorial.aQuest.stepsInsideSteps.body",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.conversationToo.title",
                    Body = "tutorial.aQuest.conversationToo.body",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "tutorial.aQuest.beforeRelease.title",
                    Body = "tutorial.aQuest.beforeRelease.body",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
            },
        },
    };
}
