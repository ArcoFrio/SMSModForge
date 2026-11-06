using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The editor's preview against the game: the same screen, drawn by both.
/// <para/>
/// Where a pack's objects land is read from the game and compared with where the
/// editor's geometry puts them. What they look like is a screenshot of the game
/// and the preview's own picture, compared cell by cell. A preview that is
/// nearly right is the worst kind of wrong: an author believes it and finds out
/// from a player.
/// </summary>
public sealed partial class InGameSuiteTests
{
    /// <summary>How far a corner may be from the game's, in canvas pixels.</summary>
    private const double PlaceTolerance = 1.5;

    /// <summary>A cell's mean difference per colour channel, out of 255. Text
    /// and soft edges never match to the pixel, so this is what is left once
    /// they are as close as an anti-aliased picture gets. Measured at 0.3 to 4.1
    /// (1.8.3); the worst before the preview was put right was 44.</summary>
    private const double PictureTolerance = 5.0;

    /// <summary>The same over a whole game screen, which is mostly flat colour.
    /// Measured at 1.6 (1.8.3), 16.7 before.</summary>
    private const double ScreenTolerance = 3.0;

    /// <summary>Everything the game's copy of the sheet needs before it can be
    /// looked at: open, at one canvas pixel to one screen pixel.</summary>
    private static string SheetPath => "PackUiCanvas_" + InGameSuitePreviewSheet.Id + "/Pack_" + InGameSuitePreviewSheet.Id + "/Sheet";

    [Fact]
    public void F10_The_preview_shows_what_the_game_draws()
    {
        if (Off) return;
        var game = _s.InGame();

        string folder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "preview-vs-game");
        Directory.CreateDirectory(folder);

