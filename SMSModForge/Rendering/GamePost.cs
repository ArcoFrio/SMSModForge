using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// The game's post-processing, as it runs over a frame (1.9.1, the author:
/// "Ultra should be the 1:1 comparison to what we see in the game"): Beautify
/// (Kronnect), the room's only volume (Core_Volume), with the effects the game
/// has on - bloom, vertical anamorphic flares, ACES fitted tonemapping at 1.3,
/// vignetting - then URP's own pass, which clamps. Its passes are the game's
/// compiled programs turned into C# (GamePostProgram.g.cs, Tools/Shaders/
/// dxbc_to_cs.py); the order, the targets' sizes and what each pass reads are
/// Beautify's own (BeautifyRendererFeature.DoBloomAndFlares, decompiled from
/// the game's URP runtime): bloom from a thresholded copy down five halvings,
/// each blurred, and back up; the flares likewise, blurred up and down only;
/// both into the final pass with the frame. Every target keeps what the game's
/// does: the frame in the camera's 11-11-10-bit floats, bloom and flares in
/// half floats.
/// <para/>
/// Images are linear RGBA floats, rows from the BOTTOM (v up).
/// </summary>
internal static partial class GamePost
{
    /// <summary>A render target: RGBA floats, rows from the bottom.</summary>
    internal sealed class Target
    {
        public readonly int W, H;
        public readonly float[] Px;
        public Target(int w, int h) { W = w; H = h; Px = new float[w * h * 4]; }
    }

    /// <summary>The settings the game's Beautify material runs with (material-dump
    /// of Hidden/Kronnect/Beautify in the player's room, 2026-11-16) - what
    /// BeautifyRendererFeature puts in its uniforms from Core_Volume.</summary>
    internal sealed class Settings
    {
        public float BloomIntensity = 0.3001f, BloomThreshold = 0.640000045f, BloomMaxBrightness = 1000f;
        public (float r, float g, float b, float a) BloomTint = (0.5f, 0.5f, 1f, 0f);
        public float BloomSpread = 0.5f;
        public float FlaresIntensity = 0.6664445f, FlaresThreshold = 0.809999943f, FlaresMaxBrightness = 1000f;
        public (float r, float g, float b, float a) FlaresTint = (0.5f, 0.5f, 1f, 0f);
        public float FlaresSpread = 0.423f;
        public bool FlaresVertical = true;
        public (float x, float y, float z, float w) FXColor = (1f, 1.3f, 1000f, 0f);
        public (float x, float y, float z, float w) ColorBoost = (1f, 1f, 0f, 0f);
        public (float x, float y, float z, float w) TintColor = (1f, 1f, 1f, 0f);
        public (float x, float y, float z, float w) Purkinje = (1f, 0.15f, 0f, 0.257000029f);
        public (float x, float y, float z, float w) Vignetting = (0f, 0f, 0f, 1f);
        public (float x, float y, float z, float w) VignettingData = (0.5f, 0.5f, 1f, 0.86f);
        public float VignettingData2 = 1f;
        /// <summary>bloomResolution and anamorphicFlaresResolution (1 of 10).</summary>
        public int BloomResolution = 1, FlaresResolution = 1;
        /// <summary>Five halvings (no turbo mode).</summary>
        public int Iterations = 5;
        /// <summary>URP's grading: the table's parameters (1/width, 1/height,
        /// height - 1) and the exposure after it.</summary>
        public (float x, float y, float z, float w) LutParams = (1f / 1024f, 1f / 32f, 31f, 1f);

        /// <summary>The final pass's uniforms, made once.</summary>
        internal float[]? ComposeCb;

