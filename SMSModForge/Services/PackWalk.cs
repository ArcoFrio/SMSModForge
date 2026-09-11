using System;
using System.Collections.Generic;
using SMSModForge.Model;

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

        foreach (var d in pack.Dialogues)
        {
            foreach (var hit in Flatten(d.StartConditions, $"Dialogue '{d.Key}' start conditions"))
                yield return hit;

            foreach (var node in d.Nodes)
                foreach (var hit in Flatten(node.Conditions, $"Dialogue '{d.Key}' node {node.Id}"))
                    yield return hit;
        }

        foreach (var r in pack.IntegrationRules)
        {
            foreach (var hit in Flatten(r.Conditions, $"Integration rule '{r.Key}'")) yield return hit;
            foreach (var b in r.Branches)
                foreach (var hit in Flatten(b.Conditions, $"Integration rule '{r.Key}'"))
                    yield return hit;
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var hit in Flatten(h.Conditions, $"Place '{p.Key}' on enter")) yield return hit;
            foreach (var h in p.OnExit)
                foreach (var hit in Flatten(h.Conditions, $"Place '{p.Key}' on exit")) yield return hit;
            foreach (var b in p.NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, $"Place '{p.Key}' navigator")) yield return hit;
        }

        foreach (var v in pack.VanillaExtensions)
            foreach (var b in v.NavigatorButtons)
                foreach (var hit in Flatten(b.Conditions, $"Vanilla extension '{v.Source}'"))
                    yield return hit;

        foreach (var b in pack.MapButtons)
            foreach (var hit in Flatten(b.Conditions, $"Map button '{b.Label}'")) yield return hit;

        foreach (var w in pack.Wallpapers)
            foreach (var hit in Flatten(w.UnlockConditions, $"Wallpaper '{w.Key}' unlock conditions"))
                yield return hit;
    }

    /// <summary>Every action in the pack.</summary>
    public static IEnumerable<(NodeActionDef Action, string Where)> Actions(ModPack? pack)
    {
        if (pack == null) yield break;

        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
            {
                string where = $"Dialogue '{d.Key}' node {node.Id}";
                foreach (var a in node.ActionsOnStart) yield return (a, where);
                foreach (var a in node.ActionsOnFinish) yield return (a, where);
            }

        foreach (var r in pack.IntegrationRules)
        {
            string where = $"Integration rule '{r.Key}'";
            foreach (var a in r.Actions) yield return (a, where);
            foreach (var b in r.Branches)
                foreach (var a in b.Actions) yield return (a, where);
        }

        foreach (var p in pack.Places)
        {
            foreach (var h in p.OnEnter)
                foreach (var a in h.Actions) yield return (a, $"Place '{p.Key}' on enter");
            foreach (var h in p.OnExit)
                foreach (var a in h.Actions) yield return (a, $"Place '{p.Key}' on exit");
        }
    }

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
                yield return (() => n.Text, t => n.Text = t, $"Dialogue '{d.Key}' node {n.Id}");
            }

        foreach (var p in pack.Places)
            foreach (var b in p.NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, $"Place '{p.Key}' navigator");
            }

        foreach (var v in pack.VanillaExtensions)
            foreach (var b in v.NavigatorButtons)
            {
                var button = b;
                yield return (() => button.Label, t => button.Label = t, $"Vanilla extension '{v.Source}'");
            }

        foreach (var b in pack.MapButtons)
        {
            var button = b;
            yield return (() => button.Label, t => button.Label = t, $"Map button '{button.Label}'");
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
