using System;
using System.Threading.Tasks;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// CPU port of <c>Sprites/JiggleSprite</c>. The HLSL is small enough that running
/// it on the CPU at 256×256 (65 536 px) is comfortably real-time in C#: a parallel
/// pass on a modern machine ships well under 16 ms.
/// <para/>
/// Inputs are premultiplied BGRA32 byte arrays in scanline-top-down order — the
/// format we'll hand back to a WPF <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>.
/// </summary>
public static class JiggleShader
{
    /// <summary>
    /// Source texture resolution. Base / mask / overlay PNGs are loaded at
    /// this size; <see cref="Render"/> samples from them at this grid.
    /// </summary>
    public const int Size = 256;
    public const int Stride = Size * 4; // BGRA32

    /// <summary>
    /// Output / display resolution for the shader pass. Deliberately a
    /// strict multiple of <see cref="Size"/> (currently 2×) so the output
    /// grid is denser than the source grid: adjacent output pixels carry
    /// distinct UVs separated by <c>1/RenderSize</c>, while source texels
    /// are spaced at <c>1/Size</c>. The visible payoff is sub-source-texel
    /// motion — the boundary between two source-texels can shift by one
    /// output pixel at a time, producing the same crisp-yet-smooth feel
    /// Unity gets when a point-filtered sprite moves through worldspace
    /// at a screen-pixel density higher than its native texel density.
    /// At equal source/output resolution the shader could only produce
    /// whole-source-texel jumps, which felt clunky for fine motion.
    /// </summary>
    public const int RenderSize = Size * 2;
    public const int RenderStride = RenderSize * 4;

    /// <summary>
    /// Apply the JiggleSprite distortion. <paramref name="baseTex"/> and
    /// <paramref name="maskTex"/> are both <see cref="Size"/>×<see cref="Size"/>
    /// BGRA byte buffers; <paramref name="output"/> is sized to
    /// <see cref="RenderSize"/>×<see cref="RenderSize"/>.
    /// <paramref name="time"/> is seconds since the preview started (the
    /// shader's <c>_Time.y</c>).
    /// <para/>
    /// <paramref name="superSample"/> selects 4× sub-pixel supersampling (the
    /// motion-edge anti-aliasing described below). When false, a single sample
    /// is taken at the pixel centre — a quarter of the work, at the cost of a
    /// little shimmer on fast jiggle boundaries. The preview-quality menu
    /// toggles this.
    /// </summary>
    public static void Render(
        byte[] baseTex, byte[] maskTex,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, bool superSample = true)
        => Render(baseTex, maskTex, Size, Size, p, tint, time, output, RenderSize, RenderSize, superSample);

