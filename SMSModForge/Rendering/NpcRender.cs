using System;
using System.Threading.Tasks;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// A pack NPC over a <see cref="PreviewBackdrop"/>, as the game draws it in that
/// place (1.9.1, the author: "the NPC preview should get the same treatment as
/// the Character one ... Ultra should be the most accurate look for them in
/// game"). Measured in the game (InGameSuiteTests.S9): a pack NPC's material is
/// a bust's - the same program, its jiggle and its fake GI - but it stands on
/// the room's layer (Default), so:
/// <list type="bullet">
/// <item>the room's lights reach it: the program multiplies it by that layer's
/// light texture (its t0), which in the PC's room carries the lamp's warmth
/// (<see cref="NpcLight"/>), where a bust takes the global light alone;</item>
/// <item>its edge glow reads the room as a bust's does - the room without the
/// NPC in it (measured: closer than the frame with the NPC in it, and than the
/// flat black Unity binds once the frame is drawn, UnityBlack);</item>
/// <item>it is the size the place makes it: its art at 100 pixels a unit, times
/// the level's own scale, at 50 screen pixels a unit (the game's 960x540);</item>
/// <item>a lamp's glow and what the place's lights add are drawn over it as
/// over everything.</item>
/// </list>
/// In the backdrop's frame (the game's screen around a bust), centred.
/// </summary>
internal static class NpcRender
{
    /// <summary>Screen pixels (the game's 960x540) of an NPC art pixel in a level
    /// of scale <paramref name="levelScale"/>: 100 art pixels a unit, 50 screen
    /// pixels a unit.</summary>
    public static float ScreenPerArtPixel(float levelScale) => levelScale * 0.5f;

    /// <summary>The fake GI an NPC's art reads with: the backdrop's settings over
    /// its room as a bust's reads it - an instance of its own, whose background no
    /// bust's shadow changes (the bust preview swaps its scene's in, a frame at a time).</summary>
    public static FakeGi Gi(PreviewBackdrop backdrop) => new(backdrop);

    /// <summary>How far the gather reads on an NPC's art, in its texels: the
    /// program's offsets are in uv, so a larger art reaches more texels.</summary>
    public static int Reach(FakeGiSettings settings, int w, int h) => (int)MathF.Ceiling(settings.EdgeWidth * Math.Max(w, h)) + 1;

