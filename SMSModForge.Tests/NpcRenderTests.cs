using System;
using System.IO;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The NPC preview over a backdrop (NpcRender, 1.9.1): a pack NPC as the game
/// draws it in a place - InGameSuiteTests.S9 holds it to the game itself; this
/// holds what that check found, without the game: the room's light falls on an
/// NPC as it does on the room (the PC's room's warm lamp; nothing in the Forest,
/// whose only light is the global white), it is drawn at its size in the place,
/// and its edge takes the fake GI from the room. Measured on what is drawn.
/// </summary>
public sealed class NpcRenderTests
{
    private readonly ITestOutputHelper _out;
    public NpcRenderTests(ITestOutputHelper o) => _out = o;

    private const int N = BackdropRender.FrameSize;

    private static BackdropScene Scene(string name)
    {
        var scene = new BackdropScene(PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", name)));
        scene.Prepare();
        return scene;
    }

    /// <summary>An art of <paramref name="w"/> x <paramref name="h"/>: opaque grey in
    /// a border of <paramref name="clear"/> clear texels, or the suite's figure.</summary>
    private static byte[] Art(int w, int h, int clear, bool figure = false)
    {
        var art = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (figure)
                {
                    var c = InGameSuitePacks.Looks.FigureAt(x, y);
                    art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                }
                else if (x >= clear && y >= clear && x < w - clear && y < h - clear)
                { art[i] = 128; art[i + 1] = 128; art[i + 2] = 128; art[i + 3] = 255; }
            }
        return art;
    }

    private static float[] Body(BackdropScene scene, byte[] art, int w, int h, float levelScale, FakeGi? gi, FakeGi.Art? giArt,
                                Func<float, float, (float, float, float)> light)
    {
        float scale = NpcRender.ScreenPerArtPixel(levelScale);
        float width = w * scale, height = h * scale;
        float left = scene.Left + (N - width) / 2f, bottom = scene.FrameBottom + (N - height) / 2f;
        var body = new float[N * N * 4];
        NpcRender.Body(art, new byte[w * h * 4], w, h, new JiggleParams(), (1f, 1f, 1f, 1f), 1f, gi, giArt, light,
                       left, bottom, width, height, scene.Left, scene.FrameBottom, body, true, scene.Decode, 1);
        return body;
    }

    [Fact]
    public void TheRoomsLampLightsAnNpcInThePcsRoomAndTheForestLeavesItAsItIs()
    {
        var room = Scene("MyRoom");
        var forest = Scene("Forest");
        var art = Art(200, 400, 0);
        var inRoom = Body(room, art, 200, 400, 0.79f, null, null, new NpcLight(room).At);
        var inForest = Body(forest, art, 200, 400, 0.79f, null, null, new NpcLight(forest).At);
        var alone = Body(room, art, 200, 400, 0.79f, null, null, (_, _) => (1f, 1f, 1f));
        // The NPC's middle, in linear light (the body is RGBA).
        int o = ((N / 2) * N + N / 2) * 4;
        _out.WriteLine($"the NPC's middle: in the PC's room R {inRoom[o]:0.0000} G {inRoom[o + 1]:0.0000} B {inRoom[o + 2]:0.0000}; in the Forest R {inForest[o]:0.0000} G {inForest[o + 1]:0.0000} B {inForest[o + 2]:0.0000}; "
                       + $"under the global light alone R {alone[o]:0.0000}");
        Assert.True(alone[o + 3] > 0.99f, "nothing drawn at the NPC's middle - the check proves nothing");
        // The lamp is warm: more red than blue on a grey NPC, and more than the global
        // light gives (the game measured red 1.117 there, S9).
        Assert.True(inRoom[o] > alone[o] * 1.03f, "the PC's room's lamp does not light the NPC");
        Assert.True(inRoom[o] - inRoom[o + 2] > 0.005f, "the lamp's light on the NPC is not warm");
        // The Forest has no light of its own: the NPC as under the global light alone.
        Assert.True(Math.Abs(inForest[o] - alone[o]) < 0.004f && Math.Abs(inForest[o + 2] - alone[o + 2]) < 0.004f,
                    "the Forest lights the NPC though it has no light of its own");
    }

    [Theory]
    [InlineData(256, 0.79f)]
    [InlineData(300, 1f)]
    public void AnNpcIsDrawnAtItsSizeInThePlace(int artWidth, float levelScale)
    {
        var room = Scene("Forest");
        var art = Art(artWidth, artWidth, 0);
        var body = Body(room, art, artWidth, artWidth, levelScale, null, null, (_, _) => (1f, 1f, 1f));
        int row = N / 2, drawn = 0;
        for (int x = 0; x < N; x++) if (body[(row * N + x) * 4 + 3] > 0.5f) drawn++;
        double expected = artWidth * levelScale / 2.0;   // 100 art pixels a unit, 50 screen pixels a unit, the level's scale
        _out.WriteLine($"{artWidth} art pixels in a level of scale {levelScale}: {drawn} screen pixels across (the game's {expected:0.0})");
        Assert.True(Math.Abs(drawn - expected) <= 1.0, $"drawn {drawn} pixels across, the game's {expected:0.0}");
    }

    [Fact]
    public void AnNpcsEdgeTakesTheFakeGiFromTheRoom()
    {
        var room = Scene("MyRoom");
        var art = Art(256, 256, 0, figure: true);
        var gi = NpcRender.Gi(room.Backdrop);
        var giArt = new FakeGi.Art(art, 256, 256, NpcRender.Reach(room.Backdrop.Gi, 256, 256));
        var light = new NpcLight(room).At;
        var lit = Body(room, art, 256, 256, 0.79f, gi, giArt, light);
        var bare = Body(room, art, 256, 256, 0.79f, null, null, light);
        int changed = 0;
        for (int i = 0; i < lit.Length; i += 4)
            if (Math.Abs(lit[i] - bare[i]) + Math.Abs(lit[i + 1] - bare[i + 1]) + Math.Abs(lit[i + 2] - bare[i + 2]) > 0.01f) changed++;
        _out.WriteLine($"the fake GI changes {changed} pixels of the NPC");
        Assert.True(changed > 300, $"the fake GI changes only {changed} pixels of the NPC's edge");
    }
}
