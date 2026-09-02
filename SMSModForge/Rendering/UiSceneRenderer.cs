using System;
using System.Collections.Generic;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>A decoded sprite, ready to draw.</summary>
public sealed record UiSprite(byte[] Pixels, int Width, int Height);

/// <summary>A font asset with its atlas already decoded to one byte per texel.</summary>
public sealed record UiFontSet(TmpFont Font, byte[] Alpha, int Width, int Height);

/// <summary>Where the renderer gets its pictures and its letters. An interface
/// so the renderer can be tested without a disk, and so a pack's own assets can
/// be served alongside the game's without it knowing the difference.</summary>
public interface IUiAssets
{
    UiSprite? Sprite(string key);
    UiFontSet? Font(string name);
}

/// <summary>What the renderer could not draw, and why. Surfaced rather than
/// swallowed: a preview with a silently missing sprite is a preview that lies,
/// and the whole point of this one is that it does not.</summary>
public sealed class UiRenderReport
{
    public List<string> MissingSprites { get; } = new();
    public List<string> MissingFonts { get; } = new();
    public List<string> MissingGlyphs { get; } = new();
    public List<string> Untrustworthy { get; } = new();

    /// <summary>Text drawn by the game's LEGACY UI.Text rather than by
    /// TextMeshPro. Those name a Unity font file, not a baked atlas, so nothing
    /// here can draw them and they are listed apart from a genuinely missing
    /// font — one is a gap in the extraction, the other is a different
    /// mechanism that has not been built yet. About 149 objects in the game,
    /// against 2382 TextMeshPro ones.</summary>
    public List<string> LegacyText { get; } = new();
    public int Drawn { get; set; }

    public bool Complete => MissingSprites.Count == 0 && MissingFonts.Count == 0
                            && MissingGlyphs.Count == 0;

    private static void Note(List<string> into, string what)
    {
        if (into.Count < 40 && !into.Contains(what)) into.Add(what);
    }

    internal void MissingSprite(string key) => Note(MissingSprites, key);
    internal void MissingFont(string name) => Note(MissingFonts, name);
    internal void MissingGlyph(string what) => Note(MissingGlyphs, what);
    internal void NotTrustworthy(string path) => Note(Untrustworthy, path);
    internal void Legacy(string what) => Note(LegacyText, what);
}

/// <summary>
/// Draws a vanilla UI tree the way the game draws it.
/// <para/>
/// The whole tree in one pass, parent before child, siblings in order — because
/// that is what decides what covers what. A canvas has no z-buffer: an object
/// drawn later is simply on top, and its position among its siblings IS its
/// depth. So the walk order is not an implementation detail, it is the feature.
/// <para/>
/// Geometry comes from <see cref="UiLayout"/> rather than from the recorded
/// rectangles, even though the extraction has both. Two reasons. It is the same
/// code an authored object will go through, so a pack's own additions and the
/// vanilla around them cannot disagree about what an anchor means; and it was
/// checked against a hundred of the game's own answers, so using it is not a
/// step away from the truth.
/// </summary>
public static class UiSceneRenderer
{
    /// <summary>Draw a whole surface at its canvas size.</summary>
    public static byte[] Render(VanillaUiSurface surface, IUiAssets assets,
                                UiRenderReport? report = null)
    {
        if (surface == null || !surface.IsDrawable) return Array.Empty<byte>();
        int w = (int)Math.Round(surface.Width), h = (int)Math.Round(surface.Height);
        var target = UiCompositor.NewLayer(w, h);

        var canvas = UiRect.FromCanvas(surface.Width, surface.Height);
        DrawChildren(target, w, h, surface.Root, canvas, surface, assets,
                     1.0, null, report ?? new UiRenderReport(), surface.Path);
        return target;
    }