    /// <summary>
    /// The NPC's body into <paramref name="body"/> (premultiplied linear RGBA over
    /// the frame, rows top-down): its art (<paramref name="w"/> by
    /// <paramref name="h"/>, straight sRGB BGRA, rows top-down) and mask at the
    /// screen rect (<paramref name="left"/>, <paramref name="bottom"/>,
    /// <paramref name="width"/>, <paramref name="height"/>, pixels from the
    /// bottom left), its fake GI with <paramref name="gi"/>, times
    /// <paramref name="light"/> at each pixel. At <paramref name="zoom"/> as
    /// <see cref="BackdropRender.Body"/>: the game's pixels a frame pixel is, each
    /// way, averaged premultiplied.
    /// </summary>
    public static void Body(byte[] baseTex, byte[] maskTex, int w, int h, JiggleParams p, (float r, float g, float b, float a) tint, float time,
                            FakeGi? gi, FakeGi.Art? art, Func<float, float, (float r, float g, float b)> light,
                            float left, float bottom, float width, float height, int frameLeft, int frameBottom,
                            float[] body, bool exactBlur, float[]? decode, int zoom)
    {
        const int N = BackdropRender.FrameSize;
        if (zoom < 1) throw new ArgumentOutOfRangeException(nameof(zoom));
        int G = N * zoom, stride = w * 4;
        var u = new float[G];
        for (int gx = 0; gx < G; gx++) u[gx] = (frameLeft + (gx + 0.5f) / zoom - left) / width;
        var v = new float[G];
        for (int gy = 0; gy < G; gy++)
        {
            int y = gy / zoom, sub = gy % zoom;
            v[gy] = (frameBottom + (N - 1 - y) + (zoom - 1 - sub + 0.5f) / zoom - bottom) / height;
        }
        var grid = new JiggleShader.Grid(p, time, u, v);
        var lin = decode ?? FakeGi.SrgbToLinear;
        bool white = tint.r == 1f && tint.g == 1f && tint.b == 1f;
        static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        float tr = white ? 1f : Lin(tint.r), tg = white ? 1f : Lin(tint.g), tb = white ? 1f : Lin(tint.b), ta = tint.a;
        float share = 1f / (zoom * zoom);
        // The frame rows and columns the quad covers (beyond them nothing of it).
        int x0 = Math.Max(0, (int)MathF.Floor(left - frameLeft)), x1 = Math.Min(N - 1, (int)MathF.Ceiling(left + width - frameLeft));
        Parallel.For(0, N, y =>
        {
            int row = y * N * 4;
            Array.Clear(body, row, N * 4);
            int sy = frameBottom + (N - 1 - y);
            for (int x = x0; x <= x1; x++)
            {
                int o = row + x * 4;
                float br = 0f, bg = 0f, bb = 0f, ba = 0f;
                for (int sub = 0; sub < zoom; sub++)
                {
                    int gy = y * zoom + sub;
                    float vy = v[gy];
                    if (!(vy >= 0f && vy < 1f)) continue;
                    float syc = sy + (zoom - 1 - sub + 0.5f) / zoom;
                    for (int subX = 0; subX < zoom; subX++)
                    {
                        int gx = x * zoom + subX;
                        float ux = u[gx];
                        if (!(ux >= 0f && ux < 1f)) continue;
                        int mx = Math.Clamp((int)MathF.Floor(ux * w), 0, w - 1);
                        int my = Math.Clamp((int)MathF.Floor(vy * h), 0, h - 1);
                        int mi = (h - 1 - my) * stride + mx * 4;
                        var (du, dv) = grid.Offset(gx, gy, maskTex[mi + 2] / 255f, maskTex[mi + 1] / 255f, maskTex[mi] / 255f, maskTex[mi + 3] / 255f);
                        float uu = ux + du, vv = vy + dv;
                        int tx = Math.Clamp((int)MathF.Floor(uu * w), 0, w - 1);
                        int ty = Math.Clamp((int)MathF.Floor(vv * h), 0, h - 1);
                        int ti = (h - 1 - ty) * stride + tx * 4;
                        byte aByte = baseTex[ti + 3];
                        float a = aByte / 255f * ta;
                        if (!(a > 0f)) continue;
                        float sxc = frameLeft + x + (subX + 0.5f) / zoom;
                        float r = lin[baseTex[ti + 2]] * tr, g = lin[baseTex[ti + 1]] * tg, b = lin[baseTex[ti]] * tb;
                        if (gi != null && art != null && a > 0.01f && gi.CanLight(art, tx, ty, aByte))
                        {
                            var (rim, gdx, gdy) = gi.Gather(art, uu, vv, a);
                            gi.Light(rim, gdx, gdy, sxc, syc, ref r, ref g, ref b, a, exactBlur);
                        }
                        // The program's light: its colour (and the fake GI) times the layer's light texture.
                        var (lr, lg, lb) = light(sxc, syc);
                        br += MathF.Max(r * lr, 0f) * a; bg += MathF.Max(g * lg, 0f) * a; bb += MathF.Max(b * lb, 0f) * a; ba += a;
                    }
                }
                body[o] = br * share; body[o + 1] = bg * share; body[o + 2] = bb * share; body[o + 3] = ba * share;
            }
        });
    }

    /// <summary>
    /// The frame with the NPC in it, as the game makes it: the room (a lamp's
    /// with no bust's shadow), each layer of the NPC over it in turn (its body,
    /// then its blink: premultiplied linear over the frame, null for none), a
    /// lamp's glow or what the place's lights add over everything, then the
    /// post-processing <paramref name="parts"/> into <paramref name="output"/>.
    /// </summary>
    public static void Compose(BackdropScene scene, GamePost.Target target, float[] scratchBackground, float[] coverage,
                               BackdropRender.PostParts parts, byte[] output, params float[]?[] layers)
        => Compose(scene, target, scratchBackground, coverage, parts, output, null, layers);

