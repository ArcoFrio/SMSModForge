using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Where a UI object sits, in the terms Unity's canvas actually uses.
/// <para/>
/// Not x and y. A UI rectangle is defined by two anchors, a pivot, an offset
/// from those anchors and a size, and the whole point of that scheme is that
/// the result depends on the parent's size — which is how the game's UI keeps
/// working at resolutions nobody authored it at. Storing a position instead
/// would produce something that looked right in the preview at 1920×1080 and
/// drifted everywhere else.
/// <para/>
/// Anchors are fractions of the parent (0,0 is its bottom-left, 1,1 its
/// top-right). When min and max are equal the object is pinned to a point and
/// <see cref="Size"/> is its literal size; when they differ it stretches with
/// the parent and Size becomes an inset instead. That is Unity's rule, not
/// this editor's, and the preview has to honour it either way.
/// </summary>
public sealed class UiRectDef
{
    /// <summary>Bottom-left anchor, as a fraction of the parent.</summary>
    [JsonProperty("anchorMin", Order = 1)]
    public float[] AnchorMin { get; set; } = { 0.5f, 0.5f };

    /// <summary>Top-right anchor, as a fraction of the parent.</summary>
    [JsonProperty("anchorMax", Order = 2)]
    public float[] AnchorMax { get; set; } = { 0.5f, 0.5f };

    /// <summary>The point within this rectangle that <see cref="Position"/>
    /// places, and that rotation and scale turn around.</summary>
    [JsonProperty("pivot", Order = 3)]
    public float[] Pivot { get; set; } = { 0.5f, 0.5f };

    /// <summary>Offset of the pivot from the anchors, in canvas pixels.</summary>
    [JsonProperty("position", Order = 4)]
    public float[] Position { get; set; } = { 0f, 0f };

    /// <summary>Size in canvas pixels when the anchors are a point; the inset
    /// from them when the anchors are a rectangle.</summary>
    [JsonProperty("size", Order = 5)]
    public float[] Size { get; set; } = { 100f, 100f };

    [JsonProperty("rotationZ", Order = 6)]
    public float RotationZ { get; set; }

    [JsonProperty("scale", Order = 7)]
    public float[] Scale { get; set; } = { 1f, 1f };

    public bool ShouldSerializeRotationZ() => RotationZ != 0f;
    public bool ShouldSerializeScale() => Scale is not { Length: 2 } s ||
                                          s[0] != 1f || s[1] != 1f;
}

/// <summary>
/// A picture on a UI object.
/// <para/>
/// The <see cref="Type"/> is the load-bearing field, and Sliced is the one that
/// matters: the vanilla UI draws 2556 of its 4807 images that way, from just 51
/// distinct sprites, because a nine-sliced sprite can be stretched to any size
/// without its corners deforming. That is why the same "Semi Rounded" appears
/// 518 times at 518 different sizes, and why the tint does the rest — 1521 of
/// those sliced images are plain white recoloured here.
/// </summary>
public sealed class UiImageDef
{
    /// <summary>Sprite name. A vanilla sprite key, or a file the pack ships.</summary>
    [JsonProperty("sprite", Order = 1)]
    public string Sprite { get; set; } = "";

    /// <summary>Simple, Sliced, Tiled or Filled — Unity's own names, so what is
    /// stored here means the same thing it means in the game.</summary>
    [JsonProperty("type", Order = 2)]
    public string Type { get; set; } = "Simple";

    /// <summary>Tint as "#RRGGBBAA". White means the sprite's own colours.</summary>
    [JsonProperty("tint", Order = 3)]
    public string Tint { get; set; } = "#FFFFFFFF";

    /// <summary>Whether a sliced image fills its middle or draws only the frame.
    /// Several vanilla sprites have borders covering the whole sprite, leaving
    /// no middle at all, and this decides whether that reads as a frame or a
    /// panel.</summary>
    [JsonProperty("fillCenter", Order = 4)]
    public bool FillCenter { get; set; } = true;

    [JsonProperty("preserveAspect", Order = 5)]
    public bool PreserveAspect { get; set; }

