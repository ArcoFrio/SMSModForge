using System;

namespace SMSModForge.Rendering;

/// <summary>A colour as the pack stores it: straight (not premultiplied) BGRA.</summary>
public readonly record struct UiColor(byte B, byte G, byte R, byte A)
{
    public static readonly UiColor White = new(255, 255, 255, 255);

    public bool IsOpaqueWhite => B == 255 && G == 255 && R == 255 && A == 255;
    public bool IsInvisible => A == 0;

    /// <summary>Parse "#RRGGBB" or "#RRGGBBAA". Anything unreadable comes back
    /// as opaque white, which draws the sprite's own colours — the same thing a
    /// missing tint means, and visibly wrong rather than invisible.</summary>
    public static UiColor Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return White;
        string s = hex.Trim().TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return White;
        try
        {
            byte r = Convert.ToByte(s.Substring(0, 2), 16);
            byte g = Convert.ToByte(s.Substring(2, 2), 16);
            byte b = Convert.ToByte(s.Substring(4, 2), 16);
            byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
            return new UiColor(b, g, r, a);
        }
        catch { return White; }
    }
}

/// <summary>
/// Putting premultiplied images on top of one another.
/// <para/>
/// Premultiplied throughout, matching <see cref="JiggleShader"/> and
/// <see cref="UiSlice"/>. It is not a stylistic choice: blending a soft edge in
/// straight alpha pulls the colour toward black as it fades, which shows up as
/// a grey halo around every rounded corner and every letter — exactly the
/// places this preview is meant to be trusted on.
/// </summary>
public static class UiCompositor
{
    /// <summary>A transparent buffer of the given size.</summary>
    public static byte[] NewLayer(int width, int height)
        => new byte[Math.Max(0, width) * Math.Max(0, height) * 4];

    /// <summary>
    /// Source-over: draw <paramref name="source"/> onto <paramref name="target"/>
    /// with its top-left at (<paramref name="left"/>, <paramref name="top"/>).
    /// <para/>
    /// Anything falling outside the target is clipped rather than wrapped — the
    /// offsets that reach here include shadow distances, and a shadow that
    /// wrapped to the far edge would look like a rendering bug in the pack
    /// rather than in the preview.
    /// </summary>
    /// <param name="alpha">Extra opacity, 0..1, for a CanvasGroup.</param>
    /// <remarks>
    /// In linear light, because the game renders in linear colour space: a
    /// see-through colour darkens far less than its alpha says. Measured from
    /// the game: black at 0.84 over white gives 112, where blending the stored
    /// values gives 41. Every shadow, every tint with alpha and every soft edge
    /// goes through here, so getting this wrong made all of them too dark.
    /// </remarks>
    public static void Blend(byte[] target, int targetWidth, int targetHeight,
                             byte[] source, int sourceWidth, int sourceHeight,
                             int left, int top, double alpha = 1.0)
    {
        if (target == null || source == null) return;
        if (targetWidth <= 0 || targetHeight <= 0) return;
        if (sourceWidth <= 0 || sourceHeight <= 0) return;
        if (alpha <= 0) return;
        if (alpha > 1) alpha = 1;

        int x0 = Math.Max(0, left), y0 = Math.Max(0, top);
        int x1 = Math.Min(targetWidth, left + sourceWidth);
        int y1 = Math.Min(targetHeight, top + sourceHeight);

        for (int y = y0; y < y1; y++)
        {
            int sourceRow = (y - top) * sourceWidth;
            int targetRow = y * targetWidth;
            for (int x = x0; x < x1; x++)
            {
                int s = (sourceRow + (x - left)) * 4;
                int t = (targetRow + x) * 4;

                int stored = source[s + 3];
                if (stored == 0) continue;
                double sa = stored * alpha / 255.0;
                if (sa <= 0) continue;

                // Solid over anything, or anything over nothing: no mixing, and
                // between them most of a screen's pixels.
                int da = target[t + 3];
                if (sa >= 1.0)
                {
                    target[t] = source[s]; target[t + 1] = source[s + 1];
                    target[t + 2] = source[s + 2]; target[t + 3] = 255;
                    continue;
                }
                if (da == 0)
                {
                    for (int ch = 0; ch < 3; ch++) target[t + ch] = Round(source[s + ch] * alpha);
                    target[t + 3] = Round(stored * alpha);
                    continue;
                }

                double behind = da / 255.0 * (1.0 - sa);
                double total = sa + behind;
                for (int ch = 0; ch < 3; ch++)
                {
                    double over = ToLinear[Unmultiplied(source[s + ch], stored)];
                    double under = ToLinear[Unmultiplied(target[t + ch], da)];
                    double mixed = (over * sa + under * behind) / total;
                    target[t + ch] = Round(FromLinear(mixed) * total);
                }
                target[t + 3] = Round(total * 255.0);
            }
        }
    }

