using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SMSModForge.Model;

/// <summary>
/// One custom place = one new level GameObject + a map button + a roomtalk
/// node. Maps 1:1 to a single <c>Places.CreateNewPlace(...)</c> call on the
/// mod side: <c>CreateNewPlace(index, name, pathName, buttonText, parallaxStrength)</c>.
/// <para/>
/// The mod-side loader allocates the sibling index dynamically at load time
/// based on the order packs are processed — packs do NOT bake absolute level
/// indices, so two mods can both define places without colliding. Navigator
/// references between places are resolved by stable name (see
/// <see cref="PlaceTargetRef"/>), not by index.
/// </summary>
public sealed class PlaceDef
{
    /// <summary>
    /// Pack-local key. Combined with the pack id to form a globally-unique
    /// reference token <c>"pack:&lt;packId&gt;.&lt;key&gt;"</c>. Required.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "newplace";

    /// <summary>
    /// Internal name used for the level / map button / roomtalk GameObjects.
    /// Becomes <c>{allocatedIndex}_{InternalName}</c> at runtime. Should be a
    /// stable CamelCase identifier (no spaces). If empty, the loader falls
    /// back to <see cref="Key"/>.
    /// </summary>
    [JsonProperty("internalName", Order = 2)]
    public string InternalName { get; set; } = "Newplace";

    /// <summary>
    /// Human-facing label shown on the navigator button. Free text.
    /// </summary>
    [JsonProperty("displayName", Order = 3)]
    public string DisplayName { get; set; } = "New Place";

    /// <summary>Relative path (from pack root) to the base sprite PNG (2048×1136).</summary>
    [JsonProperty("baseSprite", Order = 4)]
    public string BaseSprite { get; set; } = "";

    /// <summary>
    /// Relative path to the secondary sprite PNG (2048×1136) — the layer BEHIND
    /// the base art (sorting order −12 against the base's −10), i.e. the backdrop
    /// seen past the room in front. Used by the level shader for blur/distance.
    /// <para/>
    /// The two layers are called Base and Secondary throughout: these keys, the
    /// Places tab's sprite / mask / sorting-order fields, and the Layer picker a
    /// <c>SetSprite</c> action shows for the Places category. "Backdrop"
    /// describes what this layer is, and is never a separate name for it.
    /// </summary>
    [JsonProperty("secondarySprite", Order = 5)]
    public string SecondarySprite { get; set; } = "";

    /// <summary>
    /// Relative path to the BASE sprite's mask PNG (256×143). Shown as
    /// "Base mask" in the Places tab, pairing with
    /// <see cref="SecondaryMaskSprite"/>.
    /// <para/>
    /// Applied to the base sprite's own material as <c>_MaskTex</c>, and nowhere
    /// else — the secondary sprite and every GameObject in the place are
    /// untouched by it. The material is cloned per level first, so a pack's mask
    /// can't leak into the vanilla levels that share the Beach material.
    /// </summary>
    [JsonProperty("maskSprite", Order = 6)]
    public string MaskSprite { get; set; } = "";

