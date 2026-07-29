using System;
using System.Collections.Generic;

namespace SMSModForge.Model;

/// <summary>
/// Works out what a vanilla extension actually CHANGES, so the manifest stores
/// (and the runtime applies) only that.
/// <para/>
/// Authoring a vanilla extension means editing a hierarchy the pack doesn't
/// own: you seed the tree from <see cref="VanillaLevelCatalog"/> and most nodes
/// stay exactly as the game has them. Writing all of those out would bloat the
/// manifest and, worse, read as though the pack were asserting values it merely
/// copied. So at save time every bound node is compared against the catalog:
/// <list type="bullet">
///   <item>Transform / active flags are set from the COMPARISON, not from the
///   author having remembered to tick a box.</item>
///   <item>A bound node that changes nothing and has no surviving descendant is
///   dropped entirely — it would have been a no-op at runtime anyway.</item>
/// </list>
/// Nodes the pack creates are always kept, and a level with no catalog entry
/// falls back to the author's manual override flags, so an un-extracted setup
/// still behaves exactly as before.
/// <para/>
/// The pass never touches the live model: it builds pruned COPIES and hands
/// back a restore action, so saving can't quietly rewrite what's on screen.
/// </summary>
public static class VanillaDelta
{
    /// <summary>Float comparison tolerance. Unity transforms round-trip through
    /// the extractor's 5-decimal text form, so an exact compare would report
    /// spurious differences.</summary>
    private const float Epsilon = 1e-4f;

    /// <summary>
    /// Swap each vanilla extension's GameObjects list for its pruned delta.
    /// Returns an action that puts the originals back — call it in a finally.
    /// </summary>
    public static Action PrepareForSave(ModPack pack)
    {
        var restores = new List<Action>();
        if (pack?.VanillaExtensions == null || !VanillaLevelCatalog.IsAvailable)
            return () => { };

        foreach (var ext in pack.VanillaExtensions)
        {
            var level = VanillaLevelCatalog.FindLevelByToken(ext.Source);
            if (level?.Hierarchy == null) continue;   // no baseline → leave as authored

            var original = ext.GameObjects;
            var pruned = PruneList(original, VanillaLevelCatalog.RootChildren(level));
            if (ReferenceEquals(pruned, original)) continue;

            var target = ext;
            restores.Add(() => target.GameObjects = original);
            ext.GameObjects = pruned;
        }
        return () => { foreach (var r in restores) r(); };
    }

    /// <summary>
    /// Report what the delta pass would drop / keep, without changing anything.
    /// Backs an editor-side summary so the author can see that "nothing to
    /// save" really means "nothing differs from vanilla".
    /// </summary>
    public static (int Kept, int Dropped) Summarize(ModPack pack)
    {
        int kept = 0, dropped = 0;
        if (pack?.VanillaExtensions == null || !VanillaLevelCatalog.IsAvailable) return (0, 0);
        foreach (var ext in pack.VanillaExtensions)
        {
            var level = VanillaLevelCatalog.FindLevelByToken(ext.Source);
            if (level?.Hierarchy == null) continue;
            int before = CountNodes(ext.GameObjects);
            int after = CountNodes(PruneList(ext.GameObjects, VanillaLevelCatalog.RootChildren(level)));
            kept += after;
            dropped += before - after;
        }
        return (kept, dropped);
    }

    /// <summary>
    /// Whether this extension changes anything at all — i.e. whether saving
    /// would write any GameObject for it. Drives the sidebar marker that tells
    /// "I've actually modified this vanilla place" apart from "I opened it and
    /// the editor filled in the hierarchy".
    /// <para/>
    /// Without a catalog there's nothing to compare against, so anything
    /// authored counts.
    /// </summary>
    public static bool HasChanges(VanillaPlaceExtensionDef ext)
    {
        if (ext == null) return false;
        var level = VanillaLevelCatalog.FindLevelByToken(ext.Source);
        if (level?.Hierarchy == null) return ext.GameObjects.Count > 0;
        var pruned = PruneList(ext.GameObjects, VanillaLevelCatalog.RootChildren(level));
        return !ReferenceEquals(pruned, ext.GameObjects);
    }

