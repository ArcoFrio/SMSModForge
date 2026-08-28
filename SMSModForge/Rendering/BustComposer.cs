using System;
using System.IO;
using System.Windows.Media.Imaging;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// Loads PNGs off disk into BGRA32 byte buffers at <see cref="JiggleShader.Size"/>×<see cref="JiggleShader.Size"/>,
/// and composites overlay layers (blink, expression, mouth frames) on top of
/// a jiggle-distorted base.
/// </summary>
public static class BustComposer
{
    public static byte[] LoadPng(string absPath)
    {
        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
            return new byte[JiggleShader.Stride * JiggleShader.Size];

        using var stream = File.OpenRead(absPath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        // Force to 256×256 BGRA32.
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        if (converted.PixelWidth == JiggleShader.Size && converted.PixelHeight == JiggleShader.Size)
        {
            var buf = new byte[JiggleShader.Stride * JiggleShader.Size];
            converted.CopyPixels(buf, JiggleShader.Stride, 0);
            return buf;
        }

        // Point-sampled, not TransformedBitmap. The shipped vanilla art is a
        // half-scale copy, so this path now runs for every borrowed bust rather
        // than almost never, and WPF's own filtering turns a 128px bust into a
        // soft 256px one. Nearest-neighbour keeps the pixels honest: visibly
        // chunky rather than smeared, which reads as low resolution instead of
        // as bad art. It also matches the composite pass below, which already
        // point-filters its overlays for the same reason.
        //
        // UNIFORM, and centred. This used to map the source's width onto 256 and
        // its height onto 256 independently, which stretches anything that is not
        // square — so a 512x256 bust looked square here and does not in game,
        // where the runtime fits it with one pixels-per-unit for both axes and
        // centres it on the same pivot. One scale for both axes, and transparent
        // padding around the short side, is what the game does; the preview is
        // only worth having if it agrees with it.
        int sw = converted.PixelWidth, sh = converted.PixelHeight;
        int srcStride = sw * 4;
        var src = new byte[srcStride * sh];
        converted.CopyPixels(src, srcStride, 0);

        var outBuf = new byte[JiggleShader.Stride * JiggleShader.Size];

        // The larger ratio wins, so the art fits INSIDE the frame rather than
        // overflowing it — matching FittedSprite in the plugin.
        double scale = ArtFit.BustScale(sw, sh);
        if (scale <= 0) return outBuf;

        int dw = System.Math.Max(1, (int)System.Math.Round(sw / scale));
        int dh = System.Math.Max(1, (int)System.Math.Round(sh / scale));
        if (dw > JiggleShader.Size) dw = JiggleShader.Size;
        if (dh > JiggleShader.Size) dh = JiggleShader.Size;
        int ox = (JiggleShader.Size - dw) / 2;
        int oy = (JiggleShader.Size - dh) / 2;

        for (int y = 0; y < dh; y++)
        {
            int sy = (int)((long)y * sh / dh);
            if (sy >= sh) sy = sh - 1;
            int srcRow = sy * srcStride;
            int dstRow = (y + oy) * JiggleShader.Stride;
            for (int x = 0; x < dw; x++)
            {
                int sx = (int)((long)x * sw / dw);
                if (sx >= sw) sx = sw - 1;
                int si = srcRow + sx * 4;
                int di = dstRow + (x + ox) * 4;
                outBuf[di]     = src[si];
                outBuf[di + 1] = src[si + 1];
                outBuf[di + 2] = src[si + 2];
                outBuf[di + 3] = src[si + 3];
            }
        }
        return outBuf;
    }

    /// <summary>
    /// Standard "source over premultiplied" composite. <paramref name="destPremul"/>
    /// is sized for <see cref="JiggleShader.RenderSize"/>², <paramref name="overlayStraight"/>
    /// is sized for <see cref="JiggleShader.Size"/>² (source resolution). We NN-upscale
    /// the overlay during the composite pass — point-filtering matches the
    /// pixel-art look and keeps the overlay crisp at the denser output grid.
    /// Destination is mutated in place.
    /// </summary>
    /// <param name="offsetU">
    /// Constant UV displacement applied to the overlay's lookup, in exactly the
    /// sense <see cref="JiggleShader"/> uses: the sample coordinate moves by
    /// +offset, so the drawn image moves by -offset. Zero composites the overlay
    /// where it was authored.
    /// <para/>
    /// This is how the game moves bust overlays. They do NOT carry the jiggle
    /// shader — it would recolour them — so instead each is displaced rigidly by
    /// the jiggle evaluated once at its own centroid (see the plugin's
    /// <c>OverlayJiggle</c>). Feeding that same constant through the same
    /// sampling path the body uses is what makes the preview agree with the game
    /// rather than approximate it.
    /// </param>
    public static void Composite(byte[] destPremul, byte[] overlayStraight,
                                 float offsetU = 0f, float offsetV = 0f)
    {
        if (destPremul.Length != JiggleShader.RenderStride * JiggleShader.RenderSize)
            throw new ArgumentException("dest bad size", nameof(destPremul));
        if (overlayStraight.Length != JiggleShader.Stride * JiggleShader.Size)
            throw new ArgumentException("overlay bad size", nameof(overlayStraight));

        // Scale factor between the source grid and the render grid. Constant
        // (currently 2) so we can use integer division for the NN lookup with
        // no per-pixel float math.
        const int scale = JiggleShader.RenderSize / JiggleShader.Size;

        // Displacement stays POINT-sampled, on the output grid.
        //
        // Bilinear was tried here to smooth the bounce and is the wrong trade: at
        // a 2x upscale an output pixel maps to source y/scale, which never lands
        // on a texel centre, so every pixel becomes a blend of two texels even at
        // zero offset. That softened the art permanently, not just while moving.
        //
        // What remains is a real limit, not a bug: a typical Strength of 0.015
        // moves the sprite about 7.7 output pixels peak-to-peak, so a rigid
        // point-sampled translation has roughly 8 distinct positions per bounce
        // cycle. The BODY avoids this only because its displacement is per pixel
        // — its texel boundaries cross at different moments across the sprite —
        // and that is precisely what the overlays cannot have without the jiggle
        // shader, which recolours them. Vertical bounce shows the stepping worst,
        // being the largest term on most masks.
        //
        // v is bottom-up (Unity's convention, the one JiggleShader samples in)
        // while rows run top-down, hence the sign flip on the row term.
        float shiftX = offsetU * JiggleShader.RenderSize;
        float shiftY = -offsetV * JiggleShader.RenderSize;

        for (int y = 0; y < JiggleShader.RenderSize; y++)
        {
            // MathF.Floor, not integer division: the latter truncates toward
            // zero, folding a negative coordinate back onto row 0 instead of
            // letting it fall off the canvas.
            int srcY = (int)MathF.Floor((y + shiftY) / scale);
            if (srcY < 0 || srcY >= JiggleShader.Size) continue;
            int destRow = y * JiggleShader.RenderStride;
            int srcRow = srcY * JiggleShader.Stride;

            for (int x = 0; x < JiggleShader.RenderSize; x++)
            {
                int srcX = (int)MathF.Floor((x + shiftX) / scale);
                if (srcX < 0 || srcX >= JiggleShader.Size) continue;
                int oIdx = srcRow + srcX * 4;
                int dIdx = destRow + x * 4;

                float a = overlayStraight[oIdx + 3] / 255f;
                // Premultiply the overlay on the fly.
                float ob = overlayStraight[oIdx    ] / 255f * a;
                float og = overlayStraight[oIdx + 1] / 255f * a;
                float or = overlayStraight[oIdx + 2] / 255f * a;

                float db = destPremul[dIdx    ] / 255f;
                float dg = destPremul[dIdx + 1] / 255f;
                float dr = destPremul[dIdx + 2] / 255f;
                float da = destPremul[dIdx + 3] / 255f;

                float invA = 1.0f - a;
                destPremul[dIdx    ] = (byte)((ob + db * invA) * 255f);
                destPremul[dIdx + 1] = (byte)((og + dg * invA) * 255f);
                destPremul[dIdx + 2] = (byte)((or + dr * invA) * 255f);
                destPremul[dIdx + 3] = (byte)((a  + da * invA) * 255f);
            }
        }
    }

    /// <summary>
    /// Source-over of one straight-alpha overlay onto a straight-alpha buffer,
    /// both at <see cref="JiggleShader.Size"/>.
    /// <para/>
    /// <see cref="Composite"/> is the wrong tool for layering a bust: it writes
    /// PREMULTIPLIED output at render resolution, i.e. after the jiggle pass.
    /// The overlays have to go on before it, because in game they ride the
    /// body's jiggle material and are displaced with it.
    /// </summary>
    public static void CompositeStraight(byte[] destStraight, byte[] overlayStraight)
    {
        int n = JiggleShader.Stride * JiggleShader.Size;
        if (destStraight.Length != n || overlayStraight.Length != n) return;

        for (int i = 0; i < n; i += 4)
        {
            float a = overlayStraight[i + 3] / 255f;
            if (a <= 0f) continue;
            if (a >= 1f)
            {
                destStraight[i    ] = overlayStraight[i    ];
                destStraight[i + 1] = overlayStraight[i + 1];
                destStraight[i + 2] = overlayStraight[i + 2];
                destStraight[i + 3] = 255;
                continue;
            }

            float da = destStraight[i + 3] / 255f;
            float outA = a + da * (1f - a);
            if (outA <= 0f) { destStraight[i + 3] = 0; continue; }

            // Straight-alpha source-over: composite premultiplied, then divide
            // the accumulated alpha back out so the buffer stays straight —
            // the shader samples it as a plain texture.
            for (int c = 0; c < 3; c++)
            {
                float o = overlayStraight[i + c] / 255f * a;
                float d = destStraight[i + c] / 255f * da * (1f - a);
                destStraight[i + c] = (byte)System.Math.Min(255f, (o + d) / outA * 255f);
            }
            destStraight[i + 3] = (byte)(outA * 255f);
        }
    }

    /// <summary>
    /// Decodes a PNG to a BGRA32 buffer at its native resolution, capped so the
    /// longer side is at most <paramref name="maxSide"/> (aspect preserved).
    /// Used by the NPC preview so a 1024-px pose renders near its real detail
    /// instead of being squashed to 256² first. Returns a 1×1 transparent buffer
    /// on failure.
    /// </summary>
    public static (byte[] pixels, int w, int h) LoadPngNative(string absPath, int maxSide)
    {
        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
            return (new byte[4], 1, 1);

        using var stream = File.OpenRead(absPath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        int nw = frame.PixelWidth, nh = frame.PixelHeight;
        double scale = System.Math.Min(1.0, maxSide / (double)System.Math.Max(nw, nh));
        int w = System.Math.Max(1, (int)System.Math.Round(nw * scale));
        int h = System.Math.Max(1, (int)System.Math.Round(nh * scale));
        return (LoadInto(frame, w, h), w, h);
    }

    /// <summary>Decodes a PNG scaled to exactly <paramref name="w"/>×<paramref name="h"/>
    /// BGRA — used to load the mask / blink at the same grid as the base.</summary>
    public static byte[] LoadPngAt(string absPath, int w, int h)
    {
        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath))
            return new byte[w * h * 4];
        using var stream = File.OpenRead(absPath);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return LoadInto(decoder.Frames[0], w, h);
    }

