using System;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// The bust shader's fake GI - the edge lighting a bust takes from what is
/// behind it - over a <see cref="PreviewBackdrop"/>, as the game's program
/// computes it (GameBustProgram), fast enough to run every frame:
/// <list type="number">
/// <item>Where the art's alpha is the same all round (the body's inside), the
/// rim the program would find is below its own threshold (0.0005) and nothing
/// is added - shown, not assumed: <see cref="CannotLight"/> is the program's
/// gather run on uniform alpha, which bounds every real neighbourhood (the rim
/// only rises as alpha around falls), with a margin under the threshold.</item>
/// <item>Elsewhere the program's own gather (<see cref="GameBustProgram.Gather"/>)
/// finds the rim and the outward direction.</item>
/// <item>The background blur it then takes - 86 reads of the backdrop along a
/// kernel turned to that direction - comes from a table worked out once per
/// backdrop by the program's own blur (<see cref="GameBustProgram.Blur"/>), at
/// every <see cref="TableStep"/> screen pixels and <see cref="Directions"/>
/// directions, read between them. FakeGiTests holds the table to the exact
/// blur, the result within a level.</item>
/// <item>The program's own finish (<see cref="GameBustProgram.Finish"/>):
/// saturation, brightness, tint, the rim's power, added to the colour.</item>
/// </list>
/// The background is the backdrop's room as the game hands it to the program:
/// its _CameraSortingLayerTexture, downsampled 4x by box (Renderer2D:
/// Downsampling 4xBox), in linear light.
/// </summary>
internal sealed class FakeGi
{
    public const int TableStep = 4;
    public const int Directions = 64;

    /// <summary>The farthest the gather reads from its pixel, in texels of a
    /// <see cref="JiggleShader.Size"/> art, plus one for where in its texel the
    /// pixel lies: its offsets are within the unit circle (FakeGiTests reads
    /// them), times Edge Width.</summary>
    public int Reach { get; }

    public int ScreenWidth { get; }
    public int ScreenHeight { get; }
    internal readonly float[] Cb0;
    private readonly float[] _cb1 = new float[GameBustProgram.Cb1Size];
    /// <summary>The background the program reads now (see <see cref="UseBackground"/>).</summary>
    internal float[] Background { get; private set; }
    internal readonly int BackgroundWidth, BackgroundHeight;
    private readonly bool _on;

    // Skip[a * 256 + low]: the rim cannot pass the threshold for a pixel of
    // alpha a/255 whose gather reads nothing below low/255.
    private readonly bool[] _skip = new bool[256 * 256];

    private float[]? _table;     // (row * columns + column) * (Directions + 1) + direction, then R, G, B sums
    private int _tableLeft, _tableBottom, _tableColumns, _tableRows;
    private readonly object _tableLock = new();

    /// <summary>Over a backdrop's room: its background as the game hands it to
    /// the program - its _CameraSortingLayerTexture, taken from the game.</summary>
    public FakeGi(PreviewBackdrop backdrop)
        : this(backdrop.Gi, backdrop.ScreenWidth, backdrop.ScreenHeight, backdrop.Background, backdrop.BackgroundWidth, backdrop.BackgroundHeight) { }

