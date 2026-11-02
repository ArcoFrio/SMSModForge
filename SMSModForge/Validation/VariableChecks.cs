using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;

namespace SMSModForge.Validation;

/// <summary>
/// Checks that need to know every value a pack's variable can hold
/// (<see cref="VariableValues"/>), found testing a pack in the game
/// (2026-11-01). Both catch a value renamed in one place and not the other,
/// which the game takes without a word and shows as something simply missing.
/// The Play dialogue check sits with them: a name nothing answers to, missing
/// in the same silent way.
/// </summary>
internal static class VariableChecks
{
    public static void Check(ModPack pack, List<ValidationIssue> issues)
    {
        var values = VariableValues.Of(pack);
        Comparisons(pack, values, issues);
        OutfitBindings(pack, values, issues);
        PlayDialogues(pack, issues);
    }

    /// <summary>
    /// A Play dialogue action naming nothing, or something that is none of the
    /// pack's dialogues or the game's conversations: nothing plays, and the
    /// game says so only in its log. A name built from a variable is read in
    /// the game, so it is left alone.
    /// </summary>
    private static void PlayDialogues(ModPack pack, List<ValidationIssue> issues)
    {
        var dialogues = new HashSet<string>(pack.Dialogues.Where(d => !d.IsVanillaBased).Select(d => d.Key),
                                            StringComparer.OrdinalIgnoreCase);
        HashSet<string>? conversations = null;
        foreach (var (a, at) in PackWalk.ActionsAt(pack))
        {
            if (a.Type != NodeActionTypes.PlayDialogue || a.Params == null) continue;
            bool game = string.Equals(Get(a.Params, "source", "pack"), "vanilla", StringComparison.OrdinalIgnoreCase);
            string name = Get(a.Params, game ? "conversation" : "dialogue", "").Trim();
            if (VariableValues.IsBuilt(name)) continue;
            if (name.Length == 0)
            {
                issues.Add(new(Severity.Warning, at.Path, Loc.T("validation.action.playDialogue.none"), "action.playDialogueNone"));
                continue;
            }
            if (game)
            {
                conversations ??= new HashSet<string>(VanillaDialogueCatalog.All.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
                if (!conversations.Contains(name))
                    issues.Add(new(Severity.Warning, at.Path, Loc.F("validation.action.playDialogue.unknownConversation", "name", name),
                                   "action.playDialogueUnknownConversation"));
            }
            else if (!dialogues.Contains(name))
                issues.Add(new(Severity.Warning, at.Path, Loc.F("validation.action.playDialogue.unknownDialogue", "name", name),
                               "action.playDialogueUnknownDialogue"));
        }
    }

    /// <summary>
    /// A comparison with a value the pack never gives the variable: it can be
    /// true only if another mod sets it. In the pack where this was found a
    /// character's placements compared her outfit variable to <c>Default</c>
    /// and <c>Swim</c> while every action set it to <c>AnisBase</c>,
    /// <c>AnisSwim</c>... - the moment one ran, she was drawn in no room.
    /// </summary>
    private static void Comparisons(ModPack pack, VariableValues values, List<ValidationIssue> issues)
    {
        var said = new HashSet<(string, string, string)>();
        foreach (var (c, at) in PackWalk.ConditionsAt(pack))
        {
            var p = c.Params;
            if (p == null || IsGameVariable(p, "source") || IsGameVariable(p, "valueSource")) continue;
            bool list = c.Type == NodeConditionTypes.ListContains;
            bool equals = c.Type == NodeConditionTypes.VariableEquals
                          || (c.Type == NodeConditionTypes.VariableCompare && Get(p, "comparison", "equals") == "equals");
            if (!list && !equals) continue;

            string name = Get(p, list ? "list" : "name", "");
            string value = Get(p, "value", "");
            if (!values.Declared(name) || VariableValues.IsBuilt(name) || VariableValues.IsBuilt(value)) continue;
            value = VariableValues.Literal(value);
            if (values.CanBe(name, value) || !said.Add((at.Path, name, value))) continue;

            var can = values.ValuesOf(name);
            string message = list
                ? can.Count == 0
                    ? Loc.F("validation.list.neverFilled", "name", name, "value", value)
                    : Loc.F("validation.list.neverHolds", "name", name, "value", value, "values", Quoted(can))
                : Loc.F("validation.variable.neverSetTo", "name", name, "value", value, "values", Quoted(can));
            issues.Add(new(Severity.Warning, at.Path, message, list ? "list.neverHolds" : "variable.neverSetTo"));
        }
    }

    /// <summary>
    /// A line whose outfit follows a variable (<c>$Name</c>) that can hold a
    /// value naming none of the speaker's outfits: on that line the bust does
    /// not appear while it does. Empty is fine - the speaker keeps what she
    /// wears. In the pack where this was found the variable's default was
    /// <c>Default</c> against outfits <c>AnisBase</c>, <c>AnisSwim</c>: no bust
    /// on any of those lines until something set the variable.
    /// </summary>
    /// <para/>
    /// Once per dialogue and variable, at its first such line: one variable
    /// wrong is one fix, and that pack's 183 lines each saying so buried it.
    private static void OutfitBindings(ModPack pack, VariableValues values, List<ValidationIssue> issues)
    {
        foreach (var d in pack.Dialogues)
        {
            var said = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in d.Nodes)
            {
                string outfit = (n.Outfit ?? "").Trim();
                if (!outfit.StartsWith("$", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(n.Actor)) continue;
                string name = outfit.Substring(1);
                string key = name + "\n" + n.Actor.Trim();
                if (!values.Declared(name) || values.IsOpen(name) || said.Contains(key)) continue;
                var wears = OutfitsOf(pack, n.Actor);
                if (wears.Count == 0) continue;
                var not = values.ValuesOf(name).Where(v => v.Trim().Length > 0 && !wears.Contains(v.Trim())).ToList();
                if (not.Count == 0) continue;
                said.Add(key);
                issues.Add(new(Severity.Warning, $"dialogues[{d.Key}].nodes[id={n.Id}].outfit",
                    Loc.F("validation.node.outfitVariableNotOutfit", "variable", name, "values", Quoted(not),
                          "actor", NameOf(pack, n.Actor)),
                    "node.outfitVariableNotOutfit"));
            }
        }
    }

    /// <summary>The bust names a speaker can wear, as the plugin finds them:
    /// the pack's character of that key, else the game's.</summary>
    private static HashSet<string> OutfitsOf(ModPack pack, string actor)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var own = pack.Characters.FirstOrDefault(c => string.Equals(c.Key, actor, StringComparison.OrdinalIgnoreCase));
        if (own != null)
        {
            if (!string.IsNullOrWhiteSpace(own.DefaultOutfit)) names.Add(own.DefaultOutfit.Trim());
            foreach (var o in own.Outfits)
            {
                string bust = string.IsNullOrWhiteSpace(o.GameObjectName) ? o.Key : o.GameObjectName;
                if (!string.IsNullOrWhiteSpace(bust)) names.Add(bust.Trim());
            }
            return names;
        }
        foreach (var bust in Shared.VanillaCastData.Busts)
            if (string.Equals(Shared.VanillaCastData.KeyFor(Shared.VanillaCastData.SpokenName(bust.Character)), actor.Trim(),
                              StringComparison.OrdinalIgnoreCase))
                names.Add(bust.GoName);
        return names;
    }

    private static string NameOf(ModPack pack, string actor)
    {
        var own = pack.Characters.FirstOrDefault(c => string.Equals(c.Key, actor, StringComparison.OrdinalIgnoreCase));
        return own != null && !string.IsNullOrWhiteSpace(own.DisplayName) ? own.DisplayName : actor;
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(v => "'" + v + "'"));

    private static bool IsGameVariable(Dictionary<string, string> p, string key)
        => p.TryGetValue(key, out var v) && string.Equals(v, "vanilla", StringComparison.OrdinalIgnoreCase);

    private static string Get(Dictionary<string, string> p, string key, string fallback)
        => p.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;
}