    /// <summary>
    /// Draw one base — a child of the canvas — on its own, at the canvas size.
    /// <para/>
    /// Still positioned as if the rest of the canvas were there, because it is:
    /// an anchor is a fraction of the parent, and a base measured against its
    /// own bounding box would sit somewhere the game never puts it.
    /// </summary>
    public static byte[] RenderBase(VanillaUiSurface surface, VanillaUiSurface.Node node,
                                    IUiAssets assets, UiRenderReport? report = null)
    {
        if (surface == null || node == null || !surface.IsDrawable) return Array.Empty<byte>();
        int w = (int)Math.Round(surface.Width), h = (int)Math.Round(surface.Height);
        var target = UiCompositor.NewLayer(w, h);

        var canvas = UiRect.FromCanvas(surface.Width, surface.Height);
        Draw(target, w, h, node, canvas, surface, assets, 1.0, null,
             report ?? new UiRenderReport(), surface.Path, forceVisible: true);
        return target;
    }

    // ── The walk ─────────────────────────────────────────────────────

    private static void DrawChildren(byte[] target, int w, int h,
                                     VanillaUiSurface.Node parent, UiRect parentRect,
                                     VanillaUiSurface surface, IUiAssets assets,
                                     double alpha, UiRect? clip,
                                     UiRenderReport report, string path)
    {
        // Sibling order is draw order. The extraction records it, and sorting by
        // it rather than trusting the array's order means a reordered file
        // cannot quietly change what is on top.
        var ordered = new List<VanillaUiSurface.Node>(parent.Children);
        ordered.Sort((a, b) => a.SiblingIndex.CompareTo(b.SiblingIndex));

        foreach (var child in ordered)
            Draw(target, w, h, child, parentRect, surface, assets, alpha, clip, report,
                 path + "/" + child.Name, forceVisible: false);
    }

    private static void Draw(byte[] target, int w, int h,
                             VanillaUiSurface.Node node, UiRect parentRect,
                             VanillaUiSurface surface, IUiAssets assets,
                             double alpha, UiRect? clip,
                             UiRenderReport report, string path, bool forceVisible)
    {
        // A switched-off object draws nothing, and neither does anything under
        // it. forceVisible is for looking at one base on purpose: the game keeps
        // 34 of its 49 canvases off until their moment, and an editor that
        // honoured that would show an author a blank rectangle.
        if (!node.ActiveSelf && !forceVisible) return;

        var rect = node.Rect == null
            ? parentRect
            : UiLayout.Resolve(parentRect,
                               Doubles(node.Rect.AnchorMin), Doubles(node.Rect.AnchorMax),
                               Doubles(node.Rect.Pivot), Doubles(node.Rect.AnchoredPosition),
                               Doubles(node.Rect.SizeDelta));

        if (node.Rect?.Resolved is { IsTrustworthy: false })
            report.NotTrustworthy(path);

        // A base being looked at on purpose is shown at full opacity, for the
        // same reason it is shown at all: several vanilla screens sit at alpha
        // zero until the game fades them in, and Tooltip_Finances is one. An
        // editor that honoured that would hand the author a blank rectangle and
        // no clue why. Everything BELOW the base keeps its own alpha.
        double here = forceVisible ? alpha : alpha * (node.CanvasGroup?.Alpha ?? 1f);
        if (here <= 0) return;

        var inner = node.Clips ? Intersect(clip, rect) : clip;

        DrawImage(target, w, h, node, rect, surface, assets, here, clip, report, path);
        DrawText(target, w, h, node, rect, surface, assets, here, clip, report, path);

        DrawChildren(target, w, h, node, rect, surface, assets, here, inner, report, path);
    }

    // ── What an object draws ─────────────────────────────────────────

