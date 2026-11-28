using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A pack's lamps drift with the place as the cursor moves (after 1.10.0, the author: "Would it be too performance
/// costly to add an option to have the light and darkness sources move together with parallax? If not, I'd like you
/// to add the checkboxes for either moving with background, foreground, or manually set it. Moving with background
/// should default to on."). Measured on what is drawn, with the cursor handed to the method the preview hands it to
/// each frame: how far the place's back and front pictures have drifted, and how far each light the look draws (and
/// its sun) has - the look's own lights, not the arithmetic that places them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceLightDriftTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-lightdrift-" + Guid.NewGuid().ToString("N"));

    public PlaceLightDriftTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, byte grey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        int w = 2048, h = 1136;
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = grey; px[i + 1] = grey; px[i + 2] = grey; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>A place whose back picture drifts well apart from its front, with a red lamp made before lamps could drift.</summary>
    private void WritePack()
    {
        Png(Path.Combine(_dir, "Places", "front.png"), 90);
        Png(Path.Combine(_dir, "Places", "back.png"), 60);
        var pack = PackRepository.CreateEmpty("lightdrift.pack");
        var hall = new PlaceDef
        {
            Key = "hall", InternalName = "Hall", DisplayName = "Hall",
            BaseSprite = "Places/front.png", SecondarySprite = "Places/back.png",
            ParallaxStrength = 0.75f, ParallaxSecondaryStrength = 1.2f,
        };
        hall.GameObjects.Add(new GameObjectDef
        {
            Name = "Old lamp", X = -3f, Y = 1f,
            Light = new LightDef { Color = "#FF0000", Strength = 0.8f, OuterRadius = 2f },
        });
        pack.Places.Add(hall);
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static double DriftedX(UIElement e) => (e.RenderTransform as MatrixTransform)?.Matrix.OffsetX ?? 0;

    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    /// <summary>Where the look draws the place's red lamp and its white one now: canvas X of each.</summary>
    private static (double Red, double White) LampsX(PlacePreview preview)
    {
        var lights = preview.PlaceLightsNow?.Lights ?? throw new Xunit.Sdk.XunitException("the look draws no lights");
        double ToCanvas(float sx) => sx * 1920.0 / Rendering.PlaceLights.ScreenW;
        var red = lights.Single(l => l.R > l.G + 0.1f);
        var white = lights.Single(l => !(l.R > l.G + 0.1f));
        return (ToCanvas(red.X), ToCanvas(white.X));
    }

    [Fact]
    public void ANewLampDriftsWithTheBackPicture_OneMadeBeforeWithTheFront_AndOwnByItsStrength()
    {
        WritePack();
        bool was = EditorPrefs.PlacePreviewParallax;
        EditorPrefs.PlacePreviewParallax = false;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.Single();
                WindowHarness.Pump();
                var place = vm.SelectedPlace!;
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                // The template level's own light would be a second white one: off, to tell the two lamps apart.
                place.GameObjects.First(g => g.Name == TemplateLight.ObjectName).RemoveLight();
                WindowHarness.Pump();

                // "+ Light", as clicked.
                var add = Descendants(Field<StackPanel>(preview, "_objectMenuList")).OfType<Button>().Single(b => b.Content is TextBlock t && t.Text == Loc.T("preview.light.add"));
                Click(add);
                WindowHarness.Pump();
                var lamp = place.GameObjects.Single(g => g.Name == "Light");
                var old = place.GameObjects.Single(g => g.Name == "Old lamp");
                Assert.Equal(GameObjectViewModel.DriftBack, lamp.Drift);
                Assert.Equal(GameObjectViewModel.DriftFront, old.Drift);
                var back = preview.LightDrift.Back;
                var front = preview.LightDrift.Front;
                var own = preview.LightDrift.Own;
                Assert.True(back.IsVisible, "the new lamp's Moves with is not beside the picture");
                Assert.True(ReferenceEquals(back.Background, front.Background) == false, "Back is not shown as the one chosen");

                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = true;
                WindowHarness.Pump();
                preview.FollowCursor(960, 540, Screen);    // the middle: as made
                WindowHarness.Pump();
                var (redAt, whiteAt) = LampsX(preview);

                // The cursor at the screen's left edge: the pictures drift, and the lamps with them.
                preview.FollowCursor(0, 540, Screen);
                WindowHarness.Pump();
                double backMoved = DriftedX(Field<Image>(preview, "_secondaryImage"));
                double frontMoved = DriftedX(Field<Image>(preview, "_baseImage"));
                var (redNow, whiteNow) = LampsX(preview);
                _out.WriteLine($"the back picture drifted {backMoved:F1} canvas px, the front {frontMoved:F1}; the new lamp {whiteNow - whiteAt:F1}, the old {redNow - redAt:F1}");
                Assert.True(Math.Abs(backMoved - frontMoved) > 10, "control: the back and front drift alike here - the check cannot tell them apart");
                Assert.Equal(backMoved, whiteNow - whiteAt, 1);
                Assert.Equal(frontMoved, redNow - redAt, 1);

                // What it costs (the author asked whether it would be "too performance costly"): 60 frames of the
                // cursor moving, the lights read as the look reads them each time it is drawn - at most once a frame,
                // so this is the most it can cost. Timed and written down, not held to a number: a timing beside
                // eleven other testers measures them.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 60; i++) { preview.FollowCursor(i * 32, 540, Screen); _ = preview.PlaceLightsNow; }
                clock.Stop();
                _out.WriteLine($"60 frames of drifting, with the lights worked out again for each: {clock.Elapsed.TotalMilliseconds / 60:F2} ms a frame");
                preview.FollowCursor(0, 540, Screen);
                WindowHarness.Pump();

                // Its sun goes with it.
                var sun = Field<Canvas>(preview, "_lightLayer").Children.OfType<FrameworkElement>().Single(e => e.ToolTip as string == lamp.Display);
                Assert.Equal(backMoved, DriftedX(sun), 1);

                // Own: chosen, it moves nothing until its strength is changed; at 0 it is the front's.
                Assert.True(preview.Pick(lamp));
                WindowHarness.Pump();
                Click(own);
                WindowHarness.Pump();
                Assert.Equal(GameObjectViewModel.DriftOwn, lamp.Drift);
                Assert.True(preview.LightDrift.OwnFields.IsVisible, "Own shows no strength to set");
                Assert.Equal(backMoved, LampsX(preview).White - whiteAt, 1);
                lamp.ParallaxStrength = 0f;
                WindowHarness.Pump();
                Assert.Equal(frontMoved, LampsX(preview).White - whiteAt, 1);
                lamp.ParallaxStrength = 0.4f;
                WindowHarness.Pump();
                double ownMoved = LampsX(preview).White - whiteAt;
                _out.WriteLine($"at its own 0.4 the new lamp drifted {ownMoved:F1}");
                Assert.True(Math.Abs(ownMoved - frontMoved) > 5 && Math.Abs(ownMoved - backMoved) > 5, "its own strength did not move it its own way");

                // Front, as clicked: the front's drift.
                Click(front);
                WindowHarness.Pump();
                Assert.Equal(frontMoved, LampsX(preview).White - whiteAt, 1);
                Click(back);
                WindowHarness.Pump();
                Assert.Equal(backMoved, LampsX(preview).White - whiteAt, 1);

                // Put back when the box is unticked: where it was made.
                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = false;
                WindowHarness.Pump();
                Assert.Equal(whiteAt, LampsX(preview).White, 1);

                // What the game is given, through the real save: the lamp drifts, its strength left to the back picture.
                string savedTo = Path.Combine(_dir, "saved");
                PackRepository.Save(vm.Pack, savedTo);
                var saved = JObject.Parse(File.ReadAllText(Path.Combine(savedTo, PackRepository.ManifestFileName)));
                var savedLamp = saved.SelectTokens("$..gameObjects[*]").OfType<JObject>().Single(o => (string?)o["name"] == "Light");
                var savedOld = saved.SelectTokens("$..gameObjects[*]").OfType<JObject>().Single(o => (string?)o["name"] == "Old lamp");
                _out.WriteLine($"saved: {savedLamp.ToString(Newtonsoft.Json.Formatting.None)}");
                Assert.Equal(false, (bool?)savedLamp["parallaxDisabled"]);
                Assert.Null(savedLamp["parallaxStrength"]);
                Assert.NotEqual(false, (bool?)savedOld["parallaxDisabled"]);
            });
        }
        finally { EditorPrefs.PlacePreviewParallax = was; }
    }

    [Fact]
    public void OnAGameLevelWithoutABackPicture_ANewLampDriftsWithTheFront_AndBackCannotBeChosen()
    {
        if (!VanillaLevelCatalog.IsAvailable) return;
        var pack = PackRepository.CreateEmpty("lightdrift.levels");
        pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = "vanilla:26_Downtown" });   // no back picture
        pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = "vanilla:14_Beach" });     // a back picture
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("ExtPreview");
            foreach (var (level, hasBack) in new[] { (0, false), (1, true) })
            {
                var ext = vm.VanillaExtensions[level];
                vm.SelectedVanillaExtension = ext;
                WindowHarness.Pump();
                Assert.Equal(hasBack, ext.PreviewHasBackLayer);
                preview.AddLightHere();
                WindowHarness.Pump();
                var lamp = ext.GameObjects.Last(g => g.Name.StartsWith("Light", StringComparison.Ordinal) && g.HasLight && !g.Model.KeepsWhatItFinds);
                var back = preview.LightDrift.Back;
                _out.WriteLine($"{ext.Source}: the new lamp drifts with the {lamp.Drift}; Back can be chosen: {back.IsEnabled}");
                Assert.Equal(hasBack ? GameObjectViewModel.DriftBack : GameObjectViewModel.DriftFront, lamp.Drift);
                Assert.True(back.IsVisible);
                Assert.Equal(hasBack, back.IsEnabled);
            }
        });
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
