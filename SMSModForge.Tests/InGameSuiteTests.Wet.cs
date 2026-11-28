using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// An NPC's wet drops (after 1.10.0, the author: "Could we implement a proper preview of the wet effects? Currently
/// we have only an approximation that doesn't at all look like the game's"): what the game draws, read from the
/// game itself, and the preview held to it.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The wet place (InGameSuitePacks.Looks): three wet characters over a dark floor - one under a body twice the
    /// size, one turned 45 degrees, one scaled 2 x 0.5. In the game, the interface and the busts off, the
    /// post-processing off, time frozen once the drops have been falling longer than one lives: every setting of the
    /// drops, every live drop (how old, where, how big, what colour), the frame, and the frame with the drops off.
    /// <para/>
    /// Held to <see cref="WetDrops"/>: ten drops a system, born half a second apart, each as see-through as its age
    /// says, 0.04 x 0.5 units times its object's own scale, and each started inside the box round its Wet object
    /// (turned with it, scaled by its own scale only) and fallen straight down from there at 9.81 - a fall any other
    /// way puts the older ones a hundred units out of the box (the game's frame-by-frame steps allowed for: they add
    /// half of g times the age times a frame).
    /// <para/>
    /// And drawn as the game draws them: the place as the Places tab previews it, lit as the look lights it (without
    /// the post), the game's own drops laid over it, at the game's 960x540 test screen - where the game drew a drop, the
    /// preview is within six levels of its pixel at 95% of them. Controls: the game's frame without the drops is not;
    /// the same drops a game pixel to the side are not.
    /// </summary>
    [Fact]
    public void S17_An_NPCs_wet_drops_as_the_game_draws_them()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.WetPlaceKey;
        var hidden = new List<string> { "9_MainCanvas" };
        var wets = new List<string>();
        string on, off, settings, live, level;
        try
        {
            Travel(game, token, "_" + InGameSuitePacks.Looks.WetPlaceObject);
            level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains(level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("postfx off");
            wets.AddRange(new[] { InGameSuitePacks.Looks.WetPlain, InGameSuitePacks.Looks.WetTurned, InGameSuitePacks.Looks.WetScaled,
                                  InGameSuitePacks.Looks.WetGroup + "/" + InGameSuitePacks.Looks.WetNested }
                          .Select(n => level + "/NPCs/" + n + "/Wet"));
            // Longer than a drop lives (5 s): as many out as there will ever be.
            game.Ask("wait 6", 15);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            settings = game.Ask("particles " + wets[0]);
            live = game.Ask("particles-live " + level + "/NPCs", 20);
            on = LookFolder(game.Ask("screen-look wet-on", 15));
            foreach (var w in wets) game.Ask("set-active " + w + " false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look wet-off", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("postfx on");
            foreach (var w in wets) game.Ask("set-active " + w + " true");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        // The answers end with the file each was written to.
        static string FileIn(string said) => Regex.Match(said, @"[A-Za-z]:\\.*$").Value.Trim();
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "wet");
        Directory.CreateDirectory(keep);
        foreach (var (from, name) in new[] { (FileIn(settings), "settings.txt"), (FileIn(live), "live.txt"),
                                             (Path.Combine(on, "screen.png"), "wet-on.png"), (Path.Combine(off, "screen.png"), "wet-off.png") })
            File.Copy(from, Path.Combine(keep, name), true);
        _out.WriteLine("kept in " + keep);
        foreach (var line in File.ReadLines(Path.Combine(keep, "settings.txt"))) _out.WriteLine("   " + line);
        var systems = LiveDrops(File.ReadAllLines(Path.Combine(keep, "live.txt")));
        // Two a Wet object (after 1.10.0, the author: "Copy both, like Anna"): Anna_Towel's first and, under it as
        // "Wet (1)", its second - where the first is, turned and scaled as it is.
        Assert.Equal(8, systems.Count);
        foreach (var second in systems.Where(s => s.Path.EndsWith("/Wet/Wet (1)", StringComparison.Ordinal)))
        {
            var first = systems.Single(s => s.Path == second.Path[..^"/Wet (1)".Length]);
            Assert.True(Math.Abs(first.X - second.X) < 1e-3 && Math.Abs(first.Y - second.Y) < 1e-3, $"{second.Path}: not where the first system is");
            Assert.Equal(first.Turn, second.Turn, 2);
            Assert.Equal(first.ScaleX, second.ScaleX, 3);
            Assert.Equal(first.ScaleY, second.ScaleY, 3);
        }
        Assert.Equal(4, systems.Count(s => s.Path.EndsWith("/Wet", StringComparison.Ordinal)));

        // ── The game's drops held to the model ──────────────────────────
        int outsideTheGames = 0;
        foreach (var sys in systems)
        {
            bool second = sys.Path.EndsWith("/Wet (1)", StringComparison.Ordinal);
            var model = second ? WetDrops.TowelSecond : WetDrops.TowelFirst;
            // The box: the game's own, or the placement's area (the turned one's) - the particle shape's scale.
            bool own = sys.Path.Contains("/" + InGameSuitePacks.Looks.WetTurned + "/Wet", StringComparison.Ordinal);
            Assert.Equal(own ? InGameSuitePacks.Looks.WetTurnedAreaW : WetDrops.BoxW, sys.AreaW, 3);
            Assert.Equal(own ? InGameSuitePacks.Looks.WetTurnedAreaH : WetDrops.BoxH, sys.AreaH, 3);
            int expectedCount = (int)Math.Round(model.Rate * model.Life);
            Assert.True(sys.Drops.Count == expectedCount, $"{sys.Path}: {sys.Drops.Count} drops out, not {expectedCount}");
            double step = 1 / model.Rate;
            double phase = sys.Drops[0].Age % step;
            // The second's box is lower than the first's by a share of its height (Anna's: 2.04 of 8.45).
            double mid = model.Shift * sys.AreaH;
            foreach (var d in sys.Drops)
            {
                double steps = (d.Age - phase) / step;
                Assert.True(Math.Abs(steps - Math.Round(steps)) < 0.01, $"{sys.Path}: a drop aged {d.Age} is not born on a step of {step:0.###} s");
                double expected = WetDrops.AlphaOver(d.Age / WetDrops.Life) * 255;
                Assert.InRange(d.Alpha * 255, expected - 2, expected + 2);
                Assert.Equal(model.SizeXMin, d.SizeX, 4);
                Assert.Equal(model.SizeYMin, d.SizeY, 4);
                // Where it started: back up its fall, into the Wet object's own frame, over its own scale.
                double sx = d.X - sys.X, sy = d.Y + 0.5 * WetDrops.Gravity * d.Age * d.Age - sys.Y;
                double c = Math.Cos(-sys.Turn * Math.PI / 180), si = Math.Sin(-sys.Turn * Math.PI / 180);
                double lx = (c * sx - si * sy) / sys.ScaleX, ly = (si * sx + c * sy) / sys.ScaleY;
                // The game moves a drop a frame at a time, which falls it a little further than the even fall by
                // about half of g times its age times the frame's length: a frame of up to a thirtieth of a second.
                double slack = (0.5 * WetDrops.Gravity * d.Age / 30 + 0.01) / Math.Min(Math.Abs(sys.ScaleX), Math.Abs(sys.ScaleY));
                Assert.True(Math.Abs(lx) <= sys.AreaW / 2 + slack && Math.Abs(ly - mid) <= sys.AreaH / 2 + slack,
                            $"{sys.Path}: a drop aged {d.Age:0.###} started at ({lx:0.###}, {ly:0.###}) of its {sys.AreaW:0.##} x {sys.AreaH:0.##} box round {mid:0.###}, outside it");
                if (own && Math.Abs(lx) > WetDrops.BoxW / 2 + slack) outsideTheGames++;
            }
        }
        // Control: the area took - drops of the turned one start where the game's own box would not have them (each
        // of ten is out of it at 60%: all inside by chance one time in ten thousand). The drops' size was held above:
        // 0.04 x 0.5 for every one, the area or not.
        Assert.True(outsideTheGames > 0, "control: every drop of the one with an area of its own started inside the game's box");

        // ── The game's drops, drawn by the preview ──────────────────────
        var all = new List<WetDrops.Drop>();
        foreach (var sys in systems)
            foreach (var d in sys.Drops)
                all.Add(new WetDrops.Drop
                {
                    X = 960 + d.X * 100, Y = 540 - d.Y * 100,
                    W = d.SizeX * Math.Abs(sys.ScaleX) * 100, H = d.SizeY * Math.Abs(sys.ScaleY) * 100, Alpha = (float)d.Alpha,
                });
        var (gw, gh, gameOn) = Png(Path.Combine(keep, "wet-on.png"));
        var (_, _, gameOff) = Png(Path.Combine(keep, "wet-off.png"));
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", InGameSuitePacks.Looks.Id);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        byte[]? still = null, aside = null;
        int sprays = 0;
        IReadOnlyList<(string Name, WetDrops.Emitter Emitter)> emitters = Array.Empty<(string, WetDrops.Emitter)>();
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var vm = new PlaceViewModel(pack.Places.Single(p => p.Key == InGameSuitePacks.Looks.WetPlaceKey));
            var preview = new PlacePreview
            {
                PackRoot = packFolder, BaseSprite = vm.BaseSprite, SecondarySprite = vm.SecondarySprite,
                LevelArtOrder = vm.BaseSortingOrder, LevelArtSecondaryOrder = vm.SecondarySortingOrder,
                GameObjects = vm.GameObjects, NpcCatalog = pack.Npcs.Select(n => new NpcViewModel(n)).ToList(),
                Shaders = false, ParallaxPreview = false, AsTheGameShows = true,
                DimInactive = false, DimUnderInactiveParent = false,
                Width = 1920, Height = 1080,
            };
            preview.Measure(new Size(1920, 1080));
            preview.Arrange(new Rect(0, 0, 1920, 1080));
            preview.UpdateLayout();
            WindowHarness.Pump();
            sprays = preview.SprayCount;
            emitters = preview.Emitters;
            var mips = WetDrops.Texture!;
            // At the test screen's own size: the game draws a drop at its pixels (one 2 pixels wide there), and so
            // does the preview drawn at that size.
            preview.DropsOverride = new WetDrops.Over(all.ToArray(), all.Count, mips, 1920, 1080);
            still = preview.RenderStill(look: false, lights: true, width: gw, height: gh);
            var moved = all.Select(d => { d.X += 2; return d; }).ToArray();
            preview.DropsOverride = new WetDrops.Over(moved, moved.Length, mips, 1920, 1080);
            aside = preview.RenderStill(look: false, lights: true, width: gw, height: gh);
        });
        Assert.Equal(4, sprays);

        // ── The preview's own sprays held to the game's Wet objects ─────
        // Where each is, how it is turned, its own scale (the box's size): the preview's, from the place as the editor
        // reads it, against the game's object (the author: "the wet effects on previously edited NPCs (like the Anis
        // ones on the bathroom) now show with a much larger area than before. Did we test this in game"). Until then
        // the drops above were the game's own; where the preview put them was never compared - and it put them 1/0.79
        // as far from the middle as their characters.
        double farthest = 0;
        foreach (var sys in systems.Where(s => s.Path.EndsWith("/Wet", StringComparison.Ordinal)))
        {
            string name = sys.Path.Split('/')[^2];
            var mine = emitters.Where(e => e.Name == name).Select(e => e.Emitter).ToList();
            Assert.True(mine.Count == 1, $"{sys.Path}: the preview has {mine.Count} sprays for {name}");
            var e = mine[0];
            double x = (e.X - 960) / 100, y = (540 - e.Y) / 100;
            double turn = Math.Atan2(-e.AxY, e.AxX) * 180 / Math.PI, gameTurn = sys.Turn > 180 ? sys.Turn - 360 : sys.Turn;
            _out.WriteLine($"{name}: game at ({sys.X:0.###}, {sys.Y:0.###}) turned {gameTurn:0.##} scale {sys.ScaleX:0.###} x {sys.ScaleY:0.###}; "
                           + $"preview at ({x:0.###}, {y:0.###}) turned {turn:0.##} scale {e.ScaleX:0.###} x {e.ScaleY:0.###}");
            farthest = Math.Max(farthest, Math.Max(Math.Abs(sys.X), Math.Abs(sys.Y)));
            Assert.True(Math.Abs(x - sys.X) < 0.02 && Math.Abs(y - sys.Y) < 0.02, $"{name}: the preview's spray is at ({x:0.###}, {y:0.###}), the game's at ({sys.X:0.###}, {sys.Y:0.###})");
            Assert.InRange(turn, gameTurn - 0.5, gameTurn + 0.5);
            Assert.Equal(sys.ScaleX, e.ScaleX, 3);
            Assert.Equal(sys.ScaleY, e.ScaleY, 3);
            Assert.Equal(sys.AreaW, e.AreaW, 3);
            Assert.Equal(sys.AreaH, e.AreaH, 3);
        }
        // Control: a spray far enough from the middle that putting it 1/0.79 as far (the mistake) misses by a lot.
        Assert.True(farthest * (1 / 0.79 - 1) > 0.5, "control: every spray is too near the middle for a misplaced one to show");

        Assert.NotNull(still);
        Assert.NotNull(aside);
        // The preview at the test screen's size: a game pixel is one of its pixels.
        static int Mine(byte[] p, int x, int y, int c) => p[(y * 960 + x) * 4 + c];
        static int Diff(byte[] g, int i, byte[] p, int x, int y)
            => Math.Max(Math.Abs(g[i] - Mine(p, x, y, 0)), Math.Max(Math.Abs(g[i + 1] - Mine(p, x, y, 1)), Math.Abs(g[i + 2] - Mine(p, x, y, 2))));
        int dropPixels = 0, near = 0, nearOff = 0, nearAside = 0, floor = 0, floorNear = 0;
        var worst = new List<string>();
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                int change = Math.Max(Math.Abs(gameOn[i] - gameOff[i]), Math.Max(Math.Abs(gameOn[i + 1] - gameOff[i + 1]), Math.Abs(gameOn[i + 2] - gameOff[i + 2])));
                if (change > 3)
                {
                    dropPixels++;
                    if (Diff(gameOn, i, still!, x, y) <= 6) near++;
                    else if (worst.Count < 12)
                        worst.Add($"({x},{y}) game {gameOn[i + 2]},{gameOn[i + 1]},{gameOn[i]} preview {Mine(still!, x, y, 2)},{Mine(still!, x, y, 1)},{Mine(still!, x, y, 0)}");
                    if (Diff(gameOff, i, still!, x, y) <= 6) nearOff++;
                    if (Diff(gameOn, i, aside!, x, y) <= 6) nearAside++;
                }
                else
                {
                    floor++;
                    if (Diff(gameOn, i, still!, x, y) <= 3) floorNear++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"game {gw}x{gh}: {dropPixels} pixels a drop drew - the preview within six {Share(near, dropPixels):P2}, "
                       + $"the game's frame without the drops {Share(nearOff, dropPixels):P2}, the drops a pixel aside {Share(nearAside, dropPixels):P2}; "
                       + $"the rest {floor} - within three {Share(floorNear, floor):P2}");
        foreach (var w in worst) _out.WriteLine("   " + w);
        Assert.True(dropPixels > 40, "the game's drops drew too little of the screen to tell");
        Assert.True(Share(floorNear, floor) >= 0.98, "control: away from the drops the preview is not the game's place - the comparison is off");
        Assert.True(Share(nearOff, dropPixels) < 0.2, "control: the game's frame without its drops passes for the preview's with them");
        Assert.True(Share(nearAside, dropPixels) < 0.8, "control: the drops a game pixel aside pass for the drops where they are");
        Assert.True(Share(near, dropPixels) >= 0.95, $"the preview draws the game's drops more than six levels off at {dropPixels - near} of {dropPixels} pixels");
    }

    /// <summary>A live drop: its age, where it is (the world, its system's own scale in), its size, its colour now
    /// (straight sRGB, 0..1; its alpha the colour over life's times its start colour's).</summary>
    private sealed record LiveDrop(double Age, double X, double Y, double SizeX, double SizeY, double Alpha,
                                   double R = 1, double G = 1, double B = 1);
    private sealed record LiveSystem(string Path, double X, double Y, double Turn, double ScaleX, double ScaleY, double AreaW, double AreaH,
                                     List<LiveDrop> Drops);

    /// <summary>The bridge's particles-live: each system (where, turned how, its own scale) and its drops (age, where
    /// in the world with the system's own scale, size, alpha).</summary>
    private static List<LiveSystem> LiveDrops(IEnumerable<string> lines)
    {
        static double[] V(string s) => s.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var systems = new List<LiveSystem>();
        foreach (var line in lines)
        {
            if (line.StartsWith("== ", StringComparison.Ordinal))
            {
                var at = V(Regex.Match(line, @" at (\S+)").Groups[1].Value);
                var rot = V(Regex.Match(line, @" rot (\S+)").Groups[1].Value);
                var scale = V(Regex.Match(line, @" scale (\S+)").Groups[1].Value);
                var shape = V(Regex.Match(line, @" shape (\S+)").Groups[1].Value);
                systems.Add(new LiveSystem(Regex.Match(line, @"^== (.*?) n=\d").Groups[1].Value, at[0], at[1], rot[2], scale[0], scale[1], shape[0], shape[1], new List<LiveDrop>()));
            }
            else if (line.StartsWith("p ", StringComparison.Ordinal) && systems.Count > 0)
            {
                var f = Regex.Matches(line, @"(\w+)=(\S+)").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
                var own = V(f["worldOwn"]);
                var size = V(f["size"]);
                string hex = f["colour"];
                double Byte(int at) => int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber) / 255.0;
                systems[^1].Drops.Add(new LiveDrop(double.Parse(f["age"], CultureInfo.InvariantCulture), own[0], own[1], size[0], size[1], Byte(7),
                                                   Byte(1), Byte(3), Byte(5)));
            }
        }
        return systems;
    }
}
