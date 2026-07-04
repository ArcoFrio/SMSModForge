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
        [NodeConditionTypes.GameObjectActive] = new[]
        {
            new ParamSchema("path", "GO path", ParamType.GameObjectPath, "",
                "Scene-graph path; passes when the resolved GO has activeSelf == true."),
        },

        // ── Misc ──────────────────────────────────────────────────────
        [NodeConditionTypes.Random] = new[]
        {
            new ParamSchema("chance", "Chance", ParamType.Float, "0.5",
                "Probability of passing (0..1). 0.5 = 50% chance."),
        },
        [NodeConditionTypes.AlwaysTrue] = Empty,
    };

    /// <summary>Returns the schema for <paramref name="conditionType"/>, or
    /// <see cref="Empty"/> when unknown.</summary>
    public static ParamSchema[] For(string conditionType)
        => conditionType != null && ByType.TryGetValue(conditionType, out var schema)
            ? schema
            : Empty;
}
