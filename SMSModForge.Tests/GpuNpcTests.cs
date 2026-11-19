using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The NPC preview's frame over a backdrop on the graphics card
/// (GpuFrame.DrawNpc, 1.9.1) is the processor's (NpcRender), step for step as
/// NpcPreview runs them: the room - the PC's room through its lamp, the Forest
/// as captured - the NPC through the bust's program at its size, lit by its
/// layer's light, its edge glow from the room, its blink, the lamp's glow, the
/// post-processing - the NPC moved by every mask channel, at the game's zoom.
/// Compared on the screen's bytes, within Direct3D's rounding. Controls: the
/// card without the edge glow, and the processor without the room's light, are
/// visibly not it.
/// </summary>
public sealed class GpuNpcTests
{
    private readonly ITestOutputHelper _out;
    public GpuNpcTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData("MyRoom", true)]
    [InlineData("Forest", false)]
    [InlineData("Beach", false)]   // a lamp whose glow is drawn with the room (1.9.2): still over the NPC
    public void TheCardsNpcFrameIsTheProcessors(string backdropName, bool withBlink)
    {
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", backdropName)));
        scene.Prepare();
        const int W = 240, H = 480, N = BackdropRender.FrameSize;
        var art = new byte[W * H * 4];
        var mask = new byte[W * H * 4];
        var blink = withBlink ? new byte[W * H * 4] : null;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x * 256 / W, y * 256 / H);
                int i = (y * W + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = (byte)(x * 255 / W); mask[i + 1] = (byte)(y * 255 / H); mask[i + 2] = 180; mask[i + 3] = 255;
                if (blink != null && Math.Abs(x - 120) < 20 && Math.Abs(y - 150) < 6) { blink[i] = 30; blink[i + 1] = 20; blink[i + 2] = 20; blink[i + 3] = 255; }
            }
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var tint = (1f, 1f, 1f, 1f);
        const float Time = 3.7f;
        var gi = NpcRender.Gi(scene.Backdrop);
        var giArt = new FakeGi.Art(art, W, H, NpcRender.Reach(scene.Backdrop.Gi, W, H));
        var blinkArt = blink != null ? new FakeGi.Art(blink, W, H, NpcRender.Reach(scene.Backdrop.Gi, W, H)) : null;
        var light = new NpcLight(scene);
        float scale = NpcRender.ScreenPerArtPixel(0.79f), width = W * scale, height = H * scale;
        float left = scene.Left + MathF.Round((N - width) / 2f * 2f) / 2f, bottom = scene.FrameBottom + MathF.Round((N - height) / 2f * 2f) / 2f;
        int zoom = JiggleShader.GameZoom;
        var parts = BackdropRender.PostParts.All;

        byte[] Cpu(bool lit)
        {
            var body = new float[N * N * 4];
            Func<float, float, (float, float, float)> l = lit ? light.At : (_, _) => (1f, 1f, 1f);
            NpcRender.Body(art, mask, W, H, p, tint, Time, gi, giArt, l, left, bottom, width, height, scene.Left, scene.FrameBottom, body, true, scene.Decode, zoom);
            float[]? blinkBody = null;
            if (blink != null)
            {
                blinkBody = new float[N * N * 4];
                NpcRender.Body(blink, mask, W, H, p, tint, Time, gi, blinkArt, l, left, bottom, width, height, scene.Left, scene.FrameBottom, blinkBody, true, scene.Decode, zoom);
            }
            var output = new byte[N * N * 4];
            NpcRender.Compose(scene, BackdropRender.Scene(scene), new float[scene.Backdrop.BackgroundWidth * scene.Backdrop.BackgroundHeight * 4], new float[N * N],
                              parts, output, body, blinkBody);
            return output;
        }
        byte[] Card(bool giOn)
        {
            var output = new byte[N * N * 4];
            card!.DrawNpc(scene, gi, giOn, light, art, blink, mask, W, H, p, tint, Time, left, bottom, width, height, parts, zoom, output);
            return output;
        }

        var cpu = Cpu(true);
        var onCard = Card(true);
        int same = 0, overOne = 0;
        for (int i = 0; i < cpu.Length; i++)
        {
            int d = Math.Abs(cpu[i] - onCard[i]);
            if (d == 0) same++;
            if (d > 1) overOne++;
        }
        var noGi = Card(false);
        int noGiOff = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - noGi[i]) > 1);
        var unlit = Cpu(false);
        int unlitOff = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(unlit[i] - onCard[i]) > 1);
        _out.WriteLine($"{backdropName}{(withBlink ? " with a blink" : "")}: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off; "
                       + $"the card without the edge glow {noGiOff} over a level; the processor without the room's light {unlitOff}");
        Assert.True(noGiOff > 300, "control: the card without the edge glow passes for the processor's frame with it");
        if (scene.Lamp != null) Assert.True(unlitOff > 1000, "control: without the room's light the processor's NPC passes for the card's lit one");
        Assert.True(overOne <= cpu.Length / 5000, $"the card's NPC frame is more than a level from the processor's at {overOne} bytes");
        Assert.True(same > cpu.Length * 0.98, $"only {same} of {cpu.Length} bytes are the processor's");
    }
}
