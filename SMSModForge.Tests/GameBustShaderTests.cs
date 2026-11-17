using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The editor's bust shader port (JiggleShader) against the game's own compiled
/// pixel program for busts (Sprites/JiggleSprite_FakeGI_2DLit, Universal2D, the
/// variant without 2D lights), run over synthetic art by a reference interpreter
/// of its disassembly (Tools/Shaders/make_bust_fixtures.py, 1.9.1: the author
/// asked for "a full shader check ... to make sure our editor implementation
/// matches exactly").
/// <para/>
/// Two things are held: where every pixel reads the art (the displacement -
/// bounce, wave, the four-octave noise and its sway, the mask's alpha), and the
/// colour written. The control is 1.9.0's noise, which ported the level
/// shader's: it must land far from the game's, or the fixture cannot tell a
/// wrong noise from a right one.
/// </summary>
public sealed class GameBustShaderTests
{
    private readonly ITestOutputHelper _out;
    public GameBustShaderTests(ITestOutputHelper o) => _out = o;

    private sealed record Case(float Time, JiggleParams Params, float[] Uv, byte[] Colour);

    private sealed record Fixture(int SrcW, int SrcH, int OutW, int OutH, byte[] Art, byte[] Mask, Case[] Cases);

    private static Fixture Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameBustShader", "displacement.bin");
        using var r = new BinaryReader(File.OpenRead(path));
        Assert.Equal("GBS1", new string(r.ReadChars(4)));
        int sw = r.ReadInt32(), sh = r.ReadInt32(), ow = r.ReadInt32(), oh = r.ReadInt32(), n = r.ReadInt32();
        var art = r.ReadBytes(sw * sh * 4);
        var mask = r.ReadBytes(sw * sh * 4);
        var cases = new Case[n];
        for (int i = 0; i < n; i++)
        {
            float t = r.ReadSingle();
            var p = new JiggleParams
            {
                Speed = r.ReadSingle(), Strength = r.ReadSingle(), Frequency = r.ReadSingle(),
                NoiseScale = r.ReadSingle(), NoiseSpeed = r.ReadSingle(), NoiseStrength = r.ReadSingle(),
            };
            var uv = new float[ow * oh * 2];
            for (int k = 0; k < uv.Length; k++) uv[k] = r.ReadSingle();
            cases[i] = new Case(t, p, uv, r.ReadBytes(ow * oh * 4));
        }
        return new Fixture(sw, sh, ow, oh, art, mask, cases);
    }

    /// <summary>Where the port reads the art at output pixel (x, y): the mask
    /// read undisplaced, as Render reads it, and the frame's offset added.</summary>
    private static (float u, float v) Reads(Fixture f, JiggleShader.Frame frame, int x, int y)
    {
        float u = (x + 0.5f) / f.OutW, v = 1.0f - (y + 0.5f) / f.OutH;
        int mx = Math.Clamp((int)MathF.Floor(u * f.SrcW), 0, f.SrcW - 1);
        int my = Math.Clamp((int)MathF.Floor(v * f.SrcH), 0, f.SrcH - 1);
        int mi = (f.SrcH - 1 - my) * f.SrcW * 4 + mx * 4;
        var (du, dv) = frame.Offset(f.Mask[mi + 2] / 255f, f.Mask[mi + 1] / 255f, f.Mask[mi] / 255f, f.Mask[mi + 3] / 255f, u, v);
        return (u + du, v + dv);
    }

    [Fact]
    public void EveryPixelReadsTheArtWhereTheGamesProgramReadsIt()
    {
        var f = Load();
        foreach (var c in f.Cases)
        {
            var frame = new JiggleShader.Frame(c.Params, c.Time);
            var grid = new JiggleShader.Grid(c.Params, c.Time, f.OutW, f.OutH);
            double worst = 0, worstGrid = 0;
            for (int y = 0; y < f.OutH; y++)
                for (int x = 0; x < f.OutW; x++)
                {
                    var (u, v) = Reads(f, frame, x, y);
                    int k = (y * f.OutW + x) * 2;
                    worst = Math.Max(worst, Math.Max(Math.Abs(u - c.Uv[k]), Math.Abs(v - c.Uv[k + 1])));

                    // The per-frame tables Render uses: the same numbers.
                    int mx = Math.Clamp((int)MathF.Floor(grid.U[x] * f.SrcW), 0, f.SrcW - 1);
                    int my = Math.Clamp((int)MathF.Floor(grid.V[y] * f.SrcH), 0, f.SrcH - 1);
                    int mi = (f.SrcH - 1 - my) * f.SrcW * 4 + mx * 4;
                    var (gu, gv) = grid.Offset(x, y, f.Mask[mi + 2] / 255f, f.Mask[mi + 1] / 255f, f.Mask[mi] / 255f, f.Mask[mi + 3] / 255f);
                    worstGrid = Math.Max(worstGrid, Math.Max(Math.Abs(grid.U[x] + gu - c.Uv[k]), Math.Abs(grid.V[y] + gv - c.Uv[k + 1])));
                }
            _out.WriteLine($"t={c.Time}: largest difference from the game's program {worst:0.0e0} uv (the frame's tables: {worstGrid:0.0e0})");
            Assert.True(worst < 1e-5, $"t={c.Time}: the port reads the art {worst} uv away from where the game does");
            Assert.True(worstGrid < 1e-5, $"t={c.Time}: the per-frame tables read the art {worstGrid} uv away from where the game does");
        }
    }

    [Fact]
    public void EveryPixelTakesTheColourTheGamesProgramWrites()
    {
        var f = Load();
        foreach (var c in f.Cases)
        {
            var output = new byte[f.OutW * f.OutH * 4];
            JiggleShader.Render(f.Art, f.Mask, f.SrcW, f.SrcH, c.Params, (1f, 1f, 1f, 1f), c.Time, output, f.OutW, f.OutH);
            int differ = 0;
            for (int i = 0; i < output.Length; i += 4)
                if (output[i] != c.Colour[i] || output[i + 1] != c.Colour[i + 1] || output[i + 2] != c.Colour[i + 2] || output[i + 3] != c.Colour[i + 3])
                    differ++;
            _out.WriteLine($"t={c.Time}: {differ} of {output.Length / 4} pixels differ from the game's program");
            // A pixel whose uv lands within a rounding of a texel's edge may take
            // the next texel; nothing else may differ.
            Assert.True(differ <= output.Length / 4 / 2000, $"t={c.Time}: {differ} pixels differ");
        }
    }

    /// <summary>The control: 1.9.0's noise (the level shader's gradient noise,
    /// timed by Noise Speed alone), copied here as it was. It must land far from
    /// where the game's program reads, where the bust's port lands on it.</summary>
    [Fact]
    public void TheNoiseOf1_9_0IsFarFromTheGames_TheControl()
    {
        var f = Load();
        var c = f.Cases[1];
        var p = c.Params;
        double worst = 0;
        for (int y = 0; y < f.OutH; y++)
            for (int x = 0; x < f.OutW; x++)
            {
                float u = (x + 0.5f) / f.OutW, v = 1.0f - (y + 0.5f) / f.OutH;
                int mx = Math.Clamp((int)(u * f.SrcW), 0, f.SrcW - 1), my = Math.Clamp((int)((1 - v) * f.SrcH), 0, f.SrcH - 1);
                int mi = my * f.SrcW * 4 + mx * 4;
                float ma = f.Mask[mi + 3] / 255f, mr = f.Mask[mi + 2] / 255f, mg = f.Mask[mi + 1] / 255f, mb = f.Mask[mi] / 255f;
                float du = MathF.Sin(v * p.Frequency + c.Time * p.Speed) * p.Strength * mg * ma;
                float dv = MathF.Sin(c.Time * p.Speed) * p.Strength * mr * ma;
                float amp = p.NoiseStrength * mb * ma;
                float ncx = u * p.NoiseScale + c.Time * p.NoiseSpeed, ncy = v * p.NoiseScale + c.Time * p.NoiseSpeed;
                du += OldGradientNoise(ncx, ncy) * amp;
                dv += OldGradientNoise(ncx + 100f, ncy + 100f) * amp;
                int k = (y * f.OutW + x) * 2;
                worst = Math.Max(worst, Math.Max(Math.Abs(u + du - c.Uv[k]), Math.Abs(v + dv - c.Uv[k + 1])));
            }
        _out.WriteLine($"1.9.0's noise: up to {worst:0.0000} uv from the game's ({worst * 512:0.0} preview pixels)");
        Assert.True(worst > 0.005, "1.9.0's noise lands on the game's - the fixture cannot tell them apart");
    }

    private static float OldGradientNoise(float px, float py)
    {
        static (float, float) Hash(float ix, float iy)
        {
            float g1 = MathF.Sin(ix * 127.1f + iy * 311.7f) * 43758.5453f;
            float g2 = MathF.Sin(ix * 269.5f + iy * 183.3f) * 43758.5453f;
            g1 -= MathF.Floor(g1);
            g2 -= MathF.Floor(g2);
            return (g1 * 2f - 1f, g2 * 2f - 1f);
        }
        float ix = MathF.Floor(px), iy = MathF.Floor(py), fx = px - ix, fy = py - iy;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
        var (a0, a1) = Hash(ix, iy);
        var (b0, b1) = Hash(ix + 1f, iy);
        var (c0, c1) = Hash(ix, iy + 1f);
        var (d0, d1) = Hash(ix + 1f, iy + 1f);
        float n00 = a0 * fx + a1 * fy, n10 = b0 * (fx - 1f) + b1 * fy, n01 = c0 * fx + c1 * (fy - 1f), n11 = d0 * (fx - 1f) + d1 * (fy - 1f);
        float bottom = n00 + ux * (n10 - n00), top = n01 + ux * (n11 - n01);
        return bottom + uy * (top - bottom);
    }

    /// <summary>A tint darkens light, as the game (linear colour space) applies
    /// it: half-strength grey on a mid colour is half its light, not half its
    /// number. White changes nothing at all.</summary>
    [Fact]
    public void ATintDarkensLight_AndWhiteLeavesTheArtAsItIs()
    {
        var art = new byte[JiggleShader.Stride * JiggleShader.Size];
        for (int i = 0; i < art.Length; i += 4) { art[i] = 0x60; art[i + 1] = 0x80; art[i + 2] = 0xC0; art[i + 3] = 255; }
        var mask = new byte[art.Length];
        var output = new byte[JiggleShader.RenderStride * JiggleShader.RenderSize];
        JiggleShader.Render(art, mask, new JiggleParams(), (1f, 1f, 1f, 1f), 1f, output);
        Assert.Equal(new byte[] { 0x60, 0x80, 0xC0, 255 }, output.Take(4).ToArray());

        double half = Math.Pow((0.5 + 0.055) / 1.055, 2.4);    // a grey of 0.5 (sRGB), in linear light
        JiggleShader.Render(art, mask, new JiggleParams(), (0.5f, 0.5f, 0.5f, 1f), 1f, output);
        byte[] was = { 0x60, 0x80, 0xC0 };
        for (int ch = 0; ch < 3; ch++)
        {
            double c = was[ch] / 255.0;
            double lin = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            double l = lin * half;
            int expect = (int)Math.Round((l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055) * 255);
            Assert.InRange(output[ch], expect - 1, expect + 1);
            Assert.NotEqual((int)Math.Round(was[ch] * 0.5), output[ch]);      // not the sRGB arithmetic
        }
    }
}
