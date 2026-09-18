using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameCreator.Runtime.Quests;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using GcQuest = GameCreator.Runtime.Quests.Quest;

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
    /// <para/>
    /// <b>The game's own quests</b>, when a pack extends one, get the same: the
    /// tasks the pack added to them (<see cref="VanillaQuestEdits"/>) are watched
    /// like a pack quest's, and the game's own tasks can be hidden, or taken out
    /// - hidden, and completed the moment they start.
    /// </summary>
    internal sealed class QuestTicker
    {
        private const string Tag = "[SMSModForge.PackPlugin] Quests: ";

        /// <summary>
        /// A task, as the manifest names it: one of the game's by id, or one the
        /// pack added to a game quest by key. The added task's id depends on the
        /// name the game gives the quest it found, so it is worked out once the
        /// quest is.
        /// </summary>
        private struct TaskRef
        {
            public int Id;
            public string AddedKey;

            public int IdIn(string packId, GcQuest quest)
                => AddedKey == null ? Id : QuestIds.AddedTaskId(packId, quest.name, AddedKey);
        }

        /// <summary>
        /// A task kept out of the journal until conditions of the pack's pass.
        /// Once they have, it stays shown for that save - remembered in the
        /// pack's save data under <see cref="Key"/> - unless it follows them
        /// live.
        /// </summary>
        private sealed class Gate
        {
            public TaskRef Task;
            public JArray Conditions;
            public bool Live;
            public string Key;
        }

        /// <summary>
        /// A quest's own start and reset conditions. Either can be missing.
        /// While the reset conditions pass, the start conditions wait - so a
        /// quest whose two lists both pass does not start and reset in turn
        /// every frame.
        /// </summary>
        private sealed class Start
        {
            public string QuestKey;

            /// <summary>One of the game's own quests, which this pack extends,
            /// rather than one of the pack's.</summary>
            public bool Vanilla;
            public JArray Conditions;
            public JArray Resets;
        }

        private sealed class Watched
        {
            public string QuestKey;
            public bool Vanilla;
            public string TaskKey;
            public TaskRef Task;
            public JArray Conditions;
            public JArray Actions;
            public string CountVariable;
            public bool CountVanilla;
            public bool Seen;
            public State Last;
        }

        /// <summary>
        /// What the journal shows of one quest that follows the player: the
        /// description its done tasks give it, the tasks it keeps out of sight,
        /// and - on one of the game's quests - the tasks the pack took out.
        /// </summary>
        private sealed class Shown
        {
            public string QuestKey;
            public bool Vanilla;

            /// <summary>What the quest's description is before any task changes
            /// it: the pack quest's own, or an extension's override. Empty on an
            /// extension means the game's own text, whatever that is.</summary>
            public string Description;

            public readonly List<KeyValuePair<TaskRef, string>> Descriptions = new List<KeyValuePair<TaskRef, string>>();
            public readonly List<TaskRef> HiddenUntilStarted = new List<TaskRef>();
            public readonly List<int> AlwaysHidden = new List<int>();
            public readonly List<int> Removed = new List<int>();
            public readonly List<Gate> Gates = new List<Gate>();

            public bool IsEmpty => Descriptions.Count == 0 && HiddenUntilStarted.Count == 0
                                   && AlwaysHidden.Count == 0 && Removed.Count == 0 && Gates.Count == 0
                                   && !(Vanilla && Description.Length > 0);

            // ── Worked out for the quest object found ────────────────────

            /// <summary>What was last written, and to which quest object. A quest
            /// registered again is a new object carrying its first description,
            /// so what was written to the old one says nothing about it.</summary>
            public object AppliedTo;
            public string AppliedDescription;
            public readonly Dictionary<int, bool> AppliedHidden = new Dictionary<int, bool>();

            /// <summary>Task id and the description it gives the quest, in the
            /// order the quest lists its tasks - the last done one wins.</summary>
            public readonly List<KeyValuePair<int, string>> ByOrder = new List<KeyValuePair<int, string>>();
            public readonly List<int> UntilStarted = new List<int>();
            public readonly HashSet<int> NeverShown = new HashSet<int>();
            public readonly List<KeyValuePair<int, Gate>> ByGate = new List<KeyValuePair<int, Gate>>();

            /// <summary>This frame's answer per task, reused between frames.</summary>
            public readonly Dictionary<int, bool> Hide = new Dictionary<int, bool>();

            /// <summary>The tasks taken out, and everything under them, deepest
            /// first - so a task's subtasks are moved past before it is. The flag
            /// marks one to leave alone.</summary>
            public readonly List<KeyValuePair<int, bool>> Skip = new List<KeyValuePair<int, bool>>();
            public readonly HashSet<int> SkipSaid = new HashSet<int>();
        }

        private readonly List<Start> _starts = new List<Start>();
        private readonly List<Watched> _tasks = new List<Watched>();
        private readonly List<Shown> _shown = new List<Shown>();

        public bool IsEmpty => _starts.Count == 0 && _tasks.Count == 0 && _shown.Count == 0;

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

                // An entry naming one of the game's quests is an EXTENSION of
                // it: nothing of it is registered, and it is addressed by the
                // game's own name rather than by the pack's key.
                string source = ((string)q["source"] ?? "").Trim();
                bool vanilla = source.Length > 0;
                string name = vanilla ? source : key;

                var start = q["startConditions"] as JArray;
                var resets = q[GameConditionEdits.ResetConditionsKey] as JArray;
                if ((start != null && start.Count > 0) || (resets != null && resets.Count > 0))
                    ticker._starts.Add(new Start
                    {
                        QuestKey = name,
                        Vanilla = vanilla,
                        Conditions = start != null && start.Count > 0 ? start : null,
                        Resets = resets != null && resets.Count > 0 ? resets : null,
                    });

                var shown = new Shown
                {
                    QuestKey = name,
                    Vanilla = vanilla,
                    Description = (string)q["description"] ?? "",
                };

                if (vanilla)
                {
                    ticker.CollectHooks(name, q["vanillaTasks"] as JArray, shown);
                    ticker.CollectAdded(name, q[QuestTreeEdits.AddedTasksKey] as JArray, shown);
                }
                else ticker.CollectOwn(manifest.PackId, key, q["tasks"] as JArray, shown);

                if (!shown.IsEmpty) ticker._shown.Add(shown);
            }
            return ticker;
        }

        /// <summary>A pack quest's tasks, depth first.</summary>
        private void CollectOwn(string packId, string questKey, JArray tasks, Shown shown)
        {
            if (tasks == null) return;
            foreach (var token in tasks)
            {
                if (!(token is JObject t)) continue;
                string key = (string)t["key"];
                if (string.IsNullOrEmpty(key)) continue;

                var task = new TaskRef { Id = QuestIds.TaskId(packId, questKey, key) };
                Watch(t, questKey, false, key, task);
                CollectShown(t, task, shown, "quest:" + questKey + "/" + key);
                CollectOwn(packId, questKey, t["subtasks"] as JArray, shown);
            }
        }

        /// <summary>
        /// The tasks a pack adds to one of the game's quests: each added task,
        /// and the direct subtasks of an added top-level one - the same two
        /// levels <see cref="VanillaQuestEdits"/> builds.
        /// </summary>
        private void CollectAdded(string questName, JArray added, Shown shown)
        {
            if (added == null) return;
            foreach (var token in added)
            {
                if (!(token is JObject t)) continue;
                string key = ((string)t["key"] ?? "").Trim();
                if (key.Length == 0) continue;

                var task = new TaskRef { AddedKey = key };
                Watch(t, questName, true, key, task);
                CollectShown(t, task, shown, "game:" + questName + "/+" + key);

                if (QuestTreeEdits.Clean((string)t[QuestTreeEdits.UnderKey]).Length > 0) continue;
                if (!(t["subtasks"] is JArray subs)) continue;
                foreach (var subToken in subs)
                {
                    if (!(subToken is JObject sub)) continue;
                    string subKey = ((string)sub["key"] ?? "").Trim();
                    if (subKey.Length == 0) continue;
                    var subTask = new TaskRef { AddedKey = subKey };
                    Watch(sub, questName, true, subKey, subTask);
                    CollectShown(sub, subTask, shown, "game:" + questName + "/+" + subKey);
                }
            }
        }

        /// <summary>What one task of the pack's changes about the journal.</summary>
        private static void CollectShown(JObject t, TaskRef task, Shown into, string gateKey)
        {
            string text = (string)t["questDescription"];
            if (!string.IsNullOrEmpty(text)) into.Descriptions.Add(new KeyValuePair<TaskRef, string>(task, text));
            if (t["hideUntilStarted"]?.Type == JTokenType.Boolean && (bool)t["hideUntilStarted"])
                into.HiddenUntilStarted.Add(task);
            if (IsTrue(t[QuestTreeEdits.HideUntilConditionsKey]))
                into.Gates.Add(GateFor(t, task, gateKey));
        }

        private static bool IsTrue(JToken token) => token != null && token.Type == JTokenType.Boolean && (bool)token;

        private static Gate GateFor(JObject owner, TaskRef task, string key)
            => new Gate
            {
                Task = task,
                Conditions = owner[QuestTreeEdits.ShowConditionsKey] as JArray ?? new JArray(),
                Live = IsTrue(owner[QuestTreeEdits.ShowConditionsLiveKey]),
                Key = key,
            };

        /// <summary>
        /// What a pack says about the game's own tasks in a quest it extends:
        /// the description each one gives the quest as it is done, the actions
        /// to run when it is, how the journal shows it, and whether it stays in
        /// the quest.
        /// </summary>
        private void CollectHooks(string questName, JArray hooks, Shown into)
        {
            if (hooks == null) return;
            foreach (var token in hooks)
            {
                if (!(token is JObject h)) continue;

                string taskText = ((string)h["task"] ?? "").Trim();
                if (!int.TryParse(taskText, System.Globalization.NumberStyles.Integer,
                                  System.Globalization.CultureInfo.InvariantCulture, out int id))
                    continue;
                var task = new TaskRef { Id = id };

                string text = (string)h["questDescription"];
                if (!string.IsNullOrEmpty(text))
                    into.Descriptions.Add(new KeyValuePair<TaskRef, string>(task, text));

                string visibility = (string)h[QuestTreeEdits.VisibilityKey];
                if (QuestVocabulary.Is(visibility, QuestTreeEdits.Hidden)) into.AlwaysHidden.Add(id);
                else if (QuestVocabulary.Is(visibility, QuestTreeEdits.HiddenUntilStarted)) into.HiddenUntilStarted.Add(task);
                else if (QuestVocabulary.Is(visibility, QuestTreeEdits.HiddenUntilConditions))
                    into.Gates.Add(GateFor(h, task, "game:" + questName + "/" + taskText));

                if (h[QuestTreeEdits.RemovedKey]?.Type == JTokenType.Boolean && (bool)h[QuestTreeEdits.RemovedKey])
                    into.Removed.Add(id);

                var actions = h["actions"] as JArray;
                if (actions != null && actions.Count > 0)
                    _tasks.Add(new Watched
                    {
                        QuestKey = questName,
                        Vanilla = true,
                        TaskKey = taskText,
                        Task = task,
                        Actions = actions,
                    });
            }
        }

        /// <summary>A task's own conditions, actions and variable counter, when
        /// it has any.</summary>
        private void Watch(JObject t, string questKey, bool vanilla, string key, TaskRef task)
        {
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
                    Vanilla = vanilla,
                    TaskKey = key,
                    Task = task,
                    Conditions = conditions != null && conditions.Count > 0 ? conditions : null,
                    Actions = actions != null && actions.Count > 0 ? actions : null,
                    CountVariable = countVariable,
                    CountVanilla = QuestVocabulary.Is((string)t["countSource"], QuestVocabulary.Vanilla),
                });
            }
        }

        /// <summary>Quest names already complained about, so a miss is one line
        /// in the log rather than one a frame.</summary>
        private readonly HashSet<string> _missing = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>
        /// The quest a row is about: one of the pack's own, or one of the
        /// game's that the pack extends.
        /// <para/>
        /// A name the game does not have is said once. Nothing else would say
        /// it: an extension registers nothing, so a misspelled quest simply
        /// never changes anything, which from the outside looks like the
        /// feature not working.
        /// </summary>
        private GcQuest Find(PackContext ctx, bool vanilla, string name, ManualLogSource log)
        {
            var quest = vanilla ? QuestRegistry.FindVanilla(name) : QuestRegistry.Find(ctx.PackId, name);
            if (quest == null && vanilla && _missing.Add(name))
                log?.LogWarning(Tag + ctx.PackId + " extends '" + name + "', which is not one of the game's quests. "
                                + "It is named as the game names it, which is not always the journal's title.");
            return quest;
        }

        /// <summary>Whether this pack's Resets when list for a quest passes
        /// right now - what a start has to wait for, here and at the game's own
        /// places a pack has taken over (<see cref="QuestPlaceRules"/>).</summary>
        public bool ResetsPass(PackContext ctx, string quest, ManualLogSource log)
        {
            foreach (var s in _starts)
            {
                if (s.Resets == null || !string.Equals(s.QuestKey, quest, System.StringComparison.Ordinal)) continue;
                if (ConditionEvaluator.All(s.Resets, ctx.Vars, log, ctx.PackId)) return true;
            }
            return false;
        }

        public void Tick(PackContext ctx, ManualLogSource log)
        {
            if (IsEmpty) return;
            var journal = QuestRuntime.Journal;
            if (journal == null) return;

            foreach (var s in _starts)
            {
                var quest = Find(ctx, s.Vanilla, s.QuestKey, log);
                if (quest == null) continue;
                bool resetsPass = s.Resets != null && ConditionEvaluator.All(s.Resets, ctx.Vars, log, ctx.PackId);

                if (!journal.IsQuestInactive(quest))
                {
                    // In progress, completed or failed: back to not started,
                    // tasks and counts with it, the way the game's own resets
                    // do it.
                    if (!resetsPass) continue;
                    journal.DeactivateQuest(quest);
                    log?.LogInfo(Tag + "'" + s.QuestKey + "' in " + ctx.PackId
                                 + " put back to not started - its reset conditions passed.");
                    continue;
                }

                if (s.Conditions == null || resetsPass) continue;
                if (!ConditionEvaluator.All(s.Conditions, ctx.Vars, log, ctx.PackId)) continue;

                journal.ActivateQuest(quest);
                log?.LogInfo(Tag + "'" + s.QuestKey + "' in " + ctx.PackId + " started - its start conditions passed.");
            }

            foreach (var w in _tasks)
            {
                var quest = Find(ctx, w.Vanilla, w.QuestKey, log);
                if (quest == null) continue;
                int id = w.Task.IdIn(ctx.PackId, quest);

                var state = journal.GetTaskState(quest, id);

                if (state == State.Active && w.CountVariable != null
                    && TryReadNumber(w, ctx, out double count)
                    && System.Math.Abs(count - journal.GetTaskValue(quest, id)) > 1e-9)
                {
                    // The journal completes the task itself once this reaches the
                    // target, the same as when an action sets it.
                    journal.SetTaskValue(quest, id, count);
                    state = journal.GetTaskState(quest, id);
                }

                if (state == State.Active && w.Conditions != null
                    && ConditionEvaluator.All(w.Conditions, ctx.Vars, log, ctx.PackId))
                {
                    journal.CompleteTask(quest, id);
                    state = journal.GetTaskState(quest, id);
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

            // After the tasks above have moved, so a task completed this frame
            // changes the description this frame too.
            foreach (var s in _shown)
            {
                var quest = Find(ctx, s.Vanilla, s.QuestKey, log);
                if (quest == null) continue;

                if (!ReferenceEquals(s.AppliedTo, quest))
                {
                    s.AppliedTo = quest;
                    s.AppliedDescription = null;
                    s.AppliedHidden.Clear();
                    Resolve(s, ctx.PackId, quest);
                }

                // Tasks taken out move on first, so the description and what is
                // shown below already see where that leaves the quest.
                foreach (var skip in s.Skip)
                {
                    if (skip.Value || journal.GetTaskState(quest, skip.Key) != State.Active) continue;
                    journal.CompleteTask(quest, skip.Key);
                    if (s.SkipSaid.Add(skip.Key))
                        log?.LogInfo(Tag + ctx.PackId + " took task " + skip.Key + " out of '" + s.QuestKey + "': "
                                     + (journal.GetTaskState(quest, skip.Key) == State.Completed
                                         ? "it started, so the quest moved past it."
                                         : "it started, but the game would not let the quest move past it yet."));
                }

                {
                    string text = s.Description;
                    foreach (var d in s.ByOrder)
                        if (journal.GetTaskState(quest, d.Key) == State.Completed) text = d.Value;

                    if (text != s.AppliedDescription)
                    {
                        // Nothing of the pack's applies any more: one of the
                        // game's quests goes back to describing itself, rather
                        // than to a copy of what it said when the pack loaded.
                        if (s.Vanilla && text.Length == 0) VanillaQuestEdits.RestoreDescription(quest);
                        else if (s.Vanilla) VanillaQuestEdits.SetDescription(quest, text);
                        else QuestRegistry.SetDescription(quest, text);
                        s.AppliedDescription = text;
                    }
                }

                // Every reason a task has to be out of sight, in one answer per
                // task: any one of them hides it.
                s.Hide.Clear();
                foreach (int id in s.NeverShown) s.Hide[id] = true;
                foreach (int id in s.UntilStarted)
                    Hide(s, id, journal.GetTaskState(quest, id) == State.Inactive);
                foreach (var gate in s.ByGate)
                    Hide(s, gate.Key, !GateOpen(gate.Value, ctx, log));
                foreach (var pair in s.Hide) ApplyHidden(s, quest, pair.Key, pair.Value);
            }
        }

        private static void Hide(Shown s, int id, bool hide)
        {
            bool already;
            s.Hide[id] = (s.Hide.TryGetValue(id, out already) && already) || hide;
        }

        /// <summary>
        /// Whether a task gated on conditions may be shown. Once its conditions
        /// have passed, it stays shown for that save - unless it follows them
        /// live, in which case they are asked every frame.
        /// </summary>
        private static bool GateOpen(Gate gate, PackContext ctx, ManualLogSource log)
        {
            var store = gate.Live ? null : ctx.Vars;
            if (store != null && store.IsTaskRevealed(gate.Key)) return true;
            if (!ConditionEvaluator.All(gate.Conditions, ctx.Vars, log, ctx.PackId)) return false;
            if (store != null)
            {
                store.RevealTask(gate.Key);
                log?.LogInfo(Tag + ctx.PackId + ": '" + gate.Key + "' is shown from now on - its show conditions passed.");
            }
            return true;
        }

        private static void ApplyHidden(Shown s, GcQuest quest, int id, bool hide)
        {
            if (s.AppliedHidden.TryGetValue(id, out bool was) && was == hide) return;
            if (s.Vanilla) VanillaQuestEdits.SetHidden(quest, id, hide);
            else
            {
                var task = quest.GetTask(id);
                if (task == null) return;
                QuestRegistry.SetHidden(task, hide);
            }
            s.AppliedHidden[id] = hide;
        }

        /// <summary>
        /// Work out, for the quest object the game has, which ids everything in
        /// <paramref name="s"/> means and in what order the quest lists them.
        /// Read off the live tree, so a task a pack added sits where it was put.
        /// </summary>
        private static void Resolve(Shown s, string packId, GcQuest quest)
        {
            var order = new Dictionary<int, int>();
            var parentOf = new Dictionary<int, int>();
            var postOrder = new List<int>();
            void Walk(IEnumerable<int> ids, int parent)
            {
                foreach (int id in ids)
                {
                    if (order.ContainsKey(id)) continue;
                    order[id] = order.Count;
                    parentOf[id] = parent;
                    Walk(quest.Tasks.Children(id), id);
                    postOrder.Add(id);
                }
            }
            Walk(quest.Tasks.RootIds, QuestIds.NoNode);

            s.ByOrder.Clear();
            s.ByOrder.AddRange(s.Descriptions
                .Select(d => new KeyValuePair<int, string>(d.Key.IdIn(packId, quest), d.Value))
                .Where(d => order.ContainsKey(d.Key))
                .OrderBy(d => order[d.Key]));

            s.UntilStarted.Clear();
            s.UntilStarted.AddRange(s.HiddenUntilStarted.Select(t => t.IdIn(packId, quest)).Where(order.ContainsKey));

            s.ByGate.Clear();
            foreach (var gate in s.Gates)
            {
                int id = gate.Task.IdIn(packId, quest);
                if (order.ContainsKey(id)) s.ByGate.Add(new KeyValuePair<int, Gate>(id, gate));
            }

            // A task taken out is out of sight with everything under it, and
            // each of them is moved past as it starts - except under an "any
            // one" task, where finishing the task taken out (or its last
            // subtask, which finishes it) would finish the task above it for
            // the player. There the whole branch is left alone.
            var removed = new HashSet<int>(s.Removed.Where(order.ContainsKey));

            // The top-most task taken out above (or at) a task, or NoNode.
            int TakenOutAt(int id)
            {
                int found = QuestIds.NoNode;
                for (int at = id; at != QuestIds.NoNode && parentOf.ContainsKey(at); at = parentOf[at])
                    if (removed.Contains(at)) found = at;
                return found;
            }

            s.NeverShown.Clear();
            foreach (int id in s.AlwaysHidden.Where(order.ContainsKey)) s.NeverShown.Add(id);

            s.Skip.Clear();
            foreach (int id in postOrder)
            {
                int top = TakenOutAt(id);
                if (top == QuestIds.NoNode) continue;
                s.NeverShown.Add(id);

                int parent = parentOf[top];
                var parentTask = parent == QuestIds.NoNode ? null : quest.GetTask(parent);
                bool leaveAlone = parentTask != null && parentTask.Completion == TaskType.AnySubtask;
                s.Skip.Add(new KeyValuePair<int, bool>(id, leaveAlone));
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
