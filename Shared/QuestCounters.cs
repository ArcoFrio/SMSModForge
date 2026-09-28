using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Whether a pack has a task that counts somewhere the journal would not
    /// draw the number.
    /// <para/>
    /// The journal builds its rows from two prefabs. Both have a complete
    /// counter under them, wired to the same fields — but only
    /// <c>Journal_Task</c> names that counter in
    /// <c>m_ActiveElements.m_ActiveIfIsCounter</c>, so only a top-level task
    /// ever has its counter switched on. A counting SUBTASK draws as a plain
    /// line while its number ticks up unseen.
    /// <para/>
    /// The runtime puts that right by pointing the subtask prefab's field at
    /// the counter already under it. It only does so for somebody who has a
    /// pack that needs it, because the prefab is a shared asset and the change
    /// reaches the game's own quests too — which is what this answers.
    /// <para/>
    /// Here rather than in the plugin so it can be checked. The plugin loads
    /// into a game this repository cannot start, and getting this wrong is
    /// silent in both directions: too eager and it changes a game nobody asked
    /// it to, too shy and the count somebody is waiting for never appears.
    /// </summary>
    public static class QuestCounters
    {
        /// <summary>
        /// Whether any quest in <paramref name="manifestRoot"/> counts on a
        /// subtask.
        /// <para/>
        /// Both shapes a pack can produce. A quest of the pack's own nests its
        /// subtasks under <c>subtasks</c>, so anything below the top level
        /// counts. A task added to one of the GAME's quests is a subtask when
        /// it names a parent to go <c>under</c>, whatever its nesting in the
        /// file — the editor writes those flat.
        /// </summary>
        public static bool OnASubtask(JObject manifestRoot)
        {
            if (!(manifestRoot?["quests"] is JArray quests)) return false;

            foreach (var token in quests)
            {
                if (!(token is JObject quest)) continue;
                if (Deep(quest["tasks"] as JArray, 0)) return true;

                if (quest[QuestTreeEdits.AddedTasksKey] is JArray added)
                    foreach (var one in added)
                        if (one is JObject task && Counts(task)
                            && !string.IsNullOrEmpty((string)task[QuestTreeEdits.UnderKey]))
                            return true;
            }
            return false;
        }

        /// <summary>A counting task below the top level, at any depth.</summary>
        private static bool Deep(JArray tasks, int depth)
        {
            if (tasks == null) return false;
            foreach (var token in tasks)
            {
                if (!(token is JObject task)) continue;
                if (depth > 0 && Counts(task)) return true;
                if (Deep(task["subtasks"] as JArray, depth + 1)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether this task counts at all.
        /// <para/>
        /// Zero is not counting: the editor writes <c>countTo</c> on every task
        /// it has ever been set on, and a task whose counter was turned back
        /// off keeps the field at zero rather than losing it.
        /// </summary>
        private static bool Counts(JObject task)
        {
            var countTo = task["countTo"];
            if (countTo == null || countTo.Type == JTokenType.Null) return false;
            if (countTo.Type != JTokenType.Integer && countTo.Type != JTokenType.Float) return false;
            return (double)countTo > 0;
        }
    }
}
