using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameCreator.Runtime.Quests;
using SMSModForge.Shared;
using GcQuest = GameCreator.Runtime.Quests.Quest;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The pack's own quests that have tasks a player had not seen when they
    /// finished them, dealt with once per loaded save the way each quest's
    /// author chose. What to do is <see cref="QuestGrowth"/>'s decision; this
    /// is the doing, in the game's journal.
    /// <para/>
    /// <b>Reopening writes the journal's entries directly</b> - the quest, and
    /// the tasks above the new ones, back to in progress - rather than asking
    /// the journal to start them. Starting a quest or a task runs what the game
    /// runs when one starts; these were started long ago, and every task the
    /// player finished keeps its tick. Only the new tasks are started the
    /// journal's own way, so they start as any task does. Read off the game's
    /// IL (2026-09-24): starting a task asks nothing of its quest or parent,
    /// only that the task itself has not started.
    /// <para/>
    /// <b>Starting over</b> is the journal's own reset - the same the Quest
    /// action's "reset quest" does - which puts the quest and all its tasks back
    /// to not started.
    /// </summary>
    internal static class QuestGrowthRuntime
    {
        private const string Tag = "[SMSModForge.PackPlugin] Quests: ";

        /// <summary>
        /// Record every pack quest's tasks as they are now, in each pack's save
        /// data: at pack load for a new game (a loaded save replaces it with its
        /// own record when it is bound), and after <see cref="Apply"/>, so the
        /// save's next write carries what it was played with.
        /// </summary>
        public static void Remember(IEnumerable<PackContext> contexts)
        {
            foreach (var ctx in contexts)
            {
                if (ctx?.Vars == null || ctx.Pack == null) continue;
                foreach (var spec in QuestRuntime.ReadSpecs(ctx.Pack, null))
                    ctx.Vars.RememberQuestSteps(spec.Key, QuestGrowth.KeysOf(StepsOf(spec.Tasks)));
            }
        }

        /// <summary>Once per loaded save, after the player has answered any
        /// warning about it: every pack quest, then the record brought up to
        /// date.</summary>
        public static void Apply(IList<PackContext> contexts, ManualLogSource log)
        {
            var journal = QuestRuntime.Journal;
            if (journal == null) return;

            foreach (var ctx in contexts)
            {
                if (ctx?.Vars == null || ctx.Pack == null) continue;
                foreach (var spec in QuestRuntime.ReadSpecs(ctx.Pack, null))
                {
                    try { ApplyOne(journal, ctx, spec, log); }
                    catch (Exception e)
                    {
                        log?.LogError(Tag + "'" + spec.Key + "' (" + ctx.PackId + "): could not check it for tasks "
                                      + "added since it was finished: " + e);
                    }
                }
            }
            Remember(contexts);
        }

        private static void ApplyOne(Journal journal, PackContext ctx, QuestSpec spec, ManualLogSource log)
        {
            var quest = QuestRegistry.Find(ctx.PackId, spec.Key);
            if (quest == null) return;

            var seen = ctx.Vars.QuestStepsSeen(spec.Key);
            Func<string, int> idOf = key => QuestIds.TaskId(ctx.PackId, spec.Key, key);
            var plan = QuestGrowth.Decide(spec.WhenStepsAdded,
                                          VanillaQuestEdits.StateOf(journal.GetQuestState(quest)),
                                          StepsOf(spec.Tasks), seen,
                                          key => VanillaQuestEdits.StateOf(journal.GetTaskState(quest, idOf(key))));
            if (plan.Outcome == QuestGrowth.Outcome.Nothing) return;

            string name = "'" + spec.Key + "' (" + ctx.PackId + ")";
            string what = name + " was finished in this save and now has tasks it did not have then ("
                          + string.Join(", ", plan.Added.ToArray()) + ")"
                          + (seen == null ? " - worked out from where the player stopped, as this save has no record of it" : "");

            switch (plan.Outcome)
            {
                case QuestGrowth.Outcome.LeftFinished:
                    log?.LogInfo(Tag + what + ". Left finished: " + Why(plan.Because) + ".");
                    return;

                case QuestGrowth.Outcome.StartOver:
                    journal.DeactivateQuest(quest);
                    log?.LogInfo(Tag + what + ". Started over, as the pack asks: it is back to "
                                 + journal.GetQuestState(quest) + " and starts again the way it always starts.");
                    return;

                case QuestGrowth.Outcome.Reopen:
                    Reopen(journal, quest, plan, idOf);
                    log?.LogInfo(Tag + what + ". Reopened at the new tasks, as the pack asks: the quest is "
                                 + journal.GetQuestState(quest) + ", "
                                 + string.Join(", ", plan.Start.Select(k => k + " " + journal.GetTaskState(quest, idOf(k))).ToArray())
                                 + (plan.ReopenSteps.Count > 0 ? "; back in progress above them: " + string.Join(", ", plan.ReopenSteps.ToArray()) : "")
                                 + ".");
                    return;
            }
        }

        private static void Reopen(Journal journal, GcQuest quest, QuestGrowth.Plan plan, Func<string, int> idOf)
        {
            journal.QuestEntries[quest.Id] = QuestEntry.NewActive(journal.IsQuestTracking(quest));
            foreach (string key in plan.ReopenSteps)
            {
                int id = idOf(key);
                journal.TaskEntries[new TaskKey(quest, id)] = TaskEntry.NewActive(journal.GetTaskValue(quest, id));
            }
            foreach (string key in plan.Start) journal.ActivateTask(quest, idOf(key));
        }

        private static string Why(QuestGrowth.Because because)
        {
            switch (because)
            {
                case QuestGrowth.Because.AddedEarlier:
                    return "the pack asks to reopen it, but a new task comes before one it already had";
                case QuestGrowth.Because.NotEveryStepDone:
                    return "the pack asks to reopen it, but the player finished it without doing every task";
                case QuestGrowth.Because.NothingToLeadTo:
                    return "the pack asks to reopen it, but a new task sits where nothing would lead the player to it";
                default:
                    return "the pack leaves finished quests finished";
            }
        }

        /// <summary>The quest's tasks, as <see cref="QuestGrowth"/> reads them.</summary>
        internal static List<QuestGrowth.Step> StepsOf(IEnumerable<TaskSpec> tasks)
        {
            var steps = new List<QuestGrowth.Step>();
            foreach (var t in tasks ?? Enumerable.Empty<TaskSpec>())
            {
                var step = new QuestGrowth.Step { Key = t.Key, Completion = VanillaQuestEdits.CompletionOf(t.Completion) };
                step.Subtasks.AddRange(StepsOf(t.Subtasks));
                steps.Add(step);
            }
            return steps;
        }
    }
}
