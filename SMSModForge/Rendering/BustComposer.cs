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
