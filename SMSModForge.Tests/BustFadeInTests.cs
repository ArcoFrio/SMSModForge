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
/// The Characters tab's bust preview fades a bust in as the game does (1.9.1,
/// the author: "the Bust fade-in for characters, with a toggle below as well
/// that defaults to true"). Its clock is held at chosen moments and the colour
/// the preview drew is read back: black and half there at 0.5 s, the
/// character under a half silhouette at 2.5 s, itself at 5 s. The controls:
/// with the toggle off an outfit shows at once, and switching it on plays it
/// again. Drawn with no backdrop (the bust alone, its numbers its own); over
/// the player's room, <see cref="OverTheRoom_TheSilhouetteDarkensTheRoom_ThenTheBustShows"/>.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BustFadeInTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-fade-" + Guid.NewGuid().ToString("N"));

    public BustFadeInTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static byte[] Drawn(JigglePreview preview)
    {
        var px = new byte[4];
        ((WriteableBitmap)preview.Source).CopyPixels(new Int32Rect(256, 256, 1, 1), px, 4, 0);
        return px;
    }

    private static double Linear(double srgb) => srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    private static double Srgb(double linear) => linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;

    /// <summary>Runs <paramref name="body"/> with the bust preview drawing over
    /// <paramref name="backdrop"/> ("None" for none), and puts the choice back.</summary>
    internal static void WithBackdrop(string backdrop, Action body)
    {
        var before = PreviewBackdropManager.Current;
        PreviewBackdropManager.Apply(PreviewBackdropManager.All.First(d => d.Name == backdrop));
        try { body(); }
        finally { PreviewBackdropManager.Apply(before); }
    }

    private void Pack(Color colour)
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, colour);
        var pack = PackRepository.CreateEmpty("fade.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png" });
        anna.Outfits.Add(new OutfitDef { Key = "night", GameObjectName = "Anna_Night", BaseSprite = "Busts/anna.png" });
        pack.Characters.Add(anna);
        PackRepository.Save(pack, _dir);
    }

    /// <summary>
    /// The backdrop is chosen at the bottom of the bust preview's options too
    /// (1.9.1, the author: "it should still appear in the main tabs but just be
    /// maybe at the bottom"): None there takes the room away from the preview
    /// and marks None, the room brings it back. Control: before any press the
    /// bar marks the room, and the preview draws over it.
    /// </summary>
    [Fact]
    public void TheBackdropButtonsUnderThePreviewChooseWhatItDrawsOver() => WithBackdrop("MyRoom", () =>
    {
        Pack(Color.FromRgb(0xC0, 0x80, 0x60));
        Assert.NotNull(PreviewBackdropManager.Current.SceneWaited());   // loaded, or the first look finds it loading
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
            var preview = (JigglePreview)window.FindName("PreviewControl");
            var bar = (PreviewBackdropBar)window.FindName("BustBackdropBar");
            Assert.True(bar.IsVisible, "the backdrop buttons are not shown under the preview");
            Assert.Equal("MyRoom", bar.Marked());
            Assert.True(preview.OverBackdrop);
            Button ButtonFor(string name) => bar.Children.OfType<Button>().Single(b => (string)b.Tag == name);
            ButtonFor("None").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
            Assert.Equal("None", bar.Marked());
            Assert.Equal("None", PreviewBackdropManager.Current.Name);
            Assert.False(preview.OverBackdrop, "the preview still draws over the room after None");
            ButtonFor("MyRoom").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Wait(TimeSpan.FromMilliseconds(400));
            Assert.Equal("MyRoom", bar.Marked());
            Assert.True(preview.OverBackdrop, "the room did not come back");
        });
    });

    [Fact]
    public void AnOutfitShownFadesInAsInTheGame_AndTheToggleTurnsItOff() => WithBackdrop("None", () =>
    {
        var colour = Color.FromRgb(0xC0, 0x80, 0x60);
        Pack(colour);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            var preview = (JigglePreview)window.FindName("PreviewControl");
            var toggle = (CheckBox)window.FindName("FadeInToggle");
            Assert.True(toggle.IsChecked, "the fade-in is not on by default");
            Assert.True(preview.FadesIn);

            double now = 0;
            preview.IntroNow = () => now;
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();

            byte[] At(double t)
            {
                now = t;
                WindowHarness.Wait(TimeSpan.FromMilliseconds(350));
                var px = Drawn(preview);
                _out.WriteLine($"at {t:0.0} s: B {px[0]} G {px[1]} R {px[2]} A {px[3]}");
                return px;
            }

            // The first second: the body hidden, a black silhouette half in.
            var half = At(0.5);
            Assert.Equal(new byte[] { 0, 0, 0 }, half.Take(3).ToArray());
            Assert.InRange(half[3], 126, 130);

            // Then the character under a silhouette half gone: half its light.
            var reveal = At(2.5);
            Assert.Equal(255, reveal[3]);
            byte[] lit = { colour.B, colour.G, colour.R };
            for (int c = 0; c < 3; c++)
                Assert.InRange(reveal[c], (int)Math.Round(Srgb(Linear(lit[c] / 255.0) * 0.5) * 255) - 1,
                                           (int)Math.Round(Srgb(Linear(lit[c] / 255.0) * 0.5) * 255) + 1);

            // Done: the character itself.
            Assert.Equal(new byte[] { colour.B, colour.G, colour.R, 255 }, At(5));

            // The control: off, another outfit shows at once.
            toggle.IsChecked = false;
            WindowHarness.Pump();
            now = 100;
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.Last();
            WindowHarness.Pump();
            Assert.Equal(new byte[] { colour.B, colour.G, colour.R, 255 }, At(100.2));

            // On again, it plays again from that moment.
            now = 200;
            toggle.IsChecked = true;
            WindowHarness.Pump();
            var again = At(200.5);
            Assert.Equal(new byte[] { 0, 0, 0 }, again.Take(3).ToArray());
            Assert.InRange(again[3], 126, 130);
        });
    });

    /// <summary>
    /// Over the player's room (the default backdrop) the same moments, read at
    /// the same pixel against the room the preview draws there with the bust
    /// off (no outfit): half in, the black silhouette takes away half the
    /// room's light; at the end the bust's own colour, lit by the room's lamps
    /// (never darker than the art). Every pixel opaque. Control: before the
    /// silhouette darkens anything, the room is what is read.
    /// </summary>
    [Fact]
    public void OverTheRoom_TheSilhouetteDarkensTheRoom_ThenTheBustShows() => WithBackdrop("MyRoom", () =>
    {
        var colour = Color.FromRgb(0xC0, 0x80, 0x60);
        Pack(colour);
        Assert.NotNull(PreviewBackdropManager.Current.SceneWaited());

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            var preview = (JigglePreview)window.FindName("PreviewControl");
            double now = 0;
            preview.IntroNow = () => now;
            byte[] At(double t)
            {
                now = t;
                WindowHarness.Wait(TimeSpan.FromMilliseconds(350));
                var px = Drawn(preview);
                _out.WriteLine($"at {t:0.0} s: B {px[0]} G {px[1]} R {px[2]} A {px[3]}");
                return px;
            }

            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            WindowHarness.Pump();
            var start = At(0.0);         // the silhouette not in yet: the room
            var half = At(0.5);
            var done = At(5);
            Assert.True(preview.OverBackdrop, "the preview is not drawing over the room");
            Assert.All(new[] { start, half, done }, px => Assert.Equal(255, px[3]));
            // Half way through the silhouette's second, the room under it is
            // darkened by about half: not exactly, since the lamp's glow is
            // added over the silhouette afterwards and the post-processing bends
            // what is drawn (S3 holds the frame to the game's own).
            for (int c = 0; c < 3; c++)
            {
                double room = Linear(start[c] / 255.0), seen = Linear(half[c] / 255.0);
                Assert.InRange(seen, room * 0.3, room * 0.65);
            }
            byte[] art = { colour.B, colour.G, colour.R };
            // The bust shows: its art's colour, give or take what the post-processing
            // does around it (its bloom from the room - and this square bust's
            // shadow, its lamp inside the art, darkens all the room around it).
            for (int c = 0; c < 3; c++) Assert.InRange(done[c], art[c] - 15, art[c] + 25);
            Assert.False(start.Take(3).SequenceEqual(art), "the room under the bust reads as the bust's own colour - the check cannot tell them apart");
        });
    });
}
