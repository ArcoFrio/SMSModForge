using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The bust program as the editor runs it (Rendering/GameBustProgram.g.cs,
/// made from the game's compiled program by Tools/Shaders/dxbc_to_cs.py)
/// against the game's program run by the reference interpreter
/// (Tools/Shaders/make_gi_fixtures.py, Fixtures/GameBustShader/gi.bin): a bust
/// over a background in the player's room's settings, the fake GI on (1.9.1,
/// the author: "the sprites in game look differently based on the lighting,
/// with some sprite edge lighting in place too").
/// </summary>
public sealed class FakeGiTests
{
    private readonly ITestOutputHelper _out;
    public FakeGiTests(ITestOutputHelper output) => _out = output;

    internal sealed class Fixture
    {
        public int ArtSize, BgW, BgH, ScreenW, ScreenH;
        public float[] Art = Array.Empty<float>(), Mask = Array.Empty<float>(), Background = Array.Empty<float>();
        public float[] Cb0 = Array.Empty<float>(), Cb1 = Array.Empty<float>();
        public List<Case> Cases = new();

        public sealed record Case(float Time, int Left, int Bottom, int Phase, Pixel[] Pixels);
        public readonly record struct Pixel(int X, int Y, float R, float G, float B, float A, bool Alive);

        public static Fixture Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameBustShader", "gi.bin");
            using var r = new BinaryReader(File.OpenRead(path));
            Assert.Equal("GGI1", new string(r.ReadChars(4)));
            var f = new Fixture { ArtSize = r.ReadInt32(), BgW = r.ReadInt32(), BgH = r.ReadInt32(), ScreenW = r.ReadInt32(), ScreenH = r.ReadInt32() };
            int cases = r.ReadInt32();
            float[] Floats(int n) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = r.ReadSingle(); return a; }
            f.Art = Floats(f.ArtSize * f.ArtSize * 4);
            f.Mask = Floats(f.ArtSize * f.ArtSize * 4);
            f.Background = Floats(f.BgW * f.BgH * 4);
            f.Cb0 = Floats(GameBustProgram.Cb0Size);
            f.Cb1 = Floats(GameBustProgram.Cb1Size);
            for (int c = 0; c < cases; c++)
            {
                float time = r.ReadSingle();
                int left = r.ReadInt32(), bottom = r.ReadInt32(), phase = r.ReadInt32(), n = r.ReadInt32();
                var px = new Pixel[n];
                for (int i = 0; i < n; i++)
                    px[i] = new Pixel(r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadInt32() != 0);
                f.Cases.Add(new Case(time, left, bottom, phase, px));
            }
            return f;
        }

        public float[] Cb0At(float time)
        {
            var cb = (float[])Cb0.Clone();
            cb[15 * 4] = time / 20; cb[15 * 4 + 1] = time; cb[15 * 4 + 2] = time * 2; cb[15 * 4 + 3] = time * 3;
            return cb;
        }
    }

    /// <summary>The fixture's textures, read as the interpreter reads them.</summary>
    internal struct FixtureTextures : IGameTextures
    {
        public Fixture F;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        public (float, float, float, float) S1(float u, float v) => GameSampling.Point(F.Art, F.ArtSize, F.ArtSize, u, v);
        public (float, float, float, float) S2(float u, float v) => GameSampling.Point(F.Mask, F.ArtSize, F.ArtSize, u, v);
        public (float, float, float, float) S3(float u, float v) => GameSampling.Bilinear(F.Background, F.BgW, F.BgH, u, v);
    }

    internal static GameInputs InputsAt(Fixture f, Fixture.Case c, int x, int y)
    {
        float u = (x + 0.5f - c.Left) / (2f * f.ArtSize), v = (y + 0.5f - c.Bottom) / (2f * f.ArtSize);
        return new GameInputs
        {
            v1x = 1, v1y = 1, v1z = 1, v1w = 1, v2x = u, v2y = v, v2z = u, v2w = v,
            v3x = (x + 0.5f) / f.ScreenW, v3y = (y + 0.5f) / f.ScreenH, v3z = 0, v3w = 1,
        };
    }

    [Fact]
    public void The_bust_program_draws_every_pixel_as_the_games_program_does()
    {
        var f = Fixture.Load();
        var tex = new FixtureTextures { F = f };
        int pixels = 0, same = 0, aliveWrong = 0, giOffDiffers = 0;
        double worst = 0;
        foreach (var c in f.Cases)
        {
            var cb0 = f.Cb0At(c.Time);
            var cb0Off = (float[])cb0.Clone(); cb0Off[134 * 4] = 0f;   // the control: the fake GI off
            foreach (var p in c.Pixels)
            {
                var v = InputsAt(f, c, p.X, p.Y);
                var reg = new GameRegisters();
                GameBustProgram.Program(ref reg, cb0, f.Cb1, ref tex, in v);
                pixels++;
                if (reg.Discarded == p.Alive) { aliveWrong++; continue; }
                if (!p.Alive) { same++; continue; }
                double d = Math.Max(Math.Max(Math.Abs(reg.o0x - p.R), Math.Abs(reg.o0y - p.G)), Math.Max(Math.Abs(reg.o0z - p.B), Math.Abs(reg.o0w - p.A)));
                worst = Math.Max(worst, d);
                if (d <= 1e-6) same++;
                var off = new GameRegisters();
                GameBustProgram.Program(ref off, cb0Off, f.Cb1, ref tex, in v);
                if (Math.Abs(off.o0x - p.R) * 255 > 1 || Math.Abs(off.o0y - p.G) * 255 > 1 || Math.Abs(off.o0z - p.B) * 255 > 1) giOffDiffers++;
            }
        }
        _out.WriteLine($"{same} of {pixels} pixels as the game's program draws them (worst {worst:E2}); drawn or not wrong at {aliveWrong}; "
                       + $"with the fake GI off, {giOffDiffers} drawn pixels more than a level away");
        Assert.True(giOffDiffers > 2000, "the fixture barely shows the fake GI - it cannot tell a port without it");
        Assert.Equal(0, aliveWrong);
        Assert.Equal(pixels, same);
    }

    internal static FakeGi GiOf(Fixture f, float edgeWidth = -1f)
    {
        float C(int reg, int c) => f.Cb0[reg * 4 + c];
        var settings = new FakeGiSettings
        {
            Enabled = C(134, 0), Intensity = C(134, 1), BlurRadius = C(134, 2), BlurAmount = C(134, 3),
            EdgeWidth = edgeWidth > 0 ? edgeWidth : C(135, 0), Saturation = C(135, 1), Brightness = C(135, 2),
            TintR = C(136, 0), TintG = C(136, 1), TintB = C(136, 2), RimPower = C(137, 0), RimSoftness = C(137, 1),
        };
        return new FakeGi(settings, f.ScreenW, f.ScreenH, f.Background, f.BgW, f.BgH);
    }

    /// <summary>Where a fixture pixel reads the art: its uv displaced as the
    /// program displaces it (JiggleShader.Frame, held to the program by
    /// GameBustShaderTests), and what it reads there.</summary>
    internal static (float u, float v, float r, float g, float b, float a, int tx, int ty) ReadAt(Fixture f, Fixture.Case c, int x, int y)
    {
        float C1(int reg, int k) => f.Cb1[reg * 4 + k];
        var p = new JiggleParams { Speed = C1(3, 0), Strength = C1(3, 1), Frequency = C1(3, 2), NoiseScale = C1(3, 3), NoiseSpeed = C1(4, 0), NoiseStrength = C1(4, 1) };
        var frame = new JiggleShader.Frame(p, c.Time);
        float u = (x + 0.5f - c.Left) / (2f * f.ArtSize), v = (y + 0.5f - c.Bottom) / (2f * f.ArtSize);
        var (mr, mg, mb, ma) = GameSampling.Point(f.Mask, f.ArtSize, f.ArtSize, u, v);
        var (du, dv) = frame.Offset(mr, mg, mb, ma, u, v);
        float uu = u + du, vv = v + dv;
        var (r, g, b, a) = GameSampling.Point(f.Art, f.ArtSize, f.ArtSize, uu, vv);
        int tx = Math.Clamp((int)MathF.Floor(uu * f.ArtSize), 0, f.ArtSize - 1), ty = Math.Clamp((int)MathF.Floor(vv * f.ArtSize), 0, f.ArtSize - 1);
        return (uu, vv, r, g, b, a, tx, ty);
    }

    /// <summary>The fixture art's alpha as bytes, rows from the top, as an art
    /// file has them.</summary>
    internal static byte[] ArtBgra(Fixture f)
    {
        int s = f.ArtSize;
        var bgra = new byte[s * s * 4];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                bgra[((s - 1 - y) * s + x) * 4 + 3] = (byte)MathF.Round(f.Art[(y * s + x) * 4 + 3] * 255f);
        return bgra;
    }

    private sealed class Farthest { public float Texels; }

    private struct RecordingTextures : IGameTextures
    {
        public Farthest Far; public float U, V; public int Size;
        public (float, float, float, float) S0(float u, float v) => (1f, 1f, 1f, 1f);
        public (float, float, float, float) S1(float u, float v)
        {
            Far.Texels = Math.Max(Far.Texels, Math.Max(Math.Abs(u - U), Math.Abs(v - V)) * Size);
            return (0f, 0f, 0f, 0.5f);
        }
        public (float, float, float, float) S2(float u, float v) => (0f, 0f, 0f, 0f);
        public (float, float, float, float) S3(float u, float v) => (0f, 0f, 0f, 1f);
    }

    private static float FarthestRead(FakeGi gi)
    {
        var far = new Farthest();
        var rng = new Random(7);
        var none = new GameInputs();
        var cb1 = new float[GameBustProgram.Cb1Size];
        for (int i = 0; i < 200; i++)
        {
            float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
            var tex = new RecordingTextures { Far = far, U = u, V = v, Size = JiggleShader.Size };
            var reg = new GameRegisters { r0x = u, r0y = v, r1w = 1f };
            GameBustProgram.Gather(ref reg, gi.Cb0, cb1, ref tex, in none);
        }
        return far.Texels;
    }

    /// <summary>The bound that lets most of a bust skip the gather is only sound
    /// if the gather reads within it. Control: twice the edge width reads
    /// beyond it.</summary>
    [Fact]
    public void The_fake_gi_reads_the_art_no_farther_than_its_reach()
    {
        var f = Fixture.Load();
        var gi = GiOf(f);
        float far = FarthestRead(gi), wider = FarthestRead(GiOf(f, f.Cb0[135 * 4] * 2f));
        _out.WriteLine($"the gather reads up to {far:0.00} texels away (reach {gi.Reach}, one of it for where a pixel is in its texel); at twice the edge width {wider:0.00}");
        Assert.True(far <= gi.Reach - 1, $"the gather reads {far} texels away, beyond its reach {gi.Reach - 1}");
        Assert.True(wider > gi.Reach - 1, "twice the edge width reads no farther - the measure is not reading the gather");
    }

    /// <summary>Wherever the bound says the fake GI cannot light a pixel, the
    /// program's own gather finds a rim under its threshold. Control: a bound
    /// that takes the pixel's own alpha for its neighbourhood's skips pixels the
    /// program lights.</summary>
    [Fact]
    public void Where_the_bound_skips_a_pixel_the_program_lights_nothing()
    {
        var f = Fixture.Load();
        var gi = GiOf(f);
        var art = new FakeGi.Art(ArtBgra(f), f.ArtSize, f.ArtSize, gi.Reach);
        var tex = new FixtureTextures { F = f };
        var none = new GameInputs();
        var cb1 = new float[GameBustProgram.Cb1Size];
        int drawn = 0, skipped = 0, wrong = 0, controlWrong = 0;
        foreach (var c in f.Cases)
            foreach (var p in c.Pixels)
            {
                if (!p.Alive) continue;
                var (u, v, _, _, _, a, tx, ty) = ReadAt(f, c, p.X, p.Y);
                int aByte = (int)MathF.Round(a * 255f);
                var reg = new GameRegisters { r0x = u, r0y = v, r1w = a };
                GameBustProgram.Gather(ref reg, gi.Cb0, cb1, ref tex, in none);
                bool lit = a > 0.01f && reg.r0x > 0.0005f;
                drawn++;
                if (!gi.CanLight(art, tx, ty, aByte)) { skipped++; if (lit) wrong++; }
                if (gi.CannotLight(aByte, aByte) && lit) controlWrong++;
            }
        _out.WriteLine($"{skipped} of {drawn} drawn pixels skipped, {wrong} of them lit by the program; the own-alpha bound would skip {controlWrong} lit ones");
        Assert.True(skipped > drawn / 4, "the bound skips too little to be worth having");
        Assert.True(controlWrong > 0, "the control bound skips no lit pixel - the check cannot fail");
        Assert.Equal(0, wrong);
    }

    /// <summary>FakeGi on the fixture's bust: with the program's own blur, the
    /// very numbers the program writes; with the table, within a level of them
    /// on screen.</summary>
    [Fact]
    public void The_fake_gi_lights_a_bust_as_the_games_program_does()
    {
        var f = Fixture.Load();
        var gi = GiOf(f);
        var art = new FakeGi.Art(ArtBgra(f), f.ArtSize, f.ArtSize, gi.Reach);
        gi.PrepareTable(224, -20, 512, 560);
        int drawn = 0, exactSame = 0, tableWithin1 = 0, tableWorst = 0, changed = 0;
        foreach (var c in f.Cases)
            foreach (var p in c.Pixels)
            {
                if (!p.Alive) continue;
                drawn++;
                var (u, v, r, g, b, a, _, _) = ReadAt(f, c, p.X, p.Y);
                float er = r, eg = g, eb = b, tr = r, tg = g, tb = b;
                gi.Apply(art, u, v, p.X + 0.5f, p.Y + 0.5f, ref er, ref eg, ref eb, a, exact: true);
                gi.Apply(art, u, v, p.X + 0.5f, p.Y + 0.5f, ref tr, ref tg, ref tb, a);
                // The program's end: times the light (1 here), at least 0, premultiplied.
                float Pm(float x) => MathF.Max(x, 0f) * a;
                if (Math.Abs(Pm(er) - p.R) <= 1e-6 && Math.Abs(Pm(eg) - p.G) <= 1e-6 && Math.Abs(Pm(eb) - p.B) <= 1e-6) exactSame++;
                if (er != r || eg != g || eb != b) changed++;
                int L(float lin) => (int)MathF.Round(ToSrgb(lin) * 255f);
                int d = Math.Max(Math.Abs(L(Pm(tr)) - L(p.R)), Math.Max(Math.Abs(L(Pm(tg)) - L(p.G)), Math.Abs(L(Pm(tb)) - L(p.B))));
                tableWorst = Math.Max(tableWorst, d);
                if (d <= 1) tableWithin1++;
            }
        _out.WriteLine($"{drawn} drawn pixels, {changed} lit by the fake GI: the exact path writes the program's numbers at {exactSame}; the table within a level at {tableWithin1} (worst {tableWorst} levels)");
        Assert.True(changed > drawn / 10, "the fixture's fake GI lights too little to tell");
        Assert.Equal(drawn, exactSame);
        Assert.True(tableWithin1 >= drawn * 0.999 && tableWorst <= 2, $"the table is {tableWorst} levels off at worst, within one at {tableWithin1} of {drawn}");
    }

    private static float ToSrgb(float l)
    {
        double c = Math.Clamp(l, 0f, 1f);
        return (float)(c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055);
    }
}
