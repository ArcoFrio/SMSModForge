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
        float len2 = dx * dx + dy * dy;
        if (MathF.Sqrt(len2) < 0.1f) { dx = 0f; dy = 1f; }
        else { float inv = 1f / MathF.Sqrt(len2); dx *= inv; dy *= inv; }   // rsq

        // Copied out of the `in` parameter: a lambda can't close over one.
        float variation = s.AmplitudeVariation, strength = s.Strength;
        float waveCount = s.WaveCount, compressionRatio = s.CompressionRatio;

        // The wave terms depend on the pixel only through t = dot(uv, dir), in
        // the game's uv: v runs UP. Until 1.9.1 this used v running down, which
        // mirrored the motion - a pack place's wave ran the other way from the
        // game's (LevelShaderTests holds it to the game's program now). Worked
        // out once a row for an upright direction (a pack place's, Beach's),
        // once a column for a level one, and at every pixel otherwise - exactly,
        // where a table used to interpolate them.
        bool byRow = dx == 0f, byColumn = !byRow && dy == 0f;
        float[]? rowWaves = null, colWaves = null;
        if (byRow)
        {
            rowWaves = new float[outH * 3];
            for (int oy = 0; oy < outH; oy++)
                Waves(1f - (oy + 0.5f) / outH, 0f, dx, dy, timePhase, waveCount, rowWaves, oy * 3, true);
        }
        else if (byColumn)
        {
            colWaves = new float[outW * 3];
            for (int ox = 0; ox < outW; ox++)
                Waves((ox + 0.5f) / outW, 0f, dx, dy, timePhase, waveCount, colWaves, ox * 3, false);
        }

        Parallel.For(0, outH, oy =>
        {
            // Output pixel centres in the game's uv (v up), so a reduced-size
            // pass samples the same field the full-size one would.
            float v = 1f - (oy + 0.5f) / outH;
            int outRow = oy * outStride;
            Span<float> w = stackalloc float[3];
            for (int ox = 0; ox < outW; ox++)
            {
                float u = (ox + 0.5f) / outW;
                float sinSlide, sinCross1, sinCross2;
                if (byRow) { sinSlide = rowWaves![oy * 3]; sinCross1 = rowWaves[oy * 3 + 1]; sinCross2 = rowWaves[oy * 3 + 2]; }
                else if (byColumn) { sinSlide = colWaves![ox * 3]; sinCross1 = colWaves[ox * 3 + 1]; sinCross2 = colWaves[ox * 3 + 2]; }
                else
                {
                    WavesAt(u * dx + v * dy, timePhase, waveCount, w);
                    sinSlide = w[0]; sinCross1 = w[1]; sinCross2 = w[2];
                }
                Shade(baseTex, srcW, srcH, maskTex, maskW, maskH, u, v, variation, strength, compressionRatio, dx, dy,
                      sinSlide, sinCross1, sinCross2, output, outRow + ox * 4);
            }
        });
    }

    /// <summary>
    /// The colour the program draws at one point of the art - uv (<paramref name="u"/>,
    /// <paramref name="v"/>), v up - into <paramref name="output"/> at
    /// <paramref name="at"/> (BGRA): what <see cref="Render"/> writes at an output
    /// pixel's centre, for any point - a pixel of the game's screen over the art,
    /// wherever its quad lands (InGameSuiteTests.S10).
    /// </summary>
    public static void At(byte[] baseTex, int srcW, int srcH, byte[] maskTex, int maskW, int maskH,
                          in Settings s, float time, float u, float v, byte[] output, int at)
    {
        float timePhase = time * s.Speed + s.PhaseOffset;
        float dx = s.DirX, dy = s.DirY;
        float len2 = dx * dx + dy * dy;
        if (MathF.Sqrt(len2) < 0.1f) { dx = 0f; dy = 1f; }
        else { float inv = 1f / MathF.Sqrt(len2); dx *= inv; dy *= inv; }
        Span<float> w = stackalloc float[3];
        WavesAt(u * dx + v * dy, timePhase, s.WaveCount, w);
        Shade(baseTex, srcW, srcH, maskTex, maskW, maskH, u, v, s.AmplitudeVariation, s.Strength, s.CompressionRatio, dx, dy,
              w[0], w[1], w[2], output, at);
    }

    /// <summary>One pixel, its three sines worked out: the amplitude from the
    /// mask, the displacement, the art read there.</summary>
    private static void Shade(byte[] baseTex, int srcW, int srcH, byte[] maskTex, int maskW, int maskH, float u, float v,
                              float variation, float strength, float compressionRatio, float dx, float dy,
                              float sinSlide, float sinCross1, float sinCross2, byte[] output, int at)
    {
        // amp = (1 + variation*sin(u*PI)) * (mask.a * strength), the mask
        // point-sampled where the pixel is.
        int mx = Math.Clamp((int)MathF.Floor(u * maskW), 0, maskW - 1);
        int my = Math.Clamp((int)MathF.Floor(v * maskH), 0, maskH - 1);
        float maskA = maskTex[(maskH - 1 - my) * maskW * 4 + mx * 4 + 3] / 255f;
        float amp = (variation * MathF.Sin(u * Pi) + 1f) * (maskA * strength);

        // As compiled: c1 = amp*sin(waveArg)*compression, slide =
        // amp*sin(t*waves + phase)*0.5, then perp*c1 + slide*dir, then
        // perp*(0.4*sin(waveArg*1.7 + phase*0.8))*amp on top.
        float c1 = amp * sinCross1 * compressionRatio;
        float slide = amp * sinSlide * 0.5f;
        float offU = -dy * c1 + slide * dx;
        float offV = dx * c1 + slide * dy;
        float c2 = sinCross2 * 0.4f;
        offU = -dy * c2 * amp + offU;
        offV = dx * c2 * amp + offV;

        // add_sat: the displaced UV is saturated, not wrapped, so the
        // edges smear rather than showing the opposite side.
        float su = Math.Clamp(u + offU, 0f, 1f);
        float sv = Math.Clamp(v + offV, 0f, 1f);

        // Bilinear, because the game imports level art with
        // FilterMode.Bilinear and because the displacement peaks at a
        // few pixels: point sampling quantises that to whole texels and
        // the art reads as juddering rather than drifting. (Rows here are
        // top-down, so the game's v is turned over to read them.)
        SampleBilinear(baseTex, srcW, srcH, srcW * 4, su, 1f - sv, output, at);
    }

    /// <summary>The three sines at t = dot(uv, dir): the slide, and the two cross
    /// waves, as the program computes their arguments.</summary>
    private static void WavesAt(float t, float timePhase, float waveCount, Span<float> into)
    {
        float waveArg = t * waveCount * Pi + -(timePhase + timePhase);
        into[0] = MathF.Sin(t * waveCount + timePhase);
        into[1] = MathF.Sin(waveArg);
        into[2] = MathF.Sin(waveArg * 1.7f + timePhase * 0.8f);
    }

    private static void Waves(float a, float b, float dx, float dy, float timePhase, float waveCount,
                              float[] into, int at, bool aIsV)
    {
        // t = dot(uv, dir) = u*dx + v*dy with the other coordinate's term zero.
        float t = aIsV ? b * dx + a * dy : a * dx + b * dy;
        Span<float> w = stackalloc float[3];
        WavesAt(t, timePhase, waveCount, w);
        into[at] = w[0]; into[at + 1] = w[1]; into[at + 2] = w[2];
    }

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
