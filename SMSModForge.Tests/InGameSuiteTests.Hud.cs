using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The game's interface over the place preview (after 1.10.0, the author: "The UI texture shown on all previews should
/// match as well, and I think we should do a proper render there instead of just a PNG slapped onto it"), held to the
/// game's own screen.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>
    /// The hud place (InGameSuitePacks.Looks): eleven navigator buttons - two rows, one without words, the tenth
    /// numbered 0, the eleventh not numbered. In the game, time frozen: its frame with the interface, and without it.
    /// In the editor, the interface as the place preview draws it (GameHud, the place's buttons in it). Wherever the
    /// preview's interface is solid and even (a game pixel and the eight round it covered completely, none more than
    /// six levels from another: the pills, the strips, the buttons, the discs - everything but an edge), its colour
    /// is within six levels of the game's at 98% of the pixels: an element a pixel out of place, or of another colour,
    /// puts the game's edge or colour inside it. Edges are left out on purpose: the test copy draws its interface at
    /// 960x540, half the preview's 1920x1080, so an edge the game puts on one side of a pixel the preview halves
    /// (measured: by 8 x 8 cells only half matched within five, though the flat parts were 99.5% the same). Control:
    /// the picture it replaces (VanillaOverlays/GameplayUIExtended.png, with the buttons the preview drew over it left
    /// out) is not.
    /// </summary>
    [Fact]
    public void S18_The_place_previews_interface_is_the_games()
    {
        if (Off) return;
        var game = _s.InGame();
        string token = "pack:" + InGameSuitePacks.Looks.Id + "." + InGameSuitePacks.Looks.HudPlaceKey;
        var hidden = new List<string>();
        string on, off;
        try
        {
            Travel(game, token, "_" + InGameSuitePacks.Looks.HudPlaceObject);
            game.Ask("wait 2", 10);
            hidden.AddRange(OnBusts(game));
            foreach (var h in hidden) game.Ask("set-active " + h + " false");
            game.Ask("freeze on");
            game.Ask("wait 0.3", 10);
            on = LookFolder(game.Ask("screen-look hud-on", 15));
            game.Ask("set-active 9_MainCanvas false");
            game.Ask("wait 0.3", 10);
            off = LookFolder(game.Ask("screen-look hud-off", 15));
        }
        finally
        {
            game.Ask("set-active 9_MainCanvas true");
            game.Ask("freeze off");
            foreach (var h in hidden) game.Ask("set-active " + h + " true");
            Travel(game, "vanilla:5_MyRoom", "5_MyRoom");
        }
        string keep = Path.Combine(Path.GetDirectoryName(_s.Report)!, "hud");
        Directory.CreateDirectory(keep);
        File.Copy(Path.Combine(on, "screen.png"), Path.Combine(keep, "hud-on.png"), true);
        File.Copy(Path.Combine(off, "screen.png"), Path.Combine(keep, "hud-off.png"), true);
        var (gw, gh, gameOn) = Png(Path.Combine(keep, "hud-on.png"));
        var (_, _, gameOff) = Png(Path.Combine(keep, "hud-off.png"));

        // The preview's interface: the words its buttons say, as the place preview puts them.
        var words = InGameSuitePacks.Looks.HudButtons
            .Select(b => string.IsNullOrEmpty(b.Label) ? Shared.NavigatorLabels.Fallback(b.Target) : b.Label).ToList();
        var hud = GameHud.Render(words, out int hw, out int hh);
        Assert.NotNull(hud);
        Assert.Equal(gw * 2, hw);
        Assert.Equal(gh * 2, hh);
        SavePbgra(Path.Combine(keep, "preview-hud.png"), hud!, hw, hh);
        // The control: the picture it replaces, stretched over the screen as the preview stretched it.
        var old = new FormatConvertedBitmap(new TransformedBitmap(
            BitmapFrame.Create(new Uri(Path.Combine(TranslationCoverageTests.EditorDir, "Resources", "VanillaOverlays", "GameplayUIExtended.png"))),
            new ScaleTransform(1920.0 / 2048, 1080.0 / 1136)), PixelFormats.Pbgra32, null, 0);
        var oldPx = new byte[old.PixelWidth * old.PixelHeight * 4];
        old.CopyPixels(oldPx, old.PixelWidth * 4, 0);
        int ow = old.PixelWidth, oh = old.PixelHeight;

        // Where a picture covers a game pixel completely (its four pixels opaque): their mean, as the game's.
        static (bool Covers, int R, int G, int B) At(byte[] p, int pw, int ph, int x, int y)
        {
            int r = 0, g = 0, b = 0;
            for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int px = x * 2 + dx, py = y * 2 + dy;
                    if (px < 0 || py < 0 || px >= pw || py >= ph) return (false, 0, 0, 0);
                    int i = (py * pw + px) * 4;
                    if (p[i + 3] != 255) return (false, 0, 0, 0);
                    b += p[i]; g += p[i + 1]; r += p[i + 2];
                }
            return (true, (r + 2) / 4, (g + 2) / 4, (b + 2) / 4);
        }
        // Solid and even there: the pixel and the eight round it covered, none more than six levels from another.
        static (bool Covers, int R, int G, int B) Flat(byte[] p, int pw, int ph, int x, int y)
        {
            var at = At(p, pw, ph, x, y);
            if (!at.Covers) return (false, 0, 0, 0);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = At(p, pw, ph, x + dx, y + dy);
                    if (!n.Covers || Math.Abs(n.R - at.R) > 6 || Math.Abs(n.G - at.G) > 6 || Math.Abs(n.B - at.B) > 6) return (false, 0, 0, 0);
                }
            return at;
        }
        int covered = 0, near = 0, oldCovered = 0, oldNear = 0, drawn = 0;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int i = (y * gw + x) * 4;
                if (Math.Max(Math.Abs(gameOn[i] - gameOff[i]), Math.Max(Math.Abs(gameOn[i + 1] - gameOff[i + 1]), Math.Abs(gameOn[i + 2] - gameOff[i + 2]))) > 3) drawn++;
                var mine = Flat(hud!, hw, hh, x, y);
                if (mine.Covers)
                {
                    covered++;
                    if (Math.Abs(mine.R - gameOn[i + 2]) <= 6 && Math.Abs(mine.G - gameOn[i + 1]) <= 6 && Math.Abs(mine.B - gameOn[i]) <= 6) near++;
                }
                var theirs = Flat(oldPx, ow, oh, x, y);
                if (theirs.Covers)
                {
                    oldCovered++;
                    if (Math.Abs(theirs.R - gameOn[i + 2]) <= 6 && Math.Abs(theirs.G - gameOn[i + 1]) <= 6 && Math.Abs(theirs.B - gameOn[i]) <= 6) oldNear++;
                }
            }
        double Share(int n, int of) => of == 0 ? 0 : (double)n / of;
        _out.WriteLine($"game {gw}x{gh}: the interface drew {drawn} pixels; the preview's is solid and even at {covered} - within six {Share(near, covered):P2}; "
                       + $"the old picture's at {oldCovered} - within six {Share(oldNear, oldCovered):P2}");
        Assert.True(drawn > 20000, "the game's interface drew too little to tell");
        Assert.True(covered > 15000, "the preview's interface is solid at too little of the screen to tell");
        Assert.True(Share(oldNear, oldCovered) < 0.9, "control: the old picture passes for the game's interface");
        Assert.True(Share(near, covered) >= 0.98, $"the preview's interface is more than six levels off the game's at {covered - near} of {covered} pixels");
    }

    private static void SavePbgra(string file, byte[] px, int w, int h)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, w * 4)));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
