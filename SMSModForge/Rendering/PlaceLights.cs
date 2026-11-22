using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// A place's own 2D lights in its preview (1.9.2), as URP's 2D renderer draws
/// them in the game - the model the bust preview's room lamp was held to
/// (<see cref="GameLamp"/>, InGameSuiteTests.S6), for any set of point lights:
/// every one of the game's 144 lights in 109 levels is a point light of blend
/// style 0, and a pack's own (1.10.0) are made the same way.
/// <list type="number">
/// <item>The light texture - the game's screen at 0.4 (384x216), cleared to the
/// global light (white, 1) - gets each light's share: URP's light lookup
/// through its falloff lookup at the light's falloff intensity, times its
/// colour and intensity. It keeps 11-bit floats and truncates. The lookup, as
/// Hidden/Light2D's point light reads it (1.10.0, from the game's own program):
/// its distance channel (<c>1 - 2d/256</c>) times the inner radius multiplier
/// <c>1 / (1 - inner/outer)</c>, clamped, times the cone's
/// <c>(outer - g + [inner = 1]) / (outer - inner)</c>, clamped - <c>g</c> its
/// angle channel (<c>1 - angle from the light's down / pi</c>), the angles over
/// 360 - so a cone is centred on the light's up and turned with it.</item>
/// <item>A sprite on the level's layer is its colour times that texture, read
/// between its four nearest texels; the camera's target truncates again.</item>
/// <item>Each light's glow (its light volume, the same shader at every screen
/// pixel times its volume opacity) is added over everything.</item>
/// </list>
/// Shadows are left out: of the levels, only five have shadow casters (the
/// Club, the Stage, the Church, the gas station, the air duct). What is drawn
/// with the game's unlit material (Sprites-Default: 78 of 734 renderers, the
/// whole picture of some levels lit by lamps of intensity 1) is left as it is
/// by the light texture, the glow added over it all the same.
/// </summary>
internal sealed class PlaceLights
{
    /// <summary>One point light: where it is on the game's screen (960x540, pixels
    /// from the bottom left), its outer radius in those pixels, its colour times its
    /// intensity, its falloff intensity and its glow's opacity (0: no glow); its
    /// turn (degrees, anticlockwise), the inner radius multiplier (1 / (1 -
    /// inner/outer)) and its cone's angles over 360 (1, 1: no cone). A freeform
    /// or sprite light (1.10.0) has its <see cref="LightShape"/> instead of a place
    /// and radius.</summary>
    public readonly record struct Light(float X, float Y, float Radius, float R, float G, float B, float Falloff, float VolumeOpacity,
                                        float Rotation = 0f, float InnerRadiusMult = 1f, float InnerAngle = 1f, float OuterAngle = 1f,
                                        LightShape? Shape = null, bool LightsPlace = true, bool LightsCharacters = false,
                                        bool GlowsOverCharacters = false);

    public const int ScreenW = 960, ScreenH = 540;
    /// <summary>The 2D renderer's light texture scale (Renderer2DData, read in the game: 0.4).</summary>
    private const float TextureScale = 0.4f;
    /// <summary>A world unit on the game's screen at 960x540.</summary>
    public const float PixelsPerWorldUnit = 50f;

    private readonly Light[] _lights;
    private readonly int _tw, _th;
    private readonly float[] _texel;
    /// <summary>Each shaped light's glow at every pixel of the game's screen (RGB, rows from the bottom), where the game
    /// draws it: null for a round light (worked out where asked) or one with no glow.</summary>
    private readonly float[]?[] _shapedGlow;
    /// <summary>The characters' layer's light texture (1.10.0): the global light and the lights that light the
    /// characters, as the place's; null when none does (a bust takes the global light alone).</summary>
    private readonly float[]? _bustTexel;
    private readonly (float r, float g, float b) _global, _charactersGlobal;

    /// <summary>The base light the place's layer starts from (the global light, or the place's own, 1.10.0), and the
    /// characters'.</summary>
    public (float r, float g, float b) Global => _global;
    public (float r, float g, float b) CharactersGlobal => _charactersGlobal;
    private readonly FalloffLookup _falloff;
    private static readonly float[] Look = BuildLook();

