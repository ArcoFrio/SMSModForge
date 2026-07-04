using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A pack-defined speaker. Dialogue nodes reference actors by <see cref="Key"/>;
/// the plugin maintains a runtime mapping from actor → currently-active bust
/// (initialised from <see cref="DefaultBustKey"/>) and drives the visual
/// state from <c>Dialogue.EventStartNext</c>. Unlike GC2's actors-are-SOs
/// model, pack actors are pure pack data — no asset bundle authoring
/// required.
/// <para/>
/// The <see cref="DisplayName"/> is what the dialogue UI shows above the
/// line; <see cref="DefaultBustKey"/> picks which outfit GameObject under
/// <c>2_Bust_Manager</c> represents the actor (matches a bust's
/// <c>OutfitDef.GameObjectName</c> — bust GOs are named after their outfit's
/// GO name when built by the pack plugin).
/// </summary>
public sealed class ActorDef
{
    /// <summary>Pack-local key. Used to address the actor from dialogue nodes.</summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "newactor";

    /// <summary>Speech-line display name (e.g. "Anna", "???"). Shown in the dialogue UI.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Actor";

    /// <summary>
    /// The bust GO name (matching an <see cref="OutfitDef.GameObjectName"/>
    /// in this pack) to display when this actor first appears and no node
    /// override changes the bust. Should be one of <see cref="Outfits"/>.
    /// Empty means "no bust" — the actor is a disembodied voice / narrator.
    /// </summary>
    [JsonProperty("defaultBustKey", Order = 3)]
    public string DefaultBustKey { get; set; } = "";

    /// <summary>
    /// Every bust GameObject name this actor can wear. A dialogue node
    /// picks one via <see cref="DialogueNodeDef.Outfit"/> to switch the
    /// actor's bust mid-dialogue — the runtime deactivates the actor's
    /// previously-shown bust and activates the new one. Each entry matches
    /// an <see cref="OutfitDef.GameObjectName"/> in this pack (or a vanilla
    /// bust GO name). <see cref="DefaultBustKey"/> is the entry shown until
    /// a node overrides it; an empty list means the actor only ever uses
    /// <see cref="DefaultBustKey"/>.
    /// </summary>
    [JsonProperty("outfits", Order = 4)]
    public List<string> Outfits { get; set; } = new();

    /// <summary>
    /// Optional speech-bubble name colour as a hex string
    /// (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>). The plugin registers this
    /// against the active <c>TMPWordColorizer</c> at runtime so the
    /// actor's name in the speech UI is painted in that colour — same
    /// mechanism vanilla dialogues use. Empty / unset = use the
    /// colorizer's default colour (typically white).
    /// </summary>
    [JsonProperty("nameColor", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public string? NameColor { get; set; }

    /// <summary>
    /// Per-expression visual overrides. Maps a pack-local expression key
    /// (e.g. "Happy") to the GO name of the expression child renderer
    /// under <c>MBase1/Expressions/&lt;name&gt;</c> on the bust. Empty list
    /// means the four standard expressions (<c>Happy/Angry/Sad/Flirty</c>)
    /// each map to a child of the same name on the bust.
    /// </summary>
    [JsonProperty("expressions", Order = 6)]
    public List<ActorExpressionDef> Expressions { get; set; } = new();

    /// <summary>
    /// Optional per-actor typewriter voice — the blip that plays as the
    /// dialogue line types out. Maps to GameCreator2's <c>Actor.Typewriter</c>
    /// (frequency + a min/max pitch range). Unset means the runtime applies a
    /// neutral default so the line still blips; set it to pick a Male/Female
    /// preset or dial the pitch in by hand.
    /// </summary>
    [JsonProperty("typewriter", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public TypewriterDef? Typewriter { get; set; }
}

/// <summary>
/// Per-actor typewriter voice settings, mirrored onto GameCreator2's
/// <c>Typewriter</c> (<c>m_Frequency</c> int, <c>m_Pitch</c> Vector2 min/max)
/// at runtime. The typing blip clip itself is inherited from the game's
/// existing actors — only the cadence and pitch are pack-authored.
/// </summary>
public sealed class TypewriterDef
{
    /// <summary>Editor-only hint for which named preset the values match
    /// (<c>"M"</c>, <c>"F"</c>, or <c>"Custom"</c>). The runtime ignores it.</summary>
    [JsonProperty("template", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
    public string? Template { get; set; }

    /// <summary>Whether the typewriter voice runs for this actor. Off = silent typing.</summary>
    [JsonProperty("enabled", Order = 2, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [System.ComponentModel.DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    /// <summary>GC2 <c>Typewriter.m_Frequency</c> — how often the blip fires as text types.</summary>
    [JsonProperty("frequency", Order = 3)]
    public int Frequency { get; set; } = 45;

    /// <summary>Low end of GC2 <c>Typewriter.m_Pitch</c> (the blip randomises between min and max).</summary>
    [JsonProperty("pitchMin", Order = 4)]
    public float PitchMin { get; set; } = 1.0f;

    /// <summary>High end of GC2 <c>Typewriter.m_Pitch</c>.</summary>
    [JsonProperty("pitchMax", Order = 5)]
    public float PitchMax { get; set; } = 1.5f;
}

public sealed class ActorExpressionDef
{
    /// <summary>Pack-local expression key referenced from <see cref="DialogueNodeDef.Expression"/>.</summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "";

    /// <summary>
    /// Name of the child under <c>&lt;bust&gt;/MBase1/Expressions/</c> to
    /// activate. Defaults to <see cref="Key"/> when empty.
    /// </summary>
    [JsonProperty("expressionGoName", Order = 2)]
    public string ExpressionGoName { get; set; } = "";
}