    /// <summary>How many GameObjects this extension would actually write.</summary>
    public static int ChangedCount(VanillaPlaceExtensionDef ext)
    {
        if (ext == null) return 0;
        var level = VanillaLevelCatalog.FindLevelByToken(ext.Source);
        if (level?.Hierarchy == null) return CountNodes(ext.GameObjects);
        return CountNodes(PruneList(ext.GameObjects, VanillaLevelCatalog.RootChildren(level)));
    }

    /// <summary>
    /// The two ways an extension can affect a vanilla level, counted apart:
    /// <see cref="Added"/> GameObjects the pack introduces, and
    /// <see cref="Modified"/> objects of the game's own that it reaches into.
    /// <para/>
    /// They're different kinds of intrusion and worth reading separately —
    /// dropping a prop onto a level leaves the level itself untouched, while
    /// moving or gating one of its existing objects doesn't.
    /// </summary>
    public readonly record struct Tally(int Added, int Modified)
    {
        public bool Any => Added > 0 || Modified > 0;
    }

    public static Tally Analyze(VanillaPlaceExtensionDef ext)
    {
        if (ext == null) return default;
        var level = VanillaLevelCatalog.FindLevelByToken(ext.Source);
        var nodes = level?.Hierarchy == null
            ? ext.GameObjects
            : PruneList(ext.GameObjects, VanillaLevelCatalog.RootChildren(level));
        int added = 0, modified = 0;
        CountKinds(nodes, ref added, ref modified);
        return new Tally(added, modified);
    }

    private static void CountKinds(List<GameObjectDef> nodes, ref int added, ref int modified)
    {
        foreach (var n in nodes)
        {
            if (!n.Bind)
            {
                // A created node — and everything under it — is new content.
                added++;
            }
            else if (ChangesItsTarget(n))
            {
                modified++;
            }
            // A bound node that changes nothing itself is only here as the path
            // to something below it, so it counts as neither.
            CountKinds(n.Children, ref added, ref modified);
        }
    }

    /// <summary>Whether a bound node actually does something to the object it
    /// resolves, rather than merely being an ancestor of one that does.</summary>
    private static bool ChangesItsTarget(GameObjectDef n)
        => n.OverrideTransform || n.OverrideActive || AltersItsTarget(n);

    // ── Shared comparison predicates ──────────────────────────────────────
    //
    // The save-time prune and the editor's live "this differs from vanilla"
    // highlight must agree, or the editor would mark a node changed that saves
    // as nothing (or worse, the reverse). One definition each, used by both.

    /// <summary>Position / rotation / scale differ from the vanilla object.</summary>
    public static bool TransformDiffers(GameObjectDef n, VanillaLevelCatalog.Node b)
        => !Near(n.X, b.X) || !Near(n.Y, b.Y) || !Near(n.RotationZ, b.RotationZ)
           || !Near(n.ScaleX, b.ScaleX) || !Near(n.ScaleY, b.ScaleY);

    /// <summary>Active state differs from the vanilla object.</summary>
    public static bool ActiveDiffers(GameObjectDef n, VanillaLevelCatalog.Node b)
        => n.StartActive != b.ActiveSelf;

    /// <summary>
    /// Carries something that has to be written for it to work. Used by the
    /// SAVE to decide whether a bound node survives the prune: an NPC hung
    /// under a vanilla object needs that object's node in the manifest to say
    /// where it goes, even though the object itself is untouched.
    /// </summary>
    public static bool HasOwnAdditions(GameObjectDef n)
        => n.Components.Count > 0 || n.ActiveConditions.Count > 0 || n.Npcs.Count > 0;