    /// <summary>
    /// Size-parameterised jiggle pass. <paramref name="baseTex"/> /
    /// <paramref name="maskTex"/> are <paramref name="srcW"/>×<paramref name="srcH"/>
    /// BGRA buffers; <paramref name="output"/> is <paramref name="outW"/>×<paramref name="outH"/>.
    /// The bust preview uses the fixed 256→512 overload above; the NPC preview
    /// calls this with the pose's real (non-square) resolution so a 1024-px pose
    /// isn't crushed to 256² before display.
    /// </summary>
    public static void Render(
        byte[] baseTex, byte[] maskTex, int srcW, int srcH,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, int outW, int outH, bool superSample = true)
    {
        int srcStride = srcW * 4, outStride = outW * 4;
        if (baseTex.Length != srcStride * srcH) throw new ArgumentException("baseTex bad size", nameof(baseTex));
        if (maskTex.Length != srcStride * srcH) throw new ArgumentException("maskTex bad size", nameof(maskTex));
        if (output.Length  != outStride * outH) throw new ArgumentException("output bad size",  nameof(output));

        // Sin term depends only on time, hoist it.
        float sinTime = MathF.Sin(time * p.Speed);
        float waveTimeOffset = time * p.Speed;
        float noiseTimeOffset = time * p.NoiseSpeed;

        // 2×2 sub-pixel sample offsets within each output pixel. Sampling
        // four positions per pixel and averaging gives anti-aliasing
        // exactly where it's needed — at displacement-driven boundaries
        // between source texels — while leaving flat regions crisp (all
        // four sub-samples hit the same source pixel, so the average is
        // that pixel's colour with no blur). This kills the horizontal
        // step ("tear") that appears when the wave term makes adjacent
        // output rows land in different source pixels: with strict point
        // sampling that's a hard 1-pixel jog, with 4× supersampling the
        // transition row averages two source colours and reads as smooth.
        // The pattern (0.25, 0.75) inside the pixel is a rotated grid in
        // miniature — four corners of a centred 0.5×0.5 sub-square.
        const float SubA = 0.25f;
        const float SubB = 0.75f;

        // Which displacement terms are actually live this frame. When an
        // outfit leaves a channel at zero strength its math multiplies out to
        // nothing, so we skip it wholesale — this changes no pixel (the term
        // contributed 0 anyway) but cuts the dominant cost: the gradient noise
        // alone is ~16 sin() per sample, so a noise-free outfit gets that much
        // cheaper. Both off → a plain point-sample of the base texture.
        bool useJiggle = p.Strength != 0f;
        bool useNoise  = p.NoiseStrength != 0f;

        // 4× supersample averages by 0.25; a single centre sample by 1.
        float sampleWeight = superSample ? 0.25f : 1f;

        // Parallel over rows of the OUTPUT grid (denser than the source).
        Parallel.For(0, outH, y =>
        {
            int row = y * outStride;
            for (int x = 0; x < outW; x++)
            {
                // Accumulators for the sub-samples (kept in straight alpha so
                // the average is colour-correct; premultiply once at
                // write-time below).
                float sumR = 0, sumG = 0, sumB = 0, sumA = 0;

                if (superSample)
                {
                    SampleAt(x + SubA, y + SubA);
                    SampleAt(x + SubB, y + SubA);
                    SampleAt(x + SubA, y + SubB);
                    SampleAt(x + SubB, y + SubB);
                }
                else
                {
                    SampleAt(x + 0.5f, y + 0.5f);
                }

                // Local function so the call sites stay readable. We pay
                // nothing for it — JIT inlines local-function calls when the
                // closure captures are stack-local like here.
                void SampleAt(float sx, float sy)
                {
                    float u = sx / outW;
                    float v = 1.0f - sy / outH;

                    float du = u, dv = v;
                    if (useJiggle || useNoise)
                    {
                        // Mask sample at (u,v) — point filter, as the game does.
                        int mx = (int)(u * srcW); if (mx >= srcW) mx = srcW - 1;
                        int my = (int)((1.0f - v) * srcH); if (my >= srcH) my = srcH - 1;
                        int mi = my * srcStride + mx * 4;
                        float ma = maskTex[mi + 3] / 255f;

                        if (useJiggle)
                        {
                            // Bounce (R), Wave (G).
                            float mr = maskTex[mi + 2] / 255f;
                            float mg = maskTex[mi + 1] / 255f;
                            float bounce = sinTime * p.Strength * mr * ma;
                            float wave   = MathF.Sin(v * p.Frequency + waveTimeOffset) * p.Strength * mg * ma;
                            du += wave;
                            dv += bounce;
                        }

                        if (useNoise)
                        {
                            // Gradient noise displacement (Blue channel) —
                            // matches the game's compiled DXBC.
                            float mb = maskTex[mi] / 255f;
                            float noiseAmp = p.NoiseStrength * mb * ma;
                            float ncx = u * p.NoiseScale + noiseTimeOffset;
                            float ncy = v * p.NoiseScale + noiseTimeOffset;
                            du += GradientNoise(ncx, ncy) * noiseAmp;
                            dv += GradientNoise(ncx + 100f, ncy + 100f) * noiseAmp;
                        }
                    }

                    if (du < 0f || du >= 1f || dv < 0f || dv >= 1f)
                        return; // contributes (0,0,0,0) — sums unchanged

                    SamplePoint(baseTex, du, dv, srcW, srcH,
                        out float cr, out float cg, out float cb, out float ca);

                    // Apply tint.
                    sumR += cr * tint.r;
                    sumG += cg * tint.g;
                    sumB += cb * tint.b;
                    sumA += ca * tint.a;
                }

                // Average the sub-samples. Out-of-bounds samples contributed
                // (0,0,0,0); we still divide by the full sample count so the
                // boundary fades to transparent smoothly rather than clipping
                // abruptly to the in-bounds colour.
                float r = sumR * sampleWeight;
                float g = sumG * sampleWeight;
                float b = sumB * sampleWeight;
                float a = sumA * sampleWeight;

                // Premultiplied alpha output (Blend One OneMinusSrcAlpha).
                int outIdx = row + x * 4;
                output[outIdx    ] = (byte)(b * a * 255f);
                output[outIdx + 1] = (byte)(g * a * 255f);
                output[outIdx + 2] = (byte)(r * a * 255f);
                output[outIdx + 3] = (byte)(a * 255f);
            }
        });
    }

