using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Builds the editable tree an author sees when they open a vanilla UI: every
/// object the game has, carrying the game's own values, bound to it.
/// <para/>
/// Seeding the whole thing rather than an empty list is what makes the screen
/// editable in place — an author who wants to move one label should see the
/// label, not a blank panel and a note about adding one. The cost of writing
/// all of it back out is paid at save time by <see cref="VanillaUiDelta"/>,
/// which drops everything that still matches. So the tree is big and the
/// manifest stays small, which is the right way round: the big one is a working
/// copy that never leaves memory.
/// <para/>
/// The same approach vanilla place extensions already take, for the same
/// reasons.
/// </summary>
public static class VanillaUiSeed
{
    /// <summary>
    /// A tree for the base itself, with every descendant. The root binds to
    /// <c>"."</c>; everything below binds to its path from the base.
    /// </summary>
    /// <param name="nameForKey">Turns the extraction's sprite key into the
    /// name an authored image should carry. Needed because nine sprite names in
    /// the game belong to more than one crop, so the raw name does not identify
    /// a picture — see VanillaUiAssets. Null falls back to the raw name, which
    /// is right for a caller with no assets and wrong only for those nine.</param>
    public static UiNodeDef FromBase(VanillaUiSurface.Node vanilla,
                                     Func<string, string>? nameForKey = null)
    {
        if (vanilla == null) throw new ArgumentNullException(nameof(vanilla));
        return Build(vanilla, ".", nameForKey);
    }

    /// <summary>
    /// The full screen from the game, with an already-stored delta laid back
    /// over it.
    /// <para/>
    /// This is what reopening a saved pack needs, and it is not the same as
    /// seeding. A pack on disk holds only what it changed — two or three nodes
    /// where the screen has thirty — so seeding alone would show the author
    /// their edit floating in nothing, and seeding that DISCARDED the delta
    /// would quietly throw the edit away. Both are worse than either being
    /// slow.
    /// <para/>
    /// A stored node that no longer matches anything in the game is kept rather
    /// than dropped: the screen may have moved on between game versions, and
    /// losing an author's work is a bigger failure than showing it in an odd
    /// place. <paramref name="stranded"/> counts those so the editor can say so.
    /// </summary>
    public static UiNodeDef Merge(VanillaUiSurface.Node vanilla,
                                  IEnumerable<UiNodeDef>? stored,
                                  out int stranded,
                                  Func<string, string>? nameForKey = null)
    {
        var seeded = FromBase(vanilla, nameForKey);
        stranded = 0;
        if (stored == null) return seeded;

        var byPath = new Dictionary<string, UiNodeDef>(StringComparer.Ordinal);
        Index(seeded, byPath);

        foreach (var node in stored)
            stranded += Apply(node, seeded, byPath);

        // The stored places have been used; the tree an author edits says the
        // same thing by the order its lists are in. Leaving the numbers behind
        // would let a stale one be written out again long after the order it
        // described stopped being asserted.
        Forget(seeded);
        return seeded;
    }

    private static void Forget(UiNodeDef node)
    {
        node.SiblingIndex = -1;
        foreach (var child in node.Children) Forget(child);
    }

    /// <summary>
    /// A screen of the game's, as objects the PACK owns.
    /// <para/>
    /// The difference from seeding an extension is one field, and it changes
    /// everything about what the result is. An extension's nodes carry a
    /// <c>Bind</c>: they name the game's objects and only say what differs, so
    /// editing one edits the game's screen. A copy carries no binds, so every
    /// object is the pack's - free to move, rename or delete - and the screen it
    /// came from is left untouched.
    /// <para/>
    /// That is what a pack wants when it needs a screen LIKE one of the game's
    /// rather than a changed version of it: a second shop, not a different
    /// general store.
    /// <para/>
    /// The cost is that a copy is frozen at the moment the extraction was taken.
    /// An extension rides along when the game moves its screen about; a copy
    /// stays as it was. For a screen meant to be independent that is the right
    /// way round.
    /// </summary>
    public static UiNodeDef CopyOf(VanillaUiSurface.Node vanilla,
                                   Func<string, string>? nameForKey = null)
    {
        var seeded = FromBase(vanilla, nameForKey);
        if (seeded != null) Unbind(seeded);
        return seeded!;
    }

    /// <summary>Cut every tie to the game's own objects. The override flags go
    /// with the binds: they only mean anything as "differs from the object this
    /// is bound to", and there is no longer such an object.</summary>
    private static void Unbind(UiNodeDef node)
    {
        node.Bind = "";
        node.OverrideRect = false;
        node.OverrideImage = false;
        node.OverrideText = false;
        node.OverrideActive = false;
        foreach (var child in node.Children) Unbind(child);
    }

