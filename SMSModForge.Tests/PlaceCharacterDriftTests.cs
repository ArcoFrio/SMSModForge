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
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A character standing in a place drifts with the cursor as its own "Moves with" says (after 1.10.0, the author asked
/// whether we "ever have the option of changing parallax settings for individual NPCs", and then for it): the front
/// picture's drift as before, the back picture's, or a strength of its own - and the place's NPCs object, set to
/// drift, takes every character with it, as it now does in the game. Measured on what is drawn: how far the
/// character's picture has moved against how far the place's back and front pictures have.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceCharacterDriftTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-chardrift-" + Guid.NewGuid().ToString("N"));

    public PlaceCharacterDriftTests(ITestOutputHelper o)
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

    private const string Guard = "Guard";

    /// <summary>A place whose back picture drifts well apart from its front, with a guard standing in it, placed
    /// before characters could drift.</summary>
    private void WritePack()
    {
        Png(Path.Combine(_dir, "Places", "front.png"), 2048, 1136, 90);
        Png(Path.Combine(_dir, "Places", "back.png"), 2048, 1136, 60);
        Png(Path.Combine(_dir, "Npcs", "guard.png"), 120, 300, 200);
        var pack = PackRepository.CreateEmpty("chardrift.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png" });
        var hall = new PlaceDef
        {
            Key = "hall", InternalName = "Hall", DisplayName = "Hall",
            BaseSprite = "Places/front.png", SecondarySprite = "Places/back.png",
            ParallaxStrength = 0.75f, ParallaxSecondaryStrength = 1.2f,
        };
        hall.GameObjects.Add(new GameObjectDef
        {
            Name = "NPCs", Role = GameObjectDef.RoleNpcRoot,
            Npcs = { new NpcPlacementDef { Npc = "guard", Name = Guard, StartActive = true, Body = new NpcTransform { X = 2f } } },
        });
        pack.Places.Add(hall);
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    private static double OffsetX(UIElement e) => (e.RenderTransform as MatrixTransform)?.Matrix.OffsetX ?? 0;

    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    /// <summary>The guard's picture as the preview draws it now (it is drawn again after every change).</summary>
    private static Image GuardPicture(PlacePreview preview)
        => Field<Canvas>(preview, "_worldLayer").Children.OfType<Image>()
               .Single(i => (i.ToolTip as string)?.StartsWith(Guard, StringComparison.Ordinal) == true && i.Width == 120);

    /// <summary>How far the guard's picture is drifted with the cursor at the screen's left edge, against the middle.</summary>
    private static double GuardDrift(PlacePreview preview)
    {
        preview.FollowCursor(960, 540, Screen);
        WindowHarness.Pump();
        double at = OffsetX(GuardPicture(preview));
        preview.FollowCursor(0, 540, Screen);
        WindowHarness.Pump();
        return OffsetX(GuardPicture(preview)) - at;
    }

    [Fact]
    public void ACharacterDriftsWithTheFrontAsBefore_OrTheBackPicture_OrByItsOwnStrength_AndWithTheNpcsObject()
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
                var npcs = place.GameObjects.Single(g => g.IsNpcRoot);
                var guard = npcs.Npcs.Single();
                Assert.Equal(Drifting.Front, guard.Drift);   // placed before characters could drift

                // Picked, its "Moves with" is beside the picture with Front chosen; the lamp's is not.
                Assert.True(preview.Pick(guard));
                WindowHarness.Pump();
                var d = preview.CharacterDrift;
                Assert.True(d.Panel.IsVisible, "the character's Moves with is not beside the picture");
                Assert.False(preview.LightDrift.Panel.IsVisible, "a lamp's Moves with shows for a character");
                Assert.False(ReferenceEquals(d.Front.Background, d.Back.Background), "Front is not shown as the one chosen");
                Assert.True(d.Back.IsEnabled, "a place of the pack's has a back picture to drift with");

                ((CheckBox)window.FindName("PlaceParallaxPreview")).IsChecked = true;
                WindowHarness.Pump();
                preview.FollowCursor(0, 540, Screen);
                WindowHarness.Pump();
                double backMoved = OffsetX(Field<Image>(preview, "_secondaryImage"));
                double frontMoved = OffsetX(Field<Image>(preview, "_baseImage"));
                preview.FollowCursor(960, 540, Screen);
                WindowHarness.Pump();
                backMoved -= OffsetX(Field<Image>(preview, "_secondaryImage"));
                frontMoved -= OffsetX(Field<Image>(preview, "_baseImage"));
                Assert.True(Math.Abs(backMoved - frontMoved) > 10, "control: the back and front drift alike here - the check cannot tell them apart");

                double asMade = GuardDrift(preview);
                _out.WriteLine($"the back picture drifted {backMoved:F1} canvas px, the front {frontMoved:F1}; the guard, as placed, {asMade:F1}");
                Assert.Equal(frontMoved, asMade, 1);

                // Back, as clicked: the back picture's drift. Its shadow goes with it.
                Click(d.Back);
                WindowHarness.Pump();
                Assert.Equal(Drifting.Back, guard.Drift);
                double withBack = GuardDrift(preview);
                _out.WriteLine($"with the back: {withBack:F1}");
                Assert.Equal(backMoved, withBack, 1);

                // Own: chosen, it moves nothing until its strength is changed.
                Click(d.Own);
                WindowHarness.Pump();
                Assert.Equal(Drifting.Own, guard.Drift);
                Assert.True(d.OwnFields.IsVisible, "Own shows no strength to set");
                Assert.Equal(backMoved, GuardDrift(preview), 1);
                guard.ParallaxStrength = 0.4f;
                WindowHarness.Pump();
                double own = GuardDrift(preview);
                _out.WriteLine($"by its own 0.4: {own:F1}");
                Assert.True(Math.Abs(own - frontMoved) > 5 && Math.Abs(own - backMoved) > 5, "its own strength did not move it its own way");

                // What the game is given, through the real save.
                string savedTo = Path.Combine(_dir, "saved");
                PackRepository.Save(vm.Pack, savedTo);
                JObject Saved() => JObject.Parse(File.ReadAllText(Path.Combine(savedTo, PackRepository.ManifestFileName)))
                                          .SelectTokens("$..npcs[*]").OfType<JObject>().Single(o => (string?)o["name"] == Guard && o["npc"] != null);
                var ownSaved = Saved();
                _out.WriteLine($"saved with its own: {ownSaved.ToString(Newtonsoft.Json.Formatting.None)}");
                Assert.Equal(false, (bool?)ownSaved["parallaxDisabled"]);
                Assert.Equal(0.4, (double)ownSaved["parallaxStrength"]!, 3);

                // Front again: written as a character that does not drift was before - nothing about drifting.
                Click(d.Front);
                WindowHarness.Pump();
                Assert.Equal(frontMoved, GuardDrift(preview), 1);
                PackRepository.Save(vm.Pack, savedTo);
                var frontSaved = Saved();
                Assert.Null(frontSaved["parallaxDisabled"]);
                Assert.Null(frontSaved["parallaxStrength"]);
                Assert.Null(frontSaved["parallaxReversed"]);

                // The NPCs object set to drift (its Parallax, the back picture's strength): every character with it.
                npcs.ParallaxEnabled = true;
                WindowHarness.Pump();
                Assert.Equal(Drifting.Back, npcs.Drift);
                double withTheNpcs = GuardDrift(preview);
                _out.WriteLine($"standing in the NPCs object set to drift: {withTheNpcs:F1}");
                Assert.Equal(backMoved, withTheNpcs, 1);
            });
        }
        finally { EditorPrefs.PlacePreviewParallax = was; }
    }
}
