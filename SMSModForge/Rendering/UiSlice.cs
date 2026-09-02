using System;

namespace SMSModForge.Rendering;

/// <summary>
/// A sprite's nine-slice border, in pixels: how far in from each edge the
/// stretchable middle begins. Unity's order is left, bottom, right, top.
/// </summary>
public readonly record struct SliceBorder(double Left, double Bottom, double Right, double Top)
{
    public bool IsEmpty => Left <= 0 && Bottom <= 0 && Right <= 0 && Top <= 0;

    public override string ToString() => $"[{Left:0.##},{Bottom:0.##},{Right:0.##},{Top:0.##}]";
}

/// <summary>
/// Nine-slicing: drawing one small sprite at any size without deforming its
/// corners. It is how this game's interface is built — 2556 of its images are
/// sliced, and they come from just 51 distinct sprites, most of them a couple
/// of kilobytes and tinted at use.
/// <para/>
/// <b>Why this composes one bitmap instead of drawing nine.</b> The obvious
/// approach — nine cropped images in a grid — was tried here before and
/// abandoned. It sliced correctly and still looked wrong: hairline seams
/// appeared between the pieces, because each was stretched and filtered to its
/// own crop edge with nothing beyond it to sample, so the joins faded wherever
/// a cell boundary landed off a whole pixel. Arbitrary zoom guarantees that.
/// The fix is to decide every destination pixel here, once, and hand the
/// renderer a single continuous image to scale.
/// <para/>
/// Pixels are premultiplied BGRA, the format a WriteableBitmap wants and the
/// one <see cref="JiggleShader"/> already uses. Premultiplied matters for more
/// than convention: blending a transparent edge in straight alpha darkens it
/// toward black, which shows up as a grey fringe around every rounded corner.
/// </summary>
public static class UiSlice
{
    /// <summary>
    /// The border as it applies to a rectangle of this size.
    /// <para/>
    /// Two adjustments, both of which the vanilla UI leans on heavily:
    /// <list type="number">
    ///   <item><b>Scale.</b> The stored border is in sprite pixels, and a
    ///   sprite does not necessarily draw one texture pixel per canvas pixel.
    ///   Dividing by <paramref name="divisor"/> — the sprite's pixels-per-unit
    ///   over the canvas's, times the image's own multiplier — converts it.
    ///   About 390 images in the game use a multiplier other than 1, from 0.5
    ///   to 5, so this is not a formality.</item>
    ///   <item><b>Shrink.</b> When the borders no longer fit, they are scaled
    ///   down together to exactly fill the rectangle. This is the case that
    ///   looks like an edge case and is not: 1112 of the game's 2556 sliced
    ///   images are smaller than their own border sum on at least one axis.
    ///   The clearest example is a 256×256 "Rounded" sprite bordered 128 on
    ///   every side — the whole sprite is corner — drawn at 85×85 as a circular
    ///   close button. Without shrinking that is not a button, it is
    ///   wreckage.</item>
    /// </list>
    /// Unity does the same two steps in the same order, in
    /// <c>Image.GetAdjustedBorders</c>.
    /// </summary>
    public static SliceBorder Adjust(SliceBorder border, double width, double height,
                                     double divisor = 1.0)
    {
        if (divisor <= 0) divisor = 1.0;
        double left = border.Left / divisor, right = border.Right / divisor;
        double bottom = border.Bottom / divisor, top = border.Top / divisor;

        double across = left + right;
        if (across > width && across > 0)
        {
            double k = Math.Max(0, width) / across;
            left *= k;
            right *= k;
        }

        double up = bottom + top;
        if (up > height && up > 0)
        {
            double k = Math.Max(0, height) / up;
            bottom *= k;
            top *= k;
        }

        return new SliceBorder(left, bottom, right, top);
    }

