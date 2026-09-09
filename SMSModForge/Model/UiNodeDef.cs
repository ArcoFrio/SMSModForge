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

    /// <summary>
    /// Conditions that decide whether this object is shown, evaluated the same
    /// way as everywhere else in a pack and re-checked every frame.
    /// <para/>
    /// This is how a list hides what it should not offer - a shop shelf drops
    /// what has already been bought - without a rule per item saying so. The
    /// object states its own condition, which is where an author looks for it.
    /// </summary>
    [JsonProperty("activeConditions", Order = 10)]
    public List<NodeConditionDef> ActiveConditions { get; set; } = new();

    /// <summary>
    /// Conditions that decide whether a click does anything.
    /// <para/>
    /// Without these a click cannot ask a question, so anything conditional -
    /// "buy it IF there is enough money" - had to be pushed into an
    /// integration rule per item, with the click reduced to writing down what
    /// was asked for. The question belongs on the button that asks it.
    /// <para/>
    /// A click that fails them does nothing at all: no actions run, and the
    /// press still plays so the button does not feel broken.
    /// </summary>
    [JsonProperty("clickConditions", Order = 17)]
    public List<NodeConditionDef> ClickConditions { get; set; } = new();

    /// <summary>Behaviour attached to this object — what a button does when it
    /// is clicked, and anything else with parameters.</summary>
    [JsonProperty("components", Order = 11)]
    public List<ComponentDef> Components { get; set; } = new();

    /// <summary>
    /// What happens when this object is clicked.
    /// <para/>
    /// The same actions the rest of the pack uses - set a variable, switch an
    /// object on, emit a signal - rather than a vocabulary of its own. An
    /// author who has written a dialogue already knows this list, and a button
    /// that can only do button-shaped things is a button that stops being
    /// useful the first time it needs to do anything else.
    /// <para/>
    /// A node with none of these is not clickable at all: the pointer passes
    /// through it to whatever is behind, which is what decoration should do.
    /// </summary>
    [JsonProperty("onClick", Order = 13)]
    public List<NodeActionDef> OnClick { get; set; } = new();

    /// <summary>
    /// Tint while the pointer is over this object, as "#RRGGBBAA". Empty means
    /// no hover feedback at all.
    /// <para/>
    /// A declared colour, and the game does the same kind of thing: its own
    /// buttons carry a Unity ColorBlock set to ColorTint, tinting to #F5F5F5
    /// on hover and #C8C8C8 on press over a 0.1s fade. Measured from 756 of
    /// them, so a pack that wants to look native has the numbers.
    /// </summary>
    [JsonProperty("hoverTint", Order = 14)]
    public string HoverTint { get; set; } = "";

    /// <summary>
    /// What THIS object sounds like when clicked, overriding whatever the
    /// screen says its buttons sound like. Empty means it uses the screen's.
    /// </summary>
    [JsonProperty("clickSound", Order = 15, NullValueHandling = NullValueHandling.Ignore)]
    public string ClickSound { get; set; } = "";

    /// <summary>
    /// How this object arrives when it is switched on, or null to simply
    /// appear.
    /// <para/>
    /// The same setting a whole screen has, because an object that a condition
    /// or an action turns on is arriving exactly as a screen does - a panel
    /// swapping for another inside one screen is the same moment, one level
    /// down.
    /// </summary>
    [JsonProperty("open", Order = 16, NullValueHandling = NullValueHandling.Ignore)]
    public UiOpenDef? Open { get; set; }

    /// <summary>How this object leaves when an action switches it off, or null
    /// to simply vanish. See <see cref="UiDef.Close"/>.</summary>
    [JsonProperty("close", Order = 17, NullValueHandling = NullValueHandling.Ignore)]
    public UiOpenDef? Close { get; set; }

    /// <summary>
    /// How this object arranges its children, or null to leave each child where
    /// it was put.
    /// <para/>
    /// The reason to reach for one is a list that changes: hide an item from a
    /// row of fixed positions and it leaves a hole, while a row that arranges
    /// itself closes ranks. The game uses 380 of these across its own screens.
    /// </summary>
    [JsonProperty("layout", Order = 16, NullValueHandling = NullValueHandling.Ignore)]
    public UiLayoutDef? Layout { get; set; }

    [JsonProperty("children", Order = 15)]
    public List<UiNodeDef> Children { get; set; } = new();

    /// <summary>Whether clicking this object does anything.</summary>
    [JsonIgnore]
    public bool IsClickable => OnClick.Count > 0;

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

    /// <summary>
    /// Where this object sits among its parent's children, or -1 for wherever
    /// it naturally lands.
    /// <para/>
    /// Sibling order IS draw order on a canvas - there is no depth to sort by,
    /// so a later sibling is simply in front. Left alone, an object the game
    /// owns keeps the place the game gave it and one the pack adds goes on the
    /// end, which is right almost always and is why this is normally -1. It is
    /// written only when an author has actually rearranged a parent's children,
    /// and then it is written on ALL of them: half an order is not an order.
    /// </summary>
    [JsonProperty("siblingIndex", Order = 25)]
    public int SiblingIndex { get; set; } = -1;

    public bool IsBound => !string.IsNullOrEmpty(Bind);

    public bool ShouldSerializeActiveConditions() => ActiveConditions.Count > 0;
    public bool ShouldSerializeClickConditions() => ClickConditions.Count > 0;
    public bool ShouldSerializeComponents() => Components.Count > 0;
    public bool ShouldSerializeChildren() => Children.Count > 0;
    public bool ShouldSerializeOnClick() => OnClick.Count > 0;
    public bool ShouldSerializeClickSound() => !string.IsNullOrEmpty(ClickSound);
    public bool ShouldSerializeOpen() => Open != null && Open.DoesAnything;
    public bool ShouldSerializeClose() => Close != null && Close.DoesAnything;
    public bool ShouldSerializeHoverTint() => !string.IsNullOrEmpty(HoverTint);
    public bool ShouldSerializeLayout() => Layout != null;
    public bool ShouldSerializeClipChildren() => ClipChildren;
    public bool ShouldSerializeStartActive() => !StartActive;
    public bool ShouldSerializeBind() => IsBound;
    public bool ShouldSerializeOverrideRect() => OverrideRect;
    public bool ShouldSerializeOverrideImage() => OverrideImage;
    public bool ShouldSerializeOverrideText() => OverrideText;
    public bool ShouldSerializeOverrideActive() => OverrideActive;
    public bool ShouldSerializeSiblingIndex() => SiblingIndex >= 0;
}

/// <summary>A shadow or an outline: a colour and how far it is offset.</summary>
public sealed class UiEffectDef
{
    [JsonProperty("color", Order = 1)]
    public string Color { get; set; } = "#000000FF";

    [JsonProperty("distance", Order = 2)]
    public float[] Distance { get; set; } = { 1f, -1f };
}
