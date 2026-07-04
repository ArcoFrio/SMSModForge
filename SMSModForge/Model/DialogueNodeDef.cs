using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One node in a dialogue graph. Nodes are stored as a flat list with
/// stable integer ids and explicit parent/child references — this maps
/// cleanly to GC2's <c>Content</c>/<c>TSerializableTree</c> at runtime
/// (which uses int ids internally), while keeping the on-disk format
/// editor-friendly and diff-friendly.
/// <para/>
/// The editor manages the id space (sequential, monotonically increasing
/// per dialogue, never reused). The runtime translates pack ids into the
/// hash-of-Guid ids GC2 generates inside <c>Content</c> via a per-dialogue
/// id map.
/// </summary>
public sealed class DialogueNodeDef
{
    /// <summary>Pack-stable id. Unique within the parent dialogue.</summary>
    [JsonProperty("id", Order = 1)]
    public int Id { get; set; }

    /// <summary>
    /// Node kind. Determines the GC2 <c>TNodeType</c> the plugin attaches:
    /// <c>Text</c> → <c>NodeTypeText</c>, <c>Choice</c> → <c>NodeTypeChoice</c>,
    /// <c>Random</c> → <c>NodeTypeRandom</c>.
    /// </summary>
    [JsonProperty("kind", Order = 2)]
    public DialogueNodeKind Kind { get; set; } = DialogueNodeKind.Text;

    /// <summary>
    /// Pack-local actor key (matches a <see cref="ActorDef.Key"/>). Empty
    /// means "no speaker" — the line plays without changing the active bust.
    /// </summary>
    [JsonProperty("actor", Order = 3)]
    public string Actor { get; set; } = "";

    /// <summary>
    /// Pack-local expression key (matches an entry under
    /// <see cref="ActorDef.Expressions"/>). Empty means "use the actor's
    /// default expression".
    /// </summary>
    [JsonProperty("expression", Order = 4)]
    public string Expression { get; set; } = "";

    /// <summary>
    /// Optional outfit switch — a bust GameObject name listed in the
    /// speaking actor's <see cref="ActorDef.Outfits"/>. When set, the
    /// runtime swaps the actor to this bust: the actor's previously-shown
    /// bust is deactivated and this one activated, so an actor can change
    /// outfit between nodes of the same dialogue. Empty = keep whatever
    /// bust the actor is currently wearing.
    /// </summary>
    [JsonProperty("outfit", Order = 5)]
    public string Outfit { get; set; } = "";

    /// <summary>
    /// The line text. Supports the standard the host mod substitution syntax:
    /// <c>[PV:name]</c> for pack variables (the plugin replaces this before
    /// the line displays). Inline TextMeshPro tags pass through unchanged.
    /// </summary>
    [JsonProperty("text", Order = 6)]
    public string Text { get; set; } = "";

    /// <summary>
    /// Stable tag for jump targets. If non-empty, other nodes can target
    /// this node via <see cref="JumpDef.TargetTag"/>. Optional.
    /// </summary>
    [JsonProperty("tag", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public string? Tag { get; set; }

    /// <summary>
    /// Conditions that must pass for this node to be considered runnable.
    /// Empty list = always runnable. For choice children, these gate
    /// whether the choice is offered at all.
    /// </summary>
    [JsonProperty("conditions", Order = 8)]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    /// <summary>Actions to run when the node starts displaying.</summary>
    [JsonProperty("actionsOnStart", Order = 9)]
    public List<NodeActionDef> ActionsOnStart { get; set; } = new();

    /// <summary>Actions to run when the node finishes (the player advances past it).</summary>
    [JsonProperty("actionsOnFinish", Order = 10)]
    public List<NodeActionDef> ActionsOnFinish { get; set; } = new();

    /// <summary>
    /// IDs of child nodes in display order. For text nodes the runtime
    /// follows children sequentially; for choice nodes each child is one
    /// choice; for random nodes the runtime picks one child at random.
    /// </summary>
    [JsonProperty("children", Order = 11)]
    public List<int> Children { get; set; } = new();

    /// <summary>
    /// What happens after this node finishes. Defaults to <c>Continue</c>
    /// (descend into children); <c>Exit</c> ends the dialogue; <c>Jump</c>
    /// resumes from the node with a matching <see cref="Tag"/>.
    /// </summary>
    [JsonProperty("jump", Order = 12, NullValueHandling = NullValueHandling.Ignore)]
    public JumpDef? Jump { get; set; }

    /// <summary>
    /// How a Text line advances. <see cref="NodeDurationMode.UntilInteraction"/>
    /// (the default) waits for the player; <see cref="NodeDurationMode.Timeout"/>
    /// auto-advances <see cref="Timeout"/> seconds after the typewriter finishes.
    /// Mirrors GC2's node <c>Duration</c>. Omitted from JSON when it's the
    /// default, so existing dialogues are unaffected.
    /// </summary>
    [JsonProperty("duration", Order = 13)]
    public NodeDurationMode Duration { get; set; } = NodeDurationMode.UntilInteraction;
    public bool ShouldSerializeDuration() => Duration != NodeDurationMode.UntilInteraction;

    /// <summary>
    /// Seconds the line lingers after typing finishes, when
    /// <see cref="Duration"/> is <see cref="NodeDurationMode.Timeout"/>. Matches
    /// GC2's 3s default. Only serialized for Timeout nodes.
    /// </summary>
    [JsonProperty("timeout", Order = 14)]
    public float Timeout { get; set; } = 3f;
    public bool ShouldSerializeTimeout() => Duration == NodeDurationMode.Timeout;
}

/// <summary>
/// Subset of GC2's <c>NodeDuration</c> we expose (Audio / Animation are out of
/// scope — pack nodes carry no per-node audio clip or animation). Names match
/// GC2's enum exactly so the runtime can <c>Enum.Parse</c> them by string.
/// </summary>
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum NodeDurationMode
{
    /// <summary>Wait for player input before advancing (GC2 default).</summary>
    UntilInteraction,
    /// <summary>Auto-advance after the typewriter finishes + a timeout.</summary>
    Timeout,
}

/// <summary>
/// Node kind enum. Mirrors GC2's <c>TNodeType</c> subclasses we support.
/// Stored as a string in JSON via Newtonsoft's <c>StringEnumConverter</c>.
/// </summary>
[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum DialogueNodeKind
{
    /// <summary>A text line. Children are followed sequentially.</summary>
    Text,
    /// <summary>Player picks one child. Children whose conditions fail are hidden.</summary>
    Choice,
    /// <summary>Runtime picks one child uniformly at random (from those that pass conditions).</summary>
    Random,
}

/// <summary>
/// Describes the post-node transition. Stored compactly because most nodes
/// just continue.
/// </summary>
public sealed class JumpDef
{
    /// <summary>Continue / Exit / Jump.</summary>
    [JsonProperty("mode", Order = 1)]
    public JumpMode Mode { get; set; } = JumpMode.Continue;

    /// <summary>The <see cref="DialogueNodeDef.Tag"/> to jump to. Only used when <see cref="Mode"/> is <see cref="JumpMode.Jump"/>.</summary>
    [JsonProperty("targetTag", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public string? TargetTag { get; set; }
}

[JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
public enum JumpMode
{
    /// <summary>Descend into the node's children (the default for every text node).</summary>
    Continue,
    /// <summary>End the dialogue immediately after this node.</summary>
    Exit,
    /// <summary>Find the node tagged <see cref="JumpDef.TargetTag"/> and continue from there.</summary>
    Jump,
}