    public IReadOnlyList<Light> Lights => _lights;

    /// <summary>The light texture (RGB per texel, rows from the bottom) and its size - for tests, and for the card
    /// (Gpu.GpuPlaceScene, which applies the lights as <see cref="Apply(ref float, ref float, ref float, float, float, float)"/> does).</summary>
    internal float[] Texels => _texel;
    internal (int w, int h) TextureSize => (_tw, _th);

    /// <summary>URP's light lookup's two channels and the falloff lookup these lights read, for the card.</summary>
    internal static float[] LookTable => Look;
    internal static float[] AngleTable => LookAngle;
    internal float[] FalloffTexels => _falloff.Texels;

    /// <summary>Each light's glow where it is a shaped one's (null for the others), in the lights' order - for the card.</summary>
    internal float[]?[] ShapedGlows => _shapedGlow;

    public PlaceLights(IEnumerable<Light> lights, (float r, float g, float b) global, float[]? lookupDecode)
        : this(lights, global, (1f, 1f, 1f), lookupDecode) { }

    /// <param name="charactersGlobal">The characters' base light: white 1 (the scene's global light) unless the place's
    /// own base light takes theirs too (1.10.0).</param>
    public PlaceLights(IEnumerable<Light> lights, (float r, float g, float b) global, (float r, float g, float b) charactersGlobal,
                       float[]? lookupDecode)
    {
        _charactersGlobal = charactersGlobal;
        _lights = lights.Where(l => l.Radius > 0f || l.Shape != null).ToArray();
        _global = global;
        _falloff = FalloffLookup.Of(lookupDecode);
        _tw = (int)(ScreenW * TextureScale); _th = (int)(ScreenH * TextureScale);
        _texel = new float[_tw * _th * 3];
        _shapedGlow = new float[]?[_lights.Length];
        for (int k = 0; k < _lights.Length; k++)
        {
            var l = _lights[k];
            if (l.Shape == null || l.VolumeOpacity <= 0f) continue;
            var glow = new float[ScreenW * ScreenH * 3];
            Parallel.For(0, ScreenH, y =>
            {
                for (int x = 0; x < ScreenW; x++)
                {
                    var (r, g, b) = Share(l, x + 0.5f, y + 0.5f);
                    int o = (y * ScreenW + x) * 3;
                    glow[o] = r * l.VolumeOpacity; glow[o + 1] = g * l.VolumeOpacity; glow[o + 2] = b * l.VolumeOpacity;
                }
            });
            _shapedGlow[k] = glow;
        }
        Parallel.For(0, _th, j =>
        {
            for (int i = 0; i < _tw; i++)
            {
                float sx = (i + 0.5f) * ScreenW / _tw, sy = (j + 0.5f) * ScreenH / _th;
                float r = 0f, g = 0f, b = 0f;
                foreach (var l in _lights)
                {
                    if (!l.LightsPlace) continue;
                    var (lr, lg, lb) = Share(l, sx, sy);
                    r += lr; g += lg; b += lb;
                }
                int o = (j * _tw + i) * 3;
                _texel[o] = GamePost.Small(global.r + r, 6);
                _texel[o + 1] = GamePost.Small(global.g + g, 6);
                _texel[o + 2] = GamePost.Small(global.b + b, 5);
            }
        });
        _bustTexel = BustTexels();
    }

    /// <summary>Whether any light lights the characters' layer.</summary>
    public bool LightsCharacters => _bustTexel != null;

    /// <summary>Whether a bust is lit by anything but the scene's white 1: a light on the characters, or the place's
    /// own base light taking theirs.</summary>
    public bool ChangesCharacters => _bustTexel != null || _charactersGlobal != (1f, 1f, 1f);

