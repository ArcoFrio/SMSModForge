using System;
using System.Threading.Tasks;
using SMSModForge.Shared;

namespace SMSModForge.Rendering;

/// <summary>
/// The bust's shadow from the room's lamp (1.9.1, the author: "Add it"), made
/// as URP's 2D renderer makes it in the game.
/// <para/>
/// A lamp with shadows on is lit through a shadow texture the size of its
/// light texture (the screen at the 2D renderer's light texture scale, 0.4:
/// 384x216). Into its red channel, keeping the larger value (blend Max):
/// <list type="bullet">
/// <item>the bust's own sprite (Hidden/Shadow2DShadowSprite): its alpha, read
/// as the sprite reads it (point), times the renderer's alpha - a bust shadows
/// itself, and less while it fades in;</item>
/// <item>the projected shadow (Hidden/ShadowProjected2D): URP's shadow mesh of
/// the caster's outline - every edge pushed away from the lamp to its shadow
/// radius, and at every point a soft wedge of the lamp's softness angle -
/// through the shader's own vertex program and its falloff lookup.</item>
/// </list>
/// The outline is <see cref="ShadowOutline"/>'s, which the plugin gives the
/// game's caster too, so this casts exactly the game's shape. The lamp's light
/// and its glow are then <c>1 - intensity x shadow</c> of themselves
/// (Hidden/Light2D: <c>col * (1 - k) + col * (1 - intensity) * k</c>), read
/// back at each screen pixel between the texture's four nearest texels.
/// </summary>
internal sealed class BustShadow
{
    /// <summary>The lamp, in screen pixels from the bottom left, and URP's
    /// numbers for its shadow - all read from the game (backdrop.json).</summary>
    internal sealed record Lamp(float X, float Y, float Radius, float Softness, float Falloff, float Intensity, float VolumeIntensity, float TextureScale);

    /// <summary>ShadowRendering.k_MaxShadowSoftnessAngle: softness 1 is 15 degrees.</summary>
    private const float MaxSoftnessDegrees = 15f;

    public readonly int W, H;
    public readonly Lamp Light;
    private readonly int _screenW, _screenH;
    private readonly float _soft, _toTexture;
    /// <summary>URP's falloff lookup (Light2DLookupTexture) at this lamp's
    /// falloff: its two nearest rows already blended.</summary>
    private readonly float[] _falloff;
    private const int FalloffWidth = FalloffLookup.Width;

    public BustShadow(Lamp light, int screenW, int screenH, float[]? lookupDecode = null)
    {
        Light = light;
        _screenW = screenW; _screenH = screenH;
        W = (int)(screenW * light.TextureScale);
        H = (int)(screenH * light.TextureScale);
        _toTexture = W / (float)screenW;
        _soft = MathF.PI / 180f * light.Softness * MaxSoftnessDegrees;
        _lookup = FalloffLookup.Of(lookupDecode);
        _falloff = _lookup.Row(light.Falloff);
    }

    private readonly FalloffLookup _lookup;

    /// <summary>URP's falloff lookup at (<paramref name="u"/>, <paramref name="v"/>):
    /// what the lamp's own light reads it with.</summary>
    internal float FalloffAt(float u, float v) => _lookup.At(u, v);

    /// <summary>The lookup at <paramref name="u"/>, bilinear, clamped.</summary>
    private float Falloff(float u)
    {
        float t = u * FalloffWidth - 0.5f;
        if (t <= 0f) return _falloff[0];
        if (t >= FalloffWidth - 1) return _falloff[FalloffWidth - 1];
        int i = (int)t;
        float w = t - i;
        return _falloff[i] + (_falloff[i + 1] - _falloff[i]) * w;
    }

    /// <summary>
    /// What one picture casts: URP's shadow mesh (ShadowUtility.
    /// GenerateShadowMesh) of its outline, in texels from the picture's centre,
    /// and the picture's alpha for the shadow it casts on itself.
    /// </summary>
    internal sealed class Caster
    {
        public readonly int ArtW, ArtH;
        /// <summary>Alpha per texel, rows from the BOTTOM.</summary>
        public readonly byte[] Alpha;
        public readonly int OutlinePoints;
        internal readonly float[] Px, Py, Ox, Oy;
        internal readonly int[] Kind;
        internal readonly int[] Tris;