    /// <summary>
    /// Scales the nine-slice border before it is fitted, so the same sprite can
    /// keep chunkier or finer corners on different objects.
    /// <para/>
    /// Not a detail. About 390 of the game's sliced images use something other
    /// than 1, from 0.5 to 5, and leaving it out of the authored form made a
    /// freshly seeded Quitagme draw 3% of its pixels differently from the game
    /// while every other value matched.
    /// </summary>
    [JsonProperty("pixelsPerUnitMultiplier", Order = 7)]
    public float PixelsPerUnitMultiplier { get; set; } = 1f;

    public bool ShouldSerializePixelsPerUnitMultiplier()
        => Math.Abs(PixelsPerUnitMultiplier - 1f) > 0.0001f;

    /// <summary>Whether this image is clickable. Off makes it decoration that
    /// the mouse passes straight through, which matters when it sits over a
    /// button.</summary>
    [JsonProperty("raycastTarget", Order = 6)]
    public bool RaycastTarget { get; set; } = true;

    public bool ShouldSerializePreserveAspect() => PreserveAspect;
    public bool ShouldSerializeFillCenter() => !FillCenter;
    public bool ShouldSerializeRaycastTarget() => !RaycastTarget;
}

/// <summary>
/// Text on a UI object.
/// <para/>
/// <see cref="Font"/> names a TextMeshPro font asset, not a font file. That is
/// the game's own unit: an asset carries a baked atlas, its metrics and its
/// material, and the same underlying typeface appears as several assets with
/// different treatments — Curse Casual exists three times over, plain, outlined
/// and as the dialogue variant, and they are not interchangeable.
/// </summary>
public sealed class UiTextDef
{
    [JsonProperty("value", Order = 1)]
    public string Value { get; set; } = "";

    /// <summary>TMP font asset name, e.g. "Curse Casual SDF Outline".</summary>
    [JsonProperty("font", Order = 2)]
    public string Font { get; set; } = "";

    [JsonProperty("size", Order = 3)]
    public float Size { get; set; } = 36f;

    [JsonProperty("color", Order = 4)]
    public string Color { get; set; } = "#FFFFFFFF";

    /// <summary>TMP's own alignment name, e.g. "Center" or "MidlineLeft".</summary>
    [JsonProperty("alignment", Order = 5)]
    public string Alignment { get; set; } = "Center";

    [JsonProperty("wrap", Order = 6)]
    public bool Wrap { get; set; } = true;

    /// <summary>Extra space between lines, in TextMeshPro's own units — a
    /// percentage of the point size, not pixels. Carried because the game uses
    /// it: leaving it out shifted every multi-line label on Quitagme by a
    /// fraction of a line, which is 415 pixels of quiet disagreement.</summary>
    [JsonProperty("lineSpacing", Order = 10)]
    public float LineSpacing { get; set; }

    /// <summary>Extra space between characters, same units.</summary>
    [JsonProperty("characterSpacing", Order = 11)]
    public float CharacterSpacing { get; set; }

    public bool ShouldSerializeLineSpacing() => LineSpacing != 0f;
    public bool ShouldSerializeCharacterSpacing() => CharacterSpacing != 0f;

    /// <summary>Shrink the text to fit its rectangle rather than overflow it.
    /// Vanilla uses this heavily for labels whose content is a variable.</summary>
    [JsonProperty("autoSize", Order = 7)]
    public bool AutoSize { get; set; }

    [JsonProperty("autoSizeMin", Order = 8)]
    public float AutoSizeMin { get; set; } = 12f;

    [JsonProperty("autoSizeMax", Order = 9)]
    public float AutoSizeMax { get; set; } = 72f;

    public bool ShouldSerializeWrap() => !Wrap;
    public bool ShouldSerializeAutoSize() => AutoSize;
    public bool ShouldSerializeAutoSizeMin() => AutoSize;
    public bool ShouldSerializeAutoSizeMax() => AutoSize;
}

/// <summary>
/// One object in a UI tree.
/// <para/>
/// The shape deliberately mirrors <see cref="GameObjectDef"/> — same names for
/// children, conditions, components, and the same bind/override pair that lets
/// an extension store only what it changed — because an author who has built a
/// place already knows how this works. What differs is the vocabulary, and it
/// differs because it has to: a canvas positions things by anchors against a
/// parent rectangle, not by coordinates in a world.
/// </summary>
public sealed class UiNodeDef
{
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "";

    [JsonProperty("rect", Order = 2)]
    public UiRectDef Rect { get; set; } = new();

