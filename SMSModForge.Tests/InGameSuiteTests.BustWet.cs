using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// A bust's wet drops (after 1.10.0: "busts can carry the same wet drops in dialogues, and the bust preview doesn't
/// show them yet" - the author: "yes"): what the game draws over a pack bust whose outfit has its Wet particle on, read
/// from the game itself, and the bust preview held to it.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The wet figure bust (InGameSuitePacks.Looks) over the PC's room, the interface off, the post-processing off, time
    /// frozen once the drops have fallen longer than one lives: the Wet object's settings, every live drop, the bust's
    /// corners on screen, and the frame with and without the drops.
    /// <para/>
    /// Held to: the drops as an NPC's (S17: ten out, born half a second apart, as see-through as their age, 0.04 x 0.5
    /// units, started in the 3.21 x 8.45 box round the Wet object - its own scale 1 inside a body scaled 4, which leaves
    /// them alone); the bust preview's Wet object, from the outfit over the room's backdrop with the bust where the game
    /// drew it, where the game's is (Rendering.WetDrops.OnBust); and the game's drops blended by the preview's own pass
    /// (WetDrops.BlendInto) over the game's frame without them: within six levels of the game's frame with them at 95%
    /// of the pixels they changed, and off by under a fifth of what they changed. Control: the drops two game pixels aside
    /// are off by more than half of it.
    /// </summary>
    [Fact]
    public void S20_A_busts_wet_drops_as_the_game_draws_them()
    {
        if (Off) return;
        var game = _s.InGame();
        const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
        string root = "2_Bust_Manager/" + InGameSuitePacks.Looks.WetFigureBust;
        string wet = root + "/MBase1/Wet";
        string settings, live, look, on, off;
        game.Ask("wait 8", 20);
        game.Ask("set-active 9_MainCanvas false");
        game.Ask("set-active " + Buttons + " false");
        game.Ask("postfx off");
        game.Ask("set-active " + root + " true");
        try
        {
            game.Ask("wait 6.5", 20);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            settings = game.Ask("particles " + wet, 20);
            live = game.Ask("particles-live " + root + "/MBase1", 20);
            look = game.Ask("bust-look " + root + "/MBase1 bust-wet", 30);
            on = LookFolder(game.Ask("screen-look bust-wet-on", 15));
            game.Ask("set-active " + wet + " false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look bust-wet-off", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("set-active " + root + " false");
            game.Ask("postfx on");
            game.Ask("set-active " + Buttons + " true");
            game.Ask("set-active 9_MainCanvas true");
        }
        static string FileIn(string said) => Regex.Match(said, @"[A-Za-z]:\\.*$").Value.Trim();
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "bust-wet");
        Directory.CreateDirectory(keep);
        foreach (var (from, name) in new[] { (FileIn(settings), "settings.txt"), (FileIn(live), "live.txt"),
                                             (Path.Combine(look.Substring("bust-wet".Length).Trim(), "info.txt"), "bust.txt"),
                                             (Path.Combine(on, "screen.png"), "wet-on.png"), (Path.Combine(off, "screen.png"), "wet-off.png") })
            File.Copy(from, Path.Combine(keep, name), true);
        _out.WriteLine("kept in " + keep);
        foreach (var line in File.ReadLines(Path.Combine(keep, "settings.txt"))) _out.WriteLine("   " + line);
        var systems = LiveDrops(File.ReadAllLines(Path.Combine(keep, "live.txt")));
        // Anna's towel's two systems (after 1.10.0, the author: "Copy both, like Anna"): the first, and the second under
        // it as "Wet (1)" - 2.04 units lower, as Anna's own is (0.51 under her body scaled 4).
        Assert.Equal(2, systems.Count);
        var sys = systems.Single(s => s.Path.EndsWith("/Wet", StringComparison.Ordinal));
        var second = systems.Single(s => s.Path.EndsWith("/Wet/Wet (1)", StringComparison.Ordinal));
        Assert.True(Math.Abs(second.X - sys.X) < 1e-3 && Math.Abs(second.Y - sys.Y) < 1e-3, "the second system is not where the first is");

        // ── The game's drops held to the model, as an NPC's ──────────────
        foreach (var (one, model) in new[] { (sys, WetDrops.TowelFirst), (second, WetDrops.TowelSecond) })
        {
            int expectedCount = (int)Math.Round(model.Rate * model.Life);
            Assert.Equal(expectedCount, one.Drops.Count);
            double step = 1 / model.Rate, phase = one.Drops[0].Age % step, mid = model.Shift * WetDrops.BoxH;
            foreach (var d in one.Drops)
            {
                double steps = (d.Age - phase) / step;
                Assert.True(Math.Abs(steps - Math.Round(steps)) < 0.01, $"{one.Path}: a drop aged {d.Age} is not born on a step of {step:0.###} s");
                double expected = WetDrops.AlphaOver(d.Age / WetDrops.Life) * 255;
                Assert.InRange(d.Alpha * 255, expected - 2, expected + 2);
                Assert.Equal(model.SizeXMin, d.SizeX, 4);
                Assert.Equal(model.SizeYMin, d.SizeY, 4);
                double lx = d.X - one.X, ly = d.Y + 0.5 * WetDrops.Gravity * d.Age * d.Age - one.Y;
                double slack = 0.5 * WetDrops.Gravity * d.Age / 30 + 0.01;
                Assert.True(Math.Abs(lx) <= WetDrops.BoxW / 2 + slack && Math.Abs(ly - mid) <= WetDrops.BoxH / 2 + slack,
                            $"{one.Path}: a drop aged {d.Age:0.###} started at ({lx:0.###}, {ly:0.###}) of its box round {mid:0.###}, outside it");
            }
            Assert.Equal(1, one.ScaleX, 3);
            Assert.Equal(1, one.ScaleY, 3);
        }

        // ── The bust preview's Wet object where the game's is ───────────
        var bust = File.ReadAllLines(Path.Combine(keep, "bust.txt"));
        var drawn = bust.First(l => l.StartsWith("drawnCorners=", StringComparison.Ordinal)).Substring("drawnCorners=".Length).Split(' ')[0]
                        .Split(';').Select(c => c.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        int bustBottom = (int)Math.Round(drawn[0][1]);
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        _out.WriteLine($"the bust drawn from ({drawn[0][0]:0.##}, {drawn[0][1]:0.##}) to ({drawn[3][0]:0.##}, {drawn[3][1]:0.##}); the backdrop's at left {scene.Left}, resting bottom {backdrop.BustRestBottom}");
        Assert.Equal(scene.Left, drawn[0][0], 1);
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", InGameSuitePacks.Looks.Id);
        WetDrops.Emitter? emitter = null;
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var outfit = pack.Characters.SelectMany(c => c.Outfits).Single(o => o.Key == "wetfigure");
            var preview = new JigglePreview { PackRoot = packFolder, Outfit = new OutfitViewModel(outfit) };
            Assert.NotNull(preview.WetDropsNow(scene, bustBottom, 0f));
            emitter = preview.LastEmitter;
        });
        Assert.NotNull(emitter);
        var e = emitter!.Value;
        double ex = (e.X - 960) / 100, ey = (540 - e.Y) / 100;
        _out.WriteLine($"the Wet object: the game's at ({sys.X:0.####}, {sys.Y:0.####}) scale {sys.ScaleX} x {sys.ScaleY}; the preview's at ({ex:0.####}, {ey:0.####}) scale {e.ScaleX} x {e.ScaleY}");
        Assert.True(Math.Abs(ex - sys.X) < 0.02 && Math.Abs(ey - sys.Y) < 0.02, $"the preview's Wet object is at ({ex:0.###}, {ey:0.###}), the game's at ({sys.X:0.###}, {sys.Y:0.###})");
        Assert.Equal(sys.ScaleX, e.ScaleX, 3);
        Assert.Equal(sys.ScaleY, e.ScaleY, 3);
        Assert.Equal(1, e.AxX, 6);
        Assert.Equal(-1, e.AyY, 6);

        // ── The game's drops blended by the preview's pass ──────────────
        var (gw, gh, gameOn) = Png(Path.Combine(keep, "wet-on.png"));
        var (_, _, gameOff) = Png(Path.Combine(keep, "wet-off.png"));
        static float Linear(byte v) { float s = v / 255f; return s <= 0.04045f ? s / 12.92f : MathF.Pow((s + 0.055f) / 1.055f, 2.4f); }
        static int Srgb(float l) { float s = l <= 0.0031308f ? 12.92f * l : 1.055f * MathF.Pow(Math.Clamp(l, 0f, 1f), 1f / 2.4f) - 0.055f; return Math.Clamp((int)MathF.Round(s * 255f), 0, 255); }
        var mips = WetDrops.Texture!;
        byte[] Blended(double aside)
        {
            var drops = systems.SelectMany(one => one.Drops)
                               .Select(d => new WetDrops.Drop { X = 960 + d.X * 100 + aside, Y = 540 - d.Y * 100, W = d.SizeX * 100, H = d.SizeY * 100, Alpha = (float)d.Alpha }).ToArray();
            var target = new GamePost.Target(gw, gh);
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    int i = (y * gw + x) * 4, o = ((gh - 1 - y) * gw + x) * 4;
                    target.Px[o] = Linear(gameOff[i + 2]); target.Px[o + 1] = Linear(gameOff[i + 1]); target.Px[o + 2] = Linear(gameOff[i]); target.Px[o + 3] = 1f;
                }
            WetDrops.BlendInto(target, new WetDrops.Over(drops, drops.Length, mips, 1920, 1080));
            var px = new byte[gw * gh * 4];
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    int i = (y * gw + x) * 4, o = ((gh - 1 - y) * gw + x) * 4;
                    px[i] = (byte)Srgb(target.Px[o + 2]); px[i + 1] = (byte)Srgb(target.Px[o + 1]); px[i + 2] = (byte)Srgb(target.Px[o]); px[i + 3] = 255;
                }
            return px;
        }
        var mine = Blended(0);
        var aside = Blended(4);   // two game pixels: a drop is two wide
        static int Diff(byte[] a, byte[] b, int i) => Math.Max(Math.Abs(a[i] - b[i]), Math.Max(Math.Abs(a[i + 1] - b[i + 1]), Math.Abs(a[i + 2] - b[i + 2])));
        // The drops are faint over the bright figure (a few levels at most pixels), so beside how many pixels come
        // within six, how far off each is against how much the drops changed it: the change itself is the scale.
        int dropPixels = 0, near = 0;
        long change = 0, offMine = 0, offAside = 0;
        for (int i = 0; i < gameOn.Length; i += 4)
        {
            int c = Diff(gameOn, gameOff, i);
            if (c <= 3) continue;
            dropPixels++;
            change += c;
            int m = Diff(gameOn, mine, i);
            offMine += m;
            offAside += Diff(gameOn, aside, i);
            if (m <= 6) near++;
        }
        double Share(int n) => dropPixels == 0 ? 0 : (double)n / dropPixels;
        double mineShare = change == 0 ? 1 : (double)offMine / change, asideShare = change == 0 ? 0 : (double)offAside / change;
        _out.WriteLine($"{dropPixels} pixels the drops changed (by {(double)change / Math.Max(1, dropPixels):0.0} levels on average): the preview's blend within six {Share(near):P2}, "
                       + $"off by {mineShare:P1} of the change; the drops two pixels aside off by {asideShare:P1} of it");
        Assert.True(dropPixels > 40, "the game's drops drew too little to tell");
        Assert.True(asideShare > 0.5, "control: the drops two pixels aside are nearly as close as the drops where they are");
        Assert.True(mineShare < 0.2, $"the preview's blend of the game's drops is off by {mineShare:P1} of what they change");
        Assert.True(Share(near) >= 0.95, $"the preview's blend of the game's drops is more than six levels off at {dropPixels - near} of {dropPixels}");
    }
}