    /// <summary>
    /// Whether the pack ALTERS the vanilla object, as opposed to merely hanging
    /// new content off it. The distinction matters to the author and not to the
    /// serializer: attaching a component or gating its active state changes the
    /// object the game shipped, while parenting an NPC or a new GameObject under
    /// it leaves it exactly as it was.
    /// <para/>
    /// Kept apart from <see cref="HasOwnAdditions"/> on purpose — conflating
    /// them marked the level's own NPCs container as "changed" the moment an NPC
    /// was placed in it, which is the one thing that container is for.
    /// </summary>
    public static bool AltersItsTarget(GameObjectDef n)
        => n.Components.Count > 0 || n.ActiveConditions.Count > 0;

    /// <summary>
    /// LIVE test of whether a bound node currently changes its vanilla target —
    /// compared against the seeded baseline rather than the override flags,
    /// which aren't computed until save. Backs the editor's per-row highlight.
    /// False for nodes the pack creates (they're additions, not changes) and for
    /// nodes with no baseline to compare against.
    /// </summary>
    public static bool IsLiveVanillaChange(GameObjectDef n)
    {
        if (n == null || !n.Bind) return false;
        var b = n.Baseline;
        if (b == null) return n.OverrideTransform || n.OverrideActive || AltersItsTarget(n);
        return TransformDiffers(n, b) || ActiveDiffers(n, b) || AltersItsTarget(n);
    }

    // ── Re-anchoring (the inverse of the prune) ───────────────────────────

    /// <summary>
    /// Point a bound node back at its baseline, restoring every field the save
    /// path is entitled to DROP. The counterpart of <see cref="Prune"/> — they
    /// have to agree, so they live together.
    /// <para/>
    /// A bound node that overrides nothing writes no transform, no active flag
    /// and no renderer fields: they belong to the vanilla object, not to the
    /// pack. That means they come back from disk as CLR defaults, and comparing
    /// THOSE against the baseline reports every untouched node as moved to the
    /// origin and scaled to 1 — which the next save then bakes in as a real
    /// override. Attaching a baseline is therefore not enough; the node has to
    /// be re-anchored to it.
    /// <para/>
    /// Anchoring is a no-op semantically: while a node isn't overriding, its own
    /// transform is never applied to anything. It exists so the comparison, the
    /// preview and the gizmo all read the object the game actually has.
    /// </summary>
    public static void Rebase(GameObjectDef node,
                              VanillaLevelCatalog.Node baseline,
                              VanillaLevelCatalog.Level level)
    {
        if (node == null || baseline == null) return;
        node.Baseline = baseline;

        // Preview-only and dropped for every bound node, so always re-derived.
        node.VanillaArtPath = VanillaLevelCatalog.FindNodeArt(level, baseline);
        node.SortingOrder = baseline.SpriteRenderer?.SortingOrder ?? 0;
        // How solid the object really draws — renderer tint and material alpha
        // together. Without this a vanilla reflection previewed at full
        // strength, since its material carries the fade and its colour doesn't.
        node.StartAlpha = baseline.SpriteRenderer?.EffectiveAlpha ?? 1f;
        // ...and its colour, not just that colour's alpha. Dropping the RGB is
        // what made the mauve street reflections preview at full sprite colour.
        node.Tint = baseline.SpriteRenderer?.TintRgb ?? "";

        if (!node.OverrideTransform)
        {
            node.X = baseline.X;
            node.Y = baseline.Y;
            node.RotationZ = baseline.RotationZ;
            node.ScaleX = baseline.ScaleX;
            node.ScaleY = baseline.ScaleY;
        }
        if (!node.OverrideActive) node.StartActive = baseline.ActiveSelf;
    }

    /// <summary>
    /// Put a bound node back to exactly what the vanilla level has, discarding
    /// whatever was authored on it. Uses the baseline already attached by
    /// seeding, so it restores the REAL extracted values rather than zeroing
    /// fields. Does nothing for a node the pack created — there's no vanilla
    /// state to go back to.
    /// </summary>
    public static bool ResetToBaseline(GameObjectDef node)
    {
        var b = node?.Baseline;
        if (b == null || !node.Bind) return false;

        node.OverrideTransform = false;
        node.OverrideActive = false;
        node.X = b.X;
        node.Y = b.Y;
        node.RotationZ = b.RotationZ;
        node.ScaleX = b.ScaleX;
        node.ScaleY = b.ScaleY;
        node.StartActive = b.ActiveSelf;
        node.SortingOrder = b.SpriteRenderer?.SortingOrder ?? 0;
        return true;
    }

