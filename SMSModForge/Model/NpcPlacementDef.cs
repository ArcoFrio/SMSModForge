using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One appearance of an <see cref="NpcDef"/> inside a place's scene. The
/// placement lives as a node in the place's GameObject tree (under the forced
/// NPCs-root node's container subtree), so its parent chain is its tree
/// position — the placement itself owns only its local transform and whether
/// it starts active (reference NPCs are parked inactive and switched on by
/// host-mod code or integration rules, by GameObject name).
/// </summary>
public sealed class NpcPlacementDef
{
    /// <summary>Key of the <see cref="NpcDef"/> to instantiate.</summary>
    [JsonProperty("npc", Order = 1)]
    public string Npc { get; set; } = "";

    /// <summary>
    /// GameObject name — the handle <c>SetGameObjectActive</c> (and any host
    /// mod) targets. Blank falls back to the NPC key; give it an explicit
    /// name when the same NPC is placed twice in one level.
    /// </summary>
    [JsonProperty("name", Order = 2)]
    public string Name { get; set; } = "";

    /// <summary>The NPC body's own local transform (what a Unity inspector
    /// shows on the NPC object). Negative scale X mirrors the pose.</summary>
    [JsonProperty("body", Order = 4)]
    public NpcTransform Body { get; set; } = new();

    /// <summary>The shadow circle's local transform (offset under the NPC,
    /// in-plane spin via rotZ, tilt via rotX/rotY, squash via scale).</summary>
    [JsonProperty("shadow", Order = 5)]
    public NpcTransform Shadow { get; set; } = new() { Y = -2f, ScaleY = 0.45f };

    /// <summary>The blink overlay's local transform (usually a small offset).</summary>
    [JsonProperty("blink", Order = 6)]
    public NpcTransform Blink { get; set; } = new();

    /// <summary>The particle (Wet) emitter's local transform.</summary>
    [JsonProperty("wet", Order = 7)]
    public NpcTransform Wet { get; set; } = new() { X = -0.3f, Y = 7.7f };

    /// <summary>
    /// How the Wet emitter looks (<see cref="WetEffectDef"/>): which of the game's two wet effects, the box its drops
    /// start in (dragged by its sides in the place preview - after 1.10.0, the author: "make sure we're not actually
    /// changing the droplet size by doing that, just the area of the effect"), and its settings. Null: the towel's
    /// own, as every placement before it.
    /// </summary>
    [JsonProperty("wetEffect", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public WetEffectDef? WetEffect { get; set; }

    /// <summary>Whether the NPC GameObject starts active. False for the
    /// usual "parked variant a rule activates" pattern.</summary>
    [JsonProperty("startActive", Order = 8)]
    public bool StartActive { get; set; } = false;

    /// <summary>
    /// Sorting order for this placement's body, overriding the NPC's own.
    /// <para/>
    /// Depth is a property of the ROOM, not of the character: the same NPC
    /// stands behind one level's furniture and in front of another's, and every
    /// vanilla level picks its own orders. So it lives here with the transforms,
    /// for the same reason they do. Null means "use the NPC's own", which is
    /// what every placement authored before this field existed does.
    /// </summary>
    [JsonProperty("sortingOrder", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public int? SortingOrder { get; set; }

    /// <summary>
    /// Generic utility components attached to this NPC's GameObject — the same
    /// vocabulary any <see cref="GameObjectDef"/> takes. They're added before
    /// the NPC is activated, so a <c>FadeInSprite</c> here makes this NPC fade
    /// in when it's switched on instead of popping. (Nothing fades by default.)
    /// </summary>
    [JsonProperty("components", Order = 9)]
    public List<ComponentDef> Components { get; set; } = new();

    /// <summary>
    /// GameObjects parented under this NPC, alongside its built-in
    /// <c>Circle</c> / <c>Blink</c> / <c>Wet</c> parts — props that ride along
    /// with the pose (a held object, an effect). Built with LOCAL transforms so
    /// they compose with the NPC's body.
    /// </summary>
    [JsonProperty("children", Order = 10)]
    public List<GameObjectDef> Children { get; set; } = new();

    /// <summary>
    /// Activation conditions: the object switches itself on/off as these pass,
    /// instead of a rule having to drive it.
    /// </summary>
    [JsonProperty("activeConditions", Order = 11)]
    public List<NodeConditionDef> ActiveConditions { get; set; } = new();

    /// <summary>Switch back off when the conditions stop matching.</summary>
    [JsonProperty("deactivateWhenUnmet", Order = 12)]
    public bool DeactivateWhenUnmet { get; set; } = true;

    /// <summary>
    /// How the character drifts as the cursor moves (after 1.10.0, the author asked for "parallax settings for
    /// individual NPCs"), as an object's are read (<see cref="GameObjectDef.ParallaxDisabled"/>): off (the default,
    /// and every character placed before) it stays with what it stands under - the NPCs object, which moves with
    /// the level's front picture, as the game's own crowds do. On, it drifts by <see cref="ParallaxStrength"/>, or
    /// with the level's back picture when that is unset. Its shadow, blink, reflection and children go with it.
    /// <para/>
    /// Written only when on, so a pack whose characters do not drift saves as it did.
    /// </summary>
    [JsonProperty("parallaxDisabled", Order = 13)]
    public bool ParallaxDisabled { get; set; } = true;

    /// <summary>Its own strength; null drifts with the level's back picture (none where the level has none).</summary>
    [JsonProperty("parallaxStrength", Order = 14, NullValueHandling = NullValueHandling.Ignore)]
    public float? ParallaxStrength { get; set; }

    /// <summary>Drift with the cursor instead of against it. Null: as the level's back picture's drift has it (not
    /// reversed where the level has none), as for an object (<see cref="GameObjectDef.ParallaxReversed"/>); written
    /// whenever it is set, false too.</summary>
    [JsonProperty("parallaxReversed", Order = 15, NullValueHandling = NullValueHandling.Ignore)]
    public bool? ParallaxReversed { get; set; }

    public bool ShouldSerializeParallaxDisabled() => !ParallaxDisabled;
    public bool ShouldSerializeParallaxStrength() => !ParallaxDisabled && ParallaxStrength.HasValue;
    public bool ShouldSerializeParallaxReversed() => !ParallaxDisabled && ParallaxReversed.HasValue;

    public bool ShouldSerializeComponents() => Components.Count > 0;
    public bool ShouldSerializeChildren() => Children.Count > 0;
    public bool ShouldSerializeActiveConditions() => ActiveConditions.Count > 0;
}
