using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What happens to a player who had finished one of the pack's quests when a
    /// later version of the pack gives that quest steps it did not have.
    /// <para/>
    /// The author picks per quest (<see cref="Key"/>), and the choices and their
    /// limits are the author's own (2026-09-23/24):
    /// <list type="bullet">
    ///   <item><see cref="LeaveFinished"/>, the default: nothing changes.</item>
    ///   <item><see cref="Reopen"/>: the quest is in progress again with every
    ///   step the player did still done, and only the new ones left - but only
    ///   when that is a clean addition: every new step comes after every step
    ///   the quest already had, and the player had done all of those. Anything
    ///   else is left finished, because reopening it could strand the player
    ///   behind a step they can no longer reach.</item>
    ///   <item><see cref="StartOver"/>: back to not started, so the quest starts
    ///   again the way it always starts.</item>
    /// </list>
    /// Only the pack's own quests: one of the game's quests a pack adds steps
    /// to is left as the player finished it.
    /// <para/>
    /// <b>Which steps are new</b> comes from what the save last saw the quest
    /// hold, which the plugin records in the pack's save file. A save from
    /// before that record existed has none, and then the steps the player never
    /// did that come after the last one they did count as new - except the ones
    /// nobody has to do: the other choices under an "any one" step, and the
    /// notes under a "by action" one.
    /// <para/>
    /// Compiled into both projects: the plugin acts on it, and the editor's
    /// tests check it without a game.
    /// </summary>
    public static class QuestGrowth
    {
        /// <summary>The manifest field on a quest of the pack's own.</summary>
        public const string Key = "whenStepsAdded";

        public const string LeaveFinished = "leave it finished";
        public const string Reopen = "reopen at the new steps";
        public const string StartOver = "start it over";

        public static readonly string[] Choices = { LeaveFinished, Reopen, StartOver };

        /// <summary>One step of the quest, as the pack has it now.</summary>
        public sealed class Step
        {
            public string Key = "";
            public QuestTreeEdits.Completion Completion = QuestTreeEdits.Completion.InOrder;
            public readonly List<Step> Subtasks = new List<Step>();
        }

        public enum Outcome
        {
            /// <summary>Not finished, or nothing new: nothing to do.</summary>
            Nothing,
            /// <summary>New steps, and the quest stays finished.</summary>
            LeftFinished,
            Reopen,
            StartOver,
        }

        /// <summary>Why a quest with new steps was left finished.</summary>
        public enum Because
        {
            /// <summary>The author chose to leave it.</summary>
            Chosen,
            /// <summary>A new step comes before one the quest already had.</summary>
            AddedEarlier,
            /// <summary>The player finished it without doing every step.</summary>
            NotEveryStepDone,
            /// <summary>A new step is one choice of an "any one" step, a note under
            /// a "by action" one, or under a step that had none - nothing reopening
            /// could lead the player through.</summary>
            NothingToLeadTo,
        }

        public sealed class Plan
        {
            public Outcome Outcome = Outcome.Nothing;
            public Because Because = Because.Chosen;

            /// <summary>The steps the player has not seen, in the quest's order.</summary>
            public readonly List<string> Added = new List<string>();

            /// <summary>Steps the player did, to be in progress again because new
            /// ones are under them - outermost first.</summary>
            public readonly List<string> ReopenSteps = new List<string>();

            /// <summary>New steps to start.</summary>
            public readonly List<string> Start = new List<string>();
        }

        /// <summary>The author's choice, read as one of <see cref="Choices"/>;
        /// anything else leaves the quest finished.</summary>
        public static string ChoiceOf(string value)
        {
            foreach (string choice in Choices)
                if (QuestVocabulary.Is(value, choice)) return choice;
            return LeaveFinished;
        }

        /// <summary>Every step's key, in the order the quest is done.</summary>
        public static List<string> KeysOf(IList<Step> roots)
            => Order(roots).Select(p => p.Step.Key).ToList();

        private struct Placed
        {
            public Step Step;
            public Step Parent;

            /// <summary>Under an "any one" or "by action" step, at any depth:
            /// a step the quest can be finished without.</summary>
            public bool Optional;
        }

        private static List<Placed> Order(IList<Step> roots)
        {
            var order = new List<Placed>();
            Walk(roots, null, false, order);
            return order;
        }

        private static void Walk(IList<Step> steps, Step parent, bool optional, List<Placed> into)
        {
            if (steps == null) return;
            foreach (var step in steps)
            {
                if (step == null) continue;
                into.Add(new Placed { Step = step, Parent = parent, Optional = optional });
                bool under = optional
                             || step.Completion == QuestTreeEdits.Completion.AnyOne
                             || step.Completion == QuestTreeEdits.Completion.ByAction;
                Walk(step.Subtasks, step, under, into);
            }
        }

        /// <summary>
        /// What to do with one quest for the save just loaded.
        /// </summary>
        /// <param name="choice">The author's choice (<see cref="Key"/>).</param>
        /// <param name="quest">Where the save stands with the quest.</param>
        /// <param name="roots">The quest's steps, as the pack has them now.</param>
        /// <param name="seen">The steps the save last saw the quest hold, or
        /// null when it has no record of it.</param>
        /// <param name="stateOf">Where the save stands with each step, by key.</param>
        public static Plan Decide(string choice, QuestTreeEdits.TaskState quest, IList<Step> roots,
                                  ICollection<string> seen, Func<string, QuestTreeEdits.TaskState> stateOf)
        {
            var plan = new Plan();
            if (quest != QuestTreeEdits.TaskState.Completed) return plan;

            var order = Order(roots);
            var added = new HashSet<string>(StringComparer.Ordinal);
            if (seen != null)
            {
                foreach (var p in order)
                    if (!seen.Contains(p.Step.Key)) added.Add(p.Step.Key);
            }
            else
            {
                // No record: what the player never did after the last step they
                // did - leaving out what nobody has to do.
                int last = -1;
                for (int i = 0; i < order.Count; i++)
                    if (stateOf(order[i].Step.Key) != QuestTreeEdits.TaskState.Inactive) last = i;
                for (int i = last + 1; i < order.Count; i++)
                    if (!order[i].Optional) added.Add(order[i].Step.Key);
            }
            if (added.Count == 0) return plan;

            foreach (var p in order)
                if (added.Contains(p.Step.Key)) plan.Added.Add(p.Step.Key);

            switch (ChoiceOf(choice))
            {
                case StartOver:
                    plan.Outcome = Outcome.StartOver;
                    return plan;
                case Reopen:
                    break;
                default:
                    return LeftFinished(plan, Because.Chosen);
            }

            // ── Reopen, if it is a clean addition ────────────────────────
            int firstAdded = order.FindIndex(p => added.Contains(p.Step.Key));
            int lastOld = order.FindLastIndex(p => !added.Contains(p.Step.Key));
            if (lastOld > firstAdded) return LeftFinished(plan, Because.AddedEarlier);

            foreach (var p in order)
            {
                if (added.Contains(p.Step.Key) || p.Optional) continue;
                if (stateOf(p.Step.Key) != QuestTreeEdits.TaskState.Completed)
                    return LeftFinished(plan, Because.NotEveryStepDone);
            }

            var parents = order.ToDictionary(p => p.Step, p => p.Parent);
            foreach (var p in order)
            {
                if (!added.Contains(p.Step.Key) || p.Parent == null || added.Contains(p.Parent.Key)) continue;
                // A new step under a step the player did: that step has to lead
                // to it, in order or with the rest - and it must have had steps
                // of its own before, or it was a step whose completion ran its
                // actions and would run them again.
                bool leads = p.Parent.Completion == QuestTreeEdits.Completion.InOrder
                             || p.Parent.Completion == QuestTreeEdits.Completion.AnyOrder;
                bool hadSteps = p.Parent.Subtasks.Any(s => !added.Contains(s.Key));
                if (p.Optional || !leads || !hadSteps) return LeftFinished(plan, Because.NothingToLeadTo);
            }

            // The first new step, the steps above it opened again, and - under
            // an "any order" step - its new siblings too, since those all start
            // together. In-order steps start the next one by themselves.
            var first = order[firstAdded];
            var chain = new List<Step>();
            for (var up = first.Parent; up != null; up = parents[up]) chain.Insert(0, up);
            plan.ReopenSteps.AddRange(chain.Select(s => s.Key));

            plan.Start.Add(first.Step.Key);
            foreach (var up in chain)
            {
                if (up.Completion != QuestTreeEdits.Completion.AnyOrder) continue;
                foreach (var sibling in up.Subtasks)
                    if (added.Contains(sibling.Key) && !plan.Start.Contains(sibling.Key)) plan.Start.Add(sibling.Key);
            }
            plan.Outcome = Outcome.Reopen;
            return plan;
        }

        private static Plan LeftFinished(Plan plan, Because because)
        {
            plan.Outcome = Outcome.LeftFinished;
            plan.Because = because;
            return plan;
        }
    }
}
