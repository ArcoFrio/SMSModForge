using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SMSModForge.Model;

/// <summary>
/// One free-floating "<c>if</c>" rule evaluated per-frame by the pack
/// runtime. Conceptually the same primitive as a dialogue node — a
/// list of <see cref="NodeConditionDef"/>s combined with AND
/// semantics, plus a list of <see cref="NodeActionDef"/>s that fire
/// when the conditions pass — but the runtime evaluates it from
/// <c>Plugin.Update</c> rather than from a dialogue-line callback.
/// <para/>
/// Used for pack-side orchestration that doesn't fit on a single
/// dialogue: rebuilding lists on day change, mirroring derived
/// state, gating UI widgets, etc.
/// </summary>
public sealed class UpdateRuleDef
{
    /// <summary>
    /// Stable pack-local identifier. Used by the runtime to track
    /// per-rule edge state across reloads and to label log entries.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "rule1";

    /// <summary>
    /// Free-text label shown in the editor's rule list. Pure
    /// authoring aid — has no runtime effect.
    /// </summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Rule";

    /// <summary>
    /// Free-form description shown beside the rule's name in the
    /// editor. Useful for documenting why a rule exists or what
    /// invariant it maintains.
    /// </summary>
    [JsonProperty("description", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    /// <summary>
    /// When the rule fires relative to its conditions transitioning.
    /// See <see cref="UpdateRuleTriggerMode"/>.
    /// </summary>
    [JsonProperty("triggerMode", Order = 4)]
    public UpdateRuleTriggerMode TriggerMode { get; set; } = UpdateRuleTriggerMode.OnRisingEdge;

    /// <summary>
    /// Conditions combined with AND semantics — identical evaluator
    /// to dialogue start conditions.
    /// </summary>
    [JsonProperty("conditions", Order = 5)]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    /// <summary>
    /// Actions fired in order when the trigger condition is met.
    /// Same vocabulary as a dialogue node's <c>actionsOnStart</c>.
    /// </summary>
    [JsonProperty("actions", Order = 6)]
    public List<NodeActionDef> Actions { get; set; } = new();

    /// <summary>
    /// When true, the editor's rule-detail view is in "code mode" —
    /// the picker UI is hidden and a JSON textbox is shown instead.
    /// This is purely an editor preference: the underlying
    /// <see cref="Conditions"/> + <see cref="Actions"/> lists remain
    /// the source of truth in both modes. Round-trips through the
    /// manifest so the per-rule preference survives reload.
    /// </summary>
    [JsonProperty("codeMode", Order = 7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool CodeMode { get; set; } = false;
}

/// <summary>
/// Selects when an <see cref="UpdateRuleDef"/>'s actions fire
/// relative to its conditions transitioning. The runtime tracks the
/// previous-tick condition state per rule, so edge modes only need
/// the last value + the current one.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum UpdateRuleTriggerMode
{
    /// <summary>
    /// Fires the action list every frame the conditions are true.
    /// Rare — use only for "keep variable X synced to expression Y"
    /// patterns where the cost of re-running the actions each frame
    /// is acceptable.
    /// </summary>
    WhilePassing,

    /// <summary>
    /// Fires once when the conditions transition from false to true.
    /// The most common mode. Re-arms only after a falling edge.
    /// </summary>
    OnRisingEdge,

    /// <summary>
    /// Fires once when the conditions transition from true to false.
    /// Mirror of <see cref="OnRisingEdge"/>, useful for cleanup logic.
    /// </summary>
    OnFallingEdge,

    /// <summary>
    /// Fires once per <c>CoreGameScene</c> entry, after the dialogue
    /// runtime has built (so all variables, actors, etc. exist).
    /// Conditions are still required to pass.
    /// </summary>
    OnSceneLoad,

    /// <summary>
    /// Fires once per in-game day advance — same trigger that drives
    /// <c>refreshMode: Daily</c> resets. Conditions still required.
    /// Use for "rebuild this list each day" patterns like a daily-rebuilt list
    /// eligibility rebuild.
    /// </summary>
    OnDayChange,
}
