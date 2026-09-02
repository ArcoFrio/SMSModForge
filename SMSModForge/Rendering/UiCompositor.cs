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

                double sa = source[s + 3] * alpha;
                if (sa <= 0) continue;

                double inverse = 1.0 - sa / 255.0;
                for (int ch = 0; ch < 3; ch++)
                    target[t + ch] = Round(source[s + ch] * alpha + target[t + ch] * inverse);
                target[t + 3] = Round(sa + target[t + 3] * inverse);
            }
        }
    }

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
