using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// One GameObject in a <see cref="PlaceDef"/>'s scene hierarchy — the unified,
/// data-driven building block for everything the pack layers onto a level.
/// A node with a <see cref="Sprite"/> is a sprite GameObject (a backdrop, prop,
/// portal, cameo…); a sprite-less node is a pure container (an empty transform
/// that groups children and can carry components). Nodes nest via
/// <see cref="Children"/>, and NPC placements hang under them via
/// <see cref="Npcs"/>, so one tree describes the whole scene.
/// <para/>
/// The runtime builds each node under its parent, applies the transform /
/// sorting / parallax / alpha below, and names the GameObject after
/// <see cref="Name"/> so dialogue actions (<c>SetGameObjectActive</c>,
/// <c>FadeSprite</c>, <c>MoveGameObject</c>, <c>SpinGameObject</c>) can target
/// it by that bare name.
/// </summary>
public sealed class GameObjectDef
{
    /// <summary>The forced NPCs-container node's <see cref="Role"/>: it grafts
    /// onto the level's built-in <c>NPCs</c> object instead of creating a new
    /// GameObject, and its subtree uses LOCAL (composing) transforms.</summary>
    public const string RoleNpcRoot = "npcRoot";

    /// <summary>GameObject name — what dialogue actions target. Keep it unique
    /// across the pack (e.g. "MyOverlay", "a prop").</summary>
    [JsonProperty("name", Order = 1)]
    public string Name { get; set; } = "GameObject";

    /// <summary>Relative path (from pack root) to the sprite PNG. Any size —
    /// the runtime reads the image's own dimensions. Blank = a pure container
    /// GameObject (no SpriteRenderer).</summary>
    [JsonProperty("sprite", Order = 2)]
    public string Sprite { get; set; } = "";

    /// <summary>World X position (the level's centre is ~0).</summary>
    [JsonProperty("x", Order = 3)]
    public float X { get; set; } = 0f;

    /// <summary>World Y position (positive = up). Overlays the pan reveals sit
    /// above the visible area, e.g. y = 15. The object parents to the level so
    /// it rides along when the level pans.</summary>
    [JsonProperty("y", Order = 4)]
    public float Y { get; set; } = 0f;

    /// <summary>Z rotation in degrees (in-plane spin). GameObjects are full
    /// transforms, so they can be rotated / scaled like any sprite.</summary>
    [JsonProperty("rotationZ", Order = 5)]
    public float RotationZ { get; set; } = 0f;

    /// <summary>Local X scale (1 = native sprite size; negative mirrors).</summary>
    [JsonProperty("scaleX", Order = 6)]
    public float ScaleX { get; set; } = 1f;

    /// <summary>Local Y scale (1 = native sprite size).</summary>
    [JsonProperty("scaleY", Order = 7)]
    public float ScaleY { get; set; } = 1f;

    /// <summary>SpriteRenderer sorting order. Lower draws further back; the
    /// overlay stack uses -9 for stacked overlays.</summary>
    [JsonProperty("sortingOrder", Order = 8)]
    public int SortingOrder { get; set; } = 0;

    /// <summary>Disable the cloned ParallaxMouseEffect so the object stays put
    /// as the mouse moves (true for almost every overlay).</summary>
    [JsonProperty("parallaxDisabled", Order = 9)]
    public bool ParallaxDisabled { get; set; } = true;

    /// <summary>Whether the object starts visible. False for things a dialogue
    /// reveals later (Flash, Portal); true for an always-on backdrop (Sky).</summary>
    [JsonProperty("startActive", Order = 10)]
    public bool StartActive { get; set; } = true;

    /// <summary>Initial alpha (0..1). Start at 0 for an object a dialogue fades
    /// in with <c>FadeSprite</c> (e.g. the Portal).</summary>
    [JsonProperty("startAlpha", Order = 11)]
    public float StartAlpha { get; set; } = 1f;

    /// <summary>Optional relative path to a mask PNG. When set, the object gets
    /// its own material with this mask bound to <c>_MaskTex</c> (the Solid
    /// cameo's shader trick). Blank = no mask.</summary>
    [JsonProperty("mask", Order = 12)]
    public string Mask { get; set; } = "";

    /// <summary>
    /// Generic utility components attached to this GameObject. Each is added +
    /// configured at build time and reacts to the GameObject being activated.
    /// See <see cref="ComponentDef"/>.
    /// </summary>
    [JsonProperty("components", Order = 13)]
    public List<ComponentDef> Components { get; set; } = new();

    /// <summary>
    /// Nested GameObjects parented under this one, forming the hierarchy. The
    /// runtime builds each child under this object's transform (recursively),
    /// so a child rides along with its parent. Same shape as this node.
    /// </summary>
    [JsonProperty("children", Order = 14)]
    public List<GameObjectDef> Children { get; set; } = new();

