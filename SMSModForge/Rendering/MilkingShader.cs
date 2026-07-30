using System;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// CPU port of the MILKING variant of <c>Universal Render Pipeline/2D/Sprite-Lit-Jiggle</c>
/// — the shader every level's material uses, and the one a place's mask sprite
/// actually drives.
/// <para/>
/// This is a different shader from the busts' <c>Sprites/JiggleSprite</c> (see
/// <see cref="JiggleShader"/>), and its mask means something different: only the
/// ALPHA channel is read, as a per-pixel intensity. There is no R/G/B split into
/// bounce, wave and noise here.
/// <para/>
/// Recovered from the compiled bytecode rather than the shipped source, because
/// AssetRipper leaves this shader's fragment body as a stub. The variant is
/// sub-program blob 292 of shader 4477 in <c>sharedassets1.assets</c> — the
/// <c>_JIGGLETYPE_MILKING</c> fragment with no shape-light keywords —
/// disassembled with <c>D3DDisassemble</c>. Constant-buffer slots were matched
/// to properties by HLSL packing order, which lines up exactly: every scalar the
/// bytecode reads lands where its declaration says it should
/// (<c>cb0[137].y</c> = <c>_MilkingWaveCount</c>, <c>cb0[138]</c> =
/// <c>_MilkingDirection</c>, <c>cb0[139].x</c> = <c>_MilkingCompressionRatio</c>).
/// <para/>
/// The three bare literals below (0.5, 0.4, and the 1.7 / 0.8 pair) are hard
/// coded in the bytecode. They are NOT <c>_JiggleSecondarySpeed</c> and
/// <c>_JiggleSecondaryStrength</c>, which this variant never reads — worth
/// stating, because those two properties exist on the material and look for all
/// the world as though they should drive the secondary wave.
/// </summary>
public static class MilkingShader
{
    private const float Pi = 3.14159f;   // the literal the bytecode carries

    /// <summary>
    /// The settings the game's own level material ships with. A pack place is a
    /// clone of the Beach level, so these are what its art is displaced by
    /// whatever the pack does — only the mask changes.
    /// </summary>
    public readonly struct Settings
    {
        public readonly float Speed, Strength, PhaseOffset, AmplitudeVariation;
        public readonly float WaveCount, CompressionRatio, DirX, DirY;

        public Settings(float speed, float strength, float phaseOffset, float amplitudeVariation,
                        float waveCount, float compressionRatio, float dirX, float dirY)
        {
            Speed = speed; Strength = strength; PhaseOffset = phaseOffset;
            AmplitudeVariation = amplitudeVariation; WaveCount = waveCount;
            CompressionRatio = compressionRatio; DirX = dirX; DirY = dirY;
        }

        /// <summary>Beach.mat verbatim — the material every pack level clones.</summary>
        public static readonly Settings Level = new(
            speed: 2f, strength: 0.004f, phaseOffset: 0f, amplitudeVariation: 0f,
            waveCount: 3f, compressionRatio: 0.1f, dirX: 0f, dirY: 1f);
    }

    /// <summary>
    /// Displace <paramref name="baseTex"/> by the mask and write the result.
    /// Both are premultiplied BGRA, at their OWN sizes: the mask is sampled in
    /// UV space exactly as the GPU samples it, so a 256² mask drives 2048-wide
    /// art with no resampling step in between. That is also what lets the mask
    /// editor's live buffer be handed straight in while it is being painted.
    /// <paramref name="output"/> is <paramref name="outW"/>×<paramref name="outH"/>.
    /// <paramref name="time"/> is the shader's <c>_Time.y</c>.
    /// </summary>
    public static void Render(byte[] baseTex, int srcW, int srcH,
                              byte[] maskTex, int maskW, int maskH,
                              in Settings s, float time,
                              byte[] output, int outW, int outH)
    {
        int srcStride = srcW * 4, maskStride = maskW * 4, outStride = outW * 4;
        if (baseTex.Length != srcStride * srcH) throw new ArgumentException("baseTex bad size", nameof(baseTex));
        if (maskTex.Length != maskStride * maskH) throw new ArgumentException("maskTex bad size", nameof(maskTex));
        if (output.Length != outStride * outH) throw new ArgumentException("output bad size", nameof(output));

        // timePhase = _Time.y * _JiggleSpeed + _JigglePhaseOffset
        float timePhase = time * s.Speed + s.PhaseOffset;

        // dir = normalize(_MilkingDirection), falling back to (0,1) when the
        // vector is degenerate — the bytecode's own length < 0.1 guard.
        float dx = s.DirX, dy = s.DirY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.1f) { dx = 0f; dy = 1f; }
        else { dx /= len; dy /= len; }
        float px = -dy, py = dx;                 // perpendicular

