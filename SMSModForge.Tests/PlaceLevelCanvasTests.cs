using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The player's room's buttons over a place (after 1.10.0, the author: "I don't see the player room buttons in the
/// preview of the Bedroom for Androids at all. Why is that?"). A place with a copy of
/// <c>5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas</c>, its Sleep button moved, as the Androids Bedroom has: the place
/// preview draws the canvas under the game's interface, the Sleep button where the place moved it and not where the
/// game's room has it (the control), the buttons the place says nothing of where the game's room has them; the moved
/// button's handles stand on it. Measured on the layer as drawn. Without the game's extracted interface there is
/// nothing to draw, and the test says so.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceLevelCanvasTests : IDisposable
{
    private const string Buttons = "5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas";
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-levelcanvas-" + Guid.NewGuid().ToString("N"));

    public PlaceLevelCanvasTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Every level's canvas drawn over the screen draws something, as the extraction has it - the place
    /// preview puts each over its level's extension (SMSMODFORGE_SHOT_DIR: each, to look at).</summary>
    [Fact]
    public void Every_levels_screen_canvas_draws()
    {
        var paths = Directory.GetFiles(Path.Combine(VanillaUiLibrary.Root, "Surfaces"), "5_Levels_*.json")
            .Select(f => Model.VanillaUiSurface.Load(f)?.Path).Where(p => p != null && LevelCanvas.SurfaceAt(p) != null).ToList();
        _out.WriteLine("level canvases over the screen: " + string.Join(", ", paths));
        Assert.Contains(Buttons, paths);
        foreach (var path in paths)
        {
            var pixels = LevelCanvas.Render(LevelCanvas.SurfaceAt(path)!, Array.Empty<LevelCanvas.Move>(), out int w, out int h);
            int drawn = pixels == null ? 0 : Enumerable.Range(0, w * h).Count(i => pixels[i * 4 + 3] > 0);
            _out.WriteLine($"{path}: {w}x{h}, {drawn} pixels drawn");
            Assert.True(drawn > 100, path + " draws nothing");
            if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels!, w * 4)));
                using var fs = File.Create(Path.Combine(shots, "canvas-" + path!.Replace('/', '_') + ".png"));
                enc.Save(fs);
            }
        }
    }

    [Fact]
    public void A_copy_of_the_rooms_button_canvas_is_drawn_with_the_places_moves_and_its_handles_on_them()
    {
        Assert.True(LevelCanvas.SurfaceAt(Buttons) != null, "the game's extracted interface has no " + Buttons);
        var original = LevelCanvas.LocalPosition(LevelCanvas.SurfaceAt(Buttons)!, "Player_Room_Buttons/SleepButton")!.Value;
        var pc = LevelCanvas.LocalPosition(LevelCanvas.SurfaceAt(Buttons)!, "Player_Room_Buttons/PCButton")!.Value;
        Png(Path.Combine(_dir, "Locations", "Bedroom.png"), 64, 36, Color.FromRgb(0x30, 0x30, 0x40));
        var pack = PackRepository.CreateEmpty("levelcanvas");
        var place = new PlaceDef { Key = "bedroom", InternalName = "Bedroom", BaseSprite = "Locations/Bedroom.png" };
        const float SleepX = 0f, SleepY = -140f;
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "PlayerRoom_ButtonCanvas", CopyOf = Buttons, StartActive = true, OverrideActive = true,
            Children =
            {
                new GameObjectDef
                {
                    Name = "Player_Room_Buttons", Bind = true,
                    Children = { new GameObjectDef { Name = "SleepButton", Bind = true, OverrideTransform = true, X = SleepX, Y = SleepY } },
                },
            },
        });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                WindowHarness.Pump();
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.Single();
                WindowHarness.Pump();
                window.UpdateLayout();
                WindowHarness.Pump();
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                Assert.Equal(new[] { Buttons }, preview.LevelCanvasesDrawn);
                Assert.True(preview.LevelCanvasesUnderTheInterface, "the room's buttons are drawn over the game's interface");

                // The layer as drawn, at the canvas's own 1920x1080.
                var layer = preview.LevelCanvasLayer;
                layer.Measure(new Size(1920, 1080));
                layer.Arrange(new Rect(0, 0, 1920, 1080));
                var shot = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                shot.Render(layer);
                var px = new byte[1920 * 1080 * 4];
                shot.CopyPixels(px, 1920 * 4, 0);
                if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
                {
                    Directory.CreateDirectory(shots);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(shot));
                    using var fs = File.Create(Path.Combine(shots, "level-canvas.png"));
                    enc.Save(fs);
                }
                // How much is drawn in the 60x60 square round a canvas point (x right, y up from the middle).
                int Drawn(double x, double y)
                {
                    int cx = (int)Math.Round(960 + x), cy = (int)Math.Round(540 - y), n = 0;
                    for (int j = cy - 30; j < cy + 30; j++)
                        for (int i = cx - 30; i < cx + 30; i++)
                            if (i >= 0 && j >= 0 && i < 1920 && j < 1080 && px[(j * 1920 + i) * 4 + 3] > 128) n++;
                    return n;
                }
                int moved = Drawn(SleepX, SleepY), left = Drawn(original.X, original.Y), pcThere = Drawn(pc.X, pc.Y), nowhere = Drawn(-900, 480);
                _out.WriteLine($"drawn round the moved Sleep button {moved} of 3600 pixels, round where the room has it {left}, round the PC button {pcThere}, an empty corner {nowhere}");
                Assert.True(moved > 1500, "the Sleep button is not drawn where the place moved it");
                Assert.True(left < 50, "control: the Sleep button is still drawn where the game's room has it");
                Assert.True(pcThere > 1500, "the PC button, which the place leaves as it is, is not where the game's room has it");
                Assert.True(nowhere < 50, "control: the canvas is drawn over a corner with nothing in it");

                // The moved button's handles stand on it.
                var sleep = vm.SelectedPlace!.GameObjects.Single(g => g.Name == "PlayerRoom_ButtonCanvas").Children.Single().Children.Single();
                Assert.True(preview.Pick(sleep), "the Sleep button could not be picked");
                WindowHarness.Pump();
                var at = preview.HandlesAt;
                _out.WriteLine($"its handles at ({at?.X:0.#}, {at?.Y:0.#}) of the canvas");
                Assert.True(at is { } h && Math.Abs(h.X - (960 + SleepX)) < 2 && Math.Abs(h.Y - (540 - SleepY)) < 2,
                            $"the Sleep button's handles are at ({at?.X:0.#}, {at?.Y:0.#}), the button at ({960 + SleepX}, {540 - SleepY})");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