    /// <summary>
    /// As the other, the NPC's wet drops (<paramref name="drops"/>, the screen's canvas) blended in as a wet bust's
    /// are (JigglePreview): with a lamp before its glow, each drop's pixels counted as drawn over; without one after
    /// what the place's lights add, over everything. A shower's drops come already lit (NpcPreview.WetDropsNow).
    /// </summary>
    public static void Compose(BackdropScene scene, GamePost.Target target, float[] scratchBackground, float[] coverage,
                               BackdropRender.PostParts parts, byte[] output, WetDrops.Over? drops, params float[]?[] layers)
    {
        var lamp = scene.Lamp;
        if (lamp != null) lamp.Room(null, target, scratchBackground, scene.Backdrop.BackgroundWidth, scene.Backdrop.BackgroundHeight);
        else BackdropRender.ResetFrame(target, scene);
        Array.Clear(coverage);
        foreach (var layer in layers)
            if (layer != null) BackdropRender.Layer(target, scene, layer, coverage);
        if (lamp != null)
        {
            if (drops != null) WetDrops.BlendInto(target, drops, scene, coverage);
            lamp.Glow(target, null, BackdropRender.Covered(scene, coverage), onTheRoomsLayer: true);
        }
        else
        {
            BackdropRender.Lamps(target, scene, coverage);
            if (drops != null) WetDrops.BlendInto(target, drops);
        }
        BackdropRender.Present(target, scene, parts, output);
    }
}

/// <summary>
/// The light a sprite on a backdrop's room layer (an NPC) is multiplied by, at a
/// screen point: with a lamp, its light texture without a bust's shadow
/// (<see cref="GameLamp.LightAt"/>); otherwise the room lit over the room under
/// the global light alone, both as the game captured them (the place's own
/// lights' share), per texel of a light texture the size the game's is
/// (the screen at 0.4), read between the four nearest.
/// </summary>
internal sealed class NpcLight
{
    private readonly GameLamp? _lamp;
    private readonly float[]? _texel;
    private readonly int _w, _h, _tw, _th;

    public NpcLight(BackdropScene scene)
    {
        var b = scene.Backdrop;
        _w = b.ScreenWidth; _h = b.ScreenHeight;
        _lamp = scene.Lamp;
        if (_lamp != null) return;
        _tw = (int)Math.Round(_w * 0.4); _th = (int)Math.Round(_h * 0.4);
        var room = b.RoomHdr;
        var unlit = b.UnlitHdr;
        var texel = new float[_tw * _th * 3];
        Parallel.For(0, _th, j =>
        {
            int ya = (int)Math.Floor(j * (double)_h / _th), yb = Math.Max(ya + 1, (int)Math.Floor((j + 1) * (double)_h / _th));
            for (int i = 0; i < _tw; i++)
            {
                int xa = (int)Math.Floor(i * (double)_w / _tw), xb = Math.Max(xa + 1, (int)Math.Floor((i + 1) * (double)_w / _tw));
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0; int n = 0;
                    for (int y = ya; y < yb && y < _h; y++)
                        for (int x = xa; x < xb && x < _w; x++)
                        {
                            int p = (y * _w + x) * 3 + c;
                            if (unlit[p] > 0.05f) { sum += room[p] / unlit[p]; n++; }
                        }
                    texel[(j * _tw + i) * 3 + c] = n > 0 ? (float)(sum / n) : 1f;
                }
            }
        });
        _texel = texel;
    }

    /// <summary>The light texture itself - RGB per texel, rows from the bottom - and
    /// its size: what the graphics card reads it from (GpuNpcBody).</summary>
    public (float[] rgb, int w, int h) Texels
    {
        get
        {
            if (_lamp == null) return (_texel!, _tw, _th);
            var (w, h) = _lamp.LightTextureSize;
            return (_lamp.LitNoShadow, w, h);
        }
    }

    public (float r, float g, float b) At(float sx, float sy)
    {
        if (_lamp != null) return _lamp.LightAt(sx, sy);
        var t = _texel!;
        float u = Math.Clamp(sx / _w * _tw - 0.5f, 0f, _tw - 1), v = Math.Clamp(sy / _h * _th - 0.5f, 0f, _th - 1);
        int i = Math.Min((int)u, _tw - 2), j = Math.Min((int)v, _th - 2);
        float fu = u - i, fv = v - j;
        int a = (j * _tw + i) * 3, b = a + 3, c = a + _tw * 3, d = c + 3;
        float R(int k) => (t[a + k] + (t[b + k] - t[a + k]) * fu) * (1f - fv) + (t[c + k] + (t[d + k] - t[c + k]) * fu) * fv;
        return (R(0), R(1), R(2));
    }
}
