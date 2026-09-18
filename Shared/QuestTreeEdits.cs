using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What a pack does to the task list of one of the game's own quests, in
    /// terms both sides can compute the same way.
    /// <para/>
    /// Compiled into both projects from one file, like <see cref="QuestIds"/>:
    /// the editor lists a quest's tasks in the order this says, and the runtime
    /// builds the game's quest in the same order - two copies of the placement
    /// rule is how the list an author reads stops being the list a player gets.
    /// <para/>
    /// Written for the plugin's compiler as well as the editor's, so no newer
    /// C# than the plugin's.
    /// </summary>
    public static class QuestTreeEdits
    {
        // ── On disk ──────────────────────────────────────────────────────

        /// <summary>The extension's list of tasks the pack adds.</summary>
        public const string AddedTasksKey = "addedTasks";

        /// <summary>On an added task: the id of the game's task it sits under.
        /// Absent for a top-level one.</summary>
        public const string UnderKey = "under";

        /// <summary>On an added task: the id of the game's task it comes before,
        /// among the tasks it sits with. Absent for "after all of the game's".</summary>
        public const string BeforeKey = "before";

        /// <summary>On a game task: how the journal shows it. Absent for "as the
        /// game has it".</summary>
        public const string VisibilityKey = "visibility";

        /// <summary>On a game task: taken out of the quest.</summary>
        public const string RemovedKey = "removed";

        /// <summary>Out of the journal until it has started.</summary>
        public const string HiddenUntilStarted = "hidden until started";

        /// <summary>Never in the journal.</summary>
        public const string Hidden = "hidden";

        /// <summary>Out of the journal until its show conditions pass.</summary>
        public const string HiddenUntilConditions = "hidden until conditions pass";

        /// <summary>On any task: the conditions that bring a task hidden until
        /// they pass into the journal.</summary>
        public const string ShowConditionsKey = "showConditions";

        /// <summary>On any task: hide it again when its show conditions stop
        /// passing. Absent: once shown, it stays shown in that save.</summary>
        public const string ShowConditionsLiveKey = "showConditionsLive";

        /// <summary>On a task of the pack's: hidden until its show conditions pass.</summary>
        public const string HideUntilConditionsKey = "hideUntilConditions";

        // ── Where added tasks go ─────────────────────────────────────────

        /// <summary>One child of a task (or of the quest), in the order the
        /// journal lists them: either one of the game's own or one the pack adds.</summary>
        public struct Placed<TGame, TAdded>
        {
            public TGame Game;
            public TAdded Added;
            public bool IsAdded;
        }

        /// <summary>
        /// The children of one task, or the quest's top-level tasks, with the
        /// pack's added ones among the game's own.
        /// <para/>
        /// An added task sits just before the game task its "before" names, and
        /// after all of the game's when it names none - or names one that is not
        /// there any more, which is where a quest the game changed in an update
        /// leaves it rather than losing it. Added tasks in the same place keep
        /// the order the pack lists them in.
        /// </summary>
        /// <param name="game">The game's own children, in the game's order.</param>
        /// <param name="tokenOf">A game task's id, as text.</param>
        /// <param name="added">The pack's tasks for this same parent, in the
        /// pack's order.</param>
        /// <param name="beforeOf">The id an added task says it comes before.</param>
        public static List<Placed<TGame, TAdded>> Arrange<TGame, TAdded>(
            IList<TGame> game, Func<TGame, string> tokenOf,
            IList<TAdded> added, Func<TAdded, string> beforeOf)
        {
            var result = new List<Placed<TGame, TAdded>>();
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in game) tokens.Add(tokenOf(g) ?? "");

            var placed = new bool[added.Count];
            foreach (var g in game)
            {
                string token = tokenOf(g) ?? "";
                for (int i = 0; i < added.Count; i++)
                {
                    if (placed[i] || !string.Equals(Clean(beforeOf(added[i])), token, StringComparison.Ordinal)) continue;
                    placed[i] = true;
                    result.Add(new Placed<TGame, TAdded> { Added = added[i], IsAdded = true });
                }
                result.Add(new Placed<TGame, TAdded> { Game = g });
            }

            for (int i = 0; i < added.Count; i++)
                if (!placed[i]) result.Add(new Placed<TGame, TAdded> { Added = added[i], IsAdded = true });
            return result;
        }

        /// <summary>
        /// What an added task at <paramref name="index"/> of a finished order
        /// should store as "before": the next of the game's tasks after it, or
        /// nothing when none follows. The inverse of <see cref="Arrange"/>, so an
        /// editor that moves a row can write back where it now sits.
        /// </summary>
        public static string BeforeFor<TGame, TAdded>(IList<Placed<TGame, TAdded>> order, int index,
                                                     Func<TGame, string> tokenOf)
        {
            for (int i = index + 1; i < order.Count; i++)
                if (!order[i].IsAdded) return tokenOf(order[i].Game) ?? "";
            return "";
        }

        public static string Clean(string token) => (token ?? "").Trim();

        // ── Putting a quest back on its feet ─────────────────────────────

        /// <summary>A task's state, as the game's journal files it.</summary>
        public enum TaskState { Inactive, Active, Completed, Abandoned, Failed }

        /// <summary>How a task with subtasks finishes, in the game's terms.</summary>
        public enum Completion { InOrder, AnyOrder, AnyOne, ByAction }

        /// <summary>One task as the repair sees it: its id, how it finishes and
        /// the tasks under it, in order.</summary>
        public sealed class Node
        {
            public int Id;
            public Completion Completion;
            public readonly List<Node> Children = new List<Node>();
        }

        public enum RepairKind { None, CompleteTask, ActivateTask, EvaluateQuest }

        public struct RepairStep
        {
            public RepairKind Kind;
            public int TaskId;
        }

        /// <summary>
        /// The next thing to do for a quest the save left part-way through a
        /// task list that has since changed under it - a pack that added tasks
        /// to it is gone, or no longer adds the same ones.
        /// <para/>
        /// Only what the game itself would have done had the list always been
        /// this one, read off its own rules: a task in progress whose subtasks
        /// are all done (any one of them, for "any one") is completed; an
        /// in-order list with nothing in progress starts its next task; and a
        /// quest whose top-level tasks are all done is completed. Deepest
        /// first, and one step at a time, because each step can move the rest -
        /// the caller applies it and asks again.
        /// <para/>
        /// A list with nothing in progress cannot happen in a quest the game
        /// runs by its own rules - starting a task starts its first subtask, and
        /// finishing one starts the next - so starting the next one never
        /// overrides anything the game meant.
        /// </summary>
        public static RepairStep NextRepair(IList<Node> roots, bool questActive, Func<int, TaskState> stateOf)
        {
            if (!questActive) return new RepairStep();

            foreach (var root in roots)
            {
                var step = Visit(root, stateOf);
                if (step.Kind != RepairKind.None) return step;
            }

            if (roots.Count > 0 && roots.All(r => stateOf(r.Id) == TaskState.Completed))
                return new RepairStep { Kind = RepairKind.EvaluateQuest };

            var next = NextInOrder(roots, stateOf);
            return next == null ? new RepairStep() : new RepairStep { Kind = RepairKind.ActivateTask, TaskId = next.Id };
        }

        private static RepairStep Visit(Node node, Func<int, TaskState> stateOf)
        {
            if (stateOf(node.Id) != TaskState.Active || node.Children.Count == 0) return new RepairStep();

            foreach (var child in node.Children)
            {
                var step = Visit(child, stateOf);
                if (step.Kind != RepairKind.None) return step;
            }

            bool done;
            switch (node.Completion)
            {
                case Completion.AnyOne: done = node.Children.Any(c => stateOf(c.Id) == TaskState.Completed); break;
                case Completion.ByAction: done = false; break;
                default: done = node.Children.All(c => stateOf(c.Id) == TaskState.Completed); break;
            }
            if (done) return new RepairStep { Kind = RepairKind.CompleteTask, TaskId = node.Id };

            if (node.Completion == Completion.InOrder)
            {
                var next = NextInOrder(node.Children, stateOf);
                if (next != null) return new RepairStep { Kind = RepairKind.ActivateTask, TaskId = next.Id };
            }
            return new RepairStep();
        }

        // ── A save that meets the tasks a pack adds ──────────────────────

        /// <summary>Where a save stands with one of the game's quests whose
        /// tasks a pack changes.</summary>
        public enum SaveProgress
        {
            /// <summary>Not started: it will run with the changes from the
            /// start.</summary>
            NotStarted,

            /// <summary>In progress, and nothing the pack adds is behind the
            /// player.</summary>
            InProgress,

            /// <summary>In progress, with a task the pack adds sitting where the
            /// player has already gone past - see <see cref="IsBehind"/>.</summary>
            MayGetStuck,

            /// <summary>Completed, failed or abandoned: nothing the pack changes
            /// in it will be seen in this save.</summary>
            Finished,
        }

        /// <summary>
        /// Where a save stands with a quest, given the quest's task tree as the
        /// packs leave it and the tasks they add to it.
        /// <para/>
        /// Taking a task out is never what gets a quest stuck: the task stays
        /// in the tree and is completed the moment it starts, so it only ever
        /// moves the quest on. Adding one can: see <see cref="IsBehind"/>.
        /// </summary>
        public static SaveProgress ProgressOf(TaskState quest, IList<Node> roots, IEnumerable<int> added,
                                              Func<int, TaskState> stateOf)
        {
            if (quest == TaskState.Inactive) return SaveProgress.NotStarted;
            if (quest != TaskState.Active) return SaveProgress.Finished;

            var parents = new Dictionary<int, Node>();
            IndexParents(roots, null, parents, 0);
            foreach (int id in added ?? Enumerable.Empty<int>())
                if (IsBehind(id, roots, parents, stateOf)) return SaveProgress.MayGetStuck;
            return SaveProgress.InProgress;
        }

        private static void IndexParents(IList<Node> list, Node parent, Dictionary<int, Node> into, int depth)
        {
            if (list == null || depth > 64) return;
            foreach (var n in list)
            {
                if (n == null || into.ContainsKey(n.Id)) continue;
                into[n.Id] = parent;
                IndexParents(n.Children, n, into, depth + 1);
            }
        }

        /// <summary>
        /// Whether a task that has not started sits where the save has already
        /// gone past, so the game will not start it - and what is waiting on it
        /// waits for good. Read off Game Creator's own rules:
        /// <list type="bullet">
        ///   <item>In an in-order list (and the quest's top-level tasks are one),
        ///   a task can only be completed once every task before it is, and
        ///   finishing one starts the first that is not done. A task added
        ///   before one that has already started holds that one up.</item>
        ///   <item>A task whose subtasks run together starts all of them as it
        ///   starts, and finishes when all of them have. One added after it
        ///   started is never started, so it never finishes.</item>
        ///   <item>"Any one of them" finishes on any subtask, and a task whose
        ///   subtasks are started by the game's own steps starts none itself:
        ///   neither is held up by a subtask that did not start.</item>
        ///   <item>Under a task that has not started, it is where that task sits
        ///   that counts; under one that has finished, nothing waits on it.</item>
        /// </list>
        /// </summary>
        private static bool IsBehind(int id, IList<Node> roots, Dictionary<int, Node> parents,
                                     Func<int, TaskState> stateOf)
        {
            for (int depth = 0; depth < 64; depth++)
            {
                if (stateOf(id) != TaskState.Inactive) return false;
                Node parent;
                if (!parents.TryGetValue(id, out parent)) return false;   // not in the tree

                IList<Node> siblings = roots;
                if (parent != null)
                {
                    var state = stateOf(parent.Id);
                    if (state == TaskState.Inactive) { id = parent.Id; continue; }
                    if (state != TaskState.Active) return false;
                    if (parent.Completion == Completion.AnyOrder) return true;
                    if (parent.Completion != Completion.InOrder) return false;
                    siblings = parent.Children;
                }

                bool after = false;
                foreach (var s in siblings)
                {
                    if (s == null) continue;
                    if (s.Id == id) { after = true; continue; }
                    if (after && Started(s, stateOf, 0)) return true;
                }
                return false;
            }
            return false;
        }

        private static bool Started(Node node, Func<int, TaskState> stateOf, int depth)
        {
            if (stateOf(node.Id) != TaskState.Inactive) return true;
            if (depth > 64) return false;
            foreach (var child in node.Children)
                if (child != null && Started(child, stateOf, depth + 1)) return true;
            return false;
        }

        /// <summary>The task an in-order list is waiting to start: nothing in
        /// progress, everything before it done, and it not started. Null when
        /// the list is not in that state.</summary>
        private static Node NextInOrder(IList<Node> list, Func<int, TaskState> stateOf)
        {
            // Anything in progress anywhere in the list means the game is still
            // moving it - including a task after one that has not started, which
            // is what a task added before the player's current one looks like.
            if (list.Any(n => stateOf(n.Id) == TaskState.Active)) return null;
            foreach (var n in list)
            {
                var s = stateOf(n.Id);
                if (s == TaskState.Completed) continue;
                return s == TaskState.Inactive ? n : null;
            }
            return null;
        }
    }
}
