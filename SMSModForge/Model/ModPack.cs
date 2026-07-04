using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Root document of a mod pack. One per folder under
/// <c>BepInEx\plugins\the host mod\ModPacks\&lt;packId&gt;\modpack.json</c>.
/// <para/>
/// A pack carries every modular content slice for one mod author bundle:
/// busts, places, and (future) dialogues. Loaders should ignore unknown keys
/// so v1 readers tolerate v2+ additions. The legacy filename
/// <c>bustpack.json</c> with schema <c>smsbustforge/bustpack/v1</c> is still
/// accepted on read for packs authored before the rename.
/// </summary>
public sealed class ModPack
{
    /// <summary>
    /// Schema marker. Bump when making breaking changes. Loaders should reject
    /// packs with a higher major version than they understand.
    /// </summary>
    [JsonProperty("$schema", Order = 0)]
    public string Schema { get; set; } = "smsmodforge/modpack/v1";

    /// <summary>
    /// Unique identifier for the pack — also the folder name. Used to namespace
    /// every authored key (outfits and places) at load time so two packs can
    /// both define a "newgirl" or "beach" key without colliding.
    /// </summary>
    [JsonProperty("packId", Order = 1)]
    public string PackId { get; set; } = "Untitled";

    [JsonProperty("characters", Order = 2)]
    public List<CharacterDef> Characters { get; set; } = new();

    /// <summary>
    /// Custom places authored by this pack. Each one becomes a new entry under
    /// <c>5_Levels</c> in <c>CoreGameScene</c> at mod load time, alongside a
    /// matching map button and roomtalk node. See <see cref="PlaceDef"/>.
    /// </summary>
    [JsonProperty("places", Order = 3)]
    public List<PlaceDef> Places { get; set; } = new();

    /// <summary>
    /// Pack-authored buttons that hang off a <em>vanilla</em> place's
    /// navigator strip — typically the "entry point" buttons that let the
    /// player reach this pack's custom places from existing levels.
    /// See <see cref="VanillaPlaceExtensionDef"/>.
    /// </summary>
    [JsonProperty("vanillaExtensions", Order = 4)]
    public List<VanillaPlaceExtensionDef> VanillaExtensions { get; set; } = new();

    /// <summary>
    /// Pack-authored radial buttons added to the World Map (e.g. a
    /// "House for Sale" button under the Foundry district that travels
    /// to a custom a place button place). Modelled after
    /// the host mod's a place button radial button.
    /// See <see cref="MapButtonDef"/>.
    /// </summary>
    [JsonProperty("mapButtons", Order = 9)]
    public List<MapButtonDef> MapButtons { get; set; } = new();

    /// <summary>
    /// Speakers for this pack's dialogues. Decoupled from GC2 Actor SOs so
    /// packs don't need a custom asset bundle just to add a new character
    /// line. See <see cref="ActorDef"/>.
    /// </summary>
    [JsonProperty("actors", Order = 5)]
    public List<ActorDef> Actors { get; set; } = new();

    /// <summary>
    /// Pack-defined variables, used by dialogue conditions and actions.
    /// Per-pack save file at <c>BepInEx/plugins/SMSModForge/Saves/&lt;packId&gt;.json</c>.
    /// See <see cref="PackVariableDef"/>.
    /// </summary>
    [JsonProperty("variables", Order = 6)]
    public List<PackVariableDef> Variables { get; set; } = new();

    /// <summary>
    /// Dialogues authored by this pack. Each becomes a runtime GC2
    /// <c>Dialogue</c> MonoBehaviour parented under its target roomtalk.
    /// See <see cref="DialogueDef"/>.
    /// </summary>
    [JsonProperty("dialogues", Order = 7)]
    public List<DialogueDef> Dialogues { get; set; } = new();

    /// <summary>
    /// CG / story scenes authored by this pack. Each becomes a
    /// GameObject under <c>4_CG_Manager-Sexy</c> at runtime, driven by
    /// <c>ActivateScene</c> / <c>DeactivateAllScenes</c> node actions.
    /// See <see cref="SceneDef"/>.
    /// </summary>
    [JsonProperty("scenes", Order = 8)]
    public List<SceneDef> Scenes { get; set; } = new();