    private static void Index(UiNodeDef node, Dictionary<string, UiNodeDef> into)
    {
        if (node.IsBound) into[node.Bind] = node;
        foreach (var child in node.Children) Index(child, into);
    }

    private static int Apply(UiNodeDef stored, UiNodeDef seededRoot,
                             Dictionary<string, UiNodeDef> byPath)
    {
        if (!stored.IsBound)
        {
            // Something the pack added, with nowhere obvious to go — its parent
            // was a bound node that resolved, and that call site attaches it.
            // Reaching here means the top level, so the base is its home.
            seededRoot.Children.Add(stored);
            return 0;
        }

        if (!byPath.TryGetValue(stored.Bind, out var target))
        {
            // The object it was written against is gone. Kept where it can be
            // seen and re-saved rather than silently dropped.
            seededRoot.Children.Add(stored);
            return 1;
        }

        if (stored.OverrideRect) target.Rect = stored.Rect;
        if (stored.OverrideImage) target.Image = stored.Image;
        if (stored.OverrideText) target.Text = stored.Text;
        if (stored.OverrideActive) target.StartActive = stored.StartActive;
        if (stored.Alpha.HasValue) target.Alpha = stored.Alpha;
        if (stored.Shadow != null) target.Shadow = stored.Shadow;
        if (stored.Outline != null) target.Outline = stored.Outline;
        if (stored.Components.Count > 0) target.Components = stored.Components;
        if (stored.ActiveConditions.Count > 0) target.ActiveConditions = stored.ActiveConditions;

        int stranded = 0;
        foreach (var child in stored.Children)
        {
            if (child.IsBound) { stranded += Apply(child, seededRoot, byPath); continue; }
            target.Children.Add(child);          // an addition, in its right place
        }

        // Last, so that everything it has to arrange is present: the objects
        // the game owns were already in the seed, and the pack's own have just
        // been added on the end.
        Reorder(stored, target, byPath);
        return stranded;
    }

    /// <summary>
    /// Put a parent's children back into the order the pack recorded.
    /// <para/>
    /// Recorded only when an author actually rearranged something, and then for
    /// every child at once — so this either has the whole arrangement or none
    /// of it, and never has to guess what to do with half.
    /// </summary>
    private static void Reorder(UiNodeDef stored, UiNodeDef target,
                                Dictionary<string, UiNodeDef> byPath)
    {
        var placed = new List<KeyValuePair<int, UiNodeDef>>();
        foreach (var child in stored.Children)
        {
            if (child.SiblingIndex < 0) continue;

            // A bound child is the seeded object it names; one the pack added
            // is the very object just put into the list.
            UiNodeDef? node = child.IsBound
                ? (byPath.TryGetValue(child.Bind, out var found) ? found : null)
                : child;

            if (node != null) placed.Add(new KeyValuePair<int, UiNodeDef>(child.SiblingIndex, node));
        }
        if (placed.Count == 0) return;

        foreach (var one in placed) target.Children.Remove(one.Value);
        foreach (var one in placed.OrderBy(o => o.Key))
            target.Children.Insert(Math.Min(one.Key, target.Children.Count), one.Value);
    }

    /// <summary>
    /// Put one object back the way the game has it, leaving its children and
    /// anything the pack added alone.
    /// <para/>
    /// Undo works on a step; this works on an object, which is what an author
    /// wants after a session of nudging one panel about and deciding they
    /// preferred it where it was. Everything the delta compares is restored, so
    /// the node stops asserting anything and drops out of the manifest.
    /// </summary>
    public static void ResetTo(UiNodeDef node, VanillaUiSurface.Node vanilla,
                               Func<string, string>? nameForKey = null)
    {
        if (node == null || vanilla == null) return;
        node.Rect = RectOf(vanilla.Rect);
        node.Image = ImageOf(vanilla.Image, nameForKey);
        node.Text = TextOf(vanilla.Text);
        node.Alpha = vanilla.CanvasGroup?.Alpha;
        node.Shadow = EffectOf(vanilla.Shadow);
        node.Outline = EffectOf(vanilla.Outline);
        node.ClipChildren = vanilla.Clips;
        node.StartActive = vanilla.ActiveSelf;
        node.ActiveConditions.Clear();
        node.Components.Clear();
    }

