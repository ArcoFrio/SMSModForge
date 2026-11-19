using System;
using System.IO;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Where a backdrop's lamp draws its glow (1.9.2, <see cref="LampSettings.GlowWithTheRoom"/>) is where the game
/// draws it. URP draws a light's glow with the layer batch that ends at the highest layer the light lights, and
/// copies the edge glow's background right after that batch's glow: the Beach's lamp lights only the room's layer,
/// so its glow is in the room before the busts and in that copy; the PC's room's also lights Photos, so its glow
/// comes after the busts, over them, and is not in the copy. With no bust's shadow, the background the preview copies
/// from its room is held here to the game's own copy (background.png, Tools/Game/backdrop-from-game.py). Control:
/// each lamp drawn the other way misses the game's copy. And a bust is under the PC's room's glow and not the
/// Beach's, while a pack NPC, on the room's layer itself, is under both.
/// </summary>
public sealed class GameLampGlowTests
{
    private readonly ITestOutputHelper _out;
    public GameLampGlowTests(ITestOutputHelper o) => _out = o;

    private static PreviewBackdrop Load(string name) =>
        PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", name));

    /// <summary>The share of the background's pixels the game's copy, every channel within an 11-bit float's
    /// step of it (a 64th of the value) or 0.004.</summary>
    private static double ShareLikeTheGames(PreviewBackdrop backdrop, GameLamp lamp)
    {
        int bw = backdrop.BackgroundWidth, bh = backdrop.BackgroundHeight;
        var computed = new float[bw * bh * 4];
        lamp.Room(null, new GamePost.Target(backdrop.ScreenWidth, backdrop.ScreenHeight), computed, bw, bh);
        var game = backdrop.Background;
        int like = 0;
        for (int i = 0; i < bw * bh; i++)
        {
            bool same = true;
            for (int c = 0; c < 3; c++)
            {
                float a = computed[i * 4 + c], b = game[i * 4 + c];
                if (Math.Abs(a - b) > Math.Max(0.004f, Math.Max(a, b) / 64f)) same = false;
            }
            if (same) like++;
        }
        return (double)like / (bw * bh);
    }

    [Theory]
    [InlineData("Beach", true)]
    [InlineData("MyRoom", false)]
    public void TheEdgeGlowsBackgroundIsTheGamesCopy(string name, bool glowWithTheRoom)
    {
        var backdrop = Load(name);
        Assert.Equal(glowWithTheRoom, backdrop.Lamp!.GlowWithTheRoom);
        double asShipped = ShareLikeTheGames(backdrop, new GameLamp(backdrop));
        double otherWay = ShareLikeTheGames(backdrop, new GameLamp(backdrop, !glowWithTheRoom));
        _out.WriteLine($"{name}: the background is the game's copy at {asShipped:P2} of its pixels; with the glow drawn the other way {otherWay:P2}");
        // Measured 2026-11-19: the Beach 98.6% (1.6% the other way), the PC's room 95.8% (15.6%); the rest,
        // scattered, are pixels the separate captures disagree on.
        Assert.True(asShipped >= 0.95, $"{name}: the preview's edge glow background is the game's copy at only {asShipped:P2} of its pixels");
        Assert.True(otherWay <= 0.3, $"control: {name}'s lamp drawn the other way still passes for the game's copy ({otherWay:P2})");
    }

    [Theory]
    [InlineData("Beach")]
    [InlineData("MyRoom")]
    public void ABustIsUnderTheGlowOnlyWhenItComesAfterThem_APackNpcAlways(string name)
    {
        var backdrop = Load(name);
        var lamp = new GameLamp(backdrop);
        int w = backdrop.ScreenWidth, h = backdrop.ScreenHeight;
        int bw = backdrop.BackgroundWidth, bh = backdrop.BackgroundHeight;
        bool Covered(int x, int y) => x >= 400 && x < 560 && y >= 200 && y < 360;
        GamePost.Target Drawn()
        {
            var t = new GamePost.Target(w, h);
            lamp.Room(null, t, new float[bw * bh * 4], bw, bh);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Covered(x, y)) { int o = (y * w + x) * 4; t.Px[o] = t.Px[o + 1] = t.Px[o + 2] = 0.5f; }
            return t;
        }
        var bust = Drawn();
        lamp.Glow(bust, null, Covered);
        var npc = Drawn();
        lamp.Glow(npc, null, Covered, onTheRoomsLayer: true);
        int at = (280 * w + 480) * 4;
        float bustGlow = MathF.Max(bust.Px[at + 1], bust.Px[at + 2]) - 0.5f, npcGlow = MathF.Max(npc.Px[at + 1], npc.Px[at + 2]) - 0.5f;
        _out.WriteLine($"{name}: the glow over a bust {bustGlow:0.0000}, over a pack NPC {npcGlow:0.0000}");
        Assert.True(npcGlow > 0.005f, $"{name}: a pack NPC is not under the lamp's glow");
        if (lamp.GlowWithTheRoom) Assert.True(bustGlow == 0f, $"{name}: the glow is drawn with the room, yet it is over the bust ({bustGlow})");
        else Assert.True(bustGlow > 0.005f, $"{name}: the glow comes after the busts, yet it is not over the bust");
        // Around them, the room as the game captured it, both ways.
        int room = (100 * w + 100) * 3, px = (100 * w + 100) * 4;
        for (int c = 0; c < 3; c++)
        {
            Assert.Equal(backdrop.RoomHdr[room + c], bust.Px[px + c]);
            Assert.Equal(backdrop.RoomHdr[room + c], npc.Px[px + c]);
        }
    }
}