    private static (float gx, float gy) GradientHash(float ix, float iy)
    {
        float h1 = ix * 127.1f + iy * 311.7f;
        float h2 = ix * 269.5f + iy * 183.3f;
        float g1 = MathF.Sin(h1) * 43758.5453f;
        float g2 = MathF.Sin(h2) * 43758.5453f;
        g1 = g1 - MathF.Floor(g1);
        g2 = g2 - MathF.Floor(g2);
        return (g1 * 2f - 1f, g2 * 2f - 1f);
    }

    private static float GradientNoise(float px, float py)
    {
        float ix = MathF.Floor(px);
        float iy = MathF.Floor(py);
        float fx = px - ix;
        float fy = py - iy;

        float ux = fx * fx * (3f - 2f * fx);
        float uy = fy * fy * (3f - 2f * fy);

        var (g00x, g00y) = GradientHash(ix, iy);
        float n00 = g00x * fx + g00y * fy;

        var (g10x, g10y) = GradientHash(ix + 1f, iy);
        float n10 = g10x * (fx - 1f) + g10y * fy;

        var (g01x, g01y) = GradientHash(ix, iy + 1f);
        float n01 = g01x * fx + g01y * (fy - 1f);

        var (g11x, g11y) = GradientHash(ix + 1f, iy + 1f);
        float n11 = g11x * (fx - 1f) + g11y * (fy - 1f);

        float bottom = n00 + ux * (n10 - n00);
        float top = n01 + ux * (n11 - n01);
        return bottom + uy * (top - bottom);
    }

    /// <summary>
    /// Nearest-neighbour (point) sample from a BGRA32 texture buffer. UV
    /// origin is bottom-left (Unity convention); the buffer is stored
    /// top-down in scanline order. We snap to the texel whose centre is
    /// closest to the (u,v) coordinate — same behaviour the GPU performs
    /// when a texture is set to <c>FilterMode.Point</c>, which is what
    /// pixel-art-style games like this one use for their sprites so the
    /// art stays crisp at non-integer screen scales.
    /// </summary>
    private static void SamplePoint(byte[] tex, float u, float v, int w, int h,
        out float r, out float g, out float b, out float a)
    {
        // Map UV to integer pixel coordinates. The +0.5 implicit in (u*w)
        // → floor lands on the texel whose centre is nearest to the UV.
        int x = (int)(u * w);
        int y = (int)((1.0f - v) * h);
        if (x < 0) x = 0; else if (x >= w) x = w - 1;
        if (y < 0) y = 0; else if (y >= h) y = h - 1;

        int i = y * w * 4 + x * 4;
        b = tex[i    ] / 255f;
        g = tex[i + 1] / 255f;
        r = tex[i + 2] / 255f;
        a = tex[i + 3] / 255f;
    }

