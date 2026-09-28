using System;
using System.Collections.Generic;
using SMSModForge.Localization;

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
        [NodeConditionTypes.VariableCompare] = new[]
        {
            new ParamSchema("source", "param.label.source", ParamType.Choice, "pack",
                "condition.variableCompare.source.tip"),
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "condition.variableCompare.name.tip"),
            new ParamSchema("comparison", "param.label.comparison", ParamType.Choice, "equals",
                "condition.variableCompare.comparison.tip"),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "condition.variableCompare.value.tip"),
        },
        [NodeConditionTypes.VariableEquals] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "condition.variableEquals.name.tip"),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "condition.variableEquals.value.tip"),
        },
        [NodeConditionTypes.VariableGreaterThan] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.variableGreaterThan.value.tip"),
        },
        [NodeConditionTypes.VariableGreaterOrEqual] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.variableGreaterOrEqual.value.tip"),
        },
        [NodeConditionTypes.VariableLessThan] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.variableLessThan.value.tip"),
        },
        [NodeConditionTypes.VariableLessOrEqual] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.variableLessOrEqual.value.tip"),
        },
        [NodeConditionTypes.VariableExists] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "condition.variableExists.name.tip"),
        },

        // ── GC2 global comparisons ────────────────────────────────────
        [NodeConditionTypes.GameVariableEquals] = new[]
        {
            new ParamSchema("name", "param.label.gc2Global", ParamType.GameVarRef, "",
                "condition.gameVariableEquals.name.tip"),
            new ParamSchema("value", "param.label.value", ParamType.String, "",
                "condition.gameVariableEquals.value.tip"),
        },
        [NodeConditionTypes.GameVariableNumberGreaterThan] = new[]
        {
            new ParamSchema("name", "param.label.gc2Global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.gameVariableNumberGreaterThan.value.tip"),
        },
        [NodeConditionTypes.GameVariableNumberGreaterOrEqual] = new[]
        {
            new ParamSchema("name", "param.label.gc2Global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.gameVariableNumberGreaterOrEqual.value.tip"),
        },
        [NodeConditionTypes.GameVariableNumberLessThan] = new[]
        {
            new ParamSchema("name", "param.label.gc2Global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.gameVariableNumberLessThan.value.tip"),
        },
        [NodeConditionTypes.GameVariableNumberLessOrEqual] = new[]
        {
            new ParamSchema("name", "param.label.gc2Global", ParamType.GameVarRef, "", ""),
            new ParamSchema("value", "param.label.threshold", ParamType.Float, "0",
                "condition.gameVariableNumberLessOrEqual.value.tip"),
        },

        // ── Scene-graph state ─────────────────────────────────────────
        [NodeConditionTypes.LevelActive] = new[]
        {
            new ParamSchema("level", "param.label.level", ParamType.LevelRef, "",
                "condition.levelActive.level.tip"),
        },
        // Targeting (kind / target / overlayLevel) comes from the shared
        // category row — the same one SetGameObjectActive uses, since the
        // condition asks about exactly what that action sets. Only the key
        // the row does not draw itself is declared here; the legacy 'path'
        // spelling is migrated to 'target' by NodeConditionViewModel and
        // still read by the runtime.
        [NodeConditionTypes.GameObjectActive] = new[]
        {
            new ParamSchema("target", "param.label.target", ParamType.GameObjectPath, "",
                "condition.gameObjectActive.target.tip"),
        },

        // Device / key / phase are drawn by the shared input row instead, the
        // way the Set-Active family draws its own targeting: the key list has
        // to follow the device picker, which a flat schema row cannot do.
        // Declared here anyway so the validator and the docs see them.
        [NodeConditionTypes.InputKey] = new[]
        {
            new ParamSchema("key", "param.label.key", ParamType.String, "",
                "condition.inputKey.key.tip"),
            new ParamSchema("phase", "param.label.when", ParamType.Choice, "Pressed",
                "condition.inputKey.phase.tip",
                fixedOptions: new[] { "Pressed", "Down", "Released", "Up" }),
        },

        // ── Misc ──────────────────────────────────────────────────────
        // Deprecated — no longer offered in the Type combo, but the schema
        // stays so packs authored before DailyChance still render/edit.
        [NodeConditionTypes.Random] = new[]
        {
            new ParamSchema("chance", "param.label.chance", ParamType.Float, "0.5",
                "condition.random.chance.tip"),
        },
        [NodeConditionTypes.DailyChance] = new[]
        {
            new ParamSchema("chance", "param.label.chance", ParamType.Percent, "50",
                "condition.dailyChance.chance.tip"),
        },
        [NodeConditionTypes.VariableStartsWith] = new[]
        {
            new ParamSchema("name", "param.label.variable", ParamType.PackVarRef, "",
                "condition.variableStartsWith.name.tip"),
            new ParamSchema("source", "param.label.source", ParamType.Choice, "pack",
                "condition.variableStartsWith.source.tip",
                new[] { "pack", "vanilla" }),
            new ParamSchema("value", "param.label.startsWith", ParamType.String, "",
                "condition.variableStartsWith.value.tip"),
            new ParamSchema("ignoreCase", "param.label.ignoreCase", ParamType.Bool, "false",
                "condition.variableStartsWith.ignoreCase.tip"),
        
        },

        // ── Lists ─────────────────────────────────────────────────────
        [NodeConditionTypes.ListContains] = new[]
        {
            new ParamSchema("list", "param.label.list", ParamType.ListVarRef, "",
                "condition.listContains.list.tip"),
            new ParamSchema("value", "param.label.contains", ParamType.String, "",
                "condition.listContains.value.tip"),
        },
        [NodeConditionTypes.ListCount] = new[]
        {
            new ParamSchema("list", "param.label.list", ParamType.ListVarRef, "",
                "condition.listCount.list.tip"),
            new ParamSchema("comparison", "param.label.countIs", ParamType.Choice, "equals",
                "condition.listCount.comparison.tip",
                // English on purpose: values packs store; the list shows them through choice.comparison.
                new[] { "equals", "greater than", "greater or equal", "less than", "less or equal" }),
            new ParamSchema("value", "param.label.value", ParamType.Int, "0",
                "condition.listCount.value.tip"),
        },

        [NodeConditionTypes.Timer] = new[]
        {
            new ParamSchema("randomize", "param.label.randomize", ParamType.Bool, "false",
                "condition.timer.randomize.tip"),
            new ParamSchema("seconds", "param.label.waitS", ParamType.Float, "30",
                "condition.timer.seconds.tip",
                enabledWhen: "randomize", enabledWhenValue: "false"),
            new ParamSchema("minSeconds", "param.label.minS", ParamType.Float, "15",
                "condition.timer.minSeconds.tip",
                enabledWhen: "randomize"),
            new ParamSchema("maxSeconds", "param.label.maxS", ParamType.Float, "45",
                "condition.timer.maxSeconds.tip",
                enabledWhen: "randomize"),
            new ParamSchema("stagger", "param.label.staggerStart", ParamType.Bool, "false",
                "condition.timer.stagger.tip"),
        
        },
        [NodeConditionTypes.AlwaysTrue] = Empty,
        [NodeConditionTypes.Weather] = new[]
        {
            new ParamSchema("state", "param.label.weatherIs", ParamType.Choice, "BadWeather",
                "condition.weather.state.tip",
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