    /// <summary>
    /// NPC placements parented directly under this GameObject — each references
    /// an <see cref="NpcDef"/> from the pack's NPCs tab and carries its own
    /// local transform. Typically authored under the forced NPCs-root node's
    /// subtree (containers), but any node may host them.
    /// </summary>
    [JsonProperty("npcs", Order = 15)]
    public List<NpcPlacementDef> Npcs { get; set; } = new();

    /// <summary>
    /// Optional role marker. <see cref="RoleNpcRoot"/> designates the single
    /// forced node that maps to the level's built-in <c>NPCs</c> container
    /// (grafted onto rather than created; its subtree uses local transforms).
    /// Blank for ordinary GameObjects.
    /// </summary>
    [JsonProperty("role", Order = 16)]
    public string Role { get; set; } = "";

    /// <summary>
    /// Optional conditions that drive whether this GameObject is active. When
    /// empty (the default) the object simply keeps <see cref="StartActive"/>
    /// forever and nothing re-evaluates it.
    /// <para/>
    /// When set, the runtime re-checks them and switches the object on as they
    /// pass — so "show this when the story is at X" is authored on the object
    /// itself rather than as a rule that has to remember what it last touched.
    /// Objects whose conditions are mutually exclusive therefore take turns
    /// automatically, with no cascade and no bookkeeping.
    /// </summary>
    [JsonProperty("activeConditions", Order = 17)]
    public List<NodeConditionDef> ActiveConditions { get; set; } = new();

    /// <summary>
    /// Whether the object is switched back OFF once
    /// <see cref="ActiveConditions"/> stop passing. True (the default) gates it
    /// continuously — the object is active exactly while the conditions hold.
    /// False latches it: it turns on the first time they pass and stays on,
    /// for one-way reveals. Ignored when there are no conditions.
    /// </summary>
    [JsonProperty("deactivateWhenUnmet", Order = 18)]
    public bool DeactivateWhenUnmet { get; set; } = true;

    /// <summary>
    /// Bind to an EXISTING GameObject instead of creating one. The runtime
    /// resolves <see cref="Name"/> (a bare name or a path) under this node's
    /// parent and applies only what's authored here — so a pack can reach into
    /// a scene it doesn't own, most usefully a vanilla level, without
    /// rebuilding the structure that's already there.
    /// <para/>
    /// A bound node always applies its <see cref="Components"/>,
    /// <see cref="ActiveConditions"/>, <see cref="Children"/> and
    /// <see cref="Npcs"/>, since those are additions either way. Everything
    /// that would otherwise CLOBBER the existing object is opt-in:
    /// <see cref="OverrideTransform"/> and <see cref="OverrideActive"/>. That
    /// way an untouched bound node is a pure no-op on the vanilla object.
    /// <para/>
    /// When the object can't be found the subtree is skipped with a warning
    /// rather than created — "bind" is a claim that it already exists, and
    /// silently creating one would mask a renamed or moved target.
    /// </summary>
    [JsonProperty("bind", Order = 19)]
    public bool Bind { get; set; } = false;

    /// <summary>Apply this node's position / rotation / scale to the bound
    /// object (as LOCAL values). Off = leave the existing transform alone.
    /// Only meaningful with <see cref="Bind"/>.</summary>
    [JsonProperty("overrideTransform", Order = 20)]
    public bool OverrideTransform { get; set; } = false;

    /// <summary>Apply this node's <see cref="StartActive"/> to the bound
    /// object. Off = leave it however the scene had it. Only meaningful with
    /// <see cref="Bind"/> — and unnecessary when
    /// <see cref="ActiveConditions"/> are driving the object.</summary>
    [JsonProperty("overrideActive", Order = 21)]
    public bool OverrideActive { get; set; } = false;

    /// <summary>
    /// Absolute path to this object's EXTRACTED vanilla art, filled in when a
    /// vanilla extension is seeded from the catalog. Preview-only and never
    /// serialized — it's a machine-local path, and the sprite belongs to the
    /// object already in the scene, not to the pack. Kept separate from
    /// <see cref="Sprite"/> so un-binding a node can't leak it into the
    /// manifest.
    /// </summary>
    [JsonIgnore]
    public string VanillaArtPath { get; set; } = "";

    /// <summary>What the preview should draw: the pack's own sprite when there
    /// is one, otherwise the extracted vanilla art.</summary>
    [JsonIgnore]
    public string PreviewSprite => !string.IsNullOrWhiteSpace(Sprite) ? Sprite : VanillaArtPath;

    /// <summary>
    /// The vanilla object this node was seeded from, when it came from the
    /// extracted catalog. Never serialized — it's editor state — but it's what
    /// lets the editor say "this differs from vanilla" WHILE you're editing,
    /// rather than only discovering it at save time when the override flags get
    /// computed.
    /// </summary>
    [JsonIgnore]
    public VanillaLevelCatalog.Node? Baseline { get; set; }

