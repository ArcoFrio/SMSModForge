using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

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
            Group = "Quests",
            Title = "A quest in the journal",
            Summary = "Make a quest that starts, moves along and finishes by itself.",
            Level = 16,
            Steps = new[]
            {
                new TutorialStep
                {
                    Title = "The journal is the game's",
                    Body = "The game's quest screen lists the game's own quests. A quest made " +
                           "here goes into that same list, beside them, with the same popups when " +
                           "it starts and when it is finished.\n\n" +
                           "Everything about it is on this tab: what it is called, when it starts, " +
                           "its steps, and what finishes each one.",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "Start one",
                    Body = "Press + Quest. It arrives with a title waiting to be replaced.",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuest",
                    AlsoAllow = new[] { "panel:questDetail" },
                    OnEnter = (vm, s) => s.Set("quests", vm.Quests.Count),
                    IsDone = (vm, s) => s.GrewSince("quests", vm.Quests.Count),
                    Hint = "+ Quest, at the top of the list on the left.",
                },
                new TutorialStep
                {
                    Title = "Call it something",
                    Body = "Give it a title. The game writes its own as a name and then the person " +
                           "it is about in brackets - \"Secrets (Adrian)\" - and the journal paints " +
                           "the bracketed part pink by itself. Yours can follow that or not.\n\n" +
                           "The runtime name under it follows the title while the quest is new. " +
                           "The game files a player's progress under it, which the last step comes " +
                           "back to.",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Title.Trim().Length > 0 && q.Title != "New quest",
                    Hint = "Title, in the Quest box on the right.",
                },
                new TutorialStep
                {
                    Title = "Say when it starts",
                    Body = "Add a start condition - any of the conditions a dialogue or a rule can " +
                           "use. The quest starts by itself the moment all of them pass: a variable " +
                           "reaching a value, a day going by, the player being somewhere.\n\n" +
                           "An empty list is the one place where empty does not mean 'always'. With " +
                           "no start conditions a quest waits for a Quest action to start it, so a " +
                           "quest you have only begun writing never turns up in a player's journal.",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questStart",
                    IsDone = (vm, s) => vm.SelectedQuest is { } q && q.Model.StartConditions.Count > 0,
                    Hint = "+ Add condition, in the Starts when box.",
                },
                new TutorialStep
                {
                    Title = "Give it steps",
                    Body = "Add two tasks and write a line for each - the lines the player reads " +
                           "under the quest.\n\n" +
                           "Tasks run top to bottom. Starting the quest starts the first one, " +
                           "finishing one starts the next, and finishing the last completes the " +
                           "quest. The journal shows each one as it starts.",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "btn:addQuestTask",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.Tasks.Count >= 2
                                        && q.Model.Tasks.All(t => t.Name.Trim().Length > 0),
                    Hint = "+ Task twice, then select each one and fill in its Text.",
                },
                new TutorialStep
                {
                    Title = "Say what finishes a step",
                    Body = "Select a task and add a condition under Completes when. While the task " +
                           "is in progress, the moment its conditions all pass it is done and the " +
                           "next one starts.\n\n" +
                           "When it completes is the other half: actions that run as the task " +
                           "finishes - set a variable, show something, start another quest. They " +
                           "run however the task was finished, so they are the place for anything " +
                           "that should always follow it.",
                    Kind = StepKind.Do,
                    Tab = TabQuests,
                    Anchor = "panel:questTaskCompletion",
                    AlsoAllow = new[] { "panel:questDetail" },
                    IsDone = (vm, s) => vm.SelectedQuest is { } q
                                        && q.Model.AllTasks().Any(t => t.Conditions.Count > 0),
                    Hint = "Select a task, then + Add condition under Completes when.",
                },
                new TutorialStep
                {
                    Title = "Steps inside steps",
                    Body = "A task can have subtasks. The journal draws them under it with a small " +
                           "arrow, and never with a tick - even the game's finished subtasks look " +
                           "the same as unfinished ones. Only a top-level task gets the check mark.\n\n" +
                           "Once a task has subtasks it finishes through them - in order, in any " +
                           "order, or when any one is done, as its Completes box says - so its own " +
                           "conditions and actions go away and its subtasks get them instead. Set it " +
                           "to 'by action' and its subtasks never start at all: they become notes " +
                           "under it, which is how the game gives hints.\n\n" +
                           "A task can also count - three flowers found, five photos taken. " +
                           "Quest actions can set the count, or it can follow a number variable " +
                           "you already keep. Either way it finishes the task when it reaches the " +
                           "target, and only shows beside a top-level task.",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
                new TutorialStep
                {
                    Title = "From a conversation, too",
                    Body = "A dialogue node, a rule or a UI button can move a quest as well, with the " +
                           "Quest action: start it, complete the step the player is on, add to a " +
                           "count. The QuestState and QuestCounter conditions ask where a player has " +
                           "got to, so a conversation can wait until a quest is in progress. Both " +
                           "can name the game's own quests too.\n\n" +
                           "The game only finishes a step that is in progress, so completing the " +
                           "second task before the first is done does nothing at all.",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                },
                new TutorialStep
                {
                    Title = "Before you release it",
                    Body = "The game saves a player's progress on a quest under its runtime name, " +
                           "and on a task under the task's. Renaming either once players have " +
                           "your pack starts that quest over for every one of them, because to " +
                           "the game it is a different quest.\n\n" +
                           "Rename freely while you are writing. After release, leave the names " +
                           "alone and change the title and text instead - those are only what " +
                           "the journal shows.",
                    Kind = StepKind.Read,
                    Tab = TabQuests,
                    Anchor = "panel:questDetail",
                },
            },
        },
    };
}
