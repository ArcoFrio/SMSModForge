using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's look over a place on the graphics card (GpuPost.LookOver, 1.9.1)
/// is the processor's (GamePost.LookOver): the place as the preview drew it -
/// the PC's room's screen here, with bright spots to bloom and streak - through
/// the bloom, the light streaks and the colour, every tick box on and off as
/// the place preview has them (the bloom with the colour off as well). Compared
/// on the screen's bytes, within Direct3D's rounding. Control: the card with the
/// bloom off is visibly not the processor's with it.
/// </summary>
public sealed class GpuLookTests
{
    private readonly ITestOutputHelper _out;
    public GpuLookTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void TheCardsPlaceLookIsTheProcessors()
    {
        using var card = GpuPost.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom")));
        scene.Prepare();
        const int W = 960, H = 540;
        var frame = BitmapFrame.Create(new Uri(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom", "room.png")),
                                       BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var input = new byte[W * H * 4];
        bgra.CopyPixels(input, W * 4, 0);
        // Bright spots: what the bloom and the streaks take.
        for (int k = 0; k < 6; k++)
            for (int y = 0; y < 12; y++)
                for (int x = 0; x < 12; x++)
                {
                    int i = ((60 + k * 70 + y) * W + 100 + k * 130 + x) * 4;
                    input[i] = input[i + 1] = input[i + 2] = 255; input[i + 3] = 255;
                }
        var work = new GamePost.Work();
        foreach (var parts in new[] { BackdropRender.PostParts.All, new BackdropRender.PostParts(true, false, true), new BackdropRender.PostParts(false, false, true),
                                      new BackdropRender.PostParts(true, true, false), new BackdropRender.PostParts(false, false, false) })
        {
            var cpu = new byte[W * H * 4];
            GamePost.LookOver(input, new GamePost.Target(W, H), scene.Backdrop.Post, scene.Backdrop.GradingTable, scene.Decode, parts, work, cpu);
            var onCard = new byte[W * H * 4];
            card!.LookOver(input, W, H, scene, parts, onCard);
            int same = 0, overOne = 0;
            for (int i = 0; i < cpu.Length; i++)
            {
                int d = Math.Abs(cpu[i] - onCard[i]);
                if (d == 0) same++;
                if (d > 1) overOne++;
            }
            _out.WriteLine($"bloom {parts.Bloom}, streaks {parts.Flares}, colour {parts.Colour}: {same} of {cpu.Length} bytes the processor's, {overOne} more than a level off");
            Assert.True(overOne <= cpu.Length / 1000, $"{parts}: the card's look is more than a level from the processor's at {overOne} bytes");
            Assert.True(same > cpu.Length * 0.97, $"{parts}: only {same} of {cpu.Length} bytes are the processor's");
            if (parts.Bloom)
            {
                var noBloom = new byte[W * H * 4];
                card.LookOver(input, W, H, scene, parts with { Bloom = false, Flares = false }, noBloom);
                int off = Enumerable.Range(0, cpu.Length).Count(i => Math.Abs(cpu[i] - noBloom[i]) > 1);
                Assert.True(off > 2000, $"control: {parts} without the bloom passes for it ({off} bytes apart)");
            }
        }
    }

    /// <summary>
    /// The look over a view (the place preview zoomed, 1.9.1: GamePost.LookOverView)
    /// is the look over the screen where the view is the screen - the same compose
    /// at each pixel - and, zoomed into a corner and past the screen's edge, the
    /// card's view is the processor's. Control: the view a pixel off its place is
    /// not the screen's look.
    /// </summary>
    [Fact]
    public void TheLookOverAViewIsTheScreensWhereItIsTheScreen_AndTheCardsIsTheProcessors()
    {
        using var card = GpuPost.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom")));
        scene.Prepare();
        const int W = 960, H = 540;
        var frame = BitmapFrame.Create(new Uri(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom", "room.png")),
                                       BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var screen = new byte[W * H * 4];
        bgra.CopyPixels(screen, W * 4, 0);
        var s = scene.Backdrop.Post;
        var parts = BackdropRender.PostParts.All;
        var whole = new byte[W * H * 4];
        GamePost.LookOver(screen, new GamePost.Target(W, H), s, scene.Backdrop.GradingTable, scene.Decode, parts, new GamePost.Work(), whole);
        // The view the screen itself: uv at each pixel's centre.
        var asView = new byte[W * H * 4];
        GamePost.LookOverView(screen, new GamePost.Target(W, H), screen, W, H, 1f / W, 0f, -1f / H, 1f, s, scene.Backdrop.GradingTable, scene.Decode,
                              parts, new GamePost.Work(), asView);
        int same = Enumerable.Range(0, whole.Length).Count(i => whole[i] == asView[i]);
        var shifted = new byte[W * H * 4];
        GamePost.LookOverView(screen, new GamePost.Target(W, H), screen, W, H, 1f / W, 40f / W, -1f / H, 1f - 30f / H, s, scene.Backdrop.GradingTable, scene.Decode,
                              parts, new GamePost.Work(), shifted);
        int shiftedOff = Enumerable.Range(0, whole.Length).Count(i => Math.Abs(whole[i] - shifted[i]) > 2);
        _out.WriteLine($"the view as the screen: {same} of {whole.Length} bytes the screen's look; shifted: {shiftedOff} over two off");
        Assert.True(same > whole.Length * 0.999, $"the view as the screen is not the screen's look: {same} of {whole.Length} bytes");
        Assert.True(shiftedOff > 10000, "control: the view shifted passes for the screen's look");

        // Zoomed 3x into the top-right corner, a quarter of the view past the screen's edge.
        const int VW = 640, VH = 360;
        var view = new byte[VW * VH * 4];
        for (int y = 0; y < VH; y++)
            for (int x = 0; x < VW; x++)
            {
                int sx = Math.Min(W - 1, 700 + x / 3), sy = Math.Min(H - 1, y / 3);
                Array.Copy(screen, (sy * W + sx) * 4, view, (y * VW + x) * 4, 4);
            }
        float ax = 1f / 3f / W, bx = 700f / W, ay = -1f / 3f / H, by = 1f;
        var cpu = new byte[VW * VH * 4];
        GamePost.LookOverView(screen, new GamePost.Target(W, H), view, VW, VH, ax, bx, ay, by, s, scene.Backdrop.GradingTable, scene.Decode, parts, new GamePost.Work(), cpu);
        var onCard = new byte[VW * VH * 4];
        card!.LookOverView(screen, W, H, view, VW, VH, ax, bx, ay, by, scene, parts, onCard);
        int cardSame = 0, overOne = 0;
        for (int i = 0; i < cpu.Length; i++)
        {
            int d = Math.Abs(cpu[i] - onCard[i]);
            if (d == 0) cardSame++;
            if (d > 1) overOne++;
        }
        _out.WriteLine($"zoomed into the corner: the card {cardSame} of {cpu.Length} bytes the processor's, {overOne} more than a level off");
        Assert.True(overOne <= cpu.Length / 1000, $"the card's view look is more than a level from the processor's at {overOne} bytes");
        Assert.True(cardSame > cpu.Length * 0.97, $"only {cardSame} of {cpu.Length} bytes of the card's view look are the processor's");
    }
}
