using System;

namespace SMSModForge.Rendering;

/// <summary>
/// Stamp-based brush/eraser for a single 8-bit channel of <see cref="MaskBuffer"/>.
/// Uses a per-stroke contribution buffer so that overlapping stamps within one
/// mouseDown→mouseUp drag track the maximum contribution at each pixel rather
/// than summing additively. The caller is responsible for allocating the stroke
/// buffer and applying it back to the channel after each stamp batch.
/// </summary>
public static class BrushEngine
{
    /// <summary>
    /// How much of the brush reaches a pixel <paramref name="dist"/> from the
    /// stroke's path, from 0 to 1 (1.9.0, made to feel like Paint.NET's brush).
    /// <para/>
    /// Hardness is the share of the radius painted fully: inside
    /// <c>radius × hardness</c> everything, then a smooth fade to nothing at the
    /// radius - so at 0% the fade starts at the centre and the brush is a soft
    /// glow, at 100% it is a disc. The edge itself is antialiased over one
    /// pixel, as an antialiased hard brush in a paint program is: until 1.9.0 a
    /// 100% brush cut pixels in or out whole, which drew stair-steps, and a
    /// small brush at a high hardness had a fade narrower than a pixel, which
    /// looked just as hard.
    /// </summary>
    public static float Falloff(float dist, float radius, float hardness)
    {
        if (dist >= radius + 0.5f) return 0f;
        float rim = Math.Clamp(radius + 0.5f - dist, 0f, 1f);
        float core = radius * Math.Clamp(hardness, 0f, 1f);
        float band = radius - core;
        if (band <= 0f || dist <= core) return rim;
        float t = Math.Clamp((dist - core) / band, 0f, 1f);
        return rim * (1f - t * t * (3f - 2f * t));
    }

    /// <summary>
    /// Paint the brush once at (cx, cy), anywhere between pixels, into
    /// <paramref name="strokeBuffer"/>. Each pixel records
    /// <c>max(existing, opacity × falloff)</c> — no build-up within a stroke.
    /// Returns the inclusive dirty rect.
    /// </summary>
    public static (int x0, int y0, int x1, int y1) Stamp(float[] strokeBuffer, float cx, float cy,
                                                         float radius, float hardness, float opacity)
        => StampLine(strokeBuffer, cx, cy, cx, cy, radius, hardness, opacity);