        /// <summary>From post.json (backdrop-from-game.py: the game's materials'
        /// values); anything missing keeps the value above.</summary>
        public static Settings From(Newtonsoft.Json.Linq.JObject json)
        {
            var s = new Settings();
            var b = json["beautify"]?["values"];
            float[]? V(Newtonsoft.Json.Linq.JToken? t, string name) => t?[name]?.ToObject<float[]>();
            (float, float, float, float) V4(float[]? a, (float, float, float, float) d) => a is { Length: 4 } ? (a[0], a[1], a[2], a[3]) : d;
            if (V(b, "_Bloom") is { Length: 4 } bloom) { s.BloomIntensity = bloom[0]; s.BloomThreshold = bloom[3]; }
            if (V(b, "_BloomWeights2") is { Length: 4 } bw2) s.BloomMaxBrightness = bw2[2];
            s.BloomTint = V4(V(b, "_BloomTint"), s.BloomTint);
            if (V(b, "_BloomSpread") is { Length: >= 1 } spread) s.BloomSpread = spread[0];
            if (V(b, "_AFData") is { Length: 4 } af) { s.FlaresIntensity = af[0]; s.FlaresThreshold = af[1]; s.FlaresMaxBrightness = af[3]; }
            s.FlaresTint = V4(V(b, "_AFTint"), s.FlaresTint);
            s.FXColor = V4(V(b, "_FXColor"), s.FXColor);
            s.ColorBoost = V4(V(b, "_ColorBoost"), s.ColorBoost);
            s.TintColor = V4(V(b, "_TintColor"), s.TintColor);
            s.Purkinje = V4(V(b, "_Purkinje"), s.Purkinje);
            s.Vignetting = V4(V(b, "_Vignetting"), s.Vignetting);
            s.VignettingData = V4(V(b, "_VignettingData"), s.VignettingData);
            if (V(b, "_VignettingData2") is { Length: >= 1 } vd2) s.VignettingData2 = vd2[0];
            s.LutParams = V4(V(json["uberpost"]?["values"], "_Lut_Params"), s.LutParams);
            return s;
        }
    }

    // ── Storage, as the game's targets keep numbers ──────────────────────

    /// <summary>R16G16B16A16_SFloat: half floats, rounded to nearest.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Half(float x) => (float)(System.Half)x;

    /// <summary>B10G11R11_UFloatPack32: no sign, 5-bit exponent, 6 bits of
    /// mantissa for red and green, 5 for blue; rounded toward zero (measured
    /// against Beautify's output in the game: toward zero gives 86.8% of pixels
    /// the very levels, to nearest 33.6%).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Small(float x, int mantissa)
    {
        if (!(x > 0f)) return 0f;
        int bits = BitConverter.SingleToInt32Bits(x);
        int exp = ((bits >> 23) & 0xFF) - 127;
        if (exp > 15) return 65024f;                         // the largest such float
        if (exp < -14)
        {
            // Denormal in the small format: a step of 2^-14 / 2^m.
            float step = MathF.Pow(2f, -14 - mantissa);
            return MathF.Floor(x / step) * step;
        }
        int drop = 23 - mantissa;
        return BitConverter.Int32BitsToSingle(bits & ~((1 << drop) - 1));
    }

    /// <summary>The step between <see cref="Small"/>'s values at <paramref name="x"/>.</summary>
    internal static float Step(float x, int mantissa)
    {
        if (!(x > 0f)) return MathF.Pow(2f, -14 - mantissa);
        int exp = Math.Max(((BitConverter.SingleToInt32Bits(x) >> 23) & 0xFF) - 127, -14);
        return MathF.Pow(2f, exp - mantissa);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void ToFrameFormat(float[] px, int i)
    {
        px[i] = Small(px[i], 6); px[i + 1] = Small(px[i + 1], 6); px[i + 2] = Small(px[i + 2], 5); px[i + 3] = 1f;
    }

    // ── Textures for the passes ──────────────────────────────────────────

    private struct Pair : IGameTextures
    {
        public Target A, B;
        public (float, float, float, float) S0(float u, float v) => GameSampling.Bilinear(A.Px, A.W, A.H, u, v);
        public (float, float, float, float) S1(float u, float v) => GameSampling.Bilinear(B.Px, B.W, B.H, u, v);
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 0f);
    }