        /// <param name="bgra">The picture, straight BGRA, rows from the top.</param>
        public Caster(byte[] bgra, int w, int h, float texelsPerUnit)
        {
            ArtW = w; ArtH = h;
            Alpha = new byte[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Alpha[(h - 1 - y) * w + x] = bgra[(y * w + x) * 4 + 3];
            var corners = ShadowOutline.Of(Alpha, w, h, texelsPerUnit);
            int n = corners.Length / 2;
            OutlinePoints = n;
            // The shape editor's path: edges (n-1 -> 0), (0 -> 1), ... (ShadowCaster2D.SetShadowShape).
            var v0 = new int[n]; var v1 = new int[n];
            for (int k = 0, prev = n - 1; k < n; prev = k, k++) { v0[k] = prev; v1[k] = k; }
            int count = n + 4 * n;
            Px = new float[count]; Py = new float[count]; Ox = new float[count]; Oy = new float[count];
            Kind = new int[count];
            float X(int i) => corners[2 * i] - w / 2f;
            float Y(int i) => corners[2 * i + 1] - h / 2f;
            // CalculateProjectionInfo + CalculateVertices.
            for (int i = 0; i < n; i++) { Px[i] = X(i); Py[i] = Y(i); Kind[i] = -1; }
            for (int e = 0; e < n; e++)
            {
                int a = v0[e], b = v1[e], m = n + 4 * e;
                Ox[a] = X(b); Oy[a] = Y(b);
                Set(m, a, b, 0);      // hard, from v0
                Set(m + 1, b, a, 0);  // hard, from v1
                Set(m + 2, a, b, 1);  // soft left, at v0
                Set(m + 3, a, b, 3);  // soft right, at v0
            }
            // CalculateTriangles: each edge's hard quad, then each point's soft wedge.
            Tris = new int[n * 9];
            int t = 0;
            for (int e = 0; e < n; e++)
            {
                int a = v0[e], b = v1[e], m = n + 4 * e;
                Tris[t++] = a; Tris[t++] = m; Tris[t++] = m + 1;
                Tris[t++] = m + 1; Tris[t++] = b; Tris[t++] = a;
            }
            for (int e = 0; e < n; e++)
            {
                int m = n + 4 * e;
                Tris[t++] = v0[e]; Tris[t++] = m + 2; Tris[t++] = m + 3;
            }

            void Set(int i, int at, int other, int kind)
            {
                Px[i] = X(at); Py[i] = Y(at); Ox[i] = X(other); Oy[i] = Y(other); Kind[i] = kind;
            }
        }
    }