    private static float[] BoxedRoom(PreviewBackdrop backdrop)
    {
        int w = backdrop.ScreenWidth, h = backdrop.ScreenHeight, bw = w / 4, bh = h / 4;
        var background = new float[bw * bh * 4];
        var lin = SrgbToLinear;
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                double r = 0, g = 0, b = 0;
                for (int dy = 0; dy < 4; dy++)
                    for (int dx = 0; dx < 4; dx++)
                    {
                        int sy = h - 1 - (by * 4 + dy), sx = bx * 4 + dx;      // the room's rows run from the top
                        int i = (sy * w + sx) * 4;
                        b += lin[backdrop.Room[i]]; g += lin[backdrop.Room[i + 1]]; r += lin[backdrop.Room[i + 2]];
                    }
                int o = (by * bw + bx) * 4;
                background[o] = (float)(r / 16); background[o + 1] = (float)(g / 16); background[o + 2] = (float)(b / 16); background[o + 3] = 1f;
            }
        return background;
    }

    /// <summary>Over a background given as it is: linear RGBA, rows from the
    /// bottom, <paramref name="bw"/> by <paramref name="bh"/>, on a screen of
    /// <paramref name="w"/> by <paramref name="h"/>.</summary>
    public FakeGi(FakeGiSettings g, int w, int h, float[] background, int bw, int bh)
    {
        ScreenWidth = w; ScreenHeight = h;
        Background = background; BackgroundWidth = bw; BackgroundHeight = bh;
        Cb0 = new float[GameBustProgram.Cb0Size];
        void Set(int reg, float x, float y, float z, float wv) { Cb0[reg * 4] = x; Cb0[reg * 4 + 1] = y; Cb0[reg * 4 + 2] = z; Cb0[reg * 4 + 3] = wv; }
        Set(23, w, h, 1f + 1f / w, 1f + 1f / h);
        Set(129, 1f, 0f, 0f, 0f);
        Set(134, g.Enabled, g.Intensity, g.BlurRadius, g.BlurAmount);
        Set(135, g.EdgeWidth, g.Saturation, g.Brightness, 0f);
        Set(136, g.TintR, g.TintG, g.TintB, 1f);
        Set(137, g.RimPower, g.RimSoftness, 1f, 0f);
        _on = g.Enabled > 0.5f && g.Intensity > 0.001f;
        Reach = (int)MathF.Ceiling(g.EdgeWidth * JiggleShader.Size) + 1;

        // What uniform alpha around cannot light.
        var uniform = new UniformAlpha();
        var none = new GameInputs();
        for (int low = 0; low < 256; low++)
        {
            uniform.Alpha = low / 255f;
            for (int a = 0; a < 256; a++)
            {
                var r = new GameRegisters { r0x = 0.5f, r0y = 0.5f, r1w = a / 255f };
                GameBustProgram.Gather(ref r, Cb0, _cb1, ref uniform, in none);
                _skip[a * 256 + low] = r.r0x <= 0.0004f;
            }
        }
    }

    internal static readonly float[] SrgbToLinear = BuildSrgbToLinear();

    private static float[] BuildSrgbToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }

    private struct UniformAlpha : IGameTextures
    {
        public float Alpha;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        public (float, float, float, float) S1(float u, float v) => (0f, 0f, 0f, Alpha);
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 1f);
    }

    /// <summary>Whether the program's rim stays under its threshold for a pixel
    /// of alpha <paramref name="a"/>/255 whose gather reads nothing under
    /// <paramref name="low"/>/255 - so the fake GI adds nothing to it.</summary>
    public bool CannotLight(int a, int low) => !_on || _skip[a * 256 + low];

    // ── The art ──────────────────────────────────────────────────────────

    /// <summary>One art's alpha as the program reads it, and the lowest alpha
    /// within <see cref="Reach"/> of every texel.</summary>
    internal sealed class Art
    {
        public readonly int W, H;
        public readonly float[] Alpha;     // rows from the bottom
        public readonly byte[] Low;        // rows from the bottom

        /// <summary>The gather of each pixel of the bust as drawn at the zoom
        /// last asked for (<see cref="Keep"/>: two screen pixels a texel times
        /// the zoom - <see cref="JiggleShader.RenderSize"/> x zoom square, rows
        /// from the top) while it has not moved, for the alpha it had then - NaN
        /// for none yet. Its uv there never changes, breathing or not.</summary>
        public float[] StillRim, StillDx, StillDy, StillAlpha;
        private int _keptZoom;

        /// <summary>Keep the gathers for a bust drawn at <paramref name="zoom"/>
        /// (<see cref="BackdropRender.Body"/>): a new zoom starts them afresh.</summary>
        public void Keep(int zoom)
        {
            if (zoom == _keptZoom) return;
            int n = JiggleShader.RenderSize * zoom * JiggleShader.RenderSize * zoom;
            StillRim = new float[n]; StillDx = new float[n]; StillDy = new float[n]; StillAlpha = new float[n];
            Array.Fill(StillAlpha, float.NaN);
            _keptZoom = zoom;
        }

        public Art(byte[] bgra, int w, int h, int reach)
        {
            W = w; H = h;
            StillRim = StillDx = StillDy = StillAlpha = Array.Empty<float>();
            Keep(1);
            Alpha = new float[w * h];
            var a = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte v = bgra[((h - 1 - y) * w + x) * 4 + 3];
                    a[y * w + x] = v;
                    Alpha[y * w + x] = v / 255f;
                }
            // A square window (a superset of the gather's disc); beyond the art,
            // the edge texel, as the clamped reads take it.
            var rows = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte m = 255;
                    for (int k = -reach; k <= reach; k++) m = Math.Min(m, a[y * w + Math.Clamp(x + k, 0, w - 1)]);
                    rows[y * w + x] = m;
                }
            Low = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte m = 255;
                    for (int k = -reach; k <= reach; k++) m = Math.Min(m, rows[Math.Clamp(y + k, 0, h - 1) * w + x]);
                    Low[y * w + x] = m;
                }
        }
    }

    private struct ArtTextures : IGameTextures
    {
        public Art A;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public (float, float, float, float) S1(float u, float v)
        {
            float fx = MathF.Floor(u * A.W), fy = MathF.Floor(v * A.H);
            int x = fx < 0f ? 0 : fx > A.W - 1 ? A.W - 1 : (int)fx, y = fy < 0f ? 0 : fy > A.H - 1 ? A.H - 1 : (int)fy;
            return (0f, 0f, 0f, A.Alpha[y * A.W + x]);
        }
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 1f);
    }

    private struct BackgroundTextures : IGameTextures
    {
        public float[] Bg; public int W, H;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        public (float, float, float, float) S1(float u, float v) => (0f, 0f, 0f, 1f);
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => GameSampling.Bilinear(Bg, W, H, u, v);
    }

    // ── The blur ─────────────────────────────────────────────────────────

    /// <summary>The program's own blur at a screen point (uv, from the bottom
    /// left) for an outward direction: the three sums its finish takes
    /// (registers r0.y, r0.z, r0.w - red, green, blue).</summary>
    public (float r, float g, float b) ExactBlur(float su, float sv, float dx, float dy)
    {
        var tex = new BackgroundTextures { Bg = Background, W = BackgroundWidth, H = BackgroundHeight };
        var reg = new GameRegisters { r0z = su, r0w = sv, r2x = dx, r2y = dy };
        var none = new GameInputs();
        GameBustProgram.Blur(ref reg, Cb0, _cb1, ref tex, in none);
        return (reg.r0y, reg.r0z, reg.r0w);
    }

    /// <summary>Make the blur table over a screen rect (pixels from the bottom
    /// left), once; later calls for the same rect return at once.</summary>
    public void PrepareTable(int left, int bottom, int width, int height)
    {
        lock (_tableLock)
        {
            if (_table != null && _tableLeft == left && _tableBottom == bottom
                && _tableColumns == width / TableStep + 2 && _tableRows == height / TableStep + 2) return;
            int cols = width / TableStep + 2, rows = height / TableStep + 2;
            var table = new float[cols * rows * (Directions + 1) * 3];
            float sw = ScreenWidth, sh = ScreenHeight;
            Parallel.For(0, rows, r =>
            {
                for (int c = 0; c < cols; c++)
                {
                    float su = (left + c * TableStep) / sw, sv = (bottom + r * TableStep) / sh;
                    int at = (r * cols + c) * (Directions + 1) * 3;
                    for (int d = 0; d <= Directions; d++)
                    {
                        float dx = 0f, dy = 0f;
                        if (d < Directions) { double th = d * 2 * Math.PI / Directions; dx = (float)Math.Cos(th); dy = (float)Math.Sin(th); }
                        var (br, bg, bb) = ExactBlur(su, sv, dx, dy);
                        table[at + d * 3] = br; table[at + d * 3 + 1] = bg; table[at + d * 3 + 2] = bb;
                    }
                }
            });
            _table = table; _tableLeft = left; _tableBottom = bottom; _tableColumns = cols; _tableRows = rows;
        }
    }

    /// <summary>
    /// Read <paramref name="background"/> from now on (same size): the room as
    /// a bust's shadow leaves it, a frame at a time. The exact blur reads it;
    /// the blur table stays the room's own, without the shadow (the lower
    /// qualities' approximation).
    /// </summary>
    public void UseBackground(float[] background) => Background = background;

    /// <summary>The blur from the table, at a screen pixel's centre (pixels from
    /// the bottom left), between the four nearest points and the two nearest
    /// directions (the zero direction alone when there is none).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public (float r, float g, float b) TableBlur(float sx, float sy, float dx, float dy)
    {
        var t = _table ?? throw new InvalidOperationException("PrepareTable first");
        float fx = Math.Clamp((sx - _tableLeft) / TableStep, 0f, _tableColumns - 1.001f);
        float fy = Math.Clamp((sy - _tableBottom) / TableStep, 0f, _tableRows - 1.001f);
        int c0 = (int)fx, r0 = (int)fy;
        float ax = fx - c0, ay = fy - r0;
        int d0, d1; float ad;
        if (dx == 0f && dy == 0f) { d0 = d1 = Directions; ad = 0f; }
        else
        {
            double th = Math.Atan2(dy, dx); if (th < 0) th += 2 * Math.PI;
            double fd = th / (2 * Math.PI) * Directions;
            d0 = (int)fd % Directions; d1 = (d0 + 1) % Directions; ad = (float)(fd - Math.Floor(fd));
        }
        int stride = (Directions + 1) * 3;
        float r = 0, g = 0, b = 0;
        for (int k = 0; k < 4; k++)
        {
            int cc = c0 + (k & 1), rr = r0 + (k >> 1);
            float wgt = ((k & 1) == 1 ? ax : 1 - ax) * ((k >> 1) == 1 ? ay : 1 - ay);
            int at = (rr * _tableColumns + cc) * stride;
            int i0 = at + d0 * 3, i1 = at + d1 * 3;
            r += wgt * (t[i0] + (t[i1] - t[i0]) * ad);
            g += wgt * (t[i0 + 1] + (t[i1 + 1] - t[i0 + 1]) * ad);
            b += wgt * (t[i0 + 2] + (t[i1 + 2] - t[i0 + 2]) * ad);
        }
        return (r, g, b);
    }

    // ── A pixel ──────────────────────────────────────────────────────────

    /// <summary>
    /// The fake GI on one pixel of a bust, as the program adds it: the colour
    /// <paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/> (linear,
    /// the art times the vertex colour) and alpha <paramref name="a"/> at the
    /// displaced uv (<paramref name="u"/>, <paramref name="v"/>, v up), at screen
    /// pixel (<paramref name="sx"/>, <paramref name="sy"/>) from the bottom
    /// left. <paramref name="exact"/> takes the program's own blur instead of the
    /// table.
    /// </summary>
    public void Apply(Art art, float u, float v, float sx, float sy, ref float r, ref float g, ref float b, float a, bool exact = false)
    {
        if (!_on || !(a > 0.01f)) return;
        var (rim, dx, dy) = Gather(art, u, v, a);
        Light(rim, dx, dy, sx, sy, ref r, ref g, ref b, a, exact);
    }

    /// <summary>The program's gather alone: the rim and the outward direction
    /// for a pixel of alpha <paramref name="a"/> at the displaced uv - what
    /// depends only on the art, so a pixel that has not moved can keep it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public (float rim, float dx, float dy) Gather(Art art, float u, float v, float a)
    {
        var reg = new GameRegisters { r0x = u, r0y = v, r1w = a };
        var tex = new ArtTextures { A = art };
        var none = new GameInputs();
        GameBustProgram.Gather(ref reg, Cb0, _cb1, ref tex, in none);
        return (reg.r0x, reg.r2x, reg.r2y);
    }

    /// <summary>The rest after the gather: past the program's threshold, the
    /// blur at the screen pixel and the program's finish on the colour.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public void Light(float rim, float dx, float dy, float sx, float sy, ref float r, ref float g, ref float b, float a, bool exact = false)
    {
        if (!_on || !(a > 0.01f) || !(rim > 0.0005f)) return;
        float su = sx / ScreenWidth, sv = sy / ScreenHeight;
        var (br, bg, bb) = exact ? ExactBlur(su, sv, dx, dy) : TableBlur(sx, sy, dx, dy);
        var reg = new GameRegisters { r0x = rim, r0y = br, r0z = bg, r0w = bb, r1x = r, r1y = g, r1z = b, r1w = a };
        var tex = new ArtTextures();
        var none = new GameInputs();
        GameBustProgram.Finish(ref reg, Cb0, _cb1, ref tex, in none);
        r = reg.r1x; g = reg.r1y; b = reg.r1z;
    }

    /// <summary>Whether a pixel of alpha byte <paramref name="aByte"/> whose
    /// displaced read is texel (<paramref name="tx"/>, <paramref name="ty"/>),
    /// rows from the bottom, can take any of the fake GI - for a vertex alpha of
    /// 1 (the bound is the art's own alpha; otherwise run <see cref="Apply"/>).</summary>
    public bool CanLight(Art art, int tx, int ty, int aByte) => !CannotLight(aByte, art.Low[ty * art.W + tx]);
}
