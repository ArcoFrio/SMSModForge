using System;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The bust's body over the room on the graphics card (GpuBody: the game's
/// whole bust program at every one of the game's pixels) is what the
/// processor draws (BackdropRender.Body, the program in parts, held to the
/// game by BackdropRenderTests and the in-game suite): the person's shape
/// moving by every layer, its edge glow from the shipped room, tinted, at the
/// game's x1 and x2, and the intro's silhouette. Within Direct3D's rounding
/// (GpuDeviceTests): a few millionths. Control: the card without the fake GI
/// is visibly not the processor's frame with it.
/// </summary>
public sealed class GpuBodyTests
{
    private readonly ITestOutputHelper _out;
    public GpuBodyTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void TheCardDrawsTheBodyAsTheProcessorDoes(int zoom, bool tinted)
    {
        using var body = GpuBody.TryCreate();
        Assert.True(body != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        var gi = scene.Prepare();
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * S + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                // Every layer, varying over the art.
                mask[i] = (byte)(x * 255 / S); mask[i + 1] = (byte)(y * 255 / S); mask[i + 2] = 200; mask[i + 3] = 255;
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var tint = tinted ? (0.9f, 0.7f, 0.8f, 0.9f) : (1f, 1f, 1f, 1f);
        var giArt = new FakeGi.Art(art, S, S, gi.Reach);
        int bustBottom = backdrop.BustRestBottom + 2;
        const float Time = 9.37f, Silhouette = 0.6f;

        var cpu = new float[N * N * 4];
        var cpuSil = new float[N * N * 4];
        BackdropRender.Body(art, mask, p, tint, Time, gi, giArt, scene.Left, scene.FrameBottom, bustBottom, cpu, true, cpuSil, Silhouette, true, scene.Decode, zoom);
        var card = new float[N * N * 4];
        var cardSil = new float[N * N * 4];
        body!.Draw(art, mask, p, tint, Time, gi, true, scene.Left, scene.FrameBottom, bustBottom, card, true, cardSil, Silhouette, scene.Decode, zoom);
        var cardOff = new float[N * N * 4];
        body.Draw(art, mask, p, tint, Time, gi, false, scene.Left, scene.FrameBottom, bustBottom, cardOff, true, null, 0f, scene.Decode, zoom);

        (double worst, int overLevel) Compare(float[] a, float[] b)
        {
            double w = 0; int n = 0;
            for (int i = 0; i < a.Length; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                w = Math.Max(w, d);
                if (d * 255 > 0.5) n++;
            }
            return (w, n);
        }
        var (worst, over) = Compare(cpu, card);
        var (worstSil, overSil) = Compare(cpuSil, cardSil);
        var (_, overOff) = Compare(cpu, cardOff);
        _out.WriteLine($"zoom {zoom}, tinted {tinted}: body worst {worst:E2} ({over} values over half a level), silhouette worst {worstSil:E2} ({overSil}); "
                       + $"the card without the fake GI: {overOff} values over half a level from the processor's with it");
        Assert.True(overOff > 1000, "control: the card's body without the fake GI passes for the processor's with it");
        // Ties: where a moved read lands on a texel's very edge, the card's
        // rounding can take the texel beside it (measured: 3 to 6 values in a
        // million, 2026-11-16) - on the card, as the game's own frame does.
        Assert.True(over <= cpu.Length / 10000, $"the card's body is over half a level from the processor's at {over} values - more than ties");
        Assert.True(overSil <= cpu.Length / 10000, $"the card's silhouette is over half a level off at {overSil} values");
    }
}