    private float[]? BustTexels()
    {
        if (!_lights.Any(l => l.LightsCharacters)) return null;
        var texel = new float[_tw * _th * 3];
        Parallel.For(0, _th, j =>
        {
            for (int i = 0; i < _tw; i++)
            {
                float sx = (i + 0.5f) * ScreenW / _tw, sy = (j + 0.5f) * ScreenH / _th;
                float r = 0f, g = 0f, b = 0f;
                foreach (var l in _lights)
                {
                    if (!l.LightsCharacters) continue;
                    var (lr, lg, lb) = Share(l, sx, sy);
                    r += lr; g += lg; b += lb;
                }
                int o = (j * _tw + i) * 3;
                texel[o] = GamePost.Small(_charactersGlobal.r + r, 6);
                texel[o + 1] = GamePost.Small(_charactersGlobal.g + g, 6);
                texel[o + 2] = GamePost.Small(_charactersGlobal.b + b, 5);
            }
        });
        return texel;
    }

    /// <summary>
    /// The light a bust is multiplied by at a screen point (pixels from the bottom left): its layer's light texture
    /// read between its four nearest texels, as the bust's program reads its t0 - the global light alone unless a
    /// light lights the characters (1.10.0).
    /// </summary>
    public (float r, float g, float b) CharactersLightAt(float sx, float sy)
    {
        var texel = _bustTexel;
        if (texel == null)
            return (GamePost.Small(_charactersGlobal.r, 6), GamePost.Small(_charactersGlobal.g, 6), GamePost.Small(_charactersGlobal.b, 5));
        float u = Math.Clamp(sx / ScreenW * _tw - 0.5f, 0f, _tw - 1), v = Math.Clamp(sy / ScreenH * _th - 0.5f, 0f, _th - 1);
        int i = Math.Min((int)u, _tw - 2), j = Math.Min((int)v, _th - 2);
        float fu = u - i, fv = v - j;
        int a = (j * _tw + i) * 3, b = a + 3, c = a + _tw * 3, d = c + 3;
        float T(int k) => (texel[a + k] + (texel[b + k] - texel[a + k]) * fu) * (1f - fv) + (texel[c + k] + (texel[d + k] - texel[c + k]) * fu) * fv;
        return (T(0), T(1), T(2));
    }

    /// <summary>Which lights' glows a pass adds: all of them (the place as seen), those drawn behind the characters
    /// (the place as a bust's edge glow reads it), or those drawn over them (what is added over a bust).</summary>
    public enum Glows { All, BehindCharacters, OverCharacters, None }

    /// <summary>URP's light lookup texture (half floats): 1 - 2d/256 from its centre.</summary>
    private static float[] BuildLook()
    {
        var look = new float[256 * 256];
        for (int i = 0; i < 256; i++)
            for (int j = 0; j < 256; j++)
            {
                float d = MathF.Sqrt((j - 128f) * (j - 128f) + (i - 128f) * (i - 128f));
                float r = j != 255 && i != 255 ? Math.Clamp(1f - 2f * d / 256f, 0f, 1f) : 0f;
                look[i * 256 + j] = (float)(Half)r;
            }
        return look;
    }

    private static readonly float[] LookAngle = BuildLookAngle();

    /// <summary>Its angle channel (Light2DLookupTexture): 1 - acos(down . the direction from the centre) / pi, so 1
    /// straight down, 0 straight up, 0.5 at the centre itself (a zero direction). Half floats.</summary>
    private static float[] BuildLookAngle()
    {
        var look = new float[256 * 256];
        for (int i = 0; i < 256; i++)
            for (int j = 0; j < 256; j++)
            {
                float dx = j - 128f, dy = i - 128f, len = MathF.Sqrt(dx * dx + dy * dy);
                float ny = len > 1e-5f ? dy / len : 0f;
                float g = Math.Clamp(1f - MathF.Acos(-ny) / MathF.PI, 0f, 1f);
                look[i * 256 + j] = (float)(Half)g;
            }
        return look;
    }

