using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One level NPC — a posed character sprite that lives inside a place's
/// <c>NPCs</c> hierarchy rather than the bust manager. Structurally a slim
/// bust: a SpriteRenderer with the jiggle material (per-NPC mask + uniforms),
/// an optional eyes-closed <c>Blink</c> overlay that shares the parent's
/// material, an optional <c>Circle</c> shadow (a flat dark ellipse deformed
/// purely by its transform), and an optional <c>Wet</c> droplet particle
/// clone.
/// <para/>
/// The def is the reusable *look* of one pose/outfit variant. WHERE it
/// appears — level, chain path under <c>NPCs</c>, transform, initial
/// active state — is authored per-place as a <see cref="NpcPlacementDef"/>,
/// so the same variant can be placed in several rooms.
/// </summary>
public sealed class NpcDef
{
    /// <summary>Pack-local key placements reference. Required, unique.</summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "newnpc";

    /// <summary>Editor-facing label. Falls back to <see cref="Key"/> when blank.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "";

    /// <summary>Relative path (from pack root) to the pose PNG. Any size —
    /// the runtime reads the image's own dimensions (100 px/unit).</summary>
    [JsonProperty("sprite", Order = 3)]
    public string Sprite { get; set; } = "";

    /// <summary>Relative path to the jiggle-mask PNG (R=bounce, G=wave,
    /// B=noise, A=intensity — same convention as bust masks).</summary>
    [JsonProperty("mask", Order = 4)]
    public string Mask { get; set; } = "";

    /// <summary>SpriteRenderer sorting order of the NPC itself. The reference
    /// NPCs sit at -1, under the level's props; their Blink is always this
    /// +1 and the shadow uses its own order below.</summary>
    [JsonProperty("sortingOrder", Order = 5)]
    public int SortingOrder { get; set; } = -1;

    /// <summary>Shader uniforms for the jiggle material. Same block busts
    /// use, but seeded with the NPC preset (softer, denser noise, pixel
    /// snap on) rather than the bust preset.</summary>
    [JsonProperty("jiggle", Order = 6)]
    public JiggleParams Jiggle { get; set; } = NewNpcJiggle();

    [JsonProperty("blink", Order = 7)]
    public NpcBlinkDef Blink { get; set; } = new();

    [JsonProperty("shadow", Order = 8)]
    public NpcShadowDef Shadow { get; set; } = new();

    [JsonProperty("wet", Order = 9)]
    public NpcWetDef Wet { get; set; } = new();

    /// <summary>
    /// Mirror the pose downward as a floor reflection — the pattern several
    /// vanilla levels use, where an NPC carries a child holding the same sprite
    /// at scale (1, -1). Off by default; nothing about an NPC implies it wants
    /// one, and it costs a second renderer.
    /// </summary>
    [JsonProperty("reflection", Order = 10)]
    public NpcReflectionDef Reflection { get; set; } = new();

    /// <summary>The jiggle preset every reference NPC ships with (the bust
    /// defaults differ: 3 / -0.02 / 4 / 5 / 0.5, no snap).</summary>
    public static JiggleParams NewNpcJiggle() => new()
    {
        Speed = 4f,
        Strength = 0.02f,
        Frequency = 2f,
        NoiseScale = 12f,
        NoiseSpeed = 2f,
        NoiseStrength = 0.06f,
        PixelSnap = true,
    };
}

/// <summary>
/// The eyes-closed overlay child — non-positional properties only. Its
/// transform (offset) lives on the placement. Empty <see cref="Sprite"/> = no
/// Blink child. Shares the parent NPC's material so the jiggle lines up, and
/// renders one sorting step above it. Timing mirrors the vanilla component.
/// </summary>
public sealed class NpcBlinkDef
{
    /// <summary>Relative path to the eyes-closed PNG. Blank = no blink.</summary>
    [JsonProperty("sprite", Order = 1)]
    public string Sprite { get; set; } = "";

    /// <summary>Shortest time the eyes stay open, in seconds.</summary>
    [JsonProperty("minWait", Order = 2)]
    public float MinWait { get; set; } = 2f;

    /// <summary>Longest time the eyes stay open.</summary>
    [JsonProperty("maxWait", Order = 3)]
    public float MaxWait { get; set; } = 5f;

    /// <summary>How long the eyes stay closed per blink.</summary>
    [JsonProperty("hold", Order = 4)]
    public float Hold { get; set; } = 0.2f;
}

/// <summary>
/// A downward mirror of the pose, standing in for a reflection on a wet or
/// polished floor. Reproduces what vanilla levels do by hand: a child of the
/// NPC holding the same sprite, flipped on Y.
/// </summary>
public sealed class NpcReflectionDef
{
    [JsonProperty("enabled", Order = 1)]
    public bool Enabled { get; set; } = false;

    /// <summary>How visible the mirrored copy is. Defaulted to what the game's
    /// own NPCCoreReflectionMat uses (_Alpha 0.58), so an untouched reflection
    /// matches the ones already in the levels.</summary>
    [JsonProperty("alpha", Order = 2)]
    public float Alpha { get; set; } = 0.58f;

    /// <summary>
    /// Tint applied to the mirrored copy. Defaulted to the muted mauve the
    /// game's own street reflections use (#AD92AA on Downtown's) — a reflection
    /// takes the colour of what it's lying on, so an untinted one reads as a
    /// second character rather than something on the pavement.
    /// </summary>
    [JsonProperty("tint", Order = 3)]
    public string Tint { get; set; } = "#AD92AA";

    /// <summary>Vertical offset from the pose's origin, in the NPC's local
    /// units. Negative pushes it further down. Defaulted to what Downtown's
    /// reflections sit at (-2.16 to -2.46 across its five).</summary>
    [JsonProperty("offsetY", Order = 4)]
    public float OffsetY { get; set; } = -2.3f;

    /// <summary>Sorting order for the mirrored copy. Vanilla puts these in FRONT
    /// of the body — Downtown's bodies sit at -9 and their reflections at 0 —
    /// which is what makes them read as lying on the floor.</summary>
    [JsonProperty("sortingOrder", Order = 5)]
    public int SortingOrder { get; set; } = 0;
}

/// <summary>
/// The soft floor shadow — a procedurally generated dark circle — non-positional
/// properties only. Its transform (offset / rotation / squash) lives on the
/// placement, since the shadow lies differently under each posed instance.
/// </summary>
public sealed class NpcShadowDef
{
    [JsonProperty("enabled", Order = 1)]
    public bool Enabled { get; set; } = true;

    /// <summary>RGBA tint of the circle. Reference shadows are half-transparent black.</summary>
    [JsonProperty("color", Order = 2)]
    public string Color { get; set; } = "#00000082";

    /// <summary>Sorting order — well under the NPC so it never overlaps limbs.</summary>
    [JsonProperty("sortingOrder", Order = 3)]
    public int SortingOrder { get; set; } = -3;
}

/// <summary>
/// The "Wet" droplet particle child, cloned from the same vanilla source busts
/// use — non-positional properties only. Its emitter transform lives on the
/// placement. Off = the child isn't built at all.
/// </summary>
public sealed class NpcWetDef
{
    [JsonProperty("enabled", Order = 1)]
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the particle GameObject starts active.</summary>
    [JsonProperty("startActive", Order = 2)]
    public bool StartActive { get; set; } = true;
}
