using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// How the game draws a pack NPC (1.9.1, the author: "the NPC preview should
/// get the same treatment as the Character one ... I assume they behave very
/// similarly. We might need to confirm, but again, Ultra should be the most
/// accurate look for them in game").
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// A pack NPC standing in the PC's room, measured as the game draws it -
    /// the look NPC (the figure, moved by noise, twice its size) switched on,
    /// the interface off, the game's time frozen: its renderer, material and
    /// the lights on its layer, the screen, the background its edge glow reads;
    /// and the camera's HDR frame with the post-processing off - as drawn, with
    /// the fake GI off, with the lights neutral (the global light alone), and
    /// the room with the NPC off, lit and neutral. Written to the looks folder
    /// (npc-*), read by Tools/Game/npc-look.py. The NPC is switched off and the
    /// game's look put back after.
    /// </summary>
    [Fact]
    public void S9_A_pack_NPC_is_measured_as_the_game_draws_it()
    {
        if (Off) return;
        var game = _s.InGame();
        string npc = InGameSuitePacks.Looks.NpcPath;
        string level = InGameSuitePacks.Looks.NpcLevel;
        var hidden = new List<string> { "9_MainCanvas" };
        var folders = new List<string>();
        try
        {
            Travel(game, "vanilla:" + level, level);
            game.Ask("wait 2", 10);
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains("5_Levels/" + level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("set-active " + npc + " true");
            game.Ask("wait 2", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            string said = game.Ask("bust-look " + npc + " npc-look", 30);
            folders.Add(said.Substring("npc-look".Length).Trim());
            game.Ask("postfx off");
            void Hdr(string name)
            {
                game.Ask("wait 0.3", 10);
                string folder = LookFolder(game.Ask("screen-look " + name, 15));
                game.Ask("rt-save _CameraTarget " + Path.Combine(Path.GetRelativePath(game.BridgeFolder, folder), "frame.bin"), 20);
                folders.Add(folder);
            }
            Hdr("npc-hdr");
            game.Ask("fake-gi off");
            Hdr("npc-nogi");
            game.Ask("lights neutral");
            Hdr("npc-neutral");
            game.Ask("set-active " + npc + " false");
            Hdr("npc-room-neutral");
            game.Ask("lights restore");
            Hdr("npc-room");
            game.Ask("fake-gi on");
            // A moment later: what moves by itself in the room.
            game.Ask("postfx on");
            game.Ask("freeze off");
            game.Ask("set-active " + npc + " true");
            game.Ask("wait 1.3", 10);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            folders.Add(LookFolder(game.Ask("screen-look npc-later", 15)));
        }
        finally
        {
            game.Ask("lights restore");
            game.Ask("fake-gi on");
            game.Ask("postfx on");
            game.Ask("freeze off");
            game.Ask("set-active " + npc + " false");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
        }
        foreach (var f in folders) _out.WriteLine(f);
        var info = File.ReadAllLines(Path.Combine(folders[0], "info.txt"));
        foreach (var l in info.Where(l => l.StartsWith("renderer=") || l.StartsWith("material=") || l.StartsWith("light=") || l.StartsWith("drawnCorners=")
                                          || l.StartsWith("background") || l.StartsWith("spriteRect=")))
            _out.WriteLine("   " + l);
        Assert.Contains(info, l => l.StartsWith("renderer=layer Default"));

        // The NPC preview's frame over the PC's room (NpcRender), for the same
        // NPC at the game's frozen time and drawn rect, at the test copy's x1,
        // against what the game drew - as S3 holds the bust preview. The room's
        // own moving parts are left out. Controls: lit by the global light
        // alone (as a bust is), and without its edge glow, the NPC is not the game's.
        var look = LookOf(folders[0]);
        var (_, _, laterPx) = Png(Path.Combine(folders[^1], "screen.png"));
        var corners = look.Corners("drawnCorners");
        float left = corners[0].x, bottom = corners[0].y, width = corners[1].x - corners[0].x, height = corners[2].y - corners[0].y;
        float time = look.F("time");
        var p = new SMSModForge.Model.JiggleParams
        {
            Speed = look.F("mat_JiggleSpeed"), Strength = look.F("mat_JiggleStrength"), Frequency = look.F("mat_JiggleFrequency"),
            NoiseScale = look.F("mat_NoiseScale"), NoiseSpeed = look.F("mat_NoiseSpeed"), NoiseStrength = look.F("mat_NoiseStrength"),
        };
        int size = InGameSuitePacks.Looks.Size;
        var art = new byte[size * size * 4];
        var mask = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = InGameSuitePacks.Looks.FigureAt(x, y);
                int i = (y * size + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
                mask[i] = 255; mask[i + 3] = 255;          // the noise channel (blue), as the pack's mask
            }
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        scene.Prepare();
        var gi = NpcRender.Gi(backdrop);
        var giArt = new FakeGi.Art(art, size, size, NpcRender.Reach(backdrop.Gi, size, size));
        var light = new NpcLight(scene);
        const int N = BackdropRender.FrameSize;
        byte[] Frame(bool lit, FakeGi? withGi)
        {
            var body = new float[N * N * 4];
            NpcRender.Body(art, mask, size, size, p, (1f, 1f, 1f, 1f), time, withGi, withGi != null ? giArt : null,
                           lit ? light.At : (_, _) => (1f, 1f, 1f), left, bottom, width, height, scene.Left, scene.FrameBottom, body, true, scene.Decode, 1);
            var output = new byte[N * N * 4];
            NpcRender.Compose(scene, BackdropRender.Scene(scene), new float[backdrop.BackgroundWidth * backdrop.BackgroundHeight * 4], new float[N * N],
                              BackdropRender.PostParts.All, output, body);
            return output;
        }
        // Its edge glow reads the room as a bust's does (NpcRender.Gi). Measured
        // 2026-11-16, the pixels of the NPC's rect within three levels: over the room
        // 42347, over the frame with the NPC in it (the game's copy of its target)
        // 41855, over the flat black bound once the frame is drawn 39018.
        var npcGi = gi;
        var preview = Frame(true, npcGi);
        var unlitNpc = Frame(false, npcGi);
        // The same frame on the graphics card (Options > Draw previews on), at the test copy's x1.
        var onCard = new byte[N * N * 4];
        using (var card = GpuFrame.TryCreate() ?? throw new InvalidOperationException("no graphics card device"))
            card.DrawNpc(scene, npcGi, true, light, art, null, mask, size, size, p, (1f, 1f, 1f, 1f), time, left, bottom, width, height,
                         BackdropRender.PostParts.All, 1, onCard);
        SaveFrame(onCard, Path.Combine(look.Folder, "preview-card.png"));
        var bare = Frame(true, null);
        SaveFrame(preview, Path.Combine(look.Folder, "preview.png"));
        int compared = 0, within3 = 0, cardWithin3 = 0, moving = 0, npcCompared = 0, npcWithin3 = 0, unlitWithin3 = 0, edgeCompared = 0, edgeWithin3 = 0, bareEdgeWithin3 = 0;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int sx = scene.Left + x, sy = scene.FrameBottom + (N - 1 - y);
                var g = look.At(sx, sy);
                int li = ((look.H - 1 - sy) * look.W + sx) * 4;
                bool onNpc = sx >= left - 2 && sx < left + width + 2 && sy >= bottom - 2 && sy < bottom + height + 2;
                if (!onNpc && (Math.Abs(laterPx[li] - g.b) > 1 || Math.Abs(laterPx[li + 1] - g.g) > 1 || Math.Abs(laterPx[li + 2] - g.r) > 1)) { moving++; continue; }
                int o = (y * N + x) * 4;
                int D(byte[] f) => Math.Max(Math.Abs(f[o] - g.b), Math.Max(Math.Abs(f[o + 1] - g.g), Math.Abs(f[o + 2] - g.r)));
                int d = D(preview);
                compared++;
                if (d <= 3) within3++;
                if (D(onCard) <= 3) cardWithin3++;
                if (Math.Max(Math.Abs(preview[o] - unlitNpc[o]), Math.Max(Math.Abs(preview[o + 1] - unlitNpc[o + 1]), Math.Abs(preview[o + 2] - unlitNpc[o + 2]))) >= 4)
                {
                    npcCompared++;
                    if (d <= 3) npcWithin3++;
                    if (D(unlitNpc) <= 3) unlitWithin3++;
                }
                if (Math.Max(Math.Abs(preview[o] - bare[o]), Math.Max(Math.Abs(preview[o + 1] - bare[o + 1]), Math.Abs(preview[o + 2] - bare[o + 2]))) >= 4)
                {
                    edgeCompared++;
                    if (d <= 3) edgeWithin3++;
                    if (D(bare) <= 3) bareEdgeWithin3++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"the NPC over the PC's room: {compared} pixels ({moving} moving left out) within three {Share(within3, compared):P2}; "
                       + $"where the room's light shows on it ({npcCompared}) {Share(npcWithin3, npcCompared):P2}, lit as a bust {Share(unlitWithin3, npcCompared):P2}; "
                       + $"its edge glow ({edgeCompared}) {Share(edgeWithin3, edgeCompared):P2}, without it {Share(bareEdgeWithin3, edgeCompared):P2}");
        _out.WriteLine($"   on the graphics card: within three {Share(cardWithin3, compared):P2}");
        Assert.True(Share(within3, compared) >= 0.99, $"the NPC preview is more than three levels from the game at {compared - within3} of {compared}");
        Assert.True(Share(cardWithin3, compared) >= 0.99, $"the NPC preview on the card is more than three levels from the game at {compared - cardWithin3} of {compared}");
        Assert.True(npcCompared >= 300 && Share(npcWithin3, npcCompared) >= 0.9, $"where the room's light shows on the NPC: {npcCompared - npcWithin3} of {npcCompared} more than three levels off");
        Assert.True(Share(unlitWithin3, npcCompared) < Share(npcWithin3, npcCompared) - 0.3, "control: the NPC lit as a bust is nearly as close - the check does not see the room's light");
        if (edgeCompared >= 300)
        {
            // 85%: measured 87-88% (the rest a few levels darker, median 9), against 3% without it.
            Assert.True(Share(edgeWithin3, edgeCompared) >= 0.85, $"the NPC's edge glow is more than three levels off at {edgeCompared - edgeWithin3} of {edgeCompared}");
            Assert.True(Share(bareEdgeWithin3, edgeCompared) < Share(edgeWithin3, edgeCompared) - 0.3, "control: without its edge glow the NPC is nearly as close");
        }
    }
}