    private static int CountNodes(List<GameObjectDef> nodes)
    {
        int n = 0;
        foreach (var g in nodes) n += 1 + CountNodes(g.Children);
        return n;
    }

    /// <summary>Prune a sibling list against the matching baseline siblings.
    /// Returns the original instance when nothing changed, so an extension with
    /// no bound nodes costs nothing.</summary>
    private static List<GameObjectDef> PruneList(List<GameObjectDef> nodes,
                                                 IReadOnlyList<VanillaLevelCatalog.Node> baseline)
    {
        List<GameObjectDef>? result = null;
        for (int i = 0; i < nodes.Count; i++)
        {
            var kept = Prune(nodes[i], FindBaseline(baseline, nodes[i].Name));
            if (!ReferenceEquals(kept, nodes[i]) || kept == null)
            {
                // First divergence — copy everything decided so far.
                if (result == null)
                {
                    result = new List<GameObjectDef>(nodes.Count);
                    for (int j = 0; j < i; j++) result.Add(nodes[j]);
                }
            }
            if (result != null && kept != null) result.Add(kept);
        }
        return result ?? nodes;
    }

    private static VanillaLevelCatalog.Node? FindBaseline(
        IReadOnlyList<VanillaLevelCatalog.Node> siblings, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var b in siblings)
            if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;
        return null;
    }

    /// <summary>
    /// Returns the node to serialize: the original when nothing needed
    /// rewriting, a trimmed copy when it did, or null when the node carries no
    /// change at all and can be dropped.
    /// </summary>
    private static GameObjectDef? Prune(GameObjectDef node, VanillaLevelCatalog.Node? baseline)
    {
        // A node the pack creates is always real content. Its children are
        // created too (they don't exist in vanilla), so nothing below it can be
        // compared — keep the subtree verbatim.
        if (!node.Bind) return node;

        // Bound, but the baseline doesn't know this object: keep the author's
        // manual flags rather than guessing it's unchanged.
        if (baseline == null) return node;

        bool transformDiffers = TransformDiffers(node, baseline);
        bool activeDiffers = ActiveDiffers(node, baseline);

        var prunedChildren = PruneList(node.Children, baseline.Children);

        bool addsSomething = HasOwnAdditions(node) || prunedChildren.Count > 0;

        // Nothing differs and nothing hangs off it — the node would resolve an
        // existing object and then do precisely nothing.
        if (!transformDiffers && !activeDiffers && !addsSomething) return null;

        // Something to say: emit a copy carrying only the applicable overrides.
        var copy = Clone(node);
        copy.OverrideTransform = transformDiffers;
        copy.OverrideActive = activeDiffers;
        copy.Children = prunedChildren;
        return copy;
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) <= Epsilon;

    /// <summary>Shallow copy sharing the child collections we don't rewrite.
    /// The copy is serialized and discarded, so sharing is safe.</summary>
    private static GameObjectDef Clone(GameObjectDef n) => new()
    {
        Name = n.Name,
        Sprite = n.Sprite,
        X = n.X, Y = n.Y, RotationZ = n.RotationZ, ScaleX = n.ScaleX, ScaleY = n.ScaleY,
        SortingOrder = n.SortingOrder,
        ParallaxDisabled = n.ParallaxDisabled,
        StartActive = n.StartActive,
        StartAlpha = n.StartAlpha,
        Mask = n.Mask,
        Components = n.Components,
        Children = n.Children,
        Npcs = n.Npcs,
        Role = n.Role,
        ActiveConditions = n.ActiveConditions,
        DeactivateWhenUnmet = n.DeactivateWhenUnmet,
        Bind = n.Bind,
        OverrideTransform = n.OverrideTransform,
        OverrideActive = n.OverrideActive,
    };
}
