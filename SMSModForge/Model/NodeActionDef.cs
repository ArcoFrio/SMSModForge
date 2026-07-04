using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One action authored against a dialogue node. Actions run at <em>node</em>
/// granularity (on-start / on-finish) rather than as GC2 Instructions; the
/// plugin subscribes to <c>Dialogue.EventStartNext</c>/<c>EventFinishNext</c>
/// and executes the pack's authored actions there. This sidesteps GC2's
/// polymorphic-list serialisation, which requires reflection on private
/// fields for every concrete <c>Instruction</c> subclass.
/// <para/>
/// The wire format keeps a simple <see cref="Type"/> discriminator + a
/// free-form <see cref="Params"/> dictionary so we can grow the action
/// vocabulary without versioning each entry. Unknown action types are
/// logged and skipped at load time.
/// </summary>
public sealed class NodeActionDef
{
    [JsonProperty("type", Order = 1)]
    public string Type { get; set; } = "";

    [JsonProperty("params", Order = 2)]
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>
    /// Weighted branches for the <see cref="NodeActionTypes.DiceRoll"/>
    /// action: one roll picks exactly ONE branch (chances must sum to 100).
    /// Empty/omitted for every other action type.
    /// </summary>
    [JsonProperty("branches", Order = 3)]
    public List<DiceBranchDef> Branches { get; set; } = new();

    public bool ShouldSerializeBranches() => Branches != null && Branches.Count > 0;
}

/// <summary>
/// One weighted branch of a <see cref="NodeActionTypes.DiceRoll"/> action:
/// a percentage chance plus the single action that runs when the roll lands
/// in this branch. Branch actions are full <see cref="NodeActionDef"/>s, so
/// anything authorable as a node action (including a nested dice roll) works.
/// </summary>
public sealed class DiceBranchDef
{
    [JsonProperty("chance", Order = 1)]
    public int Chance { get; set; } = 50;

    [JsonProperty("action", Order = 2)]
    public NodeActionDef Action { get; set; } = new() { Type = NodeActionTypes.SetVariable };
}

/// <summary>
/// Stable identifiers for the action types the editor and plugin both
/// understand. Stored as <see cref="NodeActionDef.Type"/>; matched on the
/// runtime side by a switch on this exact string.
/// </summary>
public static class NodeActionTypes
{
    /// <summary>Set a pack variable. Params: <c>name</c>, <c>value</c>.</summary>
    public const string SetVariable = "SetVariable";

    /// <summary>Add/subtract from a numeric pack variable. Params: <c>name</c>, <c>delta</c>.</summary>
    public const string IncrementVariable = "IncrementVariable";

    /// <summary>Set the active bust for an actor (pack-local actor key). Params: <c>actor</c>, <c>bustKey</c>.</summary>
    public const string SetActorBust = "SetActorBust";

    /// <summary>Force an actor's expression for the upcoming line. Params: <c>actor</c>, <c>expression</c>.</summary>
    public const string SetActorExpression = "SetActorExpression";

    /// <summary>
    /// Raise or lower an actor's bust relative to the CG / scene layer —
    /// the generalised replacement for a host sprite-focus marker
    /// marker. When <c>focused</c> is true the bust's sprite sorting
    /// orders are bumped so it draws over a full-screen scene; false
    /// restores the resting orders. Params: <c>actor</c>, <c>focused</c>.
    /// </summary>
    public const string SetSpriteFocus = "SetSpriteFocus";

    /// <summary>
    /// Immediately deactivate the actor's current bust GameObject — equivalent
    /// to <c>SetActive(false)</c> on the bust under <c>2_Bust_Manager</c>. No
    /// fade. Params: <c>actor</c>.
    /// </summary>
    public const string DeactivateBust = "DeactivateBust";

    /// <summary>
    /// Trigger the vanilla "leave" fade-out on the actor's current bust by
    /// activating the <c>MBase1/Leave</c> child if it exists. Mirrors the
    /// the host mod pattern of staged exits — vanilla components on
    /// <c>Leave</c> drive the fade and the eventual SetActive(false) on
    /// the parent. Falls back to an immediate deactivate when no
    /// <c>Leave</c> child is present (e.g. on a pack-defined bust that
    /// didn't replicate that child). Params: <c>actor</c>.
    /// </summary>
    public const string LeaveBust = "LeaveBust";

