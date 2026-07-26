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
}
