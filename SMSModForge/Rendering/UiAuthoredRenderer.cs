using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Draws an AUTHORED UI tree — what the pack will produce, rather than what the
/// game currently has.
/// <para/>
/// The same walk as <see cref="UiSceneRenderer"/> and deliberately so: parent
/// before child, siblings in order, anchors resolved by
/// <see cref="UiLayout"/>. What differs is only where the values come from, and
/// that difference is the whole point — this is the one that shows an author
/// their own edits. A preview drawing the vanilla surface would have been a
/// picture of what they started from.
/// <para/>
/// The two must agree on an untouched tree. A seeded screen rendered through
/// here and the same screen rendered from the extraction produce the same
/// pixels, and that is checked, because any difference is information the seed
/// dropped on the way across.
/// </summary>
public static class UiAuthoredRenderer
{
    /// <summary>Draw an authored tree onto a canvas of the given size.</summary>
    /// <param name="highlight">An object to outline, so a tree selection is
    /// visible on the picture. Null draws no marker.</param>
    public static byte[] Render(UiNodeDef root, double canvasWidth, double canvasHeight,
                                IUiAssets assets, UiRenderReport? report = null,
                                UiNodeDef? highlight = null)
    {
        if (root == null || assets == null) return Array.Empty<byte>();
        int w = (int)Math.Round(canvasWidth), h = (int)Math.Round(canvasHeight);
        if (w <= 0 || h <= 0) return Array.Empty<byte>();

        var target = UiCompositor.NewLayer(w, h);
        var canvas = UiRect.FromCanvas(canvasWidth, canvasHeight);
        report ??= new UiRenderReport();

        Draw(target, w, h, root, canvas, assets, 1.0, null, report,
             root.Name, canvasWidth, canvasHeight, highlight, forceVisible: true);
        return target;
    }

    private static void Draw(byte[] target, int w, int h, UiNodeDef node, UiRect parentRect,
                             IUiAssets assets, double alpha, UiRect? clip,
                             UiRenderReport report, string path,
                             double canvasWidth, double canvasHeight,
                             UiNodeDef? highlight, bool forceVisible)
    {
        if (!node.StartActive && !forceVisible) return;

        var rect = UiLayout.Resolve(parentRect,
                                    Doubles(node.Rect.AnchorMin), Doubles(node.Rect.AnchorMax),
                                    Doubles(node.Rect.Pivot), Doubles(node.Rect.Position),
                                    Doubles(node.Rect.Size));

        // The root is shown at full opacity even when the game fades it in,
        // for the same reason it is shown at all - see UiSceneRenderer.
        double here = forceVisible ? alpha : alpha * (node.Alpha ?? 1f);
        if (here <= 0) return;

        var inner = node.ClipChildren ? Intersect(clip, rect) : clip;

        DrawImage(target, w, h, node, rect, assets, here, clip, report,
                  canvasWidth, canvasHeight);
        DrawText(target, w, h, node, rect, assets, here, clip, report,
                 canvasWidth, canvasHeight);

        foreach (var child in node.Children)
            Draw(target, w, h, child, rect, assets, here, inner, report,
                 path + "/" + child.Name, canvasWidth, canvasHeight, highlight,
                 forceVisible: false);

        // Drawn last so it sits over the object it marks, and over its
        // children — an outline hidden behind what it points at is no use.
        if (highlight != null && ReferenceEquals(node, highlight))
            Outline(target, w, h, UiLayout.ToScreen(rect, canvasWidth, canvasHeight));
    }

    private static void DrawImage(byte[] target, int w, int h, UiNodeDef node, UiRect rect,
                                  IUiAssets assets, double alpha, UiRect? clip,
                                  UiRenderReport report, double canvasWidth, double canvasHeight)
    {
        var image = node.Image;
        if (image == null || string.IsNullOrEmpty(image.Sprite)) return;

        var tint = UiColor.Parse(image.Tint);
        if (tint.IsInvisible) return;

        var sprite = assets.SpriteByName(image.Sprite);
        if (sprite == null) { report.MissingSprite(image.Sprite); return; }

        var screen = UiLayout.ToScreen(rect, canvasWidth, canvasHeight);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        // The border belongs to the sprite, not to the authored node — an
        // author picks a sprite, and how it slices is a property of the art.
        var border = image.Type.Equals("Sliced", StringComparison.OrdinalIgnoreCase)
            ? BorderFor(image.Sprite, assets) : default;

        double divisor = UiSlice.DivisorFor(PixelsPerUnitFor(image.Sprite, assets),
                                            100, image.PixelsPerUnitMultiplier);
        var pixels = UiSlice.Compose(sprite.Pixels, sprite.Width, sprite.Height,
                                     border, dw, dh, image.FillCenter, divisor);
        UiSlice.Tint(pixels, tint.B, tint.G, tint.R, tint.A);

        Place(target, w, h, pixels, dw, dh, screen, node, clip, alpha,
              canvasWidth, canvasHeight);
        report.Drawn++;
    }

