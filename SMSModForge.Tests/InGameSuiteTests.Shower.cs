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
using Looks = SMSModForge.Tests.InGameSuitePacks.Looks;

namespace SMSModForge.Tests;

/// <summary>
/// A shower (after 1.10.0, the author: "I think there are 2 different wet effects at play here. One is the towel Anna
/// one, and the other is the Anna shower NPC sprite one ... the shower one might actually be the one that resembles an
/// actual shower ... we might only want to apply the top and bottom settings to that one"; "I want them to disappear
/// abruptly at the floor of the box"; "tweak some of the key settings of the particle effect, including color").
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The shower place (InGameSuitePacks.Looks): the game's AnnaInShower system as it is, and one with every setting -
    /// a box 3 x 3, the drops from its top side, gone at its floor, tinted, half as many, half as big again, falling
    /// at 0.8. In the game:
    /// <list type="bullet">
    /// <item>each system's settings are what the placement says, over the game's own;</item>
    /// <item>every live drop is the size, the colour and the place those settings give it - the set one's started on
    /// its box's top side and none is below its floor; control: the game's own shower has drops below its box;</item>
    /// <item>while they fall, nothing is drawn under the set one's floor, frame after frame (the drops are taken out
    /// before a frame is drawn, not after); control: under the plain one's, there is.</item>
    /// </list>
    /// And the preview: its two emitters are where the game's are, with the same settings; the game's own drops,
    /// drawn by the preview as a shower's (solid quads, blended into the place before its lights), are the game's picture,
    /// held to how much they change each pixel - control: the drops two game pixels aside are off by most of it.
    /// </summary>
    [Fact]
    public void S21_A_showers_drops_as_the_game_draws_them()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + Looks.Id + "." + Looks.ShowerPlaceKey;
        var hidden = new List<string> { "9_MainCanvas" };
        var wets = new List<string>();
        string plainSettings, setSettings, live, on, off;
        var running = new List<string>();
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "shower");
        Directory.CreateDirectory(keep);
        static string FileIn(string said) => Regex.Match(said, @"[A-Za-z]:\\.*$").Value.Trim();
        try
        {
            Travel(game, token, "_" + Looks.ShowerPlaceObject);
            string level = "5_Levels/" + game.Ask("level").Split(' ').Last().Trim();
            string canvases = game.Ask("find-all *Canvas*");
            hidden.AddRange(canvases.Substring(canvases.IndexOf(':') + 1).Split(" | ", StringSplitOptions.RemoveEmptyEntries)
                                     .Select(p => p.Trim()).Where(p => p.Contains(level + "/")));
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("postfx off");
            wets.Add(level + "/NPCs/" + Looks.ShowerPlain + "/Wet");
            wets.Add(level + "/NPCs/" + Looks.ShowerSet + "/Wet");
            // Prewarmed: as many out as there will be from the start; a moment to settle.
            game.Ask("wait 3", 10);
            // Frames while the drops fall: nothing may be drawn under the set one's floor in any.
            for (int i = 0; i < 8; i++)
            {
                running.Add(LookFolder(game.Ask("screen-look shower-run-" + i, 15)));
                game.Ask("wait 0.13", 10);
            }
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            // Each answer's file is the same name for both: read at once.
            plainSettings = File.ReadAllText(FileIn(game.Ask("particles " + wets[0], 20)));
            setSettings = File.ReadAllText(FileIn(game.Ask("particles " + wets[1], 20)));
            live = File.ReadAllText(FileIn(game.Ask("particles-live " + level + "/NPCs", 20)));
            on = LookFolder(game.Ask("screen-look shower-on", 15));
            foreach (var w in wets) game.Ask("set-active " + w + " false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look shower-off", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("postfx on");
            foreach (var w in wets) game.Ask("set-active " + w + " true");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        File.WriteAllText(Path.Combine(keep, "plain-settings.txt"), plainSettings);
        File.WriteAllText(Path.Combine(keep, "set-settings.txt"), setSettings);
        File.WriteAllText(Path.Combine(keep, "live.txt"), live);
        File.Copy(Path.Combine(on, "screen.png"), Path.Combine(keep, "on.png"), true);
        File.Copy(Path.Combine(off, "screen.png"), Path.Combine(keep, "off.png"), true);
        for (int i = 0; i < running.Count; i++) File.Copy(Path.Combine(running[i], "screen.png"), Path.Combine(keep, "run-" + i + ".png"), true);
        _out.WriteLine("kept in " + keep);
        foreach (var line in plainSettings.Split('\n').Concat(setSettings.Split('\n'))) _out.WriteLine("   " + line.TrimEnd());

        // ── Each system's settings: the game's own, and the placement's over them ──────────────
        var set = Looks.ShowerSettings;
        float tintR = 0xFF / 255f, tintG = 0xA0 / 255f, tintB = 0x80 / 255f, tintA = 0xC0 / 255f;
        ShowerSettingsHeld(plainSettings, "the game's shower", amount: 1, size: 1, fall: 1, (1, 1, 1, 1), w: 2, h: 1, fromTop: false);
        ShowerSettingsHeld(setSettings, "the set shower", set.Amount!.Value, set.Size!.Value, set.Fall!.Value, (tintR, tintG, tintB, tintA),
                 set.Width!.Value, set.Height!.Value, fromTop: true);

        // ── Every live drop ────────────────────────────────────────────
        var systems = LiveDrops(live.Split('\n').Select(l => l.TrimEnd('\r')));
        Assert.Equal(2, systems.Count);
        var plain = systems.Single(s => s.Path.Contains("/" + Looks.ShowerPlain + "/"));
        var setSys = systems.Single(s => s.Path.Contains("/" + Looks.ShowerSet + "/"));
        _out.WriteLine($"the game's shower: {plain.Drops.Count} drops at ({plain.X:0.###}, {plain.Y:0.###}) scale {plain.ScaleX:0.####}; "
                       + $"the set one: {setSys.Drops.Count} at ({setSys.X:0.###}, {setSys.Y:0.###}) scale {setSys.ScaleX:0.####}");
        Assert.Equal(WetDrops.ShowerOwnScale, plain.ScaleX, 3);
        Assert.Equal(WetDrops.ShowerOwnScale * 1.2, setSys.ScaleX, 3);
        Assert.InRange(plain.Drops.Count, 380, 400);
        Assert.InRange(setSys.Drops.Count, 1, 200);
        int belowPlain = ShowerDropsHeld(plain, "the game's shower", 1, 1, 1, (1, 1, 1, 1), 2, 1, fromTop: false, floor: false);
        int belowSet = ShowerDropsHeld(setSys, "the set shower", set.Amount.Value, set.Size.Value, set.Fall.Value, (tintR, tintG, tintB, tintA),
                             set.Width.Value, set.Height.Value, fromTop: true, floor: true);
        _out.WriteLine($"drops whose lower end is under their box's floor: the game's shower {belowPlain}, the set one {belowSet}");
        Assert.Equal(0, belowSet);
        Assert.True(belowPlain > 50, "control: the game's own shower has no drops under its box - the floor check cannot fail");

        // ── While they fall: nothing drawn under the set one's floor ────────────────
        var (gw, gh, gameOff) = Png(Path.Combine(keep, "off.png"));
        (int L, int T, int R, int B) Under(LiveSystem s, double w, double h)
        {
            double floor = s.Y - h / 2 * s.ScaleY, half = w / 2 * s.ScaleX;
            int row = (int)Math.Ceiling(gh / 2.0 - floor * 50) + 2;
            return ((int)Math.Floor(gw / 2.0 + (s.X - half) * 50) - 1, row, (int)Math.Ceiling(gw / 2.0 + (s.X + half) * 50) + 1, Math.Min(gh - 1, row + 60));
        }
        int Changed(byte[] frame, (int L, int T, int R, int B) a)
        {
            int n = 0;
            for (int y = Math.Max(0, a.T); y <= a.B; y++)
                for (int x = Math.Max(0, a.L); x <= Math.Min(gw - 1, a.R); x++)
                {
                    int i = (y * gw + x) * 4;
                    if (Math.Max(Math.Abs(frame[i] - gameOff[i]), Math.Max(Math.Abs(frame[i + 1] - gameOff[i + 1]), Math.Abs(frame[i + 2] - gameOff[i + 2]))) > 4) n++;
                }
            return n;
        }
        var underSet = Under(setSys, set.Width.Value, set.Height.Value);
        var underPlain = Under(plain, 2, 1);
        int setRun = 0, plainRun = 0;
        for (int i = 0; i < running.Count; i++)
        {
            var (_, _, frame) = Png(Path.Combine(keep, "run-" + i + ".png"));
            int a = Changed(frame, underSet), b = Changed(frame, underPlain);
            _out.WriteLine($"frame {i}: under the set one's floor {a} pixels drawn, under the game's shower's box {b}");
            setRun += a; plainRun += b;
        }
        Assert.Equal(0, setRun);
        Assert.True(plainRun > 20, "control: nothing is drawn under the game's own shower either - the frames cannot show a floor");

        // ── The preview ────────────────────────────────────────────────
        var all = new List<WetDrops.Drop>();
        foreach (var sys in systems)
            foreach (var d in sys.Drops)
                all.Add(new WetDrops.Drop
                {
                    X = 960 + d.X * 100, Y = 540 - d.Y * 100, W = d.SizeX * Math.Abs(sys.ScaleX) * 100, H = d.SizeY * Math.Abs(sys.ScaleY) * 100,
                    Alpha = (float)d.Alpha, R = (float)d.R, G = (float)d.G, B = (float)d.B, Solid = true, Lit = true,
                });
        var (_, _, gameOn) = Png(Path.Combine(keep, "on.png"));
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", Looks.Id);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        byte[]? still = null, aside = null;
        IReadOnlyList<(string Name, WetDrops.Emitter Emitter)> emitters = Array.Empty<(string, WetDrops.Emitter)>();
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var vm = new PlaceViewModel(pack.Places.Single(p => p.Key == Looks.ShowerPlaceKey));
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
            emitters = preview.Emitters;
            // At the game's own pixels: a drop thinner than a pixel is the pixels whose middles it covers, there as in
            // the game; blended into the place before its lights, in linear light, as the game's lit material is.
            var mips = WetDrops.Texture!;
            preview.DropsOverride = new WetDrops.Over(all.ToArray(), all.Count, mips, 1920, 1080);
            still = preview.RenderStill(look: false, lights: true, width: gw, height: gh);
            var moved = all.Select(d => { d.X += 4; return d; }).ToArray();
            preview.DropsOverride = new WetDrops.Over(moved, moved.Length, mips, 1920, 1080);
            aside = preview.RenderStill(look: false, lights: true, width: gw, height: gh);
        });
        // Beside the game's, for a look by eye.
        foreach (var (name, px) in new[] { ("preview.png", still), ("preview-aside.png", aside) })
        {
            if (px == null) continue;
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                System.Windows.Media.Imaging.BitmapSource.Create(gw, gh, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, gw * 4)));
            using var fs = File.Create(Path.Combine(keep, name));
            enc.Save(fs);
        }

        // Its emitters: where the game's systems are, its scale (the shower's 1.053 in it), its settings.
        foreach (var (sys, name, settings) in new[] { (plain, Looks.ShowerPlain, (WetEffectDef?)null), (setSys, Looks.ShowerSet, set) })
        {
            var e = emitters.Single(m => m.Name == name).Emitter;
            double x = (e.X - 960) / 100, y = (540 - e.Y) / 100;
            _out.WriteLine($"{name}: game at ({sys.X:0.###}, {sys.Y:0.###}) scale {sys.ScaleX:0.####} box {sys.AreaW:0.##} x {sys.AreaH:0.##}; "
                           + $"preview at ({x:0.###}, {y:0.###}) scale {e.ScaleX:0.####} box {e.AreaW:0.##} x {e.AreaH:0.##}");
            Assert.Equal(WetDrops.Kind.Shower, e.Of);
            Assert.True(Math.Abs(x - sys.X) < 0.02 && Math.Abs(y - sys.Y) < 0.02, $"{name}: the preview's shower is at ({x:0.###}, {y:0.###}), the game's at ({sys.X:0.###}, {sys.Y:0.###})");
            Assert.Equal(sys.ScaleX, e.ScaleX, 3);
            Assert.Equal(sys.ScaleY, e.ScaleY, 3);
            Assert.Equal(settings?.Width ?? WetDrops.ShowerW, e.AreaW, 3);
            Assert.Equal(settings?.Height ?? WetDrops.ShowerH, e.AreaH, 3);
            Assert.Equal(settings?.FromTop == true, e.FromTop);
            Assert.Equal(settings?.Floor == true, e.Floor);
            Assert.Equal(settings?.Amount ?? 1f, e.Amount, 3);
            Assert.Equal(settings?.Size ?? 1f, e.Size, 3);
            Assert.Equal(settings?.Fall ?? 1f, e.Fall, 3);
            Assert.Equal(settings == null ? 1f : tintG, e.TintG, 3);
            Assert.Equal(settings == null ? 1f : tintA, e.TintA, 3);
        }

        // The game's drops drawn by the preview, against the game's frame, at its own pixels.
        Assert.NotNull(still);
        Assert.NotNull(aside);
        static int Mine(byte[] p, int x, int y, int c, int w) => p[(y * w + x) * 4 + c];
        int Diff(byte[] g, int i, byte[] p, int x, int y)
            => Math.Max(Math.Abs(g[i] - Mine(p, x, y, 0, gw)), Math.Max(Math.Abs(g[i + 1] - Mine(p, x, y, 1, gw)), Math.Abs(g[i + 2] - Mine(p, x, y, 2, gw))));
        // The drops are faint (a few levels at many of their pixels), so beside how many pixels come within six, how
        // far off each is against how much the drops changed it: the change itself is the scale (as S20's).
        int dropPixels = 0, near = 0, floor = 0, floorNear = 0;
        long change = 0, offMine = 0, offAside = 0;
        var worst = new List<string>();
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                int c = Math.Max(Math.Abs(gameOn[i] - gameOff[i]), Math.Max(Math.Abs(gameOn[i + 1] - gameOff[i + 1]), Math.Abs(gameOn[i + 2] - gameOff[i + 2])));
                if (c > 3)
                {
                    dropPixels++;
                    change += c;
                    int m = Diff(gameOn, i, still!, x, y);
                    offMine += m;
                    offAside += Diff(gameOn, i, aside!, x, y);
                    if (m <= 6) near++;
                    else if (worst.Count < 12)
                        worst.Add($"({x},{y}) game {gameOn[i + 2]},{gameOn[i + 1]},{gameOn[i]} preview {Mine(still!, x, y, 2, gw)},{Mine(still!, x, y, 1, gw)},{Mine(still!, x, y, 0, gw)} (no drops {gameOff[i + 2]},{gameOff[i + 1]},{gameOff[i]})");
                }
                else
                {
                    floor++;
                    if (Diff(gameOn, i, still!, x, y) <= 3) floorNear++;
                }
            }
        double Share(long n, long of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"game {gw}x{gh}: {dropPixels} pixels the drops drew (by {Share(change, dropPixels):0.0} levels on average) - the preview "
                       + $"within six {Share(near, dropPixels):P2}, off by {Share(offMine, change):P1} of the change; the drops two pixels aside "
                       + $"off by {Share(offAside, change):P1}; the rest {floor} - within three {Share(floorNear, floor):P2}");
        foreach (var w in worst) _out.WriteLine("   " + w);
        Assert.True(dropPixels > 200, "the game's showers drew too little of the screen to tell");
        Assert.True(Share(floorNear, floor) >= 0.98, "control: away from the drops the preview is not the game's place - the comparison is off");
        Assert.True(Share(offAside, change) > 0.5, "control: the drops two pixels aside pass for the drops where they are");
        Assert.True(Share(offMine, change) < 0.2, $"the preview is off by {Share(offMine, change):P1} of what the game's drops change");
        Assert.True(Share(near, dropPixels) >= 0.95, $"the preview draws the game's shower drops more than six levels off at {dropPixels - near} of {dropPixels} pixels");
    }

    /// <summary>A shower system's settings as the bridge's <c>particles</c> wrote them, held to the game's own and the
    /// placement's over them.</summary>
    private void ShowerSettingsHeld(string text, string what, double amount, double size, double fall, (float R, float G, float B, float A) tint,
                          double w, double h, bool fromTop)
    {
        static double[] V(string s) => s.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
        string Find(string pattern) { var m = Regex.Match(text, pattern); Assert.True(m.Success, $"{what}: no {pattern} in the settings"); return m.Groups[1].Value; }
        Assert.Equal(WetDrops.Shower.Rate * amount, D(Find(@"emission on rate=(\S+)")), 2);
        Assert.Equal(WetDrops.Shower.Life, D(Find(@" lifetime=(\S+)")), 3);
        Assert.Equal(WetDrops.Shower.Gravity * fall, D(Find(@" gravity=(\S+) space")), 3);
        Assert.True(D(Find(@" max=(\d+)")) >= WetDrops.Shower.Rate * amount * WetDrops.Shower.Life, $"{what}: fewer drops allowed than it makes");
        var sx = Find(@"size3D x=(\S+)").Split(".."); var sy = Find(@" y=(\S+) z=").Split("..");
        Assert.Equal(WetDrops.Shower.SizeXMin * size, D(sx[0]), 3);
        Assert.Equal(WetDrops.Shower.SizeXMax * size, D(sx[1]), 3);
        Assert.Equal(WetDrops.Shower.SizeYMin * size, D(sy[0]), 3);
        Assert.Equal(WetDrops.Shower.SizeYMax * size, D(sy[1]), 3);
        var colours = Find(@" colour=(#\w+\.\.#\w+)").Split("..");
        void Colour(string hex, float r, float g, float b, float a)
        {
            int Byte(int at) => int.Parse(hex.Substring(at, 2), NumberStyles.HexNumber);
            Assert.InRange(Byte(1), r * tint.R * 255 - 2, r * tint.R * 255 + 2);
            Assert.InRange(Byte(3), g * tint.G * 255 - 2, g * tint.G * 255 + 2);
            Assert.InRange(Byte(5), b * tint.B * 255 - 2, b * tint.B * 255 + 2);
            Assert.InRange(Byte(7), a * tint.A * 255 - 2, a * tint.A * 255 + 2);
        }
        var s = WetDrops.Shower;
        Colour(colours[0], s.R0, s.G0, s.B0, s.A0);
        Colour(colours[1], s.R1, s.G1, s.B1, s.A1);
        var pos = V(Find(@"shape on Box .* pos=(\S+) rot="));
        var scale = V(Find(@"shape on Box .* scale=(\S+)"));
        Assert.Equal(0, pos[0], 3);
        Assert.Equal(fromTop ? h / 2 : 0, pos[1], 3);
        Assert.Equal(w, scale[0], 3);
        Assert.Equal(fromTop ? 0 : h, scale[1], 3);
    }

    /// <summary>
    /// Every live drop of a shower held to its settings: its size and colour in their ranges, where it started (its
    /// fall backed up, into its system's own units) in the box or on its top side; how many have their lower end
    /// under the box's floor - none may, with <paramref name="floor"/>.
    /// </summary>
    private int ShowerDropsHeld(LiveSystem sys, string what, double amount, double size, double fall, (float R, float G, float B, float A) tint,
                      double w, double h, bool fromTop, bool floor)
    {
        var s = WetDrops.Shower;
        double g = WetDrops.Gravity * s.Gravity * fall;
        int below = 0;
        foreach (var d in sys.Drops)
        {
            Assert.InRange(d.SizeX, Math.Min(s.SizeXMin, s.SizeXMax) * size - 1e-4, Math.Max(s.SizeXMin, s.SizeXMax) * size + 1e-4);
            Assert.InRange(d.SizeY, Math.Min(s.SizeYMin, s.SizeYMax) * size - 1e-4, Math.Max(s.SizeYMin, s.SizeYMax) * size + 1e-4);
            Assert.InRange(d.R, Math.Min(s.R0, s.R1) * tint.R - 0.01, Math.Max(s.R0, s.R1) * tint.R + 0.01);
            Assert.InRange(d.G, Math.Min(s.G0, s.G1) * tint.G - 0.01, Math.Max(s.G0, s.G1) * tint.G + 0.01);
            Assert.InRange(d.B, Math.Min(s.B0, s.B1) * tint.B - 0.01, Math.Max(s.B0, s.B1) * tint.B + 0.01);
            double fade = WetDrops.AlphaOver(d.Age / s.Life, s.FadeIn, s.FadeOut);
            Assert.InRange(d.Alpha, Math.Min(s.A0, s.A1) * tint.A * fade - 0.01, Math.Max(s.A0, s.A1) * tint.A * fade + 0.01);
            // Where it started: its fall backed up; the game's frame-by-frame steps fall it a little further.
            double slack = (0.5 * g * d.Age / 30 + 0.01) / Math.Abs(sys.ScaleY);
            double lx = (d.X - sys.X) / sys.ScaleX, ly = (d.Y + 0.5 * g * d.Age * d.Age - sys.Y) / sys.ScaleY;
            Assert.True(Math.Abs(lx) <= w / 2 + 0.01, $"{what}: a drop started {lx:0.###} across its box, outside it");
            if (fromTop) Assert.True(Math.Abs(ly - h / 2) <= slack, $"{what}: a drop aged {d.Age:0.###} started {ly:0.###} up its box, not on its top side ({h / 2:0.###})");
            else Assert.True(Math.Abs(ly) <= h / 2 + slack, $"{what}: a drop aged {d.Age:0.###} started {ly:0.###} up its box, outside it");
            double bottom = (d.Y - d.SizeY * Math.Abs(sys.ScaleY) / 2 - sys.Y) / sys.ScaleY;
            if (bottom < -h / 2 - 1e-3) below++;
        }
        return below;
    }
}
