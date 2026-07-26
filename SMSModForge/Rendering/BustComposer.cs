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

        // Force to 256×256 BGRA32 by drawing into a transformed bitmap if needed.
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        if (converted.PixelWidth == JiggleShader.Size && converted.PixelHeight == JiggleShader.Size)
        {
            var buf = new byte[JiggleShader.Stride * JiggleShader.Size];
            converted.CopyPixels(buf, JiggleShader.Stride, 0);
            return buf;
        }

        var scaled = new TransformedBitmap(converted,
            new System.Windows.Media.ScaleTransform(
                JiggleShader.Size / (double)converted.PixelWidth,
                JiggleShader.Size / (double)converted.PixelHeight));
        var buf2 = new byte[JiggleShader.Stride * JiggleShader.Size];
        scaled.CopyPixels(buf2, JiggleShader.Stride, 0);
        return buf2;
    }

    /// <summary>
    /// Standard "source over premultiplied" composite. <paramref name="destPremul"/>
    /// is sized for <see cref="JiggleShader.RenderSize"/>², <paramref name="overlayStraight"/>
    /// is sized for <see cref="JiggleShader.Size"/>² (source resolution). We NN-upscale
    /// the overlay during the composite pass — point-filtering matches the
    /// pixel-art look and keeps the overlay crisp at the denser output grid.
    /// Destination is mutated in place.
    /// </summary>
    public static void Composite(byte[] destPremul, byte[] overlayStraight)
    {
        if (destPremul.Length != JiggleShader.RenderStride * JiggleShader.RenderSize)
            throw new ArgumentException("dest bad size", nameof(destPremul));
        if (overlayStraight.Length != JiggleShader.Stride * JiggleShader.Size)
            throw new ArgumentException("overlay bad size", nameof(overlayStraight));

        // Scale factor between the source grid and the render grid. Constant
        // (currently 2) so we can use integer division for the NN lookup with
        // no per-pixel float math.
        const int scale = JiggleShader.RenderSize / JiggleShader.Size;

        for (int y = 0; y < JiggleShader.RenderSize; y++)
        {
            int srcY = y / scale;
            int destRow = y * JiggleShader.RenderStride;
            int srcRow = srcY * JiggleShader.Stride;

            for (int x = 0; x < JiggleShader.RenderSize; x++)
            {
                int srcX = x / scale;
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
