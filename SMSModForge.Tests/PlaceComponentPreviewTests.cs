using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What components do, played in the place preview (after 1.10.0, the author: "for the components that show something
/// visually, would we be able to see what they do in real time? Including live preview when changing parameters").
/// One place with one object per component, read off the drawn elements at chosen moments: a fade in rising to its
/// target after its delay, a fade out falling and its object switching off, a blink low then high, one child at a
/// time on under a RandomChildActivator and a CinemaSpriteChanger (the pick changing), a ConcertLightSwing turning
/// from its smallest angle to its largest, a CarInteriorShake moving only once told to start, a RandomSpriteBlender's
/// children fading about; and a setting typed in the component's row shown at once. The moments these are held to
/// are the game's (InGameSuiteTests.S25).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceComponentPreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-components-" + Guid.NewGuid().ToString("N"));

    public PlaceComponentPreviewTests(ITestOutputHelper o)
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

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    private static GameObjectDef Thing(string name, float x, float y, params ComponentDef[] components)
    {
        var g = new GameObjectDef { Name = name, Sprite = "Locations/Thing.png", X = x, Y = y, SortingOrder = 5 };
        g.Components.AddRange(components);
        return g;
    }

    private static ComponentDef Game(string type, params (string Key, object Value)[] values)
    {
        var c = new ComponentDef { Type = type };
        foreach (var (k, v) in values) c.Params[k] = JToken.FromObject(v);
        return c;
    }

    [Fact]
    public void Components_play_in_the_place_preview_and_follow_their_settings_as_typed()
    {
        Png(Path.Combine(_dir, "Locations", "Room.png"), 64, 36, Color.FromRgb(0x30, 0x30, 0x38));
        Png(Path.Combine(_dir, "Locations", "Thing.png"), 50, 50, Color.FromRgb(0xC0, 0x80, 0x40));
        var pack = PackRepository.CreateEmpty("components");
        var place = new PlaceDef { Key = "room", InternalName = "Room", BaseSprite = "Locations/Room.png" };
        place.GameObjects.Add(Thing("FadeIn", -6, 3, new ComponentDef { Type = PackComponentType.FadeInSprite, StartDelay = 0.5f, FadeDuration = 1f, TargetAlpha = 0.8f }));
        place.GameObjects.Add(Thing("FadeOut", -4, 3, new ComponentDef { Type = PackComponentType.FadeOutSprite, StartDelay = 0f, Duration = 1f, DeactivateOnComplete = true }));
        place.GameObjects[^1].Children.Add(Thing("FadeOutChild", 0, 1));
        place.GameObjects.Add(Thing("Blink", -2, 3, new ComponentDef { Type = PackComponentType.BlinkingSprite, BlinkInterval = 0.5f, MinAlpha = 0.2f, MaxAlpha = 0.9f }));
        var picker = Thing("Picker", 0, 3, new ComponentDef { Type = PackComponentType.RandomChildActivator });
        for (int i = 0; i < 3; i++) picker.Children.Add(Thing("Pick" + i, i, 0));
        place.GameObjects.Add(picker);
        var cinema = Thing("Cinema", 4, 3, Game("CinemaSpriteChanger", ("activationInterval", 0.5)));
        for (int i = 0; i < 3; i++) cinema.Children.Add(Thing("Frame" + i, i, 0));
        place.GameObjects.Add(cinema);
        place.GameObjects.Add(Thing("Swing", -6, -2, Game("ConcertLightSwing", ("minAngle", -30), ("maxAngle", 30), ("swingSpeed", 0.5), ("useEasing", false),
                                                         ("speedVariation", 0), ("randomizeStartPhase", false))));
        place.GameObjects.Add(Thing("Shake", -2, -2, Game("CarInteriorShake", ("shakeOnStart", true), ("shakeIntensity", 0.5), ("shakeSpeed", 10))));
        place.GameObjects.Add(Thing("NoShake", 0, -2, Game("CarInteriorShake", ("shakeIntensity", 0.5), ("shakeSpeed", 10))));
        var blender = Thing("Blender", 3, -2, Game("RandomSpriteBlender", ("blendDuration", 0.3), ("minDelay", 0.3), ("maxDelay", 0.5)));
        for (int i = 0; i < 2; i++) blender.Children.Add(Thing("Blended" + i, i, 0));
        place.GameObjects.Add(blender);
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
                Settle(window);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                Settle(window);
                vm.SelectedPlace = vm.Places.Single();
                Settle(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                double clock = 0;
                preview.AnimClockForTests = () => clock;
                Image I(string name) => preview.ObjectImageFor(name)!;
                void At(double t) { clock = t; preview.AnimateAtForTests(t); Settle(window); }
                double Shown(string name) => I(name).Visibility == Visibility.Visible ? I(name).Opacity : 0;
                // An object's drawn middle and the turn of its x axis on the canvas.
                (Point Mid, double Angle) Drawn(string name)
                {
                    var img = I(name);
                    var a = img.TranslatePoint(new Point(0, 0), preview.SceneCanvas);
                    var b = img.TranslatePoint(new Point(img.Width, 0), preview.SceneCanvas);
                    var mid = img.TranslatePoint(new Point(img.Width / 2, img.Height / 2), preview.SceneCanvas);
                    return (mid, Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
                }
                _out.WriteLine($"{preview.AnimatedElements} elements played");

                // Fade in: nothing until its delay, half its target half way, its target after.
                At(0.25); double fi0 = Shown("FadeIn");
                At(1.0); double fi1 = Shown("FadeIn");
                At(2.0); double fi2 = Shown("FadeIn");
                _out.WriteLine($"fade in: {fi0:0.##} {fi1:0.##} {fi2:0.##}");
                Assert.Equal(0, fi0, 2); Assert.Equal(0.4, fi1, 2); Assert.Equal(0.8, fi2, 2);
                // Fade out: half at half its time, then switched off - its child with it.
                At(0.5); double fo0 = Shown("FadeOut"); bool childOn = Shown("FadeOutChild") > 0.9;
                At(1.2); double fo1 = Shown("FadeOut"), child1 = Shown("FadeOutChild");
                _out.WriteLine($"fade out: {fo0:0.##} then {fo1:0.##}, its child {child1:0.##}");
                Assert.Equal(0.5, fo0, 2); Assert.True(childOn, "the fading object's child is off before the fade ends");
                Assert.Equal(0, fo1, 2);
                Assert.True(child1 < 0.5, "switched off, the fading object's child is still drawn as on");
                // Blink: low first, high after an interval.
                At(0.25); double b0 = Shown("Blink"); At(0.75); double b1 = Shown("Blink");
                Assert.Equal(0.2, b0, 2); Assert.Equal(0.9, b1, 2);
                // One child at a time, and the pick changing.
                foreach (var (parent, prefix, step) in new[] { ("Picker", "Pick", 3.0), ("Cinema", "Frame", 0.5) })
                {
                    var picks = new HashSet<int>();
                    for (int k = 0; k < 12; k++)
                    {
                        At(k * step + 0.01);
                        var on = Enumerable.Range(0, 3).Where(i => Shown(prefix + i) > 0.9).ToList();
                        Assert.True(on.Count == 1, $"{parent}: {on.Count} children fully on at {k * step:0.##} s");
                        picks.Add(on[0]);
                    }
                    _out.WriteLine($"{parent}: picked {string.Join(",", picks)} over twelve turns");
                    Assert.True(picks.Count >= 2, $"{parent}: always the same child");
                }
                // The swing: its smallest angle at the start, its largest a half swing later (speed 0.5: a second).
                At(0); double s0 = Drawn("Swing").Angle; At(1); double s1 = Drawn("Swing").Angle;
                _out.WriteLine($"swing: {s0:0.#} then {s1:0.#} degrees on the canvas");
                Assert.True(Math.Abs(Math.Abs(s1 - s0) - 60) < 1, $"the swing turns {Math.Abs(s1 - s0):0.#} degrees from its smallest angle to its largest, not 60");
                // The shake: moving once started, within its intensity; control: not started, still.
                var shakes = Enumerable.Range(0, 20).Select(k => { At(k * 0.05); return Drawn("Shake").Mid; }).ToList();
                var stills = Enumerable.Range(0, 20).Select(k => { At(k * 0.05); return Drawn("NoShake").Mid; }).ToList();
                double spread = shakes.Max(p => p.X) - shakes.Min(p => p.X), stillSpread = stills.Max(p => p.X) - stills.Min(p => p.X);
                _out.WriteLine($"shake: across {spread:0.#} canvas pixels; not started {stillSpread:0.#}");
                Assert.True(spread > 5, "the shake does not move");
                Assert.True(stillSpread < 0.01, "control: a shake not started moves too");
                // The blender: its children fade about.
                var blended = Enumerable.Range(0, 20).Select(k => { At(k * 0.2); return Shown("Blended0"); }).ToList();
                _out.WriteLine("blender's child: " + string.Join(" ", blended.Select(v => v.ToString("0.##"))));
                Assert.True(blended.Max() - blended.Min() > 0.3, "the blender's child does not fade about");

                // Live: the fade in's target typed in its row shows at once.
                var row = vm.SelectedPlace!.GameObjects.Single(g => g.Name == "FadeIn").Components.Single();
                row.TargetAlpha = 0.3f;
                Settle(window);
                At(2.0);
                double typed = Shown("FadeIn");
                _out.WriteLine($"the fade in's target typed as 0.3: drawn {typed:0.##}");
                Assert.Equal(0.3, typed, 2);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