    /// <summary>What a light adds at a screen point, RGB: a round one its falloff (<see cref="At"/>) times its colour;
    /// a freeform one the falloff lookup at its mesh's alpha there, times its colour; a sprite one its picture there
    /// (times the picture's alpha), times its colour - as Hidden/Light2D's additive program has each.</summary>
    private (float r, float g, float b) Share(in Light l, float sx, float sy)
    {
        if (l.Shape is { } shape)
        {
            if (shape.IsPicture)
            {
                var (cr, cg, cb) = shape.Cookie(sx, sy);
                return (cr * l.R, cg * l.G, cb * l.B);
            }
            float alpha = shape.Alpha(sx, sy);
            if (alpha <= 0f) return (0f, 0f, 0f);
            float f = _falloff.At(alpha, l.Falloff);
            return (f * l.R, f * l.G, f * l.B);
        }
        float k = At(l, sx, sy);
        return (k * l.R, k * l.G, k * l.B);
    }

    /// <summary>A light's share at a screen point: the lookup in its own space (turned back by its rotation, radius 1,
    /// plus half a texel), through its inner radius and cone, then its falloff.</summary>
    private float At(in Light l, float sx, float sy)
    {
        float dx = sx - l.X, dy = sy - l.Y;
        if (l.Rotation != 0f)
        {
            float r = l.Rotation * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
            (dx, dy) = (c * dx + s * dy, -s * dx + c * dy);
        }
        float u = (dx / l.Radius + 1f) * 0.5f + 0.5f / 256f;
        float v = (dy / l.Radius + 1f) * 0.5f + 0.5f / 256f;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
        float att = Math.Clamp(GameSampling.BilinearOne(Look, 256, 256, u, v) * l.InnerRadiusMult, 0f, 1f);
        if (l.InnerAngle < 1f)
        {
            float g = GameSampling.BilinearOne(LookAngle, 256, 256, u, v);
            float num = l.OuterAngle - g, den = l.OuterAngle - l.InnerAngle;
            // The program multiplies by 1 / den: an infinity when the two angles meet, which saturates to 1 or 0.
            att *= den > 0f ? Math.Clamp(num / den, 0f, 1f) : num > 0f ? 1f : 0f;
        }
        return _falloff.At(att, l.Falloff);
    }

    /// <summary>
    /// A pixel of the camera's target (its colour, as the sprites drew it, in the target's format) at a screen point
    /// (<paramref name="sx"/>, <paramref name="sy"/>: pixels of the game's 960x540 screen from the bottom left, past
    /// its edges too): times the light texture there, truncated as the target truncates - as far as what was drawn
    /// there is lit (<paramref name="lit"/>: 1 a lit material, 0 an unlit one) - then each light's glow added.
    /// </summary>
    public void Apply(ref float r, ref float g, ref float b, float sx, float sy, float lit = 1f) => Apply(ref r, ref g, ref b, sx, sy, lit, Glows.All);

    /// <summary>As <see cref="Apply(ref float, ref float, ref float, float, float, float)"/>, adding only the glows asked for.</summary>
    public void Apply(ref float r, ref float g, ref float b, float sx, float sy, float lit, Glows glows)
    {
        float u = Math.Clamp(sx / ScreenW * _tw - 0.5f, 0f, _tw - 1), v = Math.Clamp(sy / ScreenH * _th - 0.5f, 0f, _th - 1);
        int i = Math.Min((int)u, _tw - 2), j = Math.Min((int)v, _th - 2);
        float fu = u - i, fv = v - j;
        int a = (j * _tw + i) * 3, bb = a + 3, c = a + _tw * 3, d = c + 3;
        float T(int k) => (_texel[a + k] + (_texel[bb + k] - _texel[a + k]) * fu) * (1f - fv) + (_texel[c + k] + (_texel[d + k] - _texel[c + k]) * fu) * fv;
        // What is drawn with an unlit material (lit 0: the game's Sprites-Default) the light texture leaves as it is.
        if (lit > 0f)
        {
            float lr = GamePost.Small(r * T(0), 6), lg = GamePost.Small(g * T(1), 6), lb = GamePost.Small(b * T(2), 5);
            r = lit >= 1f ? lr : r + (lr - r) * lit;
            g = lit >= 1f ? lg : g + (lg - g) * lit;
            b = lit >= 1f ? lb : b + (lb - b) * lit;
        }
        float gr = 0f, gg = 0f, gb = 0f;
        for (int k = 0; k < _lights.Length; k++)
        {
            var l = _lights[k];
            if (l.VolumeOpacity <= 0f || glows == Glows.None) continue;
            if (glows == Glows.BehindCharacters && l.GlowsOverCharacters) continue;
            if (glows == Glows.OverCharacters && !l.GlowsOverCharacters) continue;
            if (_shapedGlow[k] is { } glow)
            {
                // Drawn at the game's own screen pixels: the one this point falls in.
                int px = (int)sx, py = (int)sy;
                if (px < 0 || py < 0 || px >= ScreenW || py >= ScreenH) continue;
                int o = (py * ScreenW + px) * 3;
                gr += glow[o]; gg += glow[o + 1]; gb += glow[o + 2];
                continue;
            }
            if (l.Shape != null) continue;
            float f = At(l, sx, sy) * l.VolumeOpacity;
            gr += f * l.R; gg += f * l.G; gb += f * l.B;
        }
        if (gr > 0f || gg > 0f || gb > 0f)
        {
            r = GamePost.Small(r + gr, 6);
            g = GamePost.Small(g + gg, 6);
            b = GamePost.Small(b + gb, 5);
        }
    }

