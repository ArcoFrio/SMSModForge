using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A bust with no backdrop and an NPC's pose on the graphics card (GpuAlone:
/// the game's bust program, fake GI off) are the processor's
/// (JiggleShader.Render, held to the game's program by GameBustShaderTests):
/// the person's shape moving by every layer, white and tinted, at x1 and the
/// game's x2, a square bust and a tall pose drawn smaller than its art.
/// Compared on the screen's bytes, within Direct3D's rounding and the
/// texel-edge ties. Control: the card's frame a moment later is not it.
/// </summary>
public sealed class GpuAloneTests
{
    private readonly ITestOutputHelper _out;
    public GpuAloneTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(256, 256, 512, 512, 1, false)]
    [InlineData(256, 256, 512, 512, 2, false)]
    [InlineData(256, 256, 512, 512, 2, true)]
    [InlineData(240, 600, 205, 512, 2, false)]
    public void TheCardDrawsAJiggleAloneAsTheProcessorDoes(int sw, int sh, int ow, int oh, int zoom, bool tinted)
    {
        using var card = GpuAlone.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var art = new byte[sw * sh * 4];
        var mask = new byte[sw * sh * 4];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x * 256 / sw, y * 256 / sh);
                int i = (y * sw + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = (byte)(x * 255 / sw); mask[i + 1] = (byte)(y * 255 / sh); mask[i + 2] = 200; mask[i + 3] = 255;
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var tint = tinted ? (0.9f, 0.7f, 0.8f, 0.9f) : (1f, 1f, 1f, 1f);
        const float Time = 4.4f;
        var cpu = new byte[ow * oh * 4];
        JiggleShader.Render(art, mask, sw, sh, p, tint, Time, cpu, ow, oh, zoom);
        var onCard = new byte[ow * oh * 4];
        card!.Draw(art, mask, sw, sh, p, tint, Time, onCard, ow, oh, zoom);
        var later = new byte[ow * oh * 4];
        card.Draw(art, mask, sw, sh, p, tint, Time + 0.37f, later, ow, oh, zoom);
        int same = 0, overOne = 0;
        for (int i = 0; i < cpu.Length; i++)
        {
            int d = Math.Abs(cpu[i] - onCard[i]);
            if (d == 0) same++;
            if (d > 1) overOne++;
        }
        int control = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - later[i]) > 1);
        _out.WriteLine($"{sw}x{sh} into {ow}x{oh}, zoom {zoom}, tinted {tinted}: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off; a moment later {control}");
        Assert.True(control > 1000, "control: the card's frame a moment later passes for this one");
        Assert.True(overOne <= cpu.Length / 2000, $"the card is more than a level from the processor at {overOne} bytes");
        Assert.True(same > cpu.Length * 0.97, $"only {same} of {cpu.Length} bytes are the processor's");
    }

    /// <summary>Each preview quality's sampling with no backdrop (1.9.1:
    /// PreviewQualityDef.Sampling - fewer samples below Ultra, Low a coarser
    /// grid): the card's picture is the processor's at every one.</summary>
    [Fact]
    public void TheCardSamplesAsTheProcessorAtEveryQuality()
    {
        using var card = GpuAlone.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        const int S = 256, O = 512;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * S + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = (byte)x; mask[i + 1] = (byte)y; mask[i + 2] = 200; mask[i + 3] = 255;
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        foreach (var quality in Services.PreviewQualityManager.All)
        {
            var cpu = new byte[O * O * 4];
            JiggleShader.Render(art, mask, S, S, p, (1f, 1f, 1f, 1f), 2.2f, cpu, O, O, quality.Sampling);
            var onCard = new byte[O * O * 4];
            card!.Draw(art, mask, S, S, p, (1f, 1f, 1f, 1f), 2.2f, onCard, O, O, quality.Sampling);
            int same = 0, overOne = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                int d = Math.Abs(cpu[i] - onCard[i]);
                if (d == 0) same++;
                if (d > 1) overOne++;
            }
            _out.WriteLine($"{quality.Name} ({quality.Sampling}): {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off");
            Assert.True(overOne <= cpu.Length / 2000, $"{quality.Name}: the card is more than a level from the processor at {overOne} bytes");
            Assert.True(same > cpu.Length * 0.97, $"{quality.Name}: only {same} of {cpu.Length} bytes are the processor's");
        }
    }
}