        // Copied out of the `in` parameter: a lambda can't close over one.
        float variation = s.AmplitudeVariation, strength = s.Strength;
        float waveCount = s.WaveCount, compressionRatio = s.CompressionRatio;

        // Every wave term depends on the pixel ONLY through t = dot(uv, dir),
        // so they are tabulated once per frame instead of evaluated per pixel.
        // Level art is millions of pixels and this collapses six sines each into
        // a lookup; t spans [0, |dx|+|dy|] for uv in the unit square.
        float tMax = Math.Abs(dx) + Math.Abs(dy);
        var slideT = new float[WaveTable];
        var crossT = new float[WaveTable];
        for (int i = 0; i < WaveTable; i++)
        {
            float t = tMax * i / (WaveTable - 1);
            float waveArg = t * waveCount * Pi - 2f * timePhase;
            slideT[i] = 0.5f * MathF.Sin(t * waveCount + timePhase);
            crossT[i] = MathF.Sin(waveArg) * compressionRatio
                      + 0.4f * MathF.Sin(waveArg * 1.7f + timePhase * 0.8f);
        }
        float tScale = tMax > 0 ? (WaveTable - 1) / tMax : 0f;

        Parallel.For(0, outH, oy =>
        {
            // Output pixel centres in UV, so a reduced-size pass samples the
            // same field the full-size one would.
            float v = (oy + 0.5f) / outH;
            int outRow = oy * outStride;
            for (int ox = 0; ox < outW; ox++)
            {
                float u = (ox + 0.5f) / outW;

                // amp = (1 + variation*sin(u*PI)) * mask.a * strength
                int mx = Math.Min(maskW - 1, (int)(u * maskW));
                int my = Math.Min(maskH - 1, (int)(v * maskH));
                float maskA = maskTex[my * maskStride + mx * 4 + 3] / 255f;
                float ampVar = variation == 0f ? 1f : 1f + variation * MathF.Sin(u * Pi);
                float amp = ampVar * maskA * strength;

                // Interpolated so the wave stays smooth between table entries —
                // the whole displacement is only a few pixels, and quantising it
                // would show up as banding rather than motion.
                float ft = (u * dx + v * dy) * tScale;
                int i0 = (int)ft; if (i0 < 0) i0 = 0;
                if (i0 > WaveTable - 2) i0 = WaveTable - 2;
                float fr = ft - i0;
                float slide = amp * (slideT[i0] + (slideT[i0 + 1] - slideT[i0]) * fr);
                float cross = amp * (crossT[i0] + (crossT[i0 + 1] - crossT[i0]) * fr);

                float offU = slide * dx + cross * px;
                float offV = slide * dy + cross * py;

                // add_sat: the displaced UV is saturated, not wrapped, so the
                // edges smear rather than showing the opposite side.
                float su = Math.Clamp(u + offU, 0f, 1f);
                float sv = Math.Clamp(v + offV, 0f, 1f);

                // Bilinear, because the game imports level art with
                // FilterMode.Bilinear and because the displacement peaks at a
                // few pixels: point sampling quantises that to whole texels and
                // the art reads as juddering rather than drifting.
                SampleBilinear(baseTex, srcW, srcH, srcStride, su, sv, output, outRow + ox * 4);
            }
        });
    }

    /// <summary>Table resolution for the wave terms. The wave repeats every
    /// <c>2/waveCount</c> in t, so a few thousand entries put many samples in
    /// every cycle at any sane wave count.</summary>
    private const int WaveTable = 4096;

    private static void SampleBilinear(byte[] src, int w, int h, int stride,
                                       float u, float v, byte[] dst, int di)
    {
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int x1 = Math.Min(w - 1, x0 + 1), y1 = Math.Min(h - 1, y0 + 1);
        x0 = Math.Clamp(x0, 0, w - 1); y0 = Math.Clamp(y0, 0, h - 1);
        int r00 = y0 * stride, r10 = y1 * stride;
        int i00 = r00 + x0 * 4, i01 = r00 + x1 * 4, i10 = r10 + x0 * 4, i11 = r10 + x1 * 4;
        for (int c = 0; c < 4; c++)
        {
            float top = src[i00 + c] + (src[i01 + c] - src[i00 + c]) * ax;
            float bot = src[i10 + c] + (src[i11 + c] - src[i10 + c]) * ax;
            dst[di + c] = (byte)(top + (bot - top) * ay + 0.5f);
        }
    }
}