    /// <summary>
    /// The shadow texture (W x H, rows from the bottom) for <paramref name="caster"/>
    /// with its picture's centre at screen pixel (<paramref name="originX"/>,
    /// <paramref name="originY"/>), <paramref name="pixelsPerTexel"/> screen pixels
    /// a texel, its renderer at <paramref name="rendererAlpha"/>.
    /// </summary>
    public void Draw(Caster caster, float originX, float originY, float pixelsPerTexel, float rendererAlpha, float[] shadow)
    {
        Array.Clear(shadow);
        int count = caster.Px.Length;
        var x = new float[count]; var y = new float[count]; var ax = new float[count]; var ay = new float[count];
        for (int i = 0; i < count; i++)
        {
            var (px, py, o1x, o1y) = Project(originX + caster.Px[i] * pixelsPerTexel, originY + caster.Py[i] * pixelsPerTexel,
                                              caster.Kind[i], originX + caster.Ox[i] * pixelsPerTexel, originY + caster.Oy[i] * pixelsPerTexel);
            x[i] = Snap(px * _toTexture); y[i] = Snap(py * _toTexture); ax[i] = o1x; ay[i] = o1y;
        }
        // The sprite on itself.
        float left = originX - caster.ArtW / 2f * pixelsPerTexel, bottom = originY - caster.ArtH / 2f * pixelsPerTexel;
        float toScreen = 1f / _toTexture;
        if (rendererAlpha > 0f)
            Parallel.For(0, H, j =>
            {
                float sy = (j + 0.5f) * toScreen;
                int ty = (int)MathF.Floor((sy - bottom) / pixelsPerTexel);
                if (ty < 0 || ty >= caster.ArtH) return;
                for (int i = 0; i < W; i++)
                {
                    float sx = (i + 0.5f) * toScreen;
                    int tx = (int)MathF.Floor((sx - left) / pixelsPerTexel);
                    if (tx < 0 || tx >= caster.ArtW) continue;
                    float v = caster.Alpha[ty * caster.ArtW + tx] / 255f * rendererAlpha;
                    if (v > shadow[j * W + i]) shadow[j * W + i] = v;
                }
            });
        // The projected shadow: every triangle's texels, row by row.
        var tris = caster.Tris;
        int nt = tris.Length / 3;
        float hard = Falloff(1f);
        var tri = new Tri[nt];
        int kept = 0;
        for (int t = 0; t < nt; t++)
        {
            int a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
            if (Tri.Make(x[a], y[a], x[b], y[b], x[c], y[c], ax[a], ay[a], ax[b], ay[b], ax[c], ay[c], out var made)) tri[kept++] = made;
        }
        Parallel.For(0, H, j =>
        {
            double py = j + 0.5;
            int row = j * W;
            for (int t = 0; t < kept; t++)
            {
                ref readonly var tr = ref tri[t];
                if (py < tr.MinY || py > tr.MaxY) continue;
                if (!tr.Span(py, out double from, out double to)) continue;
                int i0 = Math.Max(0, (int)Math.Ceiling(from - 0.5)), i1 = Math.Min(W - 1, (int)Math.Floor(to - 0.5));
                if (tr.Flat)
                {
                    for (int i = i0; i <= i1; i++) if (hard > shadow[row + i]) shadow[row + i] = hard;
                    continue;
                }
                for (int i = i0; i <= i1; i++)
                {
                    double px = i + 0.5;
                    float ox = (float)(tr.Xu * px + tr.Xv * py + tr.Xc), oy = (float)(tr.Yu * px + tr.Yv * py + tr.Yc);
                    float d = MathF.Min(MathF.Max(oy, 1e-6f), 1f);
                    float r = MathF.Min(MathF.Abs(ox) / d, 1f);
                    float v = Falloff(1f - r);
                    if (v > shadow[row + i]) shadow[row + i] = v;
                }
            }
        });
        // Into the texture's red channel: an 11-bit float, truncated.
        for (int i = 0; i < shadow.Length; i++) shadow[i] = GamePost.Small(shadow[i], 6);
    }

    /// <summary>The rasterizer's grid: vertices at 1/256 of a texel.</summary>
    private static float Snap(float v) => MathF.Round(v * 256f) / 256f;

