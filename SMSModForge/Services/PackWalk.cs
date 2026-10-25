using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Localization;

namespace SMSModForge.Services;

/// <summary>
/// Every condition, action and author-written line in a pack, once, with a
/// human-readable note saying where it was found.
/// <para/>
/// This exists because two features already need to sweep the whole pack —
/// renaming a variable and renaming everything else — and a pack has more
/// hiding places than anyone remembers: a dialogue's start conditions, each
/// node's conditions and its two action lists, an integration rule's branches,
/// a place's enter and exit hooks, the navigator buttons on a place AND on a
/// vanilla extension, the map buttons, a wallpaper's unlock conditions.
/// <para/>
/// Written out twice, those two sweeps drift the first time somebody gives
/// wallpapers an action list: one feature learns about it and the other quietly
/// stops being complete, in a way nothing fails on. So it is written once.
/// <para/>
/// The <c>Where</c> is for telling an author what a rename touched, which is
/// the difference between a refactor and a silent rewrite of parts of the pack
/// they are not looking at. Each place also has a path, written the way the
/// issue list writes one, so the editor can go there - the Variables tab's
/// "where it's used" (1.8.0).
/// </summary>
public static class PackWalk
{
    /// <summary>Where something is: in words for the author, and as the path
    /// the issue list goes to things by (<c>dialogues[Key].nodes[id=3]</c>).
    /// The path is empty for a place that is no one record.</summary>
    public readonly record struct Spot(string Where, string Path);

    /// <summary>Every condition in the pack, groups flattened into the leaves
    /// they hold — a group carries nested conditions instead of params, so
    /// treating one as a leaf would skip everything inside it.</summary>
    public static IEnumerable<(NodeConditionDef Condition, string Where)> Conditions(ModPack? pack)
        => ConditionsAt(pack).Select(h => (h.Condition, h.At.Where));

    /// <summary><see cref="Conditions"/>, with a path to each.</summary>
    public static IEnumerable<(NodeConditionDef Condition, Spot At)> ConditionsAt(ModPack? pack)
    {
        if (pack == null) yield break;
        var seen = new HashSet<NodeConditionDef>(ReferenceEqualityComparer.Instance);
        foreach (var hit in KnownConditions(pack))
            if (seen.Add(hit.Condition)) yield return hit;

        // Anything a list below does not know about yet. A walk that misses a
        // place fails silently - a rename that leaves one row behind - so
        // whatever the pack saves is swept up here, and said to be somewhere
        // in the pack rather than not found.
        foreach (var c in SavedObjects.All<NodeConditionDef>(pack))
            if (!NodeConditionTypes.IsGroup(c.Type) && seen.Add(c))
                yield return (c, Elsewhere);
    }

    private static Spot Elsewhere => new(Loc.T("walk.elsewhere"), "");

