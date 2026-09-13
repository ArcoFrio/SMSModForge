using System;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The words a pack uses for quests, on disk.
    /// <para/>
    /// Compiled into both projects from one file, like <see cref="QuestIds"/>:
    /// the editor writes these strings and the runtime switches on them, and two
    /// copies of a list of strings is how "complete task" in one becomes
    /// "Complete Task" in the other and an action quietly does nothing.
    /// <para/>
    /// Stored as the plain phrases an author reads, the way a variable
    /// comparison stores "greater than", so a manifest opened in a text editor
    /// still says what it does.
    /// </summary>
    public static class QuestVocabulary
    {
        // ── Types ────────────────────────────────────────────────────────

        /// <summary>The one action that drives a quest. What it does is its
        /// <see cref="OperationParam"/>.</summary>
        public const string ActionType = "Quest";

        /// <summary>Is a quest, or one of its tasks, in a given state.</summary>
        public const string StateCondition = "QuestState";

        /// <summary>Compare a task's counter against a number.</summary>
        public const string CounterCondition = "QuestCounter";

        // ── Params ───────────────────────────────────────────────────────

        /// <summary><see cref="Vanilla"/> for one of the game's quests; absent
        /// for one of the pack's own. The same key, and the same value, a
        /// variable row uses for the same question.</summary>
        public const string SourceParam = "source";

        /// <summary>A pack quest's key, or a game quest's asset name.</summary>
        public const string QuestParam = "quest";

        /// <summary>A pack task's key, or a game task's numeric id. Empty on a
        /// condition means the quest itself.</summary>
        public const string TaskParam = "task";

        public const string OperationParam = "operation";
        public const string StateParam = "state";
        public const string ComparisonParam = "comparison";
        public const string ValueParam = "value";

        public const string Vanilla = "vanilla";

        // ── What the action does ─────────────────────────────────────────

        public const string Start = "start quest";
        public const string Reset = "reset quest";
        public const string CompleteTask = "complete task";
        public const string FailTask = "fail task";
        public const string SetCounter = "set counter";
        public const string AddToCounter = "add to counter";
        public const string Track = "track quest";
        public const string Untrack = "untrack quest";

        /// <summary>In the order the picker offers them: the common path
        /// through a quest first.</summary>
        public static readonly string[] Operations =
        {
            Start, CompleteTask, SetCounter, AddToCounter, FailTask, Track, Untrack, Reset,
        };

        /// <summary>Whether an operation acts on one task rather than on the
        /// whole quest.</summary>
        public static bool TakesTask(string operation)
            => Is(operation, CompleteTask) || Is(operation, FailTask)
               || Is(operation, SetCounter) || Is(operation, AddToCounter);

        /// <summary>Whether an operation needs a number.</summary>
        public static bool TakesValue(string operation)
            => Is(operation, SetCounter) || Is(operation, AddToCounter);

        // ── States ───────────────────────────────────────────────────────

        public const string NotStarted = "not started";
        public const string InProgress = "in progress";
        public const string Completed = "completed";
        public const string Failed = "failed";

        public static readonly string[] States = { NotStarted, InProgress, Completed, Failed };

        // ── How a task with subtasks completes ───────────────────────────

        /// <summary>Its subtasks, one after another. The game's default, and
        /// how 191 of its 230 tasks work.</summary>
        public const string InOrder = "in order";

        /// <summary>All of its subtasks, in any order.</summary>
        public const string AnyOrder = "any order";

        /// <summary>Any one of its subtasks.</summary>
        public const string AnyOne = "any one";

        /// <summary>Only when an action completes it. Its subtasks are never
        /// started, so they read as notes under it rather than steps.</summary>
        public const string ByAction = "by action";

        public static readonly string[] Completions = { InOrder, AnyOrder, AnyOne, ByAction };

        // ── Where a counter's number comes from ──────────────────────────

        /// <summary>A task's <c>countFrom</c> when its count follows a number
        /// variable rather than being set by Quest actions. Absent means the
        /// actions set it.</summary>
        public const string CountFromVariable = "variable";

        // ── Comparisons, the same words a variable uses ──────────────────

        public const string EqualTo = "equals";
        public const string GreaterThan = "greater than";
        public const string GreaterOrEqual = "greater or equal";
        public const string LessThan = "less than";
        public const string LessOrEqual = "less or equal";

        public static readonly string[] Comparisons = { EqualTo, GreaterThan, GreaterOrEqual, LessThan, LessOrEqual };

        /// <summary>
        /// A counter against a number, by one of <see cref="Comparisons"/>.
        /// False for a comparison this build does not know: a gate that cannot
        /// be read is not one that should quietly pass.
        /// </summary>
        public static bool Compare(double actual, string comparison, double expected)
        {
            switch ((comparison ?? EqualTo).Trim().ToLowerInvariant())
            {
                // Counters are whole steps written as doubles; a tolerance keeps
                // 3 from failing to equal 2.9999999 after a few additions.
                case EqualTo:        return Math.Abs(actual - expected) < 1e-6;
                case GreaterThan:    return actual > expected;
                case GreaterOrEqual: return actual >= expected - 1e-6;
                case LessThan:       return actual < expected;
                case LessOrEqual:    return actual <= expected + 1e-6;
                default:             return false;
            }
        }

        /// <summary>Case-insensitive, trimmed: a hand-edited manifest saying
        /// "Complete Task" means what it says.</summary>
        public static bool Is(string value, string word)
            => string.Equals((value ?? "").Trim(), word, StringComparison.OrdinalIgnoreCase);
    }
}