    /// <summary>
    /// Hidden/ShadowProjected2D's vertex program (p0_vs_1), in screen pixels:
    /// where a point of the shadow mesh goes, and the (x, y) its pixel program
    /// reads. <paramref name="kind"/> is the tangent's x: -1 the point itself,
    /// 0 pushed straight away from the lamp, 1 and 3 the soft wedge's edges.
    /// </summary>
    internal (float x, float y, float o1x, float o1y) Project(float px, float py, int kind, float otherX, float otherY)
    {
        // Instruction by instruction: dp2 is one multiply then a fused
        // multiply-add on the GPU; mad is a multiply and an add.
        float lx = Light.X, ly = Light.Y, radius = Light.Radius;
        float dx = px - lx, dy = py - ly;
        float len2 = MathF.FusedMultiplyAdd(dy, dy, dx * dx);
        float inv = 1f / MathF.Sqrt(len2);
        float nx = dx * inv, ny = dy * inv;
        // acos(|nx|), the shader's polynomial.
        float ax = MathF.Abs(nx);
        float poly = ax * -0.0187293f + 0.0742610f;
        poly = poly * ax + -0.2121144f;
        poly = poly * ax + 1.5707288f;
        float len = MathF.Sqrt(len2), root = MathF.Sqrt(1f - ax);
        float flip = nx < -nx ? root * poly * -2f + MathF.PI : 0f;
        float acos = poly * root + flip;
        float below = MathF.Ceiling(Math.Clamp(-ny, 0f, 1f));
        float angle = below * -acos + acos * (1f - below);
        angle = (kind - 2) * _soft + angle;
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        float isSoft = kind >= 1 ? 1f : 0f, isHard = kind == 0 ? 1f : 0f;
        float o1x = (kind == 3 ? 1f : 0f) + (kind == 1 ? -1f : 0f);
        float o1y = (kind == 3 ? 1f : 0f) + (kind == 1 ? 1f : 0f);
        float dirX = isSoft * cos + nx * isHard, dirY = isSoft * sin + ny * isHard;
        // The half-way direction between this point's and the edge's other point's.
        float qx = otherX - lx, qy = otherY - ly;
        float qi = 1f / MathF.Sqrt(MathF.FusedMultiplyAdd(qy, qy, qx * qx));
        qx = qx * qi + nx; qy = qy * qi + ny;
        float bi = 1f / MathF.Sqrt(MathF.FusedMultiplyAdd(qy, qy, qx * qx));
        qx *= bi; qy *= bi;
        float along = MathF.FusedMultiplyAdd(ny, qy, nx * qx);
        float dist = MathF.Max(len, radius / along) * isHard;
        dist = isSoft * (1f / MathF.Cos(_soft) * radius) + dist;
        float moved = MathF.Min(isSoft + isHard, 1f);
        float projX = dist * dirX + lx, projY = dist * dirY + ly;
        float stay = 1f - moved;
        return (moved * projX + stay * px, moved * projY + stay * py, o1x, o1y);
    }

    /// <summary>One triangle of the shadow mesh in texture space: its rows, the
    /// span a row's centre line crosses, and the pixel program's (x, y) as a
    /// plane over it (a triangle's attributes are linear in screen space).</summary>
    private readonly struct Tri
    {
        public readonly double X0, Y0, X1, Y1, X2, Y2, MinY, MaxY;
        public readonly double Xu, Xv, Xc, Yu, Yv, Yc;
        public readonly bool Flat;

        private Tri(double x0, double y0, double x1, double y1, double x2, double y2, float a0, float b0, float a1, float b1, float a2, float b2)
        {
            X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
            MinY = Math.Min(y0, Math.Min(y1, y2)); MaxY = Math.Max(y0, Math.Max(y1, y2));
            Flat = a0 == 0f && b0 == 0f && a1 == 0f && b1 == 0f && a2 == 0f && b2 == 0f;
            double det = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            (Xu, Xv, Xc) = Plane(a0, a1, a2);
            (Yu, Yv, Yc) = Plane(b0, b1, b2);

            (double, double, double) Plane(double f0, double f1, double f2)
            {
                double du = ((f1 - f0) * (y2 - y0) - (f2 - f0) * (y1 - y0)) / det;
                double dv = ((f2 - f0) * (x1 - x0) - (f1 - f0) * (x2 - x0)) / det;
                return (du, dv, f0 - du * x0 - dv * y0);
            }
        }

        public static bool Make(float x0, float y0, float x1, float y1, float x2, float y2, float a0, float b0, float a1, float b1, float a2, float b2, out Tri made)
        {
            made = default;
            if (!float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1) || !float.IsFinite(x2) || !float.IsFinite(y2)) return false;
            if ((double)(x1 - x0) * (y2 - y0) - (double)(x2 - x0) * (y1 - y0) == 0) return false;
            made = new Tri(x0, y0, x1, y1, x2, y2, a0, b0, a1, b1, a2, b2);
            return true;
        }

