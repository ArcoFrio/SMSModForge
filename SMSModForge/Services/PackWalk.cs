using System;
using System.Collections.Generic;
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
/// they are not looking at.
/// </summary>
public static class PackWalk
{
    /// <summary>Every condition in the pack, groups flattened into the leaves
    /// they hold — a group carries nested conditions instead of params, so
    /// treating one as a leaf would skip everything inside it.</summary>
    public static IEnumerable<(NodeConditionDef Condition, string Where)> Conditions(ModPack? pack)
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
                yield return (c, Loc.T("walk.elsewhere"));
    }

    private static IEnumerable<(NodeConditionDef Condition, string Where)> KnownConditions(ModPack pack)
    {

        foreach (var d in pack.Dialogues)
        {
            foreach (var hit in Flatten(d.StartConditions, Loc.F("walk.dialogueStart", "name", d.Key)))
                yield return hit;

            foreach (var node in d.Nodes)
                foreach (var hit in Flatten(node.Conditions, Loc.F("walk.dialogueNode", "name", d.Key, "node", node.Id)))
                    yield return hit;
        }

        foreach (var r in pack.IntegrationRules)
        {
            foreach (var hit in Flatten(r.Conditions, Loc.F("walk.rule", "name", r.Key))) yield return hit;
            foreach (var b in r.Branches)
                foreach (var hit in Flatten(b.Conditions, Loc.F("walk.rule", "name", r.Key)))
                    yield return hit;
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var hit in Flatten(h.Conditions, Loc.F("walk.placeEnter", "name", p.Key))) yield return hit;
            foreach (var h in p.OnExit)
                foreach (var hit in Flatten(h.Conditions, Loc.F("walk.placeExit", "name", p.Key))) yield return hit;
            foreach (var b in p.NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, Loc.F("walk.placeNavigator", "name", p.Key))) yield return hit;
        }

        foreach (var v in pack.VanillaExtensions)
            foreach (var b in v.NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, Loc.F("walk.vanillaExtension", "name", v.Source)))
                    yield return hit;

        foreach (var b in pack.MapButtons)
            foreach (var hit in Flatten(b.Conditions, Loc.F("walk.mapButton", "name", b.Label))) yield return hit;

        foreach (var w in pack.Wallpapers)
            foreach (var hit in Flatten(w.UnlockConditions, Loc.F("walk.wallpaperUnlock", "name", w.Key)))
                yield return hit;

        foreach (var q in pack.Quests)
        {
            foreach (var hit in Flatten(q.StartConditions, Loc.F("walk.questStart", "name", q.Key)))
                yield return hit;
            foreach (var hit in Flatten(q.ResetConditions, Loc.F("walk.questReset", "name", q.Key)))
                yield return hit;
            foreach (var place in q.SiteConditions)
                foreach (var hit in Flatten(place.Conditions, Loc.F("walk.questPlace", "name", q.Key)))
                    yield return hit;
            foreach (var t in q.AllTasks())
            {
                foreach (var hit in Flatten(t.Conditions, Loc.F("walk.questTask", "name", q.Key, "task", t.Key)))
                    yield return hit;
                foreach (var hit in Flatten(t.ShowConditions, Loc.F("walk.questTaskShow", "name", q.Key, "task", t.Key)))
                    yield return hit;
            }
            foreach (var h in q.VanillaTasks)
                foreach (var hit in Flatten(h.ShowConditions, Loc.F("walk.questGameTaskShow", "name", q.Key, "task", h.Task)))
                    yield return hit;
        }

        // An object's own conditions - whether it is there at all - in a place
        // of the pack's or one of the game's it extends, NPCs placed in it
        // included.
        foreach (var p in pack.Places)
            foreach (var list in ObjectConditions(p.GameObjects))
                foreach (var hit in Flatten(list, Loc.F("walk.place", "name", p.Key))) yield return hit;
        foreach (var v in pack.VanillaExtensions)
            foreach (var list in ObjectConditions(v.GameObjects))
                foreach (var hit in Flatten(list, Loc.F("walk.vanillaExtension", "name", v.Source))) yield return hit;

        // A screen's: when it may open, and when each of its objects shows and
        // can be clicked.
        foreach (var ui in pack.Uis)
        {
            string where = Loc.F("walk.ui", "name", UiName(ui));
            foreach (var hit in Flatten(ui.OpenConditions, where)) yield return hit;
            foreach (var node in UiNodes(ui.Nodes))
            {
                foreach (var hit in Flatten(node.ActiveConditions, where)) yield return hit;
                foreach (var hit in Flatten(node.ClickConditions, where)) yield return hit;
            }
        }
    }

    /// <summary>
    /// Every action in the pack, including the ones inside a dice roll's
    /// branches - each of those is an action in its own right, with params of
    /// its own to follow.
    /// </summary>
    public static IEnumerable<(NodeActionDef Action, string Where)> Actions(ModPack? pack)
    {
        if (pack == null) yield break;
        var seen = new HashSet<NodeActionDef>(ReferenceEqualityComparer.Instance);
        foreach (var (action, where) in TopLevelActions(pack))
            foreach (var each in WithBranches(action))
                if (seen.Add(each)) yield return (each, where);

        // The backstop, as for conditions.
        foreach (var a in SavedObjects.All<NodeActionDef>(pack))
            if (seen.Add(a)) yield return (a, Loc.T("walk.elsewhere"));
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

    private static IEnumerable<(NodeActionDef Action, string Where)> TopLevelActions(ModPack pack)
    {

        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
            {
                string where = Loc.F("walk.dialogueNode", "name", d.Key, "node", node.Id);
                foreach (var a in node.ActionsOnStart) yield return (a, where);
                foreach (var a in node.ActionsOnFinish) yield return (a, where);
            }

        foreach (var r in pack.IntegrationRules)
        {
            string where = Loc.F("walk.rule", "name", r.Key);
            foreach (var a in r.Actions) yield return (a, where);
            foreach (var b in r.Branches)
                foreach (var a in b.Actions) yield return (a, where);
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var a in h.Actions) yield return (a, Loc.F("walk.placeEnter", "name", p.Key));
            foreach (var h in p.OnExit)
                foreach (var a in h.Actions) yield return (a, Loc.F("walk.placeExit", "name", p.Key));
        }

        foreach (var q in pack.Quests)
        {
            foreach (var t in q.AllTasks())
                foreach (var a in t.Actions) yield return (a, Loc.F("walk.questTask", "name", q.Key, "task", t.Key));

            // What a pack hangs on the game's own tasks, in a quest it extends.
            foreach (var h in q.VanillaTasks)
                foreach (var a in h.Actions) yield return (a, Loc.F("walk.questGameTask", "name", q.Key, "task", h.Task));
        }

        // What a screen's objects do when clicked.
        foreach (var ui in pack.Uis)
        {
            string where = Loc.F("walk.ui", "name", UiName(ui));
            foreach (var node in UiNodes(ui.Nodes))
                foreach (var a in node.OnClick) yield return (a, where);
        }
    }

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
    {
        if (pack == null) yield break;

        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
            {
                var n = node;
                yield return (() => n.Text, t => n.Text = t, Loc.F("walk.dialogueNode", "name", d.Key, "node", n.Id));
            }

        foreach (var p in pack.Places)
            foreach (var b in p.NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, Loc.F("walk.placeNavigator", "name", p.Key));
            }

        foreach (var v in pack.VanillaExtensions)
            foreach (var b in v.NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, Loc.F("walk.vanillaExtension", "name", v.Source));
            }

        foreach (var b in pack.MapButtons)
        {
            var button = b;
            yield return (() => button.Label, t => button.Label = t, Loc.F("walk.mapButton", "name", button.Label));
        }

        // A transition's black-screen words, wherever the transition is.
        foreach (var (action, where) in Actions(pack))
            if (action.Type == NodeActionTypes.Transitions
                && action.Params.ContainsKey(Shared.Transitions.TextParam))
            {
                var ps = action.Params;
                yield return (() => ps.TryGetValue(Shared.Transitions.TextParam, out var v) ? v : "",
                              t => ps[Shared.Transitions.TextParam] = t, where);
            }
    }

    private static IEnumerable<(NodeConditionDef, string)> Flatten(
        List<NodeConditionDef>? conditions, string where)
    {
        if (conditions == null) yield break;
        foreach (var c in conditions)
        {
            if (NodeConditionTypes.IsGroup(c.Type))
            {
                foreach (var inner in Flatten(c.Conditions, where)) yield return inner;
                continue;
            }
            yield return (c, where);
        }
    }
}
