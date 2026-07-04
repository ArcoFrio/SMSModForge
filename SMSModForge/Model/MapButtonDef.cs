using Newtonsoft.Json;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored entry on the World Map — a radial button placed inside
/// one of the map's district menus (Seaside, Foundry, Shopside, NeonRow,
/// TheLine). Clicking it travels to <see cref="Target"/>, just like a
/// per-place <see cref="NavigatorButtonDef"/>; the difference is the
/// visual placement (a radial button under
/// <c>World_Map/Canvas/Core/Radial_Buttons/&lt;District&gt;</c> rather
/// than a strip button under <c>9_MainCanvas/Navigator/MapButtons</c>).
/// <para/>
/// Modelled after the host mod's "a place button" radial button under
/// the Foundry district.
/// </summary>
public sealed class MapButtonDef
{
    /// <summary>
    /// Stable reference to the destination place — same wire format as
    /// <see cref="NavigatorButtonDef.Target"/>: <c>vanilla:&lt;goName&gt;</c>,
    /// <c>pack:&lt;packId&gt;.&lt;key&gt;</c>, or <c>self:&lt;key&gt;</c>.
    /// </summary>
    [JsonProperty("target", Order = 1)]
    public string Target { get; set; } = "";

    /// <summary>
    /// Name of the district radial menu to host this button under
    /// (e.g. <c>"Foundry"</c>). Must be a child name of
    /// <c>World_Map/Canvas/Core/Radial_Buttons</c>. See
    /// <see cref="WorldMapDistricts"/> for the known list.
    /// </summary>
    [JsonProperty("district", Order = 2)]
    public string District { get; set; } = "";

    /// <summary>
    /// Button text (e.g. <c>"House for Sale"</c>). If empty, the runtime
    /// falls back to the target token.
    /// </summary>
    [JsonProperty("label", Order = 3)]
    public string Label { get; set; } = "";

    /// <summary>
    /// Optional name of a child under <c>12_AudioPlayer</c> to enable
    /// when this button is pressed (e.g. <c>"MyTrack"</c>).
    /// Disables every sibling audio source first. Leave empty for no
    /// music change.
    /// </summary>
    [JsonProperty("music", Order = 4)]
    public string Music { get; set; } = "";

    /// <summary>
    /// Visibility conditions, evaluated per frame by the runtime against this
    /// pack's variable store. Same typed, groupable vocabulary as
    /// <see cref="NavigatorButtonDef.Conditions"/> and dialogue/rule
    /// conditions: the top-level list is AND-ed, and entries may be
    /// <c>All</c>/<c>Any</c> groups for nested AND/OR. Empty = always visible
    /// (the button still only shows while its district menu is open).
    /// The converter reads the legacy <c>{variable, minValue}</c> shape too.
    /// </summary>
    [JsonProperty("conditions", Order = 5)]
    [JsonConverter(typeof(LegacyButtonConditionsConverter))]
    public List<NodeConditionDef> Conditions { get; set; } = new();

    public bool ShouldSerializeConditions() => Conditions.Count > 0;
}
