using System;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Vortice.DXGI;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Every drawing on the graphics card writes all it reads (1.9.1). Direct3D
/// leaves a new buffer or picture undefined: zeros as a rule, but beside other
/// previews at work, memory one of them has just let go of - so a drawing that
/// read a part it had not written would be right on a quiet machine and wrong
/// on a busy one. Here every new one is made full of zeros (the quiet machine)
/// and then full of white (1.0): each drawing is the same picture both ways.
/// Control: the fill lands.
/// </summary>
public sealed class GpuNewContentTests
{
    private const uint Fill = 0x3F800000;   // 1.0f
    private readonly ITestOutputHelper _out;
    public GpuNewContentTests(ITestOutputHelper o) => _out = o;

    private static T With<T>(uint? fill, Func<T> draw)
    {
        GpuDevice.NewContentForTests = fill;
        try { return draw(); }
        finally { GpuDevice.NewContentForTests = null; }
    }

    private void Same(string what, byte[] plain, byte[] filled)
    {
        int off = Enumerable.Range(0, plain.Length).Count(i => Math.Abs(plain[i] - filled[i]) > 1);
        _out.WriteLine($"{what}: {off} of {plain.Length} bytes more than a level apart, made new full of white rather than zeros");
        Assert.True(off == 0, $"{what}: made new full of white rather than zeros, {off} of {plain.Length} bytes differ - it reads what it did not write");
    }

    private static BackdropScene Scene(string name)
    {
        var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", name)));
        scene.Prepare();
        return scene;
    }