    /// <summary>The divisor for <see cref="Adjust"/>: how many sprite pixels go
    /// into one canvas pixel.</summary>
    public static double DivisorFor(double spritePixelsPerUnit,
                                    double canvasReferencePixelsPerUnit,
                                    double pixelsPerUnitMultiplier)
    {
        if (canvasReferencePixelsPerUnit <= 0) canvasReferencePixelsPerUnit = 100;
        if (spritePixelsPerUnit <= 0) spritePixelsPerUnit = 100;
        if (pixelsPerUnitMultiplier <= 0) pixelsPerUnitMultiplier = 1;
        return spritePixelsPerUnit / canvasReferencePixelsPerUnit * pixelsPerUnitMultiplier;
    }

    /// <summary>
    /// Draw <paramref name="source"/> at <paramref name="targetWidth"/> ×
    /// <paramref name="targetHeight"/>, keeping its corners and stretching only
    /// what is between them.
    /// <para/>
    /// <paramref name="border"/> is the sprite's own, in source pixels; the
    /// destination border is derived from it by <see cref="Adjust"/>. An empty
    /// border degenerates to a plain stretch, which is what an unsliced image
    /// is, so callers do not need a separate path for Simple.
    /// <para/>
    /// Not handled: preserveAspect, which applies to 10 of the game's 2559
    /// sliced images. Callers that need it should letterbox the target
    /// rectangle before calling, rather than have this quietly do something
    /// approximate.
    /// </summary>
    /// <param name="source">Premultiplied BGRA, top row first.</param>
    /// <param name="fillCenter">False draws the frame and leaves the middle
    /// transparent.</param>
    public static byte[] Compose(byte[] source, int sourceWidth, int sourceHeight,
                                 SliceBorder border, int targetWidth, int targetHeight,
                                 bool fillCenter = true, double divisor = 1.0)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentException("the source has no pixels");
        if (source.Length < sourceWidth * sourceHeight * 4)
            throw new ArgumentException("the source is smaller than its stated size");
        if (targetWidth <= 0 || targetHeight <= 0) return Array.Empty<byte>();

        var dest = new byte[targetWidth * targetHeight * 4];
        var fitted = Adjust(border, targetWidth, targetHeight, divisor);

