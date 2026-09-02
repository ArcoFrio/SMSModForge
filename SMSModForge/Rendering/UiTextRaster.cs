using System;

namespace SMSModForge.Rendering;

/// <summary>
/// Drawing a laid-out string from its font's atlas.
/// <para/>
/// Each glyph is a rectangle of the atlas stretched onto a rectangle of the
/// target. For a distance-field font the sampled value is not ink but a
/// distance, and <see cref="TmpFont.Coverage"/> turns it into one — which is
/// the whole reason a 1024×1024 atlas can draw text at any size without going
/// soft. For a bitmap atlas the value already is ink and passes straight
/// through; seven of the game's fonts are like that, and thresholding one would
/// wreck it.
/// <para/>
/// The padding matters and is easy to miss. A glyph's rect in the atlas is its
/// tight box, and the field that antialiases its edge lives in the texels
/// AROUND that box. Sampling only the rect clips the edge off and leaves text
/// that looks bitten; so both the source rectangle and the destination
/// rectangle are grown by the padding before sampling, which is what
/// TextMeshPro does when it builds a glyph quad.
/// </summary>
public static class UiTextRaster
{
    /// <summary>
    /// Draw <paramref name="layout"/> into a new premultiplied BGRA buffer of
    /// the given size.
    /// </summary>
    /// <param name="atlasAlpha">One byte per texel — the atlas's alpha channel,
    /// which is where TextMeshPro keeps everything. Its RGB is zero.</param>
    public static byte[] Draw(TextLayout layout, TmpFont font,
                              byte[] atlasAlpha, int atlasWidth, int atlasHeight,
                              int width, int height, UiColor color)
    {
        var target = UiCompositor.NewLayer(width, height);
        DrawInto(target, width, height, layout, font,
                 atlasAlpha, atlasWidth, atlasHeight, color);
        return target;
    }

    /// <summary>As <see cref="Draw"/>, onto a buffer that already exists.</summary>
    public static void DrawInto(byte[] target, int width, int height,
                                TextLayout layout, TmpFont font,
                                byte[] atlasAlpha, int atlasWidth, int atlasHeight,
                                UiColor color)
    {
        if (target == null || layout == null || font == null || atlasAlpha == null) return;
        if (width <= 0 || height <= 0 || atlasWidth <= 0 || atlasHeight <= 0) return;
        if (atlasAlpha.Length < atlasWidth * atlasHeight) return;
        if (color.IsInvisible) return;

        double padding = font.Atlas.Padding;
        double cb = color.B / 255.0, cg = color.G / 255.0;
        double cr = color.R / 255.0, ca = color.A / 255.0;

        foreach (var placed in layout.Glyphs)
        {
            var glyph = placed.Glyph;
            var metrics = glyph.Metrics;
            if (metrics.Width <= 0 || metrics.Height <= 0) continue;   // a space
            var rect = glyph.RectTopLeft;
            if (rect.Length < 4 || rect[2] <= 0 || rect[3] <= 0) continue;

            // How much the glyph was scaled to reach its placed size. Derived
            // rather than passed, so a caller cannot hand in one that disagrees
            // with the layout it also handed in.
            double scale = placed.Width / metrics.Width;
            if (scale <= 0 || double.IsNaN(scale)) continue;

            // Grown by the padding on both sides, in both spaces.
            double sx = rect[0] - padding, sy = rect[1] - padding;
            double sw = rect[2] + padding * 2, sh = rect[3] + padding * 2;
            double dx = placed.X - padding * scale, dy = placed.Y - padding * scale;
            double dw = placed.Width + padding * 2 * scale;
            double dh = placed.Height + padding * 2 * scale;
            if (dw <= 0 || dh <= 0) continue;

            // Destination pixels per atlas texel: what decides how sharply the
            // distance field resolves its edge.
            double pixelsPerTexel = dw / sw;

            int px0 = Math.Max(0, (int)Math.Floor(dx));
            int py0 = Math.Max(0, (int)Math.Floor(dy));
            int px1 = Math.Min(width, (int)Math.Ceiling(dx + dw));
            int py1 = Math.Min(height, (int)Math.Ceiling(dy + dh));

            for (int y = py0; y < py1; y++)
            {
                double v = (y + 0.5 - dy) / dh;
                double fy = sy + v * sh - 0.5;

                for (int x = px0; x < px1; x++)
                {
                    double u = (x + 0.5 - dx) / dw;
                    double fx = sx + u * sw - 0.5;

                    double alpha = Sample(atlasAlpha, atlasWidth, atlasHeight, fx, fy);
                    double ink = font.Coverage(alpha, pixelsPerTexel);
                    if (ink <= 0) continue;

                    double a = ink * ca;
                    if (a <= 0) continue;

                    int t = (y * width + x) * 4;
                    double inverse = 1.0 - a;
                    target[t] = Round(a * cb * 255 + target[t] * inverse);
                    target[t + 1] = Round(a * cg * 255 + target[t + 1] * inverse);
                    target[t + 2] = Round(a * cr * 255 + target[t + 2] * inverse);
                    target[t + 3] = Round(a * 255 + target[t + 3] * inverse);
                }
            }
        }
    }

    /// <summary>Bilinear sample of the atlas, 0..1, clamped at its edges.
    /// <para/>
    /// Clamping rather than wrapping: a glyph near the atlas boundary would
    /// otherwise pick up whatever was baked on the far side, and the artefact
    /// is a stray mark next to one letter, which reads as a font problem.</summary>
    private static double Sample(byte[] atlas, int width, int height, double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double wx = x - x0, wy = y - y0;

        int x0c = Clamp(x0, width), x1c = Clamp(x0 + 1, width);
        int y0c = Clamp(y0, height), y1c = Clamp(y0 + 1, height);

        double a = atlas[y0c * width + x0c], b = atlas[y0c * width + x1c];
        double c = atlas[y1c * width + x0c], d = atlas[y1c * width + x1c];

        double top = a + (b - a) * wx;
        double bottom = c + (d - c) * wx;
        return (top + (bottom - top) * wy) / 255.0;
    }

    private static int Clamp(int v, int limit) => v < 0 ? 0 : v >= limit ? limit - 1 : v;

    private static byte Round(double v)
        => (byte)(v < 0 ? 0 : v > 255 ? 255 : v + 0.5);
}