    /// <summary>
    /// Optional mask for the SECONDARY sprite, giving that layer a jiggle of its
    /// own. Shown as "Secondary mask" in the Places tab.
    /// <para/>
    /// This goes beyond what vanilla does: a level's secondary sprite ships on
    /// plain <c>Sprite-Lit-Default</c>, with no mask and no displacement, and
    /// only the base art carries the jiggle material. Setting this hands the
    /// secondary a clone of the level's own jiggle material so it can be
    /// displaced too — which is how you get water or foliage moving at a
    /// different depth from the room in front of it. Blank leaves it exactly as
    /// vanilla has it, so no existing place changes.
    /// </summary>
    [JsonProperty("secondaryMaskSprite", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public string SecondaryMaskSprite { get; set; } = "";

    public bool ShouldSerializeSecondaryMaskSprite() => !string.IsNullOrEmpty(SecondaryMaskSprite);

    /// <summary>
    /// SpriteRenderer sorting order of the base level art. Null keeps the Beach
    /// prototype's −10, which is what every vanilla level but four uses.
    /// <para/>
    /// Worth setting when a place needs objects to sit BEHIND the level art:
    /// sorting order is the only thing that decides it, since every sprite in
    /// the game — all 734 of them — is on the one "Default" sorting layer.
    /// </summary>
    [JsonProperty("baseSortingOrder", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
    public int? BaseSortingOrder { get; set; }

    /// <summary>Sorting order of the secondary (distance) sprite. Null keeps the
    /// prototype's −12. Vanilla levels put it at −12 or −15, i.e. behind the base
    /// art but in front of anything deliberately parked below it.</summary>
    [JsonProperty("secondarySortingOrder", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public int? SecondarySortingOrder { get; set; }

    /// <summary>Strength of the parallax-mouse effect on the level's MAIN
    /// sprite. Every vanilla level uses 0.75 here — the depth comes from the
    /// backdrop moving differently, not from this.</summary>
    [JsonProperty("parallaxStrength", Order = 10)]
    public float ParallaxStrength { get; set; } = 0.75f;

    /// <summary>
    /// Parallax strength of the SECONDARY (distance) sprite.
    /// <para/>
    /// This is what actually produces the depth: in the vanilla levels the two
    /// sprites almost never share a value (56 of the 61 pairs differ), with the
    /// main sprite pinned at 0.75 and the backdrop set to 0.1, 0.5 or 1.5 —
    /// below for a distant background that should barely shift, above for one
    /// that should overshoot the foreground. Giving both the same number, which
    /// is what happened before this existed, moves the whole level as one flat
    /// card and cancels the effect out.
    /// <para/>
    /// Null means "match the main sprite", which is what every place did before
    /// this setting existed — so an older pack keeps behaving exactly as it did,
    /// including the tight indoor rooms authored at 0.05 that a blanket 0.5
    /// default would have thrown wide open. Vanilla's own backdrops are 0.1,
    /// 0.5 or 1.5; 0.5 is the most common.
    /// </summary>
    [JsonProperty("parallaxSecondaryStrength", Order = 11, NullValueHandling = NullValueHandling.Ignore)]
    public float? ParallaxSecondaryStrength { get; set; }

    /// <summary>Invert the main sprite's parallax direction — it moves WITH the
    /// cursor rather than against it.</summary>
    [JsonProperty("parallaxReversed", Order = 12)]
    public bool ParallaxReversed { get; set; } = false;

    /// <summary>Invert the secondary sprite's parallax direction. Vanilla uses
    /// this once (53_Hotelroom's backdrop), to make a background drift opposite
    /// the room in front of it.</summary>
    [JsonProperty("parallaxSecondaryReversed", Order = 13)]
    public bool ParallaxSecondaryReversed { get; set; } = false;

    /// <summary>
    /// If true, the cloned level keeps the Beach prototype's <c>Audio Source</c>
    /// (ocean ambience loop). False for indoor rooms.
    /// </summary>
    [JsonProperty("keepAudio", Order = 14)]
    public bool KeepAudio { get; set; } = false;

    /// <summary>
    /// If true, the cloned level keeps the Beach prototype's
    /// <c>Particle System (2)</c> (seagulls flying overhead). Independent
    /// of <see cref="KeepAudio"/>.
    /// </summary>
    [JsonProperty("keepSeagulls", Order = 15)]
    public bool KeepSeagulls { get; set; } = false;

    /// <summary>
    /// Whether the vanilla rain/snow weather system should activate when
    /// this place is the active level. <c>None</c> = no weather,
    /// <c>Inside</c> = indoor rain/snow particles, <c>Outside</c> =
    /// outdoor rain/snow particles.
    /// </summary>
    [JsonProperty("weatherType", Order = 16)]
    [JsonConverter(typeof(StringEnumConverter))]
    public WeatherType WeatherType { get; set; } = WeatherType.None;

    /// <summary>
    /// Navigator buttons that should be visible <em>while this place is the
    /// active level</em>. Each button targets another place by stable
    /// reference (vanilla name or pack-scoped key).
    /// </summary>
    [JsonProperty("navigatorButtons", Order = 17)]
    public List<NavigatorButtonDef> NavigatorButtons { get; set; } = new();

    /// <summary>
    /// The place's whole scene hierarchy: a tree of <see cref="GameObjectDef"/>
    /// nodes built under the level at runtime. Top-level entries are the layered
    /// sprite objects (backdrops, props, animated overlays) plus the single
    /// forced NPCs-root node (<see cref="GameObjectDef.RoleNpcRoot"/>) whose
    /// container subtree hosts the NPC placements. Each node is named so
    /// dialogue actions can show/hide/fade/move/spin it.
    /// </summary>
    [JsonProperty("gameObjects", Order = 18)]
    public List<GameObjectDef> GameObjects { get; set; } = new();

    /// <summary>
    /// Action groups run once each time this place's level flips
    /// inactive→active. Each group's conditions are evaluated at that
    /// moment; passing groups execute their actions. Re-entering the level
    /// runs them again (gate with variables for one-time effects).
    /// </summary>
    [JsonProperty("onEnter", Order = 19)]
    public List<LevelHookDef> OnEnter { get; set; } = new();

    /// <summary>Same as <see cref="OnEnter"/>, on the active→inactive edge.</summary>
    [JsonProperty("onExit", Order = 20)]
    public List<LevelHookDef> OnExit { get; set; } = new();

    // Off is the overwhelming default (vanilla reverses exactly one sprite in
    // the whole game), so an unreversed place writes nothing rather than two
    // false flags onto every entry.
    public bool ShouldSerializeParallaxReversed() => ParallaxReversed;
    public bool ShouldSerializeParallaxSecondaryReversed() => ParallaxSecondaryReversed;
    public bool ShouldSerializeGameObjects() => GameObjects.Count > 0;
    public bool ShouldSerializeOnEnter() => OnEnter.Count > 0;
    public bool ShouldSerializeOnExit() => OnExit.Count > 0;
}

/// <summary>
/// One conditions-gated action group on a place's enter/exit edge — the same
/// conditions + actions vocabulary dialogue nodes and integration rules use.
/// </summary>
public sealed class LevelHookDef
{
    /// <summary>All must pass (at the moment the edge fires) for the group's actions to run.</summary>
    [JsonProperty("conditions", Order = 1)]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    [JsonProperty("actions", Order = 2)]
    public List<NodeActionDef> Actions { get; set; } = new();

    public bool ShouldSerializeConditions() => Conditions.Count > 0;
}

/// <summary>
/// Whether the vanilla weather system (rain / snow particles) should activate
/// when this place is the active level.
/// </summary>
public enum WeatherType
{
    /// <summary>No weather effects.</summary>
    None,
    /// <summary>Indoor weather particles (muted rain on windows, gentle snow).</summary>
    Inside,
    /// <summary>Outdoor weather particles (full rain / snow).</summary>
    Outside
}
