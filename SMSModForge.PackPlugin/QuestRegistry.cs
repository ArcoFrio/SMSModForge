using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Quests;
using SMSModForge.Shared;
using UnityEngine;
using GcQuest = GameCreator.Runtime.Quests.Quest;
using GcTask = GameCreator.Runtime.Quests.Task;

namespace SMSModForge.PackPlugin
{
    /// <summary>A quest a pack declares, in plain terms.</summary>
    internal sealed class QuestSpec
    {
        public string PackId = "";
        public string Key = "";
        public string Title = "";
        public string Description = "";
        public int SortOrder;
        public readonly List<TaskSpec> Tasks = new List<TaskSpec>();

        /// <summary>What happens to a player who had finished the quest when
        /// this version gives it tasks it did not have - see <see cref="QuestGrowth"/>.</summary>
        public string WhenStepsAdded = QuestGrowth.LeaveFinished;
    }

    /// <summary>One task, and the subtasks under it.</summary>
    internal sealed class TaskSpec
    {
        public string Key = "";
        public string Name = "";
        public string Description = "";

        /// <summary>The pack the task's words are from: what a
        /// <c>[PV:name]</c> in them is read from.</summary>
        public string PackId = "";

        /// <summary>How the game decides this task is done. The game's own
        /// quests use SubtasksInSequence for 191 of their 230 tasks, including
        /// every task that is completed by an instruction rather than by its
        /// subtasks - so that is the default.</summary>
        public TaskType Completion = TaskType.SubtasksInSequence;

        /// <summary>Whether the task shows a count, set by an action. The
        /// journal only draws the count on a top-level task.</summary>
        public bool Counter;
        public double CountTo;

        /// <summary>Registered hidden, so a subtask that has not started is
        /// out of the journal from the start. The ticker shows it once it has
        /// started, and hides it again if the quest is reset.</summary>
        public bool HideUntilStarted;

        /// <summary>Registered hidden for the same reason: the ticker shows it
        /// once its show conditions pass.</summary>
        public bool HideUntilConditions;

        public readonly List<TaskSpec> Subtasks = new List<TaskSpec>();
    }

    /// <summary>
    /// Pack quests, made into the game's own quests.
    /// <para/>
    /// <b>How the game finds a quest.</b> Everything reads a single catalogue,
    /// <c>QuestsRepository.Get.Quests</c>, which keeps an array of quests and two
    /// lookup tables beside it: quest by GUID, and quest-by-task-id for every
    /// task of every quest. Read off the game's IL rather than assumed, and the
    /// details are what this class is shaped by:
    /// <list type="bullet">
    ///   <item>The tables are built ONCE, on first lookup, from the array. The
    ///   internal <c>Set(Quest[])</c> replaces the array and nothing else, so
    ///   after anything has looked a quest up, replacing the array alone leaves
    ///   the new quests unfindable. Both tables are cleared after every change
    ///   so the next lookup rebuilds them.</item>
    ///   <item>The rebuild writes the task table with the indexer, so a task id
    ///   used twice does not throw - the later quest silently takes the task
    ///   over, and a game quest would lose it. So an id already in use is
    ///   refused here, loudly, before it ever reaches that table.</item>
    /// </list>
    /// <b>How progress survives a save.</b> The journal saves a quest's state by
    /// GUID and a task's by id, and those are derived from the pack's keys
    /// (<see cref="QuestIds"/>) - so a quest built again next session is the
    /// same quest to the save file.
    /// <para/>
    /// Registration is idempotent: calling it again rebuilds the catalogue as
    /// the game's own quests plus the ones given, so returning to the menu and
    /// loading back in does not stack duplicates.
    /// <para/>
    /// <b>When to register.</b> Packs load a few frames into the scene, after
    /// the journal has already read the save. The journal's state does not
    /// care - it is keyed by id and waits there to be found, which a save and a
    /// full restart confirmed in play. What the journal does on reading a save
    /// is set up the tasks that are in progress, and the only setup there is
    /// (read off <c>Journal.OnEnableTask</c>) is for a counter that follows a
    /// game variable. Pack counters are set by actions, so they have nothing to
    /// miss. A counter that follows a variable would, and would have to be
    /// registered before the save is read.
    /// </summary>
    internal static class QuestRegistry
    {
        private const string PlayerDataName = "11_PlayerData";

        /// <summary>The game's own quests, captured the first time anything is
        /// registered - before any pack quest is in the array.</summary>
        private static GcQuest[] _vanilla;

        /// <summary>Pack quests by "packId/questKey".</summary>
        private static readonly Dictionary<string, GcQuest> _byKey =
            new Dictionary<string, GcQuest>(StringComparer.Ordinal);

