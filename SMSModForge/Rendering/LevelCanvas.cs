using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// A level's own canvas over the place, as the game shows it (after 1.10.0, the author: "I don't see the player room
/// buttons in the preview of the Bedroom for Androids at all. Why is that?"). The player's room's Sleep, Calendar and
/// PC buttons are no sprites: they are images on <c>5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas</c>, a canvas drawn over
/// the whole screen (Screen Space Overlay, 1920x1080, under the game's interface at order -3). The level's
/// extraction keeps sprites alone, so the place preview never drew them - not in the game's own room, not in a pack's
/// place with a copy of it. They are drawn here from the game's interface extraction, by the renderer that draws the
/// game's screens (<see cref="UiSceneRenderer"/>), with what the pack's place says of them: an object of it moved
/// (its X and Y are the game's own local position: canvas pixels from its parent's middle), switched on or off.
/// <para/>
/// Shown as on arrival: the canvas's fade (its CanvasGroup, see-through when the game's interface was extracted -
/// the room's triggers fade it in) as it ends, seen.
/// </summary>
public static class LevelCanvas
{
    /// <summary>What a pack's place says of one object of the canvas, by its path under the canvas: its local
    /// position, size and turn when it overrides them, and whether it is on when it says.</summary>
    public sealed record Move(string Path, float[]? At, float[]? Scale, float? Turn, bool? Active);

