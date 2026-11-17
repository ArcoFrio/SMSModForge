using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace SMSModForge.Rendering;

/// <summary>
/// Texture reads as the game's programs make them (LOD 0, clamped), in the
/// float32 arithmetic Tools/Shaders/dxbc_interp.py uses: a texture is RGBA
/// floats, rows from the BOTTOM (v up). Point: the texel under (u, v).
/// Bilinear: texel centres at (i + 0.5) / size, the four around the point
/// blended along x, then y.
/// </summary>
internal static class GameSampling
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float, float, float, float) Point(float[] tex, int w, int h, float u, float v)
    {
        int x = Clamp(MathF.Floor(u * w), w), y = Clamp(MathF.Floor(v * h), h);
        int i = (y * w + x) * 4;
        return (tex[i], tex[i + 1], tex[i + 2], tex[i + 3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float, float, float, float) Bilinear(float[] tex, int w, int h, float u, float v)
    {
        // Four channels at once (1.9.1): each lane the same IEEE multiplies and
        // adds as the channel-by-channel version this replaced, in the same
        // order - (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy -
        // so every program reads exactly what it did, at a fraction of the cost.
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        float x0 = MathF.Floor(fx), y0 = MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int xa = Clamp(x0, w), xb = Clamp(x0 + 1f, w), ya = Clamp(y0, h), yb = Clamp(y0 + 1f, h);
        ref float p = ref MemoryMarshal.GetArrayDataReference(tex);
        var p00 = Vector128.LoadUnsafe(ref p, (nuint)((ya * w + xa) * 4));
        var p10 = Vector128.LoadUnsafe(ref p, (nuint)((ya * w + xb) * 4));
        var p01 = Vector128.LoadUnsafe(ref p, (nuint)((yb * w + xa) * 4));
        var p11 = Vector128.LoadUnsafe(ref p, (nuint)((yb * w + xb) * 4));
        float bx = 1f - ax, by = 1f - ay;
        var c = (p00 * bx + p10 * ax) * by + (p01 * bx + p11 * ax) * ay;
        return (c.GetElement(0), c.GetElement(1), c.GetElement(2), c.GetElement(3));
    }

    /// <summary><see cref="Bilinear"/> on a one-channel texture.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float BilinearOne(float[] tex, int w, int h, float u, float v)
    {
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        float x0 = MathF.Floor(fx), y0 = MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int xa = Clamp(x0, w), xb = Clamp(x0 + 1f, w), ya = Clamp(y0, h), yb = Clamp(y0 + 1f, h);
        float bottom = tex[ya * w + xa] * (1f - ax) + tex[ya * w + xb] * ax;
        float top = tex[yb * w + xa] * (1f - ax) + tex[yb * w + xb] * ax;
        return bottom * (1f - ay) + top * ay;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(float i, int n) => i < 0f ? 0 : i > n - 1 ? n - 1 : (int)i;
}
