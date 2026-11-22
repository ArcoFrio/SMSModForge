using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// The place preview keeps up with the display with every tick box on at Ultra
/// (1.10.0, the author: "The Places preview is very laggy with all checkboxes
/// enabled on ultra, even though it's barely using my PC's resources at all.
/// When I stop moving my mouse around and start again, there's also a stutter
/// going on"). Measured on a place of a pack's usual size - the level's art and
/// its backdrop at 2048x1136 with a mask each, a dozen objects that drift with
/// the mouse, two NPCs, three lamps - in a view about a 1080p screen's size,
/// drawn on the graphics card:
/// <list type="bullet">
/// <item>the looks put on screen a second, paced as the author's main monitor
/// (165 a second) - it drew 28 in a view a sixth the size;</item>
/// <item>how long the interface's thread is held for each (it took the scene's
/// pictures itself, ten milliseconds and more a look);</item>
/// <item>a still place draws nothing, and the picture follows the mouse at once
/// when it moves again (it was looked at five times a second, the first
/// movement up to a fifth of a second late);</item>
/// <item>a step of a drag builds the scene once, in a few milliseconds (an
/// object's X and Y each said two things, each rebuilding the whole scene:
/// four rebuilds of 42 ms for every move of the mouse).</item>
/// </list>
/// Control: drawn the processor's way, the same measurements come out the other
/// way - the interface's thread held many times longer for each look.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
[Trait("Timing", "Alone")]  // times the preview's frames: run by a tester alone (run-tests.ps1)
public sealed class PlacePreviewPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placeperf-" + Guid.NewGuid().ToString("N"));

    public PlacePreviewPerformanceTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>A picture with something in it everywhere (bands and squares), so a shader's movement changes it:
    /// opaque, or a disc on nothing.</summary>
    private void Png(string rel, int w, int h, int seed, bool disc = false)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                int v = ((x / 16 + y / 16 + seed) & 1) * 40 + (x * 97 / w) + (y * 61 / h) + seed * 7 % 40;
                double dx = (x - w / 2.0) / (w / 2.0), dy = (y - h / 2.0) / (h / 2.0);
                bool inside = !disc || dx * dx + dy * dy < 0.9;
                px[i] = (byte)Math.Clamp(v + 20, 0, 255); px[i + 1] = (byte)Math.Clamp(v + seed * 13 % 60, 0, 255); px[i + 2] = (byte)Math.Clamp(v + 50, 0, 255);
                px[i + 3] = (byte)(inside ? 255 : 0);
                if (!inside) px[i] = px[i + 1] = px[i + 2] = 0;
            }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(Path.Combine(_dir, rel));
        enc.Save(fs);
    }

    private static T Field<T>(object o, string name)
        => (T)typeof(PlacePreview).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private void MakePack()
    {
        Png("Art/base.png", 2048, 1136, 1);
        Png("Art/back.png", 2048, 1136, 2);
        Png("Art/mask.png", 1024, 1024, 3);
        Png("Art/npc.png", 220, 420, 4, disc: true);
        Png("Art/npcmask.png", 256, 256, 5);
        for (int k = 0; k < 4; k++) Png($"Art/obj{k}.png", 300, 300, 6 + k, disc: true);
        var pack = PackRepository.CreateEmpty("placeperf.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Art/npc.png", Mask = "Art/npcmask.png" });
        var place = new PlaceDef
        {
            Key = "p", InternalName = "P", DisplayName = "P", BaseSprite = "Art/base.png", SecondarySprite = "Art/back.png",
            MaskSprite = "Art/mask.png", SecondaryMaskSprite = "Art/mask.png",
        };
        for (int k = 0; k < 12; k++)
            place.GameObjects.Add(new GameObjectDef
            {
                Name = "Obj" + k, Sprite = $"Art/obj{k % 4}.png", X = -8 + k * 1.4f, Y = -3 + (k % 3) * 2.5f, SortingOrder = k % 5,
                ParallaxDisabled = false, ParallaxStrength = 0.2f + 0.1f * (k % 4),
            });
        place.GameObjects.Add(new GameObjectDef { Name = "LampA", X = -4, Y = 2, Light = new LightDef { Shape = "round", OuterRadius = 4f, Strength = 0.8f, Glow = 0.4f, Color = "#FFD0A0" } });
        place.GameObjects.Add(new GameObjectDef { Name = "LampB", X = 5, Y = -1, Light = new LightDef { Shape = "round", OuterRadius = 3f, Strength = 0.6f, Color = "#A0C0FF" } });
        new PlaceViewModel(place);   // its NPCs node, its template light
        var npcs = place.GameObjects.Single(g => g.IsNpcRoot);
        npcs.Npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "GuardA", Body = new NpcTransform { X = -3, Y = -1 } });
        npcs.Npcs.Add(new NpcPlacementDef { Npc = "guard", Name = "GuardB", Body = new NpcTransform { X = 4, Y = -1 } });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
    }

    private sealed record Reading(string Name, double Frames, double Looks, double Shaders, double HeldMean, double HeldMax,
                                  double Capture, double Mask, double Compare, double Work, double Present, int Same, int Busy, double Tick)
    {
        /// <summary>The interface's thread for each look shown, ms: taking the scene and putting the picture up.</summary>
        public double Interface => Capture + Mask + Compare + Present;
    }

    [Fact]
    public void TheCardDrawsThePlaceAtTheDisplaysRateAndLeavesTheInterfaceAlone()
    {
        MakePack();
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        Assert.True(SMSModForge.Rendering.Gpu.GpuDevice.Shared != null, "no graphics card device: " + SMSModForge.Rendering.Gpu.GpuDevice.SharedFailure);
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
                vm.SelectedPlace = vm.Places.First();
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
                var viewport = Field<FrameworkElement>(place, "_viewport");
                // The cursor somewhere on a 1080p screen: what the preview is told each frame (PlacePreview.Parallax.cs).
                var screen = new Rect(0, 0, 1920, 1080);

                // A picture about a 1080p screen's size: its corner dragged out, the window large enough to hold it.
                window.Width = 3600; window.Height = 2200;
                typeof(PlacePreview).GetField("_pictureScale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(place, 2.0);
                place.InvalidateMeasure();
                WindowHarness.Pump();
                Assert.True(viewport.ActualWidth > 1700, $"the view is only {viewport.ActualWidth:0} across - not the size this is measured at");

                Reading Measure(string name, bool moving, double seconds)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
                    place.LookTime.Clear();
                    int looks = place.LookPasses, shaders = place.ShaderSteps, frames = 0;
                    long last = 0, heldSum = 0, heldMax = 0;
                    var clock = Stopwatch.StartNew();
                    EventHandler onFrame = (_, _) =>
                    {
                        long now = Stopwatch.GetTimestamp();
                        if (last != 0) { heldSum += now - last; heldMax = Math.Max(heldMax, now - last); }
                        last = now;
                        frames++;
                        if (moving)
                        {
                            // The mouse: what its move does to the scene (PlacePreview.FollowCursor).
                            double t = clock.Elapsed.TotalSeconds;
                            place.FollowCursor(960 + 700 * Math.Sin(t * 2.1), 540 + 300 * Math.Cos(t * 1.7), screen);
                        }
                    };
                    CompositionTarget.Rendering += onFrame;
                    WindowHarness.Wait(TimeSpan.FromSeconds(seconds));
                    CompositionTarget.Rendering -= onFrame;
                    double s = clock.Elapsed.TotalSeconds;
                    var lt = place.LookTime;
                    int passes = Math.Max(1, place.LookPasses - looks);
                    var r = new Reading(name, frames / s, (place.LookPasses - looks) / s, (place.ShaderSteps - shaders) / s,
                                        PlacePreview.LookTimes.Ms(heldSum) / Math.Max(1, frames - 1), PlacePreview.LookTimes.Ms(heldMax),
                                        PlacePreview.LookTimes.Ms(lt.Capture) / passes, PlacePreview.LookTimes.Ms(lt.Mask) / passes, PlacePreview.LookTimes.Ms(lt.Compare) / passes,
                                        PlacePreview.LookTimes.Ms(lt.Work) / passes, PlacePreview.LookTimes.Ms(lt.Present) / passes, lt.Same, lt.Busy,
                                        // Each time the scene is looked at, drawn or not: the interface's thread, ms.
                                        PlacePreview.LookTimes.Ms(lt.Capture + lt.Mask + lt.Compare) / Math.Max(1, lt.Ticks));
                    _out.WriteLine($"{r.Name,-44} looks {r.Looks,6:0.0}/s  shaders {r.Shaders,6:0.0}/s  the interface's thread {r.Interface,5:0.0} ms a look"
                                   + $" (taking the scene {r.Capture + r.Mask + r.Compare:0.0}, showing {r.Present:0.0}), off it {r.Work:0.0};"
                                   + $" between display frames {r.HeldMean:0.0} ms (most {r.HeldMax:0.0}); still {r.Same}, waited {r.Busy}");
                    return r;
                }

                var shadersBox = (CheckBox)window.FindName("PlaceShaders");
                var parallaxBox = (CheckBox)window.FindName("PlaceParallaxPreview");
                var boxes = new[] { "PlaceLookBloom", "PlaceLookFlares", "PlaceLookColour", "PlaceLookLights" }.Select(n => (CheckBox)window.FindName(n)).ToArray();
                parallaxBox.IsChecked = true;
                shadersBox.IsChecked = true;
                foreach (var b in boxes) b.IsChecked = true;

                // ── On the card ──
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(r => r.Name == "Card"));
                WindowHarness.Pump();
                Assert.True(WindowHarness.Until(() => place.CardDrawsScene, TimeSpan.FromSeconds(60)), "the card does not draw the place's scene (" + place.CardFailure + ")");
                _out.WriteLine($"the view {viewport.ActualWidth:0} x {viewport.ActualHeight:0}; the card: {SMSModForge.Rendering.Gpu.GpuDevice.Shared!.Adapter}");
                int display = SMSModForge.Rendering.DisplayRefresh.RateOf(place);
                var own = Measure($"every box, the mouse moving, this display ({display})", true, 3);
                // As the author's main monitor: the preview paced to 165 a second, whatever this run's display is.
                SMSModForge.Rendering.DisplayRefresh.RateForTests = 165;
                Reading fast;
                try { fast = Measure("... paced as a 165 Hz display", true, 3); }
                finally { SMSModForge.Rendering.DisplayRefresh.RateForTests = null; }
                Assert.True(own.Looks >= display * 0.85, $"on the card the place drew {own.Looks:0.0} looks a second, the display {display}");
                Assert.True(fast.Looks >= 165 * 0.7, $"paced as a 165 Hz display the place drew only {fast.Looks:0.0} looks a second");
                Assert.True(own.Shaders >= own.Looks * 0.9, $"the shaders were drawn {own.Shaders:0.0} times a second, the looks {own.Looks:0.0}");
                Assert.True(fast.Interface <= 3.0, $"the interface's thread is held {fast.Interface:0.0} ms for each look");

                // A still place draws nothing - and the picture follows the mouse at once when it moves again.
                // (The shaders off: with them the place is never still.)
                shadersBox.IsChecked = false;
                WindowHarness.Pump();
                var still = Measure("the look alone, the mouse still", false, 2);
                Assert.True(still.Looks < 1 && still.Same > 20, $"a still place drew {still.Looks:0.0} looks a second (found still {still.Same} times)");
                Assert.True(still.Tick < 0.5, $"looking at a still place holds the interface's thread {still.Tick:0.00} ms each time - it is still photographed");
                var waits = new List<double>();
                for (int k = 0; k < 6; k++)
                {
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(600));
                    int shown = place.LookPasses;
                    var clock = Stopwatch.StartNew();
                    place.FollowCursor(300 + 250 * k, 400 + 60 * k, screen);
                    // Turned as fast as the queue empties: a timer's wait would be most of what is measured.
                    while (place.LookPasses == shown && clock.Elapsed < TimeSpan.FromSeconds(2)) WindowHarness.Pump();
                    waits.Add(clock.Elapsed.TotalMilliseconds);
                }
                _out.WriteLine($"after a rest, the picture follows the mouse in {string.Join(", ", waits.Select(w => w.ToString("0")))} ms");
                waits.Sort();
                Assert.True(waits[waits.Count / 2] <= 60, $"after a rest the picture follows the mouse only after {waits[waits.Count / 2]:0} ms");
                shadersBox.IsChecked = true;
                WindowHarness.Pump();

                // A drag: each step moves the object both ways (X and Y), and builds the scene once.
                foreach (string name in new[] { "Obj3", "LampA" })
                {
                    var node = vm.SelectedPlace!.GameObjects.Single(g => g.Name == name);
                    WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                    place.RebuildTime.Clear();
                    const int Steps = 40;
                    var drag = Stopwatch.StartNew();
                    for (int k = 0; k < Steps; k++)
                    {
                        node.X += 0.02f;
                        node.Y += 0.01f;
                        WindowHarness.Pump();
                    }
                    double step = drag.Elapsed.TotalMilliseconds / Steps;
                    var rt = place.RebuildTime;
                    double each = PlacePreview.LookTimes.Ms(rt.Part.Sum()) / Math.Max(1, rt.Count);
                    _out.WriteLine($"dragging {name}: {rt.Count} rebuilds over {Steps} steps, {each:0.0} ms each ("
                                   + string.Join(", ", PlacePreview.RebuildTimes.Names.Select((n, i) => $"{n} {PlacePreview.LookTimes.Ms(rt.Part[i]) / Math.Max(1, rt.Count):0.0}"))
                                   + $"); {step:0.0} ms a step with the queue emptied");
                    Assert.True(rt.Count == Steps, $"dragging {name}: {rt.Count} rebuilds for {Steps} steps");
                    Assert.True(each <= 20, $"dragging {name}: a rebuild takes {each:0.0} ms");
                }

                // With the look off again, the shaders' pictures (which stayed on the card) are on screen and moving.
                foreach (var b in boxes) b.IsChecked = false;
                WindowHarness.Pump();
                Assert.False(place.CardDrawsScene, "with every part of the look unticked the card still draws the scene");
                var layer = Field<Image>(place, "_baseImage");
                byte[] Layer()
                {
                    var bmp = (BitmapSource)layer.Source;
                    var px = new byte[bmp.PixelWidth * 4];
                    bmp.CopyPixels(new Int32Rect(0, bmp.PixelHeight / 2, bmp.PixelWidth, 1), px, bmp.PixelWidth * 4, 0);
                    return px;
                }
                Assert.True(layer.Source is WriteableBitmap, "the level's layer is not the shader's picture");
                var one = Layer();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
                var two = Layer();
                Assert.True(one.Count(v => v != 0) > one.Length / 2, "with the look off the level's moving picture is empty");
                Assert.False(one.AsSpan().SequenceEqual(two), "with the look off the level's picture does not move");
                foreach (var b in boxes) b.IsChecked = true;
                WindowHarness.Pump();

                // ── Control: the processor's way ──
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(r => r.Name == "Processor"));
                WindowHarness.Pump();
                var slow = Measure("control, the processor's way: every box, moving", true, 3);
                Assert.False(place.CardDrawsScene, "control: the card still draws the scene with the processor chosen");
                Assert.True(slow.Interface > fast.Interface * 3 && slow.Interface > 4 && slow.Tick > still.Tick * 20,
                            $"control: the processor's way holds the interface's thread {slow.Interface:0.0} ms a look, the card's {fast.Interface:0.0} - the measurement tells nothing apart");
                Assert.True(slow.Looks < own.Looks * 0.8, $"control: the processor's way drew {slow.Looks:0.0} looks a second, the card's {own.Looks:0.0}");
            });
        }
        finally
        {
            SMSModForge.Rendering.DisplayRefresh.RateForTests = null;
            PreviewQualityManager.Apply(wasQuality);
            PreviewRendererManager.Apply(wasRenderer);
        }
    }
}
