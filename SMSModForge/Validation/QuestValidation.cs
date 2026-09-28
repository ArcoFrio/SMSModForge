using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Model;
using V = SMSModForge.Shared.QuestVocabulary;
using SMSModForge.Localization;

namespace SMSModForge.Validation;

/// <summary>
/// Everything that can be wrong with a pack's quests, and with the rows that
/// name one.
/// <para/>
/// Most of it is the game refusing quietly. The journal does not throw when it
/// is asked to complete a task that is not in progress, or to count a task that
/// does not count - it just does nothing, and a quest that can never finish
/// looks, in the editor, exactly like one that can. So the checks here are the
/// game's own rules, said before the pack ships rather than discovered in play.
/// </summary>
internal static class QuestValidation
{
    public static void Check(ModPack pack, List<ValidationIssue> issues)
    {
        var rows = Rows(pack).ToList();
        CheckDefinitions(pack, rows, issues);
        foreach (var row in rows) CheckRow(pack, row, issues);
        CheckGates(pack, issues);
    }

    // -- Conditions taken out of the game's scripts --------------------

    /// <summary>
    /// Every condition list of the game's that the pack can take conditions
    /// out of, by <see cref="GameGateEditDef.Key"/>: the lists that play its
    /// conversations and the scripts that start or reset its quests. Built
    /// once, and only for a pack that takes something out.
    /// </summary>
    private static readonly Lazy<Dictionary<string, List<VanillaDialogueCatalog.Step>>> GameLists = new(() =>
    {
        var lists = new Dictionary<string, List<VanillaDialogueCatalog.Step>>(StringComparer.Ordinal);
        void Add(string by, string script, List<VanillaDialogueCatalog.Step> when,
                 List<VanillaDialogueCatalog.GateLocation> gates)
        {
            int next = 0;
            foreach (var gate in gates)
            {
                string key = Shared.GameConditionEdits.GateKey(by, script, Shared.GameConditionEdits.PathText(gate.At));
                if (!lists.ContainsKey(key))
                    lists[key] = when.Skip(next).Take(gate.Count).ToList();
                next += gate.Count;
            }
        }

        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var dialogue = VanillaDialogueCatalog.Open(entry.Id);
            if (dialogue == null) continue;
            foreach (var start in dialogue.Starts) Add(start.By, start.Script, start.When, start.Gates);
        }
        foreach (string quest in VanillaQuestReferences.Quests)
        {
            var sites = VanillaQuestReferences.For(quest);
            if (sites == null) continue;
            foreach (var site in sites.Starts.Concat(sites.Resets).Where(x => !x.IsDialogue))
                Add(site.By ?? "", site.Script ?? "", site.When, site.Gates);
        }
        return lists;
    });

    private static void CheckGates(ModPack pack, List<ValidationIssue> issues)
    {
        if (!pack.VanillaGates.Any(g => g.Removed.Count > 0) || !VanillaDialogueCatalog.IsAvailable) return;
        for (int i = 0; i < pack.VanillaGates.Count; i++)
        {
            var gate = pack.VanillaGates[i];
            if (gate.Removed.Count == 0) continue;
            string where = $"vanillaGates[{i}]";

            if (!GameLists.Value.TryGetValue(gate.Key, out var game))
            {
                issues.Add(new(Severity.Warning, where,
                    Loc.F("validation.quest.gateUnknown", "by", gate.By),
                    "quest.gateUnknown"));
                continue;
            }

            foreach (var removed in gate.Removed)
            {
                if (removed.Index >= 0 && removed.Index < game.Count
                    && string.Equals(game[removed.Index].Type, removed.Type, StringComparison.Ordinal))
                    continue;
                issues.Add(new(Severity.Warning, where,
                    removed.Title.Length > 0
                        ? Loc.F("validation.quest.gateChanged", "condition", removed.Title, "by", gate.By)
                        : Loc.F("validation.quest.gateChangedUnnamed", "by", gate.By),
                    "quest.gateChanged"));
            }
        }
    }

    // ── The quests themselves ────────────────────────────────────────

    private static void CheckDefinitions(ModPack pack, List<Row> rows, List<ValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var quest in pack.Quests)
        {
            string where = $"quests[{quest.Key}]";

            if (string.IsNullOrWhiteSpace(quest.Key))
            {
                issues.Add(new(Severity.Error, where, Loc.T("validation.quest.noKey"), "quest.noKey"));
                continue;
            }
            if (!seen.Add(quest.Key))
                issues.Add(new(Severity.Error, where, Loc.F("validation.quest.duplicateKey", "name", quest.Key), "quest.duplicateKey"));

            if (quest.IsVanillaExtension)
            {
                CheckExtension(pack, quest, where, rows, issues);
                continue;
            }

            if (string.IsNullOrWhiteSpace(VanillaQuests.StripTags(quest.Title)))
                issues.Add(new(Severity.Warning, $"{where}.title", Loc.T("validation.quest.noTitle"), "quest.noTitle"));

            if (quest.Tasks.Count == 0)
            {
                issues.Add(new(Severity.Warning, $"{where}.tasks", Loc.T("validation.quest.noTasks"), "quest.noTasks"));
            }

            CheckTaskDefinitions(pack, quest, "tasks", where, issues, "validation.quest.duplicateTaskKey");

            bool started = quest.StartConditions.Count > 0
                || rows.Any(r => r.IsAction && !r.Vanilla && r.Quest == quest.Key && V.Is(r.Operation, V.Start));
            if (!started)
            {
                issues.Add(new(Severity.Warning, where, Loc.T("validation.quest.neverStarted"), "quest.neverStarted"));
                continue;
            }

            // Said once for the quest as a whole, because that is the question an
            // author asks - "can a player finish this?" - and the per-task
            // warnings below answer a different one, which task to fix. Top-level
            // tasks run in order, so the first one nothing can finish is where
            // every player's copy of the quest stops.
            var own = OwnRows(quest) with { Rows = rows };
            var stuck = quest.Tasks.FirstOrDefault(t => !Finishable(own, t, null));
            if (stuck != null)
                issues.Add(new(Severity.Warning, where,
                    Loc.F("validation.quest.cannotComplete", "task", string.IsNullOrWhiteSpace(stuck.Name) ? stuck.Key : stuck.Name),
                    "quest.cannotComplete"));

            foreach (var top in quest.Tasks)
                ReportUnfinishable(own, top, null, $"{where}.tasks", issues);

            // A counting subtask used to be warned about here, because the
            // journal builds the counter under its subtask rows and then never
            // switches it on. The runtime now points the one field that does
            // that at the counter already there, so the number is drawn and
            // there is nothing left to warn about - see Shared/QuestCounters
            // and the plugin's SubtaskCounters.
        }
    }

    /// <summary>
    /// What every task of the pack's needs, whether it is in a quest of the
    /// pack's own or added to one of the game's: a runtime name unique in the
    /// quest, some text, a counter that can move, a completion the game knows,
    /// and no completion lists on a task that finishes through its subtasks.
    /// </summary>
    private static void CheckTaskDefinitions(ModPack pack, QuestDef quest, string list, string where,
                                             List<ValidationIssue> issues, string duplicateMessage)
    {
        var taskKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in quest.AllTasks())
        {
            string tw = $"{where}.{list}[{task.Key}]";
            if (string.IsNullOrWhiteSpace(task.Key))
                issues.Add(new(Severity.Error, tw, Loc.T("validation.quest.taskNoKey"), "quest.taskNoKey"));
            else if (!taskKeys.Add(task.Key))
                issues.Add(new(Severity.Error, tw, Loc.F(duplicateMessage, "name", task.Key), "quest.duplicateTaskKey"));

            if (string.IsNullOrWhiteSpace(task.Name))
                issues.Add(new(Severity.Warning, $"{tw}.name", Loc.T("validation.quest.taskNoText"), "quest.taskNoText"));

            if (task.CountsFromVariable)
                CheckCountVariable(pack, task, $"{tw}.countVariable", issues);

            if (task.Subtasks.Count > 0 && !V.Completions.Any(c => V.Is(task.Completion, c)))
                issues.Add(new(Severity.Error, $"{tw}.completion", Loc.F("validation.quest.badCompletion", "completion", task.Completion), "quest.badCompletion"));

            if (task.HideUntilConditions && task.ShowConditions.Count == 0)
                issues.Add(new(Severity.Warning, $"{tw}.showConditions",
                    Loc.T("validation.quest.showConditionsEmpty"),
                    "quest.showConditionsEmpty"));

            // What would stop the quest ever finishing is said further down;
            // this is the lists that are never read.
            if (task.Subtasks.Count > 0 && task.Conditions.Count + task.Actions.Count > 0)
                issues.Add(new(Severity.Warning, tw,
                    Loc.T("validation.quest.headerHasCompletion"),
                    "quest.headerHasCompletion"));
        }
    }

    /// <summary>
    /// An entry about one of the game's own quests.
    /// <para/>
    /// What the pack says about the quest cannot break it: the game goes on
    /// moving its own quest. What CAN be wrong is the pack saying it about
    /// nothing - a quest the game does not have, a task that quest does not
    /// have, or nothing at all - and, once it adds tasks, everything a task of
    /// its own can get wrong, plus where they sit among the game's.
    /// </summary>
    private static void CheckExtension(ModPack pack, QuestDef quest, string where, List<Row> rows,
                                       List<ValidationIssue> issues)
    {
        var theirs = VanillaQuests.Find(quest.Source);
        if (theirs == null)
        {
            issues.Add(new(Severity.Warning, $"{where}.source",
                Loc.F("validation.quest.unknownVanillaSource", "quest", quest.Source),
                "quest.unknownVanillaSource"));
        }
        else
        {
            foreach (var hook in quest.VanillaTasks)
            {
                if (theirs.Task(hook.Task) != null) continue;
                issues.Add(new(Severity.Warning, $"{where}.vanillaTasks[{hook.Task}]",
                    Loc.F("validation.quest.unknownVanillaTask", "quest", theirs.PlainTitle, "task", hook.Task),
                    "quest.unknownVanillaTask"));
            }
        }

        // The pack's conditions at the game's places: a place the game no
        // longer has never asks them.
        var sites = VanillaQuestReferences.For(quest.Source);
        if (VanillaQuestReferences.IsAvailable)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            void Know(IEnumerable<VanillaQuestReferences.Site> list, string does)
            {
                foreach (var site in list)
                    known.Add(site.IsDialogue
                        ? Shared.GameConditionEdits.DialogueSiteKey(site.Dialogue, site.Node ?? 0, site.Moment, does)
                        : Shared.GameConditionEdits.ScriptSiteKey(site.By, site.Script, does));
            }
            if (sites != null)
            {
                Know(sites.Starts, Shared.GameConditionEdits.Starts);
                Know(sites.Resets, Shared.GameConditionEdits.Resets);
            }
            for (int i = 0; i < quest.SiteConditions.Count; i++)
            {
                var place = quest.SiteConditions[i];
                if (place.Conditions.Count == 0 || known.Contains(place.Key)) continue;
                issues.Add(new(Severity.Warning, $"{where}.siteConditions[{i}]",
                    Loc.T("validation.quest.siteUnknown"),
                    "quest.siteUnknown"));
            }
        }

        foreach (var hook in quest.VanillaTasks)
        {
            if (hook.Visibility.Length > 0 && !hook.IsHidden && !hook.IsHiddenUntilStarted && !hook.IsHiddenUntilConditions)
                issues.Add(new(Severity.Warning, $"{where}.vanillaTasks[{hook.Task}].visibility",
                    Loc.F("validation.quest.badVisibility", "visibility", hook.Visibility),
                    "quest.badVisibility"));
            if (hook.IsHiddenUntilConditions && hook.ShowConditions.Count == 0)
                issues.Add(new(Severity.Warning, $"{where}.vanillaTasks[{hook.Task}].showConditions",
                    Loc.T("validation.quest.showConditionsEmpty"),
                    "quest.showConditionsEmpty"));
        }

        if (quest.AddedTasks.Count > 0)
            CheckAddedTasks(pack, quest, theirs, where, rows, issues);

        if (theirs != null)
        {
            var tree = ExtensionTree.Build(theirs, quest);
            var tops = tree.Where(n => n.IsTopLevel && !n.Orphan).ToList();
            if (tops.Count > 0 && tops.All(n => n.Removed))
                issues.Add(new(Severity.Warning, where,
                    Loc.F("validation.quest.everyTaskRemoved", "quest", theirs.PlainTitle),
                    "quest.everyTaskRemoved"));
        }

        if (quest.Tasks.Count > 0)
            issues.Add(new(Severity.Warning, $"{where}.tasks",
                Loc.T("validation.quest.extensionHasOwnTasks"),
                "quest.extensionHasOwnTasks"));

        bool saysSomething = quest.Description.Length > 0
                             || quest.StartConditions.Count > 0
                             || quest.ResetConditions.Count > 0
                             || quest.SiteConditions.Any(s => s.Conditions.Count > 0)
                             || quest.AddedTasks.Count > 0
                             || quest.VanillaTasks.Any(h => h.DoesAnything)
                             || ChangesPlaces(pack, quest);
        if (!saysSomething)
            issues.Add(new(Severity.Warning, where,
                quest.Source.Length == 0
                    ? Loc.T("validation.quest.extensionNamesNothing")
                    : Loc.F("validation.quest.extensionSaysNothing", "quest", theirs?.PlainTitle ?? quest.Source),
                "quest.extensionSaysNothing"));

        foreach (var other in pack.Quests)
        {
            if (ReferenceEquals(other, quest) || !other.IsVanillaExtension) continue;
            if (!string.Equals(other.Source, quest.Source, StringComparison.Ordinal)) continue;
            if (string.CompareOrdinal(other.Key, quest.Key) >= 0) continue;

            issues.Add(new(Severity.Warning, where,
                Loc.F("validation.quest.twoExtensions", "entry", other.Key),
                "quest.twoExtensions"));
            break;
        }
    }

    /// <summary>
    /// Whether the pack changes any of the game's places that start or reset
    /// this quest. Those changes are kept with the script or the conversation
    /// rather than with the quest entry, but they are made from the quest, and
    /// an entry that exists for them is not one that does nothing.
    /// </summary>
    private static bool ChangesPlaces(ModPack pack, QuestDef quest)
        => PlaceRules.SitesOf(quest)
                     .Any(found => PlaceRules.IsChanged(pack, found.Site,
                                                        PlaceRules.PlaceOf(quest, found.Site, found.Does)));

    /// <summary>
    /// Tasks a pack adds to one of the game's quests: everything a task of the
    /// pack's own is checked for, and where they sit.
    /// </summary>
    private static void CheckAddedTasks(ModPack pack, QuestDef quest, VanillaQuests.VanillaQuest? theirs,
                                        string where, List<Row> rows, List<ValidationIssue> issues)
    {
        CheckTaskDefinitions(pack, quest, "addedTasks", where, issues, "validation.quest.duplicateAddedTaskKey");

        foreach (var task in quest.AllTasks())
            if (task.Key.Length > 0 && task.Key.All(char.IsDigit))
                issues.Add(new(Severity.Error, $"{where}.addedTasks[{task.Key}]",
                    Loc.F("validation.quest.addedTaskNumericKey", "name", task.Key),
                    "quest.addedTaskNumericKey"));

        if (theirs == null) return;   // said already: the quest itself is unknown

        var tree = ExtensionTree.Build(theirs, quest);
        var named = new NamedTask(true, quest.Source);
        foreach (var node in tree)
        {
            if (node.Added == null) continue;
            string tw = $"{where}.addedTasks[{node.Added.Key}]";
            string name = string.IsNullOrWhiteSpace(node.Added.Name) ? node.Added.Key : node.Added.Name;

            if (node.Orphan)
            {
                if (node.Parent == null)
                    issues.Add(new(Severity.Warning, tw,
                        Loc.F("validation.quest.addedTaskUnknownParent", "task", name,
                              "under", ((AddedTaskDef)node.Added).Under, "quest", theirs.PlainTitle),
                        "quest.addedTaskUnknownParent"));
                continue;
            }

            if (!node.IsBuilt)
            {
                issues.Add(new(Severity.Warning, tw,
                    Loc.F("validation.quest.addedTaskTooDeep", "task", name),
                    "quest.addedTaskTooDeep"));
                continue;
            }

            var parent = node.Parent;
            if (parent?.Game != null && parent.Removed)
                issues.Add(new(Severity.Warning, tw,
                    Loc.F("validation.quest.addedUnderRemoved", "task", name),
                    "quest.addedUnderRemoved"));
            else if (parent != null && V.Is(parent.Completion, V.ByAction))
                issues.Add(new(Severity.Warning, tw,
                    Loc.F("validation.quest.addedUnderByAction", "task", name),
                    "quest.addedUnderByAction"));
        }

        // What would hold the game's quest up for good. Only for tasks the
        // runtime builds, and not under an "any one" task, which another of its
        // subtasks can finish.
        foreach (var node in tree)
        {
            if (node.Added is not AddedTaskDef added || node.Orphan || !node.IsBuilt) continue;
            var parent = node.Parent;
            if (parent != null && (parent.Removed || V.Is(parent.Completion, V.ByAction) || V.Is(parent.Completion, V.AnyOne)))
                continue;
            ReportUnfinishable(named with { Rows = rows }, added, parent?.Completion, $"{where}.addedTasks", issues);
        }
    }

    /// <summary>
    /// Whether anything in the pack can ever finish a task, by the game's own
    /// rules.
    /// <para/>
    /// A Quest action that completes it (or counts it, when it counts) always
    /// can for a task with no subtasks, or one set to complete by action. For a
    /// task with subtasks it depends on where the task sits, read off the
    /// game's <c>TaskUtils</c>: under a top-level or "in order" parent the
    /// check walks the task's siblings and answers yes on reaching the task
    /// itself - before it ever looks at the subtasks - so an action completes
    /// it outright. The game's own quests do exactly that, 29 times. Under an
    /// "any order" or "any one" parent the subtasks are checked, so the action
    /// alone is not enough.
    /// <para/>
    /// Otherwise a task with subtasks finishes itself when all of them are done,
    /// or any one of them for "any one".
    /// </summary>
    private static bool Finishable(NamedTask quest, QuestTaskDef task, string? parentCompletion)
    {
        bool acted = quest.Rows.Any(r => r.IsAction && r.Vanilla == quest.Vanilla && r.Quest == quest.Name && r.Task == task.Key
            && (V.Is(r.Operation, V.CompleteTask)
                || (task.CountTo is > 0 && (V.Is(r.Operation, V.SetCounter) || V.Is(r.Operation, V.AddToCounter)))));

        // A counter that follows a variable finishes it when the variable gets
        // there. Whether anything moves the variable is the variable's business.
        if (task.CountsFromVariable && !string.IsNullOrWhiteSpace(task.CountVariable))
            return true;

        // Its own completion conditions finish it too - but only on a task
        // with no subtasks, which is the only kind the runtime checks.
        if (task.Subtasks.Count == 0 && task.Conditions.Count > 0)
            return true;

        if (task.Subtasks.Count == 0 || V.Is(task.Completion, V.ByAction))
            return acted;

        bool inOrderParent = parentCompletion == null || V.Is(parentCompletion, V.InOrder);
        if (acted && inOrderParent) return true;

        return V.Is(task.Completion, V.AnyOne)
            ? task.Subtasks.Any(t => Finishable(quest, t, task.Completion))
            : task.Subtasks.All(t => Finishable(quest, t, task.Completion));
    }

    /// <summary>How rows name the quest a task is in: a quest of the pack's own
    /// by its key, or one of the game's - for the tasks a pack adds to it - by
    /// the game's name, on the Vanilla side.</summary>
    private sealed record NamedTask(bool Vanilla, string Name)
    {
        public List<Row> Rows { get; init; } = new();
    }

    private static NamedTask OwnRows(QuestDef quest) => new(false, quest.Key);

    /// <summary>
    /// Say where a quest stops, once, at the task that stops it: the task
    /// itself when an action has to finish it, the parent when it is an "any
    /// one" whose subtasks all stall, and otherwise each subtask that does.
    /// Subtasks under a task that completes by action are never started, so
    /// they are notes and nothing is expected of them.
    /// </summary>
    private static void ReportUnfinishable(NamedTask quest, QuestTaskDef task, string? parentCompletion,
                                           string where, List<ValidationIssue> issues)
    {
        if (Finishable(quest, task, parentCompletion)) return;
        string tw = $"{where}[{task.Key}]";

        if (task.Subtasks.Count == 0 || V.Is(task.Completion, V.ByAction))
        {
            issues.Add(new(Severity.Warning, tw,
                task.CountTo is > 0
                    ? Loc.T("validation.quest.counterNeverCompleted")
                    : Loc.T("validation.quest.taskNeverCompleted"),
                "quest.taskNeverCompleted"));
            return;
        }

        if (V.Is(task.Completion, V.AnyOne))
        {
            issues.Add(new(Severity.Warning, tw,
                Loc.T("validation.quest.anyOneNeverCompleted"),
                "quest.taskNeverCompleted"));
            return;
        }

        foreach (var sub in task.Subtasks)
            ReportUnfinishable(quest, sub, task.Completion, where, issues);
    }

    // ── Rows that name a quest ───────────────────────────────────────

    private static void CheckRow(ModPack pack, Row row, List<ValidationIssue> issues)
    {
        if (row.Quest.Length == 0)
        {
            issues.Add(new(Severity.Error, row.Where,
                Loc.T(row.IsAction ? "validation.quest.actionNoQuest" : "validation.quest.conditionNoQuest"), "quest.rowNoQuest"));
            return;
        }

        if (!QuestReferences.QuestExists(pack.Quests, row.Vanilla, row.Quest))
        {
            if (row.Vanilla)
                issues.Add(new(Severity.Warning, row.Where,
                    Loc.F("validation.quest.unknownVanillaQuest", "quest", row.Quest),
                    "quest.unknownVanillaQuest"));
            else
                issues.Add(new(Severity.Error, row.Where,
                    Loc.F(row.IsAction ? "validation.quest.actionUnknownQuest" : "validation.quest.conditionUnknownQuest", "quest", row.Quest),
                    "quest.unknownQuest"));
            return;
        }

        if (row.IsAction && !V.Operations.Any(o => V.Is(row.Operation, o)))
        {
            issues.Add(new(Severity.Error, row.Where, Loc.F("validation.quest.badOperation", "operation", row.Operation), "quest.badOperation"));
            return;
        }

        bool needsTask = row.IsAction ? V.TakesTask(row.Operation) : row.IsCounter;
        if (needsTask && row.Task.Length == 0)
        {
            issues.Add(new(Severity.Error, row.Where, Loc.T(row.IsAction ? "validation.quest.actionNoTask" : "validation.quest.conditionNoTask"), "quest.rowNoTask"));
            return;
        }

        QuestTaskInfo? task = null;
        if (row.Task.Length > 0)
        {
            task = QuestReferences.Task(pack.Quests, row.Vanilla, row.Quest, row.Task);
            if (task == null)
            {
                issues.Add(new(row.Vanilla ? Severity.Warning : Severity.Error, row.Where,
                    Loc.F("validation.quest.unknownTask", "quest", row.Quest, "task", row.Task), "quest.unknownTask"));
                return;
            }
        }

        if (row.IsAction)
        {
            if (V.TakesValue(row.Operation) && task != null)
            {
                if (!task.Counts)
                    issues.Add(new(Severity.Warning, row.Where,
                        Loc.F("validation.quest.notACounter", "task", Name(task)),
                        "quest.notACounter"));
                else if (task.CounterFollowsVariable)
                    issues.Add(new(Severity.Warning, row.Where,
                        Loc.F("validation.quest.counterFollowsVariable", "task", Name(task)),
                        "quest.counterFollowsVariable"));

                bool empty = row.Value.Trim().Length == 0;
                if (empty && V.Is(row.Operation, V.SetCounter))
                    issues.Add(new(Severity.Error, row.Where, Loc.T("validation.quest.valueMissing"), "quest.valueMissing"));
                else if (!empty && !IsNumberOrVariable(row.Value))
                    issues.Add(new(Severity.Error, row.Where, Loc.F("validation.quest.valueNotNumber", "value", row.Value), "quest.valueNotNumber"));
            }
            return;
        }

        if (row.IsCounter)
        {
            if (task != null && !task.Counts)
                issues.Add(new(Severity.Warning, row.Where, Loc.F("validation.quest.countAlwaysZero", "task", Name(task)), "quest.notACounter"));
            if (!V.Comparisons.Any(c => V.Is(row.Comparison, c)))
                issues.Add(new(Severity.Error, row.Where, Loc.F("validation.quest.badComparison", "comparison", row.Comparison), "quest.badComparison"));
            if (!IsNumberOrVariable(row.Value))
                issues.Add(new(Severity.Error, row.Where, Loc.F("validation.quest.conditionValueNotNumber", "value", row.Value), "quest.valueNotNumber"));
        }
        else if (!V.States.Any(s => V.Is(row.State, s)))
        {
            issues.Add(new(Severity.Error, row.Where, Loc.F("validation.quest.badState", "state", row.State), "quest.badState"));
        }
    }

    private static string Name(QuestTaskInfo task) => string.IsNullOrWhiteSpace(task.Name) ? task.Token : task.Name;

    /// <summary>A counter that follows a variable: that one is named, exists,
    /// and holds a number the counter can read.</summary>
    private static void CheckCountVariable(ModPack pack, QuestTaskDef task, string where, List<ValidationIssue> issues)
    {
        string name = (task.CountVariable ?? "").Trim();
        if (name.Length == 0)
        {
            issues.Add(new(Severity.Error, where, Loc.T("validation.quest.counterNoVariable"), "quest.counterNoVariable"));
            return;
        }

        if (task.CountVariableIsVanilla)
        {
            if (!VanillaGameVariables.Contains(name))
            {
                issues.Add(new(Severity.Warning, where, Loc.F("validation.quest.counterUnknownVanillaVariable", "name", name), "quest.counterUnknownVariable"));
                return;
            }
            var kind = VariableTypes.OfVanilla(VanillaGameVariables.TypeOf(name));
            if (kind != VariableKind.Number && kind != VariableKind.Unknown)
                issues.Add(new(Severity.Warning, where, Loc.F("validation.quest.counterNotANumber", "name", name, "kind", VariableTypes.Label(kind)), "quest.counterNotANumber"));
            return;
        }

        var declared = pack.Variables.FirstOrDefault(v => v.Name == name);
        if (declared == null)
        {
            issues.Add(new(Severity.Warning, where, Loc.F("validation.quest.counterUnknownVariable", "name", name), "quest.counterUnknownVariable"));
            return;
        }
        var packKind = VariableTypes.Of(declared.Type);
        if (packKind != VariableKind.Number)
            issues.Add(new(Severity.Warning, where, Loc.F("validation.quest.counterNotANumber", "name", name, "kind", VariableTypes.Label(packKind)), "quest.counterNotANumber"));
    }

    private static bool IsNumberOrVariable(string value)
    {
        string v = (value ?? "").Trim();
        return v.StartsWith("$", StringComparison.Ordinal)
               || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    // ── Finding every row ────────────────────────────────────────────

    private sealed record Row(string Where, bool IsAction, bool IsCounter, Dictionary<string, string> Params)
    {
        public bool Vanilla => QuestReferences.IsVanilla(Params);
        public string Quest => QuestReferences.Param(Params, V.QuestParam).Trim();
        public string Task => QuestReferences.Param(Params, V.TaskParam);
        public string Operation => Params.TryGetValue(V.OperationParam, out var o) && !string.IsNullOrEmpty(o) ? o : V.Start;
        public string State => Params.TryGetValue(V.StateParam, out var s) && !string.IsNullOrEmpty(s) ? s : V.InProgress;
        public string Comparison => Params.TryGetValue(V.ComparisonParam, out var c) && !string.IsNullOrEmpty(c) ? c : V.EqualTo;
        public string Value => QuestReferences.Param(Params, V.ValueParam);
    }

    /// <summary>
    /// Every quest action and condition in the pack, wherever it lives, with a
    /// path the issue list can jump to. Every host, including the ones the
    /// shared walk does not reach yet (UI buttons, GameObject gates): a quest
    /// started from a button is started, and reporting it as never started
    /// would be the check lying.
    /// </summary>
    private static IEnumerable<Row> Rows(ModPack pack)
    {
        var found = new List<Row>();

        void Conditions(IList<NodeConditionDef>? list, string where)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                string w = $"{where}.conditions[{i}]";
                if (NodeConditionTypes.IsGroup(c.Type)) { Conditions(c.Conditions, w); continue; }
                if (c.Type == NodeConditionTypes.QuestState || c.Type == NodeConditionTypes.QuestCounter)
                    found.Add(new Row(w, false, c.Type == NodeConditionTypes.QuestCounter, c.Params));
            }
        }

        void Actions(IList<NodeActionDef>? list, string where, string name)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null) continue;
                string w = $"{where}.{name}[{i}]";
                if (a.Type == NodeActionTypes.Quest) found.Add(new Row(w, true, false, a.Params));
                if (a.Branches != null)
                    for (int b = 0; b < a.Branches.Count; b++)
                        if (a.Branches[b].Action != null)
                            Actions(new[] { a.Branches[b].Action }, $"{w}.branch[{b}]", "action");
            }
        }

        foreach (var d in pack.Dialogues)
        {
            string dw = $"dialogues[{d.Key}]";
            Conditions(d.StartConditions, $"{dw}.start");
            foreach (var n in d.Nodes)
            {
                string nw = $"{dw}.nodes[id={n.Id}]";
                Conditions(n.Conditions, nw);
                Actions(n.ActionsOnStart, nw, "actionsOnStart");
                Actions(n.ActionsOnFinish, nw, "actionsOnFinish");
            }
        }

        foreach (var r in pack.IntegrationRules)
        {
            string rw = $"integrationRules[{r.Key}]";
            Conditions(r.Conditions, rw);
            Actions(r.Actions, rw, "actions");
            for (int b = 0; b < r.Branches.Count; b++)
            {
                Conditions(r.Branches[b].Conditions, $"{rw}.branches[{b}]");
                Actions(r.Branches[b].Actions, $"{rw}.branches[{b}]", "actions");
            }
        }

        void GameObjects(IEnumerable<GameObjectDef>? nodes, string where)
        {
            if (nodes == null) return;
            foreach (var go in nodes)
            {
                string gw = $"{where}.gameObjects[{go.Name}]";
                Conditions(go.ActiveConditions, gw);
                GameObjects(go.Children, gw);
            }
        }

        foreach (var p in pack.Places)
        {
            string pw = $"places[{p.Key}]";
            for (int h = 0; h < p.OnEnter.Count; h++)
            {
                Conditions(p.OnEnter[h].Conditions, $"{pw}.onEnter[{h}]");
                Actions(p.OnEnter[h].Actions, $"{pw}.onEnter[{h}]", "actions");
            }
            for (int h = 0; h < p.OnExit.Count; h++)
            {
                Conditions(p.OnExit[h].Conditions, $"{pw}.onExit[{h}]");
                Actions(p.OnExit[h].Actions, $"{pw}.onExit[{h}]", "actions");
            }
            for (int i = 0; i < p.NavigatorButtons.Count; i++)
                Conditions(p.NavigatorButtons[i].Conditions, $"{pw}.navigatorButtons[{i}]");
            GameObjects(p.GameObjects, pw);
        }

        for (int v = 0; v < pack.VanillaExtensions.Count; v++)
        {
            var ext = pack.VanillaExtensions[v];
            string vw = $"vanillaExtensions[{v}:{ext.Source}]";
            for (int i = 0; i < ext.NavigatorButtons.Count; i++)
                Conditions(ext.NavigatorButtons[i].Conditions, $"{vw}.navigatorButtons[{i}]");
            GameObjects(ext.GameObjects, vw);
        }

        for (int i = 0; i < pack.MapButtons.Count; i++)
            Conditions(pack.MapButtons[i].Conditions, $"mapButtons[{i}]");

        foreach (var w in pack.Wallpapers)
            Conditions(w.UnlockConditions, $"wallpapers[{w.Key}].unlock");

        void UiNodes(IEnumerable<UiNodeDef>? nodes, string where)
        {
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                string nw = $"{where}.nodes[{node.Name}]";
                Conditions(node.ActiveConditions, $"{nw}.active");
                Conditions(node.ClickConditions, $"{nw}.click");
                Actions(node.OnClick, nw, "onClick");
                UiNodes(node.Children, nw);
            }
        }

        foreach (var ui in pack.Uis)
        {
            string uw = $"uis[{ui.Id}]";
            Conditions(ui.OpenConditions, $"{uw}.open");
            UiNodes(ui.Nodes, uw);
        }

        // A quest's own lists can name quests too - a task finishing that
        // starts the next quest is the ordinary way to chain them.
        foreach (var q in pack.Quests)
        {
            string qw = $"quests[{q.Key}]";
            Conditions(q.StartConditions, $"{qw}.start");
            Conditions(q.ResetConditions, $"{qw}.reset");
            foreach (var place in q.SiteConditions)
                Conditions(place.Conditions, $"{qw}.siteConditions");
            string list = q.IsVanillaExtension ? "addedTasks" : "tasks";
            foreach (var t in q.AllTasks())
            {
                string tw = $"{qw}.{list}[{t.Key}]";
                Conditions(t.Conditions, tw);
                Conditions(t.ShowConditions, $"{tw}.show");
                Actions(t.Actions, tw, "actions");
            }

            // ...and what it hangs on the game's own tasks.
            foreach (var h in q.VanillaTasks)
            {
                Actions(h.Actions, $"{qw}.vanillaTasks[{h.Task}]", "actions");
                Conditions(h.ShowConditions, $"{qw}.vanillaTasks[{h.Task}].show");
            }
        }

        return found;
    }
}
