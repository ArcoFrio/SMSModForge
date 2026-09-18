using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using GameCreator.Runtime.Quests;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using GcQuest = GameCreator.Runtime.Quests.Quest;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Pack quests at play: read from the manifest, driven by the Quest action,
    /// asked about by the two quest conditions.
    /// <para/>
    /// <b>Everything goes through the game's own Journal.</b> Nothing here keeps
    /// quest state of its own. The journal saves it with the player's game, the
    /// quest screen reads it, and the game's own dialogues change it - so a pack
    /// that kept a second copy would disagree with all three the moment either
    /// side moved.
    /// <para/>
    /// <b>The journal refuses rather than fails.</b> Read off its IL and then
    /// seen in play: a task that is not in progress cannot be completed, failed
    /// or counted, and asking just returns false. An author has no way to see
    /// that happen, so the action checks afterwards and says why in the log.
    /// </summary>
    internal static class QuestRuntime
    {
        private const string Tag = "[SMSModForge.PackPlugin] ";

        // ── Reading the manifest ─────────────────────────────────────────

        /// <summary>The quests one pack declares, ready to register.</summary>
        public static List<QuestSpec> ReadSpecs(PackManifest manifest, ManualLogSource log)
        {
            var specs = new List<QuestSpec>();
            if (!(manifest?.Root["quests"] is JArray quests)) return specs;

            foreach (var token in quests)
            {
                if (!(token is JObject q)) continue;
                string key = (string)q["key"];
                if (string.IsNullOrEmpty(key))
                {
                    log?.LogWarning(Tag + "Quests: a quest in " + manifest.PackId + " has no key - skipped.");
                    continue;
                }

                // An entry naming one of the game's quests EXTENDS it: the
                // quest is already in the journal, and registering a second one
                // under the pack's own id would put a copy beside it. What the
                // pack says about it is driven by the ticker instead.
                if (!string.IsNullOrEmpty((string)q["source"])) continue;

                var spec = new QuestSpec
                {
                    PackId = manifest.PackId,
                    Key = key,
                    Title = (string)q["title"] ?? key,
                    Description = (string)q["description"] ?? "",
                };
                ReadTasks(q["tasks"] as JArray, spec.Tasks);
                specs.Add(spec);
            }
            return specs;
        }

        private static void ReadTasks(JArray tasks, List<TaskSpec> into)
        {
            if (tasks == null) return;
            foreach (var token in tasks)
            {
                if (!(token is JObject t)) continue;
                if (string.IsNullOrEmpty((string)t["key"])) continue;
                into.Add(ReadTask(t));
            }
        }

        /// <summary>One task and its subtasks, as the manifest writes them - for
        /// a quest of the pack's own, and for a task a pack adds to one of the
        /// game's.</summary>
        internal static TaskSpec ReadTask(JObject t)
        {
            double countTo = 0;
            var raw = t["countTo"];
            if (raw != null && raw.Type != JTokenType.Null)
                double.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out countTo);

            var spec = new TaskSpec
            {
                Key = ((string)t["key"] ?? "").Trim(),
                Name = (string)t["name"] ?? "",
                Description = (string)t["description"] ?? "",
                Completion = CompletionOf((string)t["completion"]),
                Counter = countTo > 0,
                CountTo = countTo,
                HideUntilStarted = t["hideUntilStarted"]?.Type == JTokenType.Boolean && (bool)t["hideUntilStarted"],
                HideUntilConditions = t[QuestTreeEdits.HideUntilConditionsKey]?.Type == JTokenType.Boolean
                                      && (bool)t[QuestTreeEdits.HideUntilConditionsKey],
            };
            ReadTasks(t["subtasks"] as JArray, spec.Subtasks);
            return spec;
        }

        internal static TaskType CompletionOf(string word)
        {
            if (V.Is(word, V.AnyOrder)) return TaskType.SubtasksInCombination;
            if (V.Is(word, V.AnyOne)) return TaskType.AnySubtask;
            if (V.Is(word, V.ByAction)) return TaskType.Manual;
            return TaskType.SubtasksInSequence;
        }

        // ── Finding things ───────────────────────────────────────────────

        private static Journal _journal;

        /// <summary>The player's journal, looked up once per scene rather than
        /// once per condition - conditions are asked every frame.</summary>
        internal static Journal Journal
        {
            get
            {
                // Unity's null: a journal from a scene that has been unloaded
                // compares equal to null, which is what re-finds it.
                if (_journal == null) _journal = QuestRegistry.FindJournal();
                return _journal;
            }
        }

        /// <summary>Said once per thing that could not be found, not once per
        /// frame it was asked about.</summary>
        private static readonly HashSet<string> _warned = new HashSet<string>();

        private static void WarnOnce(ManualLogSource log, string key, string message)
        {
            if (_warned.Add(key)) log?.LogWarning(Tag + message);
        }

        private static bool IsVanilla(JObject p)
            => V.Is((string)p[V.SourceParam], V.Vanilla);

        private sealed class Target
        {
            public GcQuest Quest;
            public int TaskId;
            public bool HasTask;
            public string Label;
        }

        /// <summary>
        /// The quest a row names, and the task if it names one. Null, having
        /// said why once, when either cannot be found.
        /// </summary>
        private static Target Resolve(JObject p, string packId, bool wantTask, ManualLogSource log, string who)
        {
            string questName = ((string)p[V.QuestParam] ?? "").Trim();
            string taskName = ((string)p[V.TaskParam] ?? "").Trim();
            bool vanilla = IsVanilla(p);

            if (questName.Length == 0)
            {
                WarnOnce(log, who + "|noquest|" + packId, who + " in " + packId + " names no quest - it does nothing.");
                return null;
            }

            var quest = vanilla ? QuestRegistry.FindVanilla(questName) : QuestRegistry.Find(packId, questName);
            if (quest == null)
            {
                WarnOnce(log, who + "|" + packId + "|" + vanilla + "|" + questName,
                         who + " in " + packId + ": no " + (vanilla ? "game" : "pack") + " quest called '"
                         + questName + "'" + (vanilla ? "." : " - is it on the Quests tab?"));
                return null;
            }

            var target = new Target { Quest = quest, Label = "'" + questName + "'" };
            if (taskName.Length == 0)
            {
                if (wantTask)
                {
                    WarnOnce(log, who + "|notask|" + packId + "|" + questName,
                             who + " in " + packId + " on " + target.Label + " names no task - it does nothing.");
                    return null;
                }
                return target;
            }

            int id;
            if (vanilla)
            {
                // A game task is named by its id: the game's own instructions
                // do the same, and two of its quests reuse a task's text. A task
                // the pack added to that quest is named by the pack's key, and
                // its id comes from the quest the game actually found.
                if (!int.TryParse(taskName, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                    id = QuestIds.AddedTaskId(packId, quest.name, taskName);
            }
            else id = QuestIds.TaskId(packId, questName, taskName);

            if (quest.GetTask(id) == null)
            {
                WarnOnce(log, who + "|notfound|" + packId + "|" + questName + "|" + taskName,
                         who + " in " + packId + ": " + target.Label + " has no task '" + taskName + "'.");
                return null;
            }

            target.TaskId = id;
            target.HasTask = true;
            target.Label = "task '" + taskName + "' of " + target.Label;
            return target;
        }

        // ── The action ───────────────────────────────────────────────────

        /// <summary>
        /// Run one Quest action. <paramref name="value"/> is the value param
        /// already resolved by the caller, so a counter can take a $variable
        /// the same way every other numeric param does.
        /// </summary>
        public static void Run(JObject p, string value, PackContext ctx)
        {
            var log = ctx?.Log;
            string packId = ctx?.PackId ?? "";
            string operation = ((string)p[V.OperationParam] ?? V.Start).Trim();

            var journal = Journal;
            if (journal == null)
            {
                WarnOnce(log, "nojournal", "Quest action: the player's journal (11_PlayerData) is not in this scene.");
                return;
            }

            var target = Resolve(p, packId, V.TakesTask(operation), log, "Quest action");
            if (target == null) return;
            var quest = target.Quest;

            if (V.Is(operation, V.Start)) { journal.ActivateQuest(quest); return; }
            if (V.Is(operation, V.Reset)) { journal.DeactivateQuest(quest); return; }
            if (V.Is(operation, V.Track)) { journal.TrackQuest(quest); return; }
            if (V.Is(operation, V.Untrack)) { journal.UntrackQuest(quest); return; }

            int id = target.TaskId;
            var before = journal.GetTaskState(quest, id);

            if (V.Is(operation, V.CompleteTask) || V.Is(operation, V.FailTask))
            {
                bool complete = V.Is(operation, V.CompleteTask);
                if (complete) journal.CompleteTask(quest, id);
                else journal.FailTask(quest, id);

                var after = journal.GetTaskState(quest, id);
                if (after != (complete ? State.Completed : State.Failed))
                    log?.LogWarning(Tag + "Quest action in " + packId + ": the game did not "
                                    + (complete ? "complete " : "fail ") + target.Label
                                    + WhyNot(before));
                return;
            }

            if (V.Is(operation, V.SetCounter) || V.Is(operation, V.AddToCounter))
            {
                // Adding with nothing written adds one, which is what a step
                // counter almost always wants. Setting with nothing written has
                // no obvious meaning, so that one is refused below.
                string raw = (value ?? "").Trim();
                if (raw.Length == 0 && V.Is(operation, V.AddToCounter)) raw = "1";

                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    log?.LogWarning(Tag + "Quest action in " + packId + ": '" + value + "' is not a number, so "
                                    + target.Label + " was not counted.");
                    return;
                }

                if (before != State.Active)
                {
                    log?.LogWarning(Tag + "Quest action in " + packId + ": " + target.Label
                                    + " was not counted" + WhyNot(before));
                    return;
                }

                double next = V.Is(operation, V.AddToCounter) ? journal.GetTaskValue(quest, id) + number : number;

                // The journal completes the task itself once this reaches the
                // task's target - completing it here as well would be asking
                // twice.
                journal.SetTaskValue(quest, id, next);
                return;
            }

            log?.LogWarning(Tag + "Quest action in " + packId + ": unknown operation '" + operation + "'.");
        }

        /// <summary>The reason worth giving for a refusal, from the state the
        /// task was in when it was asked.</summary>
        private static string WhyNot(State before)
        {
            switch (before)
            {
                case State.Inactive:
                    return " - it has not started yet. A task starts when its quest does (if it comes first), "
                           + "when the task before it is done, or when its parent task starts.";
                case State.Active:
                    // Read off TaskUtils: in progress and still refused only
                    // happens under a parent that runs its subtasks together
                    // (its own subtasks are checked there) or takes any one
                    // (another may already be done). Under an in-order parent
                    // or at the top level an active task always completes.
                    return " - it is in progress, but it sits under a task whose subtasks run together, which "
                           + "needs its own subtasks done first, or under an 'any one' task that already has "
                           + "one completed.";
                default:
                    return " - it is already " + before.ToString().ToLowerInvariant() + ".";
            }
        }

        // ── The conditions ───────────────────────────────────────────────

        /// <summary>Is the quest, or the task a row names, in the state it asks
        /// about. False when either cannot be found.</summary>
        public static bool IsInState(JObject p, string packId, ManualLogSource log)
        {
            var journal = Journal;
            if (journal == null) return false;

            var target = Resolve(p, packId, false, log, "Quest condition");
            if (target == null) return false;

            var state = target.HasTask
                ? journal.GetTaskState(target.Quest, target.TaskId)
                : journal.GetQuestState(target.Quest);

            string wanted = ((string)p[V.StateParam] ?? V.InProgress).Trim();
            if (V.Is(wanted, V.NotStarted)) return state == State.Inactive;
            if (V.Is(wanted, V.InProgress)) return state == State.Active;
            if (V.Is(wanted, V.Completed)) return state == State.Completed;
            if (V.Is(wanted, V.Failed)) return state == State.Failed;

            WarnOnce(log, "state|" + wanted, "Quest condition in " + packId + ": unknown state '" + wanted + "' - not met.");
            return false;
        }

        /// <summary>A task's counter against a number. <paramref name="value"/>
        /// is already resolved by the caller.</summary>
        public static bool CounterMatches(JObject p, string value, string packId, ManualLogSource log)
        {
            var journal = Journal;
            if (journal == null) return false;

            var target = Resolve(p, packId, true, log, "Quest counter condition");
            if (target == null) return false;

            if (!double.TryParse(value ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
            {
                WarnOnce(log, "counter|nan|" + packId + "|" + value,
                         "Quest counter condition in " + packId + ": '" + value + "' is not a number - not met.");
                return false;
            }

            return V.Compare(journal.GetTaskValue(target.Quest, target.TaskId),
                             (string)p[V.ComparisonParam], expected);
        }

        /// <summary>Forget the cached journal and what was already warned
        /// about. Called when a scene loads.</summary>
        public static void Reset()
        {
            _journal = null;
            _warned.Clear();
        }
    }
}