    private static byte[] LoadInto(BitmapFrame frame, int w, int h)
    {
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        BitmapSource src = converted;
        if (converted.PixelWidth != w || converted.PixelHeight != h)
            src = new TransformedBitmap(converted, new System.Windows.Media.ScaleTransform(
                w / (double)converted.PixelWidth, h / (double)converted.PixelHeight));
        var buf = new byte[w * h * 4];
        src.CopyPixels(buf, w * 4, 0);
        return buf;
    }

    /// <summary>Same-size premultiplied source-over: composite a straight-alpha
    /// <paramref name="overlayStraight"/> onto <paramref name="destPremul"/> when
    /// both are the identical <paramref name="w"/>×<paramref name="h"/> grid (the
    /// NPC preview loads blink at the base's resolution, so no rescale is needed).</summary>
    public static void CompositeSame(byte[] destPremul, byte[] overlayStraight, int w, int h)
    {
        int n = w * h * 4;
        if (destPremul.Length != n || overlayStraight.Length != n) return;
        for (int i = 0; i < n; i += 4)
        {
            float a = overlayStraight[i + 3] / 255f;
            if (a <= 0f) continue;
            // Premultiply the overlay, then source-over. Everything stays in the
            // 0..1 domain and is scaled to bytes once at write time — mixing a
            // 0..1 premultiplied colour with a 0..255 dest term (the earlier
            // bug) crushed the overlay to black.
            float ob = overlayStraight[i    ] / 255f * a;
            float og = overlayStraight[i + 1] / 255f * a;
            float or = overlayStraight[i + 2] / 255f * a;
            float invA = 1f - a;
            destPremul[i    ] = (byte)((ob + destPremul[i    ] / 255f * invA) * 255f);
            destPremul[i + 1] = (byte)((og + destPremul[i + 1] / 255f * invA) * 255f);
            destPremul[i + 2] = (byte)((or + destPremul[i + 2] / 255f * invA) * 255f);
            destPremul[i + 3] = (byte)((a + destPremul[i + 3] / 255f * invA) * 255f);
        }
    }

