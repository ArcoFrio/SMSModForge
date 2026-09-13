using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMSModForge.Model;
using V = SMSModForge.Shared.QuestVocabulary;

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
                issues.Add(new(Severity.Error, where, "A quest has no runtime name, so nothing can start it and the game has nowhere to save it.", "quest.noKey"));
                continue;
            }
            if (!seen.Add(quest.Key))
                issues.Add(new(Severity.Error, where, $"Two quests are called '{quest.Key}'. They would share one save slot in the game, so the second is left out.", "quest.duplicateKey"));

            if (string.IsNullOrWhiteSpace(VanillaQuests.StripTags(quest.Title)))
                issues.Add(new(Severity.Warning, $"{where}.title", "This quest has no title, so the journal lists it as a blank row.", "quest.noTitle"));

            if (quest.Tasks.Count == 0)
            {
                issues.Add(new(Severity.Warning, $"{where}.tasks", "This quest has no tasks. It can be started, but it has nothing to show under it and can never be completed.", "quest.noTasks"));
            }

            var taskKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in quest.AllTasks())
            {
                string tw = $"{where}.tasks[{task.Key}]";
                if (string.IsNullOrWhiteSpace(task.Key))
                    issues.Add(new(Severity.Error, tw, "A task has no runtime name. The game saves a task's progress under it.", "quest.taskNoKey"));
                else if (!taskKeys.Add(task.Key))
                    issues.Add(new(Severity.Error, tw, $"Two tasks in this quest are called '{task.Key}'. The game would mix up their progress, so the whole quest is left out until one is renamed.", "quest.duplicateTaskKey"));

                if (string.IsNullOrWhiteSpace(task.Name))
                    issues.Add(new(Severity.Warning, $"{tw}.name", "This task has no text, so the journal shows an empty line for it.", "quest.taskNoText"));

                if (task.CountsFromVariable)
                    CheckCountVariable(pack, task, $"{tw}.countVariable", issues);

                if (task.Subtasks.Count > 0 && !V.Completions.Any(c => V.Is(task.Completion, c)))
                    issues.Add(new(Severity.Error, $"{tw}.completion", $"'{task.Completion}' is not a way a task can complete. It will run in order.", "quest.badCompletion"));
            }

            // What would stop the quest ever finishing. Only reported for a
            // quest something starts: an unstarted quest has one problem, and
            // listing its tasks as well buries it.
            foreach (var task in quest.AllTasks())
                if (task.Subtasks.Count > 0 && task.Conditions.Count + task.Actions.Count > 0)
                    issues.Add(new(Severity.Warning, $"{where}.tasks[{task.Key}]",
                        "This task has subtasks, so its own completion conditions and actions are not used. Move them to its subtasks, or remove them.",
                        "quest.headerHasCompletion"));

            bool started = quest.StartConditions.Count > 0
                || rows.Any(r => r.IsAction && !r.Vanilla && r.Quest == quest.Key && V.Is(r.Operation, V.Start));
            if (!started)
            {
                issues.Add(new(Severity.Warning, where, "Nothing starts this quest, so it can never be completed. Give it start conditions, or start it with a Quest action from a dialogue node, a rule or a button.", "quest.neverStarted"));
                continue;
            }

            // Said once for the quest as a whole, because that is the question an
            // author asks - "can a player finish this?" - and the per-task
            // warnings below answer a different one, which task to fix. Top-level
            // tasks run in order, so the first one nothing can finish is where
            // every player's copy of the quest stops.
            var stuck = quest.Tasks.FirstOrDefault(t => !Finishable(quest, t, null, rows));
            if (stuck != null)
                issues.Add(new(Severity.Warning, where,
                    $"This quest can never be completed: it stops at '{(string.IsNullOrWhiteSpace(stuck.Name) ? stuck.Key : stuck.Name)}', " +
                    "which nothing finishes. The tasks that need something are listed separately.",
                    "quest.cannotComplete"));

            foreach (var top in quest.Tasks)
                ReportUnfinishable(quest, top, null, rows, where, issues);

            foreach (var task in quest.AllTasks())
                if (task.CountTo is > 0 && !quest.Tasks.Contains(task))
                    issues.Add(new(Severity.Warning, $"{where}.tasks[{task.Key}].countTo",
                        "The journal only draws a count beside a top-level task. This one counts, but the player will not see the number.",
                        "quest.hiddenCounter"));
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
    private static bool Finishable(QuestDef quest, QuestTaskDef task, string? parentCompletion, List<Row> rows)
    {
        bool acted = rows.Any(r => r.IsAction && !r.Vanilla && r.Quest == quest.Key && r.Task == task.Key
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
            ? task.Subtasks.Any(t => Finishable(quest, t, task.Completion, rows))
            : task.Subtasks.All(t => Finishable(quest, t, task.Completion, rows));
    }

    /// <summary>
    /// Say where a quest stops, once, at the task that stops it: the task
    /// itself when an action has to finish it, the parent when it is an "any
    /// one" whose subtasks all stall, and otherwise each subtask that does.
    /// Subtasks under a task that completes by action are never started, so
    /// they are notes and nothing is expected of them.
    /// </summary>
    private static void ReportUnfinishable(QuestDef quest, QuestTaskDef task, string? parentCompletion,
                                           List<Row> rows, string where, List<ValidationIssue> issues)
    {
        if (Finishable(quest, task, parentCompletion, rows)) return;
        string tw = $"{where}.tasks[{task.Key}]";

        if (task.Subtasks.Count == 0 || V.Is(task.Completion, V.ByAction))
        {
            issues.Add(new(Severity.Warning, tw,
                task.CountTo is > 0
                    ? "Nothing completes or counts this task, so the quest stops here. Give it completion conditions, or count it with a Quest action."
                    : "Nothing completes this task, so the quest stops here. Give it completion conditions, or complete it with a Quest action.",
                "quest.taskNeverCompleted"));
            return;
        }

        if (V.Is(task.Completion, V.AnyOne))
        {
            issues.Add(new(Severity.Warning, tw,
                "This task completes when any one of its subtasks does, and nothing completes any of them, so the quest stops here.",
                "quest.taskNeverCompleted"));
            return;
        }

        foreach (var sub in task.Subtasks)
            ReportUnfinishable(quest, sub, task.Completion, rows, where, issues);
    }

    // ── Rows that name a quest ───────────────────────────────────────

    private static void CheckRow(ModPack pack, Row row, List<ValidationIssue> issues)
    {
        string what = row.IsAction ? "This Quest action" : "This quest condition";

        if (row.Quest.Length == 0)
        {
            issues.Add(new(Severity.Error, row.Where, $"{what} names no quest.", "quest.rowNoQuest"));
            return;
        }

        if (!QuestReferences.QuestExists(pack.Quests, row.Vanilla, row.Quest))
        {
            if (row.Vanilla)
                issues.Add(new(Severity.Warning, row.Where,
                    $"'{row.Quest}' is not one of the game's quests that ModForge knows. If it is spelled exactly as the game names it, it will still work.",
                    "quest.unknownVanillaQuest"));
            else
                issues.Add(new(Severity.Error, row.Where,
                    $"There is no quest called '{row.Quest}' on the Quests tab, so {what.ToLowerInvariant()} does nothing.",
                    "quest.unknownQuest"));
            return;
        }

        if (row.IsAction && !V.Operations.Any(o => V.Is(row.Operation, o)))
        {
            issues.Add(new(Severity.Error, row.Where, $"'{row.Operation}' is not something a Quest action can do.", "quest.badOperation"));
            return;
        }

        bool needsTask = row.IsAction ? V.TakesTask(row.Operation) : row.IsCounter;
        if (needsTask && row.Task.Length == 0)
        {
            issues.Add(new(Severity.Error, row.Where, $"{what} needs a task and names none.", "quest.rowNoTask"));
            return;
        }

        QuestTaskInfo? task = null;
        if (row.Task.Length > 0)
        {
            task = QuestReferences.Task(pack.Quests, row.Vanilla, row.Quest, row.Task);
            if (task == null)
            {
                issues.Add(new(row.Vanilla ? Severity.Warning : Severity.Error, row.Where,
                    $"'{row.Quest}' has no task '{row.Task}'.", "quest.unknownTask"));
                return;
            }
        }

        if (row.IsAction)
        {
            if (V.TakesValue(row.Operation) && task != null)
            {
                if (!task.Counts)
                    issues.Add(new(Severity.Warning, row.Where,
                        $"'{Name(task)}' does not count, so setting its counter changes nothing the player can see and never completes it.",
                        "quest.notACounter"));
                else if (task.CounterFollowsVariable)
                    issues.Add(new(Severity.Warning, row.Where,
                        $"'{Name(task)}' counts from a variable, which overwrites whatever an action sets.",
                        "quest.counterFollowsVariable"));

                bool empty = row.Value.Trim().Length == 0;
                if (empty && V.Is(row.Operation, V.SetCounter))
                    issues.Add(new(Severity.Error, row.Where, "'set counter' needs a number.", "quest.valueMissing"));
                else if (!empty && !IsNumberOrVariable(row.Value))
                    issues.Add(new(Severity.Error, row.Where, $"'{row.Value}' is not a number or a $variable.", "quest.valueNotNumber"));
            }
            return;
        }

        if (row.IsCounter)
        {
            if (task != null && !task.Counts)
                issues.Add(new(Severity.Warning, row.Where, $"'{Name(task)}' does not count, so its count is always 0.", "quest.notACounter"));
            if (!V.Comparisons.Any(c => V.Is(row.Comparison, c)))
                issues.Add(new(Severity.Error, row.Where, $"'{row.Comparison}' is not a comparison. The condition is never met.", "quest.badComparison"));
            if (!IsNumberOrVariable(row.Value))
                issues.Add(new(Severity.Error, row.Where, $"'{row.Value}' is not a number or a $variable. The condition is never met.", "quest.valueNotNumber"));
        }
        else if (!V.States.Any(s => V.Is(row.State, s)))
        {
            issues.Add(new(Severity.Error, row.Where, $"'{row.State}' is not a state a quest can be in. The condition is never met.", "quest.badState"));
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
            issues.Add(new(Severity.Error, where, "This task counts from a variable, but no variable is chosen, so its count never moves.", "quest.counterNoVariable"));
            return;
        }

        if (task.CountVariableIsVanilla)
        {
            if (!VanillaGameVariables.Contains(name))
            {
                issues.Add(new(Severity.Warning, where, $"'{name}' isn't one of the game's variables that ModForge knows. If it is spelled exactly as the game names it, it will still work.", "quest.counterUnknownVariable"));
                return;
            }
            var kind = VariableTypes.OfVanilla(VanillaGameVariables.TypeOf(name));
            if (kind != VariableKind.Number && kind != VariableKind.Unknown)
                issues.Add(new(Severity.Warning, where, $"'{name}' holds {VariableTypes.Label(kind)}, not a number, so the count never moves.", "quest.counterNotANumber"));
            return;
        }

        var declared = pack.Variables.FirstOrDefault(v => v.Name == name);
        if (declared == null)
        {
            issues.Add(new(Severity.Warning, where, $"'{name}' isn't declared on the Variables tab, so the count never moves.", "quest.counterUnknownVariable"));
            return;
        }
        var packKind = VariableTypes.Of(declared.Type);
        if (packKind != VariableKind.Number)
            issues.Add(new(Severity.Warning, where, $"'{name}' holds {VariableTypes.Label(packKind)}, not a number, so the count never moves.", "quest.counterNotANumber"));
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
            foreach (var t in q.AllTasks())
            {
                string tw = $"{qw}.tasks[{t.Key}]";
                Conditions(t.Conditions, tw);
                Actions(t.Actions, tw, "actions");
            }
        }

        return found;
    }
}
