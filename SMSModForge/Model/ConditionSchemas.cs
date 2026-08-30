using System;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Per-condition-type parameter schemas. Mirrors <see cref="ActionSchemas"/> —
/// see that class for the design rationale (editor-side metadata only, runtime
/// reads params by string key regardless).
/// </summary>
public static class ConditionSchemas
{
    /// <summary>Sentinel for unknown / parameter-less condition types.</summary>
    public static readonly ParamSchema[] Empty = Array.Empty<ParamSchema>();

    /// <summary>Lookup from <see cref="NodeConditionDef.Type"/> to its schema.</summary>
    public static readonly Dictionary<string, ParamSchema[]> ByType = new()
    {
        // ── Pack-variable comparisons ─────────────────────────────────
        [NodeConditionTypes.VariableEquals] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "",
                "Pack variable to read."),
            new ParamSchema("value", "Value", ParamType.String, "",
                "Compared as a string (booleans use 'true' / 'false', numbers use the " +
                "invariant-culture decimal form)."),
        },
        [NodeConditionTypes.VariableGreaterThan] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Passes when the pack variable's numeric value is strictly greater."),
        },
        [NodeConditionTypes.VariableGreaterOrEqual] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Passes when the pack variable's numeric value is >= this."),
        },
        [NodeConditionTypes.VariableLessThan] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Passes when the pack variable's numeric value is strictly less."),
        },
        [NodeConditionTypes.VariableLessOrEqual] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Passes when the pack variable's numeric value is <= this."),
        },
        [NodeConditionTypes.VariableExists] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "",
                "Passes when any value has been written to this pack variable."),
        },

        // ── GC2 global comparisons ────────────────────────────────────
        [NodeConditionTypes.GameVariableEquals] = new[]
        {
            new ParamSchema("name", "GC2 global", ParamType.GameVarRef, "",
                "GC2 GlobalNameVariable. Vanilla globals (PC, etc.) work via the " +
                "GNV_ALIASES map."),
            new ParamSchema("value", "Value", ParamType.String, "",
                "Compared as a string."),
        },
        [NodeConditionTypes.GameVariableNumberGreaterThan] = new[]
        {
            new ParamSchema("name", "GC2 global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Strictly greater (>)."),
        },
        [NodeConditionTypes.GameVariableNumberGreaterOrEqual] = new[]
        {
            new ParamSchema("name", "GC2 global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Greater or equal (>=)."),
        },
        [NodeConditionTypes.GameVariableNumberLessThan] = new[]
        {
            new ParamSchema("name", "GC2 global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Strictly less (<)."),
        },
        [NodeConditionTypes.GameVariableNumberLessOrEqual] = new[]
        {
            new ParamSchema("name", "GC2 global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "Threshold", ParamType.Float, "0",
                "Less or equal (<=)."),
        },

        // ── Scene-graph state ─────────────────────────────────────────
        [NodeConditionTypes.LevelActive] = new[]
        {
            new ParamSchema("level", "Level", ParamType.LevelRef, "",
                "Level token (vanilla:<go> or place:<key>). Passes when the matching " +
                "5_Levels child is currently active in the scene."),
        },
        // Targeting (kind / target / overlayLevel) comes from the shared
        // category row — the same one SetGameObjectActive uses, since the
        // condition asks about exactly what that action sets. Only the key
        // the row does not draw itself is declared here; the legacy 'path'
        // spelling is migrated to 'target' by NodeConditionViewModel and
        // still read by the runtime.
        [NodeConditionTypes.GameObjectActive] = new[]
        {
            new ParamSchema("target", "Target", ParamType.GameObjectPath, "",
                "The object to test. Category decides how it resolves: a bust, an overlay " +
                "inside a chosen level, a scene, or a raw GameObject name or hierarchy " +
                "path. Passes while the resolved object is active in the hierarchy — one " +
                "that cannot be found reads the same as one switched off, so Negate says " +
                "\"off or absent\" rather than \"off\"."),
        },

        // ── Misc ──────────────────────────────────────────────────────
        // Deprecated — no longer offered in the Type combo, but the schema
        // stays so packs authored before DailyChance still render/edit.
        [NodeConditionTypes.Random] = new[]
        {
            new ParamSchema("chance", "Chance", ParamType.Float, "0.5",
                "DEPRECATED — re-rolls on EVERY evaluation. In a per-frame context " +
                "(integration rule, dialogue start conditions, button visibility) that " +
                "means ~60 rolls/second and the probability is meaningless. Switch to " +
                "'DailyChance' (once per day) or a LevelRandom variable + comparison."),
        },
        [NodeConditionTypes.DailyChance] = new[]
        {
            new ParamSchema("chance", "Chance", ParamType.Percent, "50",
                "Probability of passing, as a whole percentage. 50 = passes on ~half of " +
                "in-game days. Rolled once per in-game day: the result holds all day, " +
                "survives save/reload, and changes at each day roll-over. Every " +
                "DailyChance rolls independently — the console names each one after its " +
                "dialogue/rule at the day change."),
        },
        [NodeConditionTypes.VariableStartsWith] = new[]
        {
            new ParamSchema("name", "Variable", ParamType.PackVarRef, "",
                "Variable whose string value is tested."),
            new ParamSchema("value", "Starts with", ParamType.String, "",
                "Prefix to test for. Check Negate for \"doesn't start with\". An empty " +
                "prefix never passes — it would otherwise match everything."),
            new ParamSchema("source", "Source", ParamType.Choice, "pack",
                "Which store to read: this pack's variables, or the vanilla GC2 globals.",
                new[] { "pack", "vanilla" }),
            new ParamSchema("ignoreCase", "Ignore case", ParamType.Bool, "false",
                "Compare case-insensitively. Off by default, matching every other " +
                "variable comparison."),
        },

        // ── Lists ─────────────────────────────────────────────────────
        [NodeConditionTypes.ListContains] = new[]
        {
            new ParamSchema("list", "List", ParamType.ListVarRef, "",
                "List-typed pack variable to search."),
            new ParamSchema("value", "Contains", ParamType.String, "",
                "Entry to look for — an exact, case-sensitive match. Check Negate for " +
                "\"doesn't contain\", which is how you express a no-share constraint " +
                "against an occupancy list."),
        },
        [NodeConditionTypes.ListCount] = new[]
        {
            new ParamSchema("list", "List", ParamType.ListVarRef, "",
                "List-typed pack variable to count."),
            new ParamSchema("comparison", "Count is", ParamType.Choice, "equals",
                "How the entry count is compared against the value below.",
                new[] { "equals", "greater than", "greater or equal", "less than", "less or equal" }),
            new ParamSchema("value", "Value", ParamType.Int, "0",
                "Number to compare the entry count against. 'equals 0' is the empty " +
                "check; negate it for \"has anything in it\"."),
        },

        [NodeConditionTypes.Timer] = new[]
        {
            new ParamSchema("seconds", "Wait (s)", ParamType.Float, "30",
                "Real seconds the rule waits between fires. Ignored when 'Randomize' " +
                "is checked. The gate starts elapsed, so the rule fires once right " +
                "away and then every interval after.",
                enabledWhen: "randomize", enabledWhenValue: "false"),
            new ParamSchema("randomize", "Randomize", ParamType.Bool, "false",
                "Roll a fresh wait in the Min..Max range after every fire, instead of " +
                "using the fixed 'Wait' value. Use this for wandering / roaming so " +
                "characters don't move in lockstep."),
            new ParamSchema("minSeconds", "Min (s)", ParamType.Float, "15",
                "Shortest wait, in real seconds. Only used when 'Randomize' is checked.",
                enabledWhen: "randomize"),
            new ParamSchema("maxSeconds", "Max (s)", ParamType.Float, "45",
                "Longest wait, in real seconds. Only used when 'Randomize' is checked. " +
                "Must be >= Min.",
                enabledWhen: "randomize"),
            new ParamSchema("stagger", "Stagger start", ParamType.Bool, "false",
                "Wait a full interval before the FIRST fire instead of firing immediately. " +
                "Use it when several rules would otherwise start together — one per " +
                "character, say — so they don't all act on the same frame. Later waits are " +
                "unaffected: each rule already re-rolls its own interval independently."),
        },
        [NodeConditionTypes.AlwaysTrue] = Empty,
        [NodeConditionTypes.Weather] = new[]
        {
            new ParamSchema("state", "Weather is", ParamType.Choice, "BadWeather",
                "Raining / Snowing read the vanilla rainy-day / snowy-day game variables; " +
                "BadWeather passes on either. Check Negate for \"clear weather\".",
                new[] { "Raining", "Snowing", "BadWeather" }),
        },
    };

    /// <summary>Returns the schema for <paramref name="conditionType"/>, or
    /// <see cref="Empty"/> when unknown.</summary>
    public static ParamSchema[] For(string conditionType)
        => conditionType != null && ByType.TryGetValue(conditionType, out var schema)
            ? schema
            : Empty;
}
