using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A wet outfit's drops in the Characters tab's bust preview (after 1.10.0: "busts can carry the same wet drops in
/// dialogues, and the bust preview doesn't show them yet" - the author: "yes"): with its "Wet particle active on spawn"
/// ticked the preview draws the game's drops round the bust's middle, alone and over the room; unticked, none. Where
/// they are and how they are blended is held to the game by InGameSuiteTests.S20, the card to the processor by
/// GpuFrameTests.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BustWetPreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-bustwet-" + Guid.NewGuid().ToString("N"));

    public BustWetPreviewTests(ITestOutputHelper o) => _out = o;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void A_wet_outfit_draws_the_games_drops_over_its_bust_and_a_dry_one_none()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, Color.FromRgb(0xC0, 0x80, 0x60));
        var pack = PackRepository.CreateEmpty("bustwet.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        var outfit = new OutfitDef { Key = "swim", GameObjectName = "Anna_Swim", BaseSprite = "Busts/anna.png" };
        outfit.Particles[0].Active = true;
        anna.Outfits.Add(outfit);
        pack.Characters.Add(anna);
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        var wasBackdrop = PreviewBackdropManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
                WindowHarness.Pump();
                vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
                vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
                WindowHarness.Pump();
                var bust = (JigglePreview)window.FindName("PreviewControl");
                int Drawn()
                {
                    int from = bust.FramesShown;
                    Assert.True(WindowHarness.Until(() => bust.FramesShown >= from + 2, TimeSpan.FromSeconds(20)), "the bust preview drew no frame");
                    return bust.DropsDrawn;
                }
                foreach (var backdrop in new[] { PreviewBackdropManager.All.First(d => d.IsNone), PreviewBackdropManager.All.First(d => d.Name == "MyRoom") })
                {
                    PreviewBackdropManager.Apply(backdrop);
                    vm.SelectedOutfit!.ParticleActive = true;
                    int wet = Drawn();
                    var at = bust.LastEmitter;
                    // The frame as drawn, to look at (SMSMODFORGE_SHOT_DIR).
                    if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots && bust.Source is System.Windows.Media.Imaging.BitmapSource shown)
                    {
                        Directory.CreateDirectory(shots);
                        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shown));
                        using var fs = File.Create(Path.Combine(shots, "bust-wet-" + backdrop.Name + ".png"));
                        enc.Save(fs);
                    }
                    vm.SelectedOutfit.ParticleActive = false;
                    int dry = Drawn();
                    _out.WriteLine($"{backdrop.Name}: {wet} drops with the Wet on, at ({at?.X:0.#}, {at?.Y:0.#}) of the canvas; {dry} with it off");
                    Assert.True(wet >= 1, $"{backdrop.Name}: a wet outfit drew no drops");   // a dozen on screen at a time, as in the game
                    Assert.Equal(0, dry);
                    // The shower (after 1.10.0: "a toggle between the two wet types for both busts and npcs"): hundreds,
                    // from the top of the bust's picture (where the game has it: InGameSuiteTests.S22).
                    vm.SelectedOutfit.ParticleKindIndex = 1;
                    vm.SelectedOutfit.ParticleActive = true;
                    int shower = Drawn();
                    var showerAt = bust.LastEmitter;
                    vm.SelectedOutfit.ParticleActive = false;
                    vm.SelectedOutfit.ParticleKindIndex = 0;
                    _out.WriteLine($"{backdrop.Name}: {shower} drops under the shower, at ({showerAt?.X:0.#}, {showerAt?.Y:0.#})");
                    Assert.Equal(Rendering.WetDrops.Kind.Shower, showerAt?.Of);
                    Assert.True(shower > 100, $"{backdrop.Name}: the shower drew {shower} drops");
                    Assert.True(showerAt?.Y < at?.Y, $"{backdrop.Name}: the shower does not start above the towel's middle");
                }
            });
        }
        finally { PreviewBackdropManager.Apply(wasBackdrop); }
    }
}