        /// <summary>Every GUID this registry has ever put in the catalogue, so
        /// a second registration can tell its own quests from the game's.</summary>
        private static readonly HashSet<string> _ours = new HashSet<string>(StringComparer.Ordinal);

        private static readonly FieldInfo QuestsLut = Field(typeof(QuestsList), "m_QuestsLut");
        private static readonly FieldInfo TasksLut = Field(typeof(QuestsList), "m_TasksLut");
        private static readonly MethodInfo SetQuests = typeof(QuestsList).GetMethod(
            "Set", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>
        /// Build these quests and put them in the game's catalogue, replacing
        /// whatever pack quests were there before.
        /// </summary>
        /// <returns>How many quests are now findable by the game.</returns>
        public static int Register(IEnumerable<QuestSpec> specs, ManualLogSource log)
        {
            if (QuestsLut == null || TasksLut == null || SetQuests == null)
            {
                // The one way this can break on a game update. Said once, and
                // quests are skipped rather than half-registered.
                log?.LogError("[SMSModForge.PackPlugin] Quests: the game's quest catalogue is not " +
                              "shaped as expected (m_QuestsLut " + (QuestsLut != null) + ", m_TasksLut " +
                              (TasksLut != null) + ", Set " + (SetQuests != null) + "). No pack quests.");
                return 0;
            }

            var catalogue = QuestsRepository.Get.Quests;
            if (_vanilla == null)
                _vanilla = (catalogue.Quests ?? new GcQuest[0])
                    .Where(q => q != null && !_ours.Contains(q.Id.String)).ToArray();

            // Every task id the game's own quests use. A pack id matching one of
            // these would take that task over without a word - see the type doc.
            var taken = new HashSet<int>();
            foreach (var quest in _vanilla)
                foreach (int id in quest.Tasks.Nodes.Keys) taken.Add(id);

            _byKey.Clear();
            var built = new List<GcQuest>();

            foreach (var spec in specs)
            {
                string label = spec.PackId + "/" + spec.Key;
                try
                {
                    var quest = Build(spec, taken, log);
                    if (quest == null) continue;

                    _byKey[label] = quest;
                    _ours.Add(quest.Id.String);
                    built.Add(quest);
                }
                catch (Exception e)
                {
                    log?.LogError("[SMSModForge.PackPlugin] Quests: " + label + " could not be built: " + e);
                }
            }

            var all = new GcQuest[_vanilla.Length + built.Count];
            Array.Copy(_vanilla, all, _vanilla.Length);
            built.CopyTo(all, _vanilla.Length);

            SetQuests.Invoke(catalogue, new object[] { all });
            QuestsLut.SetValue(catalogue, null);
            TasksLut.SetValue(catalogue, null);

            // The control. Registering reads exactly like success whether or not
            // the game can find anything afterwards, so ask it, through its own
            // lookup, for every quest just put there.
            int findable = 0;
            foreach (var quest in built)
            {
                if (ReferenceEquals(catalogue.Get(quest.Id), quest)) findable++;
                else
                    log?.LogError("[SMSModForge.PackPlugin] Quests: " + quest.name +
                                  " was registered but the game cannot find it by id.");
            }

            log?.LogInfo("[SMSModForge.PackPlugin] Quests: " + findable + " of " + built.Count +
                         " pack quest(s) findable, beside " + _vanilla.Length + " of the game's.");
            return findable;
        }

        /// <summary>A pack quest by pack id and key, or null.</summary>
        public static GcQuest Find(string packId, string questKey)
        {
            GcQuest quest;
            return _byKey.TryGetValue(packId + "/" + questKey, out quest) ? quest : null;
        }

        /// <summary>
        /// One of the game's own quests, by the name its dialogues use.
        /// <para/>
        /// Instructions in the game name a quest by its ASSET name - "Secrets
        /// (Adrian)" - which is also, in every case in this game, its title. The
        /// asset name is tried first because it is what a dialogue dump shows.
        /// </summary>
        public static GcQuest FindVanilla(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var quests = GameQuests.ToList();

            foreach (var q in quests)
                if (string.Equals(q.name, name, StringComparison.Ordinal)) return q;
            foreach (var q in quests)
                if (string.Equals(TitleOf(q), name, StringComparison.Ordinal)) return q;
            return null;
        }

        /// <summary>A quest's title, or empty. The game's getter takes an Args
        /// this has no instance of, and a title that could not be read is not a
        /// reason to stop looking at the rest.</summary>
        private static string TitleOf(GcQuest quest)
        {
            try { return quest.GetTitle(Args.EMPTY) ?? ""; }
            catch { return ""; }
        }

        /// <summary>The id of a pack task. Derived, so this needs no quest to
        /// have been built - but a task that does not belong to that quest is not
        /// in its tree, and the journal will refuse it.</summary>
        public static int TaskId(string packId, string questKey, string taskKey)
            => QuestIds.TaskId(packId, questKey, taskKey);

        /// <summary>
        /// The journal every quest instruction in the game points at.
        /// <para/>
        /// Looked for switched off too: the vanilla instructions hold a direct
        /// reference to this object, so its being inactive has never mattered to
        /// the game, and it must not matter here either.
        /// </summary>
        public static Journal FindJournal()
        {
            var go = GameObject.Find(PlayerDataName);
            if (go == null)
                foreach (var candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (candidate != null && candidate.name == PlayerDataName && candidate.scene.IsValid())
                    { go = candidate; break; }

            return go == null ? null : go.GetComponent<Journal>();
        }

        // ── Building one quest ───────────────────────────────────────────

        private static GcQuest Build(QuestSpec spec, HashSet<int> taken, ManualLogSource log)
        {
            string label = spec.PackId + "/" + spec.Key;
            string guid = QuestIds.QuestGuid(spec.PackId, spec.Key);

            // Every id this quest will use, checked before anything is built, so
            // a clash leaves no half-made quest behind.
            var ids = new List<KeyValuePair<TaskSpec, int>>();
            if (!CollectIds(spec, spec.Tasks, taken, ids, log)) return null;

            var quest = ScriptableObject.CreateInstance<GcQuest>();

            // The asset name is how the game's own instructions name a quest, so
            // a pack quest answers to its title the same way.
            quest.name = spec.Title;

            // Nothing in the scene references this object the way a scene
            // references an asset, and Unity unloads unreferenced assets on a
            // scene change. A quest that vanished mid-session would take the
            // catalogue's entry with it.
            quest.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // Through the pack's own getter, so a {PC} or a [PV:name] in them
            // reads as the name or the value each time the journal draws it.
            Set(quest, "m_Title", GetStringPackText.For(spec.Title, spec.PackId));
            Set(quest, "m_Description", GetStringPackText.For(spec.Description, spec.PackId));
            Set(quest, "m_SortOrder", spec.SortOrder);
            Set(quest, "m_UniqueId", new UniqueID(guid));

            var tree = new TasksTree();
            Set(quest, "m_Tasks", tree);

            var data = (TSerializableDictionary<int, TTreeDataItem<GcTask>>)Get(tree, "m_Data");
            var roots = (List<int>)Get(tree, "m_Roots");
            var nodes = tree.Nodes;

            foreach (var task in spec.Tasks)
                AddTask(spec, task, QuestIds.NoNode, data, nodes, roots);

            // The quest's identity is the one thing that must be exactly what
            // was asked for, and UniqueID's constructor is the game's code, not
            // this one. Checked rather than trusted.
            if (!string.Equals(quest.Id.String, guid, StringComparison.Ordinal))
            {
                log?.LogError("[SMSModForge.PackPlugin] Quests: " + label + " asked for id " + guid +
                              " and got " + quest.Id.String + ". Skipped - its progress would not save.");
                UnityEngine.Object.Destroy(quest);
                return null;
            }

            foreach (var pair in ids) taken.Add(pair.Value);
            return quest;
        }

        private static bool CollectIds(QuestSpec quest, List<TaskSpec> tasks, HashSet<int> taken,
                                       List<KeyValuePair<TaskSpec, int>> into, ManualLogSource log)
        {
            foreach (var task in tasks)
            {
                int id = QuestIds.TaskId(quest.PackId, quest.Key, task.Key);

                bool clash = taken.Contains(id) || into.Any(p => p.Value == id);
                if (clash)
                {
                    log?.LogError("[SMSModForge.PackPlugin] Quests: " + quest.PackId + "/" + quest.Key +
                                  " task '" + task.Key + "' has id " + id + ", which is already in use " +
                                  "(a duplicate task key, or - vanishingly rarely - a clash with another " +
                                  "quest). The whole quest is skipped rather than take a task from another.");
                    return false;
                }

                into.Add(new KeyValuePair<TaskSpec, int>(task, id));
                if (!CollectIds(quest, task.Subtasks, taken, into, log)) return false;
            }
            return true;
        }

        private static void AddTask(QuestSpec quest, TaskSpec spec, int parent,
                                    TSerializableDictionary<int, TTreeDataItem<GcTask>> data,
                                    TreeNodes nodes, List<int> roots)
        {
            int id = QuestIds.TaskId(quest.PackId, quest.Key, spec.Key);
            var task = NewTask(spec);

            data.Add(id, new TTreeDataItem<GcTask>(id, task));
            nodes.Add(id, new TreeNode(id, parent));

            if (parent == QuestIds.NoNode) roots.Add(id);
            else nodes[parent].Children.Add(id);

            foreach (var sub in spec.Subtasks)
                AddTask(quest, sub, id, data, nodes, roots);
        }

        /// <summary>One of the game's task objects, made from a pack's task.
        /// Its place in a quest is the caller's business.</summary>
        internal static GcTask NewTask(TaskSpec spec)
        {
            var task = new GcTask();
            Set(task, "m_Completion", spec.Completion);
            Set(task, "m_Name", GetStringPackText.For(spec.Name, spec.PackId));
            Set(task, "m_Description", GetStringPackText.For(spec.Description, spec.PackId));
            Set(task, "m_UseCounter", spec.Counter ? ProgressType.Value : ProgressType.None);
            if (spec.Counter) Set(task, "m_CountTo", new PropertyGetDecimal(spec.CountTo));
            if (spec.HideUntilStarted || spec.HideUntilConditions) Set(task, "m_IsHidden", true);
            return task;
        }

        /// <summary>The storage behind a quest's task tree: the task objects by
        /// id, and the top-level ids in order. The parent/child links are
        /// <c>quest.Tasks.Nodes</c>.</summary>
        internal static TSerializableDictionary<int, TTreeDataItem<GcTask>> TreeData(GcQuest quest)
            => (TSerializableDictionary<int, TTreeDataItem<GcTask>>)Get(quest.Tasks, "m_Data");

        internal static List<int> TreeRoots(GcQuest quest) => (List<int>)Get(quest.Tasks, "m_Roots");

        /// <summary>
        /// Make the catalogue build its lookups again. Its task table maps a
        /// task id to its quest and is built once, on first use - so after a
        /// task is added to, or taken back out of, one of the game's quests, the
        /// old table would still answer for the old tree.
        /// </summary>
        internal static void ForgetLookups()
        {
            if (QuestsLut == null || TasksLut == null) return;
            var catalogue = QuestsRepository.Get.Quests;
            QuestsLut.SetValue(catalogue, null);
            TasksLut.SetValue(catalogue, null);
        }

        /// <summary>The game's own quests: the ones captured before any pack
        /// quest was registered, or the live catalogue when nothing has been.</summary>
        internal static IEnumerable<GcQuest> GameQuests
        {
            get
            {
                var quests = _vanilla ?? QuestsRepository.Get.Quests.Quests ?? new GcQuest[0];
                foreach (var q in quests)
                    if (q != null && !_ours.Contains(q.Id.String)) yield return q;
            }
        }

        // ── Changing what the journal shows, while the game runs ───────────

        /// <summary>
        /// Replace a quest's description. The journal reads it through
        /// <c>Quest.GetDescription</c> each time it draws the quest
        /// (<c>TQuestUI.Refresh</c>), so the new text is what the player sees
        /// the next time the quest is on screen.
        /// </summary>
        internal static void SetDescription(GcQuest quest, string text, string packId)
            => Set(quest, "m_Description", GetStringPackText.For(text, packId));

        /// <summary>
        /// The description property a quest carries, so it can be put back.
        /// <para/>
        /// Kept as the OBJECT rather than as text: one of the game's quests may
        /// describe itself through something other than a plain string, and a
        /// pack that stops overriding it should leave the game exactly what it
        /// had rather than a copy of what it happened to say once.
        /// </summary>
        internal static object DescriptionOf(GcQuest quest) => Get(quest, "m_Description");

        /// <summary>Put back the property <see cref="DescriptionOf"/> read.</summary>
        internal static void RestoreDescription(GcQuest quest, object property)
        {
            if (property != null) Set(quest, "m_Description", property);
        }

        /// <summary>
        /// Show or hide a task in the journal. Read off the journal's IL: both
        /// the quest's task list and a task's subtask list
        /// (<c>TQuestUI</c>/<c>TTaskUI.CollectTaskIds</c>) leave out a task whose
        /// <c>IsHidden</c> is set, and the game's journal does not ask them to
        /// show hidden ones.
        /// </summary>
        internal static void SetHidden(GcTask task, bool hidden) => Set(task, "m_IsHidden", hidden);

        // ── Reflection, kept to private fields whose names and types were read
        //    off the game's metadata ────────────────────────────────────────

        private static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                                         | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        private static void Set(object target, string name, object value)
        {
            var f = Field(target.GetType(), name);
            if (f == null)
                throw new MissingFieldException(target.GetType().Name, name);
            f.SetValue(target, value);
        }

        private static object Get(object target, string name)
        {
            var f = Field(target.GetType(), name);
            if (f == null)
                throw new MissingFieldException(target.GetType().Name, name);

            // A tree made with `new` may not have run the initialiser that makes
            // its storage - create it then, as the field's own type.
            object value = f.GetValue(target);
            if (value == null)
            {
                value = Activator.CreateInstance(f.FieldType);
                f.SetValue(target, value);
            }
            return value;
        }
    }
}
