using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;

namespace SMSModForge.Services;

/// <summary>
/// Rewrites every reference to a pack variable when it's renamed, so a rename
/// is a refactor rather than a silent break. Operates on the model defs; the
/// caller is responsible for refreshing any live ViewModels.
/// <para/>
/// A variable can be referenced three ways, and all three are handled:
/// <list type="number">
///   <item><b>Typed params</b> — any action/condition param whose schema is
///   <see cref="ParamType.PackVarRef"/> or <see cref="ParamType.ListVarRef"/>.
///   Driven off the schemas rather than a hand-listed set of types, so a new
///   variable-taking action is covered the day it's added.</item>
///   <item><b>The <c>$varName</c> source syntax</b> — <c>PickRandomFromList</c>
///   accepts either a literal list or <c>$var</c>.</item>
///   <item><b>[PV:name] tokens</b> — live-substituted in dialogue text and in
///   navigator / map button labels.</item>
/// </list>
/// <para/>
/// Vanilla-sourced references are deliberately skipped: a Variable* condition
/// with <c>source=vanilla</c> names a GC2 global, not a pack variable, so
/// renaming a pack variable must not touch it even when the names collide.
/// </summary>
public static class VariableRenamer
{
    /// <summary>Rename <paramref name="oldName"/> to <paramref name="newName"/>
    /// across <paramref name="pack"/>. Returns the number of references
    /// rewritten (0 = the variable was unused).</summary>
    public static int RenameReferences(ModPack pack, string oldName, string newName)
    {
        if (pack == null || string.IsNullOrEmpty(oldName) || oldName == newName) return 0;
        int n = 0;

        foreach (var d in pack.Dialogues)
        {
            n += RenameConditions(d.StartConditions, oldName, newName);
            foreach (var node in d.Nodes)
            {
                n += RenameConditions(node.Conditions, oldName, newName);
                n += RenameActions(node.ActionsOnStart, oldName, newName);
                n += RenameActions(node.ActionsOnFinish, oldName, newName);
                n += RenameTokens(node.Text, oldName, newName, out var text);
                node.Text = text;
            }
        }

        foreach (var r in pack.IntegrationRules)
        {
            n += RenameConditions(r.Conditions, oldName, newName);
            n += RenameActions(r.Actions, oldName, newName);
            foreach (var b in r.Branches)
            {
                n += RenameConditions(b.Conditions, oldName, newName);
                n += RenameActions(b.Actions, oldName, newName);
            }
        }

        foreach (var p in pack.Places)
        {
            n += RenameHooks(p.OnEnter, oldName, newName);
            n += RenameHooks(p.OnExit, oldName, newName);
            n += RenameNavButtons(p.NavigatorButtons, oldName, newName);
        }
        foreach (var v in pack.VanillaExtensions)
            n += RenameNavButtons(v.NavigatorButtons, oldName, newName);

        foreach (var b in pack.MapButtons)
        {
            n += RenameConditions(b.Conditions, oldName, newName);
            n += RenameTokens(b.Label, oldName, newName, out var lbl);
            b.Label = lbl;
        }

        foreach (var w in pack.Wallpapers)
            n += RenameConditions(w.UnlockConditions, oldName, newName);

        return n;
    }

    /// <summary>Every place a variable is referenced, as human-readable
    /// locations. Used to tell the user what a rename will touch.</summary>
    public static List<string> FindReferences(ModPack pack, string name)
    {
        var hits = new List<string>();
        if (pack == null || string.IsNullOrEmpty(name)) return hits;

        foreach (var d in pack.Dialogues)
        {
            if (CountConditions(d.StartConditions, name) > 0) hits.Add($"Dialogue '{d.Key}' start conditions");
            foreach (var node in d.Nodes)
            {
                int c = CountConditions(node.Conditions, name)
                      + CountActions(node.ActionsOnStart, name)
                      + CountActions(node.ActionsOnFinish, name)
                      + (HasToken(node.Text, name) ? 1 : 0);
                if (c > 0) hits.Add($"Dialogue '{d.Key}' node {node.Id}");
            }
        }
        foreach (var r in pack.IntegrationRules)
        {
            int c = CountConditions(r.Conditions, name) + CountActions(r.Actions, name);
            foreach (var b in r.Branches)
                c += CountConditions(b.Conditions, name) + CountActions(b.Actions, name);
            if (c > 0) hits.Add($"Integration rule '{r.Key}'");
        }
        foreach (var p in pack.Places)
        {
            int c = p.OnEnter.Concat(p.OnExit).Sum(h => CountConditions(h.Conditions, name) + CountActions(h.Actions, name))
                  + p.NavigatorButtons.Sum(b => CountConditions(b.Conditions, name) + (HasToken(b.Label, name) ? 1 : 0));
            if (c > 0) hits.Add($"Place '{p.Key}'");
        }
        foreach (var v in pack.VanillaExtensions)
            if (v.NavigatorButtons.Sum(b => CountConditions(b.Conditions, name) + (HasToken(b.Label, name) ? 1 : 0)) > 0)
                hits.Add($"Vanilla extension '{v.Source}'");
        foreach (var b in pack.MapButtons)
            if (CountConditions(b.Conditions, name) > 0 || HasToken(b.Label, name))
                hits.Add($"Map button '{b.Label}'");
        foreach (var w in pack.Wallpapers)
            if (CountConditions(w.UnlockConditions, name) > 0)
                hits.Add($"Wallpaper '{w.Key}' unlock conditions");

        return hits;
    }

