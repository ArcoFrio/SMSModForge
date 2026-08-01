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
    /// One-shot vs repeatable.
    /// <para/>
    /// A repeatable dialogue fires on every rising edge of its start
    /// conditions — leave the level and return and it plays again. Setting
    /// this blocks that: once played, it will not start again.
    /// <para/>
    /// The "already played" mark is runtime state, held per built dialogue and
    /// rebuilt from the manifest on each scene load, so it lasts for the
    /// current visit rather than for the save. Permanence is authored, not
    /// flagged: set a variable with
    /// <see cref="NodeActionTypes.SetVariable"/> on the final node and test it
    /// in <see cref="StartConditions"/>.
    /// </summary>
    [JsonProperty("oneShot", Order = 6, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool OneShot { get; set; } = false;

    /// <summary>
    /// "Wait for Talk button (no auto-play)": instead of auto-starting when its
    /// conditions pass on level entry, the dialogue <em>parks behind the vanilla
    /// Talk button</em> and the player starts it by clicking. It then plays
    /// exactly like a normal dialogue — the cinematic FadeUI fade leads, then
    /// the speech UI appears (see <c>DialogueDispatcher.StartDialogue</c>).
    /// Repeatable within a visit (re-arms on finish, rotates between several
    /// armed dialogues). This is the Talk-button flag; it is unrelated to
    /// <see cref="QueueBehind"/> (queue-behind-an-active-dialogue), which is a
    /// separate field.
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
    /// When true, a dialogue whose start conditions pass while another
    /// dialogue is playing stays latched and starts right after that one
    /// ends. When false (default — the original mod's behavior), it misses
    /// that window: it only fires when its conditions trigger again while
    /// no dialogue is playing.
    /// </summary>
    [JsonProperty("queueBehind", Order = 11, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool QueueBehind { get; set; } = false;

    /// <summary>
    /// Tie-breaker when several dialogues become eligible on the same tick:
    /// the highest priority starts first; equal priorities fall back to
    /// manifest order. Default 0.
    /// </summary>
    [JsonProperty("priority", Order = 12, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [System.ComponentModel.DefaultValue(0)]
    public int Priority { get; set; } = 0;

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
