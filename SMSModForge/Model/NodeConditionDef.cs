using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One condition gating something (a dialogue's start, a node's eligibility,
/// a choice's visibility). Same Type/Params discriminator-on-string pattern
/// as <see cref="NodeActionDef"/> for the same reasons — pack authors can
/// extend the condition vocabulary without inventing per-type wire shapes,
/// and the plugin's runtime evaluator dispatches via a single switch.
/// </summary>
public sealed class NodeConditionDef
{
    [JsonProperty("type", Order = 1)]
    public string Type { get; set; } = "";

    [JsonProperty("params", Order = 2)]
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>
    /// When true, the condition's result is inverted before being combined
    /// with the surrounding list. Lets a single typed condition serve both
    /// directions ("is set" / "is not set"). Maps roughly to GC2's
    /// <c>Condition.m_Sign</c>.
    /// </summary>
    [JsonProperty("negate", Order = 3, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool Negate { get; set; } = false;

    /// <summary>
    /// Child conditions, present only when <see cref="Type"/> is a group
    /// (<see cref="NodeConditionTypes.GroupAll"/> / <see cref="NodeConditionTypes.GroupAny"/>).
    /// A group evaluates its children with AND (<c>All</c>) or OR (<c>Any</c>);
    /// combined with <see cref="Negate"/> this gives full parenthesised
    /// boolean logic. Null/absent for leaf conditions, which use
    /// <see cref="Params"/> instead.
    /// </summary>
    [JsonProperty("conditions", Order = 4)]
    public List<NodeConditionDef>? Conditions { get; set; }

    /// <summary>Leaf conditions serialize <c>params</c>; groups omit it (and vice-versa).</summary>
    public bool ShouldSerializeParams() => Params != null && Params.Count > 0;
    public bool ShouldSerializeConditions() => Conditions != null && Conditions.Count > 0;
}

/// <summary>
/// Stable identifiers for condition types. Each is documented with the
/// <see cref="NodeConditionDef.Params"/> keys it expects.
/// </summary>
public static class NodeConditionTypes
{
    /// <summary>Pack variable equals a value. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableEquals = "VariableEquals";

    /// <summary>Numeric pack variable is greater than a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableGreaterThan = "VariableGreaterThan";

    /// <summary>Numeric pack variable is less than a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableLessThan = "VariableLessThan";

    /// <summary>
    /// Numeric pack variable is &gt;= a threshold. Params: <c>name</c>,
    /// <c>value</c>. Useful for the inclusive end of a <c>refreshMode</c>
    /// random gate (e.g. a <c>value &lt;= 30</c> check
    /// becomes a <see cref="VariableLessOrEqual"/> at 30).
    /// </summary>
    public const string VariableGreaterOrEqual = "VariableGreaterOrEqual";

    /// <summary>Numeric pack variable is &lt;= a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string VariableLessOrEqual = "VariableLessOrEqual";

    /// <summary>Pack variable has been set at all (any value). Params: <c>name</c>.</summary>
    public const string VariableExists = "VariableExists";

    /// <summary>GC2 GlobalNameVariable equals a value. Params: <c>name</c>, <c>value</c>. Reads through GlobalNameVariablesManager.</summary>
    public const string GameVariableEquals = "GameVariableEquals";

    /// <summary>
    /// GC2 numeric global is &gt;= a threshold. Params:
    /// <c>name</c>, <c>value</c> (invariant-culture number).
    /// Covers the host mod-side <c>newtrait-Athletic &gt;= 5</c>
    /// pattern used by the a level-gated random dialogue.
    /// </summary>
    public const string GameVariableNumberGreaterOrEqual = "GameVariableNumberGreaterOrEqual";

    /// <summary>GC2 numeric global is &lt;= a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberLessOrEqual = "GameVariableNumberLessOrEqual";

    /// <summary>GC2 numeric global is strictly &gt; a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberGreaterThan = "GameVariableNumberGreaterThan";

    /// <summary>GC2 numeric global is strictly &lt; a threshold. Params: <c>name</c>, <c>value</c>.</summary>
    public const string GameVariableNumberLessThan = "GameVariableNumberLessThan";

    /// <summary>A level is currently active in the scene. Params: <c>level</c> (the <c>5_Levels</c> child name).</summary>
    public const string LevelActive = "LevelActive";

    /// <summary>A GameObject at a scene path is active. Params: <c>path</c>.</summary>
    public const string GameObjectActive = "GameObjectActive";

    /// <summary>Probability gate — passes <c>chance</c> fraction of the time. Params: <c>chance</c> (0..1).</summary>
    public const string Random = "Random";

    /// <summary>Always-true condition (useful for testing).</summary>
    public const string AlwaysTrue = "AlwaysTrue";

    /// <summary>
    /// Group: all child conditions must pass (AND). Carries a nested
    /// <see cref="NodeConditionDef.Conditions"/> list instead of params.
    /// Not a leaf type — created via the editor's "Add AND group" button,
    /// so it is deliberately excluded from <see cref="All"/> (the Type combo).
    /// </summary>
    public const string GroupAll = "All";

    /// <summary>Group: any child condition passing is enough (OR). See <see cref="GroupAll"/>.</summary>
    public const string GroupAny = "Any";

    /// <summary>True for the two group discriminators.</summary>
    public static bool IsGroup(string type) => type == GroupAll || type == GroupAny;

    public static readonly string[] All =
    {
        VariableEquals, VariableGreaterThan, VariableGreaterOrEqual,
        VariableLessThan, VariableLessOrEqual, VariableExists,
        GameVariableEquals,
        GameVariableNumberGreaterThan, GameVariableNumberGreaterOrEqual,
        GameVariableNumberLessThan, GameVariableNumberLessOrEqual,
        LevelActive, GameObjectActive, Random, AlwaysTrue,
    };
}
