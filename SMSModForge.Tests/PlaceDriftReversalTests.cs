using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;
using Path = System.IO.Path;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// An own drift's Reversed says how it drifts in the game (after 1.10.0, FROM-PACK-TESTING #21). In a place whose
/// back picture is reversed - root 0.5925, the back reversed, as the Androids pack's places are - an object of the
/// pack's drifting by a strength of its own drifts reversed in the game unless it says otherwise: it is cloned from the
/// back picture, or given a copy of its effect, and an unset reversal is left as that has it. The tick showed it
/// unticked, unticking it was the same as unset, and a "false" written by hand was dropped by the next save. Measured on
/// what the preview draws, with the cursor handed to the method the preview hands it to each frame, and through the
/// real save.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceDriftReversalTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-driftrev-" + Guid.NewGuid().ToString("N"));

    public PlaceDriftReversalTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, int w, int h, byte grey)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = grey; px[i + 1] = grey; px[i + 2] = grey; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private const string Unset = "Unset lamp", HandWritten = "Hand-written lamp", Untouched = "Untouched lamp", Guard = "Guard";

    /// <summary>The Androids pack's drift: a front at 0.5925 and a back picture at 0.2 going the other way.</summary>
    private static PlaceDef Hall() => new()
    {
        Key = "hall", InternalName = "Hall", DisplayName = "Hall",
        BaseSprite = "Places/front.png", SecondarySprite = "Places/back.png",
        ParallaxStrength = 0.5925f, ParallaxSecondaryStrength = 0.2f, ParallaxSecondaryReversed = true,
    };

    private string Manifest => Path.Combine(_dir, PackRepository.ManifestFileName);

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    /// <summary>The tick clicked: the method a mouse's click on it calls, which toggles it and raises its Click.</summary>
    private static void Click(CheckBox tick)
        => typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                             .Invoke(tick, null);

    private static CheckBox ReversedTick(PlacePreview.DriftChoice d) => d.OwnFields.Children.OfType<CheckBox>().Single();

    private static double OffsetX(UIElement e) => (e.RenderTransform as MatrixTransform)?.Matrix.OffsetX ?? 0;

    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    /// <summary>Canvas X of the lamp the look draws in red, green or blue.</summary>
    private static double LampX(PlacePreview preview, int channel)
    {
        var lights = preview.PlaceLightsNow?.Lights ?? throw new Xunit.Sdk.XunitException("the look draws no lights");
        var lamp = lights.Single(l => channel switch
        {
            0 => l.R > l.G + 0.1f && l.R > l.B + 0.1f,
            1 => l.G > l.R + 0.1f && l.G > l.B + 0.1f,
            _ => l.B > l.R + 0.1f && l.B > l.G + 0.1f,
        });
        return lamp.X * 1920.0 / Rendering.PlaceLights.ScreenW;
    }

    /// <summary>How far each lamp drifts with the cursor at the screen's left edge against the middle, and the
    /// place's front and back pictures.</summary>
    private static (double Red, double Green, double Blue, double Front, double Back) Drifts(PlacePreview preview)
    {
        preview.FollowCursor(960, 540, Screen);
        WindowHarness.Pump();
        var at = (R: LampX(preview, 0), G: LampX(preview, 1), B: LampX(preview, 2));
        preview.FollowCursor(0, 540, Screen);
        WindowHarness.Pump();
        return (LampX(preview, 0) - at.R, LampX(preview, 1) - at.G, LampX(preview, 2) - at.B,
                OffsetX(Field<Image>(preview, "_baseImage")), OffsetX(Field<Image>(preview, "_secondaryImage")));
    }

    private void WriteLampPack()
    {
        Png(Path.Combine(_dir, "Places", "front.png"), 2048, 1136, 90);
        Png(Path.Combine(_dir, "Places", "back.png"), 2048, 1136, 60);
        var pack = PackRepository.CreateEmpty("driftrev.pack");
        var hall = Hall();
        GameObjectDef Lamp(string name, float x, string colour) => new()
        {
            Name = name, X = x, ParallaxDisabled = false, ParallaxStrength = 0.55f,
            Light = new LightDef { Color = colour, Strength = 0.8f, OuterRadius = 1.5f },
        };
        hall.GameObjects.Add(Lamp(Unset, -4f, "#FF0000"));
        hall.GameObjects.Add(Lamp(HandWritten, 0f, "#00FF00"));
        hall.GameObjects.Add(Lamp(Untouched, 4f, "#0000FF"));
        pack.Places.Add(hall);
        PackRepository.Save(pack, _dir);
        // "Not reversed" written into the pack by hand, as the Androids pack's places were fixed.
        var json = JObject.Parse(File.ReadAllText(Manifest));
        json.SelectTokens("$..gameObjects[*]").OfType<JObject>().Single(o => (string?)o["name"] == HandWritten)["parallaxReversed"] = false;
        File.WriteAllText(Manifest, json.ToString());
    }

    [Fact]
    public void InAPlaceWhoseBackIsReversed_AnOwnDriftsTickShowsTheGamesReversal_UntickedItDriftsForward_AndTheSaveKeepsIt()
    {
        WriteLampPack();
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
                RealKeys.Watch(window);   // so a press says whether the key itself was taken
                // The template level's own light would be a fourth: off, to tell the lamps apart.
                place.GameObjects.First(g => g.Name == TemplateLight.ObjectName).RemoveLight();
                WindowHarness.Pump();
                GameObjectViewModel Row(string name) => place.GameObjects.Single(g => g.Name == name);
                var tick = ReversedTick(preview.LightDrift);
                bool? TickOf(string name)
                {
                    Assert.True(preview.Pick(Row(name)));
                    WindowHarness.Pump();
                    Assert.True(tick.IsVisible, name + "'s Reversed is not shown");
                    return tick.IsChecked;
                }

                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = true;
                WindowHarness.Pump();
                var d = Drifts(preview);
                _out.WriteLine($"front {d.Front:F1}, back {d.Back:F1}; unset {d.Red:F1}, hand-written false {d.Green:F1}, untouched {d.Blue:F1}");
                // As the game drifts them: unset, the back picture's reversal - its own 0.55 taken off the front's
                // drift; "false", not reversed - added to it.
                Assert.True(d.Red < d.Front - 10, $"an unset reversal does not drift reversed here ({d.Red:F1} against the front's {d.Front:F1})");
                Assert.True(d.Green > d.Front + 10, $"a \"false\" does not drift forward ({d.Green:F1} against the front's {d.Front:F1})");
                Assert.Equal(d.Red, d.Blue, 1);
                // The tick says so: an unset one shows the back picture's, the hand-written one its own.
                Assert.Equal(true, TickOf(Unset));
                Assert.Null(Row(Unset).Model.ParallaxReversed);   // showing it is not setting it
                Assert.Equal(false, TickOf(HandWritten));

                // Unticked, as clicked: it drifts forward, the way the hand-written one does.
                Assert.Equal(true, TickOf(Unset));
                Click(tick);
                WindowHarness.Pump();
                Assert.Equal(false, tick.IsChecked);
                Assert.Equal(false, Row(Unset).Model.ParallaxReversed);
                var unticked = Drifts(preview);
                Assert.Equal(d.Green, unticked.Red, 1);
                Assert.Equal(d.Blue, unticked.Blue, 1);   // control: the untouched one did not move with it
                // Ctrl+Z takes it back to unset: the back picture's again.
                vm.Undo.Checkpoint();
                Assert.True(RealKeys.Press(window, Key.Z, ModifierKeys.Control));
                WindowHarness.Pump();
                Assert.Null(Row(Unset).Model.ParallaxReversed);
                Assert.Equal(d.Red, Drifts(preview).Red, 1);
                Assert.Equal(true, TickOf(Unset));
                Click(tick);
                WindowHarness.Pump();

                // Through the real save: both "false"s written, the untouched one left unset.
                string savedTo = Path.Combine(_dir, "saved");
                PackRepository.Save(vm.Pack, savedTo);
                var saved = JObject.Parse(File.ReadAllText(Path.Combine(savedTo, PackRepository.ManifestFileName)));
                JObject Saved(string name) => saved.SelectTokens("$..gameObjects[*]").OfType<JObject>().Single(o => (string?)o["name"] == name);
                _out.WriteLine("saved: " + Saved(Unset).ToString(Newtonsoft.Json.Formatting.None));
                Assert.Equal(JTokenType.Boolean, Saved(Unset)["parallaxReversed"]?.Type);
                Assert.False((bool)Saved(Unset)["parallaxReversed"]!);
                Assert.Equal(JTokenType.Boolean, Saved(HandWritten)["parallaxReversed"]?.Type);
                Assert.False((bool)Saved(HandWritten)["parallaxReversed"]!);
                Assert.Null(Saved(Untouched)["parallaxReversed"]);

                // The place's back picture turned forward: the untouched one's tick follows it (it is shown picked),
                // and it drifts forward; the ones set to false are as they were.
                Assert.Equal(true, TickOf(Untouched));
                place.ParallaxSecondaryReversed = false;
                WindowHarness.Pump();
                Assert.Equal(false, tick.IsChecked);
                var forward = Drifts(preview);
                _out.WriteLine($"back forward: front {forward.Front:F1}; unset {forward.Blue:F1}, false {forward.Red:F1}");
                Assert.Equal(forward.Red, forward.Blue, 1);
                Assert.Equal(d.Green, forward.Green, 1);
                place.ParallaxSecondaryReversed = true;
                WindowHarness.Pump();
                Assert.Equal(true, tick.IsChecked);

                // Back, as clicked: all of the back picture's drift, its reversal too.
                Assert.True(preview.Pick(Row(HandWritten)));
                WindowHarness.Pump();
                Click(preview.LightDrift.Back);
                WindowHarness.Pump();
                Assert.Equal(Drifting.Back, Row(HandWritten).Drift);
                Assert.Null(Row(HandWritten).Model.ParallaxReversed);
                var back = Drifts(preview);
                Assert.Equal(back.Back, back.Green, 1);
            });
        }
        finally { EditorPrefs.PlacePreviewParallax = was; }
    }

    /// <summary>The guard's picture as the preview draws it now (it is drawn again after every change).</summary>
    private static Image GuardPicture(PlacePreview preview)
        => Field<Canvas>(preview, "_worldLayer").Children.OfType<Image>()
               .Single(i => (i.ToolTip as string)?.StartsWith(Guard, StringComparison.Ordinal) == true && i.Width == 120);

    private static double GuardDrift(PlacePreview preview)
    {
        preview.FollowCursor(960, 540, Screen);
        WindowHarness.Pump();
        double at = OffsetX(GuardPicture(preview));
        preview.FollowCursor(0, 540, Screen);
        WindowHarness.Pump();
        return OffsetX(GuardPicture(preview)) - at;
    }

    /// <summary>A character drifting by a strength of its own in the same place: the same tick, the same "false".</summary>
    [Fact]
    public void ACharacterDriftingByItsOwnStrength_ShowsTheGamesReversal_AndUntickedDriftsForwardAndIsSaved()
    {
        Png(Path.Combine(_dir, "Places", "front.png"), 2048, 1136, 90);
        Png(Path.Combine(_dir, "Places", "back.png"), 2048, 1136, 60);
        Png(Path.Combine(_dir, "Npcs", "guard.png"), 120, 300, 200);
        var pack = PackRepository.CreateEmpty("driftrev.chars");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png" });
        var hall = Hall();
        hall.GameObjects.Add(new GameObjectDef
        {
            Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
            Npcs = { new NpcPlacementDef { Npc = "guard", Name = Guard, StartActive = true, ParallaxDisabled = false, ParallaxStrength = 0.55f,
                                           Body = new NpcTransform { X = 2f } } },
        });
        pack.Places.Add(hall);
        PackRepository.Save(pack, _dir);

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
                var guard = place.GameObjects.Single(g => g.IsNpcRoot).Npcs.Single();
                Assert.True(preview.Pick(guard));
                WindowHarness.Pump();
                var tick = ReversedTick(preview.CharacterDrift);
                Assert.True(tick.IsVisible, "the character's Reversed is not shown");

                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = true;
                WindowHarness.Pump();
                double unset = GuardDrift(preview);
                double front = OffsetX(Field<Image>(preview, "_baseImage"));
                _out.WriteLine($"front {front:F1}; the guard unset {unset:F1}");
                Assert.True(unset < front - 10, $"an unset reversal does not drift reversed here ({unset:F1} against {front:F1})");
                Assert.Equal(true, tick.IsChecked);
                Assert.Null(guard.Model.ParallaxReversed);

                Click(tick);
                WindowHarness.Pump();
                Assert.Equal(false, guard.Model.ParallaxReversed);
                double unticked = GuardDrift(preview);
                _out.WriteLine($"unticked {unticked:F1}");
                Assert.True(unticked > front + 10, $"unticked it does not drift forward ({unticked:F1} against {front:F1})");

                string savedTo = Path.Combine(_dir, "saved");
                PackRepository.Save(vm.Pack, savedTo);
                var saved = JObject.Parse(File.ReadAllText(Path.Combine(savedTo, PackRepository.ManifestFileName)));
                var placed = saved.SelectTokens("$..npcs[*]").OfType<JObject>().Single(o => (string?)o["name"] == Guard);
                _out.WriteLine("saved: " + placed.ToString(Newtonsoft.Json.Formatting.None));
                Assert.Equal(JTokenType.Boolean, placed["parallaxReversed"]?.Type);
                Assert.False((bool)placed["parallaxReversed"]!);
            });
        }
        finally { EditorPrefs.PlacePreviewParallax = was; }
    }
}
