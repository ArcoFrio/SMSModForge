using System;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The previews draw a bust at the game's density on a 1920x1080 screen or
/// larger (1.9.1, the author: "all quality options are showing the
/// 'scanline'-like movement I mentioned before, especially on the green layer
/// movement"). The game's Pixel Perfect Camera renders at twice 960x540 there,
/// four screen rows a texel; the preview's frame is the 960x540 size, two rows
/// a texel, so the Wave layer moved its rows in steps twice the game's.
/// <para/>
/// Measured on what is drawn: the zoom-2 frame is the game's x2 picture (the
/// same pass at twice the size, one sample a pixel - held to the game's
/// program by GameBustShaderTests) with each 2x2 block averaged, within the
/// rounding of one level. And the scanlines themselves: a straight upright
/// edge on a Wave-only mask, drawn row by row where the wave puts it; how far
/// each row's edge lies from that, on average. Control: the old one-sample
/// frame strays visibly further.
/// </summary>
public sealed class GameZoomTests
{
    private readonly ITestOutputHelper _out;
    public GameZoomTests(ITestOutputHelper o) => _out = o;

    private const int S = JiggleShader.Size, N = JiggleShader.RenderSize, Edge = 128;

    private static (byte[] art, byte[] mask) WaveEdge()
    {
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = (y * S + x) * 4;
                if (x < Edge) { art[i] = 60; art[i + 1] = 140; art[i + 2] = 220; art[i + 3] = 255; }
                mask[i + 1] = 255; mask[i + 3] = 255;          // green (Wave) only, everywhere
            }
        return (art, mask);
    }

    private static readonly JiggleParams Wave = new() { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseStrength = 0f };

    [Fact]
    public void TheZoom2FrameIsTheGamesX2PictureAtThePreviewsSize_AndItsWaveHasNoScanlines()
    {
        var (art, mask) = WaveEdge();
        const float Time = 7.3f;
        var big = new byte[N * 2 * N * 2 * 4];
        JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), Time, big, N * 2, N * 2, zoom: 1);
        var zoomed = new byte[N * N * 4];
        JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), Time, zoomed, N, N, zoom: JiggleShader.GameZoom);
        var single = new byte[N * N * 4];
        JiggleShader.Render(art, mask, S, S, Wave, (1f, 1f, 1f, 1f), Time, single, N, N, zoom: 1);

        // The x2 picture, each 2x2 block averaged.
        int worst = 0, singleOff = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                            sum += big[((y * 2 + dy) * N * 2 + x * 2 + dx) * 4 + c];
                    double expected = sum / 4.0;
                    int o = (y * N + x) * 4 + c;
                    worst = Math.Max(worst, (int)Math.Ceiling(Math.Abs(zoomed[o] - expected) - 1e-9));
                    if (Math.Abs(single[o] - expected) > 1.0) singleOff++;
                }
        _out.WriteLine($"zoom 2 against the x2 picture averaged: at most {worst} levels off; the one-sample frame is more than a level off at {singleOff} values");
        Assert.True(worst <= 1, $"the zoom-2 frame is {worst} levels from the game's x2 picture at the preview's size");
        Assert.True(singleOff > 500, "control: the one-sample frame passes for the x2 picture");

        // Scanlines: each row's edge (its coverage) against where the wave puts it.
        var frame = new JiggleShader.Frame(Wave, Time);
        double Stray(byte[] px)
        {
            double total = 0;
            for (int y = 0; y < N; y++)
            {
                double covered = 0;
                for (int x = 0; x < N; x++) covered += px[(y * N + x) * 4 + 3] / 255.0;
                float v = 1f - (y + 0.5f) / N;
                var (du, _) = frame.Offset(0f, 1f, 0f, 1f, 0.5f, v);
                double ideal = (double)Edge / S * N - du * N;
                total += Math.Abs(covered - ideal);
            }
            return total / N;
        }
        double strayZoomed = Stray(zoomed), straySingle = Stray(single);
        _out.WriteLine($"the edge strays from where the wave puts it by {strayZoomed:0.000} preview pixels a row at zoom 2, {straySingle:0.000} at one sample a pixel");
        Assert.True(straySingle > 0.15, $"control: the one-sample frame's edge strays only {straySingle:0.000} - the measure does not see the steps");
        Assert.True(strayZoomed < straySingle * 0.65, $"the zoom-2 edge strays {strayZoomed:0.000}, not clearly less than one sample's {straySingle:0.000}");

        // The same over the room (BackdropRender.Body, the bust preview's pass
        // there): its coverage, in the frame the bust fills.
        double BodyStray(int zoom)
        {
            var body = new float[N * N * 4];
            BackdropRender.Body(art, mask, Wave, (1f, 1f, 1f, 1f), Time, null, null, 0, 0, 0, body, true, null, 0f, zoom: zoom);
            var px = new byte[N * N * 4];
            for (int i = 3; i < px.Length; i += 4) px[i] = (byte)Math.Round(body[i] * 255f);
            return Stray(px);
        }
        double bodyZoomed = BodyStray(JiggleShader.GameZoom), bodySingle = BodyStray(1);
        _out.WriteLine($"over the room: {bodyZoomed:0.000} at zoom 2, {bodySingle:0.000} at one sample");
        Assert.True(bodySingle > 0.15, "control: the room pass's one-sample edge does not stray");
        Assert.True(bodyZoomed < bodySingle * 0.65, $"over the room the zoom-2 edge strays {bodyZoomed:0.000}, not clearly less than {bodySingle:0.000}");
    }
}