    /// <summary>The bind paths a tree uses, in order. For checking that a seed
    /// and the surface it came from still agree.</summary>
    public static IEnumerable<string> Paths(UiNodeDef node)
    {
        if (node.IsBound) yield return node.Bind;
        foreach (var child in node.Children)
            foreach (string path in Paths(child))
                yield return path;
    }

    private static UiNodeDef Build(VanillaUiSurface.Node vanilla, string path,
                                   Func<string, string>? nameForKey)
    {
        var node = new UiNodeDef
        {
            Name = vanilla.Name,
            Bind = path,
            StartActive = vanilla.ActiveSelf,
            Rect = RectOf(vanilla.Rect),
            Image = ImageOf(vanilla.Image, nameForKey),
            Text = TextOf(vanilla.Text),
            Alpha = vanilla.CanvasGroup?.Alpha,
            Shadow = EffectOf(vanilla.Shadow),
            Outline = EffectOf(vanilla.Outline),
            ClipChildren = vanilla.Clips,
        };

        // Repeated sibling names get the #n suffix the catalog and the delta
        // both use, so an object bound here is the one found later. Only the
        // ones that actually collide are suffixed, so the common case stays
        // readable in a manifest.
        var counts = vanilla.Children.GroupBy(c => c.Name)
                                     .ToDictionary(g => g.Key, g => g.Count());
        var used = new Dictionary<string, int>();

        foreach (var child in vanilla.Children.OrderBy(c => c.SiblingIndex))
        {
            used.TryGetValue(child.Name, out int nth);
            used[child.Name] = ++nth;

            string segment = counts[child.Name] > 1 ? child.Name + "#" + nth : child.Name;
            string childPath = path == "." ? segment : path + "/" + segment;
            node.Children.Add(Build(child, childPath, nameForKey));
        }
        return node;
    }

    // ── Copying the game's values in ─────────────────────────────────

    private static UiRectDef RectOf(VanillaUiSurface.Rect? from)
    {
        var made = new UiRectDef();
        if (from == null) return made;
        made.AnchorMin = Pair(from.AnchorMin, 0.5f);
        made.AnchorMax = Pair(from.AnchorMax, 0.5f);
        made.Pivot = Pair(from.Pivot, 0.5f);
        made.Position = Pair(from.AnchoredPosition, 0f);
        made.SizeDelta(from.SizeDelta);
        made.Scale = Pair(from.LocalScale, 1f);
        made.RotationZ = from.LocalEuler is { Length: > 2 } ? from.LocalEuler[2] : 0f;
        return made;
    }

    private static UiImageDef? ImageOf(VanillaUiSurface.Image? from,
                                      Func<string, string>? nameForKey)
    {
        if (from == null || string.IsNullOrEmpty(from.Sprite)) return null;

        // The name that identifies this exact picture, which for nine sprites in
        // the game is not the bare one.
        string named = nameForKey?.Invoke(from.SpriteKey) ?? "";
        return new UiImageDef
        {
            Sprite = named.Length > 0 ? named : from.Sprite,
            Type = from.Type,
            Tint = from.Color,
            FillCenter = from.FillCenter,
            PreserveAspect = from.PreserveAspect,
            PixelsPerUnitMultiplier = from.PixelsPerUnitMultiplier,
        };
    }

    private static UiTextDef? TextOf(VanillaUiSurface.Text? from)
        => from == null || string.IsNullOrEmpty(from.Value) ? null : new UiTextDef
        {
            Value = from.Value,
            Font = from.Font,
            Size = (float)from.Size,
            Color = from.Color,
            Alignment = from.Alignment,
            Wrap = from.Wraps,
            LineSpacing = (float)(from.Number(from.LineSpacing) ?? 0),
            CharacterSpacing = (float)(from.Number(from.CharacterSpacing) ?? 0),
        };

    private static UiEffectDef? EffectOf(VanillaUiSurface.Effect? from)
        => from == null ? null : new UiEffectDef
        {
            Color = from.Color,
            Distance = Pair(from.Distance, 0f),
        };

    private static float[] Pair(float[]? from, float fallback)
    {
        if (from == null || from.Length < 2) return new[] { fallback, fallback };
        return new[] { from[0], from[1] };
    }
}

/// <summary>Small helper so the seed can set a rect's size without repeating
/// the null handling at every call site.</summary>
internal static class UiRectDefExtensions
{
    public static void SizeDelta(this UiRectDef rect, float[]? from)
        => rect.Size = from is { Length: > 1 } ? new[] { from[0], from[1] }
                                               : new[] { 100f, 100f };
}