    private static void DrawImage(byte[] target, int w, int h,
                                  VanillaUiSurface.Node node, UiRect rect,
                                  VanillaUiSurface surface, IUiAssets assets,
                                  double alpha, UiRect? clip,
                                  UiRenderReport report, string path)
    {
        var image = node.Image;
        if (image == null || !image.Enabled || string.IsNullOrEmpty(image.SpriteKey)) return;

        var tint = UiColor.Parse(image.Color);
        if (tint.IsInvisible) return;

        var sprite = assets.Sprite(image.SpriteKey);
        if (sprite == null) { report.MissingSprite(image.Sprite + " (" + image.SpriteKey + ")"); return; }

        var screen = UiLayout.ToScreen(rect, surface.Width, surface.Height);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        // An unsliced image is a sliced one with no border, so there is no
        // second path to keep in step with the first.
        var border = image.IsSliced && image.Border.Length >= 4
            ? new SliceBorder(image.Border[0], image.Border[1], image.Border[2], image.Border[3])
            : default;
        double divisor = UiSlice.DivisorFor(image.SpritePixelsPerUnit,
                                            surface.Canvas.ReferencePixelsPerUnit,
                                            image.PixelsPerUnitMultiplier);

        var pixels = UiSlice.Compose(sprite.Pixels, sprite.Width, sprite.Height,
                                     border, dw, dh, image.FillCenter, divisor);
        UiSlice.Tint(pixels, tint.B, tint.G, tint.R, tint.A);

        Place(target, w, h, pixels, dw, dh, screen, node, clip, alpha, surface);
        report.Drawn++;
    }

    private static void DrawText(byte[] target, int w, int h,
                                 VanillaUiSurface.Node node, UiRect rect,
                                 VanillaUiSurface surface, IUiAssets assets,
                                 double alpha, UiRect? clip,
                                 UiRenderReport report, string path)
    {
        var text = node.Text;
        if (text == null || !text.Enabled || string.IsNullOrEmpty(text.Value)) return;

        var colour = UiColor.Parse(text.Color);
        if (colour.IsInvisible) return;

        // Legacy UI.Text points at a Unity font file rather than a TMP
        // asset, so there is no atlas to draw from. Said plainly instead of
        // reported as a missing font, which would send someone looking for an
        // export that was never going to contain it.
        if (string.Equals(text.Kind, "UI.Text", StringComparison.OrdinalIgnoreCase))
        {
            report.Legacy(path + "  (" + (text.Font.Length > 0 ? text.Font : "no font") + ")");
            return;
        }

        var set = assets.Font(text.Font);
        if (set == null) { report.MissingFont(text.Font); return; }

        // The font's own fallback chain, which is how the game resolves a
        // character its first atlas never baked. Reproducing it is what stops
        // the preview failing where the game succeeds.
        var fallbacks = new List<TmpFont>();
        foreach (string name in set.Font.Fallbacks)
        {
            var also = assets.Font(name);
            if (also != null) fallbacks.Add(also.Font);
        }

        var screen = UiLayout.ToScreen(rect, surface.Width, surface.Height);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        var layout = TmpTextLayout.Measure(set.Font, text.Value, text.Size,
                                           text.Wraps ? dw : 0,
                                           Align(text.Alignment),
                                           text.Number(text.LineSpacing) ?? 0,
                                           text.Number(text.CharacterSpacing) ?? 0,
                                           fallbacks);
        foreach (int missing in layout.Missing)
            report.MissingGlyph($"'{(char)missing}' in {text.Font}");

        // Where the block sits vertically inside its rectangle. This is not a
        // detail: 1945 of the game's 2273 TextMeshPro objects are vertically
        // middled, and laying every one of them from the top cut titles in half
        // wherever the font was larger than its box.
        double offset = VerticalOffset(text.Alignment, dh, layout.Height);

        // TextMeshPro lets text overflow its rectangle by default, so the
        // buffer is grown to hold whatever spills rather than cropping it. A
        // preview that silently trimmed the overflow would hide exactly the
        // authoring mistake it exists to show.
        int above = (int)Math.Max(0, Math.Ceiling(-offset));
        int below = (int)Math.Max(0, Math.Ceiling(offset + layout.Height - dh));
        int th = dh + above + below;

        var shifted = new TextLayout();
        shifted.Height = layout.Height;
        foreach (var g in layout.Glyphs)
            shifted.Glyphs.Add(g with { Y = g.Y + offset + above });
        shifted.LineWidths.AddRange(layout.LineWidths);

        var pixels = UiTextRaster.Draw(shifted, set.Font, set.Alpha, set.Width, set.Height,
                                       dw, th, colour);
        var placed = new UiRect(screen.X, screen.Y - above, dw, th);
        Place(target, w, h, pixels, dw, th, placed, node, clip, alpha, surface);
        report.Drawn++;
    }