        Play(game, "opensheet");
        game.Ask($"resolution {InGameSuitePreviewSheet.CanvasWidth} {InGameSuitePreviewSheet.CanvasHeight}");
        game.Ask("wait 2", 15);
        try
        {
            Assert.True(ActiveSoon(game, SheetPath, true), "The sheet did not open.");
            game.Ask("wait 1", 10);

            
            var ui = InGameSuitePreviewSheet.Build();
            var root = ui.Nodes.Single();
            var report = new StringBuilder();

            // ── Where things land ──
            var rects = UiGeometry.ScreenRects(root, InGameSuitePreviewSheet.CanvasWidth, InGameSuitePreviewSheet.CanvasHeight);
            var misplaced = new List<string>();
            int measured = 0;
            void Walk(UiNodeDef node, string path, bool turned)
            {
                turned |= Math.Abs(node.Rect.RotationZ) > 0.01f;
                // A turned object's corners are not a rectangle's, so those are
                // left to the picture.
                if (!turned)
                {
                    var game01 = OnScreen(Find(game, path));
                    var mine = rects[node];
                    double l = game01.L * InGameSuitePreviewSheet.CanvasWidth, r = game01.R * InGameSuitePreviewSheet.CanvasWidth;
                    double t = (1 - game01.T) * InGameSuitePreviewSheet.CanvasHeight, b = (1 - game01.B) * InGameSuitePreviewSheet.CanvasHeight;
                    measured++;
                    if (Math.Abs(l - mine.X) > PlaceTolerance || Math.Abs(r - mine.Right) > PlaceTolerance
                        || Math.Abs(t - mine.Y) > PlaceTolerance || Math.Abs(b - mine.Top) > PlaceTolerance)
                        misplaced.Add($"{path}: game {l:0.#},{t:0.#} to {r:0.#},{b:0.#}; preview {mine.X:0.#},{mine.Y:0.#} to {mine.Right:0.#},{mine.Top:0.#}");
                }
                foreach (var child in node.Children) Walk(child, path + "/" + child.Name, turned);
            }
            Walk(root, SheetPath, false);
            report.AppendLine($"{measured} objects measured, {misplaced.Count} misplaced");
            foreach (var line in misplaced) report.AppendLine("  " + line);

            // ── What they look like ──
            string shot = TakeShot(game, "previewsheet");
            var gamePixels = ReadPicture(shot, out int gw, out int gh);
            Assert.Equal(InGameSuitePreviewSheet.CanvasWidth, gw);
            Assert.Equal(InGameSuitePreviewSheet.CanvasHeight, gh);

            var assets = VanillaUiLibrary.Assets;
            var drawn = new UiRenderReport();
            var preview = UiAuthoredRenderer.Render(root, gw, gh, assets, drawn);

            WritePicture(gamePixels, gw, gh, Path.Combine(folder, "game.png"));
            WritePicture(preview, gw, gh, Path.Combine(folder, "preview.png"));
            WritePicture(Difference(gamePixels, preview), gw, gh, Path.Combine(folder, "difference.png"));

            var off = new List<string>();
            foreach (var cell in root.Children)
            {
                var box = rects[cell];
                double mean = MeanDifference(gamePixels, preview, gw, box);
                report.AppendLine($"{cell.Name,-12} {mean,6:0.00}" + (mean > PictureTolerance ? "   <-- differs" : ""));
                if (mean > PictureTolerance) off.Add($"{cell.Name} ({mean:0.0})");
            }
            string written = report.ToString();
            File.WriteAllText(Path.Combine(folder, "report.txt"), written);
            _out.WriteLine(written);
            _out.WriteLine("Pictures in " + folder);

            Assert.True(misplaced.Count == 0, "The preview puts objects somewhere else:\n" + string.Join("\n", misplaced));
            Assert.True(off.Count == 0, "The preview does not look like the game in: " + string.Join(", ", off) + " - pictures in " + folder);

            // Control: the same comparison on two different cells has to come out
            // far apart, or a low number above means nothing.
            var first = rects[root.Children[0]];
            var other = new UiRect(rects[root.Children[1]].X, rects[root.Children[1]].Y, first.Width, first.Height);
            double apart = MeanDifference(gamePixels, Shifted(preview, gw, gh, (int)(other.X - first.X)), gw, first);
            _out.WriteLine($"control, one cell against its neighbour: {apart:0.00}");
            Assert.True(apart > PictureTolerance, $"Two different cells came out the same ({apart:0.0}): the comparison cannot fail.");
        }
        finally
        {
            game.Ask("resolution 960 540");
            Play(game, "closesheet");
        }
    }

    /// <summary>
    /// One of the game's own screens, with a pack's change on it: the seeded tree
    /// the editor shows an author against the real thing. Compared only where
    /// the preview draws something solid, because what shows through the rest is
    /// whatever the game has behind it.
    /// </summary>
    [Fact]
    public void F11_The_preview_of_a_changed_game_screen_shows_what_the_game_draws()
    {
        if (Off) return;
        var game = _s.InGame();

        string folder = Path.Combine(Path.GetDirectoryName(_s.Report)!, "preview-vs-game");
        Directory.CreateDirectory(folder);

        // As an author opens it: what the save kept, laid over the game's screen.
        var saved = PackRepository.CreateEmpty("previewcheck");
        saved.Uis.Add(InGameSuitePacks.Story.PatchedUi!);
        string savedTo = Path.Combine(folder, "gamescreen-pack");
        Directory.CreateDirectory(savedTo);
        PackRepository.Save(saved, savedTo);
        var ui = new ViewModel.UiViewModel(PackRepository.Deserialize(File.ReadAllText(Path.Combine(savedTo, "modpack.json"))).Uis.Single());
        var root = ui.Nodes[0].Model;
        string path = InGameSuitePacks.Story.GameScreen.Substring("vanillaui:".Length);
        string report = "";

        game.Ask($"resolution {InGameSuitePreviewSheet.CanvasWidth} {InGameSuitePreviewSheet.CanvasHeight}");
        game.Ask("set-active " + path + " true");
        game.Ask("wait 2", 15);
        try
        {
            // Windows' fonts, added for letters the game's fonts lack, stay behind
            // the game's own last resort: its English sentences keep their look.
            string note = game.Ask("text-info says:forget to drink");
            _out.WriteLine(note);
            Assert.DoesNotContain("SMSModForge fallback", note);

            string shot = TakeShot(game, "gamescreen");
            var gamePixels = ReadPicture(shot, out int gw, out int gh);
            var preview = UiAuthoredRenderer.Render(root, gw, gh, VanillaUiLibrary.Assets, new UiRenderReport());
            WritePicture(gamePixels, gw, gh, Path.Combine(folder, "gamescreen-game.png"));

            // Over the game's own picture, so what the preview leaves see-through
            // reads as the same scene rather than as a white page.
            var over = (byte[])gamePixels.Clone();
            UiCompositor.Blend(over, gw, gh, preview, gw, gh, 0, 0);
            WritePicture(over, gw, gh, Path.Combine(folder, "gamescreen-preview.png"));

            // The one thing here the game decides as it runs: a slider stands at
            // the player's own setting, which no screen in the editor can know.
            var left = UiGeometry.ScreenRects(root, gw, gh)[root.Children.Single(c => c.Name == "Slider")];

            long sum = 0, count = 0;
            for (int i = 0; i + 3 < preview.Length; i += 4)
            {
                if (preview[i + 3] != 255) continue;
                int px = i / 4 % gw, py = i / 4 / gw;
                if (px >= left.X && px < left.Right && py >= left.Y && py < left.Top) continue;
                for (int c = 0; c < 3; c++) sum += Math.Abs(gamePixels[i + c] - preview[i + c]);
                count += 3;
            }
            double mean = count == 0 ? 0 : (double)sum / count;
            report = $"game screen {path}: {count / 3} solid pixels, mean difference {mean:0.00}";
            _out.WriteLine(report);
            _out.WriteLine("Pictures in " + folder);
            Assert.True(count > 10000, "The preview drew almost nothing solid to compare.");
            Assert.True(mean <= ScreenTolerance, report + " - pictures in " + folder);
        }
        finally
        {
            game.Ask("set-active " + path + " false");
            game.Ask("resolution 960 540");
        }
    }

    /// <summary>
    /// Nearly half of the game's labels have a font style, and no screen of a
    /// pack's own can have one, so the sheet cannot check them. Each kind found on
    /// the game's screens has its line widths read from the game: a style drawn
    /// wrong changes where the lines end before it changes anything else.
    /// </summary>
    [Fact]
    public void F12_The_preview_lays_out_the_games_styled_labels_as_the_game_does()
    {
        if (Off) return;
        var game = _s.InGame();
        var off = new List<string>();
        int measured = 0;
        var kinds = new HashSet<string>();

        foreach (string name in new[] { "Calendar", "Starmaker", "Traits" })
        {
            string path = "9_MainCanvas/" + name;
            var ui = new ViewModel.UiViewModel(new UiDef { Source = "vanillaui:" + path });
            Assert.NotEmpty(ui.Nodes);
            game.Ask("set-active " + path + " true");
            game.Ask("wait 1", 10);
            try
            {
                var labels = new List<(UiNodeDef Node, UiTextDef Text)>();
                void Walk(UiNodeDef n)
                {
                    if (!n.StartActive) return;
                    if (n.Text != null && n.Text.GameStyle is { Length: > 0 } style && style != "Normal"
                        && n.Text.Value.Length > 3 && !n.Text.Value.Contains('\n') && !n.Text.Value.Contains('<'))
                        labels.Add((n, n.Text));
                    foreach (var c in n.Children) Walk(c);
                }
                // The screen itself is switched off in the game's files: it was opened above.
                foreach (var top in ui.Nodes[0].Model.Children) Walk(top);

                foreach (var group in labels.GroupBy(l => l.Text.GameStyle))
                {
                    int tried = 0;
                    foreach (var (node, text) in group)
                    {
                        if (tried >= 3) break;
                        string said;
                        try { said = game.Ask("text-info says:" + text.Value); }
                        catch (BridgeError) { continue; }   // hidden as the game runs
                        var line = System.Text.RegularExpressions.Regex.Match(said, @"line 0 \d+-\d+ advance ([\d.,]+)");
                        var size = System.Text.RegularExpressions.Regex.Match(said, @"; size ([\d.,]+);");
                        if (!line.Success || !size.Success || said.Contains("line 1 ")) continue;
                        tried++;
                        measured++;
                        kinds.Add(group.Key);

                        double inGame = double.Parse(line.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                        var set = VanillaUiLibrary.Assets.Font(text.Font);
                        if (set == null) continue;
                        var style = UiTextStyle.Parse(text.GameStyle);
                        var laid = UiTextBlock.Measure(set.Font, text.Value, style, text.Size, 0, TextAlign.Left,
                                                       text.LineSpacing, text.CharacterSpacing, UiTextBlock.Fallbacks(set, VanillaUiLibrary.Assets));
                        // The game's figure runs on through the gap after the last
                        // letter, which does not move anything on the line.
                        double mine = laid.LineWidths[0] + (text.CharacterSpacing + set.Font.NormalSpacing) / 100.0 * text.Size;
                        _out.WriteLine($"{group.Key,-22} {text.Font,-24} '{text.Value}': game {inGame:0.0}, preview {mine:0.0}");
                        if (Math.Abs(inGame - mine) > Math.Max(3, inGame * 0.02))
                            off.Add($"{group.Key} '{text.Value}' in {text.Font}: game {inGame:0.0}, preview {mine:0.0}");
                    }
                }
            }
            finally { game.Ask("set-active " + path + " false"); }
        }

        _out.WriteLine($"{measured} lines, styles: {string.Join(" | ", kinds)}");
        Assert.True(measured >= 6, "Too few styled labels were on screen to say anything.");
        Assert.True(off.Count == 0, "Lines the preview lays out differently:\n" + string.Join("\n", off));
    }

    /// <summary>A screenshot of the game as it stands, once the game has
    /// finished writing it. The file appears at the end of the next frame.</summary>
    private static string TakeShot(GameRun game, string name)
    {
        string path = game.Ask("shot " + name).Trim();
        var deadline = DateTime.Now.AddSeconds(15);
        while (DateTime.Now < deadline)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                {
                    using var s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return path;
                }
            }
            catch (IOException) { }
            Thread.Sleep(100);
        }
        throw new TimeoutException("The game never wrote its screenshot: " + path);
    }

    /// <summary>A PNG as premultiplied BGRA, top row first: the renderer's form.</summary>
    private static byte[] ReadPicture(string path, out int width, out int height)
    {
        using var s = File.OpenRead(path);
        var frame = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        width = bgra.PixelWidth;
        height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static void WritePicture(byte[] pixels, int width, int height, string path)
    {
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var s = File.Create(path);
        png.Save(s);
    }

    private static byte[] Difference(byte[] a, byte[] b)
    {
        var d = new byte[a.Length];
        for (int i = 0; i + 3 < a.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
                d[i + c] = (byte)Math.Min(255, Math.Abs(a[i + c] - b[i + c]) * 3);
            d[i + 3] = 255;
        }
        return d;
    }

    private static double MeanDifference(byte[] a, byte[] b, int width, UiRect box)
    {
        long sum = 0, count = 0;
        int x0 = (int)Math.Max(0, Math.Round(box.X)), y0 = (int)Math.Max(0, Math.Round(box.Y));
        int x1 = (int)Math.Round(box.Right), y1 = (int)Math.Round(box.Top);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1 && x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (i + 3 >= a.Length) continue;
                for (int c = 0; c < 3; c++) sum += Math.Abs(a[i + c] - b[i + c]);
                count += 3;
            }
        return count == 0 ? 0 : (double)sum / count;
    }

    private static byte[] Shifted(byte[] pixels, int width, int height, int dx)
    {
        var moved = new byte[pixels.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = x + dx;
                if (from < 0 || from >= width) continue;
                Buffer.BlockCopy(pixels, (y * width + from) * 4, moved, (y * width + x) * 4, 4);
            }
        return moved;
    }
}
