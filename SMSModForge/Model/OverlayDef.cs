using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One extra sprite GameObject layered onto a <see cref="PlaceDef"/>'s level at
/// runtime — the data-driven replacement for the bespoke the host mod overlays
/// (a level's backdrop / flash / prop / portal overlays, a cameo, …).
/// <para/>
/// The runtime clones the level's secondary-sprite child, swaps in this
/// overlay's sprite, then applies the position / sorting / parallax / alpha
/// below and parents it under the level so it moves with it. The overlay's
/// <see cref="Name"/> becomes its GameObject name, so dialogue actions
/// (<c>SetGameObjectActive</c>, <c>FadeSprite</c>, <c>MoveGameObject</c>,
/// <c>SpinGameObject</c>) can target it by that bare name.
/// </summary>
public sealed class OverlayDef
{
    /// <summary>GameObject name — what dialogue actions target. Keep it unique
    /// across the pack (e.g. "MyOverlay", "a prop").</summary>
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "Overlay";

    /// <summary>Relative path (from pack root) to the overlay sprite PNG. Any
    /// size — the runtime reads the image's own dimensions.</summary>
    [JsonProperty("sprite", Order = 2)]
    public string Sprite { get; set; } = "";

    /// <summary>World X position (the level's centre is ~0).</summary>
    [JsonProperty("x", Order = 3)]
    public float X { get; set; } = 0f;

    /// <summary>World Y position (positive = up). Overlays the pan reveals sit
    /// above the visible area, e.g. y = 15. The overlay parents to the level so
    /// it rides along when the level pans.</summary>
    [JsonProperty("y", Order = 4)]
    public float Y { get; set; } = 0f;

    /// <summary>SpriteRenderer sorting order. Lower draws further back; the
    /// the overlay stack uses -9 for stacked overlays.</summary>
    [JsonProperty("sortingOrder", Order = 5)]
    public int SortingOrder { get; set; } = 0;

    /// <summary>Disable the cloned ParallaxMouseEffect so the overlay stays put
    /// as the mouse moves (true for almost every overlay).</summary>
    [JsonProperty("parallaxDisabled", Order = 6)]
    public bool ParallaxDisabled { get; set; } = true;

    /// <summary>Whether the overlay starts visible. False for things a dialogue
    /// reveals later (Flash, Portal); true for an always-on backdrop (Sky).</summary>
    [JsonProperty("startActive", Order = 7)]
    public bool StartActive { get; set; } = true;

    /// <summary>Initial alpha (0..1). Start at 0 for an overlay a dialogue fades
    /// in with <c>FadeSprite</c> (e.g. the Portal).</summary>
    [JsonProperty("startAlpha", Order = 8)]
    public float StartAlpha { get; set; } = 1f;

    /// <summary>Optional relative path to a mask PNG. When set, the overlay gets
    /// its own material with this mask bound to <c>_MaskTex</c> (the Solid
    /// cameo's shader trick). Blank = no mask.</summary>
    [JsonProperty("mask", Order = 9)]
    public string Mask { get; set; } = "";

    /// <summary>
    /// Generic utility components attached to this Extra GameObject. Each is
    /// added + configured at build time and reacts to the GameObject being
    /// activated. See <see cref="ComponentDef"/>.
    /// </summary>
    [JsonProperty("components", Order = 10)]
    public List<ComponentDef> Components { get; set; } = new();

    /// <summary>
    /// Nested Extra GameObjects parented under this one, forming a hierarchy.
    /// The runtime builds each child under this object's transform (recursively),
    /// so a child rides along with its parent. Same shape as a top-level Extra
    /// GameObject — sprite, transform, components, and its own children.
    /// </summary>
    [JsonProperty("children", Order = 11)]
    public List<OverlayDef> Children { get; set; } = new();

    public bool ShouldSerializeMask() => !string.IsNullOrEmpty(Mask);
    public bool ShouldSerializeComponents() => Components.Count > 0;
    public bool ShouldSerializeChildren() => Children.Count > 0;
}
