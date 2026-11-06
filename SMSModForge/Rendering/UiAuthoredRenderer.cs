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
        => Render(root == null ? Array.Empty<UiNodeDef>() : new[] { root },
                  canvasWidth, canvasHeight, assets, report, highlight);

    /// <summary>
    /// Draw several trees onto one canvas, first at the back.
    /// <para/>
    /// What testing needs: a screen that opens another screen has two on
    /// display, and drawing only the one being edited would show a click doing
    /// nothing. Later roots go over earlier ones, which is what a canvas does
    /// with its own children.
    /// </summary>
    public static byte[] Render(IReadOnlyList<UiNodeDef> roots,
                                double canvasWidth, double canvasHeight,
                                IUiAssets assets, UiRenderReport? report = null,
                                UiNodeDef? highlight = null)
    {
        if (roots == null || roots.Count == 0 || assets == null) return Array.Empty<byte>();
        int w = (int)Math.Round(canvasWidth), h = (int)Math.Round(canvasHeight);
        if (w <= 0 || h <= 0) return Array.Empty<byte>();

        var target = UiCompositor.NewLayer(w, h);
        var canvas = UiRect.FromCanvas(canvasWidth, canvasHeight);
        report ??= new UiRenderReport();

        foreach (var root in roots)
        {
            if (root == null) continue;

            // The root is shown whatever its own flag says - it is the thing
            // being looked at. Anything below it obeys what was authored.
            Draw(target, w, h, root, canvas, UiXform.Identity, assets, 1.0, null, report,
                 root.Name, canvasWidth, canvasHeight, highlight, forceVisible: true);
        }
        return target;
    }

    private static void Draw(byte[] target, int w, int h, UiNodeDef node, UiRect parentRect,
                             UiXform inherited,
                             IUiAssets assets, double alpha, UiRect? clip,
                             UiRenderReport report, string path,
                             double canvasWidth, double canvasHeight,
                             UiNodeDef? highlight, bool forceVisible,
                             UiRect? placed = null, double turnedBy = 0, bool level = false)
    {
        if (!node.StartActive && !forceVisible) return;

        // The author's eye, and only this draw: what arranges the object has
        // already counted it, so hiding one moves nothing else.
        if (node.HiddenInPreview) return;

        // Shared with the gizmo, deliberately — see UiGeometry. Two rectangles
        // out of it: children are laid out against the unscaled one, everything
        // is drawn at the scaled one.
        var (layout, rect, inner) = UiGeometry.Place(node, parentRect, inherited, placed);

        // Turned: this object and everything inside it are drawn level into a
        // layer of their own and that is turned about its pivot. One that stays
        // upright is turned back by exactly what the objects around it turned,
        // which leaves it level and still carried along with them.
        double turn = node.Upright ? -turnedBy : node.Rect.RotationZ;
        if (!level && Math.Abs(turn) > 0.001)
        {
            var layer = UiCompositor.NewLayer(w, h);
            Draw(layer, w, h, node, parentRect, inherited, assets, alpha, null, report, path,
                 canvasWidth, canvasHeight, highlight, forceVisible, placed, turnedBy + turn, level: true);

            var pivot = UiLayout.ToScreen(
                inherited.Apply(new UiRect(layout.X + At(node.Rect.Pivot, 0) * layout.Width,
                                           layout.Y + At(node.Rect.Pivot, 1) * layout.Height, 0, 0)),
                canvasWidth, canvasHeight);
            UiCompositor.BlendTurned(target, layer, w, h, pivot.X, pivot.Y, turn,
                                     clip is { } outer ? UiLayout.ToScreen(outer, canvasWidth, canvasHeight) : null);
            return;
        }

        // The root is shown at full opacity even when the game fades it in,
        // for the same reason it is shown at all - see UiSceneRenderer.
        double here = forceVisible ? alpha : alpha * (node.Alpha ?? 1f);
        if (here <= 0) return;

        var innerClip = node.ClipChildren ? Intersect(clip, rect) : clip;

        DrawImage(target, w, h, node, rect, layout, inner, assets, here, clip, report,
                  canvasWidth, canvasHeight);
        DrawText(target, w, h, node, rect, layout, inner, assets, here, clip, report,
                 canvasWidth, canvasHeight);

        // A node that arranges its children hands each one its rectangle.
        var arranged = UiGeometry.ArrangeChildren(node, layout);
        for (int i = 0; i < node.Children.Count; i++)
        {
            var child = node.Children[i];
            Draw(target, w, h, child, layout, inner, assets, here, innerClip, report,
                 path + "/" + child.Name, canvasWidth, canvasHeight, highlight,
                 forceVisible: false, placed: arranged?[i], turnedBy: turnedBy);
        }

        // Drawn last so it sits over the object it marks, and over its
        // children — an outline hidden behind what it points at is no use.
        if (highlight != null && ReferenceEquals(node, highlight))
            Outline(target, w, h, UiLayout.ToScreen(rect, canvasWidth, canvasHeight));
    }

    private static void DrawImage(byte[] target, int w, int h, UiNodeDef node, UiRect rect,
                                  UiRect own, UiXform scale,
                                  IUiAssets assets, double alpha, UiRect? clip,
                                  UiRenderReport report, double canvasWidth, double canvasHeight)
    {
        var image = node.Image;
        if (image == null || string.IsNullOrEmpty(image.Sprite)) return;

        var tint = UiGameLook.Rested(UiColor.Parse(image.Tint), node.GameTint);
        if (tint.IsInvisible) return;

        var sprite = assets.SpriteByName(image.Sprite);
        if (sprite == null) { report.MissingSprite(image.Sprite); return; }

        var screen = UiLayout.ToScreen(rect, canvasWidth, canvasHeight);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        bool sliced = image.Type.Equals("Sliced", StringComparison.OrdinalIgnoreCase);
        bool tiled = image.Type.Equals("Tiled", StringComparison.OrdinalIgnoreCase);

        // The border belongs to the sprite, not to the authored node — an
        // author picks a sprite, and how it slices is a property of the art.
        var border = sliced || tiled ? BorderFor(image.Sprite, assets) : default;

        double divisor = UiSlice.DivisorFor(PixelsPerUnitFor(image.Sprite, assets),
                                            100, image.PixelsPerUnitMultiplier);

        // Worked out at the object's own size and then scaled, as the game's mesh
        // is: under a scaled parent the corners of a sliced picture shrink with
        // it. Worked out at the scaled size they stayed full size.
        var area = UiSlice.DrawnArea(new UiRect(0, 0, own.Width, own.Height), sprite, sliced, tiled, image.PreserveAspect,
                                     divisor, At(node.Rect.Pivot, 0), At(node.Rect.Pivot, 1));
        int ow = (int)Math.Round(area.Width), oh = (int)Math.Round(area.Height);
        if (ow <= 0 || oh <= 0) return;

        var pixels = tiled
            ? UiSlice.Tile(sprite.Pixels, sprite.Width, sprite.Height, border, ow, oh, image.FillCenter, divisor)
            : UiSlice.Compose(sprite.Pixels, sprite.Width, sprite.Height,
                              border, ow, oh, image.FillCenter, divisor);

        screen = new UiRect(screen.X + area.X * scale.ScaleX, screen.Y + area.Y * scale.ScaleY,
                            area.Width * scale.ScaleX, area.Height * scale.ScaleY);
        dw = (int)Math.Round(screen.Width);
        dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;
        if (dw != ow || dh != oh) pixels = PixelResample.Bilinear(pixels, ow, oh, dw, dh);
        UiSlice.Tint(pixels, tint.B, tint.G, tint.R, tint.A);

        Place(target, w, h, pixels, dw, dh, screen, node, clip, alpha,
              canvasWidth, canvasHeight, scale: scale);
        report.Drawn++;
    }

    private static void DrawText(byte[] target, int w, int h, UiNodeDef node, UiRect rect,
                                 UiRect own, UiXform scale,
                                 IUiAssets assets, double alpha, UiRect? clip,
                                 UiRenderReport report, double canvasWidth, double canvasHeight)
    {
        var text = node.Text;
        if (text == null || string.IsNullOrEmpty(text.Value)) return;

        // A label naming a variable is drawn as the value a player would see,
        // not as the token - see UiTextTokens. Done here rather than at the
        // model, because it is how the label is SHOWN and the pack still stores
        // what was authored.
        string shown = UiTextTokens.Resolve(text.Value);
        if (string.IsNullOrEmpty(shown)) return;

        var colour = UiGameLook.Rested(UiColor.Parse(text.Color), node.GameTint);
        if (colour.IsInvisible) return;

        var set = assets.Font(text.Font);
        if (set == null) { report.MissingFont(text.Font); return; }

        var screen = UiLayout.ToScreen(rect, canvasWidth, canvasHeight);
        int dw = (int)Math.Round(screen.Width), dh = (int)Math.Round(screen.Height);
        if (dw <= 0 || dh <= 0) return;

        var fallbacks = UiTextBlock.Fallbacks(set, assets);

        var style = UiTextStyle.Parse(text.GameStyle);

        // At the object's own size, the words broken where its own width breaks
        // them, and all of it then scaled: a parent at 0.7 draws smaller words in
        // the same lines, not the same words in a narrower box.
        int ow = (int)Math.Round(own.Width), oh = (int)Math.Round(own.Height);
        if (ow <= 0 || oh <= 0) return;
        double size = text.AutoSize
            ? UiTextBlock.FitSize(set.Font, shown, style, ow, oh, text.Wrap, text.AutoSizeMin, text.AutoSizeMax,
                                  text.LineSpacing, text.CharacterSpacing, fallbacks)
            : text.Size;

        var layout = UiTextBlock.Measure(set.Font, shown, style, size,
                                         text.Wrap ? ow : 0, Align(text.Alignment),
                                         text.LineSpacing, text.CharacterSpacing,
                                         fallbacks);
        foreach (int missing in layout.Missing)
            report.MissingGlyph($"'{(char)missing}' in {text.Font}");
        layout = UiTextBlock.Scaled(layout, scale.ScaleX, scale.ScaleY);

        var (laid, left, above, tw, th) = UiTextBlock.Fit(layout, set.Font, dw, dh, text.Wrap, text.Alignment);
        var pixels = UiTextBlock.Draw(laid, set, assets, tw, th, colour, style);

        // No shadow or outline: those work on a mesh uGUI builds, and TextMeshPro
        // builds its own. The game draws none on text, whatever the object says.
        Place(target, w, h, pixels, tw, th,
              new UiRect(screen.X - left, screen.Y - above, tw, th), node, clip, alpha,
              canvasWidth, canvasHeight, effects: false);
        report.Drawn++;
    }

    private static void Place(byte[] target, int w, int h, byte[] pixels, int dw, int dh,
                              UiRect screen, UiNodeDef node, UiRect? clip, double alpha,
                              double canvasWidth, double canvasHeight, bool effects = true, UiXform? scale = null)
    {
        if (clip is { } c)
            Clip(pixels, dw, dh, screen, UiLayout.ToScreen(c, canvasWidth, canvasHeight));

        double kx = scale?.ScaleX ?? 1, ky = scale?.ScaleY ?? 1;
        UiEffects.Draw(target, w, h, pixels, dw, dh,
                       (int)Math.Round(screen.X), (int)Math.Round(screen.Y),
                       effects ? ToEffect(node.Shadow, kx, ky) : null, effects ? ToEffect(node.Outline, kx, ky) : null, alpha);
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

    /// <summary>A shadow's reach scales with its object, being part of its mesh.</summary>
    private static UiEffect? ToEffect(UiEffectDef? e, double kx = 1, double ky = 1)
        => e == null ? null
           : new UiEffect(UiColor.Parse(e.Color),
                          (e.Distance.Length > 0 ? e.Distance[0] : 0) * kx,
                          (e.Distance.Length > 1 ? e.Distance[1] : 0) * ky);

    private static UiRect Intersect(UiRect? outer, UiRect inner)
    {
        if (outer is not { } o) return inner;
        double x = Math.Max(o.X, inner.X), y = Math.Max(o.Y, inner.Y);
        double right = Math.Min(o.Right, inner.Right), top = Math.Min(o.Top, inner.Top);
        return new UiRect(x, y, Math.Max(0, right - x), Math.Max(0, top - y));
    }

    private static double At(float[]? pair, int i)
        => pair != null && pair.Length > i ? pair[i] : 0.5;

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
