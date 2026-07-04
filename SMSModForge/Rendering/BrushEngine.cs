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
    /// Stamp the brush once at (cx, cy) into <paramref name="strokeBuffer"/>.
    /// Each pixel records <c>max(existing, opacity × falloff)</c> — no additive
    /// build-up within the stroke.
    /// </summary>
    public static void Stamp(float[] strokeBuffer, int cx, int cy, float radius,
                              float hardness, float opacity)
    {
        int size = MaskBuffer.Size;
        int r = (int)MathF.Ceiling(radius);
        int x0 = Math.Max(0, cx - r);
        int y0 = Math.Max(0, cy - r);
        int x1 = Math.Min(size - 1, cx + r);
        int y1 = Math.Min(size - 1, cy + r);
        if (x0 > x1 || y0 > y1) return;

        float rSq = radius * radius;
        float hardR = radius * hardness;
        float fade = radius - hardR;

        for (int y = y0; y <= y1; y++)
        {
            int row = y * size;
            float dy = y - cy;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - cx;
                float distSq = dx * dx + dy * dy;
                if (distSq > rSq) continue;

                float falloff;
                if (fade <= 0f)
                {
                    falloff = 1f;
                }
                else
                {
                    float dist = MathF.Sqrt(distSq);
                    if (dist <= hardR) falloff = 1f;
                    else
                    {
                        float t = (dist - hardR) / fade;
                        falloff = 1f - t * t * (3f - 2f * t);
                    }
                }

                int idx = row + x;
                float contribution = opacity * falloff;
                if (contribution > strokeBuffer[idx])
                    strokeBuffer[idx] = contribution;
            }
        }
    }

    /// <summary>
    /// Stamp along the segment (fx0,fy0)→(fx1,fy1) into <paramref name="strokeBuffer"/>
    /// at <c>radius×0.25</c> spacing. Returns the inclusive dirty rect.
    /// </summary>
    public static (int x0, int y0, int x1, int y1) StampLine(
        float[] strokeBuffer, float fx0, float fy0, float fx1, float fy1,
        float radius, float hardness, float opacity)
    {
        float dx = fx1 - fx0, dy = fy1 - fy0;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        float spacing = MathF.Max(0.5f, radius * 0.25f);
        int steps = Math.Max(1, (int)MathF.Ceiling(len / spacing));

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float px = fx0 + dx * t;
            float py = fy0 + dy * t;
            Stamp(strokeBuffer, (int)MathF.Round(px), (int)MathF.Round(py),
                  radius, hardness, opacity);
        }

        int rad = (int)MathF.Ceiling(radius) + 1;
        int minX = (int)MathF.Floor(MathF.Min(fx0, fx1)) - rad;
        int maxX = (int)MathF.Ceiling(MathF.Max(fx0, fx1)) + rad;
        int minY = (int)MathF.Floor(MathF.Min(fy0, fy1)) - rad;
        int maxY = (int)MathF.Ceiling(MathF.Max(fy0, fy1)) + rad;
        return (Math.Max(0, minX), Math.Max(0, minY),
                Math.Min(MaskBuffer.Size - 1, maxX),
                Math.Min(MaskBuffer.Size - 1, maxY));
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
}
