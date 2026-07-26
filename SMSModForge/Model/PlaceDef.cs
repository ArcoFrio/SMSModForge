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

    /// <summary>Relative path to the secondary sprite PNG (2048×1136). Used by the level shader for blur/distance.</summary>
    [JsonProperty("secondarySprite", Order = 5)]
    public string SecondarySprite { get; set; } = "";

    /// <summary>Relative path to the mask sprite PNG (256×143). Drives shader masking.</summary>
    [JsonProperty("maskSprite", Order = 6)]
    public string MaskSprite { get; set; } = "";

    /// <summary>Strength of the parallax-mouse effect on this level. Vanilla outdoor levels use ~0.75; tight indoor rooms use ~0.05.</summary>
    [JsonProperty("parallaxStrength", Order = 7)]
    public float ParallaxStrength { get; set; } = 0.75f;

    /// <summary>
    /// If true, the cloned level keeps the Beach prototype's <c>Audio Source</c>
    /// (ocean ambience loop). False for indoor rooms.
    /// </summary>
    [JsonProperty("keepAudio", Order = 8)]
    public bool KeepAudio { get; set; } = false;

    /// <summary>
    /// If true, the cloned level keeps the Beach prototype's
    /// <c>Particle System (2)</c> (seagulls flying overhead). Independent
    /// of <see cref="KeepAudio"/>.
    /// </summary>
    [JsonProperty("keepSeagulls", Order = 9)]
    public bool KeepSeagulls { get; set; } = false;

    /// <summary>
    /// Whether the vanilla rain/snow weather system should activate when
    /// this place is the active level. <c>None</c> = no weather,
    /// <c>Inside</c> = indoor rain/snow particles, <c>Outside</c> =
    /// outdoor rain/snow particles.
    /// </summary>
    [JsonProperty("weatherType", Order = 10)]
    [JsonConverter(typeof(StringEnumConverter))]
    public WeatherType WeatherType { get; set; } = WeatherType.None;

    /// <summary>
    /// Navigator buttons that should be visible <em>while this place is the
    /// active level</em>. Each button targets another place by stable
    /// reference (vanilla name or pack-scoped key).
    /// </summary>
    [JsonProperty("navigatorButtons", Order = 11)]
    public List<NavigatorButtonDef> NavigatorButtons { get; set; } = new();

    /// <summary>
    /// The place's whole scene hierarchy: a tree of <see cref="GameObjectDef"/>
    /// nodes built under the level at runtime. Top-level entries are the layered
    /// sprite objects (backdrops, props, animated overlays) plus the single
    /// forced NPCs-root node (<see cref="GameObjectDef.RoleNpcRoot"/>) whose
    /// container subtree hosts the NPC placements. Each node is named so
    /// dialogue actions can show/hide/fade/move/spin it.
    /// </summary>
    [JsonProperty("gameObjects", Order = 12)]
    public List<GameObjectDef> GameObjects { get; set; } = new();

    /// <summary>
    /// Action groups run once each time this place's level flips
    /// inactive→active. Each group's conditions are evaluated at that
    /// moment; passing groups execute their actions. Re-entering the level
    /// runs them again (gate with variables for one-time effects).
    /// </summary>
    [JsonProperty("onEnter", Order = 13)]
    public List<LevelHookDef> OnEnter { get; set; } = new();

    /// <summary>Same as <see cref="OnEnter"/>, on the active→inactive edge.</summary>
    [JsonProperty("onExit", Order = 14)]
    public List<LevelHookDef> OnExit { get; set; } = new();

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
