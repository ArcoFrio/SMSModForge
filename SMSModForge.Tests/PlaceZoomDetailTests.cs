using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Zoomed in over the whole screen, the place preview shows the art's own pixels (after 1.10.0, the author: "in full
/// screen, when I zoom in I can still see things pixelated. I think at least on full screen mode things should show at
/// full size when zoomed in close enough"). A place whose art is a checker of single pixels, zoomed in: along a row of
/// the picture as drawn, how many screen pixels each of the art's pixels takes, against what its size on screen says.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceZoomDetailTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-zoomdetail-" + Guid.NewGuid().ToString("N"));

    public PlaceZoomDetailTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
        Directory.CreateDirectory(Path.Combine(_dir, "Npcs"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    /// <summary>A checker of single pixels, two colours.</summary>
    private static void Checker(string path, int w, int h, Color a, Color b)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = ((x + y) & 1) == 0 ? a : b;
                int i = (y * w + x) * 4;
                px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = c.A;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    [Theory]
    [InlineData("art", false)]
    [InlineData("character", false)]
    [InlineData("art", true)]
    [InlineData("character", true)]
    public void Zoomed_in_over_the_whole_screen_the_art_shows_its_own_pixels(string what, bool shaders)
    {
        bool character = what == "character";
        var flat = Color.FromRgb(0x80, 0x80, 0x80);
        if (character)
        {
            Checker(Path.Combine(_dir, "Locations", "Fine.png"), 2048, 1136, flat, flat);
            Checker(Path.Combine(_dir, "Locations", "FineBack.png"), 2048, 1136, flat, flat);
            Checker(Path.Combine(_dir, "Npcs", "fine.png"), 1024, 1024, Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xE0, 0xE0, 0xE0));
            // A mask, so the character is drawn through the jiggle program as a masked one is.
            Checker(Path.Combine(_dir, "Npcs", "fine-mask.png"), 64, 64, Color.FromRgb(0, 0, 0), Color.FromRgb(0, 0, 0));
        }
        else
        {
            Checker(Path.Combine(_dir, "Locations", "Fine.png"), 2048, 1136, Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xE0, 0xE0, 0xE0));
            Checker(Path.Combine(_dir, "Locations", "FineBack.png"), 2048, 1136, Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xE0, 0xE0, 0xE0));
        }
        var pack = PackRepository.CreateEmpty("zoomdetail");
        var place = new PlaceDef { Key = "fine", InternalName = "Fine", BaseSprite = "Locations/Fine.png", SecondarySprite = "Locations/FineBack.png" };
        if (shaders)
        {
            // A mask on the art too, so it is drawn through the level's program, as a masked place's is - see-through,
            // so nothing of it moves (what moves is blended between the art's pixels in the game too).
            Checker(Path.Combine(_dir, "Locations", "Fine-mask.png"), 64, 64, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
            place.MaskSprite = "Locations/Fine-mask.png";
        }
        if (character)
        {
            // Held still (no jiggle, no noise): what is measured is how its pixels are read, not how they move.
            pack.Npcs.Add(new NpcDef
            {
                Key = "fine", DisplayName = "Fine", Sprite = "Npcs/fine.png", Mask = "Npcs/fine-mask.png", Shadow = { Enabled = false },
                Jiggle = new JiggleParams { Strength = 0f, NoiseStrength = 0f },
            });
            place.GameObjects.Add(new GameObjectDef
            {
                Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
                Npcs = { new NpcPlacementDef { Npc = "fine", Name = "Fine", StartActive = true } },
            });
        }
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
        var wasQuality = PreviewQualityManager.Current;
        var wasRenderer = PreviewRendererManager.Current;
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            var main = (MainWindow)window;
            try
            {
                vm.OpenPackFromPath(_dir);
                Settle(window);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                Settle(window);
                vm.SelectedPlace = vm.Places.Single();
                Settle(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                ((CheckBox)window.FindName("PlaceShaders")).IsChecked = shaders;
                Settle(window);
                main.EnterFullScreen(preview);
                Settle(window);
                var size = preview.PictureSize;
                preview.ZoomAt(new Point(size.Width / 2, size.Height / 2), 10);   // 1.15^10: about four times
                Settle(window);
                var results = new List<string>();
                foreach (var (name, quality, renderer) in new[] { ("look off", "Low", "Processor"), ("look, processor", "Ultra", "Processor"), ("look, card", "Ultra", "Card") })
                {
                    PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == quality));
                    PreviewRendererManager.Apply(PreviewRendererManager.All.First(r => r.Name == renderer));
                    preview.LookBloom = preview.LookFlares = preview.LookColour = preview.LookLights = name != "look off";
                    int from = preview.LookPasses;
                    if (name != "look off") WindowHarness.Until(() => preview.LookPasses >= from + 2, TimeSpan.FromSeconds(20));
                    Settle(window);
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                    Settle(window);
                    var shot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    shot.Render(window);
                    int w = shot.PixelWidth, h = shot.PixelHeight;
                    var px = new byte[w * h * 4];
                    shot.CopyPixels(px, w * 4, 0);
                    if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
                    {
                        Directory.CreateDirectory(shots);
                        var enc = new PngBitmapEncoder();
                        enc.Frames.Add(BitmapFrame.Create(new CroppedBitmap(shot, new Int32Rect(w / 2 - 400, h / 2 - 200, 800, 400))));
                        using var fs = File.Create(Path.Combine(shots, "zoom-" + what + (shaders ? "-shaders" : "") + "-" + name.Replace(", ", "-").Replace(' ', '-') + ".png"));
                        enc.Save(fs);
                    }
                    // Along the middle row of the screen: the lengths of runs of one colour, light or dark.
                    var runs = new List<int>();
                    int y = h / 2, run = 0; bool? lightBefore = null;
                    for (int x = w / 2 - 300; x < w / 2 + 300; x++)
                    {
                        int i = (y * w + x) * 4;
                        int lum = (px[i] + px[i + 1] + px[i + 2]) / 3;
                        bool? light = lum > 150 ? true : lum < 100 ? false : null;
                        if (light == lightBefore && light != null) run++;
                        else { if (run > 0 && lightBefore != null) runs.Add(run); run = light != null ? 1 : 0; lightBefore = light; }
                    }
                    var mid = runs.Skip(1).OrderBy(r => r).ToList();
                    double median = mid.Count == 0 ? 0 : mid[mid.Count / 2];
                    int grey = 0;
                    for (int x = w / 2 - 300; x < w / 2 + 300; x++)
                    {
                        int i = (y * w + x) * 4;
                        int lum = (px[i] + px[i + 1] + px[i + 2]) / 3;
                        if (lum >= 100 && lum <= 150) grey++;
                    }
                    // The look on (as every quality has it): the art's pixels told apart, light and dark in turn, little
                    // between them. With every look box unticked WPF draws a character itself, and that is still soft:
                    // printed, not held (see PLAN-PREVIEW-NEXT Z3).
                    if (name != "look off")
                    {
                        // Each art pixel a few screen pixels (5 or 6 at this zoom, by the screen's size): many short runs.
                        // Blurred there are none; drawn from a smaller picture, a few long ones (the moire of 40-60).
                        Assert.True(runs.Count >= 60 && median <= 12, $"{what}{(shaders ? " with shaders" : "")}, {name}: {runs.Count} runs of one colour along 600 pixels, their middle {median} long - the art's pixels are not told apart");
                        Assert.True(grey < 100, $"{what}{(shaders ? " with shaders" : "")}, {name}: {grey} of 600 pixels between light and dark - blended");
                    }
                    results.Add($"{what}{(shaders ? " with shaders" : "")}, {name}: {runs.Count} runs, median {median} screen pixels, {grey} of 600 grey; "
                                + "characters scaled " + string.Join(",", preview.CharacterScaling) + " pictures " + string.Join(",", preview.CharacterPictureSizes));
                }
                foreach (var r in results) _out.WriteLine(r);
                if (character && shaders)
                {
                    // Control: the measure on the picture the preview kept before (its fitted size, 241 of 1024, then
                    // enlarged with the view) - it must fail there.
                    var art = new byte[1024 * 1024 * 4];
                    for (int i = 0; i < 1024 * 1024; i++) { byte v = ((i % 1024 + i / 1024) & 1) == 0 ? (byte)0x20 : (byte)0xE0; art[i * 4] = art[i * 4 + 1] = art[i * 4 + 2] = v; art[i * 4 + 3] = 255; }
                    var full = BitmapSource.Create(1024, 1024, 96, 96, PixelFormats.Bgra32, null, art, 1024 * 4);
                    var small = new TransformedBitmap(full, new ScaleTransform(241.0 / 1024, 241.0 / 1024));
                    var enlarged = new TransformedBitmap(small, new ScaleTransform(3.5 * 1024 / 241, 3.5 * 1024 / 241));
                    int ew = enlarged.PixelWidth;
                    var row = new byte[ew * 4];
                    enlarged.CopyPixels(new Int32Rect(0, enlarged.PixelHeight / 2, ew, 1), row, ew * 4, 0);
                    int oldRuns = 0, oldGrey = 0; bool? before = null;
                    for (int x = ew / 2 - 300; x < ew / 2 + 300; x++)
                    {
                        int l = row[x * 4];
                        bool? light = l > 150 ? true : l < 100 ? false : null;
                        if (light == null) oldGrey++;
                        else if (light != before) oldRuns++;
                        before = light ?? before;
                    }
                    _out.WriteLine($"control: the picture as kept before - {oldRuns} runs, {oldGrey} of 600 between light and dark");
                    Assert.True(oldRuns < 100 || oldGrey >= 100, "control: the picture as the preview kept it before passes the measure");
                }
            }
            finally
            {
                main.LeaveFullScreen();
                PreviewQualityManager.Apply(wasQuality);
                PreviewRendererManager.Apply(wasRenderer);
                typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
            }
        });
    }
}
