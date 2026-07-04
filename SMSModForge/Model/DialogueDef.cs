using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One authored dialogue in a pack. Maps 1:1 to a runtime GC2
/// <c>Dialogue</c> MonoBehaviour parented under a roomtalk node.
/// </summary>
public sealed class DialogueDef
{
    /// <summary>Pack-local key. Unique within the pack.</summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "newdialogue";

    /// <summary>Human-readable name shown in the editor's dialogue list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Dialogue";

    /// <summary>
    /// Where the dialogue lives in the scene. One of:
    /// <list type="bullet">
    ///   <item><c>"vanilla:&lt;roomTalkName&gt;"</c> — parent under an existing roomtalk (e.g. <c>vanilla:Beach</c>).</item>
    ///   <item><c>"place:&lt;packPlaceKey&gt;"</c> — parent under a roomtalk created by this pack's <see cref="PlaceDef"/>.</item>
    /// </list>
    /// The runtime resolves this to a transform under <c>8_Room_Talk</c>.
    /// </summary>
    [JsonProperty("roomTalk", Order = 3)]
    public string RoomTalk { get; set; } = "";

    /// <summary>
    /// When true, the plugin disables the parent roomtalk's vanilla
    /// <c>Trigger</c> component while this dialogue is the only one the
    /// pack expects to run there. Use for vanilla locations whose Trigger
    /// auto-plays a default dialogue on entry that you want to suppress.
    /// </summary>
    [JsonProperty("disableVanillaTrigger", Order = 4, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool DisableVanillaTrigger { get; set; } = false;

    /// <summary>
    /// Conditions that must all pass for the plugin to start this dialogue.
    /// Checked once per frame when the parent roomtalk's level becomes
    /// active (and no other dialogue is currently playing).
    /// </summary>
    [JsonProperty("startConditions", Order = 5)]
    public List<NodeConditionDef> StartConditions { get; set; } = new();

    /// <summary>
    /// One-shot vs repeatable. When set, the plugin remembers the dialogue
    /// has played at least once and won't re-trigger; pair with a
    /// <see cref="NodeActionTypes.SetVariable"/> action on the final node
    /// for finer-grained gating.
    /// </summary>
    [JsonProperty("oneShot", Order = 6, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool OneShot { get; set; } = false;

    /// <summary>
    /// Queued-on-arrival: start the dialogue <em>without</em> the FadeUI
    /// cinematic fade-to-black and after a slightly longer delay, so it eases
    /// in on level entry instead of jump-scaring the player. Mirrors the host
    /// mod's <c>StartDialogueSequenceQueue</c> (identical to a normal start but
    /// it doesn't emit FadeUI).
    /// </summary>
    [JsonProperty("queued", Order = 7, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool Queued { get; set; } = false;

    /// <summary>
    /// Debugging aid: when true, pressing F12 in-game logs a per-condition
    /// breakdown of this dialogue's start conditions (pass/fail + current
    /// variable values) to the BepInEx console. Purely diagnostic — no
    /// gameplay effect. Omitted from JSON when false.
    /// </summary>
    [JsonProperty("debugConditions", Order = 10, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool DebugConditions { get; set; } = false;

    /// <summary>
    /// All nodes in the dialogue, indexed by stable id. Order is preserved
    /// for diff-friendliness; the runtime reads <see cref="RootNodeIds"/>
    /// + each node's children list, not the array order.
    /// </summary>
    [JsonProperty("nodes", Order = 8)]
    public List<DialogueNodeDef> Nodes { get; set; } = new();

    /// <summary>
    /// IDs of root nodes (in display order). Most dialogues have exactly
    /// one root, but GC2 allows a forest of roots.
    /// </summary>
    [JsonProperty("rootNodeIds", Order = 9)]
    public List<int> RootNodeIds { get; set; } = new();
}
