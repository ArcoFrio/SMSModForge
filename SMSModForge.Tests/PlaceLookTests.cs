using System;
using System.IO;
using System.Linq;
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
/// The game's look over a place (1.9.1, the author: "I wanted to see them having
/// shader effects as well, all manually disabled through the checkboxes below
/// it"): the place preview draws the post-processing over its scene, each part
/// under its own tick box and within the preview quality. Measured from the
/// picture drawn: a dark place with a white square, the bloom lighting the dark
/// beside the square at Ultra, and not at Low (which draws no bloom) even
/// ticked; the level's lamps (1.9.2) one of the parts, drawn alone; nothing
/// drawn over the place with every part unticked (the control).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceLookTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placelook-" + Guid.NewGuid().ToString("N"));

    public PlaceLookTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void WriteSquare(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool square = Math.Abs(x - w / 2) < w / 12 && Math.Abs(y - h / 2) < w / 12;
                byte v = square ? (byte)255 : (byte)30;
                px[i] = v; px[i + 1] = v; px[i + 2] = v; px[i + 3] = 255;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    [Fact]
    public void ThePlacePreviewDrawsTheGamesLookPartByPart()
    {
        WriteSquare(Path.Combine(_dir, "Places", "front.png"), 2048, 1136);
        var pack = PackRepository.CreateEmpty("look.pack");
        pack.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave", BaseSprite = "Places/front.png" });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());

        var was = PreviewQualityManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.First();
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                var bloom = (CheckBox)window.FindName("PlaceLookBloom");
                var flares = (CheckBox)window.FindName("PlaceLookFlares");
                var colour = (CheckBox)window.FindName("PlaceLookColour");
                var lights = (CheckBox)window.FindName("PlaceLookLights");
                Assert.True(bloom.IsChecked == true && flares.IsChecked == true && colour.IsChecked == true && lights.IsChecked == true,
                            "the game's look is not on by default");

                // The look's middle row as drawn (the view's own pixels, 1.9.1), once a new pass has come in.
                int width = 0;
                byte[] Row()
                {
                    int before = place.LookPasses;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (place.LookPasses == before && clock.Elapsed < TimeSpan.FromSeconds(5)) WindowHarness.Wait(TimeSpan.FromMilliseconds(50));
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                    var bmp = (BitmapSource)((Image)typeof(PlacePreview).GetField("_lookImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(place)!).Source;
                    width = bmp.PixelWidth;
                    var px = new byte[width * 4];
                    bmp.CopyPixels(new Int32Rect(0, bmp.PixelHeight / 2, width, 1), px, width * 4, 0);
                    return Enumerable.Range(0, width).Select(x => px[x * 4 + 1]).ToArray();
                }

                PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
                var ultraRow = Row();
                Assert.True(place.LookShown, "the game's look is not drawn over the place at Ultra");
                bloom.IsChecked = false;
                var noBloomRow = Row();
                bloom.IsChecked = true;
                PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Low"));
                var lowRow = Row();
                // Beside the square: the first dark pixel right of the middle, without
                // bloom, and a few further into the dark.
                int edge = Enumerable.Range(width / 2, width * 2 / 5).First(x => noBloomRow[x] < 100) + 4;
                byte ultra = ultraRow[edge], noBloom = noBloomRow[edge], low = lowRow[edge];
                _out.WriteLine($"the square's edge at x {edge - 4} of {width}; the middle {noBloomRow[width / 2]}");
                _out.WriteLine($"beside the square: Ultra {ultra}, Ultra without bloom {noBloom}, Low (bloom ticked) {low}");
                Assert.True(ultra > noBloom + 3, $"the bloom does not light the dark beside the square ({ultra} against {noBloom} without it)");
                Assert.True(Math.Abs(low - noBloom) <= 1, $"Low draws a bloom ({low} against {noBloom} without one)");

                bloom.IsChecked = false; flares.IsChecked = false; colour.IsChecked = false;
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                Assert.True(place.LookShown, "the level's lamps alone are not drawn over the place");
                lights.IsChecked = false;
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                Assert.False(place.LookShown, "with every part unticked the look is still drawn over the place");
            });
        }
        finally { PreviewQualityManager.Apply(was); }
    }
}
