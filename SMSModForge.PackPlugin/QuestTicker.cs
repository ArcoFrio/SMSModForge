using System.Collections.Generic;
using BepInEx.Logging;
using GameCreator.Runtime.Quests;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// A pack's quests moving on their own: a quest starting when its start
    /// conditions pass, a task completing when its completion conditions do,
    /// and a task's completion actions running when it finishes.
    /// <para/>
    /// Ticked every frame beside the pack's integration rules, and gated the
    /// same way: not until the game's own variables have settled after a load.
    /// Completing a task is permanent in the player's save, so a condition that
    /// passed for a frame on uninitialised values would be wrong for good -
    /// which is the exact failure the gate exists for.
    /// <para/>
    /// <b>Only a task with no subtasks has its conditions and actions
    /// checked.</b> A task with subtasks finishes through them, and the editor
    /// only offers conditions and actions where that is not so. Anything a
    /// hand-edited manifest puts on such a task is ignored, and Validate says
    /// so. Its counter is another matter: any task can count.
    /// <para/>
    /// <b>Completion actions follow the task finishing, whatever finished it</b>:
    /// its own conditions, a Quest action somewhere else, or its counter
    /// reaching its target. They are fired from the task's state moving to
    /// completed, watched frame to frame, so there is one trigger for all three.
    /// The first sighting of a task after a load only records where it is - a
    /// task the save already had completed does not run its actions again.
    /// <para/>
    /// <b>A counter can follow a number variable</b>, the pack's or the game's.
    /// While the task is in progress its count is set to the variable whenever
    /// the two differ, through the journal's own SetTaskValue - which is what
    /// completes the task when the count reaches its target, exactly as when an
    /// action sets it. The game has a counter of its own that reads a variable,
    /// but it reads only the game's variables and it is wired up when a save is
    /// read, before a pack's quests exist - so it could serve neither side.
    /// </summary>
    internal sealed class QuestTicker
    {
        private const string Tag = "[SMSModForge.PackPlugin] Quests: ";

        private sealed class Start
        {
            public string QuestKey;
            public JArray Conditions;
        }

        private sealed class Watched
        {
            public string QuestKey;
            public string TaskKey;
            public int TaskId;
            public JArray Conditions;
            public JArray Actions;
            public string CountVariable;
            public bool CountVanilla;
            public bool Seen;
            public State Last;
        }

        private readonly List<Start> _starts = new List<Start>();
        private readonly List<Watched> _tasks = new List<Watched>();

        public bool IsEmpty => _starts.Count == 0 && _tasks.Count == 0;

        /// <summary>Everything in one pack's quests that the ticker has to watch.</summary>
        public static QuestTicker Build(PackManifest manifest)
        {
            var ticker = new QuestTicker();
            if (!(manifest?.Root["quests"] is JArray quests)) return ticker;

            foreach (var token in quests)
            {
                if (!(token is JObject q)) continue;
                string key = (string)q["key"];
                if (string.IsNullOrEmpty(key)) continue;

                if (q["startConditions"] is JArray start && start.Count > 0)
                    ticker._starts.Add(new Start { QuestKey = key, Conditions = start });

                ticker.Collect(manifest.PackId, key, q["tasks"] as JArray);
            }
            return ticker;
        }

        private void Collect(string packId, string questKey, JArray tasks)
        {
            if (tasks == null) return;
            foreach (var token in tasks)
            {
                if (!(token is JObject t)) continue;
                string key = (string)t["key"];
                if (string.IsNullOrEmpty(key)) continue;

                // Any task can count, subtasks or not, so a counter that follows
                // a variable is read before deciding whether the task's own
                // conditions and actions apply.
                string countVariable = null;
                double.TryParse((string)t["countTo"] ?? "", System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var countTo);
                if (countTo > 0 && QuestVocabulary.Is((string)t["countFrom"], QuestVocabulary.CountFromVariable))
                {
                    string name = ((string)t["countVariable"] ?? "").Trim();
                    if (name.Length > 0) countVariable = name;
                }

                bool hasSubtasks = t["subtasks"] is JArray subtasks && subtasks.Count > 0;
                var conditions = hasSubtasks ? null : t["conditions"] as JArray;
                var actions = hasSubtasks ? null : t["actions"] as JArray;

                if ((conditions != null && conditions.Count > 0) || (actions != null && actions.Count > 0)
                    || countVariable != null)
                {
                    _tasks.Add(new Watched
                    {
                        QuestKey = questKey,
                        TaskKey = key,
                        TaskId = QuestIds.TaskId(packId, questKey, key),
                        Conditions = conditions != null && conditions.Count > 0 ? conditions : null,
                        Actions = actions != null && actions.Count > 0 ? actions : null,
                        CountVariable = countVariable,
                        CountVanilla = QuestVocabulary.Is((string)t["countSource"], QuestVocabulary.Vanilla),
                    });
                }

                if (hasSubtasks) Collect(packId, questKey, (JArray)t["subtasks"]);
            }
        }

        public void Tick(PackContext ctx, ManualLogSource log)
        {
            if (IsEmpty) return;
            var journal = QuestRuntime.Journal;
            if (journal == null) return;

            foreach (var s in _starts)
            {
                var quest = QuestRegistry.Find(ctx.PackId, s.QuestKey);
                if (quest == null || !journal.IsQuestInactive(quest)) continue;
                if (!ConditionEvaluator.All(s.Conditions, ctx.Vars, log, ctx.PackId)) continue;

                journal.ActivateQuest(quest);
                log?.LogInfo(Tag + "'" + s.QuestKey + "' in " + ctx.PackId + " started - its start conditions passed.");
            }

            foreach (var w in _tasks)
            {
                var quest = QuestRegistry.Find(ctx.PackId, w.QuestKey);
                if (quest == null) continue;

                var state = journal.GetTaskState(quest, w.TaskId);

                if (state == State.Active && w.CountVariable != null
                    && TryReadNumber(w, ctx, out double count)
                    && System.Math.Abs(count - journal.GetTaskValue(quest, w.TaskId)) > 1e-9)
                {
                    // The journal completes the task itself once this reaches the
                    // target, the same as when an action sets it.
                    journal.SetTaskValue(quest, w.TaskId, count);
                    state = journal.GetTaskState(quest, w.TaskId);
                }

                if (state == State.Active && w.Conditions != null
                    && ConditionEvaluator.All(w.Conditions, ctx.Vars, log, ctx.PackId))
                {
                    journal.CompleteTask(quest, w.TaskId);
                    state = journal.GetTaskState(quest, w.TaskId);
                    if (state == State.Completed)
                        log?.LogInfo(Tag + "task '" + w.TaskKey + "' of '" + w.QuestKey + "' in " + ctx.PackId
                                     + " completed - its conditions passed.");
                }

                if (w.Seen && w.Last != State.Completed && state == State.Completed && w.Actions != null)
                {
                    try { ActionRuntime.ExecuteList(w.Actions, ctx); }
                    catch (System.Exception ex)
                    {
                        log?.LogError(Tag + "the completion actions of task '" + w.TaskKey + "' of '"
                                      + w.QuestKey + "' in " + ctx.PackId + " threw: " + ex.Message);
                    }
                }

                w.Last = state;
                w.Seen = true;
            }
        }

        /// <summary>
        /// The variable a counter follows, as a number. False - and the count
        /// left where it is - when there is no number to read: a game variable
        /// that is not there, or a pack variable holding text.
        /// </summary>
        private static bool TryReadNumber(Watched w, PackContext ctx, out double value)
        {
            value = 0;
            if (w.CountVanilla)
            {
                var raw = GameVariableBridge.Get(w.CountVariable);
                if (raw is double d) { value = d; return true; }
                if (raw is float f) { value = f; return true; }
                if (raw is int i) { value = i; return true; }
                return false;
            }

            if (ctx.Vars == null) return false;
            return double.TryParse(ctx.Vars.GetString(w.CountVariable), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