    /// <summary>The colour behind a premultiplied value.</summary>
    private static int Unmultiplied(int value, int alpha)
        => alpha >= 255 ? value : Math.Min(255, (value * 255 + alpha / 2) / alpha);

    /// <summary>A stored value's light, 0..1, by Unity's sRGB curve.</summary>
    private static readonly double[] ToLinear = MakeToLinear();

    private static double[] MakeToLinear()
    {
        var made = new double[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            made[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return made;
    }

    /// <summary>A light back to a stored value, 0..255.</summary>
    private static double FromLinear(double light)
    {
        if (light <= 0) return 0;
        if (light >= 1) return 255;
        double c = light <= 0.0031308 ? light * 12.92 : 1.055 * Math.Pow(light, 1 / 2.4) - 0.055;
        return c * 255.0;
    }

    /// <summary>
    /// Draw <paramref name="layer"/> onto <paramref name="target"/> turned about
    /// a point, both buffers the same size - how an object and everything inside
    /// it is turned. Drawn level into a layer of their own first, because every
    /// other thing the renderers do assumes a rectangle is a rectangle.
    /// </summary>
    /// <param name="degrees">Unity's rotation: positive is anticlockwise.</param>
    /// <param name="clip">Anything outside this is left out, in drawing
    /// coordinates - the masks of what the object sits inside.</param>
    public static void BlendTurned(byte[] target, byte[] layer, int width, int height,
                                   double pivotX, double pivotY, double degrees, UiRect? clip = null)
    {
        // The part of the layer with anything on it, so a small object costs a
        // small object's worth of work.
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (layer[(y * width + x) * 4 + 3] != 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return;

        double radians = degrees * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);

        // Where those corners land. Screen y grows downwards, so anticlockwise
        // on screen sends a point to the right of the pivot upwards.
        double loX = double.MaxValue, loY = double.MaxValue, hiX = double.MinValue, hiY = double.MinValue;
        foreach (var (cx, cy) in new[] { (minX, minY), (maxX + 1, minY), (minX, maxY + 1), (maxX + 1, maxY + 1) })
        {
            double dx = cx - pivotX, dy = cy - pivotY;
            double tx = pivotX + dx * cos + dy * sin, ty = pivotY - dx * sin + dy * cos;
            loX = Math.Min(loX, tx); hiX = Math.Max(hiX, tx);
            loY = Math.Min(loY, ty); hiY = Math.Max(hiY, ty);
        }

        int left = Math.Max(0, (int)Math.Floor(loX) - 1), top = Math.Max(0, (int)Math.Floor(loY) - 1);
        int right = Math.Min(width, (int)Math.Ceiling(hiX) + 1), bottom = Math.Min(height, (int)Math.Ceiling(hiY) + 1);
        int bw = right - left, bh = bottom - top;
        if (bw <= 0 || bh <= 0) return;

        var turned = new byte[bw * bh * 4];
        for (int y = 0; y < bh; y++)
        {
            double oy = top + y + 0.5 - pivotY;
            if (clip is { } c && (top + y + 0.5 < c.Y || top + y + 0.5 >= c.Y + c.Height)) continue;
            for (int x = 0; x < bw; x++)
            {
                if (clip is { } k && (left + x + 0.5 < k.X || left + x + 0.5 >= k.X + k.Width)) continue;
                double ox = left + x + 0.5 - pivotX;

                // Back along the turn: which pixel of the level layer lands here.
                double fx = pivotX + ox * cos - oy * sin - 0.5;
                double fy = pivotY + ox * sin + oy * cos - 0.5;
                int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
                if (x0 < minX - 1 || y0 < minY - 1 || x0 > maxX || y0 > maxY) continue;
                double wx = fx - x0, wy = fy - y0;

                int o = (y * bw + x) * 4;
                for (int ch = 0; ch < 4; ch++)
                {
                    double a = Read(layer, width, height, x0, y0, ch), b = Read(layer, width, height, x0 + 1, y0, ch);
                    double d = Read(layer, width, height, x0, y0 + 1, ch), e = Read(layer, width, height, x0 + 1, y0 + 1, ch);
                    double upper = a + (b - a) * wx, lower = d + (e - d) * wx;
                    turned[o + ch] = Round(upper + (lower - upper) * wy);
                }
            }
        }
        Blend(target, width, height, turned, bw, bh, left, top);
    }

    private static double Read(byte[] layer, int width, int height, int x, int y, int channel)
        => x < 0 || y < 0 || x >= width || y >= height ? 0 : layer[(y * width + x) * 4 + channel];

    /// <summary>
    /// A copy of a shape in a single flat colour, keeping only its silhouette.
    /// <para/>
    /// What a uGUI Shadow or Outline actually is: the same mesh drawn again in
    /// one colour, offset. So the copy takes its alpha from the original and
    /// nothing else.
    /// </summary>
    /// <param name="useGraphicAlpha">When true the copy fades where the
    /// original does, which is the default and what keeps a shadow from
    /// appearing under a half-transparent icon at full strength.</param>
    public static byte[] Silhouette(byte[] source, UiColor color, bool useGraphicAlpha = true)
    {
        var made = new byte[source.Length];
        double cb = color.B / 255.0, cg = color.G / 255.0, cr = color.R / 255.0;
        double ca = color.A / 255.0;

        for (int i = 0; i + 3 < source.Length; i += 4)
        {
            double a = useGraphicAlpha ? source[i + 3] * ca : (source[i + 3] > 0 ? 255 * ca : 0);
            if (a <= 0) continue;
            made[i] = Round(a * cb);
            made[i + 1] = Round(a * cg);
            made[i + 2] = Round(a * cr);
            made[i + 3] = Round(a);
        }
        return made;
    }

    private static byte Round(double v)
        => (byte)(v < 0 ? 0 : v > 255 ? 255 : v + 0.5);
}

/// <summary>A uGUI Shadow or Outline: a colour and how far it is offset.</summary>
public readonly record struct UiEffect(UiColor Color, double OffsetX, double OffsetY,
                                       bool UseGraphicAlpha = true);

/// <summary>
/// Shadows and outlines, which are the same mechanism used twice.
/// <para/>
/// Both matter more than they sound. The vanilla UI carries 667 shadows and 323
/// outlines, so a preview that skipped them would be wrong on roughly a
/// thousand objects — and wrong in a way that reads as "the art is flat" rather
/// than as a missing feature.
/// <para/>
/// A shadow is one offset copy behind the graphic. An outline is four, at every
/// combination of ±offset, which is why an outline's setting is a distance
/// rather than a width and why a large one looks like four corners rather than
/// a ring. That is uGUI's own construction, not an approximation of it.
/// </summary>
public static class UiEffects
{
    /// <summary>
    /// Draw <paramref name="graphic"/> onto <paramref name="target"/> with its
    /// shadow and outline beneath it, in uGUI's order: outline first, then
    /// shadow, then the graphic itself.
    /// </summary>
    public static void Draw(byte[] target, int targetWidth, int targetHeight,
                            byte[] graphic, int graphicWidth, int graphicHeight,
                            int left, int top,
                            UiEffect? shadow = null, UiEffect? outline = null,
                            double alpha = 1.0)
    {
        if (outline is { } o)
        {
            var copy = UiCompositor.Silhouette(graphic, o.Color, o.UseGraphicAlpha);
            // Four corners. Unity offsets by ±distance on both axes rather than
            // stroking, so the shape thickens diagonally.
            foreach (var (dx, dy) in new[]
                     {
                         (o.OffsetX, o.OffsetY), (-o.OffsetX, o.OffsetY),
                         (o.OffsetX, -o.OffsetY), (-o.OffsetX, -o.OffsetY),
                     })
            {
                UiCompositor.Blend(target, targetWidth, targetHeight,
                                   copy, graphicWidth, graphicHeight,
                                   left + (int)Math.Round(dx),
                                   // Screen y grows downwards while Unity's
                                   // effect distance is written in UI space, so
                                   // a positive offset means UP.
                                   top - (int)Math.Round(dy), alpha);
            }
        }

        if (shadow is { } s)
        {
            var copy = UiCompositor.Silhouette(graphic, s.Color, s.UseGraphicAlpha);
            UiCompositor.Blend(target, targetWidth, targetHeight,
                               copy, graphicWidth, graphicHeight,
                               left + (int)Math.Round(s.OffsetX),
                               top - (int)Math.Round(s.OffsetY), alpha);
        }

        UiCompositor.Blend(target, targetWidth, targetHeight,
                           graphic, graphicWidth, graphicHeight, left, top, alpha);
    }
}