        // Column and row boundaries. The source uses the sprite's own border,
        // the destination the adjusted one — that difference IS the slice: a
        // corner of the texture maps onto a corner of the target, whatever
        // sizes the two happen to be.
        double[] sx = Bounds(0, border.Left, sourceWidth - border.Right, sourceWidth);
        double[] sy = Bounds(0, border.Top, sourceHeight - border.Bottom, sourceHeight);
        double[] dx = Bounds(0, fitted.Left, targetWidth - fitted.Right, targetWidth);
        double[] dy = Bounds(0, fitted.Top, targetHeight - fitted.Bottom, targetHeight);

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                if (!fillCenter && row == 1 && col == 1) continue;
                Stretch(source, sourceWidth, sourceHeight,
                        sx[col], sy[row], sx[col + 1], sy[row + 1],
                        dest, targetWidth,
                        dx[col], dy[row], dx[col + 1], dy[row + 1]);
            }
        }
        return dest;
    }

    /// <summary>Three cuts into four ordered boundaries, kept monotonic so a
    /// border wider than its own sprite cannot produce a negative-width cell.</summary>
    private static double[] Bounds(double a, double b, double c, double d)
    {
        if (b < a) b = a;
        if (c < b) c = b;
        if (d < c) d = c;
        return new[] { a, b, c, d };
    }

    /// <summary>One of the nine cells: sample a source rectangle across a
    /// destination rectangle, bilinearly, clamped to the cell.
    /// <para/>
    /// The clamp is to the CELL rather than to the whole image on purpose. It
    /// is what stops a stretched edge from dragging in a pixel of the corner
    /// beside it, which is the difference between a clean border and one that
    /// smears at the joins.</summary>
    private static void Stretch(byte[] source, int sourceWidth, int sourceHeight,
                                double sx0, double sy0, double sx1, double sy1,
                                byte[] dest, int destWidth,
                                double dx0, double dy0, double dx1, double dy1)
    {
        int px0 = (int)Math.Round(dx0), px1 = (int)Math.Round(dx1);
        int py0 = (int)Math.Round(dy0), py1 = (int)Math.Round(dy1);
        if (px1 <= px0 || py1 <= py0) return;

        double sw = sx1 - sx0, sh = sy1 - sy0;

        // A source cell can be zero-wide or zero-tall while its destination is
        // not, and it is NOT nothing to draw. A sprite whose border consumes
        // the whole sprite - "Rounded" is 256x256 bordered 128 on every side -
        // has no middle column or row at all, and the game still draws it as a
        // filled shape at any size. That is because sampling a zero-extent UV
        // strip on a GPU returns the texel at that coordinate and stretches it;
        // returning early here instead drew four disconnected corner arcs with
        // nothing between them.
        bool flatX = sw <= 0, flatY = sh <= 0;

        double spanX = px1 - px0, spanY = py1 - py0;

        // A flat cell has no interior to clamp within, so it clamps to the
        // whole sprite - which is what lets it read the boundary texels either
        // side of the cut, exactly as bilinear filtering would.
        int loX = flatX ? 0 : (int)sx0, hiX = flatX ? sourceWidth - 1 : (int)Math.Ceiling(sx1) - 1;
        int loY = flatY ? 0 : (int)sy0, hiY = flatY ? sourceHeight - 1 : (int)Math.Ceiling(sy1) - 1;

        for (int y = py0; y < py1; y++)
        {
            double v = (y - py0 + 0.5) / spanY;
            double fy = (flatY ? sy0 : sy0 + v * sh) - 0.5;
            int y0 = (int)Math.Floor(fy);
            double wy = fy - y0;
            int y0c = Clamp(y0, loY, hiY, sourceHeight);
            int y1c = Clamp(y0 + 1, loY, hiY, sourceHeight);

            for (int x = px0; x < px1; x++)
            {
                double u = (x - px0 + 0.5) / spanX;
                double fx = (flatX ? sx0 : sx0 + u * sw) - 0.5;
                int x0 = (int)Math.Floor(fx);
                double wx = fx - x0;
                int x0c = Clamp(x0, loX, hiX, sourceWidth);
                int x1c = Clamp(x0 + 1, loX, hiX, sourceWidth);

                int a = (y0c * sourceWidth + x0c) * 4;
                int b = (y0c * sourceWidth + x1c) * 4;
                int c = (y1c * sourceWidth + x0c) * 4;
                int d = (y1c * sourceWidth + x1c) * 4;
                int o = (y * destWidth + x) * 4;

                for (int ch = 0; ch < 4; ch++)
                {
                    double top = source[a + ch] + (source[b + ch] - source[a + ch]) * wx;
                    double bottom = source[c + ch] + (source[d + ch] - source[c + ch]) * wx;
                    double value = top + (bottom - top) * wy;
                    dest[o + ch] = (byte)(value < 0 ? 0 : value > 255 ? 255 : value + 0.5);
                }
            }
        }
    }

    private static int Clamp(int v, int low, int high, int limit)
    {
        if (low < 0) low = 0;
        if (high >= limit) high = limit - 1;
        if (high < low) high = low;
        return v < low ? low : v > high ? high : v;
    }

    /// <summary>
    /// Multiply a composed image by a tint, the way the game does.
    /// <para/>
    /// This is not decoration. 1521 of the game's sliced images are a plain
    /// white sprite recoloured here, which is exactly why 51 sprites can dress
    /// an entire interface. In premultiplied pixels every channel including
    /// alpha scales, so a tint with alpha fades the image as well as colouring
    /// it — which is what the game means by a tint of #FFFFFF00.
    /// </summary>
    public static void Tint(byte[] pixels, byte b, byte g, byte r, byte a)
    {
        if (pixels == null) return;
        if (b == 255 && g == 255 && r == 255 && a == 255) return;   // nothing to do

        double fb = b / 255.0, fg = g / 255.0, fr = r / 255.0, fa = a / 255.0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            pixels[i] = (byte)(pixels[i] * fb * fa + 0.5);
            pixels[i + 1] = (byte)(pixels[i + 1] * fg * fa + 0.5);
            pixels[i + 2] = (byte)(pixels[i + 2] * fr * fa + 0.5);
            pixels[i + 3] = (byte)(pixels[i + 3] * fa + 0.5);
        }
    }
}