    // ── Walkers ───────────────────────────────────────────────────────────

    private static int RenameHooks(List<LevelHookDef> hooks, string o, string n)
        => hooks.Sum(h => RenameConditions(h.Conditions, o, n) + RenameActions(h.Actions, o, n));

    private static int RenameNavButtons(List<NavigatorButtonDef> buttons, string o, string n)
    {
        int c = 0;
        foreach (var b in buttons)
        {
            c += RenameConditions(b.Conditions, o, n);
            c += RenameTokens(b.Label, o, n, out var lbl);
            b.Label = lbl;
        }
        return c;
    }

    private static int RenameConditions(List<NodeConditionDef> conditions, string o, string n)
        => conditions?.Sum(c => RenameCondition(c, o, n)) ?? 0;

    private static int RenameCondition(NodeConditionDef c, string o, string n)
    {
        // Groups carry nested conditions instead of params.
        if (NodeConditionTypes.IsGroup(c.Type))
            return RenameConditions(c.Conditions, o, n);
        return RenameParams(c.Params, ConditionSchemas.For(c.Type), o, n);
    }

    private static int RenameActions(List<NodeActionDef> actions, string o, string n)
        => actions?.Sum(a => RenameParams(a.Params, ActionSchemas.For(a.Type), o, n)) ?? 0;

    /// <summary>Rewrite the variable-referencing params of one action/condition.</summary>
    private static int RenameParams(Dictionary<string, string> ps, IEnumerable<ParamSchema> schemas, string o, string n)
    {
        if (ps == null) return 0;
        // A vanilla-sourced reference names a GC2 global — not ours to rename.
        if (ps.TryGetValue("source", out var src) &&
            string.Equals(src, "vanilla", System.StringComparison.OrdinalIgnoreCase))
            return 0;

        int count = 0;
        foreach (var s in schemas)
        {
            if (!ps.TryGetValue(s.Key, out var val) || string.IsNullOrEmpty(val)) continue;
            if (s.Type == ParamType.PackVarRef || s.Type == ParamType.ListVarRef)
            {
                if (val == o) { ps[s.Key] = n; count++; }
            }
            else if (s.Type == ParamType.String && val == "$" + o)
            {
                // The '$varName' source syntax (PickRandomFromList).
                ps[s.Key] = "$" + n;
                count++;
            }
        }
        return count;
    }

    private static int CountConditions(List<NodeConditionDef> cs, string name)
        => cs?.Sum(c => CountCondition(c, name)) ?? 0;

    private static int CountCondition(NodeConditionDef c, string name)
        => NodeConditionTypes.IsGroup(c.Type)
            ? CountConditions(c.Conditions, name)
            : CountParams(c.Params, ConditionSchemas.For(c.Type), name);

    private static int CountActions(List<NodeActionDef> acts, string name)
        => acts?.Sum(a => CountParams(a.Params, ActionSchemas.For(a.Type), name)) ?? 0;

    private static int CountParams(Dictionary<string, string> ps, IEnumerable<ParamSchema> schemas, string name)
    {
        if (ps == null) return 0;
        if (ps.TryGetValue("source", out var src) &&
            string.Equals(src, "vanilla", System.StringComparison.OrdinalIgnoreCase)) return 0;
        int count = 0;
        foreach (var s in schemas)
        {
            if (!ps.TryGetValue(s.Key, out var val) || string.IsNullOrEmpty(val)) continue;
            if ((s.Type == ParamType.PackVarRef || s.Type == ParamType.ListVarRef) && val == name) count++;
            else if (s.Type == ParamType.String && val == "$" + name) count++;
        }
        return count;
    }

    // ── [PV:name] tokens ──────────────────────────────────────────────────

    private static int RenameTokens(string text, string o, string n, out string result)
    {
        result = text;
        if (string.IsNullOrEmpty(text) || text.IndexOf("[PV:", System.StringComparison.Ordinal) < 0) return 0;
        int count = 0;
        result = Regex.Replace(text, @"\[PV:([^\]]+)\]", m =>
        {
            if (m.Groups[1].Value != o) return m.Value;
            count++;
            return "[PV:" + n + "]";
        });
        return count;
    }

    private static bool HasToken(string text, string name)
        => !string.IsNullOrEmpty(text) &&
           Regex.IsMatch(text ?? "", @"\[PV:" + Regex.Escape(name) + @"\]");
}