    private static void DrawText(byte[] target, int w, int h, UiNodeDef node, UiRect rect,
                                 IUiAssets assets, double alpha, UiRect? clip,
                                 UiRenderReport report, double canvasWidth, double canvasHeight)
    {
        var text = node.Text;
        if (text == null || string.IsNullOrEmpty(text.Value)) return;

        var colour = UiColor.Parse(text.Color);
        if (colour.IsInvisible) return;

        var set = assets.Font(text.Font);
        if (set == null) { report.MissingFont(text.Font); return; }

        var screen = UiLayout.ToScreen(rect, canvasWidth, canvasHeight);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        var fallbacks = new List<TmpFont>();
        foreach (string name in set.Font.Fallbacks)
        {
            var also = assets.Font(name);
            if (also != null) fallbacks.Add(also.Font);
        }

        var layout = TmpTextLayout.Measure(set.Font, text.Value, text.Size,
                                           text.Wrap ? dw : 0, Align(text.Alignment),
                                           text.LineSpacing, text.CharacterSpacing,
                                           fallbacks);
        foreach (int missing in layout.Missing)
            report.MissingGlyph($"'{(char)missing}' in {text.Font}");

        double offset = VerticalOffset(text.Alignment, dh, layout.Height);
        int above = (int)Math.Max(0, Math.Ceiling(-offset));
        int below = (int)Math.Max(0, Math.Ceiling(offset + layout.Height - dh));
        int th = dh + above + below;

        var shifted = new TextLayout { Height = layout.Height };
        foreach (var g in layout.Glyphs) shifted.Glyphs.Add(g with { Y = g.Y + offset + above });
        shifted.LineWidths.AddRange(layout.LineWidths);

        var pixels = UiTextRaster.Draw(shifted, set.Font, set.Alpha, set.Width, set.Height,
                                       dw, th, colour);
        Place(target, w, h, pixels, dw, th,
              new UiRect(screen.X, screen.Y - above, dw, th), node, clip, alpha,
              canvasWidth, canvasHeight);
        report.Drawn++;
    }

    private static void Place(byte[] target, int w, int h, byte[] pixels, int dw, int dh,
                              UiRect screen, UiNodeDef node, UiRect? clip, double alpha,
                              double canvasWidth, double canvasHeight)
    {
        if (clip is { } c)
            Clip(pixels, dw, dh, screen, UiLayout.ToScreen(c, canvasWidth, canvasHeight));

        UiEffects.Draw(target, w, h, pixels, dw, dh,
                       (int)Math.Round(screen.X), (int)Math.Round(screen.Y),
                       ToEffect(node.Shadow), ToEffect(node.Outline), alpha);
    }

    /// <summary>A dashed box round the selected object, drawn straight onto the
    /// canvas. Dashed rather than solid because a UI is full of solid
    /// rectangles and a solid marker reads as part of the design.</summary>
    private static void Outline(byte[] target, int w, int h, UiRect box)
    {
        int x0 = (int)Math.Round(box.X), y0 = (int)Math.Round(box.Y);
        int x1 = (int)Math.Round(box.X + box.Width) - 1;
        int y1 = (int)Math.Round(box.Y + box.Height) - 1;
        if (x1 < x0 || y1 < y0) return;

        for (int x = x0; x <= x1; x++) { Dot(target, w, h, x, y0, x); Dot(target, w, h, x, y1, x); }
        for (int y = y0; y <= y1; y++) { Dot(target, w, h, x0, y, y); Dot(target, w, h, x1, y, y); }
    }

    private static void Dot(byte[] target, int w, int h, int x, int y, int phase)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        if ((phase / 6) % 2 == 1) return;                 // the dashes
        int i = (y * w + x) * 4;
        target[i] = 0; target[i + 1] = 140; target[i + 2] = 255; target[i + 3] = 255;
    }

    private static void Clip(byte[] pixels, int dw, int dh, UiRect screen, UiRect clip)
    {
        for (int y = 0; y < dh; y++)
        {
            double cy = screen.Y + y;
            bool insideY = cy >= clip.Y && cy < clip.Y + clip.Height;
            for (int x = 0; x < dw; x++)
            {
                double cx = screen.X + x;
                if (insideY && cx >= clip.X && cx < clip.X + clip.Width) continue;
                int i = (y * dw + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0;
            }
        }
    }

    /// <summary>A sprite's nine-slice border, which lives with the art rather
    /// than with the authored node. Looked up from the extraction so an author
    /// choosing a sprite gets its slicing without having to know about it.</summary>
    private static SliceBorder BorderFor(string spriteName, IUiAssets assets)
        => assets is VanillaUiAssets known ? known.BorderByName(spriteName) : default;

    private static double PixelsPerUnitFor(string spriteName, IUiAssets assets)
        => assets is VanillaUiAssets known ? known.PixelsPerUnitByName(spriteName) : 100;

    private static UiEffect? ToEffect(UiEffectDef? e)
        => e == null ? null
           : new UiEffect(UiColor.Parse(e.Color),
                          e.Distance.Length > 0 ? e.Distance[0] : 0,
                          e.Distance.Length > 1 ? e.Distance[1] : 0);

    private static UiRect Intersect(UiRect? outer, UiRect inner)
    {
        if (outer is not { } o) return inner;
        double x = Math.Max(o.X, inner.X), y = Math.Max(o.Y, inner.Y);
        double right = Math.Min(o.Right, inner.Right), top = Math.Min(o.Top, inner.Top);
        return new UiRect(x, y, Math.Max(0, right - x), Math.Max(0, top - y));
    }

    private static double VerticalOffset(string? alignment, double boxHeight, double textHeight)
    {
        string a = (alignment ?? "").ToLowerInvariant();
        if (a.Contains("top")) return 0;
        if (a.Contains("bottom")) return boxHeight - textHeight;
        return (boxHeight - textHeight) / 2.0;
    }

    private static TextAlign Align(string? alignment)
    {
        string a = (alignment ?? "").ToLowerInvariant();
        if (a.Contains("left")) return TextAlign.Left;
        if (a.Contains("right")) return TextAlign.Right;
        return TextAlign.Center;
    }

    private static double[] Doubles(float[]? pair)
        => pair == null ? new[] { 0.0, 0.0 } : pair.Select(v => (double)v).ToArray();
}
