using System;
using System.Linq;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's level art moved by its mask on the graphics card (GpuMilking) is
/// the processor's (MilkingShader.Render, held to the game's level program by
/// LevelShaderTests): striped art under a mask that varies across it, the
/// level's own settings and a slanted direction (the per-pixel path), at the
/// art's size and smaller. Within a level (the card's sine), nearly all
/// bytes equal. Control: a moment later is not it.
/// </summary>
public sealed class GpuMilkingTests
{
    private readonly ITestOutputHelper _out;
    public GpuMilkingTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(2048, 1136, 2048, 1136, false)]
    [InlineData(2048, 1136, 1024, 568, false)]
    [InlineData(600, 400, 600, 400, true)]
    public void TheCardMovesALevelAsTheProcessorDoes(int sw, int sh, int ow, int oh, bool slanted)
    {
        using var card = GpuMilking.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var art = new byte[sw * sh * 4];
        for (int y = 0; y < sh; y++)
            for (int x = 0; x < sw; x++)
            {
                int i = (y * sw + x) * 4;
                art[i] = (byte)(x * 7 % 256); art[i + 1] = (byte)(y * 5 % 256); art[i + 2] = (byte)((x / 16 + y / 16) % 2 * 200); art[i + 3] = 255;
            }
        const int M = 256;
        var mask = new byte[M * M * 4];
        for (int y = 0; y < M; y++)
            for (int x = 0; x < M; x++) mask[(y * M + x) * 4 + 3] = (byte)(x * 255 / M);
        var level = MilkingShader.Settings.Level;
        var s = slanted ? new MilkingShader.Settings(level.Speed, 0.02f, 0.3f, 0.5f, level.WaveCount, level.CompressionRatio, 0.6f, 0.8f) : level;
        const float Time = 3.7f;
        var cpu = new byte[ow * oh * 4];
        MilkingShader.Render(art, sw, sh, mask, M, M, s, Time, cpu, ow, oh);
        var onCard = new byte[ow * oh * 4];
        card!.Draw(art, sw, sh, mask, M, M, s, Time, onCard, ow, oh);
        var later = new byte[ow * oh * 4];
        card.Draw(art, sw, sh, mask, M, M, s, Time + 0.4f, later, ow, oh);
        int same = 0, overOne = 0;
        for (int i = 0; i < cpu.Length; i++)
        {
            int d = Math.Abs(cpu[i] - onCard[i]);
            if (d == 0) same++;
            if (d > 1) overOne++;
        }
        int control = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - later[i]) > 1);
        _out.WriteLine($"{sw}x{sh} into {ow}x{oh}, slanted {slanted}: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off; a moment later {control}");
        Assert.True(control > 1000, "control: a moment later passes for this frame");
        Assert.True(overOne <= cpu.Length / 1000, $"the card is more than a level from the processor at {overOne} bytes");
        Assert.True(same > cpu.Length * 0.95, $"only {same} of {cpu.Length} bytes are the processor's");
    }
}
