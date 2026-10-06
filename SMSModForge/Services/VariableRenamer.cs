using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Localization;

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
///   <item><b>[PV:name] tokens</b> — in every text a player reads: dialogue
///   lines, navigator and map button labels, and (through the list of the
///   pack's texts, <see cref="Translation.LanguageSession.Slots"/>) character
///   names, quest titles, descriptions and tasks, and the texts on screens -
///   where the game plugin fills them in too since 2026-09-27.</item>
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

        // Every condition and action, wherever it lives: the walk is shared
        // with every other rename, so a place one learns about the other does
        // too. Written out here it had missed dice branches, screens' buttons
        // and objects' own conditions.
        foreach (var (c, _) in PackWalk.Conditions(pack))
            n += RenameParams(c.Params, ConditionSchemas.For(c.Type), oldName, newName);
        foreach (var (a, _) in PackWalk.Actions(pack))
            n += RenameParams(a.Params, ActionSchemas.For(a.Type), oldName, newName);

        // Dialogue lines and button labels.
        foreach (var (read, write, _) in PackWalk.Texts(pack))
        {
            int c = RenameTokens(read(), oldName, newName, out var text);
            if (c > 0) write(text);
            n += c;
        }

        // A counter following one of the pack's own variables. A game variable
        // of the same name is a different variable.
        foreach (var q in pack.Quests)
            foreach (var t in q.AllTasks())
                if (t.CountsFromVariable && !t.CountVariableIsVanilla && t.CountVariable == oldName)
                {
                    t.CountVariable = newName;
                    n++;
                }

        // Every other text a player reads. [PV:name] is filled in in all of
        // them now, so a rename that left one behind would leave it naming a
        // variable that no longer exists - read as nothing, the token gone.
        foreach (var slot in OtherTexts(pack))
        {
            string before = slot.Get() ?? "";
            int c = RenameTokens(before, oldName, newName, out var after);
            if (c > 0) slot.Set(after);
            n += c;
        }

        return n;
    }

    /// <summary>The texts a player reads that the walks above do not reach:
    /// everything but dialogue lines and button labels, which they do.</summary>
    private static IEnumerable<Translation.LanguageSession.Slot> OtherTexts(ModPack pack)
        => Translation.LanguageSession.Slots(pack, out _)
            .Where(s => s.Kind != Shared.PackTexts.Kind.Line
                        && s.Kind != Shared.PackTexts.Kind.NavigatorLabel
                        && s.Kind != Shared.PackTexts.Kind.MapLabel
                        && s.Kind != Shared.PackTexts.Kind.TransitionText);

    /// <summary>Where one of <see cref="OtherTexts"/> is, said the way the
    /// rest of the list says it.</summary>
    private static string WhereIs(ModPack pack, Translation.LanguageSession.Slot slot)
    {
        switch (slot.Kind)
        {
            case Shared.PackTexts.Kind.CharacterName:
                return Loc.F("walk.character", "name", slot.Owner);
            case Shared.PackTexts.Kind.TaskName:
            case Shared.PackTexts.Kind.TaskQuestDescription:
                return Loc.F("walk.questTask", "name", slot.Owner, "task", slot.Detail);
            case Shared.PackTexts.Kind.GameTaskQuestDescription:
                return Loc.F("walk.questGameTask", "name", slot.Owner, "task", slot.Detail);
            case Shared.PackTexts.Kind.UiText:
                string name = pack.Uis.FirstOrDefault(u => u.Id == slot.Owner)?.Name;
                return Loc.F("walk.ui", "name", string.IsNullOrEmpty(name) ? slot.Owner : name);
            default:
                return Loc.F("walk.quest", "name", slot.Owner);
        }
    }

    /// <summary>Every place a variable is referenced, as human-readable
    /// locations. Used to tell the user what a rename will touch.</summary>
    public static List<string> FindReferences(ModPack pack, string name)
    {
        var hits = new List<string>();
        if (pack == null || string.IsNullOrEmpty(name)) return hits;

        void Hit(string where) { if (!hits.Contains(where)) hits.Add(where); }

        foreach (var (c, where) in PackWalk.Conditions(pack))
            if (CountParams(c.Params, ConditionSchemas.For(c.Type), name) > 0) Hit(where);
        foreach (var (a, where) in PackWalk.Actions(pack))
            if (CountParams(a.Params, ActionSchemas.For(a.Type), name) > 0) Hit(where);
        foreach (var (read, _, where) in PackWalk.Texts(pack))
            if (HasToken(read(), name)) Hit(where);
        foreach (var q in pack.Quests)
            if (q.AllTasks().Any(t => t.CountsFromVariable && !t.CountVariableIsVanilla && t.CountVariable == name))
                Hit(Loc.F("walk.quest", "name", q.Key));

        foreach (var slot in OtherTexts(pack))
        {
            if (!HasToken(slot.Get(), name)) continue;
            string where = WhereIs(pack, slot);
            if (!hits.Contains(where)) hits.Add(where);
        }

        return hits;
    }

    // ── Walkers ───────────────────────────────────────────────────────────

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