    /// <summary>Put a drawn graphic on the canvas, with its effects and inside
    /// whatever is clipping it.</summary>
    private static void Place(byte[] target, int w, int h, byte[] pixels, int dw, int dh,
                              UiRect screen, VanillaUiSurface.Node node,
                              UiRect? clip, double alpha, VanillaUiSurface surface)
    {
        // The clip is carried down the tree in canvas space, like every other
        // rectangle here, and converted only at the point it is used against a
        // graphic that has already been flipped.
        if (clip is { } c)
            Clip(pixels, dw, dh, screen, UiLayout.ToScreen(c, surface.Width, surface.Height));

        UiEffects.Draw(target, w, h, pixels, dw, dh,
                       (int)Math.Round(screen.X), (int)Math.Round(screen.Y),
                       ToEffect(node.Shadow), ToEffect(node.Outline), alpha);
    }

    /// <summary>Erase whatever falls outside a mask. Done to the graphic before
    /// it lands rather than to the canvas after, so one object's mask cannot
    /// cut into what was already drawn beneath it.</summary>
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

    // ── Small conversions ────────────────────────────────────────────

    private static UiEffect? ToEffect(VanillaUiSurface.Effect? e)
        => e == null ? null
           : new UiEffect(UiColor.Parse(e.Color),
                          e.Distance.Length > 0 ? e.Distance[0] : 0,
                          e.Distance.Length > 1 ? e.Distance[1] : 0,
                          e.UseGraphicAlpha);

    /// <summary>A mask inside a mask clips to the overlap, not to the newer of
    /// the two — scroll views nest them and the inner one must not escape.</summary>
    private static UiRect Intersect(UiRect? outer, UiRect inner)
    {
        if (outer is not { } o) return inner;
        double x = Math.Max(o.X, inner.X), y = Math.Max(o.Y, inner.Y);
        double right = Math.Min(o.Right, inner.Right), top = Math.Min(o.Top, inner.Top);
        return new UiRect(x, y, Math.Max(0, right - x), Math.Max(0, top - y));
    }

    /// <summary>How far down its rectangle a block of text starts.
    /// <para/>
    /// TextMeshPro spells both axes into one name, vertical first: "Center" is
    /// middled and centred, "TopLeft" is neither, and a bare "Left" is still
    /// vertically middled. Baseline, Midline and Capline are treated as middle
    /// - they differ by where within the line the anchor sits, which is a
    /// smaller error than ignoring the axis, and only one object in the game
    /// uses any of them.</summary>
    private static double VerticalOffset(string? alignment, double boxHeight,
                                         double textHeight)
    {
        string a = (alignment ?? "").ToLowerInvariant();
        if (a.Contains("top")) return 0;
        if (a.Contains("bottom")) return boxHeight - textHeight;
        return (boxHeight - textHeight) / 2.0;
    }

    private static TextAlign Align(string? alignment)
    {
        if (string.IsNullOrEmpty(alignment)) return TextAlign.Center;
        string a = alignment.ToLowerInvariant();
        if (a.Contains("left")) return TextAlign.Left;
        if (a.Contains("right")) return TextAlign.Right;
        return TextAlign.Center;
    }

    private static double[] Doubles(float[]? pair)
    {
        if (pair == null) return new[] { 0.0, 0.0 };
        var made = new double[pair.Length];
        for (int i = 0; i < pair.Length; i++) made[i] = pair[i];
        return made;
    }
}