    [JsonProperty("image", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
    public UiImageDef? Image { get; set; }

    [JsonProperty("text", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public UiTextDef? Text { get; set; }

    /// <summary>Opacity for this object and everything under it, as a
    /// CanvasGroup. Null means no group at all, which is not the same as an
    /// alpha of 1: a group also gates whether the subtree takes clicks, and
    /// adding one where the game has none changes behaviour.</summary>
    [JsonProperty("alpha", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public float? Alpha { get; set; }

    /// <summary>Drop shadow, as "#RRGGBBAA" plus an offset. Vanilla puts one on
    /// 667 objects, so a preview without them is wrong more often than right.</summary>
    [JsonProperty("shadow", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public UiEffectDef? Shadow { get; set; }

    /// <summary>Outline. Drawn as four offset copies of the graphic, which is
    /// why it costs what it costs and why the offset is a distance rather than
    /// a width.</summary>
    [JsonProperty("outline", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public UiEffectDef? Outline { get; set; }

    /// <summary>Clip children to this rectangle.</summary>
    [JsonProperty("clipChildren", Order = 8)]
    public bool ClipChildren { get; set; }

    [JsonProperty("startActive", Order = 9)]
    public bool StartActive { get; set; } = true;

    /// <summary>Conditions that decide whether this object is shown, evaluated
    /// the same way as everywhere else in a pack.</summary>
    [JsonProperty("activeConditions", Order = 10)]
    public List<NodeConditionDef> ActiveConditions { get; set; } = new();

    /// <summary>Behaviour attached to this object — what a button does when it
    /// is clicked, and anything else with parameters.</summary>
    [JsonProperty("components", Order = 11)]
    public List<ComponentDef> Components { get; set; } = new();

    [JsonProperty("children", Order = 12)]
    public List<UiNodeDef> Children { get; set; } = new();

    // ── Extending something that already exists ──────────────────────
    //
    // The same delta scheme places use. A node with Bind set is not created:
    // it names an object the vanilla UI already has, and carries only the
    // properties the author actually changed. Everything else about that
    // object is left exactly as the game made it, so a vanilla UI that shifts
    // between game versions carries the extension along with it instead of
    // being frozen at the shape it had when the pack was written.

    /// <summary>
    /// Path of the vanilla object this node adjusts, relative to the base —
    /// "Header/Title", or "." for the base itself.
    /// <para/>
    /// Empty means the opposite: an object the pack is ADDING, which is why the
    /// base needs "." rather than the empty path it would naturally have. A
    /// segment naming a repeated sibling carries the same #n suffix the catalog
    /// uses, so binding to the second of two objects called Image keeps meaning
    /// the second one.
    /// </summary>
    [JsonProperty("bind", Order = 20)]
    public string Bind { get; set; } = "";

    [JsonProperty("overrideRect", Order = 21)]
    public bool OverrideRect { get; set; }

    [JsonProperty("overrideImage", Order = 22)]
    public bool OverrideImage { get; set; }

    [JsonProperty("overrideText", Order = 23)]
    public bool OverrideText { get; set; }

    [JsonProperty("overrideActive", Order = 24)]
    public bool OverrideActive { get; set; }

    public bool IsBound => !string.IsNullOrEmpty(Bind);

    public bool ShouldSerializeActiveConditions() => ActiveConditions.Count > 0;
    public bool ShouldSerializeComponents() => Components.Count > 0;
    public bool ShouldSerializeChildren() => Children.Count > 0;
    public bool ShouldSerializeClipChildren() => ClipChildren;
    public bool ShouldSerializeStartActive() => !StartActive;
    public bool ShouldSerializeBind() => IsBound;
    public bool ShouldSerializeOverrideRect() => OverrideRect;
    public bool ShouldSerializeOverrideImage() => OverrideImage;
    public bool ShouldSerializeOverrideText() => OverrideText;
    public bool ShouldSerializeOverrideActive() => OverrideActive;
}

/// <summary>A shadow or an outline: a colour and how far it is offset.</summary>
public sealed class UiEffectDef
{
    [JsonProperty("color", Order = 1)]
    public string Color { get; set; } = "#000000FF";

    [JsonProperty("distance", Order = 2)]
    public float[] Distance { get; set; } = { 1f, -1f };
}
