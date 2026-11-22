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

namespace SMSModForge.Tests;

/// <summary>
/// The window tests draw their previews on what the run asks for (1.10.0): the
/// processor (the suite's own default under TestMode) unless
/// <c>SMSMODFORGE_TEST_RENDERER</c> names the card - which is how the place
/// preview's scene drawn by the card is put through every window test. As the
/// theme's (<see cref="HarnessThemeTests"/>): the app's own start-up runs inside
/// the harness after the run's choice is made, and must not put another on over
/// it - and the place preview must then really draw its scene on the card, or a
/// run "on the card" proves nothing about it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class HarnessRendererTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-harnessrenderer-" + Guid.NewGuid().ToString("N"));

    public HarnessRendererTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void TheWindowTestsDrawTheirPreviewsOnWhatTheRunAsksFor()
    {
        string? asked = Environment.GetEnvironmentVariable("SMSMODFORGE_TEST_RENDERER") is { Length: > 0 } r ? r : null;
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "front.png"), 256, 144, Color.FromRgb(0x30, 0x50, 0x70));
        var pack = PackRepository.CreateEmpty("harnessrenderer.pack");
        pack.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave", BaseSprite = "Places/front.png" });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());
        WindowHarness.Run(window =>
        {
            WindowHarness.Pump();
            Assert.Equal(asked ?? "Automatic", PreviewRendererManager.Current.Name, ignoreCase: true);
            bool card = string.Equals(asked, "Card", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(card, PreviewRendererManager.UseCard);

            // And the place preview's look is then the card's own drawing of the scene - or, on the processor, not.
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var place = (PlacePreview)window.FindName("PlacePreviewMain");
            Assert.True(WindowHarness.Until(() => place.LookPasses > 0, TimeSpan.FromSeconds(20)), "the look was never drawn over the place");
            Assert.True(place.CardDrawsScene == card, $"the place's scene is drawn by the card: {place.CardDrawsScene}; asked for: {asked ?? "the processor"} ({place.CardFailure})");
            Assert.Equal(card, place.LookOnCard);
        });
    }
}
