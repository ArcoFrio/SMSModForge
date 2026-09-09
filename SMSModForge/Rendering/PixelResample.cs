namespace SMSModForge.Rendering;

/// <summary>
/// Bilinear resampling of a four-bytes-per-pixel buffer.
/// <para/>
/// Plain arithmetic on a byte array, deliberately: no dispatcher, no render
/// target, safe to call from whatever thread happens to be loading art. WPF's
/// own scalers all want one or the other.
/// <para/>
/// Format-agnostic, because it has two callers wanting different ones — the
/// vanilla art restore works in straight alpha, and the UI sprite restore works
/// in premultiplied, which is the better of the two to filter in: averaging a
/// transparent pixel's colour into an opaque neighbour is what produces a halo,
/// and premultiplied alpha has already weighted it to nothing.
/// </summary>
internal static class PixelResample
{
    /// <summary>
    /// Resize <paramref name="source"/> to <paramref name="width"/> ×
    /// <paramref name="height"/>.
    /// <para/>
    /// Samples from the CENTRE of each destination pixel, which is what puts
    /// the picture back where it came from. Sampling from the corner shifts
    /// everything half a pixel up and left — invisible on one hop, and exactly
    /// the kind of drift that accumulates into a misaligned overlay.
    /// </summary>
    public static byte[] Bilinear(byte[] source, int sourceWidth, int sourceHeight,
                                  int width, int height)
    {
        int srcStride = sourceWidth * 4;
        int dstStride = width * 4;
        var made = new byte[dstStride * height];

        for (int y = 0; y < height; y++)
        {
            double fy = (y + 0.5) * sourceHeight / height - 0.5;
            int y0 = (int)System.Math.Floor(fy);
            double wy = fy - y0;

            int y1 = Clamp(y0 + 1, sourceHeight - 1);
            y0 = Clamp(y0, sourceHeight - 1);

            int row0 = y0 * srcStride, row1 = y1 * srcStride, dstRow = y * dstStride;

            for (int x = 0; x < width; x++)
            {
                double fx = (x + 0.5) * sourceWidth / width - 0.5;
                int x0 = (int)System.Math.Floor(fx);
                double wx = fx - x0;

                int x1 = Clamp(x0 + 1, sourceWidth - 1) * 4;
                x0 = Clamp(x0, sourceWidth - 1) * 4;

                int di = dstRow + x * 4;
                for (int c = 0; c < 4; c++)
                {
                    double top = source[row0 + x0 + c]
                               + (source[row0 + x1 + c] - source[row0 + x0 + c]) * wx;
                    double bottom = source[row1 + x0 + c]
                                  + (source[row1 + x1 + c] - source[row1 + x0 + c]) * wx;
                    made[di + c] = (byte)(top + (bottom - top) * wy + 0.5);
                }
            }
        }

        return made;
    }

    /// <summary>Keep a sample inside the picture. The edge row is repeated
    /// rather than wrapped, so nothing bleeds in from the far side.</summary>
    private static int Clamp(int value, int max)
        => value < 0 ? 0 : value > max ? max : value;
}
