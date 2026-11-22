using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Each drawing on the graphics card is the same whatever drew before it
/// (1.9.1). The previews share one card, and its settings and pictures stay
/// bound after a draw: once the Places preview had drawn its look zoomed in,
/// the next bloom - the bust's, an NPC's, the place's own - read that look's
/// settings and came out wrong (650,000 of a million bytes), the editor's
/// previews and the suite's checks alike, one time in a few. Here every
/// drawing comes after every other, and is the picture it is after itself.
/// Control: run before the fix, the zoomed look spoiled the drawings after it.
/// </summary>
public sealed class GpuDrawOrderTests
{
    private readonly ITestOutputHelper _out;
    public GpuDrawOrderTests(ITestOutputHelper o) => _out = o;

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

    [Fact]
    public void EachDrawingIsTheSameWhateverDrewBeforeIt()
    {
        Assert.True(GpuDevice.Shared != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var p = new JiggleParams { Speed = 3f, Strength = -0.02f, Frequency = 4f, NoiseScale = 5f, NoiseSpeed = 0.5f, NoiseStrength = 0.06f };
        var tint = (1f, 1f, 1f, 1f);
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize, Z = JiggleShader.GameZoom;
        var room = Scene("MyRoom");
        var forest = Scene("Forest");
        var (bust, bustMask) = Figure(S, S);
        var lamp = room.Lamp!;
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        lamp.Shadow.Draw(new BustShadow.Caster(bust, S, S, 100f), room.Left + N / 2f, room.Backdrop.BustRestBottom + 0.6f + N / 2f, 2f, 1f, shadow);
        var (npc, npcMask) = Figure(240, 480);
        var npcGi = NpcRender.Gi(room.Backdrop);
        var npcLight = new NpcLight(room);
        float ns = NpcRender.ScreenPerArtPixel(0.79f), nw = 240 * ns, nh = 480 * ns;
        float nl = room.Left + MathF.Round((N - nw) / 2f * 2f) / 2f, nb = room.FrameBottom + MathF.Round((N - nh) / 2f * 2f) / 2f;
        const int W = 960, H = 540, VW = 640, VH = 360;
        var screen = new byte[W * H * 4];
        for (int i = 0; i < screen.Length; i += 4) { screen[i] = (byte)(i / 4 % W / 4); screen[i + 1] = (byte)(i / 4 / W / 3); screen[i + 2] = (byte)(i % 1000 < 20 ? 255 : 60); screen[i + 3] = 255; }
        var view = new byte[VW * VH * 4];
        for (int y = 0; y < VH; y++)
            for (int x = 0; x < VW; x++)
                Array.Copy(screen, (Math.Min(H - 1, y / 3) * W + Math.Min(W - 1, 700 + x / 3)) * 4, view, (y * VW + x) * 4, 4);
        var level = new byte[600 * 400 * 4];
        for (int i = 0; i < level.Length; i += 4) { level[i] = (byte)(i % 251); level[i + 1] = (byte)(i / 7 % 253); level[i + 2] = 90; level[i + 3] = 255; }
        var levelMask = new byte[256 * 256 * 4];
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) levelMask[(y * 256 + x) * 4 + 3] = (byte)(x * 255 / 256);

        // One of each, as each preview keeps its own.
        using var bustCard = GpuFrame.TryCreate();
        using var forestCard = GpuFrame.TryCreate();
        using var npcCard = GpuFrame.TryCreate();
        using var alone = GpuAlone.TryCreate();
        using var milking = GpuMilking.TryCreate();
        using var post = GpuPost.TryCreate();
        using var viewPost = GpuPost.TryCreate();
        // A place's scene drawn by the card itself (1.10.0): the one drawing here that draws triangles.
        using var placeScene = GpuPlaceScene.TryCreate();
        using var placeMilking = GpuMilking.TryCreate();
        using var placePost = GpuPost.TryCreate();
        var drawings = new List<(string Name, Func<byte[]> Draw)>
        {
            ("a place's scene, lit, with the look", () =>
            {
                var frame = PlaceFrameSamples.Make(room, placeMilking!, BackdropRender.PostParts.All);
                var o = new byte[frame.Vw * frame.Vh * 4];
                placeScene!.Draw(frame, placePost!, o);
                return o;
            }),
            ("the bust over the PC's room", () => { var o = new byte[N * N * 4]; bustCard!.Draw(lamp, shadow, room, bust, bustMask, p, tint, 6.21f, true, room.Backdrop.BustRestBottom + 1, true, 0f, null, null, null, BackdropRender.PostParts.All, Z, o); return o; }),
            ("the bust over the Forest", () => { var o = new byte[N * N * 4]; forestCard!.DrawNoLamp(forest, bust, bustMask, p, tint, 6.21f, true, forest.Backdrop.BustRestBottom + 1, true, 0f, null, null, null, BackdropRender.PostParts.All, Z, o); return o; }),
            ("an NPC over the PC's room", () => { var o = new byte[N * N * 4]; npcCard!.DrawNpc(room, npcGi, true, npcLight, npc, null, npcMask, 240, 480, p, tint, 3.7f, nl, nb, nw, nh, BackdropRender.PostParts.All, Z, o); return o; }),
            ("a jiggle alone", () => { var o = new byte[205 * 512 * 4]; alone!.Draw(npc, npcMask, 240, 480, p, tint, 4.4f, o, 205, 512, JiggleShader.Sampling.Game); return o; }),
            ("a place's art", () => { var o = new byte[600 * 400 * 4]; milking!.Draw(level, 600, 400, levelMask, 256, 256, MilkingShader.Settings.Level, 3.7f, o, 600, 400); return o; }),
            ("the look over a place", () => { var o = new byte[W * H * 4]; post!.LookOver(screen, W, H, room, BackdropRender.PostParts.All, o); return o; }),
            ("the look over a place zoomed in", () => { var o = new byte[VW * VH * 4]; viewPost!.LookOverView(screen, W, H, view, VW, VH, 1f / 3f / W, 700f / W, -1f / 3f / H, 1f, room, BackdropRender.PostParts.All, o); return o; }),
        };

        var failures = new List<string>();
        foreach (var (name, draw) in drawings)
        {
            draw();
            var itself = draw();
            foreach (var (before, drawBefore) in drawings)
            {
                drawBefore();
                var after = draw();
                int off = Enumerable.Range(0, itself.Length).Count(i => Math.Abs(itself[i] - after[i]) > 1);
                if (off > 0) failures.Add($"{name} after {before}: {off} of {itself.Length} bytes off");
            }
        }
        foreach (var f in failures) _out.WriteLine(f);
        _out.WriteLine($"{drawings.Count} drawings, each after each: {failures.Count} came out otherwise");
        Assert.True(failures.Count == 0, "a drawing on the card depends on what drew before it: " + string.Join("; ", failures));
    }
}