    // ── Overlay displacement ──────────────────────────────────────────
    // Bust overlays (blink / mouth / expression) do NOT run this shader in
    // game. Given its material they recolour badly: the lit pass gathers the
    // sprite's own texture weighted by (1 - alpha), so a sprite covering a
    // fraction of a percent of its canvas is lit as though floating in void,
    // where the body covers about 27%. So the game leaves them on the stock
    // sprite shader and displaces each one RIGIDLY instead, by this shader's
    // offset evaluated once at the sprite's own centroid.
    //
    // The two helpers below are that calculation, kept beside the per-pixel
    // version they have to agree with.

    /// <summary>
    /// Alpha-weighted centre of a straight-alpha BGRA buffer, in UV
    /// (v bottom-up, matching <see cref="Render"/>).
    /// <para/>
    /// The geometric centre would be wrong: an overlay is a small mark on a
    /// mostly empty canvas, so the middle of the frame usually samples the mask
    /// nowhere near the art. Falls back to the centre for a fully transparent
    /// buffer. Subsampled on a 2px grid — this only picks which mask texel to
    /// read, and it runs once per overlay per frame.
    /// </summary>
    public static (float u, float v) OpaqueCentroid(byte[] straight, int w = Size, int h = Size)
    {
        double sx = 0, sy = 0, sa = 0;
        int stride = w * 4;
        for (int y = 0; y < h; y += 2)
        {
            int row = y * stride;
            for (int x = 0; x < w; x += 2)
            {
                byte a = straight[row + x * 4 + 3];
                if (a == 0) continue;
                sx += x * (double)a; sy += y * (double)a; sa += a;
            }
        }
        if (sa <= 0) return (0.5f, 0.5f);
        return ((float)(sx / sa) / w, 1.0f - (float)(sy / sa) / h);
    }

    /// <summary>
    /// The displacement this shader would apply at a single UV — the same
    /// bounce / wave / noise terms <see cref="Render"/> computes per pixel,
    /// evaluated once. Returned in the shader's sense: the SAMPLE coordinate
    /// moves by +offset, so the drawn image moves by -offset.
    /// </summary>
    public static (float du, float dv) OffsetAt(
        byte[] maskTex, float u, float v, JiggleParams p, float time,
        int srcW = Size, int srcH = Size)
    {
        int mx = (int)(u * srcW); if (mx >= srcW) mx = srcW - 1; if (mx < 0) mx = 0;
        int my = (int)((1.0f - v) * srcH); if (my >= srcH) my = srcH - 1; if (my < 0) my = 0;
        int mi = my * (srcW * 4) + mx * 4;

        float ma = maskTex[mi + 3] / 255f;
        float du = 0f, dv = 0f;

        // Same gates Render uses, so an overlay never moves on a term the body
        // has switched off.
        if (p.Strength != 0f)
        {
            float mr = maskTex[mi + 2] / 255f;
            float mg = maskTex[mi + 1] / 255f;
            du += MathF.Sin(v * p.Frequency + time * p.Speed) * p.Strength * mg * ma;
            dv += MathF.Sin(time * p.Speed) * p.Strength * mr * ma;
        }

        if (p.NoiseStrength != 0f)
        {
            float mb = maskTex[mi] / 255f;
            float amp = p.NoiseStrength * mb * ma;
            float ncx = u * p.NoiseScale + time * p.NoiseSpeed;
            float ncy = v * p.NoiseScale + time * p.NoiseSpeed;
            du += GradientNoise(ncx, ncy) * amp;
            dv += GradientNoise(ncx + 100f, ncy + 100f) * amp;
        }

        return (du, dv);
    }
}