    /// <summary>
    /// Paint the brush along the segment (fx0,fy0)→(fx1,fy1) into
    /// <paramref name="strokeBuffer"/>, as one band: every pixel takes the
    /// falloff of its distance to the segment. Returns the inclusive dirty rect.
    /// <para/>
    /// Until 1.9.0 this stamped a disc every quarter radius, each snapped to a
    /// whole pixel: a soft stroke's edge came out beaded and a thin one
    /// wobbled. Measured from the segment itself, a stroke's edge is as smooth
    /// as the brush's, at any size and any speed of drawing, and the joint with
    /// the next segment is seamless (its end is the same round cap).
    /// </summary>
    public static (int x0, int y0, int x1, int y1) StampLine(
        float[] strokeBuffer, float fx0, float fy0, float fx1, float fy1,
        float radius, float hardness, float opacity)
    {
        int size = MaskBuffer.Size;
        int reach = (int)MathF.Ceiling(radius + 0.5f) + 1;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(fx0, fx1)) - reach);
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(fy0, fy1)) - reach);
        int x1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(fx0, fx1)) + reach);
        int y1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(fy0, fy1)) + reach);
        if (x0 > x1 || y0 > y1) return (Math.Clamp(x0, 0, size - 1), Math.Clamp(y0, 0, size - 1), Math.Clamp(x0, 0, size - 1), Math.Clamp(y0, 0, size - 1));

        float sx = fx1 - fx0, sy = fy1 - fy0;
        float lenSq = sx * sx + sy * sy;
        float limitSq = (radius + 0.5f) * (radius + 0.5f);

        for (int y = y0; y <= y1; y++)
        {
            int row = y * size;
            for (int x = x0; x <= x1; x++)
            {
                // The nearest point of the segment to this pixel's centre. Texture
                // pixel x spans [x, x+1) of the pointer's coordinates, so its
                // centre is x + 0.5: the dab sits exactly under the cursor ring.
                float px = x + 0.5f - fx0, py = y + 0.5f - fy0;
                float t = lenSq > 0f ? Math.Clamp((px * sx + py * sy) / lenSq, 0f, 1f) : 0f;
                float dx = px - sx * t, dy = py - sy * t;
                float distSq = dx * dx + dy * dy;
                if (distSq >= limitSq) continue;

                float contribution = opacity * Falloff(MathF.Sqrt(distSq), radius, hardness);
                int idx = row + x;
                if (contribution > strokeBuffer[idx])
                    strokeBuffer[idx] = contribution;
            }
        }
        return (x0, y0, x1, y1);
    }

    /// <summary>
    /// Apply the stroke contribution buffer back to the channel within the dirty
    /// rect, using <paramref name="original"/> (snapshot taken at stroke start)
    /// as the base. Call after every Stamp/StampLine to keep the channel current.
    /// </summary>
    public static void ApplyStrokeBuffer(byte[] channel, byte[] original,
        float[] strokeBuffer, int x0, int y0, int x1, int y1,
        bool capMode, bool erase)
    {
        int size = MaskBuffer.Size;
        x0 = Math.Clamp(x0, 0, size - 1);
        y0 = Math.Clamp(y0, 0, size - 1);
        x1 = Math.Clamp(x1, 0, size - 1);
        y1 = Math.Clamp(y1, 0, size - 1);

        for (int y = y0; y <= y1; y++)
        {
            int row = y * size;
            for (int x = x0; x <= x1; x++)
            {
                int idx = row + x;
                float contribution = strokeBuffer[idx];
                if (contribution <= 0f) continue;

                float cur = original[idx] / 255f;
                float result;

                if (erase)
                {
                    if (capMode)
                    {
                        float floor = 1f - contribution;
                        result = cur <= floor ? cur : MathF.Max(floor, cur - contribution);
                    }
                    else
                    {
                        result = MathF.Max(0f, cur - contribution);
                    }
                }
                else
                {
                    if (capMode)
                    {
                        float ceiling = contribution;
                        result = cur >= ceiling ? cur : MathF.Min(ceiling, cur + contribution);
                    }
                    else
                    {
                        result = MathF.Min(1f, cur + contribution);
                    }
                }

                channel[idx] = (byte)(result * 255f + 0.5f);
            }
        }
    }

    /// <summary>The edge softness the soften brush starts at, in mask pixels.</summary>
    public const float DefaultEdgeSoftness = 8f;

    /// <summary>
    /// <paramref name="layer"/> with every edge softened to
    /// <paramref name="edgeWidth"/> mask pixels: a hard edge comes out as a fade
    /// from nothing to full across about that many pixels (2% to 98%; a
    /// Gaussian blur of sigma <c>width / 4</c>, measured from the pixels that
    /// exist at the mask's border). What the soften brush pulls towards (1.9.1),
    /// taken from the layer as it was before any soften stroke - so going over
    /// an edge again can never take it past this.
    /// </summary>
    public static float[] Softened(byte[] layer, int size, float edgeWidth)
    {
        var result = new float[size * size];
        float sigma = edgeWidth / 4f;
        if (sigma < 0.05f)
        {
            for (int i = 0; i < result.Length; i++) result[i] = layer[i];
            return result;
        }

        int reach = Math.Max(1, (int)MathF.Ceiling(3f * sigma));
        var kernel = new float[2 * reach + 1];
        for (int d = -reach; d <= reach; d++)
            kernel[d + reach] = MathF.Exp(-(d * d) / (2f * sigma * sigma));

        // Rows, then columns: a Gaussian is the same either way round. At the
        // border only the pixels that exist count, so a full layer stays full.
        var rows = new float[size * size];
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float sum = 0f, weight = 0f;
                for (int xx = Math.Max(0, x - reach), end = Math.Min(size - 1, x + reach); xx <= end; xx++)
                {
                    float k = kernel[xx - x + reach];
                    sum += layer[row + xx] * k;
                    weight += k;
                }
                rows[row + x] = sum / weight;
            }
        }
        for (int y = 0; y < size; y++)
        {
            int top = Math.Max(0, y - reach), bottom = Math.Min(size - 1, y + reach);
            for (int x = 0; x < size; x++)
            {
                float sum = 0f, weight = 0f;
                for (int yy = top; yy <= bottom; yy++)
                {
                    float k = kernel[yy - y + reach];
                    sum += rows[yy * size + x] * k;
                    weight += k;
                }
                result[y * size + x] = sum / weight;
            }
        }
        return result;
    }

    /// <summary>
    /// The soften brush: within the dirty rect, each pixel the stroke reached
    /// moves from what it was when the stroke began towards
    /// <paramref name="target"/> (0 to 255), by the stroke's contribution there
    /// (the brush's falloff). Where the brush covers fully it lands on the
    /// target, and since the target does not move, going over a place again
    /// leaves it as it is: an edge is softened to the softness asked for and
    /// never past it.
    /// </summary>
    public static void ApplySoften(byte[] channel, byte[] original, float[] target,
        float[] strokeBuffer, int x0, int y0, int x1, int y1)
    {
        int size = MaskBuffer.Size;
        x0 = Math.Clamp(x0, 0, size - 1);
        y0 = Math.Clamp(y0, 0, size - 1);
        x1 = Math.Clamp(x1, 0, size - 1);
        y1 = Math.Clamp(y1, 0, size - 1);

        for (int y = y0; y <= y1; y++)
        {
            int row = y * size;
            for (int x = x0; x <= x1; x++)
            {
                int idx = row + x;
                float contribution = strokeBuffer[idx];
                if (contribution <= 0f) continue;
                float cur = original[idx];
                float result = cur + (target[idx] - cur) * Math.Min(contribution, 1f);
                channel[idx] = (byte)(Math.Clamp(result, 0f, 255f) + 0.5f);
            }
        }
    }
}
