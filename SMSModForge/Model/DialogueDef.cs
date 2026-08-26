using System.Collections.Generic;
using System.Linq;
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
    /// The vanilla roomtalk this dialogue can take priority over, as
    /// <c>"vanilla:&lt;roomTalkName&gt;"</c> — or empty when its level has none.
    /// <para/>
    /// DERIVED, not authored. It used to be picked by hand and doubled as the
    /// dialogue's parent in the scene; dialogues are hosted on a per-pack
    /// always-active container now, so the only thing left that needs a
    /// roomtalk is <see cref="DisableVanillaTrigger"/>. Since a level uniquely
    /// determines its roomtalk, this reads straight off the pinned
    /// <c>LevelActive</c> start condition and can't disagree with it.
    /// <para/>
    /// The setter is kept so packs written before the change still deserialise;
    /// the stored value is ignored.
    /// </summary>
    [JsonProperty("roomTalk", Order = 3)]
    public string RoomTalk
    {
        get => VanillaPlaces.RoomTalkTokenForLevel(LevelToken);
        set => LegacyRoomTalk = value ?? "";
    }

    /// <summary>Only emit a roomtalk when there is one — most dialogues have
    /// no vanilla entry dialogue to take priority over.</summary>
    public bool ShouldSerializeRoomTalk() => !string.IsNullOrEmpty(RoomTalk);

    /// <summary>
    /// Whatever a pre-derivation pack stored in <c>roomTalk</c>. Never written
    /// back out — it exists so a pack old enough to predate the pinned
    /// LevelActive condition can still have its level inferred from the
    /// roomtalk it used to name, instead of loading with no level at all.
    /// </summary>
    [JsonIgnore]
    public string LegacyRoomTalk { get; private set; } = "";

    /// <summary>
    /// The level this dialogue is gated on, from its pinned <c>LevelActive</c>
    /// start condition. Empty when the condition is missing or has no level yet.
    /// </summary>
    [JsonIgnore]
    public string LevelToken
    {
        get
        {
            var c = StartConditions?.FirstOrDefault(x => x.Type == NodeConditionTypes.LevelActive);
            if (c?.Params != null && c.Params.TryGetValue("level", out var lv)) return lv ?? "";
            return "";
        }
    }

    /// <summary>True when this dialogue's level has a vanilla roomtalk, i.e.
    /// when <see cref="DisableVanillaTrigger"/> has something to suppress.</summary>
    [JsonIgnore]
    public bool VanillaRoomTalkAvailable => VanillaPlaces.HasRoomTalk(LevelToken);

    /// <summary>
    /// "Prioritize this dialogue over vanilla" — while every start condition
    /// holds, the plugin disables the vanilla Trigger on this level's roomtalk
    /// so the room's own entry dialogue doesn't compete with this one. The
    /// roomtalk comes from <see cref="RoomTalk"/>, i.e. from the level.
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
    /// Leave the dialogue on the Talk button after it auto-plays, so the player
    /// can hear it again.
    /// <para/>
    /// Fills the gap between the two behaviours that already existed: a plain
    /// dialogue interrupts the moment its conditions pass and is then gone
    /// until the next rising edge, while <see cref="Queued"/> waits for the
    /// Talk button and never interrupts. This is both — it plays on arrival AND
    /// stays available to replay for as long as its conditions hold.
    /// <para/>
    /// Implied by <see cref="Queued"/>, which already re-arms on finish, so the
    /// two together mean nothing more than <see cref="Queued"/> alone.
    /// <para/>
    /// Replaces the old <c>oneShot</c>, which suppressed the replay a dialogue
    /// gets on its next rising edge. That was near-useless in practice: the
    /// mark was runtime-only and rebuilt from the manifest on every scene load,
    /// so it never survived loading a save, and anything wanting to retire a
    /// dialogue for good has to set a variable on its last node and test that
    /// in <see cref="StartConditions"/> — which works regardless.
    /// </summary>
    [JsonProperty("replayOnTalk", Order = 6, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public bool ReplayOnTalk { get; set; } = false;

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
