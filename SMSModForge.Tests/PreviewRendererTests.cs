using System;
using System.Diagnostics;
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
/// Options ▸ Draw previews on (1.9.1): with the graphics card chosen, the bust
/// preview over the room draws its body there (GpuBody, held to the
/// processor's by GpuBodyTests) and its picture shows the bust; with the
/// processor chosen it does not (the control). The frame rates of both are
/// printed. Automatic under the test suite is the processor (nothing measured).
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PreviewRendererTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-renderer-" + Guid.NewGuid().ToString("N"));

    public PreviewRendererTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void WithTheCardChosenTheBustPreviewDrawsOnIt_WithTheProcessorItDoesNot()
    {
        var colour = Color.FromRgb(0xC0, 0x80, 0x60);
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, colour);
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "mask.png"), 256, 256, Color.FromRgb(200, 100, 50));
        var pack = PackRepository.CreateEmpty("renderer.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png", MaskSprite = "Busts/mask.png" });
        pack.Characters.Add(anna);
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());

        var wasQuality = PreviewQualityManager.Current;
        var wasRenderer = PreviewRendererManager.Current;
        var wasBackdrop = PreviewBackdropManager.Current;
        try
        {
            // Automatic, whatever the run itself draws on (SMSMODFORGE_TEST_RENDERER, 1.10.0).
            PreviewRendererManager.Apply(PreviewRendererManager.All.First(r => r.Name == "Automatic"));
            Assert.False(PreviewRendererManager.UseCard, "Automatic draws on the card under the test suite");
            PreviewBackdropManager.Apply(PreviewBackdropManager.All.First(d => d.Name == "MyRoom"));
            PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
                WindowHarness.Pump();
                var preview = (JigglePreview)window.FindName("PreviewControl");
                preview.FadesIn = false;
                vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
                vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
                WindowHarness.Wait(TimeSpan.FromMilliseconds(500));

                double Rate()
                {
                    int before = preview.ShaderPasses;
                    var clock = Stopwatch.StartNew();
                    WindowHarness.Wait(TimeSpan.FromSeconds(2));
                    return (preview.ShaderPasses - before) / clock.Elapsed.TotalSeconds;
                }
                byte[] Centre()
                {
                    var px = new byte[4];
                    ((WriteableBitmap)preview.Source).CopyPixels(new Int32Rect(256, 300, 1, 1), px, 4, 0);
                    return px;
                }

                PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Card"));
                // The kernel compiled (a test run keeps its own cache: seconds) or loaded.
                var compile = Stopwatch.StartNew();
                while (!preview.DrewOnCard && compile.Elapsed < TimeSpan.FromSeconds(60)) WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                _out.WriteLine($"the first frame on the card after {compile.Elapsed.TotalSeconds:0.0} s");
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                double onCard = Rate();
                bool drewOnCard = preview.DrewOnCard;
                var cardPixel = Centre();
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Processor"));
                WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
                double onProcessor = Rate();
                bool drewOnCardAfter = preview.DrewOnCard;
                var processorPixel = Centre();
                _out.WriteLine($"Ultra over the room: {onCard:0.0} frames a second on the card, {onProcessor:0.0} on the processor (display {SMSModForge.Rendering.DisplayRefresh.RateOf(preview)})");
                _out.WriteLine($"the bust's middle: card B{cardPixel[0]} G{cardPixel[1]} R{cardPixel[2]}, processor B{processorPixel[0]} G{processorPixel[1]} R{processorPixel[2]}");
                Assert.True(drewOnCard, "with the card chosen the body was not drawn on it");
                Assert.False(drewOnCardAfter, "control: with the processor chosen the body was still drawn on the card");
                // A flat bust: the same colour either way, give or take its motion and glow.
                for (int c = 0; c < 3; c++) Assert.InRange(cardPixel[c], processorPixel[c] - 12, processorPixel[c] + 12);
            });
        }
        finally
        {
            PreviewQualityManager.Apply(wasQuality);
            PreviewRendererManager.Apply(wasRenderer);
            PreviewBackdropManager.Apply(wasBackdrop);
        }
    }

    /// <summary>The NPC preview - over a backdrop and with none - and the place
    /// preview's game look draw on the card when it is chosen (1.9.1), and not
    /// with the processor chosen (the control).</summary>
    [Fact]
    public void WithTheCardChosenTheNpcAndPlaceLookDrawOnIt_WithTheProcessorTheyDoNot()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "guard.png"), 200, 400, Color.FromRgb(0x70, 0x60, 0x50));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "mask.png"), 200, 400, Color.FromRgb(200, 100, 50));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "front.png"), 256, 144, Color.FromRgb(0x30, 0x50, 0x70));
        var pack = PackRepository.CreateEmpty("renderer.pack");
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Mask = "Npcs/mask.png" });
        pack.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave", BaseSprite = "Places/front.png" });
        PackRepository.Save(pack, _dir);
        Assert.NotNull(PreviewBackdropManager.All.First(d => !d.IsNone).SceneWaited());

        var wasRenderer = PreviewRendererManager.Current;
        var wasBackdrop = PreviewBackdropManager.Current;
        var wasQuality = PreviewQualityManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                var tabs = (TabControl)window.FindName("MainTabs");
                tabs.SelectedIndex = MainTab.Npcs;
                WindowHarness.Pump();
                vm.SelectedNpc = vm.Npcs.First();
                WindowHarness.Pump();
                var npc = (NpcPreview)window.FindName("NpcPreviewControl");
                bool Until(Func<bool> done)
                {
                    var clock = Stopwatch.StartNew();
                    while (!done() && clock.Elapsed < TimeSpan.FromSeconds(60)) WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                    return done();
                }

                var results = new System.Collections.Generic.List<string>();
                foreach (var backdrop in new[] { PreviewBackdropManager.All.First(d => d.Name == "MyRoom"), PreviewBackdropManager.All.First(d => d.IsNone) })
                {
                    PreviewBackdropManager.Apply(backdrop);
                    PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Card"));
                    bool onCard = Until(() => npc.DrewOnCard && npc.OverBackdrop == !backdrop.IsNone);
                    PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Processor"));
                    bool offCard = Until(() => !npc.DrewOnCard);
                    results.Add($"the NPC over {backdrop.Name}: on the card {onCard}, then on the processor {offCard}");
                    Assert.True(onCard, $"with the card chosen the NPC over {backdrop.Name} was not drawn on it");
                    Assert.True(offCard, $"control: with the processor chosen the NPC over {backdrop.Name} was still drawn on the card");
                }

                tabs.SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.First();
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                // A still place is put through the look again only when it or what is
                // drawn changes: the light streaks (drawn at Ultra) ticked off and on.
                PreviewQualityManager.Apply(PreviewQualityManager.All.First(q => q.Name == "Ultra"));
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Card"));
                place.LookFlares = false;
                int before = place.LookPasses;
                bool lookOnCard = Until(() => place.LookPasses > before && place.LookOnCard);
                PreviewRendererManager.Apply(PreviewRendererManager.All.First(d => d.Name == "Processor"));
                place.LookFlares = true;
                before = place.LookPasses;
                bool lookOffCard = Until(() => place.LookPasses > before && !place.LookOnCard);
                results.Add($"the place's look: on the card {lookOnCard}, then on the processor {lookOffCard}");
                foreach (var r in results) _out.WriteLine(r);
                Assert.True(lookOnCard, "with the card chosen the place's look was not worked out on it");
                Assert.True(lookOffCard, "control: with the processor chosen the place's look was still worked out on the card");
            });
        }
        finally
        {
            PreviewRendererManager.Apply(wasRenderer);
            PreviewBackdropManager.Apply(wasBackdrop);
            PreviewQualityManager.Apply(wasQuality);
        }
    }
}
