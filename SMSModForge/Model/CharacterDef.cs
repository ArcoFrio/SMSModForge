using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A single character with one or more outfits — the convention being that one
/// character has many outfit-shaped GameObjects (e.g. <c>mygirl</c>,
/// <c>mygirlSwim</c>, ...).
/// </summary>
public sealed class CharacterDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "Newgirl";

    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Girl";

    /// <summary>
    /// Gifts this character likes. Each entry is surfaced to the host mod (which
    /// owns gift-giving) through the generic variable bridge at load time.
    /// </summary>
    [JsonProperty("giftLikes", Order = 3)]
    public List<string> GiftLikes { get; set; } = new();

    [JsonProperty("outfits", Order = 4)]
    public List<OutfitDef> Outfits { get; set; } = new();
}
