using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Rendering.Gpu;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Looks = SMSModForge.Tests.InGameSuitePacks.Looks;

namespace SMSModForge.Tests;

/// <summary>
/// A bust under the shower (after 1.10.0, the author: "there should be a toggle between the two wet types for both
/// busts and npcs"): an outfit whose particle is the "Shower" preset.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The shower figure (InGameSuitePacks.Looks.ShowerFigureBust) in MyRoom, the interface off: its shower where the
    /// plugin puts it (the middle of its picture's top side), the game's own settings, drawn unlit over the bust (the
    /// towel's material, white: the Bust layer's own lights are not the room's, and the editor has no picture of them -
    /// measured lit, the drops came out 1.6 times their colour by the window and 0.8 below). The bust preview's shower
    /// is where the game's is; the game's own drops blended by the preview's pass are the game's frame, held to how much
    /// they change each pixel - control: two game pixels aside they are off by most of it.
    /// <para/>
    /// Run it with the graphics card free. With another program keeping the card busy (image generation, 2026-11-28),
    /// the game drew the drops over the bust darker and oranger than it reports them - over the bust only 15-30% within
    /// six, the frames without drops identical to a passing run's - and with the card free the same build passed,
    /// 98.8% there.
    /// </para>
    /// </summary>
    [Fact]
    public void S22_A_busts_shower_as_the_game_draws_it()
    {
        if (Off) return;
        var game = _s.InGame();
        const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
        string root = "2_Bust_Manager/" + Looks.ShowerFigureBust;
        string wet = root + "/MBase1/Wet";
        string settings, live, look, on, off;
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "bust-shower");
        Directory.CreateDirectory(keep);
        static string FileIn(string said) => Regex.Match(said, @"[A-Za-z]:\\.*$").Value.Trim();
        game.Ask("wait 8", 20);
        game.Ask("set-active 9_MainCanvas false");
        game.Ask("set-active " + Buttons + " false");
        game.Ask("postfx off");
        game.Ask("set-active " + root + " true");
        try
        {
            game.Ask("wait 3", 20);
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            settings = File.ReadAllText(FileIn(game.Ask("particles " + wet, 20)));
            live = File.ReadAllText(FileIn(game.Ask("particles-live " + root + "/MBase1", 20)));
            look = game.Ask("bust-look " + root + "/MBase1 bust-shower", 30);
            on = LookFolder(game.Ask("screen-look bust-shower-on", 15));
            game.Ask("set-active " + wet + " false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look bust-shower-off", 15));
        }
        finally
        {
            game.Ask("freeze off");
            game.Ask("set-active " + root + " false");
            game.Ask("postfx on");
            game.Ask("set-active " + Buttons + " true");
            game.Ask("set-active 9_MainCanvas true");
        }
        File.WriteAllText(Path.Combine(keep, "settings.txt"), settings);
        File.WriteAllText(Path.Combine(keep, "live.txt"), live);
        File.Copy(Path.Combine(look.Substring("bust-shower".Length).Trim(), "info.txt"), Path.Combine(keep, "bust.txt"), true);
        File.Copy(Path.Combine(on, "screen.png"), Path.Combine(keep, "on.png"), true);
        File.Copy(Path.Combine(off, "screen.png"), Path.Combine(keep, "off.png"), true);
        _out.WriteLine("kept in " + keep);
        foreach (var line in settings.Split('\n')) _out.WriteLine("   " + line.TrimEnd());
        var systems = LiveDrops(live.Split('\n').Select(l => l.TrimEnd('\r')));
        var sys = Assert.Single(systems);
        _out.WriteLine($"the shower at ({sys.X:0.###}, {sys.Y:0.###}) scale {sys.ScaleX:0.####}: {sys.Drops.Count} drops");
        Assert.InRange(sys.Drops.Count, 380, 400);
        ShowerSettingsHeld(settings, "the bust's shower", 1, 1, 1, (1, 1, 1, 1), WetDrops.ShowerW, WetDrops.ShowerH, fromTop: false);
        // Over the bust, unlit: the towel's material, white (WetEffects.AttachBust).
        Assert.Matches(@"layer=Bust/10", settings);
        Assert.Matches(@"shader=Particles/Standard Unlit", settings);
        Assert.Equal(WetDrops.ShowerOwnScale, sys.ScaleX, 3);

        // ── The bust preview's shower where the game's is ───────────────
        var bust = File.ReadAllLines(Path.Combine(keep, "bust.txt"));
        var drawn = bust.First(l => l.StartsWith("drawnCorners=", StringComparison.Ordinal)).Substring("drawnCorners=".Length).Split(' ')[0]
                        .Split(';').Select(c => c.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        int bustBottom = (int)Math.Round(drawn[0][1]);
        var backdrop = PreviewBackdrop.Load(Path.Combine(GameRun.RepoRoot(), "SMSModForge", "Resources", "Backdrops", "MyRoom"));
        var scene = new BackdropScene(backdrop);
        string packFolder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "packs", Looks.Id);
        WetDrops.Emitter? emitter = null;
        WindowHarness.Run(_ =>
        {
            var pack = PackRepository.Load(packFolder);
            var outfit = pack.Characters.SelectMany(c => c.Outfits).Single(o => o.Key == "showerfigure");
            var preview = new JigglePreview { PackRoot = packFolder, Outfit = new OutfitViewModel(outfit) };
            Assert.NotNull(preview.WetDropsNow(scene, bustBottom, 0f));
            emitter = preview.LastEmitter;
        });
        Assert.NotNull(emitter);
        var e = emitter!.Value;
        double ex = (e.X - 960) / 100, ey = (540 - e.Y) / 100;
        _out.WriteLine($"the shower: the game's at ({sys.X:0.####}, {sys.Y:0.####}) scale {sys.ScaleX}; the preview's at ({ex:0.####}, {ey:0.####}) scale {e.ScaleX}");
        Assert.Equal(WetDrops.Kind.Shower, e.Of);
        Assert.True(e.Unlit, "the preview lights a bust's shower with the place");
        Assert.True(Math.Abs(ex - sys.X) < 0.02 && Math.Abs(ey - sys.Y) < 0.02, $"the preview's shower is at ({ex:0.###}, {ey:0.###}), the game's at ({sys.X:0.###}, {sys.Y:0.###})");
        Assert.Equal(sys.ScaleX, e.ScaleX, 3);
        Assert.Equal(sys.ScaleY, e.ScaleY, 3);

        // ── The game's drops in the bust preview's own frame ────────────
        // On the card, as the bust preview draws a bust over MyRoom (GpuFrame.Draw; the processor held to the card by
        // GpuFrameTests): with the game's drops and without. The difference they make, against the game's - the bust
        // itself out of it, the lamp's glow over the drops in it (the game adds it over them as over the bust).
        using var card = GpuFrame.TryCreate();
        Assert.True(card != null, "no graphics card device: " + GpuDevice.SharedFailure);
        var gi = scene.Prepare();
        var lamp = scene.Lamp!;
        const int S = JiggleShader.Size, N = BackdropRender.FrameSize;
        var art = new byte[S * S * 4];
        var mask = new byte[S * S * 4];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var c = Looks.FigureAt(x, y);
                int i = (y * S + x) * 4;
                art[i] = c.B; art[i + 1] = c.G; art[i + 2] = c.R; art[i + 3] = c.A;
            }
        var p = new JiggleParams();
        var shadow = new float[lamp.Shadow.W * lamp.Shadow.H];
        lamp.Shadow.Draw(new BustShadow.Caster(art, S, S, 100f), scene.Left + N / 2f, bustBottom - 0.4f + N / 2f, 2f, 1f, shadow);
        var none = new BackdropRender.PostParts(false, false, false);
        var mips = WetDrops.Texture!;
        byte[] Frame(double? aside)
        {
            WetDrops.Over? drops = null;
            if (aside is { } a)
            {
                var all = sys.Drops.Select(d => new WetDrops.Drop
                {
                    X = 960 + d.X * 100 + a, Y = 540 - d.Y * 100, W = d.SizeX * Math.Abs(sys.ScaleX) * 100, H = d.SizeY * Math.Abs(sys.ScaleY) * 100,
                    Alpha = (float)d.Alpha, R = (float)d.R, G = (float)d.G, B = (float)d.B, Solid = true,
                }).ToArray();
                drops = new WetDrops.Over(all, all.Length, mips, 1920, 1080);
            }
            var output = new byte[N * N * 4];
            card!.Draw(lamp, shadow, scene, art, mask, p, (1f, 1f, 1f, 1f), 6.21f, true, bustBottom, true, 0f, null, null, null, none,
                       JiggleShader.GameZoom, output, drops);
            return output;
        }
        var dry = Frame(null);
        var mine = Frame(0);
        var aside = Frame(4);   // two game pixels
        // Beside the game's, for a look by eye.
        foreach (var (name, px) in new[] { ("preview-dry.png", dry), ("preview-wet.png", mine) })
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                System.Windows.Media.Imaging.BitmapSource.Create(N, N, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, px, N * 4)));
            using var fs = File.Create(Path.Combine(keep, name));
            enc.Save(fs);
        }
        var (gw, gh, gameOn) = Png(Path.Combine(keep, "on.png"));
        var (_, _, gameOff) = Png(Path.Combine(keep, "off.png"));
        // Over the bust the test's own bust is not the game's (its jiggle and its moment are its own), and a drop's
        // change depends on what is under it: there, the game's drops blended by the preview's pass over the game's
        // own frame (WetDrops.BlendInto - the bust under the drops in both, the glow over it the same with them or
        // without). Wherever the preview's frame without the drops is the game's (the room round the bust), the card's
        // own frame, drops and glow and all.
        static float Linear(byte v) { float x = v / 255f; return x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f); }
        static int Srgb(float l) { float x = l <= 0.0031308f ? 12.92f * l : 1.055f * MathF.Pow(Math.Clamp(l, 0f, 1f), 1f / 2.4f) - 0.055f; return Math.Clamp((int)MathF.Round(x * 255f), 0, 255); }
        byte[] OverTheGames(double shift)
        {
            var all = sys.Drops.Select(d => new WetDrops.Drop
            {
                X = 960 + d.X * 100 + shift, Y = 540 - d.Y * 100, W = d.SizeX * Math.Abs(sys.ScaleX) * 100, H = d.SizeY * Math.Abs(sys.ScaleY) * 100,
                Alpha = (float)d.Alpha, R = (float)d.R, G = (float)d.G, B = (float)d.B, Solid = true,
            }).ToArray();
            var target = new GamePost.Target(gw, gh);
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    int i = (y * gw + x) * 4, o = ((gh - 1 - y) * gw + x) * 4;
                    target.Px[o] = Linear(gameOff[i + 2]); target.Px[o + 1] = Linear(gameOff[i + 1]); target.Px[o + 2] = Linear(gameOff[i]); target.Px[o + 3] = 1f;
                }
            WetDrops.BlendInto(target, new WetDrops.Over(all, all.Length, mips, 1920, 1080));
            var px = new byte[gw * gh * 4];
            for (int y = 0; y < gh; y++)
                for (int x = 0; x < gw; x++)
                {
                    int i = (y * gw + x) * 4, o = ((gh - 1 - y) * gw + x) * 4;
                    px[i] = (byte)Srgb(target.Px[o + 2]); px[i + 1] = (byte)Srgb(target.Px[o + 1]); px[i + 2] = (byte)Srgb(target.Px[o]); px[i + 3] = 255;
                }
            return px;
        }
        var blended = OverTheGames(0);
        var blendedAside = OverTheGames(4);
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(
                System.Windows.Media.Imaging.BitmapSource.Create(gw, gh, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, blended, gw * 4)));
            using var fs = File.Create(Path.Combine(keep, "blended.png"));
            enc.Save(fs);
        }
        int dropPixels = 0, near = 0, onTheCard = 0, overBust = 0, nearOverBust = 0;
        long change = 0, offMine = 0, offAside = 0;
        var gameDeltaBust = new long[3]; var blendDeltaBust = new long[3];
        for (int fy = 0; fy < N; fy++)
            for (int fx = 0; fx < N; fx++)
            {
                // The frame's pixel on the game's screen: its square's left at the bust's, its bottom at the screen's.
                int sx = scene.Left + fx, sy = gh - N + fy - scene.FrameBottom;
                if (sx < 0 || sx >= gw || sy < 0 || sy >= gh) continue;
                int g = (sy * gw + sx) * 4, f = (fy * N + fx) * 4;
                int c = 0, under = 0;
                for (int k = 0; k < 3; k++)
                {
                    c = Math.Max(c, Math.Abs(gameOn[g + k] - gameOff[g + k]));
                    under = Math.Max(under, Math.Abs(dry[f + k] - gameOff[g + k]));
                }
                if (c <= 3) continue;
                bool sameUnder = under <= 3;
                int m = 0, s = 0;
                for (int k = 0; k < 3; k++)
                {
                    int seen = gameOn[g + k] - gameOff[g + k];
                    if (sameUnder)
                    {
                        m = Math.Max(m, Math.Abs(seen - (mine[f + k] - dry[f + k])));
                        s = Math.Max(s, Math.Abs(seen - (aside[f + k] - dry[f + k])));
                    }
                    else
                    {
                        m = Math.Max(m, Math.Abs(gameOn[g + k] - blended[g + k]));
                        s = Math.Max(s, Math.Abs(gameOn[g + k] - blendedAside[g + k]));
                        gameDeltaBust[k] += gameOn[g + k] - gameOff[g + k];
                        blendDeltaBust[k] += blended[g + k] - gameOff[g + k];
                    }
                }
                if (!sameUnder) { overBust++; if (m <= 6) nearOverBust++; }
                dropPixels++;
                if (sameUnder) onTheCard++;
                change += c;
                offMine += m;
                offAside += s;
                if (m <= 6) near++;
            }
        double Share(int n) => dropPixels == 0 ? 0 : (double)n / dropPixels;
        double mineShare = change == 0 ? 1 : (double)offMine / change, asideShare = change == 0 ? 0 : (double)offAside / change;
        _out.WriteLine($"{dropPixels} pixels the drops changed (by {(double)change / Math.Max(1, dropPixels):0.0} levels on average), {onTheCard} of them "
                       + $"over the room: the preview within six {Share(near):P2}, off by {mineShare:P1} of the change; the drops two pixels aside off by {asideShare:P1} of it");
        // Over the bust apart (the room's are the card's own frame): how much the game's drops change it there against
        // the preview's blend of them, channel by channel (blue, green, red) - where the two part when they do.
        _out.WriteLine($"over the bust: {overBust} pixels, within six {(overBust == 0 ? 1 : (double)nearOverBust / overBust):P2}; the game's drops change them by "
                       + string.Join(", ", gameDeltaBust.Select(v => (overBust == 0 ? 0 : (double)v / overBust).ToString("0.0", CultureInfo.InvariantCulture)))
                       + " on average, the preview's blend by "
                       + string.Join(", ", blendDeltaBust.Select(v => (overBust == 0 ? 0 : (double)v / overBust).ToString("0.0", CultureInfo.InvariantCulture))));
        Assert.True(dropPixels > 200, "the game's drops drew too little to tell");
        Assert.True(onTheCard > dropPixels / 5, "too few of the drops over the room, where the glow goes over them, to tell");
        Assert.True(asideShare > 0.5, "control: the drops two pixels aside are nearly as close as the drops where they are");
        Assert.True(mineShare < 0.2, $"the preview with the game's drops is off by {mineShare:P1} of what they change");
        Assert.True(Share(near) >= 0.95, $"the preview with the game's drops is more than six levels off at {dropPixels - near} of {dropPixels}");
    }
}
