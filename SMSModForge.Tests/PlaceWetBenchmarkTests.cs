using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// What the wet drops cost in a real pack's place (after 1.10.0, the author: "Please make sure that doesn't eat up
/// performance, you can use SMSAndroids's bathroom place for benchmarking as it has some wet NPCs there").
/// <para/>
/// Run on a pack named by SMSMODFORGE_BENCH_PACK (a pack's folder) and SMSMODFORGE_BENCH_PLACE (a place's key), so
/// nobody's pack is written into the repository; nothing runs without them. The pack is copied to a folder of the
/// run's own first and opened from there: the author's files are only read. The place is previewed at about a 1080p
/// screen's size, every tick box on, Ultra, on the graphics card, the drops falling on a real clock - with its NPCs
/// wet as made, then with every NPC's wet off (in the copy, never saved), each still and with the mouse moving; and
/// the drops' own work a frame (working out every drop of every spray) timed apart.
/// </summary>
[Trait("Speed", "Slow")]
[Trait("Timing", "Alone")]
public sealed class PlaceWetBenchmarkTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-wetbench-" + Guid.NewGuid().ToString("N"));

    public PlaceWetBenchmarkTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from)) CopyFolder(d, Path.Combine(to, Path.GetFileName(d)));
    }

    [Fact]
    public void A_places_wet_drops_cost_next_to_nothing()
    {
        string? pack = Environment.GetEnvironmentVariable("SMSMODFORGE_BENCH_PACK");
        string? placeKey = Environment.GetEnvironmentVariable("SMSMODFORGE_BENCH_PLACE");
        if (string.IsNullOrEmpty(pack) || string.IsNullOrEmpty(placeKey) || !Directory.Exists(pack)) return;
        CopyFolder(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        var wasQuality = PreviewQualityManager.Current;
        var wasRenderer = PreviewRendererManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.First(p => p.Key == placeKey);
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(r => r.Name == "Card"));
                window.Width = 3600; window.Height = 2200;
                typeof(PlacePreview).GetField("_pictureScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(place, 2.0);
                place.InvalidateMeasure();
                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = true;
                ((CheckBox)window.FindName("PlaceShaders")).IsChecked = true;
                foreach (var n in new[] { "PlaceLookBloom", "PlaceLookFlares", "PlaceLookColour", "PlaceLookLights" })
                    ((CheckBox)window.FindName(n)).IsChecked = true;
                // The drops on a real clock, as on the author's screen (under tests they are held still).
                var clock = Stopwatch.StartNew();
                place.WetClockForTests = () => SMSModForge.Rendering.WetDrops.Steady + clock.Elapsed.TotalSeconds;
                WindowHarness.Pump();
                Assert.True(WindowHarness.Until(() => place.CardDrawsScene, TimeSpan.FromSeconds(60)), "the card does not draw the place (" + place.CardFailure + ")");
                var screen = new Rect(0, 0, 1920, 1080);

                (double Looks, double Interface, double Between) Measure(string name, bool moving)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(600));
                    place.LookTime.Clear();
                    int looks = place.LookPasses, frames = 0;
                    long last = 0, held = 0;
                    var t = Stopwatch.StartNew();
                    EventHandler onFrame = (_, _) =>
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (last != 0) held += now - last;
                        last = now;
                        frames++;
                        if (moving) place.FollowCursor(960 + 700 * Math.Sin(t.Elapsed.TotalSeconds * 2.1), 540 + 300 * Math.Cos(t.Elapsed.TotalSeconds * 1.7), screen);
                    };
                    CompositionTarget.Rendering += onFrame;
                    WindowHarness.Wait(TimeSpan.FromSeconds(4));
                    CompositionTarget.Rendering -= onFrame;
                    double s = t.Elapsed.TotalSeconds;
                    var lt = place.LookTime;
                    int passes = Math.Max(1, place.LookPasses - looks);
                    double ui = PlacePreview.LookTimes.Ms(lt.Capture + lt.Mask + lt.Compare + lt.Present) / passes;
                    double between = PlacePreview.LookTimes.Ms(held) / Math.Max(1, frames - 1);
                    _out.WriteLine($"{name,-40} {place.SprayCount} sprays: looks {(place.LookPasses - looks) / s,6:0.0}/s, the interface's thread {ui,4:0.00} ms a look,"
                                   + $" off it {PlacePreview.LookTimes.Ms(lt.Work) / passes,5:0.00} ms; between display frames {between,5:0.0} ms");
                    return ((place.LookPasses - looks) / s, ui, between);
                }

                int display = SMSModForge.Rendering.DisplayRefresh.RateOf(place);
                _out.WriteLine($"{placeKey}: the display {display} a second, the card {SMSModForge.Rendering.Gpu.GpuDevice.Shared?.Adapter}");
                var wetStill = Measure("wet as made, the mouse still", false);
                var wetMoving = Measure("wet as made, the mouse moving", true);
                // Nothing else moving: the NPCs' shaders off, the drops alone keep the look drawing.
                var shaders = (CheckBox)window.FindName("PlaceShaders");
                shaders.IsChecked = false;
                WindowHarness.Pump();
                var wetAlone = Measure("wet as made, shaders off, the mouse still", false);
                shaders.IsChecked = true;
                WindowHarness.Pump();

                // The drops' own work a frame: every drop of every spray worked out, as each look and WPF frame does.
                var collect = typeof(PlacePreview).GetMethod("CollectDrops", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var frame = new SMSModForge.Rendering.Gpu.PlaceFrame();
                var w = Stopwatch.StartNew();
                const int rounds = 2000;
                for (int i = 0; i < rounds; i++) { frame.Clear(); collect.Invoke(place, new object[] { frame }); }
                double us = w.Elapsed.TotalMilliseconds * 1000 / rounds;
                _out.WriteLine($"working out the drops: {us:0.0} microseconds a frame, {frame.DropCount} drops out now");

                // Every NPC's wet off, in the copy only.
                foreach (var n in vm.Npcs) n.WetEnabled = false;
                WindowHarness.Pump();
                Assert.Equal(0, place.SprayCount);
                var dryStill = Measure("wet off, the mouse still", false);
                var dryMoving = Measure("wet off, the mouse moving", true);
                shaders.IsChecked = false;
                WindowHarness.Pump();
                var dryAlone = Measure("wet off, shaders off, the mouse still", false);

                // The drops' own work: next to nothing. What they cost is the place drawn again while nothing else
                // moves - at sixty a second at most, the interface's thread held no longer a look than for anything else.
                Assert.True(us < 200, $"working out the drops takes {us:0} microseconds a frame");
                Assert.True(wetAlone.Looks <= 62, $"with only the drops moving the place is drawn {wetAlone.Looks:0.0} times a second, more than their sixty steps");
                // How many it reaches is the card's: shared with other work (something else holding nearly all of
                // its memory, 2026-11-27) the same run drew 36 to 60 - so not asserted, only shown.
                Assert.True(wetAlone.Interface <= 3.0, $"the drops hold the interface's thread {wetAlone.Interface:0.00} ms a look");
                Assert.True(dryAlone.Looks < 1, "control: with nothing moving the place is still drawn again");
            });
        }
        finally
        {
            PreviewQualityManager.Apply(wasQuality);
            PreviewRendererManager.Apply(wasRenderer);
        }
    }
}
