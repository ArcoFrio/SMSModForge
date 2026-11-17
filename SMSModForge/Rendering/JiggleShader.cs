using System;
using System.Threading.Tasks;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// CPU port of the game's bust shader, <c>Sprites/JiggleSprite_FakeGI_2DLit</c>
/// (busts, and a pack's NPCs, whose material the plugin clones from a bust's),
/// its displacement and colour as compiled - the Universal2D pass's pixel
/// program, read off the game's own bytecode (1.9.1).
/// <para/>
/// Until 1.9.1 this ported the LEVEL shader's noise (a gradient noise with a
/// sine hash, timed by Noise Speed alone): the dump it was read from held both
/// shaders' programs under one name. The bust's noise is a four-octave value
/// noise, timed by Bounce Speed x Noise Speed, with a sway across it - which is
/// why the Noise channel was the one that looked least like the game.
/// Bounce, wave and the mask's alpha already matched; sampling now clamps at the
/// art's edge as the game's textures do, and output rounds as a GPU writes it.
/// GameBustShaderTests holds every step to the game's program, run by a
/// reference interpreter of its disassembly.
/// <para/>
/// Inputs are straight-alpha BGRA32 byte arrays in scanline-top-down order;
/// the output is premultiplied BGRA32, for a WPF
/// <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>.
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
    /// How many of the game's screen pixels a preview pixel is, each way: the
    /// previews draw a bust as the game does on a 1920x1080 screen or larger
    /// (1.9.1). The game's Pixel Perfect Camera (reference 960x540, Stretch
    /// Fill, pixel snapping - read from its scene) renders at the largest whole
    /// multiple of 960x540 the screen holds - x2 from 1920x1080 to 3840x2160
    /// less one, x1 only below - so a bust's 256-texel art is drawn 4 screen
    /// rows a texel. The preview's frame is the x1 picture's size
    /// (<see cref="RenderSize"/>, two rows a texel), and a pass at zoom 2 works
    /// out the four x2 pixels in each of its pixels, at their own centres, and
    /// shows their average: the game's picture at the preview's size.
    /// <para/>
    /// Drawn at one sample a pixel (x1), the Wave layer moved each pair of rows
    /// of a texel by its own amount, in whole preview pixels - steps twice the
    /// game's, which read as scanlines (the author, 2026-11-16, at every
    /// quality). 1.9.0 hid them by averaging four samples at its two highest
    /// qualities; that was in fact the game's own density, and 1.9.1 took it
    /// out for a smoothing the game does not do.
    /// </summary>
    public const int GameZoom = 2;

    /// <summary>
    /// The bust pass at the preview's fixed size: <paramref name="baseTex"/> and
    /// <paramref name="maskTex"/> are <see cref="Size"/>² BGRA, the output
    /// <see cref="RenderSize"/>². <paramref name="time"/> is the shader's
    /// <c>_Time.y</c>, in seconds; <paramref name="zoom"/> the game's screen
    /// pixels a preview pixel is, each way (<see cref="GameZoom"/>).
    /// </summary>
    public static void Render(
        byte[] baseTex, byte[] maskTex,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, int zoom = 1)
        => Render(baseTex, maskTex, Size, Size, p, tint, time, output, RenderSize, RenderSize, zoom);

    /// <summary>
    /// Size-parameterised pass. <paramref name="baseTex"/> / <paramref name="maskTex"/>
    /// are <paramref name="srcW"/>×<paramref name="srcH"/> BGRA buffers;
    /// <paramref name="output"/> is <paramref name="outW"/>×<paramref name="outH"/>.
    /// The NPC preview calls this with the pose's own (non-square) resolution.
    /// At <paramref name="zoom"/> 1 one point sample at each output pixel's
    /// centre, as the game's GPU takes one per screen pixel; at 2, the four the
    /// game takes in it on a screen twice the size, averaged (premultiplied).
    /// </summary>
    public static void Render(
        byte[] baseTex, byte[] maskTex, int srcW, int srcH,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, int outW, int outH, int zoom = 1)
    {
        if (zoom < 1) throw new ArgumentOutOfRangeException(nameof(zoom));
        Render(baseTex, maskTex, srcW, srcH, p, tint, time, output, outW, outH, new Sampling(zoom, zoom, 1));
    }

    /// <summary>
    /// How finely a bust or an NPC drawn alone is sampled (1.9.1, by preview
    /// quality - PreviewQualityDef.Sampling): <see cref="Across"/> by
    /// <see cref="Down"/> samples in each output pixel, averaged, on a grid
    /// <see cref="Step"/> output pixels apart (each sample then fills its
    /// <see cref="Step"/> x <see cref="Step"/> block). The game's own on a
    /// 1920x1080 screen or larger: 2 x 2, step 1 (<see cref="Game"/>).
    /// </summary>
    public readonly record struct Sampling(int Across, int Down, int Step)
    {
        public static readonly Sampling Game = new(GameZoom, GameZoom, 1);
        /// <summary>Samples worked out for each output pixel.</summary>
        public float PerPixel => (float)(Across * Down) / (Step * Step);
    }

    /// <summary>As the size-parameterised pass above, sampled as <paramref name="s"/> says.</summary>
    public static void Render(
        byte[] baseTex, byte[] maskTex, int srcW, int srcH,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, int outW, int outH, Sampling s)
    {
        int srcStride = srcW * 4, outStride = outW * 4;
        if (baseTex.Length != srcStride * srcH) throw new ArgumentException("baseTex bad size", nameof(baseTex));
        if (maskTex.Length != srcStride * srcH) throw new ArgumentException("maskTex bad size", nameof(maskTex));
        if (output.Length  != outStride * outH) throw new ArgumentException("output bad size",  nameof(output));
        if (s.Across < 1 || s.Down < 1 || s.Step < 1) throw new ArgumentOutOfRangeException(nameof(s));
        if (s.Step == 1) { Sampled(baseTex, maskTex, srcW, srcH, p, tint, time, output, outW, outH, s.Across, s.Down); return; }
        // A coarser grid, each of its pixels filling its block of the output.
        int cw = (outW + s.Step - 1) / s.Step, ch = (outH + s.Step - 1) / s.Step;
        var coarse = new byte[cw * ch * 4];
        Sampled(baseTex, maskTex, srcW, srcH, p, tint, time, coarse, cw, ch, s.Across, s.Down);
        Enlarge(coarse, cw, ch, output, outW, outH, s.Step);
    }

    /// <summary>Each pixel of <paramref name="small"/> into its
    /// <paramref name="step"/> x <paramref name="step"/> block of <paramref name="output"/>.</summary>
    internal static void Enlarge(byte[] small, int sw, int sh, byte[] output, int outW, int outH, int step)
    {
        // One thread: a copy this light costs less than waking the others.
        ReadOnlySpan<uint> src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(new ReadOnlySpan<byte>(small));
        Span<uint> dst = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(new Span<byte>(output));
        for (int y = 0; y < outH; y++)
        {
            int from = (y / step) * sw, to = y * outW;
            if (y % step != 0) { dst.Slice(to - outW, outW).CopyTo(dst.Slice(to, outW)); continue; }
            for (int x = 0; x < outW; x++) dst[to + x] = src[from + x / step];
        }
    }

    private static void Sampled(
        byte[] baseTex, byte[] maskTex, int srcW, int srcH,
        JiggleParams p, (float r, float g, float b, float a) tint, float time,
        byte[] output, int outW, int outH, int zoomX, int zoomY)
    {
        int srcStride = srcW * 4, outStride = outW * 4;

        // The game's pixels: zoomX x zoomY of them in every output pixel.
        var grid = new Grid(p, time, outW * zoomX, outH * zoomY);
        var tintLinear = Colour.TintToLinear(tint);
        float share = 1f / (zoomX * zoomY);

        Parallel.For(0, outH, y =>
        {
            int row = y * outStride;
            for (int x = 0; x < outW; x++)
            {
                float sb = 0f, sg = 0f, sr = 0f, sa = 0f;
                for (int sy = 0; sy < zoomY; sy++)
                {
                    int gy = y * zoomY + sy;
                    float v = grid.V[gy];
                    for (int sx = 0; sx < zoomX; sx++)
                    {
                        int gx = x * zoomX + sx;
                        float u = grid.U[gx];

                        // The mask is read where the pixel is, undisplaced.
                        int mx = Math.Clamp((int)MathF.Floor(u * srcW), 0, srcW - 1);
                        int my = Math.Clamp((int)MathF.Floor(v * srcH), 0, srcH - 1);
                        int mi = (srcH - 1 - my) * srcStride + mx * 4;
                        var (du, dv) = grid.Offset(gx, gy, maskTex[mi + 2] / 255f, maskTex[mi + 1] / 255f,
                                                   maskTex[mi] / 255f, maskTex[mi + 3] / 255f);

                        // The art, at the displaced uv, clamped to its edge.
                        int tx = Math.Clamp((int)MathF.Floor((u + du) * srcW), 0, srcW - 1);
                        int ty = Math.Clamp((int)MathF.Floor((v + dv) * srcH), 0, srcH - 1);
                        int ti = (srcH - 1 - ty) * srcStride + tx * 4;
                        var (pb, pg, pr, pa) = Colour.Premultiplied(baseTex[ti + 2], baseTex[ti + 1], baseTex[ti], baseTex[ti + 3], tintLinear);
                        sb += pb; sg += pg; sr += pr; sa += pa;
                    }
                }
                Colour.Store(output, row + x * 4, sb * share, sg * share, sr * share, sa * share);
            }
        });
    }

    /// <summary>
    /// One frame's displacement: the time terms worked out once, and the offset
    /// at any pixel, as the game's program computes them, in its order.
    /// </summary>
    internal readonly struct Frame
    {
        private readonly float _strength, _frequency, _noiseScale, _noiseStrength;
        private readonly float _ts, _bounce, _noiseTime, _swayTime;

        public Frame(JiggleParams p, float time)
        {
            _strength = p.Strength;
            _frequency = p.Frequency;
            _noiseScale = p.NoiseScale;
            _noiseStrength = p.NoiseStrength;
            _ts = time * p.Speed;                        // mul r1.x, _Time.y, _JiggleSpeed
            _bounce = MathF.Sin(_ts) * p.Strength;       // sincos; mul _JiggleStrength
            _noiseTime = _ts * p.NoiseSpeed;             // mul r0.x, r1.x, _NoiseSpeed
            _swayTime = _ts * 0.5f;                      // mul r0.y, r1.x, 0.5
        }

        /// <summary>The uv offset at a pixel whose mask reads (r, g, b, a), at
        /// uv (u, v), v up. The game adds it to uv before reading the art.</summary>
        public (float du, float dv) Offset(float r, float g, float b, float a, float u, float v)
        {
            // dp3 (r,g,b)·1, times a: nothing moves below 0.001.
            if (!((r + g + b) * a > 0.001f)) return (0f, 0f);

            float dy = r * _bounce;                                          // bounce, along v
            float dx = g * (MathF.Sin(v * _frequency + _ts) * _strength);    // wave, along u

            if (b > 0.01f)
            {
                float px = u * _noiseScale + _noiseTime, py = v * _noiseScale + _noiseTime;
                var (nx, ny) = Noise(px, py);
                // The sway rides on the noise's x: sin(v * 3.14159 + t * 0.5) * 0.15.
                nx = MathF.Sin(v * 3.14159f + _swayTime) * 0.15f + nx;
                dx = nx * _noiseStrength * b + dx;
                dy = ny * _noiseStrength * b + dy;
            }
            return (dx * a, dy * a);
        }
    }

    /// <summary>
    /// One frame's displacement over an output grid: <see cref="Frame.Offset"/>
    /// with everything that depends on the column alone or the row alone worked
    /// out once - the wave and sway sines a row, every octave's lattice cell and
    /// blend weight a column and a row, and the hash of every lattice corner the
    /// frame touches (a few hundred). Each from the very expression Frame uses,
    /// so a pixel's offset is the same number; GameBustShaderTests holds both to
    /// the game's program.
    /// </summary>
    internal sealed class Grid
    {
        public readonly float[] U, V;
        private readonly float _bounce, _noiseStrength;
        private readonly float[] _wave, _sway;
        private readonly Octave[] _x = new Octave[4], _y = new Octave[4];

        public Grid(JiggleParams p, float time, int outW, int outH)
            : this(p, time, Columns(outW), Rows(outH)) { }

        private static float[] Columns(int outW)
        {
            var u = new float[outW];
            for (int x = 0; x < outW; x++) u[x] = (x + 0.5f) / outW;
            return u;
        }

        private static float[] Rows(int outH)
        {
            var v = new float[outH];
            for (int y = 0; y < outH; y++) { float sy = y + 0.5f; v[y] = 1.0f - sy / outH; }
            return v;
        }

        /// <summary>Over the uv of each output column and row as given (v up;
        /// rows top-down) - a bust drawn at some height in a larger frame.</summary>
        public Grid(JiggleParams p, float time, float[] u, float[] v)
        {
            U = u;
            V = v;
            int outW = u.Length, outH = v.Length;

            float ts = time * p.Speed;
            _bounce = MathF.Sin(ts) * p.Strength;
            _noiseStrength = p.NoiseStrength;
            float swayTime = ts * 0.5f, noiseTime = ts * p.NoiseSpeed;
            _wave = new float[outH];
            _sway = new float[outH];
            for (int y = 0; y < outH; y++)
            {
                _wave[y] = MathF.Sin(V[y] * p.Frequency + ts) * p.Strength;
                _sway[y] = MathF.Sin(V[y] * 3.14159f + swayTime) * 0.15f;
            }

            var px = new float[outW];
            for (int x = 0; x < outW; x++) px[x] = U[x] * p.NoiseScale + noiseTime;
            var py = new float[outH];
            for (int y = 0; y < outH; y++) py[y] = V[y] * p.NoiseScale + noiseTime;

            _x[0] = new Octave(px, py, null);
            _x[1] = new Octave(Map(px, c => c + c), Map(py, c => c + c), null);
            _x[2] = new Octave(Map(px, c => c * 4f), Map(py, c => c * 4f), null);
            _x[3] = new Octave(Map(px, c => c * 8f), Map(py, c => c * 8f), null);
            var qx = Map(px, c => c + 100f);
            var qy = Map(py, c => c + 100f);
            _y[0] = new Octave(qx, qy, _x[0]);          // the compiler's reuse of the first octave's weights
            _y[1] = new Octave(Map(qx, c => c + c), Map(qy, c => c + c), null);
            _y[2] = new Octave(Map(qx, c => c * 4f), Map(qy, c => c * 4f), null);
            _y[3] = new Octave(Map(qx, c => c * 8f), Map(qy, c => c * 8f), null);
        }

        private static float[] Map(float[] a, Func<float, float> f)
        {
            var r = new float[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = f(a[i]);
            return r;
        }

        /// <summary>The uv offset at output pixel (x, y), whose mask reads (r, g, b, a).</summary>
        public (float du, float dv) Offset(int x, int y, float r, float g, float b, float a)
        {
            if (!((r + g + b) * a > 0.001f)) return (0f, 0f);
            float dy = r * _bounce;
            float dx = g * _wave[y];
            if (b > 0.01f)
            {
                float nx = _x[0].At(x, y) * 0.5f + _x[1].At(x, y) * 0.25f;
                nx = _x[2].At(x, y) * 0.125f + nx;
                nx = _x[3].At(x, y) * 0.0625f + nx;
                nx += -0.5f;
                float ny = _y[0].At(x, y) * 0.5f + _y[1].At(x, y) * 0.25f;
                ny = _y[2].At(x, y) * 0.125f + ny;
                ny = _y[3].At(x, y) * 0.0625f + ny;
                ny += -0.5f;
                nx = _sway[y] + nx;
                dx = nx * _noiseStrength * b + dx;
                dy = ny * _noiseStrength * b + dy;
            }
            return (dx * a, dy * a);
        }

        /// <summary>One octave of value noise over the grid: each column's and each
        /// row's lattice cell and weight, and the hash of every corner reached.</summary>
        private sealed class Octave
        {
            private readonly float[] _colCell, _colW, _rowCell, _rowW;
            private readonly float[]? _table;
            private readonly int _x0, _y0, _w;

            public Octave(float[] cols, float[] rows, Octave? weightsFrom)
            {
                _colCell = new float[cols.Length];
                _colW = new float[cols.Length];
                for (int i = 0; i < cols.Length; i++)
                {
                    _colCell[i] = MathF.Floor(cols[i]);
                    _colW[i] = weightsFrom != null ? weightsFrom._colW[i] : Weight(cols[i] - _colCell[i]);
                }
                _rowCell = new float[rows.Length];
                _rowW = new float[rows.Length];
                for (int i = 0; i < rows.Length; i++)
                {
                    _rowCell[i] = MathF.Floor(rows[i]);
                    _rowW[i] = weightsFrom != null ? weightsFrom._rowW[i] : Weight(rows[i] - _rowCell[i]);
                }

                // The corners reached: a table, unless the lattice is so far out
                // that its cells are not every whole number any more.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (float c in _colCell) { minX = Math.Min(minX, c); maxX = Math.Max(maxX, c); }
                foreach (float c in _rowCell) { minY = Math.Min(minY, c); maxY = Math.Max(maxY, c); }
                if (Math.Abs(minX) < 1e6f && Math.Abs(maxX) < 1e6f && Math.Abs(minY) < 1e6f && Math.Abs(maxY) < 1e6f
                    && (maxX - minX + 2) * (maxY - minY + 2) <= 1 << 20)
                {
                    _x0 = (int)minX; _y0 = (int)minY;
                    _w = (int)(maxX - minX) + 2;
                    int h = (int)(maxY - minY) + 2;
                    _table = new float[_w * h];
                    for (int j = 0; j < h; j++)
                        for (int i = 0; i < _w; i++)
                            _table[j * _w + i] = Hash(_x0 + i, _y0 + j);
                }
            }

            public float At(int x, int y)
            {
                float ix = _colCell[x], iy = _rowCell[y], wx = _colW[x], wy = _rowW[y];
                if (_table == null) return Value(ix, iy, wx, wy);
                int i = (int)ix - _x0, j = (int)iy - _y0;
                int k = j * _w + i;
                float a = _table[k], b = _table[k + 1], c = _table[k + _w], d = _table[k + _w + 1];
                float ab = wx * (b - a) + a;
                float cd = wx * (d - c) + c;
                return wy * (cd - ab) + ab;
            }
        }
    }

    /// <summary>
    /// The two noises the displacement takes, x and y: each four octaves of value
    /// noise (scales 1, 2, 4, 8; weights 0.5, 0.25, 0.125, 0.0625; less 0.5), the
    /// y one at p + 100. As compiled, y's first octave blends with x's first
    /// octave's weights - the compiler took frac(p + 100) for frac(p) - and so
    /// does this.
    /// </summary>
    internal static (float x, float y) Noise(float px, float py)
    {
        float ix = MathF.Floor(px), iy = MathF.Floor(py);
        float wx = Weight(px - ix), wy = Weight(py - iy);
        float n1 = Value(ix, iy, wx, wy);
        float n2 = Octave(px + px, py + py);
        float x = n1 * 0.5f + n2 * 0.25f;
        x = Octave(px * 4f, py * 4f) * 0.125f + x;
        x = Octave(px * 8f, py * 8f) * 0.0625f + x;
        x += -0.5f;

        float qx = px + 100f, qy = py + 100f;
        float m1 = Value(MathF.Floor(qx), MathF.Floor(qy), wx, wy);
        float m2 = Octave(qx + qx, qy + qy);
        float y = m1 * 0.5f + m2 * 0.25f;
        y = Octave(qx * 4f, qy * 4f) * 0.125f + y;
        y = Octave(qx * 8f, qy * 8f) * 0.0625f + y;
        y += -0.5f;
        return (x, y);
    }

    private static float Octave(float px, float py)
    {
        float ix = MathF.Floor(px), iy = MathF.Floor(py);
        return Value(ix, iy, Weight(px - ix), Weight(py - iy));
    }

    /// <summary>Smoothstep, as compiled: (3 - 2f) * f².</summary>
    private static float Weight(float f) => (-f * 2f + 3f) * (f * f);

    /// <summary>Value noise on the cell at (ix, iy), blended by the weights.</summary>
    private static float Value(float ix, float iy, float wx, float wy)
    {
        float a = Hash(ix, iy), b = Hash(ix + 1f, iy);
        float ab = wx * (b - a) + a;
        float c = Hash(ix, iy + 1f), d = Hash(ix + 1f, iy + 1f);
        float cd = wx * (d - c) + c;
        return wy * (cd - ab) + ab;
    }

    /// <summary>The game's hash of a lattice point: p3 = frac(p.xyx * 0.1031);
    /// p3 += dot(p3, p3.yzx + 33.33); frac((p3.x + p3.y) * p3.z). The dot
    /// product as the game's GPU takes it: the first term rounded, the other two
    /// added by fused multiply-adds. The hash magnifies the last bit, and with
    /// each term rounded on its own (the plain C#), whole cells of the noise
    /// moved where the game's did not (measured in the game, 2026-11-16).</summary>
    internal static float Hash(float x, float y)
    {
        float a = Frac(x * 0.1031f), b = Frac(y * 0.1031f);
        float d = MathF.FusedMultiplyAdd(a, a + 33.33f, MathF.FusedMultiplyAdd(b, a + 33.33f, a * (b + 33.33f)));
        float px = d + a, py = d + b;
        return Frac(px * (py + px));
    }

    private static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>
    /// The colour a pixel takes: the art times the tint, in linear light (the
    /// game draws in linear colour space, so a tint darkens light, not sRGB
    /// numbers), written premultiplied and rounded, as a GPU writes it. A white
    /// tint leaves the art's own numbers exactly.
    /// </summary>
    internal static class Colour
    {
        private static readonly float[] ToLinear = BuildToLinear();

        private static float[] BuildToLinear()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            }
            return t;
        }

        private static float ToSrgb(float linear)
        {
            double c = Math.Clamp(linear, 0f, 1f);
            return (float)(c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);
        }

        /// <summary>The tint's colour in linear light (its alpha as it is), or
        /// null for white, which changes nothing.</summary>
        public static (float r, float g, float b, float a)? TintToLinear((float r, float g, float b, float a) tint)
        {
            if (tint.r == 1f && tint.g == 1f && tint.b == 1f) return tint.a == 1f ? null : (1f, 1f, 1f, tint.a);
            static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            return (Lin(tint.r), Lin(tint.g), Lin(tint.b), tint.a);
        }

        public static void Write(byte[] output, int o, byte r, byte g, byte b, byte a,
                                 (float r, float g, float b, float a)? tint)
        {
            var (pb, pg, pr, pa) = Premultiplied(r, g, b, a, tint);
            Store(output, o, pb, pg, pr, pa);
        }

        /// <summary>One texel as drawn: premultiplied, 0 to 1, BGRA order.</summary>
        public static (float b, float g, float r, float a) Premultiplied(byte r, byte g, byte b, byte a,
                                                                          (float r, float g, float b, float a)? tint)
        {
            float alpha = a / 255f;
            float cr, cg, cb;
            if (tint is { } t)
            {
                alpha *= t.a;
                cr = ToSrgb(ToLinear[r] * t.r);
                cg = ToSrgb(ToLinear[g] * t.g);
                cb = ToSrgb(ToLinear[b] * t.b);
            }
            else
            {
                cr = r / 255f; cg = g / 255f; cb = b / 255f;
            }
            if (alpha <= 0f) return (0f, 0f, 0f, 0f);
            return (cb * alpha, cg * alpha, cr * alpha, alpha);
        }

        /// <summary>A premultiplied pixel (0 to 1, BGRA), rounded as a GPU writes it.</summary>
        public static void Store(byte[] output, int o, float b, float g, float r, float a)
        {
            if (a <= 0f)
            {
                output[o] = output[o + 1] = output[o + 2] = output[o + 3] = 0;
                return;
            }
            output[o]     = (byte)(b * 255f + 0.5f);
            output[o + 1] = (byte)(g * 255f + 0.5f);
            output[o + 2] = (byte)(r * 255f + 0.5f);
            output[o + 3] = (byte)(a * 255f + 0.5f);
        }
    }
}