        /// <summary>Where the line at <paramref name="y"/> enters and leaves.</summary>
        public bool Span(double y, out double from, out double to)
        {
            from = double.MaxValue; to = double.MinValue;
            Edge(X0, Y0, X1, Y1, y, ref from, ref to);
            Edge(X1, Y1, X2, Y2, y, ref from, ref to);
            Edge(X2, Y2, X0, Y0, y, ref from, ref to);
            return from <= to;
        }

        private static void Edge(double xa, double ya, double xb, double yb, double y, ref double from, ref double to)
        {
            if ((y < ya && y < yb) || (y > ya && y > yb)) return;
            double x = ya == yb ? Math.Min(xa, xb) : xa + (y - ya) * (xb - xa) / (yb - ya);
            double x2 = ya == yb ? Math.Max(xa, xb) : x;
            if (x < from) from = x;
            if (x2 > to) to = x2;
        }
    }

    /// <summary>The shadow at a screen pixel's centre, as the lamp's light and
    /// its glow read it: between the texture's four nearest texels.</summary>
    public float At(float[] shadow, int sx, int sy)
    {
        float u = (sx + 0.5f) / _screenW * W - 0.5f, v = (sy + 0.5f) / _screenH * H - 0.5f;
        u = Math.Clamp(u, 0f, W - 1); v = Math.Clamp(v, 0f, H - 1);
        int i = Math.Min((int)u, W - 2), j = Math.Min((int)v, H - 2);
        float fu = u - i, fv = v - j;
        float a = shadow[j * W + i], b = shadow[j * W + i + 1], c = shadow[(j + 1) * W + i], d = shadow[(j + 1) * W + i + 1];
        return (a + (b - a) * fu) * (1f - fv) + (c + (d - c) * fu) * fv;
    }
}

/// <summary>
/// URP's falloff lookup (Light2DLookupTexture.CreateFalloffLookupTexture),
/// 2048x128: row y holds <c>pow(x, p)</c> for <c>p = ln(1 - n) / ln(n)</c>,
/// <c>n = (y + 64) / 256</c>, each a byte of an sRGB texture - read as this
/// machine's GPU decodes those bytes when the backdrop gives its table
/// (captured from the game; the textbook curve is up to 0.004 off it).
/// </summary>
internal sealed class FalloffLookup
{
    public const int Width = 2048, Height = 128;
    private readonly float[] _texel = new float[Width * Height];

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<float[], FalloffLookup> _made = new();
    private static readonly FalloffLookup _plain = new(null);

    public static FalloffLookup Of(float[]? decode) => decode == null ? _plain : _made.GetOrAdd(decode, d => new FalloffLookup(d));

    private FalloffLookup(float[]? decode)
    {
        for (int row = 0; row < Height; row++)
        {
            int i = row + 32;
            float num = (i + 32) / 256f;
            float p = MathF.Log(1f - num) / MathF.Log(num);
            for (int j = 0; j < Width; j++)
            {
                float r = MathF.Pow(j / 2048f, p);
                int b = (int)Math.Round(Math.Clamp(r, 0f, 1f) * 255f);
                float s = b / 255f;
                _texel[row * Width + j] = decode != null ? decode[b] : s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f);
            }
        }
    }

    /// <summary>The two rows around <paramref name="v"/> blended: a row to read along.</summary>
    public float[] Row(float v)
    {
        float tv = Math.Clamp(v * Height - 0.5f, 0f, Height - 1);
        int r0 = (int)MathF.Floor(tv), r1 = Math.Min(r0 + 1, Height - 1);
        float w = tv - r0;
        var row = new float[Width];
        for (int j = 0; j < Width; j++) row[j] = _texel[r0 * Width + j] * (1f - w) + _texel[r1 * Width + j] * w;
        return row;
    }

    /// <summary>Bilinear at (u, v), clamped.</summary>
    public float At(float u, float v) => GameSampling.BilinearOne(_texel, Width, Height, u, v);

    /// <summary>The table itself (rows of <see cref="Width"/>), for the card.</summary>
    internal float[] Texels => _texel;
}
