using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One entry on the in-game Navigator strip. Visible while its parent
/// <see cref="PlaceDef"/> is the active level; clicking it routes the player
/// to <see cref="Target"/>.
/// <para/>
/// The mod-side resolver translates <see cref="Target"/> into a concrete
/// <c>5_Levels</c> sibling index <em>after</em> every pack has registered its
/// places, so two packs can both add buttons without sibling-index collisions
/// (the core of the navigator bug that Places.cs's hardcoded numeric indices
/// cause when two mods coexist).
/// </summary>
public sealed class NavigatorButtonDef
{
    /// <summary>
    /// Stable reference to the destination place. Either a vanilla level
    /// (e.g. <c>"vanilla:14_Beach"</c>) or another pack place
    /// (e.g. <c>"pack:MyMod.SecretCave"</c>, or <c>"self:&lt;key&gt;"</c> as
    /// a shorthand for a place in the same pack). See <see cref="PlaceTargetRef"/>.
    /// </summary>
    [JsonProperty("target", Order = 1)]
    public string Target { get; set; } = "";

    /// <summary>
    /// Button text. If empty, the resolver falls back to the target place's
    /// display name (or the vanilla in-game name).
    /// </summary>
    [JsonProperty("label", Order = 2)]
    public string Label { get; set; } = "";

    /// <summary>
    /// Optional name of a child under <c>12_AudioPlayer</c> to enable when
    /// this button is pressed (e.g. <c>"MyTrack"</c>). Disables every
    /// sibling audio source first. Leave empty for no music change.
    /// </summary>
    [JsonProperty("music", Order = 3)]
    public string Music { get; set; } = "";

    /// <summary>
    /// Optional visibility conditions. The top-level list is AND-ed — the
    /// button only appears when every entry is satisfied — and entries may be
    /// <c>All</c>/<c>Any</c> groups for nested AND/OR (same vocabulary as
    /// dialogue/rule conditions). An empty list (the default) means the button
    /// is unconditionally visible while the parent place is active. The
    /// converter reads the legacy <c>{variable, minValue}</c> shape too.
    /// </summary>
    [JsonProperty("conditions", Order = 4)]
    [JsonConverter(typeof(LegacyButtonConditionsConverter))]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    public bool ShouldSerializeConditions() => Conditions.Count > 0;
}
