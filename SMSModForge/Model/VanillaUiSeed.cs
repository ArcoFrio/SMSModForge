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
    public static UiNodeDef FromBase(VanillaUiSurface.Node vanilla)
    {
        if (vanilla == null) throw new ArgumentNullException(nameof(vanilla));
        return Build(vanilla, ".");
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

    private static UiNodeDef Build(VanillaUiSurface.Node vanilla, string path)
    {
        var node = new UiNodeDef
        {
            Name = vanilla.Name,
            Bind = path,
            StartActive = vanilla.ActiveSelf,
            Rect = RectOf(vanilla.Rect),
            Image = ImageOf(vanilla.Image),
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
            node.Children.Add(Build(child, childPath));
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

    private static UiImageDef? ImageOf(VanillaUiSurface.Image? from)
        => from == null || string.IsNullOrEmpty(from.Sprite) ? null : new UiImageDef
        {
            Sprite = from.Sprite,
            Type = from.Type,
            Tint = from.Color,
            FillCenter = from.FillCenter,
            PreserveAspect = from.PreserveAspect,
        };

    private static UiTextDef? TextOf(VanillaUiSurface.Text? from)
        => from == null || string.IsNullOrEmpty(from.Value) ? null : new UiTextDef
        {
            Value = from.Value,
            Font = from.Font,
            Size = (float)from.Size,
            Color = from.Color,
            Alignment = from.Alignment,
            Wrap = from.Wraps,
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