    /// <summary>Unified "Set Active" action. Params: <c>kind</c> (Bust / Level
    /// Overlay / Scene / Direct Path), <c>target</c> (GameObject name/path, or a
    /// scene key when kind=Scene), <c>active</c>. Scene routes through the pack's
    /// scene registry and plays its activation sound when turned on. Legacy
    /// <c>path</c> (GO) is still read as a fallback for pre-unify packs.</summary>
    public const string SetGameObjectActive = "SetGameObjectActive";

    /// <summary>Emit a GC2 signal by name. Params: <c>signal</c>.</summary>
    public const string EmitSignal = "EmitSignal";

    /// <summary>
    /// Emit a GC2 signal after a delay, fire-and-forget. The action
    /// returns immediately so the dialogue keeps flowing; the signal
    /// fires later from a coroutine. Mirrors the host mod's
    /// <c>Core.EmitSignalDelayed</c> — used by dialogues where a
    /// fade-in needs to happen now and the matching fade-out a beat
    /// later (e.g. a multi-step scene chains <c>fadeInSignal</c>
    /// with <c>FadeOut2025</c> at +1.5s). Params: <c>signal</c>, <c>seconds</c>.
    /// </summary>
    public const string EmitSignalDelayed = "EmitSignalDelayed";

    /// <summary>
    /// Full cross-fade between two levels with a delayed signal at
    /// the end. Equivalent to the host mod's
    /// <c>Core.EmitSignalGameObjectDelayed</c>: enables the source
    /// level's GC2 trigger components, waits 2/3 of the delay,
    /// deactivates the source, disables the destination's trigger
    /// components, activates the destination, waits the remaining
    /// third, then emits the signal. Used by dialogues that move
    /// the player between two pre-existing levels mid-conversation
    /// (e.g. swapping between two levels mid-dialogue). Params:
    /// <c>fromLevel</c>, <c>toLevel</c> (both <c>vanilla:&lt;goName&gt;</c>
    /// or <c>place:&lt;key&gt;</c>), <c>signal</c> (optional),
    /// <c>seconds</c> (total delay).
    /// </summary>
    public const string TransitionLevels = "TransitionLevels";

    /// <summary>
    /// Tween a <see cref="UnityEngine.SpriteRenderer"/>'s alpha to a
    /// target over a duration. Generic alpha-tween primitive ported
    /// from a host fade helper; used for bespoke
    /// pack-authored fades that don't fit the screen-wide GC2 fade
    /// signals. Params: <c>path</c> (scene-graph path to the GO),
    /// <c>to</c> (target alpha 0..1), <c>seconds</c>.
    /// </summary>
    public const string FadeSprite = "FadeSprite";

    /// <summary>
    /// Tween a GameObject's position to a target over a duration — the
    /// generic transform-move primitive (e.g. the a level camera "pan"
    /// that actually slides the whole level). <c>target</c> resolves a level
    /// token (<c>self:&lt;key&gt;</c> / <c>place:&lt;key&gt;</c> /
    /// <c>vanilla:&lt;go&gt;</c>) or a plain GameObject / overlay name.
    /// Params: <c>target</c>, <c>x</c>, <c>y</c>, <c>seconds</c>,
    /// <c>relative</c> (offset from current vs absolute local position).
    /// </summary>
    public const string MoveGameObject = "MoveGameObject";

    /// <summary>
    /// Start or stop a GameObject spinning around Z at a constant rate — the
    /// generic continuous-rotation primitive (the a prop's spin). Adds a
    /// lightweight spin component the first time. Params: <c>target</c>,
    /// <c>speed</c> (degrees/second), <c>enable</c> (true to spin, false to
    /// stop). Matches a constant Z-rotation at speed 1.
    /// </summary>
    public const string SpinGameObject = "SpinGameObject";

    /// <summary>Switch active music under <c>12_AudioPlayer</c>. Params: <c>music</c>.</summary>
    public const string SwitchMusic = "SwitchMusic";