    /// <summary>
    /// A light from the catalog (a Light2D component's values): at <paramref name="screenX"/>,
    /// <paramref name="screenY"/> on the game's screen, its object turned by <paramref name="rotation"/> degrees.
    /// Null for anything but a point light, or one with no radius. URP's point light radius is in world units whatever
    /// the light's scale (measured: the PC's room's lamp, 15 units, is 750 screen pixels).
    /// </summary>
    public static Light? FromComponent(IReadOnlyDictionary<string, object?> p, float screenX, float screenY, float rotation = 0f)
        => Model.LightDef.FromCatalog(p) is { } def ? FromLight(def, screenX, screenY, rotation) : null;

    /// <summary>
    /// A light as the pack or the catalog describes it (<see cref="Model.LightDef"/>), at <paramref name="screenX"/>,
    /// <paramref name="screenY"/> on the game's screen, its object turned by <paramref name="rotation"/> degrees.
    /// Null for one the place's sprites are not lit by: one that does not light the place's layer (Default) - the
    /// PC's room's lamp of intensity 25 lights the Foreground and Photos layers alone (Tools/Scenes/level-light-layers.py)
    /// - or that is not round, or has no reach. The colour goes to the program as it is, times the strength (the
    /// catalog's alpha folded into it): Light2D's colour is not decoded.
    /// </summary>
    /// <summary>A freeform or sprite light (1.10.0) with its footprint on screen, null for one the place's sprites are
    /// not lit by (it lights another layer).</summary>
    public static Light? FromShaped(Model.LightDef def, LightShape shape)
    {
        if (!(def.LightsThePlace || def.LightsCharacters) || def.IsRound) return null;
        var (cr, cg, cb) = def.Rgb();
        float k = def.Strength;
        return new Light(0f, 0f, 0f, cr * k, cg * k, cb * k, def.Falloff, def.Glow, Shape: shape,
                         LightsPlace: def.LightsThePlace, LightsCharacters: def.LightsCharacters, GlowsOverCharacters: def.GlowsOverCharacters);
    }

    public static Light? FromLight(Model.LightDef def, float screenX, float screenY, float rotation = 0f)
    {
        if (!(def.LightsThePlace || def.LightsCharacters) || !def.IsRound) return null;
        float radius = def.OuterRadius * PixelsPerWorldUnit;
        if (radius <= 0f) return null;
        var (cr, cg, cb) = def.Rgb();
        float k = def.Strength;
        float inner = Math.Clamp(def.InnerRadius / def.OuterRadius, 0f, 0.999f);
        float outerAngle = Math.Clamp(def.OuterAngle, 0f, 360f) / 360f, innerAngle = Math.Clamp(def.InnerAngle, 0f, 360f) / 360f;
        return new Light(screenX, screenY, radius, cr * k, cg * k, cb * k, def.Falloff, def.Glow,
                         rotation, 1f / (1f - inner), innerAngle, outerAngle,
                         LightsPlace: def.LightsThePlace, LightsCharacters: def.LightsCharacters, GlowsOverCharacters: def.GlowsOverCharacters);
    }
}
