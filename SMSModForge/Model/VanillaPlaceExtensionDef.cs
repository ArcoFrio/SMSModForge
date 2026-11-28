using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One pack-authored extension of a <em>vanilla</em> place's navigator strip.
/// The pack can't modify the vanilla level itself, but it can add map buttons
/// that appear while that vanilla level is active — most commonly, "entry
/// point" buttons leading to the pack's own custom places.
/// <para/>
/// Example: a pack that adds a <c>SecretCave</c> place will typically also
/// add a <c>vanilla:14_Beach</c> extension carrying one button with
/// <c>target = "self:SecretCave"</c>, so the player can reach the new
/// place from the existing beach.
/// </summary>
public sealed class VanillaPlaceExtensionDef
{
    /// <summary>
    /// Vanilla source as a wire-format target token, e.g.
    /// <c>"vanilla:14_Beach"</c>. The editor restricts this to known
    /// vanilla GO names via <see cref="VanillaPlaces"/>; the validator flags
    /// unknown names. Stored in the same token form as
    /// <see cref="NavigatorButtonDef.Target"/> so it shares the picker UI.
    /// </summary>
    [JsonProperty("source", Order = 1)]
    public string Source { get; set; } = "";

    /// <summary>
    /// Buttons to show on the vanilla source's navigator strip. Each
    /// targets another place by stable reference, exactly like the
    /// per-place <see cref="PlaceDef.NavigatorButtons"/> entries.
    /// </summary>
    [JsonProperty("navigatorButtons", Order = 2)]
    public List<NavigatorButtonDef> NavigatorButtons { get; set; } = new();

    /// <summary>
    /// GameObjects layered onto the vanilla source level — the same authored
    /// shape as <see cref="PlaceDef.GameObjects"/> (sprites, nested children,
    /// utility components). Built under the vanilla level's transform at scene
    /// load, so packs can decorate levels they don't own without touching them
    /// structurally.
    /// </summary>
    [JsonProperty("gameObjects", Order = 3)]
    public List<GameObjectDef> GameObjects { get; set; } = new();
    public bool ShouldSerializeGameObjects() => GameObjects.Count > 0;

    /// <summary>
    /// Which rules the objects' masks follow (after 1.10.0). 1: an object with no mask stays still
    /// (<see cref="GameObjectDef.MaskKind"/> says otherwise). Absent (0): a place saved before, whose objects without
    /// a mask took the back layer's - the plugin keeps that for packs exported then, and the editor turns it into
    /// the kinds as the pack loads (PackMigration). New places start at 1.
    /// </summary>
    [JsonProperty("objectMasks", Order = 5)]
    public int ObjectMasks { get; set; } = CurrentObjectMasks;
    public bool ShouldSerializeObjectMasks() => ObjectMasks != 0;

    /// <summary>Read from a file: absent is a place saved before the rules (0), not a new one.</summary>
    [System.Runtime.Serialization.OnDeserializing]
    private void BeforeReadingObjectMasks(System.Runtime.Serialization.StreamingContext _) => ObjectMasks = 0;

    /// <summary>The rules this editor writes.</summary>
    public const int CurrentObjectMasks = PlaceDef.CurrentObjectMasks;

    /// <summary>What the pack's own objects deleted from the level were, newest last, kept to be put back
    /// (<see cref="BinnedObjectDef"/>).</summary>
    [JsonProperty("recycleBin", Order = 4)]
    public List<BinnedObjectDef> RecycleBin { get; set; } = new();
    public bool ShouldSerializeRecycleBin() => RecycleBin.Count > 0;

    /// <summary>
    /// Set by the editor while <see cref="GameObjects"/> is the whole level as it is worked on - every object
    /// of the game's, with the pack's changes on them - rather than what a pack on disk holds: only what it
    /// changed, each object saying whether it means its own place and showing (worked out as the file is
    /// written). In the editor's own copies of the pack - an undo step, a record copied - and never in the
    /// file: read back from one of those, the objects keep what they have.
    /// <para/>
    /// Without it a level read back was filled in from the game's again as a file's is, and an object the pack
    /// had moved, hidden or given another drift went back to the game's - nothing on a working object says
    /// the pack means its values - whenever an undo or a redo built the level again (1.10.0, found by the walk
    /// over every control; the same as Model.UiDef.Working for the game's screens).
    /// </summary>
    [JsonProperty("working", Order = 99)]
    public bool Working { get; set; }

    public bool ShouldSerializeWorking() => Working && !GameObjectDef.WritingTheFile;
}
