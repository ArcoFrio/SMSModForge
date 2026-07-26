using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored wallpaper for the in-game PC. The runtime clones
/// the vanilla base wallpaper (under <c>Desktop/Wallpaper/Wallpaper (0)</c>)
/// and its selector button (under <c>Wallpaperselection/UI_Core/List/WallpaperButton (0)</c>),
/// swaps both sprites to <see cref="SpritePath"/> (or
/// <see cref="ExternalSpritePath"/>), and gates the button's visibility
/// on a pack condition.
/// <para/>
/// Mirrors the host mod's <c>Wallpaper.CreateWallpaper</c> pattern. Pack
/// wallpapers live as siblings of the vanilla ones in the same panel —
/// the player picks them the same way they pick vanilla wallpapers.
/// </summary>
public sealed class WallpaperDef
{
    /// <summary>
    /// Pack-local key. Becomes the wallpaper GO name at runtime
    /// (prefixed with the pack id so two packs can both define a
    /// <c>"swimsuit"</c> without colliding).
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "wallpaper1";

    /// <summary>Human-facing label shown in the editor's wallpaper list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "New Wallpaper";

    /// <summary>
    /// Relative path (from pack root) to the wallpaper PNG. Loaded into
    /// a 1920×1080 sprite — the same dimensions vanilla wallpapers use.
    /// Mutually exclusive with <see cref="ExternalSpritePath"/>; whichever
    /// is non-empty wins (custom wins over external when both are set).
    /// </summary>
    [JsonProperty("spritePath", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public string? SpritePath { get; set; }

    /// <summary>
    /// Absolute on-disk path to the wallpaper PNG, used during the
    /// transition from the host mod — packs that haven't bundled their
    /// own wallpaper PNGs can point at the existing
    /// <c>the host mod's external wallpaper folder</c> files.
    /// Long-term, prefer <see cref="SpritePath"/> for self-contained
    /// packs.
    /// </summary>
    [JsonProperty("externalSpritePath", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public string? ExternalSpritePath { get; set; }

    /// <summary>
    /// Legacy single-condition gate. Older packs carried one condition
    /// object here; on load it's folded into <see cref="UnlockConditions"/>
    /// (see the deserialize hook below) and the field is nulled so writes
    /// always emit the list form. Never authored anymore.
    /// </summary>
    [JsonProperty("unlockCondition", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public NodeConditionDef? UnlockCondition { get; set; }

    /// <summary>
    /// Gate for the selector button's visibility — the standard condition
    /// list (AND semantics, group support, same vocabulary as everywhere
    /// else). Re-evaluated each frame by the runtime; while it fails the
    /// button is hidden (matches the host mod's <c>SetActive(Event_Seen…)</c>
    /// pattern). Empty = always visible.
    /// </summary>
    [JsonProperty("unlockConditions", Order = 6)]
    public List<NodeConditionDef> UnlockConditions { get; set; } = new();
    public bool ShouldSerializeUnlockConditions() => UnlockConditions.Count > 0;

    [System.Runtime.Serialization.OnDeserialized]
    internal void OnDeserializedMigrateUnlock(System.Runtime.Serialization.StreamingContext _)
    {
        // Promote the legacy single condition to the list so the rest of
        // the editor sees one canonical field; drop the legacy slot so a
        // re-save emits only the list form.
        if (UnlockCondition != null)
        {
            UnlockConditions.Insert(0, UnlockCondition);
            UnlockCondition = null;
        }
    }
}
