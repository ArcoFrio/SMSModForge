using System;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The bust preview over a backdrop (1.9.1, Rendering/BackdropRender): a bust
/// drawn in the player's room as the game's own program draws it there - the
/// whole program, run pixel by pixel over the same frame, holds the preview's
/// fast path to it.
/// </summary>
public sealed class BackdropRenderTests
{
    private readonly ITestOutputHelper _out;
    public BackdropRenderTests(ITestOutputHelper output) => _out = output;

    private static PreviewBackdrop Room()
        => PreviewBackdrop.Load(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "Backdrops", "MyRoom"));

    /// <summary>A bust's art as a file has it (BGRA, sRGB, straight, rows from
    /// the top): an upright body, its edge soft on the left and hard on the
    /// right, a hole; and a mask over its upper half.</summary>
    private static (byte[] art, byte[] mask) Bust()
    {
        const int S = JiggleShader.Size;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        var rng = new Random(4);
        for (int row = 0; row < S; row++)
            for (int x = 0; x < S; x++)
            {
                int y = S - 1 - row;   // from the bottom
                double d = Math.Sqrt(Math.Pow((x - 128) / 92.0, 2) + Math.Pow((y - 112) / 104.0, 2));
                double soft = Math.Clamp((1 - d) * 104 / (x < 128 ? 6.0 : 1.0), 0, 1);
                if (Math.Sqrt(Math.Pow(x - 150, 2) + Math.Pow(y - 150, 2)) < 9) soft = 0;
                int i = (row * S + x) * 4;
                art[i + 2] = (byte)(60 + 150 * x / S + rng.Next(8));
                art[i + 1] = (byte)(90 + 100 * Math.Pow(Math.Sin(y / 17.0), 2));
                art[i] = (byte)(140 - 70 * y / S);
                art[i + 3] = (byte)Math.Round(soft * 255);
                if (y > 120) { mask[i + 2] = 150; mask[i + 1] = 128; mask[i] = 180; mask[i + 3] = 255; }
            }
        return (art, mask);
    }

    private struct ProgramTextures : IGameTextures
    {
        public float[] Art, Mask, Bg; public int BgW, BgH;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        public (float, float, float, float) S1(float u, float v) => GameSampling.Point(Art, JiggleShader.Size, JiggleShader.Size, u, v);
        public (float, float, float, float) S2(float u, float v) => GameSampling.Point(Mask, JiggleShader.Size, JiggleShader.Size, u, v);
        public (float, float, float, float) S3(float u, float v) => GameSampling.Bilinear(Bg, BgW, BgH, u, v);
    }

    /// <summary>A file's BGRA as the program reads the texture: RGBA floats,
    /// rows from the bottom; colour in linear light when <paramref name="srgb"/>.</summary>
    private static float[] AsRead(byte[] bgra, bool srgb)
    {
        const int S = JiggleShader.Size;
        var t = new float[S * S * 4];
        var lin = FakeGi.SrgbToLinear;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = ((S - 1 - y) * S + x) * 4, o = (y * S + x) * 4;
                t[o] = srgb ? lin[bgra[i + 2]] : bgra[i + 2] / 255f;
                t[o + 1] = srgb ? lin[bgra[i + 1]] : bgra[i + 1] / 255f;
                t[o + 2] = srgb ? lin[bgra[i]] : bgra[i] / 255f;
                t[o + 3] = bgra[i + 3] / 255f;
            }
        return t;
    }

    [Theory]
    [InlineData(2.5f, 0, false)]
    [InlineData(11.75f, 4, false)]
    [InlineData(7.0f, -3, true)]
    public void A_bust_over_the_room_is_drawn_as_the_games_program_draws_it(float time, int bob, bool tinted)
    {
        var backdrop = Room();
        var scene = new BackdropScene(backdrop);
        var gi = new FakeGi(backdrop);
        var (art, mask) = Bust();
        var giArt = new FakeGi.Art(art, JiggleShader.Size, JiggleShader.Size, gi.Reach);
        var p = new JiggleParams { Speed = 1.3f, Strength = 0.012f, Frequency = 9f, NoiseScale = 3f, NoiseSpeed = 0.7f, NoiseStrength = 0.01f };
        var tint = tinted ? (0.9f, 0.7f, 0.8f, 1f) : (1f, 1f, 1f, 1f);
        int bustBottom = backdrop.BustRestBottom + bob;
        const int N = BackdropRender.FrameSize;
        var body = new float[N * N * 4];
        BackdropRender.Body(art, mask, p, tint, time, gi, giArt, scene.Left, scene.FrameBottom, bustBottom, body, true, null, 0f, exactBlur: true);

        // The game's program over the same frame.
        var tex = new ProgramTextures { Art = AsRead(art, true), Mask = AsRead(mask, false), Bg = gi.Background, BgW = gi.BackgroundWidth, BgH = gi.BackgroundHeight };
        var cb0 = (float[])gi.Cb0.Clone();
        cb0[15 * 4] = time / 20; cb0[15 * 4 + 1] = time; cb0[15 * 4 + 2] = time * 2; cb0[15 * 4 + 3] = time * 3;
        var cb1 = new float[GameBustProgram.Cb1Size];
        cb1[0] = 1; cb1[1] = 1;
        for (int k = 4; k < 12; k++) cb1[k] = 1;
        cb1[12] = p.Speed; cb1[13] = p.Strength; cb1[14] = p.Frequency; cb1[15] = p.NoiseScale; cb1[16] = p.NoiseSpeed; cb1[17] = p.NoiseStrength;
        static float Lin(float c) => (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        float vr = tinted ? Lin(tint.Item1) : 1f, vg = tinted ? Lin(tint.Item2) : 1f, vb = tinted ? Lin(tint.Item3) : 1f;
        int compared = 0, same = 0, lit = 0;
        double worst = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int sx = scene.Left + x, sy = scene.FrameBottom + (N - 1 - y);
                float u = (x + 0.5f) / N, v = (sy + 0.5f - bustBottom) / N;
                if (!(v >= 0f && v < 1f)) continue;
                var inputs = new GameInputs
                {
                    v1x = vr, v1y = vg, v1z = vb, v1w = tint.Item4, v2x = u, v2y = v, v2z = u, v2w = v,
                    v3x = (sx + 0.5f) / backdrop.ScreenWidth, v3y = (sy + 0.5f) / backdrop.ScreenHeight, v3z = 0, v3w = 1,
                };
                var reg = new GameRegisters();
                GameBustProgram.Program(ref reg, cb0, cb1, ref tex, in inputs);
                int i = (y * N + x) * 4;
                float er = reg.Discarded ? 0f : reg.o0x, eg = reg.Discarded ? 0f : reg.o0y, eb = reg.Discarded ? 0f : reg.o0z, ea = reg.Discarded ? 0f : reg.o0w;
                double d = Math.Max(Math.Max(Math.Abs(body[i] - er), Math.Abs(body[i + 1] - eg)), Math.Max(Math.Abs(body[i + 2] - eb), Math.Abs(body[i + 3] - ea)));
                compared++;
                worst = Math.Max(worst, d);
                if (d <= 1e-6) same++;
                // Where the fake GI changed the program's colour (its output against the art alone).
                if (!reg.Discarded && ea > 0.01f)
                {
                    var (tr, tg, tb, ta) = tex.S1(u, v);
                    if (Math.Abs(er - tr * vr * ea) > 0.004f) lit++;
                }
            }
        _out.WriteLine($"time {time}, breathing {bob}, tint {tinted}: {same} of {compared} frame pixels as the program draws them (worst {worst:E2}); about {lit} lit by the fake GI");
        Assert.True(lit > 1000, "the fake GI lights too little of the bust to tell");
        Assert.Equal(compared, same);
    }

    /// <summary>What the body keeps of the gather between frames (for pixels
    /// that have not moved) changes nothing it draws. Control: a kept gather
    /// that is wrong shows.</summary>
    [Fact]
    public void Keeping_the_gather_between_frames_changes_nothing()
    {
        var backdrop = Room();
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        var (art, mask) = Bust();
        var p = new JiggleParams { Speed = 1.3f, Strength = 0.012f, Frequency = 9f, NoiseScale = 3f, NoiseSpeed = 0.7f, NoiseStrength = 0.01f };
        const int N = BackdropRender.FrameSize;
        var kept = new FakeGi.Art(art, JiggleShader.Size, JiggleShader.Size, gi.Reach);
        var a = new float[N * N * 4];
        var b = new float[N * N * 4];
        BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), 1f, gi, kept, scene.Left, scene.FrameBottom, backdrop.BustRestBottom + 2, a, true, null, 0f);
        BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), 3.3f, gi, kept, scene.Left, scene.FrameBottom, backdrop.BustRestBottom - 4, a, true, null, 0f);
        var fresh = new FakeGi.Art(art, JiggleShader.Size, JiggleShader.Size, gi.Reach);
        BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), 3.3f, gi, fresh, scene.Left, scene.FrameBottom, backdrop.BustRestBottom - 4, b, true, null, 0f);
        int differ = 0, keptCount = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) differ++;
        foreach (var x in kept.StillAlpha) if (!float.IsNaN(x)) keptCount++;
        // The control: the same frame from a kept gather that is wrong.
        for (int i = 0; i < kept.StillRim.Length; i++) if (!float.IsNaN(kept.StillAlpha[i])) kept.StillRim[i] = 0.9f;
        BackdropRender.Body(art, mask, p, (1f, 1f, 1f, 1f), 3.3f, gi, kept, scene.Left, scene.FrameBottom, backdrop.BustRestBottom - 4, a, true, null, 0f);
        int controlDiffer = 0;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) controlDiffer++;
        _out.WriteLine($"{keptCount} pixels' gather kept; the frame drawn with them differs from one drawn fresh at {differ} numbers; with them made wrong, at {controlDiffer}");
        Assert.True(keptCount > 10000, "too few pixels kept their gather for the check to mean anything");
        Assert.True(controlDiffer > 1000, "a wrong kept gather changes nothing - the kept values are not being used");
        Assert.Equal(0, differ);
    }

    /// <summary>Where the bust is not, the frame is the room exactly as
    /// shipped: every sRGB level survives the trip through linear light.</summary>
    [Fact]
    public void Where_no_bust_covers_it_the_frame_is_the_room()
    {
        for (int k = 0; k < 256; k++)
            Assert.Equal(k, BackdropRender.ToSrgbByte(FakeGi.SrgbToLinear[k]));
        var backdrop = Room();
        var scene = new BackdropScene(backdrop);
        const int N = BackdropRender.FrameSize;
        var output = new byte[N * N * 4];
        BackdropRender.Finish(new float[N * N * 4], scene, output);
        int differ = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int sy = scene.FrameBottom + (N - 1 - y), sx = scene.Left + x;
                int ri = ((backdrop.ScreenHeight - 1 - sy) * backdrop.ScreenWidth + sx) * 4, o = (y * N + x) * 4;
                if (output[o] != backdrop.Room[ri] || output[o + 1] != backdrop.Room[ri + 1] || output[o + 2] != backdrop.Room[ri + 2]) differ++;
            }
        Assert.Equal(0, differ);
    }

    /// <summary>Over an opaque bust, the frame adds what the backdrop says the
    /// room's lights add there - and more on the side of the room's lights
    /// (its lamps, right of the bust) than on the other.</summary>
    [Fact]
    public void The_rooms_lights_add_to_the_bust_what_the_game_measured()
    {
        var backdrop = Room();
        var scene = new BackdropScene(backdrop);
        const int N = BackdropRender.FrameSize;
        var grey = new float[N * N * 4];
        float g = FakeGi.SrgbToLinear[96];
        for (int i = 0; i < grey.Length; i += 4) { grey[i] = grey[i + 1] = grey[i + 2] = g; grey[i + 3] = 1f; }
        var output = new byte[N * N * 4];
        BackdropRender.Finish(grey, scene, output);
        int differ = 0;
        for (int y = 0; y < N; y += 7)
            for (int x = 0; x < N; x += 7)
            {
                int sy = scene.FrameBottom + (N - 1 - y);
                var (lr, lg, lb) = backdrop.LightAt(scene.Left + x + 0.5f, sy + 0.5f);
                int o = (y * N + x) * 4;
                if (output[o + 2] != BackdropRender.ToSrgbByte(g + lr) || output[o + 1] != BackdropRender.ToSrgbByte(g + lg) || output[o] != BackdropRender.ToSrgbByte(g + lb)) differ++;
            }
        int Red(int x, int y) => output[(y * N + x) * 4 + 2];
        _out.WriteLine($"red over a grey bust: left {Red(20, 256)}, right {Red(490, 256)} (the grey alone {BackdropRender.ToSrgbByte(g)})");
        Assert.Equal(0, differ);
        Assert.True(Red(490, 256) > Red(20, 256) + 3, "the room's lamps light the right side no more than the left");
    }
}