    /// <summary>Bilinearly resize a BGRA32 buffer from <paramref name="srcW"/>×<paramref name="srcH"/>
    /// to <paramref name="dstW"/>×<paramref name="dstH"/>.</summary>
    public static byte[] Resize(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        var dst = new byte[dstW * dstH * 4];
        double invW = (srcW - 1) / (double)(dstW - 1);
        double invH = (srcH - 1) / (double)(dstH - 1);
        for (int y = 0; y < dstH; y++)
        {
            double sy = y * invH;
            int sy0 = (int)System.Math.Min(sy, srcH - 1);
            int sy1 = System.Math.Min(sy0 + 1, srcH - 1);
            double fy = sy - sy0;
            for (int x = 0; x < dstW; x++)
            {
                double sx = x * invW;
                int sx0 = (int)System.Math.Min(sx, srcW - 1);
                int sx1 = System.Math.Min(sx0 + 1, srcW - 1);
                double fx = sx - sx0;
                int di = (y * dstW + x) * 4;
                int si00 = (sy0 * srcW + sx0) * 4;
                int si10 = (sy0 * srcW + sx1) * 4;
                int si01 = (sy1 * srcW + sx0) * 4;
                int si11 = (sy1 * srcW + sx1) * 4;
                for (int c = 0; c < 4; c++)
                {
                    double a = src[si00 + c] * (1 - fx) * (1 - fy);
                    double b = src[si10 + c] * fx * (1 - fy);
                    double c2 = src[si01 + c] * (1 - fx) * fy;
                    double d = src[si11 + c] * fx * fy;
                    dst[di + c] = (byte)System.Math.Min(255, a + b + c2 + d);
                }
            }
        }
        return dst;
    }

    public static (float r, float g, float b, float a) ParseTint(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return (1, 1, 1, 1);
        var s = hex.TrimStart('#');
        if (s.Length == 6) s += "FF";
        if (s.Length != 8) return (1, 1, 1, 1);
        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        byte a = Convert.ToByte(s.Substring(6, 2), 16);
        return (r / 255f, g / 255f, b / 255f, a / 255f);
    }
}
