using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One texture on one of the game's own busts, replaced by the pack's.
/// <para/>
/// A list of these rather than a field per slot, and the reason is the whole
/// point of the feature: an author replacing Anna's mouth must not thereby say
/// anything about her eyes. An absent entry is not "replace with nothing" — it
/// is silence, and the runtime leaves that texture exactly as the game drew it.
/// <para/>
/// So the presence of an entry IS the tick in the editor's checkbox. There is
/// no separate enabled flag to fall out of step with it, and a pack carries
/// only the slots its author actually reached for.
/// <para/>
/// Nothing here applies to a bust the pack draws itself: that outfit already
/// describes every texture it has.
/// <para/>
/// The slot vocabulary lives in <see cref="SMSModForge.Shared.SpriteSlotNames"/>,
/// compiled into the runtime as well — a name only this side knew would be a
/// texture that silently never gets replaced.
/// </summary>
public sealed class SpriteOverrideDef
{
    /// <summary>
    /// Which texture this replaces — see
    /// <see cref="SMSModForge.Shared.SpriteSlotNames"/>. A face is
    /// <c>expression:Happy</c>, which is also how a face the pack ADDED to one
    /// of the game's characters is named.
    /// </summary>
    [JsonProperty("slot", Order = 1)]
    public string Slot { get; set; } = "";

    /// <summary>Pack-relative path to the PNG. Empty means the author has
    /// asked for this slot and not yet chosen art for it — which is worth
    /// keeping across a save, and worth an issue in the editor.</summary>
    [JsonProperty("sprite", Order = 2)]
    public string Sprite { get; set; } = "";
}
