using System;
using System.Threading.Tasks;

namespace SMSModForge.Rendering;

/// <summary>
/// The room's lamp as URP's 2D renderer draws it in the game (1.9.1): what a
/// bust's shadow takes away from the room. Every step measured on the game's
/// own frames (InGameSuiteTests.S6, and the research behind it):
/// <list type="number">
/// <item>The lamp is drawn into the room layer's light texture - the screen at
/// the light texture scale (384x216), cleared to the global light (white, 1)
/// - by Hidden/Light2D's point light: URP's light lookup (<c>1 - 2d/256</c>)
/// through its falloff lookup, times the lamp's colour, and through the
/// shadow texture, <c>1 - intensity x shadow</c>. The texture keeps 11-bit
/// floats and truncates: the lamp's share on top of the global light keeps
/// only whole 64ths of it, so a lamp dimmed by a shadow loses more than its
/// share - in the far shadow, everything it had there.</item>
/// <item>The room's sprites are their colour times that texture, read between
/// its four nearest texels; the camera's target truncates again.</item>
/// <item>The edge glow's background (_CameraSortingLayerTexture) is copied
/// from the room as it is then: one pixel of every 4x4, the one at (1, 1).</item>
/// <item>After the busts, the lamp's glow (its light volume, the same shader at
/// every screen pixel, times its volume opacity) is added over everything,
/// through the shadow at its own shadow intensity.</item>
/// </list>
/// A lamp that lights only the room's layer (<see cref="LampSettings.GlowWithTheRoom"/>,
/// 1.9.2, the Beach's) adds its glow to the room before the busts instead:
/// the edge glow's background is copied with it, and a bust is not under it.
/// What stands on the room's layer itself (a pack NPC) is under it either way.
/// Where the shadow changes nothing the room is the game's own capture, to the
/// bit; elsewhere it is worked out as above from the room's colour, which the
/// captures with the lamp off and without its glow pin down.
/// </summary>
internal sealed class GameLamp
{
    public readonly BustShadow Shadow;
    public readonly int ScreenWidth, ScreenHeight;
    private readonly int _tw, _th;
    private readonly float _gr, _gg, _gb;
    private readonly float _intensity, _volumeIntensity;
    /// <summary>The light shader's output at each light texture texel, RGB, rows from the bottom.</summary>
    private readonly float[] _lampTexel;
    /// <summary>The glow at each screen pixel, RGB, rows from the bottom.</summary>
    private readonly float[] _glow;
    /// <summary>The room's colour - what its sprites multiply the light by - RGB.</summary>
    private readonly float[] _albedo;
    /// <summary>The room as captured: without the glow, and as seen. RGB.</summary>
    private readonly float[] _noGlow, _room;
    /// <summary>A place's other lights (1.10.0, <see cref="PreviewBackdrop.LampBase"/>): the light texture without the
    /// lamp, per texel, and their glows drawn with the room, per screen pixel; null for a captured backdrop (the
    /// global light alone).</summary>
    private readonly float[]? _base, _othersGlow;

    public GameLamp(PreviewBackdrop backdrop) : this(backdrop, null) { }

