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
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// A character standing in the place, in the place preview (1.10.0): chosen in
/// the panel beside the picture, drawn where the game shows a character in a
/// dialogue, lit by the place's lights as the game lights a bust - a light of
/// the place only reaches the bust at its edges, one that lights the characters
/// too brightens it where it reaches. Measured from the drawn frame: the
/// shoulder by an orange light, against the far one (the control, which no light
/// reaches), with "Lights the characters too" off and on.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceStandInTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-standin-" + Guid.NewGuid().ToString("N"));

    public PlaceStandInTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static T Field<T>(object o, string name)
        => (T)typeof(PlacePreview).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is Panel p)
            foreach (UIElement c in p.Children)
            {
                yield return c;
                foreach (var d in Descendants(c)) yield return d;
            }
        else if (root is Decorator dec && dec.Child != null)
        {
            yield return dec.Child;
            foreach (var d in Descendants(dec.Child)) yield return d;
        }
    }

    [Fact]
    public void A_character_stands_in_the_place_lit_by_a_light_only_once_it_lights_the_characters()
    {
        int size = InGameSuitePacks.Looks.Size;
        string figure = InGameSuitePacks.PngOf(_dir, "Busts/figure.png", size, size,
                                               (x, y) => Color.FromArgb(InGameSuitePacks.Looks.FigureAt(x, y).A, 170, 170, 170));
        string grey = InGameSuitePacks.PngOf(_dir, "Places/grey.png", 2048, 1136, (_, _) => Color.FromRgb(0x50, 0x50, 0x50));
        var pack = PackRepository.CreateEmpty("standin.pack");
        var outfit = new OutfitDef { Key = "plain", GameObjectName = "StandInFigure", BlinkEnabled = false, BaseSprite = figure,
                                     Mouth = { Enabled = false }, Expression = { Enabled = false } };
        outfit.Particles.Clear();
        pack.Characters.Add(new CharacterDef { Key = "fig", Name = "Fig", DisplayName = "Fig", Pronouns = Pronouns.Neutral,
                                               BustSource = BustSource.Pack, Outfits = { outfit } });
        pack.Places.Add(new PlaceDef
        {
            Key = "hall", InternalName = "Hall", DisplayName = "Hall", BaseSprite = grey,
            GameObjects =
            {
                // Left of the bust, at its shoulders' height (the game's screen: x 250, y 200 of 960 x 540).
                new GameObjectDef { Name = "Lamp", X = -4.6f, Y = -1.4f,
                                    Light = new LightDef { Color = "#FF7A1A", Strength = 1.5f, OuterRadius = 3f, InnerRadius = 0.6f, Glow = 0.3f } },
            },
        });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");

            // Chosen in the panel beside the picture.
            var box = Field<ComboBox>(preview, "_standInBox");
            // Nobody first, then the pack's own characters (before the game's).
            object? OutfitOf(object item) => item.GetType().GetProperty("Outfit")!.GetValue(item);
            foreach (var item in box.Items.Cast<object>().Take(4)) _out.WriteLine("choice: " + item);
            foreach (var c in vm.Characters.Take(3)) _out.WriteLine($"character {c.Key}: pack bust {c.IsPackBust}, source {c.Model.BustSource}");
            Assert.Null(OutfitOf(box.Items[0]!));
            Assert.Equal("plain", ((OutfitViewModel)OutfitOf(box.Items[1]!)!).Key);
            box.SelectedIndex = 1;

            byte[] Frame(int built)
            {
                Assert.True(WindowHarness.Until(() => preview.StandInBuilt > built && preview.StandIn?.Visibility == Visibility.Visible,
                                                TimeSpan.FromSeconds(20)), "the character's place was not made");
                var standIn = preview.StandIn!;
                int shown = standIn.FramesShown;
                Assert.True(WindowHarness.Until(() => standIn.FramesShown > shown + 2, TimeSpan.FromSeconds(20)), "the character is not drawn");
                var bmp = (BitmapSource)standIn.Source;
                var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
                bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
                return px;
            }
            // The frame is the game's screen from x 224, its bottom row the screen's: a screen point's pixel in it.
            int At(byte[] px, int sx, int sy, int c) => px[((511 - sy) * 512 + (sx - 224)) * 4 + c];

            var before = Frame(0);
            // Well inside the shoulders, whatever the breathing: one by the light, the other far from it.
            int nearOff = At(before, 330, 170, 2), farOff = At(before, 600, 170, 2), placeOff = At(before, 240, 260, 2);

            // The light picked in the hierarchy, its "Lights the characters too" ticked.
            var lamp = vm.SelectedPlace!.GameObjects.Single(g => g.Name == "Lamp");
            var row = Field<StackPanel>(preview, "_objectMenuList").Children.OfType<Button>()
                .First(b => b.Content is Panel sp && sp.Children.OfType<TextBlock>().Any(tb => tb.Text.EndsWith("Lamp", StringComparison.Ordinal)));
            ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(row)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();
            WindowHarness.Pump();
            var tick = Descendants(Field<StackPanel>(preview, "_lightFields")).OfType<CheckBox>().Single();
            int built = preview.StandInBuilt;
            tick.IsChecked = true;
            WindowHarness.Pump();
            Assert.True(lamp.Light!.LightsCharacters, "the tick box did not set the light to light the characters");
            var after = Frame(built);
            int nearOn = At(after, 330, 170, 2), farOn = At(after, 600, 170, 2);
            _out.WriteLine($"the shoulder by the light (red): {nearOff} lighting the place only, {nearOn} lighting the characters too; "
                           + $"the far shoulder: {farOff}, {farOn}; the place by the light {placeOff}");
            Assert.True(placeOff > 120, "the light does not light the place where it stands");
            Assert.True(nearOn > nearOff + 20, "lighting the characters too does not brighten the bust by the light");
            Assert.True(Math.Abs(farOn - farOff) <= 3, "control: the far shoulder, which the light does not reach, changed");

            // The place keeps its template light, whose shadow of the character the backdrop has.
            Assert.NotNull(preview.StandInScene?.Backdrop.Lamp);

            if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
                foreach (var (name, px) in new[] { ("place-only", before), ("characters-too", after) })
                {
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(512, 512, 96, 96, PixelFormats.Pbgra32, null, px, 512 * 4)));
                    using var fs = File.Create(Path.Combine(shots, "standin-" + name + ".png"));
                    enc.Save(fs);
                }
        });
    }
}
