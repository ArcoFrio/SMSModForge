using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// A place's own 2D lights in its preview (1.9.2), as URP's 2D renderer draws
/// them in the game - the model the bust preview's room lamp was held to
/// (<see cref="GameLamp"/>, InGameSuiteTests.S6), for any set of point lights
/// from the level catalog: every one of the game's 144 lights in 109 levels is
/// a point light of blend style 0.
/// <list type="number">
/// <item>The light texture - the game's screen at 0.4 (384x216), cleared to the
/// global light (white, 1) - gets each light's share: URP's light lookup
/// (<c>1 - 2d/256</c>) through its falloff lookup at the light's falloff
/// intensity, times its colour and intensity. It keeps 11-bit floats and
/// truncates.</item>
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
    /// intensity, its falloff intensity and its glow's opacity (0: no glow).</summary>
    public readonly record struct Light(float X, float Y, float Radius, float R, float G, float B, float Falloff, float VolumeOpacity);

    public const int ScreenW = 960, ScreenH = 540;
    /// <summary>The 2D renderer's light texture scale (Renderer2DData, read in the game: 0.4).</summary>
    private const float TextureScale = 0.4f;
    /// <summary>A world unit on the game's screen at 960x540.</summary>
    public const float PixelsPerWorldUnit = 50f;

    private readonly Light[] _lights;
    private readonly int _tw, _th;
    private readonly float[] _texel;
    private readonly FalloffLookup _falloff;
    private static readonly float[] Look = BuildLook();

    public IReadOnlyList<Light> Lights => _lights;

    /// <summary>The light texture (RGB per texel, rows from the bottom) and its size - for tests.</summary>
    internal float[] Texels => _texel;
    internal (int w, int h) TextureSize => (_tw, _th);

    public PlaceLights(IEnumerable<Light> lights, (float r, float g, float b) global, float[]? lookupDecode)
    {
        _lights = lights.Where(l => l.Radius > 0f).ToArray();
        _falloff = FalloffLookup.Of(lookupDecode);
        _tw = (int)(ScreenW * TextureScale); _th = (int)(ScreenH * TextureScale);
        _texel = new float[_tw * _th * 3];
        Parallel.For(0, _th, j =>
        {
            for (int i = 0; i < _tw; i++)
            {
                float sx = (i + 0.5f) * ScreenW / _tw, sy = (j + 0.5f) * ScreenH / _th;
                float r = 0f, g = 0f, b = 0f;
                foreach (var l in _lights)
                {
                    float f = At(l, sx, sy);
                    r += f * l.R; g += f * l.G; b += f * l.B;
                }
                int o = (j * _tw + i) * 3;
                _texel[o] = GamePost.Small(global.r + r, 6);
                _texel[o + 1] = GamePost.Small(global.g + g, 6);
                _texel[o + 2] = GamePost.Small(global.b + b, 5);
            }
        });
    }

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

    /// <summary>A light's share at a screen point: the lookup in its own space (radius 1, plus half a texel), through
    /// its falloff.</summary>
    private float At(in Light l, float sx, float sy)
    {
        float u = ((sx - l.X) / l.Radius + 1f) * 0.5f + 0.5f / 256f;
        float v = ((sy - l.Y) / l.Radius + 1f) * 0.5f + 0.5f / 256f;
        if (u < 0f || u > 1f || v < 0f || v > 1f) return 0f;
        float att = Math.Clamp(GameSampling.BilinearOne(Look, 256, 256, u, v), 0f, 1f);
        return _falloff.At(att, l.Falloff);
    }

    /// <summary>
    /// A pixel of the camera's target (its colour, as the sprites drew it, in the target's format) at a screen point
    /// (<paramref name="sx"/>, <paramref name="sy"/>: pixels of the game's 960x540 screen from the bottom left, past
    /// its edges too): times the light texture there, truncated as the target truncates - as far as what was drawn
    /// there is lit (<paramref name="lit"/>: 1 a lit material, 0 an unlit one) - then each light's glow added.
    /// </summary>
    public void Apply(ref float r, ref float g, ref float b, float sx, float sy, float lit = 1f)
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
        foreach (var l in _lights)
        {
            if (l.VolumeOpacity <= 0f) continue;
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
    /// <paramref name="screenY"/> on the game's screen. Null for anything but a point light, or one with no radius.
    /// URP's point light radius is in world units whatever the light's scale (measured: the PC's room's lamp, 15
    /// units, is 750 screen pixels).
    /// </summary>
    public static Light? FromComponent(IReadOnlyDictionary<string, object?> p, float screenX, float screenY)
    {
        static float F(IReadOnlyDictionary<string, object?> d, string k, float fallback)
            => d.TryGetValue(k, out var v) && v != null && float.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                   System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
        if (p.TryGetValue("m_LightType", out var type) && !string.Equals(Convert.ToString(type), "Point", StringComparison.Ordinal)) return null;
        // Only a light that lights the level's sorting layer (Default) lights its sprites: the PC's room's lamp of
        // intensity 25 lights the Foreground and Photos layers alone (Tools/Scenes/level-light-layers.py).
        if (p.TryGetValue("m_ApplyToSortingLayers", out var layers) && layers is System.Collections.IEnumerable list && layers is not string
            && !list.Cast<object?>().Any(l => string.Equals(Convert.ToString(l), "Default", StringComparison.Ordinal)))
            return null;
        float radius = F(p, "m_PointLightOuterRadius", 0f) * PixelsPerWorldUnit;
        if (radius <= 0f) return null;
        float intensity = F(p, "m_Intensity", 1f);
        var (cr, cg, cb) = Colour(p.TryGetValue("m_Color", out var c) ? Convert.ToString(c) : null);
        bool volume = !p.TryGetValue("m_LightVolumeEnabled", out var ve) || ve is not bool on || on;
        return new Light(screenX, screenY, radius, cr * intensity, cg * intensity, cb * intensity, F(p, "m_FalloffIntensity", 0.5f),
                         volume ? F(p, "m_LightVolumeIntensity", 0f) : 0f);
    }

    /// <summary>The catalog's colour, RRGGBBAA hex, as Unity's linear-space Color stores it (no decode: Light2D's colour
    /// goes to the shader as it is).</summary>
    private static (float r, float g, float b) Colour(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length < 6) return (1f, 1f, 1f);
        float Byte(int at) => Convert.ToInt32(hex.Substring(at, 2), 16) / 255f;
        return (Byte(0), Byte(2), Byte(4));
    }
}