    /// <summary>
    /// Play a one-shot sound effect through the pack's shared
    /// SfxPlayer AudioSource. Stack multiple calls on the same node
    /// for layered or sequenced audio — each has its own optional
    /// delay so the dialogue itself never blocks. Params:
    /// <c>clip</c> (pack SFX key), <c>volume</c> (0..1, defaults to
    /// the SFX def's <c>defaultVolume</c> or 1), <c>delay</c>
    /// (seconds before play, defaults to 0).
    /// </summary>
    public const string PlaySFX = "PlaySFX";

    /// <summary>End the currently playing dialogue immediately.</summary>
    public const string EndDialogue = "EndDialogue";

    /// <summary>Wait the specified number of seconds before continuing. Params: <c>seconds</c>.</summary>
    public const string Wait = "Wait";

    /// <summary>
    /// Pick a random element from a list and write it into a target
    /// variable. The <c>source</c> param can be:
    /// <list type="bullet">
    ///   <item>a literal comma-separated string (<c>"A,B,C"</c>)</item>
    ///   <item><c>$varName</c> referencing a String pack variable
    ///   whose value is comma-separated</item>
    ///   <item><c>$varName</c> referencing a List pack variable —
    ///   the runtime reads its JSON-array form directly</item>
    /// </list>
    /// An empty / missing source clears the target. Generalises
    /// a host random-target picker. Params: <c>source</c>,
    /// <c>target</c>.
    /// </summary>
    public const string PickRandomFromList = "PickRandomFromList";

    /// <summary>
    /// Append a string value to a List-typed pack variable. No-op if
    /// the named variable doesn't exist, isn't a List, or
    /// <c>value</c> is empty. Params: <c>list</c> (variable name),
    /// <c>value</c>.
    /// </summary>
    public const string AddToList = "AddToList";

    /// <summary>
    /// Remove the first matching value from a List-typed pack
    /// variable. No-op if the value isn't present. Params:
    /// <c>list</c>, <c>value</c>.
    /// </summary>
    public const string RemoveFromList = "RemoveFromList";

    /// <summary>
    /// Reset a List-typed pack variable to empty. Params: <c>list</c>.
    /// </summary>
    public const string ClearList = "ClearList";

    /// <summary>
    /// Write the number of entries in a List-typed pack variable into
    /// another pack variable (Int). Surfaced as the Variable action's
    /// "List count" operation. Params: <c>fromList</c> (list variable
    /// name), <c>name</c> (target variable).
    /// </summary>
    public const string CountList = "CountList";

    /// <summary>
    /// Weighted one-of-N picker ("dice roll"): rolls once and executes exactly
    /// one of its <see cref="NodeActionDef.Branches"/>, chosen by percentage
    /// chance. The editor enforces the chances summing to exactly 100. No
    /// <c>params</c> — everything lives in the branches list.
    /// </summary>
    public const string DiceRoll = "DiceRoll";

    /// <summary>
    /// Legacy activate-only scene action (Params: <c>scene</c>). Superseded by
    /// <see cref="SetGameObjectActive"/> with <c>kind=Scene</c>, which adds
    /// deactivation; the editor migrates these on load and no longer offers the
    /// type directly. The plugin still executes it as an alias so pre-unify packs
    /// keep working: it resolves the scene through the registry, activates the GO
    /// and emits the scene's authored sound signal.
    /// </summary>
    public const string ActivateScene = "ActivateScene";

    /// <summary>
    /// Deactivate every scene GO this pack created. Convenient for a
    /// dialogue's finisher node so authors don't have to list every
    /// individual scene's <c>SetActive(false)</c>. Takes no params.
    /// </summary>
    public const string DeactivateAllScenes = "DeactivateAllScenes";

    /// <summary>All action types recognised by the editor's picker.</summary>
    public static readonly string[] All =
    {
        SetVariable, IncrementVariable, SetActorBust, SetActorExpression,
        SetSpriteFocus, DeactivateBust, LeaveBust,
        SetGameObjectActive, EmitSignal, EmitSignalDelayed,
        TransitionLevels, FadeSprite, MoveGameObject, SpinGameObject,
        SwitchMusic, PlaySFX,
        EndDialogue, Wait,
        PickRandomFromList, AddToList, RemoveFromList, ClearList, CountList,
        DiceRoll,
        ActivateScene, DeactivateAllScenes,
    };
}
