using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// Beautify's bloom and anamorphic flares (<see cref="GamePost.BloomAndFlares"/>)
/// fast enough for every frame (1.9.1, the author: Ultra "as close to how
/// smoothly it runs in the actual game as possible").
/// <para/>
/// The same passes, each written out by hand from its compiled program - the
/// same reads, the same operations in the same order (a <c>mad</c> a multiply
/// then an add, as the GPU does it), the same half-float stores - so the
/// result is the generic path's to the bit (GamePostFastTests). What changes is
/// only the overhead: the generic path calls a transpiled program through a
/// delegate for every pixel, zeroes its whole register file each time and makes
/// new buffers for every frame - 90 ms a frame, 72 of them the flares' tall
/// buffers. The buffers here are kept in a <see cref="Work"/> between frames.
/// </summary>
internal static partial class GamePost
{
    /// <summary>The bloom and flare buffers one preview reuses frame to frame.</summary>
    internal sealed class Work
    {
        public Target[] Down = Array.Empty<Target>(), Up = Array.Empty<Target>(), FDown = Array.Empty<Target>(), FUp = Array.Empty<Target>(), FTemp = Array.Empty<Target>();
        public Target? Temp;
        public int ForW, ForH;
    }

    /// <summary>How many threads a pass uses at most.</summary>
    internal static ParallelOptions PostThreads = new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    private static Target Fit(Target? t, int w, int h) => t != null && t.W == w && t.H == h ? t : new Target(w, h);

    /// <summary><see cref="BloomAndFlares"/>, fast; <paramref name="work"/> keeps its buffers.</summary>
    internal static Target BloomAndFlaresFast(Target frame, Settings s, bool flares, Work work)
    {
        int levels = s.Iterations;
        float aspect = (float)frame.H / frame.W;
        int w0 = (int)(Lerp(512f, frame.W, s.BloomResolution / 10f) / 4f) * 4;
        if (work.Down.Length != levels + 1) { work.Down = new Target[levels + 1]; work.Up = new Target[levels + 1]; }
        int bw = w0;
        for (int i = 0; i <= levels; i++)
        {
            int h = Math.Max(1, (int)(bw * aspect));
            work.Down[i] = Fit(work.Down[i], bw, h); work.Up[i] = Fit(work.Up[i], bw, h);
            bw /= 2;
        }
        var down = work.Down; var up = work.Up;
        BloomLuminancePass(down[0], frame, s);
        for (int k = 0; k < levels; k++)
        {
            ResamplePass(down[k + 1], down[k]);
            work.Temp = Fit(work.Temp, down[k + 1].W, down[k + 1].H);
            BlurPass(work.Temp, down[k + 1], vertical: false);
            BlurPass(down[k + 1], work.Temp, vertical: true);
        }
        Target dest = down[levels];
        for (int i = levels; i > 0; i--)
        {
            ResampleAndCombinePass(up[i - 1], dest, down[i - 1], s.BloomSpread);
            dest = up[i - 1];
        }
        if (!flares) return dest;

        int f0 = (int)(Lerp(512f, frame.W, s.FlaresResolution / 10f) / 4f) * 4;
        float shrink = 1.7777778f * s.FlaresSpread * f0 / 512f;
        if (work.FDown.Length != levels + 1) { work.FDown = new Target[levels + 1]; work.FUp = new Target[levels + 1]; work.FTemp = new Target[levels + 1]; }
        int fw = f0;
        for (int l = 0; l <= levels; l++)
        {
            int across = Math.Max(1, (int)(fw / shrink));
            int w = s.FlaresVertical ? f0 : across, h = s.FlaresVertical ? across : f0;
            work.FDown[l] = Fit(work.FDown[l], w, h); work.FUp[l] = Fit(work.FUp[l], w, h); work.FTemp[l] = Fit(work.FTemp[l], w, h);
            fw /= 2;
        }
        var fdown = work.FDown; var fup = work.FUp; var ftemp = work.FTemp;
        // BlurOne blurs into a new target that takes the old one's place: here
        // the two of a level swap.
        FlaresLuminancePass(ftemp[0], frame, s);
        BlurPass(fdown[0], ftemp[0], s.FlaresVertical);
        for (int n = 0; n < levels; n++)
        {
            ResamplePass(ftemp[n + 1], fdown[n]);
            BlurPass(fdown[n + 1], ftemp[n + 1], s.FlaresVertical);
        }
        Target fdest = fdown[levels];
        for (int i = levels; i > 0; i--)
        {
            if (i == 1) FlaresResamplePass(fup[i - 1], fdest, fdown[i], s);
            else ResampleAndCombinePass(fup[i - 1], fdest, fdown[i], s.BloomSpread);
            fdest = fup[i - 1];
        }
        CombinePass(down[0], fdest, dest);
        return down[0];
    }