    private static (byte[] art, byte[] mask) Figure(int w, int h)
    {
        var art = new byte[w * h * 4];
        var mask = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x * 256 / w, y * 256 / h);
                int i = (y * w + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = (byte)(x * 255 / w); mask[i + 1] = (byte)(y * 255 / h); mask[i + 2] = 200; mask[i + 3] = 255;
            }
        return (art, mask);
    }

    /// <summary>Everything on, and the bloom off: with no bloom the last pass reads a picture of none.</summary>
    private static readonly BackdropRender.PostParts[] Parts = { BackdropRender.PostParts.All, new(false, false, true), new(false, false, false) };

    private static readonly JiggleParams Moving = new() { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };

    [Fact]
    public void ControlTheFillLands()
    {
        var gpu = GpuDevice.Shared;
        Assert.True(gpu != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var read = new float[16 * 16 * 4];
        lock (gpu!.Gate)
        {
            using var t = With(Fill, () => GpuTexture.Create<float>(gpu, 16, 16, Format.R32G32B32A32_Float, true));
            using var back = new GpuReadback(gpu, 16, 16, Format.R32G32B32A32_Float);
            back.Read<float>(gpu, t, read);
        }
        Assert.All(read, v => Assert.Equal(1f, v));
    }

    [Theory]
    [InlineData("MyRoom")]
    [InlineData("Forest")]
    public void TheBustsFrame(string backdropName)
    {
        var scene = Scene(backdropName);
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var (art, mask) = Figure(S, S);
        int bustBottom = scene.Backdrop.BustRestBottom + 1;
        var lamp = scene.Lamp;
        var shadow = lamp != null ? new float[lamp.Shadow.W * lamp.Shadow.H] : null;
        if (lamp != null)
            lamp.Shadow.Draw(new BustShadow.Caster(art, S, S, 100f), scene.Left + N / 2f, scene.Backdrop.BustRestBottom + 0.6f + N / 2f, 2f, 1f, shadow!);
        foreach (var parts in Parts)
        {
            byte[] Draw()
            {
                using var card = GpuFrame.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[N * N * 4];
                if (lamp != null)
                    card!.Draw(lamp, shadow!, scene, art, mask, Moving, (1f, 1f, 1f, 1f), 6.21f, true, bustBottom, true, 0.4f, null, null, null,
                               parts, JiggleShader.GameZoom, output);
                else
                    card!.DrawNoLamp(scene, art, mask, Moving, (1f, 1f, 1f, 1f), 6.21f, true, bustBottom, true, 0.4f, null, null, null,
                                     parts, JiggleShader.GameZoom, output);
                return output;
            }
            Same($"the bust over {backdropName}, {parts}", With(0u, Draw), With(Fill, Draw));
        }
    }

    [Theory]
    [InlineData("MyRoom")]
    [InlineData("Forest")]
    public void TheNpcsFrame(string backdropName)
    {
        var scene = Scene(backdropName);
        const int W = 240, H = 480, N = BackdropRender.FrameSize;
        var (art, mask) = Figure(W, H);
        var blink = new byte[W * H * 4];
        for (int y = 140; y < 160; y++) for (int x = 100; x < 140; x++) { int i = (y * W + x) * 4; blink[i] = 30; blink[i + 1] = 20; blink[i + 2] = 20; blink[i + 3] = 255; }
        var gi = NpcRender.Gi(scene.Backdrop);
        var light = new NpcLight(scene);
        float scale = NpcRender.ScreenPerArtPixel(0.79f), width = W * scale, height = H * scale;
        float left = scene.Left + MathF.Round((N - width) / 2f * 2f) / 2f, bottom = scene.FrameBottom + MathF.Round((N - height) / 2f * 2f) / 2f;
        foreach (var parts in Parts)
        {
            byte[] Draw()
            {
                using var card = GpuFrame.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[N * N * 4];
                card!.DrawNpc(scene, gi, true, light, art, blink, mask, W, H, Moving, (1f, 1f, 1f, 1f), 3.7f, left, bottom, width, height,
                              parts, JiggleShader.GameZoom, output);
                return output;
            }
            Same($"the NPC over {backdropName}, {parts}", With(0u, Draw), With(Fill, Draw));
        }
    }

    [Fact]
    public void AJiggleAlone_AndAPlacesArt()
    {
        var (art, mask) = Figure(240, 600);
        foreach (var sampling in new[] { new JiggleShader.Sampling(2, 2, 1), new JiggleShader.Sampling(1, 1, 2) })
        {
            byte[] Draw()
            {
                using var card = GpuAlone.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[205 * 512 * 4];
                card!.Draw(art, mask, 240, 600, Moving, (1f, 1f, 1f, 1f), 4.4f, output, 205, 512, sampling);
                return output;
            }
            Same($"a jiggle alone, {sampling}", With(0u, Draw), With(Fill, Draw));
        }

        const int M = 256;
        var level = new byte[600 * 400 * 4];
        for (int i = 0; i < level.Length; i += 4) { level[i] = (byte)(i % 251); level[i + 1] = (byte)(i / 7 % 253); level[i + 2] = 90; level[i + 3] = 255; }
        var levelMask = new byte[M * M * 4];
        for (int y = 0; y < M; y++) for (int x = 0; x < M; x++) levelMask[(y * M + x) * 4 + 3] = (byte)(x * 255 / M);
        byte[] Milk()
        {
            using var card = GpuMilking.TryCreate();
            Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
            var output = new byte[600 * 400 * 4];
            card!.Draw(level, 600, 400, levelMask, M, M, MilkingShader.Settings.Level, 3.7f, output, 600, 400);
            return output;
        }
        Same("a place's art", With(0u, Milk), With(Fill, Milk));
    }

    [Fact]
    public void ThePlacesLook_OverTheScreenAndOverAView()
    {
        var scene = Scene("MyRoom");
        const int W = 960, H = 540, VW = 640, VH = 360;
        var screen = new byte[W * H * 4];
        for (int i = 0; i < screen.Length; i += 4) { screen[i] = (byte)(i / 4 % W / 4); screen[i + 1] = (byte)(i / 4 / W / 3); screen[i + 2] = (byte)(i % 1000 < 20 ? 255 : 60); screen[i + 3] = 255; }
        var view = new byte[VW * VH * 4];
        for (int y = 0; y < VH; y++)
            for (int x = 0; x < VW; x++)
                Array.Copy(screen, (Math.Min(H - 1, y / 3) * W + Math.Min(W - 1, 700 + x / 3)) * 4, view, (y * VW + x) * 4, 4);
        foreach (var parts in Parts)
        {
            byte[] Over()
            {
                using var card = GpuPost.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[W * H * 4];
                card!.LookOver(screen, W, H, scene, parts, output);
                return output;
            }
            byte[] OverView()
            {
                using var card = GpuPost.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[VW * VH * 4];
                card!.LookOverView(screen, W, H, view, VW, VH, 1f / 3f / W, 700f / W, -1f / 3f / H, 1f, scene, parts, output);
                return output;
            }
            byte[] Present()
            {
                using var card = GpuPost.TryCreate();
                Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
                var output = new byte[BackdropRender.FrameSize * BackdropRender.FrameSize * 4];
                card!.Present(BackdropRender.Scene(scene), scene, parts, output);
                return output;
            }
            Same($"the look over the screen, {parts}", With(0u, Over), With(Fill, Over));
            Same($"the look over a view, {parts}", With(0u, OverView), With(Fill, OverView));
            Same($"the bust preview's post-processing, {parts}", With(0u, Present), With(Fill, Present));
        }
    }
}
