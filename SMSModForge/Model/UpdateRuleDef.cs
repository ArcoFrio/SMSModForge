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

    /// <summary>
    /// Else-if chain. The rule's own <see cref="Conditions"/> +
    /// <see cref="Actions"/> form the IF; each branch here is tried in
    /// order when everything before it failed, and the FIRST branch
    /// whose conditions pass supplies the actions that fire. A branch
    /// with no conditions always passes — i.e. a plain ELSE. Reuses
    /// <see cref="LevelHookDef"/>: the same "conditions-gated action
    /// group" shape the place enter/exit hooks serialize.
    /// <para/>
    /// Edge semantics generalize per branch: the runtime tracks WHICH
    /// branch is selected, and OnRisingEdge fires whenever the selected
    /// branch changes (a single-branch rule degenerates to the old
    /// false→true behavior). This is the primitive that lets packs
    /// express schedule-style cascades ("if slot A → …, else if slot
    /// B → …, else → …") without one rule per case.
    /// </summary>
    [JsonProperty("branches", Order = 8)]
    public List<LevelHookDef> Branches { get; set; } = new();
    public bool ShouldSerializeBranches() => Branches.Count > 0;

    /// <summary>
    /// Optional list of values to run this rule once for, each tick — the
    /// rule's parameter. Same shape every list-taking param accepts: a literal
    /// comma-separated string (<c>"A,B,C"</c>), <c>$varName</c> naming a List
    /// variable, or <c>$varName</c> naming a String variable holding CSV.
    /// <para/>
    /// Each value is substituted into every condition and action of the rule
    /// (including its branches) wherever <c>{<see cref="ForEachAs"/>}</c>
    /// appears, so one authored rule covers a whole set of subjects — e.g.
    /// <c>forEach: "$RoamingCharacters"</c> with conditions on
    /// <c>Location_{char}</c> and actions targeting <c>{char}/Slot</c>.
    /// Because it is plain text substitution it composes with the rest of the
    /// vocabulary: <c>$Prefix_{char}</c> dereferences the per-value variable.
    /// <para/>
    /// Every value gets its OWN edge state and its own Timer deadlines, so the
    /// values behave exactly like the separate rules they replace (a Timer's
    /// <c>stagger</c> is what spreads their first fire apart). Empty = the rule
    /// runs once, unparameterized.
    /// </summary>
    [JsonProperty("forEach", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public string? ForEach { get; set; }

    /// <summary>
    /// Placeholder name substituted by <see cref="ForEach"/>, written
    /// <c>{name}</c> in conditions and actions. Defaults to <c>item</c>.
    /// </summary>
    [JsonProperty("forEachAs", Order = 10, NullValueHandling = NullValueHandling.Ignore)]
    public string? ForEachAs { get; set; }

    /// <summary>
    /// Debugging aid: when true, the runtime logs what this rule decided every
    /// time its decision CHANGES — which branch won, what won last tick, and
    /// whether it fired. When no branch passes, every branch is dumped
    /// condition by condition with live values.
    /// <para/>
    /// Unlike the dialogue flag this needs no keypress: an edge-triggered rule
    /// that fails to fire produces no trace at all otherwise, and the moment
    /// worth seeing is usually one frame during a day change. Logging on change
    /// (rather than per frame) keeps it to a handful of lines.
    /// <para/>
    /// Purely diagnostic — no gameplay effect. Omitted from JSON when false.
    /// </summary>
    [JsonProperty("debugConditions", Order = 11, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool DebugConditions { get; set; } = false;
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