    private static IEnumerable<(NodeConditionDef Condition, Spot At)> KnownConditions(ModPack pack)
    {

        foreach (var d in pack.Dialogues)
        {
            foreach (var hit in Flatten(d.StartConditions, DialogueStart(d)))
                yield return hit;

            foreach (var node in d.Nodes)
                foreach (var hit in Flatten(node.Conditions, DialogueNode(d, node)))
                    yield return hit;
        }

        foreach (var r in pack.IntegrationRules)
        {
            foreach (var hit in Flatten(r.Conditions, Rule(r))) yield return hit;
            foreach (var b in r.Branches)
                foreach (var hit in Flatten(b.Conditions, Rule(r)))
                    yield return hit;
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var hit in Flatten(h.Conditions, Place("walk.placeEnter", p))) yield return hit;
            foreach (var h in p.OnExit)
                foreach (var hit in Flatten(h.Conditions, Place("walk.placeExit", p))) yield return hit;
            foreach (var b in p.NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, Place("walk.placeNavigator", p))) yield return hit;
        }

        for (int i = 0; i < pack.VanillaExtensions.Count; i++)
            foreach (var b in pack.VanillaExtensions[i].NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, Extension(pack, i)))
                    yield return hit;

        for (int i = 0; i < pack.MapButtons.Count; i++)
            foreach (var hit in Flatten(pack.MapButtons[i].Conditions, MapButton(pack, i))) yield return hit;

        foreach (var w in pack.Wallpapers)
            foreach (var hit in Flatten(w.UnlockConditions,
                         new Spot(Loc.F("walk.wallpaperUnlock", "name", w.Key), $"wallpapers[{w.Key}]")))
                yield return hit;

        foreach (var q in pack.Quests)
        {
            foreach (var hit in Flatten(q.StartConditions, Quest("walk.questStart", q)))
                yield return hit;
            foreach (var hit in Flatten(q.ResetConditions, Quest("walk.questReset", q)))
                yield return hit;
            foreach (var place in q.SiteConditions)
                foreach (var hit in Flatten(place.Conditions, Quest("walk.questPlace", q)))
                    yield return hit;
            foreach (var t in q.AllTasks())
            {
                foreach (var hit in Flatten(t.Conditions, Task("walk.questTask", q, t)))
                    yield return hit;
                foreach (var hit in Flatten(t.ShowConditions, Task("walk.questTaskShow", q, t)))
                    yield return hit;
            }
            foreach (var h in q.VanillaTasks)
                foreach (var hit in Flatten(h.ShowConditions, GameTask("walk.questGameTaskShow", q, h)))
                    yield return hit;
        }

        // An object's own conditions - whether it is there at all - in a place
        // of the pack's or one of the game's it extends, NPCs placed in it
        // included.
        foreach (var p in pack.Places)
            foreach (var list in ObjectConditions(p.GameObjects))
                foreach (var hit in Flatten(list, Place("walk.place", p))) yield return hit;
        for (int i = 0; i < pack.VanillaExtensions.Count; i++)
            foreach (var list in ObjectConditions(pack.VanillaExtensions[i].GameObjects))
                foreach (var hit in Flatten(list, Extension(pack, i))) yield return hit;

        // A screen's: when it may open, and when each of its objects shows and
        // can be clicked.
        foreach (var ui in pack.Uis)
        {
            var at = Screen(ui);
            foreach (var hit in Flatten(ui.OpenConditions, at)) yield return hit;
            foreach (var node in UiNodes(ui.Nodes))
            {
                foreach (var hit in Flatten(node.ActiveConditions, at)) yield return hit;
                foreach (var hit in Flatten(node.ClickConditions, at)) yield return hit;
            }
        }
    }

    /// <summary>
    /// Every action in the pack, including the ones inside a dice roll's
    /// branches - each of those is an action in its own right, with params of
    /// its own to follow.
    /// </summary>
    public static IEnumerable<(NodeActionDef Action, string Where)> Actions(ModPack? pack)
        => ActionsAt(pack).Select(h => (h.Action, h.At.Where));

    /// <summary><see cref="Actions"/>, with a path to each.</summary>
    public static IEnumerable<(NodeActionDef Action, Spot At)> ActionsAt(ModPack? pack)
    {
        if (pack == null) yield break;
        var seen = new HashSet<NodeActionDef>(ReferenceEqualityComparer.Instance);
        foreach (var (action, at) in TopLevelActions(pack))
            foreach (var each in WithBranches(action))
                if (seen.Add(each)) yield return (each, at);

        // The backstop, as for conditions.
        foreach (var a in SavedObjects.All<NodeActionDef>(pack))
            if (seen.Add(a)) yield return (a, Elsewhere);
    }

    private static IEnumerable<NodeActionDef> WithBranches(NodeActionDef? action)
    {
        if (action == null) yield break;
        yield return action;
        if (action.Branches == null) yield break;
        foreach (var b in action.Branches)
            foreach (var inner in WithBranches(b.Action))
                yield return inner;
    }

    private static IEnumerable<(NodeActionDef Action, Spot At)> TopLevelActions(ModPack pack)
    {

        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
            {
                var at = DialogueNode(d, node);
                foreach (var a in node.ActionsOnStart) yield return (a, at);
                foreach (var a in node.ActionsOnFinish) yield return (a, at);
            }

        foreach (var r in pack.IntegrationRules)
        {
            var at = Rule(r);
            foreach (var a in r.Actions) yield return (a, at);
            foreach (var b in r.Branches)
                foreach (var a in b.Actions) yield return (a, at);
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var a in h.Actions) yield return (a, Place("walk.placeEnter", p));
            foreach (var h in p.OnExit)
                foreach (var a in h.Actions) yield return (a, Place("walk.placeExit", p));
        }

        foreach (var q in pack.Quests)
        {
            foreach (var t in q.AllTasks())
                foreach (var a in t.Actions) yield return (a, Task("walk.questTask", q, t));

            // What a pack hangs on the game's own tasks, in a quest it extends.
            foreach (var h in q.VanillaTasks)
                foreach (var a in h.Actions) yield return (a, GameTask("walk.questGameTask", q, h));
        }

        // What a screen's objects do when clicked.
        foreach (var ui in pack.Uis)
        {
            var at = Screen(ui);
            foreach (var node in UiNodes(ui.Nodes))
                foreach (var a in node.OnClick) yield return (a, at);
        }
    }

    // ── Places, in words and as paths ────────────────────────────────────

    private static Spot DialogueStart(DialogueDef d)
        => new(Loc.F("walk.dialogueStart", "name", d.Key), $"dialogues[{d.Key}]");

    private static Spot DialogueNode(DialogueDef d, DialogueNodeDef node)
        => new(Loc.F("walk.dialogueNode", "name", d.Key, "node", node.Id), $"dialogues[{d.Key}].nodes[id={node.Id}]");

    private static Spot Rule(UpdateRuleDef r)
        => new(Loc.F("walk.rule", "name", r.Key), $"integrationRules[{r.Key}]");

    private static Spot Place(string words, PlaceDef p)
        => new(Loc.F(words, "name", p.Key), $"places[{p.Key}]");

    private static Spot Extension(ModPack pack, int i)
        => new(Loc.F("walk.vanillaExtension", "name", pack.VanillaExtensions[i].Source),
               $"vanillaExtensions[{i}:{pack.VanillaExtensions[i].Source}]");

    private static Spot MapButton(ModPack pack, int i)
        => new(Loc.F("walk.mapButton", "name", pack.MapButtons[i].Label), $"mapButtons[{i}]");

    private static Spot Quest(string words, QuestDef q)
        => new(Loc.F(words, "name", q.Key), $"quests[{q.Key}]");

    /// <summary>A task of the pack's: in one of the game's quests the list
    /// the issue list goes to is that quest's added tasks.</summary>
    private static Spot Task(string words, QuestDef q, QuestTaskDef t)
        => new(Loc.F(words, "name", q.Key, "task", t.Key),
               $"quests[{q.Key}].{(string.IsNullOrEmpty(q.Source) ? "tasks" : "addedTasks")}[{t.Key}]");

    private static Spot GameTask(string words, QuestDef q, VanillaTaskHookDef h)
        => new(Loc.F(words, "name", q.Key, "task", h.Task), $"quests[{q.Key}].vanillaTasks[{h.Task}]");

    private static Spot Screen(UiDef ui) => new(Loc.F("walk.ui", "name", UiName(ui)), $"uis[{ui.Id}]");

    // ── Trees ─────────────────────────────────────────────────────────────

    /// <summary>Every object's own condition list in a GameObject tree: each
    /// node's, each NPC placed in it, and whatever rides on those.</summary>
    private static IEnumerable<List<NodeConditionDef>> ObjectConditions(IEnumerable<GameObjectDef>? nodes)
    {
        if (nodes == null) yield break;
        foreach (var node in nodes)
        {
            yield return node.ActiveConditions;
            foreach (var npc in node.Npcs)
            {
                yield return npc.ActiveConditions;
                foreach (var list in ObjectConditions(npc.Children)) yield return list;
            }
            foreach (var list in ObjectConditions(node.Children)) yield return list;
        }
    }

    private static IEnumerable<UiNodeDef> UiNodes(IEnumerable<UiNodeDef>? nodes)
    {
        if (nodes == null) yield break;
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in UiNodes(node.Children)) yield return child;
        }
    }

    /// <summary>What a screen is called where the author would look for it:
    /// its name, or the game's screen it is built on.</summary>
    private static string UiName(UiDef ui)
        => !string.IsNullOrWhiteSpace(ui.Name) ? ui.Name
         : !string.IsNullOrWhiteSpace(ui.Source) ? ui.Source
         : ui.Id;

    /// <summary>
    /// Every line of author-written text that carries substitution tokens, as a
    /// read/write pair.
    /// <para/>
    /// A pair rather than a string because these are the only things in the
    /// walk that cannot be edited in place: a condition is an object the caller
    /// can change, a line of dialogue is a string property that has to be
    /// assigned back.
    /// </summary>
    public static IEnumerable<(Func<string> Read, Action<string> Write, string Where)> Texts(ModPack? pack)
        => TextsAt(pack).Select(t => (t.Read, t.Write, t.At.Where));

    /// <summary><see cref="Texts"/>, with a path to each.</summary>
    public static IEnumerable<(Func<string> Read, Action<string> Write, Spot At)> TextsAt(ModPack? pack)
    {
        if (pack == null) yield break;

        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
            {
                var n = node;
                yield return (() => n.Text, t => n.Text = t, DialogueNode(d, n));
            }

        foreach (var p in pack.Places)
            foreach (var b in p.NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, Place("walk.placeNavigator", p));
            }

        for (int i = 0; i < pack.VanillaExtensions.Count; i++)
            foreach (var b in pack.VanillaExtensions[i].NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, Extension(pack, i));
            }

        for (int i = 0; i < pack.MapButtons.Count; i++)
        {
            var button = pack.MapButtons[i];
            yield return (() => button.Label, t => button.Label = t, MapButton(pack, i));
        }

        // A transition's black-screen words, wherever the transition is.
        foreach (var (action, at) in ActionsAt(pack))
            if (action.Type == NodeActionTypes.Transitions
                && action.Params.ContainsKey(Shared.Transitions.TextParam))
            {
                var ps = action.Params;
                yield return (() => ps.TryGetValue(Shared.Transitions.TextParam, out var v) ? v : "",
                              t => ps[Shared.Transitions.TextParam] = t, at);
            }
    }

    private static IEnumerable<(NodeConditionDef, Spot)> Flatten(
        List<NodeConditionDef>? conditions, Spot at)
    {
        if (conditions == null) yield break;
        foreach (var c in conditions)
        {
            if (NodeConditionTypes.IsGroup(c.Type))
            {
                foreach (var inner in Flatten(c.Conditions, at)) yield return inner;
                continue;
            }
            yield return (c, at);
        }
    }
}