    /// <summary>
    /// Pack-authored desktop wallpapers for the in-game PC. Each clones
    /// the vanilla base wallpaper + selector button, swaps the sprite,
    /// and is gated by an optional unlock condition (typically an
    /// Event_Seen* flag, matching the host mod's wallpaper unlocks).
    /// See <see cref="WallpaperDef"/>.
    /// </summary>
    [JsonProperty("wallpapers", Order = 10)]
    public List<WallpaperDef> Wallpapers { get; set; } = new();

    /// <summary>
    /// Pack-authored music tracks. Each becomes a sibling of vanilla
    /// music under <c>12_AudioPlayer</c>, picked up by the existing
    /// <c>SwitchMusic</c> action's name lookup. See <see cref="MusicDef"/>.
    /// </summary>
    [JsonProperty("music", Order = 11)]
    public List<MusicDef> Music { get; set; } = new();

    /// <summary>
    /// Pack-authored sound effects. Loaded from disk into an
    /// <see cref="UnityEngine.AudioClip"/> at pack init; triggered
    /// by the <c>PlaySFX</c> node action (single clip, optional
    /// delay, optional volume — stack multiple actions for layered
    /// or sequenced playback). See <see cref="SfxDef"/>.
    /// </summary>
    [JsonProperty("sfx", Order = 12)]
    public List<SfxDef> Sfx { get; set; } = new();

    /// <summary>
    /// Free-floating "<c>if</c>" rules evaluated by the pack runtime
    /// per-frame. Authored in the editor's Integration tab; the
    /// runtime treats each rule as a conditions+actions pair with
    /// edge-trigger semantics (see <see cref="UpdateRuleTriggerMode"/>).
    /// Used for pack-side orchestration that doesn't belong on a
    /// single dialogue — rebuilding lists on day change, mirroring
    /// derived state, etc.
    /// </summary>
    [JsonProperty("integrationRules", Order = 13)]
    public List<UpdateRuleDef> IntegrationRules { get; set; } = new();

    /// <summary>
    /// Names of pack-authored custom roomtalks, addressed as
    /// <c>vanilla:&lt;name&gt;</c> on a dialogue. The runtime creates the
    /// roomtalk node on the fly (clones an existing one); this list just
    /// records the intent so the editor lists them in the roomtalk picker and
    /// the validator doesn't flag them as unknown vanilla names. Omitted from
    /// JSON when empty.
    /// </summary>
    [JsonProperty("customRoomTalks", Order = 14)]
    public List<string> CustomRoomTalks { get; set; } = new();

    public bool ShouldSerializeCustomRoomTalks() => CustomRoomTalks != null && CustomRoomTalks.Count > 0;

    /// <summary>
    /// Editor-only cosmetic grouping of dialogues into (nestable) folders for
    /// the Dialogues-tab tree. The runtime plugin ignores this key entirely —
    /// it only affects how the editor displays the flat <see cref="Dialogues"/>
    /// list. Omitted from JSON when empty. See <see cref="DialogueFolderDef"/>.
    /// </summary>
    [JsonProperty("dialogueFolders", Order = 15)]
    public List<DialogueFolderDef> DialogueFolders { get; set; } = new();

    public bool ShouldSerializeDialogueFolders() => DialogueFolders != null && DialogueFolders.Count > 0;

    /// <summary>
    /// Editor-only cosmetic grouping of variables into (nestable) folders for
    /// the Variables-tab tree. The runtime ignores this key — it only affects
    /// how the editor displays the flat <see cref="Variables"/> list. Omitted
    /// from JSON when empty. See <see cref="VariableFolderDef"/>.
    /// </summary>
    [JsonProperty("variableFolders", Order = 16)]
    public List<VariableFolderDef> VariableFolders { get; set; } = new();

    public bool ShouldSerializeVariableFolders() => VariableFolders != null && VariableFolders.Count > 0;
}
