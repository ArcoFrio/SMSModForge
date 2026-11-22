using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SMSModForge.Rendering;

/// <summary>
/// A shaped light's footprint on the game's screen (1.10.0): a freeform light
/// or a sprite (picture) light, as URP's 2D renderer and Hidden/Light2D draw
/// them - read from the game's own runtime and program:
/// <list type="bullet">
/// <item>Freeform: <c>LightUtility.GenerateShapeMesh</c> tessellates the path
/// with vertex alpha 1 and adds a ring offset outwards by the falloff size
/// (Clipper, round joins, alpha 0 at the outside), all in the light's object
/// space; the program reads the falloff lookup at (the alpha as drawn, the
/// falloff intensity). Between the path and the ring's outside the alpha falls
/// in a straight line with the distance from the path - exactly so along the
/// straight sides and the round corners, which Clipper draws in steps far finer
/// than a pixel. So: 1 inside, <c>1 - distance / falloff size</c> outside, both
/// in object space. (Its points are jittered by a thousandth of a unit, seeded:
/// left out.)</item>
/// <item>Sprite: the sprite's mesh in object space, the picture sampled
/// (sRGB-decoded, bilinear) at its uv and multiplied by its own alpha.</item>
/// </list>
/// Points are taken back to object space through the object's transform on
/// screen, so its turn, its scale and the level's are the game's.
/// </summary>
internal sealed class LightShape : IEquatable<LightShape>
{
    // screen = M * object + T (the game's 960x540 screen, pixels from the bottom left).
    private readonly double _a, _b, _c, _d, _tx, _ty;
    private readonly double _ia, _ib, _ic, _id;
    private readonly float[] _px = Array.Empty<float>(), _py = Array.Empty<float>();
    private readonly float _edge;
    private readonly float[]? _cookie;      // linear RGBA, rows from the bottom
    private readonly int _cw, _ch;
    private readonly float _sw, _sh;        // the sprite's size in object units
    private readonly string _key;

    /// <summary>Where on screen it can reach (pixels): nothing outside.</summary>
    public readonly float MinX, MinY, MaxX, MaxY;

    public bool IsPicture => _cookie != null;

