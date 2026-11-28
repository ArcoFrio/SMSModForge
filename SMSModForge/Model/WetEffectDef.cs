using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// How a placed character's wet effect looks (after 1.10.0, the author: "there should be a toggle between the two wet
/// types for both busts and npcs"; "When selecting the Wet GO in the Places preview, I'd also like to be able to tweak
/// some of the key settings of the particle effect, including color"; "NPC sets them, placements can change"). Every
/// field is optional: the kind and the look absent, the NPC's (<see cref="NpcWetDef"/>, see <see cref="Merged"/>), and
/// the game's where the NPC sets none either; the box, the top and the floor are the placement's alone. The Wet
/// object's own scale still multiplies the box and the drops, as in the game.
/// <list type="bullet">
/// <item><see cref="KindWet"/> ("Wet" on screen): Anna's towel bust's two systems (2_Bust_Manager/Anna_Towel/MBase1/
/// Particle System and Particle System (1)): a few thin white streaks over the body, drawn over everything.</item>
/// <item><see cref="KindShower"/>: the bathroom's AnnaInShower NPC's system: a shower of fine blue-grey streaks from a
/// small box, lit by the room's lamps, sorted with the level's art.</item>
/// </list>
/// </summary>
public sealed class WetEffectDef
{
    public const string KindWet = "wet", KindShower = "shower";

    /// <summary>The wet's box (Anna's towel systems' shape scale) and the shower's.</summary>
    public const float WetWidth = 3.21f, WetHeight = 8.45f, ShowerWidth = 2f, ShowerHeight = 1f;

    [JsonProperty("kind", Order = 1, NullValueHandling = NullValueHandling.Ignore)] public string? Kind { get; set; }

    /// <summary>The box the drops start in, the Wet object's own units (the particle shape's scale).</summary>
    [JsonProperty("width", Order = 2, NullValueHandling = NullValueHandling.Ignore)] public float? Width { get; set; }
    [JsonProperty("height", Order = 3, NullValueHandling = NullValueHandling.Ignore)] public float? Height { get; set; }

    /// <summary>The shower's drops start along the box's top side only.</summary>
    [JsonProperty("fromTop", Order = 4, NullValueHandling = NullValueHandling.Ignore)] public bool? FromTop { get; set; }

    /// <summary>The shower's drops vanish where they leave the box: at its floor.</summary>
    [JsonProperty("floor", Order = 5, NullValueHandling = NullValueHandling.Ignore)] public bool? Floor { get; set; }

    /// <summary>A colour (#RRGGBB or #RRGGBBAA) the game's drop colour is multiplied by.</summary>
    [JsonProperty("tint", Order = 6, NullValueHandling = NullValueHandling.Ignore)] public string? Tint { get; set; }

    /// <summary>Times the game's drops a second.</summary>
    [JsonProperty("amount", Order = 7, NullValueHandling = NullValueHandling.Ignore)] public float? Amount { get; set; }

    /// <summary>Times the game's drop size.</summary>
    [JsonProperty("size", Order = 8, NullValueHandling = NullValueHandling.Ignore)] public float? Size { get; set; }

    /// <summary>Times the game's fall (its gravity).</summary>
    [JsonProperty("fall", Order = 9, NullValueHandling = NullValueHandling.Ignore)] public float? Fall { get; set; }

    [JsonIgnore] public bool IsShower => Kind == KindShower;
    public static bool ShowerKind(string? kind) => kind == KindShower;
    public static float DefaultWidth(string? kind) => ShowerKind(kind) ? ShowerWidth : WetWidth;
    public static float DefaultHeight(string? kind) => ShowerKind(kind) ? ShowerHeight : WetHeight;

    /// <summary>Nothing set: the same as no effect settings at all.</summary>
    [JsonIgnore]
    public bool Empty => Kind == null && Width == null && Height == null && FromTop == null && Floor == null && Tint == null
                         && Amount == null && Size == null && Fall == null;

    public WetEffectDef Clone() => (WetEffectDef)MemberwiseClone();

    /// <summary>
    /// What a placed character's wet effect is: its NPC's look (<see cref="NpcWetDef"/>) with what the placement sets
    /// over it (after 1.10.0, the author: "NPC sets them, placements can change"), and the placement's own box, top
    /// and floor. Null where neither sets it: the game's own.
    /// </summary>
    public static WetEffectDef Merged(NpcWetDef? npc, WetEffectDef? placed) => new()
    {
        Kind = placed?.Kind ?? npc?.Kind,
        Tint = placed?.Tint ?? npc?.Tint,
        Amount = placed?.Amount ?? npc?.Amount,
        Size = placed?.Size ?? npc?.Size,
        Fall = placed?.Fall ?? npc?.Fall,
        Width = placed?.Width,
        Height = placed?.Height,
        FromTop = placed?.FromTop,
        Floor = placed?.Floor,
    };
}
