using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's interface over the place preview, drawn from the game's own canvas with the place's buttons in it
/// (GameHud; after 1.10.0, the author: "do a proper render there instead of just a PNG slapped onto it"). What is
/// measured is the drawn picture: where the buttons' white backgrounds are, the second strip behind a second row, the
/// rule under a button's words left see-through as the plugin leaves it. InGameSuiteTests.S18 holds it to the game's
/// own screen.
/// </summary>
public sealed class GameHudTests
{
    private readonly ITestOutputHelper _out;
    public GameHudTests(ITestOutputHelper output) => _out = output;

    private static readonly string? Shots = Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR");

    private static void Save(string name, byte[] pbgra, int w, int h)
    {
        if (string.IsNullOrEmpty(Shots)) return;
        Directory.CreateDirectory(Shots);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pbgra, w * 4)));
        using var fs = File.Create(Path.Combine(Shots, name));
        enc.Save(fs);
    }

    /// <summary>The white, opaque stretches along a row of the picture: where buttons' backgrounds are.</summary>
    private static List<(int From, int To)> WhiteRuns(byte[] px, int w, int y)
    {
        var runs = new List<(int, int)>();
        int start = -1;
        for (int x = 0; x <= w; x++)
        {
            bool white = x < w && px[(y * w + x) * 4 + 3] == 255 && px[(y * w + x) * 4] > 240 && px[(y * w + x) * 4 + 1] > 240 && px[(y * w + x) * 4 + 2] > 240;
            if (white && start < 0) start = x;
            if (!white && start >= 0) { runs.Add((start, x - 1)); start = -1; }
        }
        return runs;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(11)]
    public void The_navigator_has_the_places_buttons_six_to_a_row(int count)
    {
        Assert.True(VanillaUiLibrary.IsAvailable, "the game's extracted interface is not beside the editor");
        var words = Enumerable.Range(1, count).Select(i => "Room " + i).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var px = GameHud.Render(words, out int w, out int h);
        _out.WriteLine($"{count} buttons: drawn in {watch.ElapsedMilliseconds} ms");
        Assert.NotNull(px);
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
        Save($"hud-{count}.png", px!, w, h);

        // The bottom row's buttons: 125 wide, 140 apart, centred, their middles 69.2 above the bottom (Navigator at
        // -18, MapButtons +87.2 in it, a button 54.305 below MapButtons' top).
        // Below the words, above the round corners.
        int y = (int)Math.Round(1080 - 69.2 + 20);
        var runs = WhiteRuns(px!, w, y).Where(r => r.To - r.From > 100).ToList();
        int last = count == 0 ? 0 : count % 6 == 0 ? 6 : count % 6;
        Assert.Equal(last, runs.Count);
        for (int i = 0; i < runs.Count; i++)
        {
            double middle = 960 - (last - 1) * 70.0 + i * 140;
            Assert.InRange((runs[i].From + runs[i].To) / 2.0, middle - 2, middle + 2);
        }
        // The row above, 95 higher, while there is one.
        var above = WhiteRuns(px!, w, y - 95).Where(r => r.To - r.From > 100).ToList();
        Assert.Equal(count > 6 ? 6 : 0, above.Count);
    }

    /// <summary>A button's words are drawn on it; the black rule under them is not (the plugin leaves it see-through
    /// until the button is hovered), and neither is the game's own Beach button.</summary>
    [Fact]
    public void A_buttons_words_are_drawn_and_the_rule_under_them_is_not()
    {
        var one = GameHud.Render(new[] { "Bedroom" }, out int w, out _)!;
        var other = GameHud.Render(new[] { "Kitchen" }, out _, out _)!;
        // Inside the one button (125 x 75 round 960, 1010.8): the words differ, so the pictures do.
        int differ = 0, dark = 0;
        for (int y = 974; y < 1048; y++)
            for (int x = 900; x < 1020; x++)
            {
                int i = (y * w + x) * 4;
                if (Math.Abs(one[i] - other[i]) > 30) differ++;
                // The rule: black, 24 below the button's middle, 100 wide - inside the button, away from its corners.
                if (y >= 1030 && y <= 1040 && x >= 930 && x <= 990 && one[i + 3] == 255 && one[i] < 40 && one[i + 1] < 40 && one[i + 2] < 40) dark++;
            }
        _out.WriteLine($"pixels the words change: {differ}; dark pixels where the rule would be: {dark}");
        Assert.True(differ > 200, "the button's words are not drawn");
        Assert.True(dark < 20, "the rule under the words is drawn: the plugin leaves it see-through at rest");
    }
}