    /// <param name="glowWithTheRoom">The lamp drawn the other way than the
    /// backdrop says: a test's control. Null: as the backdrop says.</param>
    internal GameLamp(PreviewBackdrop backdrop, bool? glowWithTheRoom)
    {
        var lamp = backdrop.Lamp ?? throw new InvalidOperationException(backdrop.Name + " has no lamp");
        ScreenWidth = backdrop.ScreenWidth; ScreenHeight = backdrop.ScreenHeight;
        Shadow = new BustShadow(new BustShadow.Lamp(lamp.X, lamp.Y, lamp.Radius, lamp.Softness, lamp.SoftnessFalloff,
                                                    lamp.ShadowIntensity, lamp.ShadowVolumeIntensity, lamp.TextureScale),
                                ScreenWidth, ScreenHeight, backdrop.LookupDecode);
        _tw = Shadow.W; _th = Shadow.H;
        (_gr, _gg, _gb) = lamp.Global;
        _intensity = lamp.ShadowIntensity; _volumeIntensity = lamp.ShadowVolumeIntensity;
        _noGlow = backdrop.RoomNoGlowHdr; _room = backdrop.RoomHdr;
        _base = backdrop.LampBase; _othersGlow = backdrop.OthersBehindGlow;
        GlowWithTheRoom = glowWithTheRoom ?? lamp.GlowWithTheRoom;

        // URP's light lookup texture (half floats) and its falloff curve.
        var look = new float[256 * 256];
        for (int i = 0; i < 256; i++)
            for (int j = 0; j < 256; j++)
            {
                float d = MathF.Sqrt((j - 128f) * (j - 128f) + (i - 128f) * (i - 128f));
                float r = j != 255 && i != 255 ? Math.Clamp(1f - 2f * d / 256f, 0f, 1f) : 0f;
                look[i * 256 + j] = (float)(Half)r;
            }
        float At(float sx, float sy)
        {
            // The vertex program's lookup uv: the light's own space (radius 1), plus half a texel.
            float u = ((sx - lamp.X) / lamp.Radius + 1f) * 0.5f + 0.5f / 256f;
            float v = ((sy - lamp.Y) / lamp.Radius + 1f) * 0.5f + 0.5f / 256f;
            float att = Math.Clamp(GameSampling.BilinearOne(look, 256, 256, u, v), 0f, 1f);
            return Shadow.FalloffAt(att, lamp.Falloff);
        }
        _lampTexel = new float[_tw * _th * 3];
        Parallel.For(0, _th, j =>
        {
            for (int i = 0; i < _tw; i++)
            {
                float f = At((i + 0.5f) * ScreenWidth / _tw, (j + 0.5f) * ScreenHeight / _th);
                int o = (j * _tw + i) * 3;
                _lampTexel[o] = f * lamp.R; _lampTexel[o + 1] = f * lamp.G; _lampTexel[o + 2] = f * lamp.B;
            }
        });
        _glow = new float[ScreenWidth * ScreenHeight * 3];
        Parallel.For(0, ScreenHeight, y =>
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                float f = At(x + 0.5f, y + 0.5f) * lamp.VolumeOpacity;
                int o = (y * ScreenWidth + x) * 3;
                _glow[o] = f * lamp.R; _glow[o + 1] = f * lamp.G; _glow[o + 2] = f * lamp.B;
            }
        });

        // The room's colour: between what the lamp-off capture and the lit one
        // (without the glow) allow, each a truncated 11-bit value - or, for a
        // place of the pack's, as the preview drew it (1.10.0).
        var lit = new float[_tw * _th * 3];
        LightTexture(null, lit);
        if (backdrop.Albedo is { } known) { _albedo = known; return; }
        _albedo = new float[ScreenWidth * ScreenHeight * 3];
        var off = backdrop.UnlitHdr;
        Parallel.For(0, ScreenHeight, y =>
        {
            for (int x = 0; x < ScreenWidth; x++)
            {
                var (tr, tg, tb) = Between(lit, x, y);
                int o = (y * ScreenWidth + x) * 3;
                _albedo[o] = Colour(off[o], _noGlow[o], tr, 6);
                _albedo[o + 1] = Colour(off[o + 1], _noGlow[o + 1], tg, 6);
                _albedo[o + 2] = Colour(off[o + 2], _noGlow[o + 2], tb, 5);
            }
        });
    }

    private static float Colour(float off, float noGlow, float light, int mantissa)
    {
        double lo = Math.Max(off, noGlow / light), hi = Math.Min(off + GamePost.Step(off, mantissa), (noGlow + GamePost.Step(noGlow, mantissa)) / light);
        return (float)(lo < hi ? (lo + hi) * 0.5 : lo);
    }

    /// <summary>The light texture through the shadow (null: none): 11-bit floats,
    /// truncated, RGB per texel.</summary>
    private void LightTexture(float[]? shadow, float[] texel)
    {
        for (int k = 0, o = 0; k < _tw * _th; k++, o += 3)
        {
            float s = shadow == null ? 0f : shadow[k];
            float keep = 1f - s, dim = (1f - _intensity) * s;
            float br = _base?[o] ?? _gr, bg = _base?[o + 1] ?? _gg, bb = _base?[o + 2] ?? _gb;
            texel[o] = GamePost.Small(br + (_lampTexel[o] * keep + _lampTexel[o] * dim), 6);
            texel[o + 1] = GamePost.Small(bg + (_lampTexel[o + 1] * keep + _lampTexel[o + 1] * dim), 6);
            texel[o + 2] = GamePost.Small(bb + (_lampTexel[o + 2] * keep + _lampTexel[o + 2] * dim), 5);
        }
    }

    /// <summary>A light texture read at a screen pixel's centre, between its four nearest texels.</summary>
    private (float r, float g, float b) Between(float[] texel, int sx, int sy)
    {
        float u = Math.Clamp((sx + 0.5f) / ScreenWidth * _tw - 0.5f, 0f, _tw - 1), v = Math.Clamp((sy + 0.5f) / ScreenHeight * _th - 0.5f, 0f, _th - 1);
        int i = Math.Min((int)u, _tw - 2), j = Math.Min((int)v, _th - 2);
        float fu = u - i, fv = v - j;
        int a = (j * _tw + i) * 3, b = a + 3, c = a + _tw * 3, d = c + 3;
        float R(int k) => (texel[a + k] + (texel[b + k] - texel[a + k]) * fu) * (1f - fv) + (texel[c + k] + (texel[d + k] - texel[c + k]) * fu) * fv;
        return (R(0), R(1), R(2));
    }

    private float[]? _texel;
    private float[]? _litNoShadow;

    /// <summary>The room layer's light texture with no bust's shadow (RGB per
    /// texel, rows from the bottom, <see cref="LightTextureSize"/>), made once.</summary>
    internal float[] LitNoShadow
    {
        get
        {
            var texel = _litNoShadow;
            if (texel != null) return texel;
            texel = new float[_tw * _th * 3];
            LightTexture(null, texel);
            return _litNoShadow = texel;
        }
    }

    internal (int w, int h) LightTextureSize => (_tw, _th);

    /// <summary>
    /// The room layer's light at a screen point (pixels from the bottom left),
    /// read between its light texture's four nearest texels, with no bust's
    /// shadow: what a sprite on that layer is multiplied by - a pack NPC, whose
    /// material is a bust's but whose layer is the room's (1.9.1, measured in
    /// the game: InGameSuiteTests.S9).
    /// </summary>
    internal (float r, float g, float b) LightAt(float sx, float sy)
    {
        var texel = LitNoShadow;
        float u = Math.Clamp(sx / ScreenWidth * _tw - 0.5f, 0f, _tw - 1), v = Math.Clamp(sy / ScreenHeight * _th - 0.5f, 0f, _th - 1);
        int i = Math.Min((int)u, _tw - 2), j = Math.Min((int)v, _th - 2);
        float fu = u - i, fv = v - j;
        int a = (j * _tw + i) * 3, b = a + 3, c = a + _tw * 3, d = c + 3;
        float R(int k) => (texel[a + k] + (texel[b + k] - texel[a + k]) * fu) * (1f - fv) + (texel[c + k] + (texel[d + k] - texel[c + k]) * fu) * fv;
        return (R(0), R(1), R(2));
    }

    // What the graphics card's copy of the room and the glow reads (GpuFrame).
    internal float[] LampTexels => _lampTexel;
    internal float[] GlowRgb => _glow;
    internal float[] AlbedoRgb => _albedo;
    internal float[] NoGlowRgb => _noGlow;
    internal float[] RoomRgb => _room;
    internal (float r, float g, float b) GlobalLight => (_gr, _gg, _gb);
    internal float Intensity => _intensity;
    internal float VolumeIntensity => _volumeIntensity;

    /// <summary>The lamp's glow is drawn with the room, before the busts (see
    /// <see cref="LampSettings.GlowWithTheRoom"/>).</summary>
    public bool GlowWithTheRoom { get; }

    /// <summary>
    /// The room, lit through <paramref name="shadow"/> (the lamp's shadow
    /// texture, or null for none) - what the camera's target holds before the
    /// busts: without the lamp's glow, or with it through the shadow when the
    /// glow is drawn with the room - over the whole screen; and the edge
    /// glow's background copied from it (RGBA, rows from the bottom).
    /// </summary>
    public void Room(float[]? shadow, GamePost.Target target, float[] background, int bw, int bh)
    {
        var texel = _texel ??= new float[_tw * _th * 3];
        LightTexture(shadow, texel);
        int w = ScreenWidth;
        Parallel.For(0, ScreenHeight, y =>
        {
            // The light texture rows this screen row reads.
            float v = Math.Clamp((y + 0.5f) / ScreenHeight * _th - 0.5f, 0f, _th - 1);
            int j = Math.Min((int)v, _th - 2);
            for (int x = 0; x < w; x++)
            {
                int o = y * w + x, p = o * 3, t = o * 4;
                bool shaded = false;
                if (shadow != null)
                {
                    float u = Math.Clamp((x + 0.5f) / w * _tw - 0.5f, 0f, _tw - 1);
                    int i = Math.Min((int)u, _tw - 2);
                    int k = j * _tw + i;
                    shaded = shadow[k] > 0f || shadow[k + 1] > 0f || shadow[k + _tw] > 0f || shadow[k + _tw + 1] > 0f;
                }
                var seen = GlowWithTheRoom ? _room : _noGlow;
                if (!shaded)
                {
                    target.Px[t] = seen[p]; target.Px[t + 1] = seen[p + 1]; target.Px[t + 2] = seen[p + 2]; target.Px[t + 3] = 1f;
                    continue;
                }
                var (lr, lg, lb) = Between(texel, x, y);
                target.Px[t] = GamePost.Small(_albedo[p] * lr, 6);
                target.Px[t + 1] = GamePost.Small(_albedo[p + 1] * lg, 6);
                target.Px[t + 2] = GamePost.Small(_albedo[p + 2] * lb, 5);
                target.Px[t + 3] = 1f;
                // A place's other lights' glows, drawn with the room and cast no shadow (1.10.0).
                if (_othersGlow != null)
                {
                    target.Px[t] = GamePost.Small(target.Px[t] + _othersGlow[p], 6);
                    target.Px[t + 1] = GamePost.Small(target.Px[t + 1] + _othersGlow[p + 1], 6);
                    target.Px[t + 2] = GamePost.Small(target.Px[t + 2] + _othersGlow[p + 2], 5);
                }
                if (GlowWithTheRoom) AddGlow(target, Shadow.At(shadow!, x, y), p, t);
            }
        });
        for (int j = 0; j < bh; j++)
            for (int i = 0; i < bw; i++)
            {
                int s = ((j * 4 + 1) * w + i * 4 + 1) * 4, o = (j * bw + i) * 4;
                background[o] = target.Px[s]; background[o + 1] = target.Px[s + 1]; background[o + 2] = target.Px[s + 2]; background[o + 3] = 1f;
            }
    }

    /// <summary>The glow added at one pixel through a shadow of <paramref name="s"/>,
    /// the target truncating it.</summary>
    private void AddGlow(GamePost.Target target, float s, int p, int t)
    {
        float keep = 1f - s, dim = (1f - _volumeIntensity) * s;
        target.Px[t] = GamePost.Small(target.Px[t] + (_glow[p] * keep + _glow[p] * dim), 6);
        target.Px[t + 1] = GamePost.Small(target.Px[t + 1] + (_glow[p + 1] * keep + _glow[p + 1] * dim), 6);
        target.Px[t + 2] = GamePost.Small(target.Px[t + 2] + (_glow[p + 2] * keep + _glow[p + 2] * dim), 5);
    }

    /// <summary>
    /// The lamp's glow over everything drawn, through <paramref name="shadow"/>.
    /// A pixel nothing was drawn over and the shadow does not reach is the
    /// game's own capture of the room. <paramref name="drawnOver"/>: whether
    /// something covers a screen pixel (rows from the bottom) - a bust, or with
    /// <paramref name="onTheRoomsLayer"/> something on the room's layer (a pack
    /// NPC). A glow drawn with the room (<see cref="GlowWithTheRoom"/>) is in
    /// the room already and goes over only what is on the room's layer.
    /// </summary>
    public void Glow(GamePost.Target target, float[]? shadow, Func<int, int, bool> drawnOver, bool onTheRoomsLayer = false)
    {
        if (GlowWithTheRoom && !onTheRoomsLayer) return;
        int w = ScreenWidth;
        Parallel.For(0, ScreenHeight, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int o = y * w + x, p = o * 3, t = o * 4;
                float s = shadow == null ? 0f : Shadow.At(shadow, x, y);
                bool over = drawnOver(x, y);
                if (GlowWithTheRoom && !over) continue;
                if (s == 0f && !over)
                {
                    target.Px[t] = _room[p]; target.Px[t + 1] = _room[p + 1]; target.Px[t + 2] = _room[p + 2];
                    continue;
                }
                AddGlow(target, s, p, t);
            }
        });
    }
}