    /// <summary>
    /// A whole screen through the game's look (the place preview's, 1.9.1): the
    /// picture <paramref name="inputPbgra"/> (premultiplied 8-bit sRGB, rows from
    /// the top, over black: the camera's clear) read as the game's GPU reads
    /// sRGB, into the camera's target, then the parts asked for, out as the
    /// screen shows it (BGRA, rows from the top). Without the colour pass the
    /// bloom is added as Beautify's final pass adds it, by its intensity.
    /// </summary>
    /// <summary>
    /// The game's look over a VIEW of the screen (the place preview zoomed in,
    /// 1.9.1, the author: "the zoom on the Places preview is not wired correctly,
    /// and even some art from the pack itself looks low res when zoomed in"):
    /// the bloom worked out from the whole screen at the game's size
    /// (<paramref name="screenPbgra"/> into <paramref name="frame"/>, only when
    /// the bloom is drawn), the rest - Beautify's compose and URP's grading, which
    /// read the picture at their own pixel - at every pixel of the view
    /// (<paramref name="viewPbgra"/>, <paramref name="vw"/> x <paramref name="vh"/>,
    /// rows from the top), whose place on the screen is uv = (ax x + bx, ay y + by)
    /// at the pixel's centre (v up; past the screen's edge too). So the picture
    /// keeps the view's own resolution, whatever the zoom.
    /// </summary>
    internal static void LookOverView(byte[]? screenPbgra, Target frame, byte[] viewPbgra, int vw, int vh, float ax, float bx, float ay, float by,
                                      Settings s, float[] table, float[] decode, BackdropRender.PostParts parts, Work work, byte[] output)
    {
        Target? bloom = null;
        if (parts.Bloom && screenPbgra != null)
        {
            int w = frame.W, h = frame.H;
            Parallel.For(0, h, PostThreads, y =>
            {
                int src = (h - 1 - y) * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int i = src + x * 4, o = (y * w + x) * 4;
                    frame.Px[o] = decode[screenPbgra[i + 2]]; frame.Px[o + 1] = decode[screenPbgra[i + 1]]; frame.Px[o + 2] = decode[screenPbgra[i]];
                    ToFrameFormat(frame.Px, o);
                }
            });
            bloom = BloomAndFlaresFast(frame, s, parts.Flares, work);
        }
        var cb = s.ComposeCb ??= ComposeCb(s);
        Parallel.For(0, vh, PostThreads, y =>
        {
            for (int x = 0; x < vw; x++)
            {
                int i = (y * vw + x) * 4;
                // Into the camera's target's format, as the screen's frame is.
                float r = Small(decode[viewPbgra[i + 2]], 6), g = Small(decode[viewPbgra[i + 1]], 6), b = Small(decode[viewPbgra[i]], 5);
                float u = ax * (x + 0.5f) + bx, v = ay * (y + 0.5f) + by;
                if (parts.Colour)
                {
                    var tex = new ViewTextures { R = r, G = g, B = b, Bloom = bloom ?? NoBloom };
                    var inputs = new GameInputs { v1x = u, v1y = v };
                    var reg = new GameRegisters();
                    GamePostProgram.Compose(ref reg, cb, NoCb1, ref tex, in inputs);
                    (r, g, b) = Grade(Small(reg.o0x, 6), Small(reg.o0y, 6), Small(reg.o0z, 5), s, table);
                }
                else if (bloom != null)
                {
                    var (br, bgr, bb, _) = GameSampling.Bilinear(bloom.Px, bloom.W, bloom.H, u, v);
                    r += br * s.BloomIntensity; g += bgr * s.BloomIntensity; b += bb * s.BloomIntensity;
                }
                output[i] = BackdropRender.ToSrgbByte(b); output[i + 1] = BackdropRender.ToSrgbByte(g); output[i + 2] = BackdropRender.ToSrgbByte(r); output[i + 3] = 255;
            }
        });
    }

    /// <summary>The compose's reads at a pixel of a view: the picture there (its
    /// own colour, wherever the program asks), the bloom at the screen's uv.</summary>
    private struct ViewTextures : IGameTextures
    {
        public float R, G, B;
        public Target Bloom;
        public (float, float, float, float) S0(float u, float v) => (R, G, B, 1f);
        public (float, float, float, float) S1(float u, float v) => GameSampling.Bilinear(Bloom.Px, Bloom.W, Bloom.H, u, v);
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 0f);
    }

    internal static void LookOver(byte[] inputPbgra, Target frame, Settings s, float[] table, float[] decode,
                                  BackdropRender.PostParts parts, Work work, byte[] output)
    {
        int w = frame.W, h = frame.H;
        Parallel.For(0, h, PostThreads, y =>
        {
            int src = (h - 1 - y) * w * 4;
            for (int x = 0; x < w; x++)
            {
                int i = src + x * 4, o = (y * w + x) * 4;
                frame.Px[o] = decode[inputPbgra[i + 2]]; frame.Px[o + 1] = decode[inputPbgra[i + 1]]; frame.Px[o + 2] = decode[inputPbgra[i]];
                ToFrameFormat(frame.Px, o);
            }
        });
        Target? bloom = parts.Bloom ? BloomAndFlaresFast(frame, s, parts.Flares, work) : null;
        Parallel.For(0, h, PostThreads, y =>
        {
            int sy = h - 1 - y;
            for (int x = 0; x < w; x++)
            {
                float r, g, b;
                if (parts.Colour) (r, g, b) = ComposeAt(frame, bloom, s, x, sy, table);
                else
                {
                    int i = (sy * w + x) * 4;
                    r = frame.Px[i]; g = frame.Px[i + 1]; b = frame.Px[i + 2];
                    if (bloom != null)
                    {
                        var (br, bg, bb, _) = GameSampling.Bilinear(bloom.Px, bloom.W, bloom.H, (x + 0.5f) / w, (sy + 0.5f) / h);
                        r += br * s.BloomIntensity; g += bg * s.BloomIntensity; b += bb * s.BloomIntensity;
                    }
                }
                int o = (y * w + x) * 4;
                output[o] = BackdropRender.ToSrgbByte(b); output[o + 1] = BackdropRender.ToSrgbByte(g); output[o + 2] = BackdropRender.ToSrgbByte(r); output[o + 3] = 255;
            }
        });
    }

    // ── The passes (Hidden/Kronnect/Beautify, the programs named) ─────────

    /// <summary>A bilinear read, exactly <see cref="GameSampling.Bilinear"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Read(Target t, float u, float v, out float r, out float g, out float b, out float a)
    {
        int w = t.W, h = t.H;
        float fx = u * w - 0.5f, fy = v * h - 0.5f;
        float x0 = MathF.Floor(fx), y0 = MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int xa = Clamp(x0, w), xb = Clamp(x0 + 1f, w), ya = Clamp(y0, h), yb = Clamp(y0 + 1f, h);
        var p = t.Px;
        int i00 = (ya * w + xa) * 4, i10 = (ya * w + xb) * 4, i01 = (yb * w + xa) * 4, i11 = (yb * w + xb) * 4;
        float bx = 1f - ax, by = 1f - ay;
        r = (p[i00] * bx + p[i10] * ax) * by + (p[i01] * bx + p[i11] * ax) * ay;
        g = (p[i00 + 1] * bx + p[i10 + 1] * ax) * by + (p[i01 + 1] * bx + p[i11 + 1] * ax) * ay;
        b = (p[i00 + 2] * bx + p[i10 + 2] * ax) * by + (p[i01 + 2] * bx + p[i11 + 2] * ax) * ay;
        a = (p[i00 + 3] * bx + p[i10 + 3] * ax) * by + (p[i01 + 3] * bx + p[i11 + 3] * ax) * ay;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Clamp(float i, int n) => i < 0f ? 0 : i > n - 1 ? n - 1 : (int)i;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Target t, int o, float r, float g, float b, float a)
    {
        var d = t.Px;
        d[o] = HalfRound(r); d[o + 1] = HalfRound(g); d[o + 2] = HalfRound(b); d[o + 3] = HalfRound(a);
    }

    // ── Four channels at a time ───────────────────────────────────────────
    // The same per-channel arithmetic as Read, on a 128-bit vector of RGBA:
    // each lane is an IEEE single multiply or add, as each scalar one is, so
    // the results are the same to the bit (GamePostFastTests).

    /// <summary>(float)(Half)x for every float the passes make, by its bits: the
    /// mantissa rounded to 10 bits, to nearest, ties to even. Halves' tiny and
    /// huge ends (below 2^-14, from 65504 up) go the library's way.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float HalfRound(float x)
    {
        uint b = BitConverter.SingleToUInt32Bits(x);
        uint a = b & 0x7FFFFFFFu;
        if (a == 0) return x;
        if (a < 0x38800000u || a >= 0x477FE000u) return Half(x);
        a = (a + 0x0FFFu + ((a >> 13) & 1u)) & 0xFFFFE000u;
        return BitConverter.UInt32BitsToSingle((b & 0x80000000u) | a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreV(Target t, int o, Vector128<float> c)
    {
        var d = t.Px;
        d[o] = HalfRound(c.GetElement(0)); d[o + 1] = HalfRound(c.GetElement(1)); d[o + 2] = HalfRound(c.GetElement(2)); d[o + 3] = HalfRound(c.GetElement(3));
    }


    /// <summary>p3_ps_20567: the bloom's threshold, at the pixel's centre.</summary>
    private static void BloomLuminancePass(Target dest, Target src, Settings s)
        => Parallel.For(0, dest.H, PostThreads, y => BloomLuminanceRow(dest, src, s, y));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BloomLuminanceRow(Target dest, Target src, Settings s, int y)
    {
        float max = s.BloomMaxBrightness, thr = s.BloomThreshold;
        var (tr, tg, tb, ta) = s.BloomTint;
        float v = (y + 0.5f) / dest.H;
        for (int x = 0; x < dest.W; x++)
        {
            float u = (x + 0.5f) / dest.W;
            Read(src, u, v, out float r, out float g, out float b, out _);
            r = MathF.Min(MathF.Max(r, 0f), max); g = MathF.Min(MathF.Max(g, 0f), max); b = MathF.Min(MathF.Max(b, 0f), max);
            float w = MathF.Max(MathF.Max(b, g), r);
            float dr = w * tr + -r, dg = w * tg + -g, db = w * tb + -b;
            r = ta * dr + r; g = ta * dg + g; b = ta * db + b;
            Store(dest, (y * dest.W + x) * 4, MathF.Max(r + -thr, 0f), MathF.Max(g + -thr, 0f), MathF.Max(b + -thr, 0f), w);
        }
    }

    /// <summary>p17_ps_20636: the flares' threshold.</summary>
    private static void FlaresLuminancePass(Target dest, Target src, Settings s)
        => Parallel.For(0, dest.H, PostThreads, y => FlaresLuminanceRow(dest, src, s, y));

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FlaresLuminanceRow(Target dest, Target src, Settings s, int y)
    {
        float max = s.FlaresMaxBrightness, thr = s.FlaresThreshold;
        float tiny = BitConverter.Int32BitsToSingle(0x38d1b717);
        float v = (y + 0.5f) / dest.H;
        for (int x = 0; x < dest.W; x++)
        {
            float u = (x + 0.5f) / dest.W;
            Read(src, u, v, out float r, out float g, out float b, out _);
            r = MathF.Min(MathF.Max(r, 0f), max); g = MathF.Min(MathF.Max(g, 0f), max); b = MathF.Min(MathF.Max(b, 0f), max);
            float w = MathF.Max(MathF.Max(b, g), r);
            float k = -thr * 0.5f + w;
            k = MathF.Min(MathF.Max(k, 0f), thr);
            k = k * k;
            k = k * 0.5f;
            k = k / (thr + tiny);
            k = MathF.Max(k, w + -thr);
            float f = k / MathF.Max(w, tiny);
            Store(dest, (y * dest.W + x) * 4, f * r, f * g, f * b, w);
        }
    }

    // ── Reads worked out once per column and row ─────────────────────────
    // Every read in these passes lands at the pixel's centre plus a fixed
    // offset, so where it lands across depends only on the column and where
    // it lands up and down only on the row. A Tap is ReadV's own arithmetic
    // for one axis, done once - the same floats, so the same reads.

    private readonly struct Tap
    {
        public readonly int Lo, Hi;
        public readonly float Frac;
        public Tap(int lo, int hi, float frac) { Lo = lo; Hi = hi; Frac = frac; }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Tap TapAt(float c, int size)
    {
        float f = c * size - 0.5f;
        float f0 = MathF.Floor(f);
        return new Tap(Clamp(f0, size), Clamp(f0 + 1f, size), f - f0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> ReadAt(float[] px, int w, in Tap x, in Tap y)
    {
        ref float p = ref MemoryMarshal.GetArrayDataReference(px);
        var p00 = Vector128.LoadUnsafe(ref p, (nuint)((y.Lo * w + x.Lo) * 4));
        var p10 = Vector128.LoadUnsafe(ref p, (nuint)((y.Lo * w + x.Hi) * 4));
        var p01 = Vector128.LoadUnsafe(ref p, (nuint)((y.Hi * w + x.Lo) * 4));
        var p11 = Vector128.LoadUnsafe(ref p, (nuint)((y.Hi * w + x.Hi) * 4));
        float bx = 1f - x.Frac, by = 1f - y.Frac;
        return (p00 * bx + p10 * x.Frac) * by + (p01 * bx + p11 * x.Frac) * y.Frac;
    }

    private enum Along { Centre, BlurBack, BlurNear, BlurFar, BlurFarBack, Plus, Minus }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Along, int, int), Tap[]> _columns = new();

    /// <summary>For each of <paramref name="destSize"/> pixels across, where a
    /// read <paramref name="along"/> lands in a source <paramref name="srcSize"/> wide.</summary>
    private static Tap[] Taps(Along along, int destSize, int srcSize)
        => _columns.GetOrAdd((along, destSize, srcSize), key =>
        {
            var taps = new Tap[destSize];
            for (int i = 0; i < destSize; i++) taps[i] = TapAt(Coordinate(along, i, destSize, srcSize), srcSize);
            return taps;
        });

    /// <summary>The coordinate a read lands at, as the vertex program puts it:
    /// the pixel's centre, then the offset (BlurAcross / BlurUpDown: 1.3846 and
    /// 3.2308 texels either side; Cross: one texel either side).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Coordinate(Along along, int i, int destSize, int srcSize)
    {
        float c = (i + 0.5f) / destSize;
        float t = 1f / srcSize * 1f;
        return along switch
        {
            Along.BlurBack => -t * 1.3846154f + c,
            Along.BlurNear => t * 1.3846154f + c,
            Along.BlurFarBack => -(t * 3.2307692f) + c,
            Along.BlurFar => t * 3.2307692f + c,
            Along.Plus => 1f / srcSize + c,
            Along.Minus => -(1f / srcSize) + c,
            _ => c,
        };
    }

    /// <summary>p5_ps_20593: five reads along one direction, Beautify's Gaussian.</summary>
    private static void BlurPass(Target dest, Target src, bool vertical)
    {
        int across = vertical ? dest.W : dest.H;
        Tap[] centre = Taps(Along.Centre, vertical ? dest.W : dest.H, vertical ? src.W : src.H);
        Tap[] back = Taps(Along.BlurBack, vertical ? dest.H : dest.W, vertical ? src.H : src.W);
        Tap[] near = Taps(Along.BlurNear, vertical ? dest.H : dest.W, vertical ? src.H : src.W);
        Tap[] farBack = Taps(Along.BlurFarBack, vertical ? dest.H : dest.W, vertical ? src.H : src.W);
        Tap[] far = Taps(Along.BlurFar, vertical ? dest.H : dest.W, vertical ? src.H : src.W);
        Tap[] mid = Taps(Along.Centre, vertical ? dest.H : dest.W, vertical ? src.H : src.W);
        Parallel.For(0, dest.H, PostThreads, y => BlurRow(dest, src, vertical, centre, back, near, farBack, far, mid, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void BlurRow(Target dest, Target src, bool vertical, Tap[] centre, Tap[] back, Tap[] near, Tap[] farBack, Tap[] far, Tap[] mid, int y)
    {
        float w0 = BitConverter.Int32BitsToSingle(0x3e6879c6), w1 = BitConverter.Int32BitsToSingle(0x3ea1e718), w2 = BitConverter.Int32BitsToSingle(0x3d8fe9dc);
        var px = src.Px;
        int sw = src.W;
        if (vertical)
        {
            // Across: the column's own centre; up and down: the row's five.
            Tap yb = back[y], yn = near[y], yf = farBack[y], yF = far[y], yc = mid[y];
            for (int x = 0; x < dest.W; x++)
            {
                ref readonly Tap xc = ref centre[x];
                var c0 = (ReadAt(px, sw, xc, yb) + ReadAt(px, sw, xc, yn)) * w1;
                c0 = ReadAt(px, sw, xc, yc) * w0 + c0;
                var c1 = ReadAt(px, sw, xc, yf) + ReadAt(px, sw, xc, yF);
                StoreV(dest, (y * dest.W + x) * 4, c1 * w2 + c0);
            }
        }
        else
        {
            Tap yc = centre[y];
            for (int x = 0; x < dest.W; x++)
            {
                var c0 = (ReadAt(px, sw, back[x], yc) + ReadAt(px, sw, near[x], yc)) * w1;
                c0 = ReadAt(px, sw, mid[x], yc) * w0 + c0;
                var c1 = ReadAt(px, sw, farBack[x], yc) + ReadAt(px, sw, far[x], yc);
                StoreV(dest, (y * dest.W + x) * 4, c1 * w2 + c0);
            }
        }
    }

    /// <summary>The four reads of the resample passes (Reads.Cross, in the
    /// programs' order: right, up, left, down), each weighted by 1 / (1 + its
    /// alpha): their weighted sum, one over the weights' sum, and the up read.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> CrossAt(float[] px, int w, in Tap xr, in Tap xc, in Tap xl, in Tap yc, in Tap yu, in Tap yd,
                                            out float inv, out Vector128<float> up)
    {
        var s = ReadAt(px, w, xr, yc);
        float wt = 1f / (s.GetElement(3) + 1f);
        var sum = s * wt;
        up = ReadAt(px, w, xc, yu);
        float w2 = 1f / (up.GetElement(3) + 1f);
        sum = up * w2 + sum;
        wt = wt + w2;
        s = ReadAt(px, w, xl, yc);
        w2 = 1f / (s.GetElement(3) + 1f);
        sum = s * w2 + sum;
        wt = w2 + wt;
        s = ReadAt(px, w, xc, yd);
        w2 = 1f / (s.GetElement(3) + 1f);
        sum = s * w2 + sum;
        wt = w2 + wt;
        inv = 1f / wt;
        return sum;
    }

    /// <summary>The cross's taps for a destination over a source.</summary>
    private sealed class CrossTaps
    {
        public Tap[] Right = null!, Centre = null!, Left = null!, Mid = null!, Up = null!, Down = null!;
        public static CrossTaps Of(Target dest, Target src) => new()
        {
            Right = Taps(Along.Plus, dest.W, src.W), Centre = Taps(Along.Centre, dest.W, src.W), Left = Taps(Along.Minus, dest.W, src.W),
            Mid = Taps(Along.Centre, dest.H, src.H), Up = Taps(Along.Plus, dest.H, src.H), Down = Taps(Along.Minus, dest.H, src.H),
        };
    }

    /// <summary>p8_ps_20597: down a level.</summary>
    private static void ResamplePass(Target dest, Target src)
    {
        var t = CrossTaps.Of(dest, src);
        Parallel.For(0, dest.H, PostThreads, y => ResampleRow(dest, src, t, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ResampleRow(Target dest, Target src, CrossTaps t, int y)
    {
        var px = src.Px;
        int sw = src.W;
        Tap yc = t.Mid[y], yu = t.Up[y], yd = t.Down[y];
        for (int x = 0; x < dest.W; x++)
        {
            var sum = CrossAt(px, sw, t.Right[x], t.Centre[x], t.Left[x], yc, yu, yd, out float inv, out _);
            StoreV(dest, (y * dest.W + x) * 4, sum * inv);
        }
    }

    /// <summary>p9_ps_20598: up a level, onto that level's down target by the spread.</summary>
    private static void ResampleAndCombinePass(Target dest, Target src, Target level, float spread)
    {
        var t = CrossTaps.Of(dest, src);
        Tap[] lx = Taps(Along.Centre, dest.W, level.W), ly = Taps(Along.Centre, dest.H, level.H);
        Parallel.For(0, dest.H, PostThreads, y => ResampleAndCombineRow(dest, src, level, spread, t, lx, ly, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ResampleAndCombineRow(Target dest, Target src, Target level, float spread, CrossTaps t, Tap[] lx, Tap[] ly, int y)
    {
        float k = spread * 2f + -1f, twice = spread + spread;
        bool low = 0.5f >= spread;
        var px = src.Px;
        int sw = src.W;
        Tap yc = t.Mid[y], yu = t.Up[y], yd = t.Down[y], ey = ly[y];
        for (int x = 0; x < dest.W; x++)
        {
            var sum = CrossAt(px, sw, t.Right[x], t.Centre[x], t.Left[x], yc, yu, yd, out float inv, out _);
            var e = ReadAt(level.Px, level.W, lx[x], ey);
            int o = (y * dest.W + x) * 4;
            if (low) StoreV(dest, o, (sum * inv) * twice + e);
            else
            {
                var c3 = sum * inv + e;
                var c4 = sum * inv + -c3;
                StoreV(dest, o, c4 * k + c3);
            }
        }
    }

    /// <summary>p11_ps_20623: the flares' last step up, tinted by the up read's brightness, and scaled.</summary>
    private static void FlaresResamplePass(Target dest, Target src, Target level, Settings s)
    {
        var t = CrossTaps.Of(dest, src);
        Tap[] lx = Taps(Along.Centre, dest.W, level.W), ly = Taps(Along.Centre, dest.H, level.H);
        Parallel.For(0, dest.H, PostThreads, y => FlaresResampleRow(dest, src, level, s, t, lx, ly, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void FlaresResampleRow(Target dest, Target src, Target level, Settings s, CrossTaps t, Tap[] lx, Tap[] ly, int y)
    {
        var (tr, tg, tb, ta) = s.FlaresTint;
        float intensity = s.FlaresIntensity;
        var px = src.Px;
        int sw = src.W;
        Tap yc = t.Mid[y], yu = t.Up[y], yd = t.Down[y], ey = ly[y];
        for (int x = 0; x < dest.W; x++)
        {
            var sum = CrossAt(px, sw, t.Right[x], t.Centre[x], t.Left[x], yc, yu, yd, out float inv, out var up) * inv;
            float r = sum.GetElement(0), g = sum.GetElement(1), b = sum.GetElement(2), a = sum.GetElement(3);
            float m = MathF.Max(MathF.Max(up.GetElement(2), up.GetElement(1)), up.GetElement(0));
            float dr = m * tr + -r, dg = m * tg + -g, db = m * tb + -b;
            dr = ta * dr + r; dg = ta * dg + g; db = ta * db + b;
            var e = ReadAt(level.Px, level.W, lx[x], ey);
            Store(dest, (y * dest.W + x) * 4, dr * intensity + e.GetElement(0), dg * intensity + e.GetElement(1), db * intensity + e.GetElement(2), a + e.GetElement(3));
        }
    }

    /// <summary>p12_ps_20624: the flares onto the bloom.</summary>
    private static void CombinePass(Target dest, Target a, Target b)
    {
        Tap[] ax = Taps(Along.Centre, dest.W, a.W), ay = Taps(Along.Centre, dest.H, a.H);
        Tap[] bx = Taps(Along.Centre, dest.W, b.W), by = Taps(Along.Centre, dest.H, b.H);
        Parallel.For(0, dest.H, PostThreads, y => CombineRow(dest, a, b, ax, ay, bx, by, y));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void CombineRow(Target dest, Target a, Target b, Tap[] ax, Tap[] ay, Tap[] bx, Tap[] by, int y)
    {
        Tap ya = ay[y], yb = by[y];
        for (int x = 0; x < dest.W; x++)
            StoreV(dest, (y * dest.W + x) * 4, ReadAt(a.Px, a.W, ax[x], ya) + ReadAt(b.Px, b.W, bx[x], yb));
    }
}