    /// <summary>The compose pass reads the frame at its own pixels (sampler s1,
    /// point) and bloom between them (s0, bilinear).</summary>
    private struct ComposeTextures : IGameTextures
    {
        public Target Frame, Bloom;
        public (float, float, float, float) S0(float u, float v) => GameSampling.Point(Frame.Px, Frame.W, Frame.H, u, v);
        public (float, float, float, float) S1(float u, float v) => GameSampling.Bilinear(Bloom.Px, Bloom.W, Bloom.H, u, v);
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 0f);
    }

    private delegate void Pass(ref GameRegisters r, float[] cb0, ref Pair tex, in GameInputs v);

    /// <summary>The vertex stages' outputs at a pixel of the target (uv at its
    /// centre) for a source of texel size (tx, ty): what each pass reads where.</summary>
    private enum Reads { Centre, BlurAcross, BlurUpDown, Cross }

    private static readonly float[] NoCb1 = new float[GameBustProgram.Cb1Size];

    private static void Blit(Target dest, Target src, Target? second, Reads reads, float[] cb0, Pass pass, bool half)
    {
        float tx = 1f / src.W, ty = 1f / src.H;
        const float blurScale = 1f;
        var tex = new Pair { A = src, B = second ?? src };
        Parallel.For(0, dest.H, y =>
        {
            float v = (y + 0.5f) / dest.H;
            for (int x = 0; x < dest.W; x++)
            {
                float u = (x + 0.5f) / dest.W;
                var inputs = new GameInputs { v1x = u, v1y = v, v1z = u, v1w = v };
                switch (reads)
                {
                    case Reads.BlurAcross:
                    {
                        // mul r0.x, texel.x, blurScale; mad o1.z, -r0.x, 1.3846, u;
                        // mul r0.xz, r0.x, (1.3846, 3.2308); o2.zw = (r0.x + u, 0 + v);
                        // o2.xy = (-r0.z + u, -0 + v); o3.xy = (r0.z + u, 0 + v)
                        float t = tx * blurScale;
                        inputs.v1z = -t * 1.3846154f + u;
                        float a = t * 1.3846154f, c = t * 3.2307692f;
                        inputs.v2z = a + u; inputs.v2w = 0f + v;
                        inputs.v2x = -c + u; inputs.v2y = -0f + v;
                        inputs.v3x = c + u; inputs.v3y = 0f + v;
                        break;
                    }
                    case Reads.BlurUpDown:
                    {
                        float t = ty * blurScale;
                        inputs.v1w = -t * 1.3846154f + v;
                        float a = t * 1.3846154f, c = t * 3.2307692f;
                        inputs.v2z = 0f + u; inputs.v2w = a + v;
                        inputs.v2x = -0f + u; inputs.v2y = -c + v;
                        inputs.v3x = 0f + u; inputs.v3y = c + v;
                        break;
                    }
                    case Reads.Cross:
                    {
                        // o1.w = texel.y + v; o2.zw = (texel.x + u, 0 + v);
                        // o2.xy = (-texel.x + u, -0 + v); o3.xy = (-0 + u, -texel.y + v)
                        inputs.v1w = ty + v;
                        inputs.v2z = tx + u; inputs.v2w = 0f + v;
                        inputs.v2x = -tx + u; inputs.v2y = -0f + v;
                        inputs.v3x = -0f + u; inputs.v3y = -ty + v;
                        break;
                    }
                }
                var reg = new GameRegisters();
                var t2 = tex;
                pass(ref reg, cb0, ref t2, in inputs);
                int o = (y * dest.W + x) * 4;
                var d = dest.Px;
                if (half) { d[o] = Half(reg.o0x); d[o + 1] = Half(reg.o0y); d[o + 2] = Half(reg.o0z); d[o + 3] = Half(reg.o0w); }
                else { d[o] = reg.o0x; d[o + 1] = reg.o0y; d[o + 2] = reg.o0z; d[o + 3] = reg.o0w; }
            }
        });
    }

    private static float[] Cb(params (int reg, float x, float y, float z, float w)[] values)
    {
        var cb = new float[GameBustProgram.Cb0Size + 40 * 4];
        foreach (var (reg, x, y, z, w) in values) { cb[reg * 4] = x; cb[reg * 4 + 1] = y; cb[reg * 4 + 2] = z; cb[reg * 4 + 3] = w; }
        return cb;
    }

    /// <summary>The bloom and flares Beautify hands its final pass, from the
    /// frame (BeautifyRendererFeature.DoBloomAndFlares).</summary>
    internal static Target BloomAndFlares(Target frame, Settings s, bool flares = true)
    {
        int levels = s.Iterations;
        float aspect = (float)frame.H / frame.W;
        // Bloom: from the thresholded frame down, each level blurred, then up.
        int w0 = (int)(Lerp(512f, frame.W, s.BloomResolution / 10f) / 4f) * 4;
        var down = new Target[levels + 1]; var up = new Target[levels + 1];
        int bw = w0;
        for (int i = 0; i <= levels; i++)
        {
            int h = Math.Max(1, (int)(bw * aspect));
            down[i] = new Target(bw, h); up[i] = new Target(bw, h);
            bw /= 2;
        }
        var lumCb = Cb((140, s.BloomIntensity, 0f, 0f, s.BloomThreshold), (142, 0f, 0f, s.BloomMaxBrightness, 0f),
                       (143, s.BloomTint.r, s.BloomTint.g, s.BloomTint.b, s.BloomTint.a));
        Blit(down[0], frame, null, Reads.Centre, lumCb, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.BloomLuminance(ref r, c, NoCb1, ref t, in v), half: true);
        var none = Cb();
        for (int k = 0; k < levels; k++)
        {
            Blit(down[k + 1], down[k], null, Reads.Cross, none, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.Resample(ref r, c, NoCb1, ref t, in v), half: true);
            BlurBoth(down[k + 1]);
        }
        var spreadCb = Cb((149, 0f, 0f, 0f, s.BloomSpread));
        Target dest = down[levels];
        for (int i = levels; i > 0; i--)
        {
            Blit(up[i - 1], dest, down[i - 1], Reads.Cross, spreadCb, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.ResampleAndCombine(ref r, c, NoCb1, ref t, in v), half: true);
            dest = up[i - 1];
        }

        if (!flares) return dest;

        // The flares: vertical, the same width at every level, each blurred up and down.
        int f0 = (int)(Lerp(512f, frame.W, s.FlaresResolution / 10f) / 4f) * 4;
        float shrink = 1.7777778f * s.FlaresSpread * f0 / 512f;
        var fdown = new Target[levels + 1]; var fup = new Target[levels + 1];
        int fw = f0;
        for (int l = 0; l <= levels; l++)
        {
            int across = Math.Max(1, (int)(fw / shrink));
            int w = s.FlaresVertical ? f0 : across, h = s.FlaresVertical ? across : f0;
            fdown[l] = new Target(w, h); fup[l] = new Target(w, h);
            fw /= 2;
        }
        var flCb = Cb((152, s.FlaresIntensity, s.FlaresThreshold, 0f, s.FlaresMaxBrightness));
        Blit(fdown[0], frame, null, Reads.Centre, flCb, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.FlaresLuminance(ref r, c, NoCb1, ref t, in v), half: true);
        fdown[0] = BlurOne(fdown[0], s.FlaresVertical);
        for (int n = 0; n < levels; n++)
        {
            Blit(fdown[n + 1], fdown[n], null, Reads.Cross, none, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.Resample(ref r, c, NoCb1, ref t, in v), half: true);
            fdown[n + 1] = BlurOne(fdown[n + 1], s.FlaresVertical);
        }
        var frCb = Cb((150, s.FlaresTint.r, s.FlaresTint.g, s.FlaresTint.b, s.FlaresTint.a), (152, s.FlaresIntensity, s.FlaresThreshold, 0f, s.FlaresMaxBrightness),
                      (149, 0f, 0f, 0f, s.BloomSpread));
        Target fdest = fdown[levels];
        for (int i = levels; i > 0; i--)
        {
            // _BloomTex is this level's down target; the last step tints and scales.
            if (i == 1)
                Blit(fup[i - 1], fdest, fdown[i], Reads.Cross, frCb, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.FlaresResample(ref r, c, NoCb1, ref t, in v), half: true);
            else
                Blit(fup[i - 1], fdest, fdown[i], Reads.Cross, spreadCb, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.ResampleAndCombine(ref r, c, NoCb1, ref t, in v), half: true);
            fdest = fup[i - 1];
        }
        // BlendOneOne: the flares onto the bloom, into the bloom's top down target.
        Blit(down[0], fdest, dest, Reads.Centre, none, (ref GameRegisters r, float[] c, ref Pair t, in GameInputs v) => GamePostProgram.Combine(ref r, c, NoCb1, ref t, in v), half: true);
        return down[0];
    }

    private static void BlurBoth(Target t)
    {
        var temp = new Target(t.W, t.H);
        var none = Cb();
        Blit(temp, t, null, Reads.BlurAcross, none, (ref GameRegisters r, float[] c, ref Pair x, in GameInputs v) => GamePostProgram.Blur(ref r, c, NoCb1, ref x, in v), half: true);
        Blit(t, temp, null, Reads.BlurUpDown, none, (ref GameRegisters r, float[] c, ref Pair x, in GameInputs v) => GamePostProgram.Blur(ref r, c, NoCb1, ref x, in v), half: true);
    }

    /// <summary>BlurThisOneDirection: into a new target, which takes the old one's place.</summary>
    private static Target BlurOne(Target t, bool vertical)
    {
        var temp = new Target(t.W, t.H);
        Blit(temp, t, null, vertical ? Reads.BlurUpDown : Reads.BlurAcross, Cb(), (ref GameRegisters r, float[] c, ref Pair x, in GameInputs v) => GamePostProgram.Blur(ref r, c, NoCb1, ref x, in v), half: true);
        return temp;
    }

    private static readonly Target NoBloom = new(1, 1);

    /// <summary>One screen pixel through the rest of the game's post-processing:
    /// Beautify's final pass (with <paramref name="bloom"/>, or none), the
    /// camera's target, then URP's pass - clamped, through its grading table,
    /// the camera's target again. Linear light, for the screen's sRGB.</summary>
    internal static (float r, float g, float b) ComposeAt(Target frame, Target? bloom, Settings s, int sx, int sy, float[] table)
    {
        var cb = s.ComposeCb ??= ComposeCb(s);
        var tex = new ComposeTextures { Frame = frame, Bloom = bloom ?? NoBloom };
        var inputs = new GameInputs { v1x = (sx + 0.5f) / frame.W, v1y = (sy + 0.5f) / frame.H };
        var reg = new GameRegisters();
        GamePostProgram.Compose(ref reg, cb, NoCb1, ref tex, in inputs);
        float r = Small(reg.o0x, 6), g = Small(reg.o0y, 6), b = Small(reg.o0z, 5);
        return Grade(r, g, b, s, table);
    }

    /// <summary>URP's pass after Beautify, as the game's runs it (low dynamic
    /// range grading, its internal table): clamped to 0..1, looked up between
    /// the table's two nearest slices (ApplyLut2D), times the exposure, into
    /// the camera's 11-11-10-bit target.</summary>
    internal static (float r, float g, float b) Grade(float r, float g, float b, Settings s, float[] table)
    {
        r = Math.Clamp(r, 0f, 1f); g = Math.Clamp(g, 0f, 1f); b = Math.Clamp(b, 0f, 1f);
        var (px, py, pz, _) = s.LutParams;
        float z = b * pz, shift = MathF.Floor(z);
        float ux = r * pz * px + px * 0.5f, uy = g * pz * py + py * 0.5f;
        ux += shift * py;
        int tw = (int)MathF.Round(1f / px), th = (int)MathF.Round(1f / py);
        var lo = GameSampling.Bilinear(table, tw, th, ux, uy);
        var hi = GameSampling.Bilinear(table, tw, th, ux + py, uy);
        float t = z - shift;
        float rr = lo.Item1 + (hi.Item1 - lo.Item1) * t, gg = lo.Item2 + (hi.Item2 - lo.Item2) * t, bb = lo.Item3 + (hi.Item3 - lo.Item3) * t;
        return (Small(rr, 6), Small(gg, 6), Small(bb, 5));
    }

    /// <summary>The final pass's constants, made once (for the graphics card's copy of it too).</summary>
    internal static float[] ComposeConstants(Settings s) => s.ComposeCb ??= ComposeCb(s);

    /// <summary>The constants of the bloom and flares passes (GpuPost runs them on the card).</summary>
    internal static float[] PassConstants(params (int reg, float x, float y, float z, float w)[] values) => Cb(values);

    private static float[] ComposeCb(Settings s) => Cb((146, s.BloomIntensity, 0f, 0f, s.BloomThreshold), (148, s.FXColor.x, s.FXColor.y, s.FXColor.z, s.FXColor.w),
        (149, s.ColorBoost.x, s.ColorBoost.y, s.ColorBoost.z, s.ColorBoost.w), (150, s.TintColor.x, s.TintColor.y, s.TintColor.z, s.TintColor.w),
        (151, s.Purkinje.x, s.Purkinje.y, s.Purkinje.z, s.Purkinje.w), (163, s.Vignetting.x, s.Vignetting.y, s.Vignetting.z, s.Vignetting.w),
        (164, s.VignettingData.x, s.VignettingData.y, s.VignettingData.z, s.VignettingData.w), (165, s.VignettingData2, 0f, 0f, 0f));

    /// <summary>The final pass over the frame, with the bloom: Beautify's output,
    /// in the camera's 11-11-10-bit floats.</summary>
    internal static Target Compose(Target frame, Target bloom, Settings s)
    {
        var cb = Cb((146, s.BloomIntensity, 0f, 0f, s.BloomThreshold), (148, s.FXColor.x, s.FXColor.y, s.FXColor.z, s.FXColor.w),
                    (149, s.ColorBoost.x, s.ColorBoost.y, s.ColorBoost.z, s.ColorBoost.w), (150, s.TintColor.x, s.TintColor.y, s.TintColor.z, s.TintColor.w),
                    (151, s.Purkinje.x, s.Purkinje.y, s.Purkinje.z, s.Purkinje.w), (163, s.Vignetting.x, s.Vignetting.y, s.Vignetting.z, s.Vignetting.w),
                    (164, s.VignettingData.x, s.VignettingData.y, s.VignettingData.z, s.VignettingData.w), (165, s.VignettingData2, 0f, 0f, 0f));
        var dest = new Target(frame.W, frame.H);
        var tex = new ComposeTextures { Frame = frame, Bloom = bloom };
        Parallel.For(0, frame.H, y =>
        {
            float v = (y + 0.5f) / frame.H;
            for (int x = 0; x < frame.W; x++)
            {
                float u = (x + 0.5f) / frame.W;
                var inputs = new GameInputs { v1x = u, v1y = v };
                var reg = new GameRegisters();
                var t = tex;
                GamePostProgram.Compose(ref reg, cb, NoCb1, ref t, in inputs);
                int o = (y * frame.W + x) * 4;
                dest.Px[o] = reg.o0x; dest.Px[o + 1] = reg.o0y; dest.Px[o + 2] = reg.o0z;
                ToFrameFormat(dest.Px, o);
            }
        });
        return dest;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
}
