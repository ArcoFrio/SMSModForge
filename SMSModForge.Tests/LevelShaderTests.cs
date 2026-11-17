using System;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The editor's level shader port (MilkingShader) against the game's compiled
/// program for a pack place's material (Sprite-Lit-Jiggle, _JIGGLETYPE_MILKING,
/// no 2D lights - a clone of Beach.mat), run over synthetic art by a reference
/// interpreter of its disassembly (Tools/Shaders/make_level_fixtures.py; 1.9.1,
/// "a full shader check"). Each case's colour must be the game's to within one
/// level a channel (bilinear sampling rounds). The control: the same art with no
/// displacement at all, which must differ, or the cases do not move enough to
/// prove anything.
/// </summary>
public sealed class LevelShaderTests
{
    private readonly ITestOutputHelper _out;
    public LevelShaderTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void EveryPixelTakesTheColourTheGamesProgramWrites()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameBustShader", "level.bin");
        using var r = new BinaryReader(File.OpenRead(path));
        Assert.Equal("GLS1", new string(r.ReadChars(4)));
        int aw = r.ReadInt32(), ah = r.ReadInt32(), mw = r.ReadInt32(), mh = r.ReadInt32(), ow = r.ReadInt32(), oh = r.ReadInt32(), n = r.ReadInt32();
        var art = r.ReadBytes(aw * ah * 4);
        var mask = r.ReadBytes(mw * mh * 4);
        var still = new byte[ow * oh * 4];
        MilkingShader.Render(art, aw, ah, mask, mw, mh,
            new MilkingShader.Settings(0f, 0f, 0f, 0f, 3f, 0.1f, 0f, 1f), 0f, still, ow, oh);

        for (int i = 0; i < n; i++)
        {
            float t = r.ReadSingle(), speed = r.ReadSingle(), strength = r.ReadSingle(), phase = r.ReadSingle(),
                  variation = r.ReadSingle(), waves = r.ReadSingle(), compression = r.ReadSingle(),
                  dirX = r.ReadSingle(), dirY = r.ReadSingle();
            r.ReadSingle();   // jiggle type: milking runs at every value the cases use
            var expected = r.ReadBytes(ow * oh * 4);
            var output = new byte[ow * oh * 4];
            MilkingShader.Render(art, aw, ah, mask, mw, mh,
                new MilkingShader.Settings(speed, strength, phase, variation, waves, compression, dirX, dirY), t, output, ow, oh);
            int off = 0, worst = 0, moved = 0;
            for (int k = 0; k < output.Length; k++)
            {
                int d = Math.Abs(output[k] - expected[k]);
                if (d > 1) off++;
                worst = Math.Max(worst, d);
                if (expected[k] != still[k]) moved++;
            }
            _out.WriteLine($"t={t} strength={strength} dir=({dirX},{dirY}): {off} channels off by more than 1 (worst {worst}); {moved} channels moved by the game");
            Assert.True(off == 0, $"t={t}, dir=({dirX},{dirY}): {off} channels differ from the game's program by more than 1 (worst {worst})");
            if (strength >= 0.04f) Assert.True(moved > 1000, "the case hardly moves - it proves nothing");
        }
    }
}