    private LightShape(double a, double b, double c, double d, double tx, double ty, string key,
                       float[]? px, float[]? py, float edge, float[]? cookie, int cw, int ch, float sw, float sh)
    {
        _a = a; _b = b; _c = c; _d = d; _tx = tx; _ty = ty;
        double det = a * d - c * b;
        if (Math.Abs(det) < 1e-12) det = 1e-12;
        _ia = d / det; _ib = -b / det; _ic = -c / det; _id = a / det;
        _px = px ?? _px; _py = py ?? _py; _edge = edge;
        _cookie = cookie; _cw = cw; _ch = ch; _sw = sw; _sh = sh;
        _key = key;
        // Its reach: the path and the ring around it, or the sprite's rect, through the transform.
        float[] xs, ys;
        if (cookie != null) { xs = new[] { -sw / 2, sw / 2, sw / 2, -sw / 2 }; ys = new[] { -sh / 2, -sh / 2, sh / 2, sh / 2 }; }
        else
        {
            xs = _px.SelectMany(x => new[] { x - edge, x + edge }).ToArray();
            ys = _py.SelectMany(y => new[] { y - edge, y + edge }).ToArray();
        }
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var x in xs)
            foreach (var y in ys)
            {
                float sx = (float)(a * x + c * y + tx), sy = (float)(b * x + d * y + ty);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx); minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        MinX = minX - 1; MinY = minY - 1; MaxX = maxX + 1; MaxY = maxY + 1;
    }

    private static string Key(string kind, double a, double b, double c, double d, double tx, double ty, string rest)
        => string.Join(",", new[] { kind, a.ToString("R", CultureInfo.InvariantCulture), b.ToString("R", CultureInfo.InvariantCulture),
                                    c.ToString("R", CultureInfo.InvariantCulture), d.ToString("R", CultureInfo.InvariantCulture),
                                    tx.ToString("R", CultureInfo.InvariantCulture), ty.ToString("R", CultureInfo.InvariantCulture), rest });

    /// <summary>A freeform light: its path (object space, in order) and falloff size (object units), the object's
    /// transform on screen <c>screen = [a c; b d] * object + (tx, ty)</c>.</summary>
    public static LightShape Free(float[] xs, float[] ys, float edge, double a, double b, double c, double d, double tx, double ty)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < xs.Length; i++) sb.Append(xs[i].ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(ys[i].ToString("R", CultureInfo.InvariantCulture)).Append(';');
        sb.Append(edge.ToString("R", CultureInfo.InvariantCulture));
        return new LightShape(a, b, c, d, tx, ty, Key("free", a, b, c, d, tx, ty, sb.ToString()), xs, ys, Math.Max(0f, edge), null, 0, 0, 0, 0);
    }

    /// <summary>A sprite light: the picture as linear RGBA (rows from the bottom), its size in object units (its
    /// pixels over the sprite's pixels per unit), centred on the object; <paramref name="identity"/> names the
    /// picture (path and when it was written) for telling two apart.</summary>
    public static LightShape Picture(float[] linearRgba, int w, int h, float pixelsPerUnit, string identity,
                                     double a, double b, double c, double d, double tx, double ty)
        => new(a, b, c, d, tx, ty, Key("picture", a, b, c, d, tx, ty, identity + "|" + pixelsPerUnit.ToString("R", CultureInfo.InvariantCulture)),
               null, null, 0f, linearRgba, w, h, w / pixelsPerUnit, h / pixelsPerUnit);

    private (double x, double y) ToObject(float sx, float sy)
    {
        double dx = sx - _tx, dy = sy - _ty;
        return (_ia * dx + _ic * dy, _ib * dx + _id * dy);
    }

    /// <summary>A freeform light's alpha at a screen point, as its mesh carries it there: 1 inside its path, falling
    /// to 0 at the falloff size outside it.</summary>
    public float Alpha(float sx, float sy)
    {
        if (sx < MinX || sx > MaxX || sy < MinY || sy > MaxY || _px.Length < 3) return 0f;
        var (x, y) = ToObject(sx, sy);
        bool inside = false;
        double best = double.MaxValue;
        for (int i = 0, j = _px.Length - 1; i < _px.Length; j = i++)
        {
            double xi = _px[i], yi = _py[i], xj = _px[j], yj = _py[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            double ex = xj - xi, ey = yj - yi, len2 = ex * ex + ey * ey;
            double t = len2 > 0 ? Math.Clamp(((x - xi) * ex + (y - yi) * ey) / len2, 0, 1) : 0;
            double qx = xi + t * ex - x, qy = yi + t * ey - y;
            best = Math.Min(best, qx * qx + qy * qy);
        }
        if (inside) return 1f;
        if (_edge <= 0f) return 0f;
        return (float)Math.Max(0.0, 1.0 - Math.Sqrt(best) / _edge);
    }

    /// <summary>A sprite light's share at a screen point before its colour: the picture there, bilinear, times its
    /// own alpha (the program premultiplies). Nothing off the sprite.</summary>
    public (float r, float g, float b) Cookie(float sx, float sy)
    {
        if (_cookie == null || sx < MinX || sx > MaxX || sy < MinY || sy > MaxY) return (0f, 0f, 0f);
        var (x, y) = ToObject(sx, sy);
        double u = x / _sw + 0.5, v = y / _sh + 0.5;
        if (u < 0 || u > 1 || v < 0 || v > 1) return (0f, 0f, 0f);
        float fx = (float)(u * _cw - 0.5), fy = (float)(v * _ch - 0.5);
        int x0 = (int)MathF.Floor(fx), y0 = (int)MathF.Floor(fy);
        float ax = fx - x0, ay = fy - y0;
        int xa = Math.Clamp(x0, 0, _cw - 1), xb = Math.Clamp(x0 + 1, 0, _cw - 1), ya = Math.Clamp(y0, 0, _ch - 1), yb = Math.Clamp(y0 + 1, 0, _ch - 1);
        float S(int k)
        {
            float p00 = _cookie[(ya * _cw + xa) * 4 + k], p10 = _cookie[(ya * _cw + xb) * 4 + k];
            float p01 = _cookie[(yb * _cw + xa) * 4 + k], p11 = _cookie[(yb * _cw + xb) * 4 + k];
            return (p00 * (1 - ax) + p10 * ax) * (1 - ay) + (p01 * (1 - ax) + p11 * ax) * ay;
        }
        float alpha = S(3);
        return (S(0) * alpha, S(1) * alpha, S(2) * alpha);
    }

    public bool Equals(LightShape? other) => other != null && other._key == _key;
    public override bool Equals(object? obj) => obj is LightShape s && Equals(s);
    public override int GetHashCode() => _key.GetHashCode(StringComparison.Ordinal);
}