    /// <summary>The game's canvas at <paramref name="scenePath"/> when it is one drawn over the screen; null
    /// otherwise, or without the game's extracted interface.</summary>
    public static VanillaUiSurface? SurfaceAt(string? scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath) || !scenePath.StartsWith("5_Levels/", StringComparison.Ordinal)
            || !VanillaUiLibrary.IsAvailable) return null;
        var surface = VanillaUiLibrary.Surface(scenePath);
        return surface is { IsDrawable: true } && string.Equals(surface.Canvas.RenderMode, "ScreenSpaceOverlay", StringComparison.Ordinal)
            ? surface : null;
    }

    /// <summary>What <paramref name="node"/> - a copy of the canvas, or the game's own canvas bound in an extension of
    /// its level - says of the canvas's objects, its own children by their names, all the way down.</summary>
    public static List<Move> MovesOf(GameObjectDef node)
    {
        var moves = new List<Move>();
        void Walk(GameObjectDef parent, string prefix)
        {
            foreach (var child in parent.Children)
            {
                if (!child.Bind || string.IsNullOrEmpty(child.Name)) continue;
                string path = prefix.Length == 0 ? child.Name : prefix + "/" + child.Name;
                if (child.OverrideTransform || child.OverrideActive)
                    moves.Add(new Move(path,
                                       child.OverrideTransform ? new[] { child.X, child.Y } : null,
                                       child.OverrideTransform ? new[] { child.ScaleX, child.ScaleY } : null,
                                       child.OverrideTransform ? child.RotationZ : null,
                                       child.OverrideActive ? child.StartActive : null));
                Walk(child, path);
            }
        }
        Walk(node, "");
        return moves;
    }

    /// <summary>The object at <paramref name="path"/> under the canvas, or null.</summary>
    public static VanillaUiSurface.Node? Find(VanillaUiSurface.Node root, string path)
    {
        var at = root;
        foreach (var name in path.Split('/'))
        {
            at = at.Children.FirstOrDefault(c => c.Name == name);
            if (at == null) return null;
        }
        return at;
    }

    /// <summary>
    /// Where the object at <paramref name="path"/> is, as the game's local position: canvas pixels from its parent's
    /// pivot (a RectTransform's localPosition: its anchored position plus where its anchor is from the parent's
    /// pivot) - what a pack's moved object says, and where the preview puts its handles. Null when not found.
    /// </summary>
    public static (double X, double Y)? LocalPosition(VanillaUiSurface surface, string path)
    {
        var parent = surface.Root;
        VanillaUiSurface.Node? node = null;
        var names = path.Split('/');
        for (int i = 0; i < names.Length; i++)
        {
            node = parent.Children.FirstOrDefault(c => c.Name == names[i]);
            if (node == null) return null;
            if (i < names.Length - 1) parent = node;
        }
        if (node?.Rect is not { } r) return null;
        var (ox, oy) = AnchorFromPivot(parent, r);
        return (r.AnchoredPosition[0] + ox, r.AnchoredPosition[1] + oy);
    }

    /// <summary>A child's anchor point from its parent's pivot, in canvas pixels (the middle of its anchors where
    /// they spread).</summary>
    private static (double X, double Y) AnchorFromPivot(VanillaUiSurface.Node parent, VanillaUiSurface.Rect child)
    {
        var p = parent.Rect;
        double w = p?.Size is { Length: >= 2 } s && s[0] > 0 ? s[0] : p?.SizeDelta[0] ?? 0;
        double h = p?.Size is { Length: >= 2 } s2 && s2[1] > 0 ? s2[1] : p?.SizeDelta[1] ?? 0;
        double px = p?.Pivot[0] ?? 0.5, py = p?.Pivot[1] ?? 0.5;
        double ax = (child.AnchorMin[0] + child.AnchorMax[0]) / 2, ay = (child.AnchorMin[1] + child.AnchorMax[1]) / 2;
        return ((ax - px) * w, (ay - py) * h);
    }

    /// <summary>
    /// The canvas at its size (premultiplied BGRA, rows from the top) with <paramref name="moves"/> applied to a copy
    /// of it - the loaded surface, shared, is never touched - its fade in. Null when nothing could be drawn.
    /// </summary>
    public static byte[]? Render(VanillaUiSurface source, IReadOnlyList<Move> moves, out int width, out int height,
                                 UiRenderReport? report = null)
    {
        width = height = 0;
        var surface = JsonConvert.DeserializeObject<VanillaUiSurface>(JsonConvert.SerializeObject(source));
        if (surface == null || !surface.IsDrawable) return null;
        surface.Path = source.Path;
        // Seen as on arrival, its fade in.
        if (surface.Root.CanvasGroup is { } group) group.Alpha = 1f;
        foreach (var move in moves)
        {
            var names = move.Path.Split('/');
            var parent = surface.Root;
            VanillaUiSurface.Node? node = null;
            for (int i = 0; i < names.Length; i++)
            {
                node = parent.Children.FirstOrDefault(c => c.Name == names[i]);
                if (node == null) break;
                if (i < names.Length - 1) parent = node;
            }
            if (node == null) continue;
            if (move.Active is { } on) { node.ActiveSelf = on; node.ActiveInHierarchy = on; }
            if (node.Rect is not { } r) continue;
            if (move.At is { Length: >= 2 } at)
            {
                var (ox, oy) = AnchorFromPivot(parent, r);
                r.AnchoredPosition = new[] { (float)(at[0] - ox), (float)(at[1] - oy) };
                // A resolved rect is where the extraction found it: moved, it is worked out again.
                r.Resolved = null;
            }
            if (move.Scale is { Length: >= 2 } sc)
            {
                r.LocalScale = new[] { sc[0], sc[1], r.LocalScale.Length > 2 ? r.LocalScale[2] : 1f };
                r.Resolved = null;
            }
            if (move.Turn is { } turn)
            {
                r.LocalEuler = new[] { r.LocalEuler.Length > 0 ? r.LocalEuler[0] : 0f, r.LocalEuler.Length > 1 ? r.LocalEuler[1] : 0f, turn };
                r.Resolved = null;
            }
        }
        width = (int)Math.Round(surface.Width);
        height = (int)Math.Round(surface.Height);
        var pixels = UiSceneRenderer.Render(surface, VanillaUiLibrary.Assets, report);
        return pixels.Length == 0 ? null : pixels;
    }
}
