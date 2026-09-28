using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Quests;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using GcQuest = GameCreator.Runtime.Quests.Quest;
using GcTask = GameCreator.Runtime.Quests.Task;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Everything packs change on the game's own quests, and the way back.
    /// <para/>
    /// <b>Why a way back is needed at all.</b> The game's quests are assets, not
    /// scene objects: they outlive a scene change, so whatever a pack wrote into
    /// one is still there after the player returns to the menu and loads another
    /// save - or loads the same save after removing the pack. So the first time
    /// anything here touches a quest, what the game had is kept (its description
    /// property, each task's hidden flag, the shape of its task tree), every
    /// scene load puts all of that back, and the packs that load put their
    /// changes on again. Nothing reads a quest between the two.
    /// <para/>
    /// <b>Adding tasks.</b> A pack's task goes into the game's own tree, as a
    /// task of the game's in every way the journal can tell: it is saved under
    /// the quest's entry by an id derived from the pack, the game's quest,
    /// and the task's key (<see cref="QuestIds.AddedTaskId"/>), and it holds
    /// the quest up the way any task does. Where it goes is
    /// <see cref="QuestTreeEdits.Arrange"/>, the same rule the editor lists it by.
    /// <para/>
    /// <b>Taking tasks out</b> is not done to the tree. The game's own scenes
    /// still name those tasks, and a task that is not in the tree makes the
    /// game's quest code fail on a missing object the moment one of them asks
    /// about it while it is in progress (read off <c>TaskUtils.CanComplete</c>,
    /// which reaches for the task after walking its siblings). So a task taken
    /// out stays, hidden, and the ticker completes it as soon as it starts -
    /// which is what the quest moving on without it looks like to the game.
    /// </summary>
    internal static class VanillaQuestEdits
    {
        private const string Tag = "[SMSModForge.PackPlugin] Quests: ";

        private sealed class Snapshot
        {
            public object Description;
            public readonly Dictionary<int, bool> Hidden = new Dictionary<int, bool>();
            public HashSet<int> Ids;
            public List<int> Roots;
            public Dictionary<int, List<int>> Children;
        }

        private static readonly Dictionary<GcQuest, Snapshot> _snapshots = new Dictionary<GcQuest, Snapshot>();

        private static Snapshot SnapshotOf(GcQuest quest)
        {
            Snapshot snap;
            if (_snapshots.TryGetValue(quest, out snap)) return snap;

            snap = new Snapshot { Description = QuestRegistry.DescriptionOf(quest) };
            var data = QuestRegistry.TreeData(quest);
            snap.Ids = new HashSet<int>(data.Keys);
            snap.Roots = QuestRegistry.TreeRoots(quest).ToList();
            snap.Children = new Dictionary<int, List<int>>();
            foreach (var id in quest.Tasks.Nodes.Keys)
                snap.Children[id] = quest.Tasks.Nodes[id].Children.ToList();
            _snapshots[quest] = snap;
            return snap;
        }

        // ── What the ticker changes ──────────────────────────────────────

        /// <summary>Replace a game quest's description, keeping what it had.</summary>
        public static void SetDescription(GcQuest quest, string text, string packId)
        {
            SnapshotOf(quest);
            QuestRegistry.SetDescription(quest, text, packId);
        }

        /// <summary>Put back the description the game gave the quest - the one
        /// it had before any pack touched it this run, not whatever a pack wrote
        /// before the last scene change.</summary>
        public static void RestoreDescription(GcQuest quest)
        {
            Snapshot snap;
            if (_snapshots.TryGetValue(quest, out snap)) QuestRegistry.RestoreDescription(quest, snap.Description);
        }

        /// <summary>Show or hide a task of a game quest, keeping what it had.</summary>
        public static void SetHidden(GcQuest quest, int taskId, bool hidden)
        {
            var task = quest.GetTask(taskId);
            if (task == null) return;
            var snap = SnapshotOf(quest);
            if (snap.Ids.Contains(taskId) && !snap.Hidden.ContainsKey(taskId)) snap.Hidden[taskId] = task.IsHidden;
            QuestRegistry.SetHidden(task, hidden);
        }

        // ── Scene changes ────────────────────────────────────────────────

        /// <summary>
        /// Every game quest back to what the game had: descriptions, hidden
        /// flags, and the tasks packs added taken back out. Called whenever a
        /// scene loads, before the packs that are installed now apply theirs.
        /// </summary>
        public static void RestoreAll(ManualLogSource log)
        {
            _repairDone = false;
            if (_snapshots.Count == 0) return;

            int removed = 0;
            foreach (var pair in _snapshots)
            {
                var quest = pair.Key;
                var snap = pair.Value;
                if (quest == null) continue;
                try
                {
                    QuestRegistry.RestoreDescription(quest, snap.Description);

                    var data = QuestRegistry.TreeData(quest);
                    var nodes = quest.Tasks.Nodes;
                    foreach (var id in data.Keys.ToList())
                    {
                        if (snap.Ids.Contains(id)) continue;
                        data.Remove(id);
                        nodes.Remove(id);
                        removed++;
                    }

                    var roots = QuestRegistry.TreeRoots(quest);
                    roots.Clear();
                    roots.AddRange(snap.Roots);
                    foreach (var child in snap.Children)
                    {
                        if (!nodes.ContainsKey(child.Key)) continue;
                        var list = nodes[child.Key].Children;
                        list.Clear();
                        list.AddRange(child.Value);
                    }

                    foreach (var hidden in snap.Hidden)
                    {
                        var task = quest.GetTask(hidden.Key);
                        if (task != null) QuestRegistry.SetHidden(task, hidden.Value);
                    }
                    snap.Hidden.Clear();
                }
                catch (Exception e)
                {
                    log?.LogError(Tag + "could not put '" + quest.name + "' back as the game had it: " + e.Message);
                }
            }

            QuestRegistry.ForgetLookups();
            if (removed > 0)
                log?.LogInfo(Tag + "took " + removed + " pack task(s) back out of the game's quests.");
        }

        // ── Adding tasks ─────────────────────────────────────────────────

        /// <summary>
        /// Put every installed pack's added tasks into the game's quests, in
        /// pack load order. A pack's tasks placed before the same game task as
        /// an earlier pack's come after the earlier pack's.
        /// </summary>
        public static void Apply(IList<PackManifest> manifests, ManualLogSource log)
        {
            int added = 0;
            foreach (var m in manifests)
            {
                if (!(m?.Root["quests"] is JArray quests)) continue;
                foreach (var token in quests)
                {
                    var q = token as JObject;
                    string source = ((string)q?["source"] ?? "").Trim();
                    if (source.Length == 0) continue;

                    var addedTasks = q[QuestTreeEdits.AddedTasksKey] as JArray;
                    var hooks = q["vanillaTasks"] as JArray;
                    bool adds = addedTasks != null && addedTasks.Count > 0;
                    if (!adds && (hooks == null || hooks.Count == 0)) continue;

                    var quest = QuestRegistry.FindVanilla(source);
                    if (quest == null)
                    {
                        if (adds)
                            log?.LogWarning(Tag + m.PackId + " adds tasks to '" + source + "', which is not one of the "
                                            + "game's quests - they are left out.");
                        continue;
                    }

                    // The game's tasks the pack keeps out of sight are out of
                    // sight from now, not from the first frame the ticker runs:
                    // the ticker waits for the save to settle, and a journal
                    // opened before then would show them. The ticker shows the
                    // ones that should be seen.
                    HideFromTheStart(quest, hooks);
                    if (!adds) continue;

                    try { added += AddTo(quest, m.PackId, addedTasks, log); }
                    catch (Exception e)
                    {
                        log?.LogError(Tag + m.PackId + " could not add its tasks to '" + quest.name + "': " + e);
                    }
                }
            }

            QuestRegistry.ForgetLookups();
            if (added > 0)
                log?.LogInfo(Tag + added + " pack task(s) added to the game's quests.");
        }

        private static void HideFromTheStart(GcQuest quest, JArray hooks)
        {
            if (hooks == null) return;
            foreach (var token in hooks)
            {
                var h = token as JObject;
                if (h == null) continue;
                int id;
                if (!int.TryParse(((string)h["task"] ?? "").Trim(), out id)) continue;

                string visibility = (string)h[QuestTreeEdits.VisibilityKey];
                bool removed = h[QuestTreeEdits.RemovedKey]?.Type == JTokenType.Boolean && (bool)h[QuestTreeEdits.RemovedKey];
                if (removed || !string.IsNullOrEmpty(visibility)) SetHidden(quest, id, true);
            }
        }

        private sealed class Added
        {
            public JObject Json;
            public string Key;
            public string Under;
            public string Before;
        }

        private static int AddTo(GcQuest quest, string packId, JArray addedTasks, ManualLogSource log)
        {
            var snap = SnapshotOf(quest);
            var data = QuestRegistry.TreeData(quest);
            var nodes = quest.Tasks.Nodes;
            var roots = QuestRegistry.TreeRoots(quest);

            var list = new List<Added>();
            foreach (var token in addedTasks)
            {
                var t = token as JObject;
                string key = ((string)t?["key"] ?? "").Trim();
                if (key.Length == 0) continue;
                list.Add(new Added
                {
                    Json = t,
                    Key = key,
                    Under = QuestTreeEdits.Clean((string)t[QuestTreeEdits.UnderKey]),
                    Before = QuestTreeEdits.Clean((string)t[QuestTreeEdits.BeforeKey]),
                });
            }

            int count = 0;
            string label = packId + " in '" + quest.name + "'";

            // Everything placed under the same parent is placed in one go, so
            // several added tasks before the same game task keep their order.
            foreach (var group in list.GroupBy(a => a.Under))
            {
                List<int> siblings;
                int parent;
                if (group.Key.Length == 0)
                {
                    siblings = roots;
                    parent = QuestIds.NoNode;
                }
                else
                {
                    int under;
                    if (!int.TryParse(group.Key, out under) || !snap.Ids.Contains(under) || !nodes.ContainsKey(under))
                    {
                        log?.LogWarning(Tag + label + ": task " + group.Key + " is not in the quest, so the "
                                        + group.Count() + " task(s) placed under it are left out.");
                        continue;
                    }
                    siblings = nodes[under].Children;
                    parent = under;
                }

                // Built first, placed after: a task whose id clashes is left out
                // before it takes a slot.
                var made = new Dictionary<Added, int>();
                foreach (var a in group)
                {
                    int id = QuestIds.AddedTaskId(packId, quest.name, a.Key);
                    if (data.ContainsKey(id))
                    {
                        log?.LogError(Tag + label + ": task '" + a.Key + "' has id " + id + ", which the quest "
                                      + "already has (a task key used twice, or two packs adding the same one). "
                                      + "It is left out.");
                        continue;
                    }

                    var spec = QuestRuntime.ReadTask(a.Json, packId);
                    data.Add(id, new TTreeDataItem<GcTask>(id, QuestRegistry.NewTask(spec)));
                    nodes.Add(id, new TreeNode(id, parent));
                    made[a] = id;
                    count++;

                    // The game's quests are two levels deep, and so are the
                    // tasks a pack adds: only a top-level task gets subtasks.
                    var subs = a.Json["subtasks"] as JArray;
                    if (subs == null || subs.Count == 0) continue;
                    if (parent != QuestIds.NoNode)
                    {
                        log?.LogWarning(Tag + label + ": subtask '" + a.Key + "' has subtasks of its own, which are "
                                        + "left out.");
                        continue;
                    }
                    foreach (var subToken in subs)
                    {
                        var sub = subToken as JObject;
                        string subKey = ((string)sub?["key"] ?? "").Trim();
                        if (subKey.Length == 0) continue;
                        int subId = QuestIds.AddedTaskId(packId, quest.name, subKey);
                        if (data.ContainsKey(subId))
                        {
                            log?.LogError(Tag + label + ": task '" + subKey + "' has id " + subId + ", which the "
                                          + "quest already has. It is left out.");
                            continue;
                        }
                        data.Add(subId, new TTreeDataItem<GcTask>(subId, QuestRegistry.NewTask(QuestRuntime.ReadTask(sub, packId))));
                        nodes.Add(subId, new TreeNode(subId, id));
                        nodes[id].Children.Add(subId);
                        count++;
                        if ((sub["subtasks"] as JArray)?.Count > 0)
                            log?.LogWarning(Tag + label + ": subtask '" + subKey + "' has subtasks of its own, "
                                            + "which are left out.");
                    }
                }

                var placed = QuestTreeEdits.Arrange(
                    siblings.ToList(), id => id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    group.Where(made.ContainsKey).ToList(), a => a.Before);
                siblings.Clear();
                foreach (var p in placed) siblings.Add(p.IsAdded ? made[p.Added] : p.Game);
            }
            return count;
        }

        // ── Putting a save back on its feet ──────────────────────────────

        private static bool _repairDone;

        private static readonly FieldInfo JournalTasks =
            typeof(Journal).GetField("m_Tasks", BindingFlags.Instance | BindingFlags.NonPublic);
        // The journal's task store is an internal type, so it is reached
        // through the field that holds it.
        private static readonly MethodInfo EvaluateQuest =
            JournalTasks?.FieldType.GetMethod("EvaluateQuestOnChangeTask",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// Once per loaded save: find the game quests whose saved progress
        /// names tasks the quest no longer has - a pack that added tasks to it
        /// is gone, or adds different ones now - and do for each what the game
        /// would have done had its task list always been this one. See
        /// <see cref="QuestTreeEdits.NextRepair"/>.
        /// </summary>
        public static void RepairOnce(ManualLogSource log)
        {
            if (_repairDone) return;
            _repairDone = true;

            var journal = QuestRuntime.Journal;
            if (journal == null) return;

            var quests = QuestRegistry.GameQuests.ToList();
            var stale = new HashSet<GcQuest>();
            foreach (var entry in journal.TaskEntries)
            {
                int taskId = entry.Key.TaskId;
                foreach (var quest in quests)
                {
                    if (stale.Contains(quest) || quest.Contains(taskId)) continue;
                    if (entry.Key.Equals(new TaskKey(quest, taskId))) stale.Add(quest);
                }
            }

            foreach (var quest in stale)
            {
                try { Repair(journal, quest, log); }
                catch (Exception e)
                {
                    log?.LogError(Tag + "could not check '" + quest.name + "' for tasks it lost: " + e.Message);
                }
            }
        }

        private static void Repair(Journal journal, GcQuest quest, ManualLogSource log)
        {
            if (journal.GetQuestState(quest) != State.Active)
            {
                log?.LogInfo(Tag + "'" + quest.name + "' has saved progress on tasks it no longer has; it is not in "
                             + "progress, so nothing needs doing.");
                return;
            }

            var roots = Nodes(quest, QuestRegistry.TreeRoots(quest));
            var tried = new HashSet<string>();
            for (int i = 0; i < 64; i++)
            {
                var step = QuestTreeEdits.NextRepair(roots, journal.GetQuestState(quest) == State.Active,
                                                     id => StateOf(journal.GetTaskState(quest, id)));
                if (step.Kind == QuestTreeEdits.RepairKind.None) break;

                // A step the game refused comes back unchanged; asking again
                // would loop, so each is tried once.
                if (!tried.Add(step.Kind + ":" + step.TaskId)) break;

                string task = step.Kind == QuestTreeEdits.RepairKind.EvaluateQuest
                    ? "the quest"
                    : "task " + step.TaskId + " (" + NameOf(quest, step.TaskId) + ")";
                switch (step.Kind)
                {
                    case QuestTreeEdits.RepairKind.CompleteTask:
                        journal.CompleteTask(quest, step.TaskId);
                        break;
                    case QuestTreeEdits.RepairKind.ActivateTask:
                        journal.ActivateTask(quest, step.TaskId);
                        break;
                    case QuestTreeEdits.RepairKind.EvaluateQuest:
                        if (JournalTasks == null || EvaluateQuest == null)
                        {
                            log?.LogWarning(Tag + "'" + quest.name + "' has every top-level task done but is still in "
                                            + "progress, and this game build does not have the check that would "
                                            + "complete it.");
                            return;
                        }
                        EvaluateQuest.Invoke(JournalTasks.GetValue(journal), new object[] { quest });
                        break;
                }
                bool done;
                switch (step.Kind)
                {
                    case QuestTreeEdits.RepairKind.CompleteTask:
                        done = journal.GetTaskState(quest, step.TaskId) == State.Completed; break;
                    case QuestTreeEdits.RepairKind.ActivateTask:
                        done = journal.GetTaskState(quest, step.TaskId) == State.Active; break;
                    default:
                        done = journal.GetQuestState(quest) != State.Active; break;
                }
                bool starting = step.Kind == QuestTreeEdits.RepairKind.ActivateTask;
                log?.LogInfo(Tag + "'" + quest.name + "' lost tasks since this save was made - "
                             + (done ? (starting ? "started " : "completed ") + task + ", as the game would have."
                                     : "tried to " + (starting ? "start " : "complete ") + task + ", and the game refused."));
            }
        }

        /// <summary>
        /// Where the loaded save stands with one of the game's quests, as the
        /// packs have left its tasks - see <see cref="QuestTreeEdits.ProgressOf"/>.
        /// Null when the journal cannot be read.
        /// </summary>
        public static QuestTreeEdits.SaveProgress? ProgressOf(GcQuest quest, IEnumerable<int> added)
        {
            var journal = QuestRuntime.Journal;
            if (journal == null || quest == null) return null;
            return QuestTreeEdits.ProgressOf(StateOf(journal.GetQuestState(quest)),
                                             Nodes(quest, QuestRegistry.TreeRoots(quest)),
                                             added,
                                             id => StateOf(journal.GetTaskState(quest, id)));
        }

        private static List<QuestTreeEdits.Node> Nodes(GcQuest quest, IEnumerable<int> ids)
        {
            var list = new List<QuestTreeEdits.Node>();
            foreach (int id in ids)
            {
                var task = quest.GetTask(id);
                if (task == null) continue;
                var node = new QuestTreeEdits.Node { Id = id, Completion = CompletionOf(task.Completion) };
                node.Children.AddRange(Nodes(quest, quest.Tasks.Children(id)));
                list.Add(node);
            }
            return list;
        }

        /// <summary>The game's task state in the shared vocabulary - by name,
        /// not by number, so a reordered enum cannot turn "done" into "failed".</summary>
        internal static QuestTreeEdits.TaskState StateOf(State state)
        {
            switch (state)
            {
                case State.Active: return QuestTreeEdits.TaskState.Active;
                case State.Completed: return QuestTreeEdits.TaskState.Completed;
                case State.Abandoned: return QuestTreeEdits.TaskState.Abandoned;
                case State.Failed: return QuestTreeEdits.TaskState.Failed;
                default: return QuestTreeEdits.TaskState.Inactive;
            }
        }

        internal static QuestTreeEdits.Completion CompletionOf(TaskType type)
        {
            switch (type)
            {
                case TaskType.SubtasksInCombination: return QuestTreeEdits.Completion.AnyOrder;
                case TaskType.AnySubtask: return QuestTreeEdits.Completion.AnyOne;
                case TaskType.Manual: return QuestTreeEdits.Completion.ByAction;
                default: return QuestTreeEdits.Completion.InOrder;
            }
        }

        private static string NameOf(GcQuest quest, int id)
        {
            try { return quest.GetTask(id)?.GetName(Args.EMPTY) ?? ""; }
            catch { return ""; }
        }
    }
}