    /// <summary>True for the forced NPCs-container node.</summary>
    [JsonIgnore]
    public bool IsNpcRoot => Role == RoleNpcRoot;

    // ── Serialization ─────────────────────────────────────────────────────
    //
    // A bound node is a DELTA against an object that already exists, so it only
    // writes what it actually applies. Without this the manifest would carry a
    // full transform and startActive for every bound node — inert at runtime
    // (the builder skips them) but reading as though the node sets them, which
    // is exactly the confusion binding exists to avoid. For an ordinary
    // create-a-GameObject node every one of these is true, so nothing changes.

    /// <summary>
    /// True only while a MANIFEST WRITE is in flight.
    /// <para/>
    /// Reducing a bound node to its delta is right for the file and wrong for
    /// everything else, because Newtonsoft honours <c>ShouldSerialize*</c> on
    /// every serialize — including the one behind undo snapshots
    /// (<see cref="PackRepository.Serialize"/>). Ungated, an undo/redo round-trip
    /// silently dropped x/y/scale/sprite/sortingOrder/startActive from every
    /// bound node and restored them as CLR defaults: position 0, scale 1. Same
    /// for any other in-memory round-trip.
    /// <para/>
    /// So the delta is opt-in per serialization, via <see cref="SaveScope"/>.
    /// Thread-static because it is scoped state, not configuration.
    /// </summary>
    [ThreadStatic] private static bool _pruningForSave;

    /// <summary>Mark the enclosing serialization as a manifest write, so bound
    /// nodes reduce to their delta. Dispose restores the previous state.</summary>
    public static IDisposable SaveScope() => new DeltaScope();

    private sealed class DeltaScope : IDisposable
    {
        private readonly bool _previous;
        public DeltaScope() { _previous = _pruningForSave; _pruningForSave = true; }
        public void Dispose() => _pruningForSave = _previous;
    }

    /// <summary>Whether a bind-gated field may be omitted right now. Outside a
    /// save the answer is always no — fidelity beats brevity everywhere the
    /// output is read back into the editor.</summary>
    private static bool MayOmit(bool isDelta) => !_pruningForSave || isDelta;

    /// <summary>True when this node applies its own transform — always for a
    /// created object, opt-in for a bound one.</summary>
    [JsonIgnore]
    public bool AppliesTransform => !Bind || OverrideTransform;

    /// <summary>True when this node owns renderer-ish properties (sprite,
    /// sorting, alpha, mask, parallax). A bound node never does — those belong
    /// to the object that's already there.</summary>
    [JsonIgnore]
    public bool AppliesOwnVisuals => !Bind;

    // Bind-gated: omitted only when writing the manifest. See MayOmit.
    public bool ShouldSerializeSprite() => MayOmit(AppliesOwnVisuals);
    public bool ShouldSerializeX() => MayOmit(AppliesTransform);
    public bool ShouldSerializeY() => MayOmit(AppliesTransform);
    public bool ShouldSerializeRotationZ() => MayOmit(AppliesTransform) && RotationZ != 0f;
    public bool ShouldSerializeScaleX() => MayOmit(AppliesTransform) && ScaleX != 1f;
    public bool ShouldSerializeScaleY() => MayOmit(AppliesTransform) && ScaleY != 1f;
    public bool ShouldSerializeSortingOrder() => MayOmit(AppliesOwnVisuals);
    public bool ShouldSerializeParallaxDisabled() => MayOmit(AppliesOwnVisuals);
    public bool ShouldSerializeStartActive() => MayOmit(!Bind || OverrideActive);
    public bool ShouldSerializeStartAlpha() => MayOmit(AppliesOwnVisuals);
    public bool ShouldSerializeMask() => MayOmit(AppliesOwnVisuals) && !string.IsNullOrEmpty(Mask);
    // Value-defaulted: an omitted empty list or empty string reads back as
    // itself, so these are safe to drop on any serialization.
    public bool ShouldSerializeComponents() => Components.Count > 0;
    public bool ShouldSerializeChildren() => Children.Count > 0;
    public bool ShouldSerializeNpcs() => Npcs.Count > 0;
    public bool ShouldSerializeRole() => !string.IsNullOrEmpty(Role);
    public bool ShouldSerializeActiveConditions() => ActiveConditions.Count > 0;
    // Only meaningful alongside conditions — keeps unconditioned objects clean.
    // Gated too: it defaults to TRUE, so dropping it would flip an unticked box.
    public bool ShouldSerializeDeactivateWhenUnmet() => MayOmit(ActiveConditions.Count > 0);
    public bool ShouldSerializeBind() => Bind;
    // The override flags mean nothing without Bind, so they stay out of the
    // manifest for the ordinary create-a-GameObject case.
    public bool ShouldSerializeOverrideTransform() => MayOmit(Bind);
    public bool ShouldSerializeOverrideActive() => MayOmit(Bind);
}
