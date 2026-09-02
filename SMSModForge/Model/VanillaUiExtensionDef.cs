using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored change to a UI the game already has.
/// <para/>
/// The unit being extended is a <em>base</em>, not a canvas. The game's main
/// canvas is a single object holding 4087 others, but it is not a single UI:
/// its children are the shop, the calendar, the payout screen, the quit dialog,
/// switched on one at a time. Asking an author to open all 4087 to add a button
/// to the payout screen would be the same mistake as asking them to open every
/// level to change one room, so <see cref="Source"/> names the child.
/// <para/>
/// Like a vanilla place extension, this stores only a delta. Nodes carrying a
/// <see cref="UiNodeDef.Bind"/> adjust something that exists; nodes without one
/// are additions. Anything the author did not touch is left as the game made
/// it, so the extension follows the vanilla UI through a game update instead of
/// pinning it to the shape it had when the pack was written.
/// </summary>
public sealed class VanillaUiExtensionDef
{
    /// <summary>
    /// The vanilla UI base being extended, as a token: <c>vanillaui:</c>
    /// followed by the base's path, e.g. <c>vanillaui:9_MainCanvas/Payout</c>.
    /// <para/>
    /// Two sibling objects can share a name — the scene has a pair of them in
    /// two places — so a path is not always unique. Those, and only those,
    /// carry a <c>#n</c> suffix in sibling order. See
    /// <see cref="VanillaUiCatalog"/>, which decides the suffix once so the
    /// editor and the runtime cannot disagree about which object was meant.
    /// </summary>
    [JsonProperty("source", Order = 1)]
    public string Source { get; set; } = "";

    /// <summary>Changes and additions, in the base's own coordinate space.</summary>
    [JsonProperty("nodes", Order = 2)]
    public List<UiNodeDef> Nodes { get; set; } = new();

    public bool ShouldSerializeNodes() => Nodes.Count > 0;
}
