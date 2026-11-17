using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Preview quality is how much of the game's look the previews draw, and
/// every quality draws as often as the display shows a frame (1.9.1, the
/// author: "we'd want them all to capped at display refresh rate, just
/// reducing how much of a load it puts on the pc in general"). Until then a
/// quality was a frame rate - 60, 30, 20, 15 a second - over the same picture.
/// Here: each level leaves out more than the one above (Ultra all of it), and
/// the bust, NPC and place previews in the window draw at the display's rate
/// at every quality, never more, the lightest in full.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
[Trait("Timing", "Alone")]  // times the previews' frames: run by a tester alone (run-tests.ps1)
public sealed class PreviewQualityTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-quality-" + Guid.NewGuid().ToString("N"));

    public PreviewQualityTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static IEnumerable<T> Descendants<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var d in Descendants<T>(child)) yield return d;
        }
    }

    [Fact]
    public void EachLevelLeavesOutMoreOfTheGamesLookThanTheOneAbove()
    {
        Assert.Equal(new[] { "Ultra", "High", "Medium", "Low" }, PreviewQualityManager.All.Select(q => q.Name));
        static string[] Parts(PreviewQualityDef q) => new[] { (q.EdgeGlow, "glow"), (q.EdgeGlowExact, "exact glow"), (q.Bloom, "bloom"), (q.Flares, "flares"), (q.Colour, "colour"),
                                                              // With no backdrop (1.9.1): how finely a bust or an NPC alone is sampled.
                                                              (q.Sampling.Across > 1, "two samples across"), (q.Sampling.Down > 1, "two rows"), (q.Sampling.Step == 1, "every pixel") }
                                                      .Where(p => p.Item1).Select(p => p.Item2).ToArray();
        var ultra = PreviewQualityManager.All[0];
        Assert.Equal(new[] { "glow", "exact glow", "bloom", "flares", "colour", "two samples across", "two rows", "every pixel" }, Parts(ultra));
        Assert.Equal(SMSModForge.Rendering.JiggleShader.Sampling.Game, ultra.Sampling);
        for (int i = 1; i < PreviewQualityManager.All.Count; i++)
        {
            var above = Parts(PreviewQualityManager.All[i - 1]);
            var here = Parts(PreviewQualityManager.All[i]);
            _out.WriteLine(PreviewQualityManager.All[i].Name + ": " + string.Join(", ", here));
            Assert.True(here.All(above.Contains) && here.Length < above.Length,
                        PreviewQualityManager.All[i].Name + " does not leave out more than " + PreviewQualityManager.All[i - 1].Name);
        }
    }

    [Fact]
    public void TheBustNpcAndPlacePreviewsDrawAtTheDisplaysRateAtEveryLevel()
    {
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, Color.FromRgb(0xC0, 0x80, 0x60));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "mask.png"), 256, 256, Color.FromRgb(200, 100, 50));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "guard.png"), 200, 400, Color.FromRgb(0x70, 0x60, 0x50));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Npcs", "mask.png"), 256, 256, Color.FromRgb(200, 100, 50));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "front.png"), 256, 144, Color.FromRgb(0x30, 0x50, 0x70));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Places", "mask.png"), 256, 256, Color.FromRgb(0xFF, 0xFF, 0xFF));
        var pack = PackRepository.CreateEmpty("quality.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png", MaskSprite = "Busts/mask.png" });
        pack.Characters.Add(anna);
        pack.Npcs.Add(new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Npcs/guard.png", Mask = "Npcs/mask.png" });
        pack.Places.Add(new PlaceDef { Key = "cave", InternalName = "Cave", DisplayName = "Cave",
                                       BaseSprite = "Places/front.png", MaskSprite = "Places/mask.png" });
        PackRepository.Save(pack, _dir);

        var was = PreviewQualityManager.Current;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                vm.OpenPackFromPath(_dir);
                var tabs = (TabControl)window.FindName("MainTabs");

                tabs.SelectedIndex = MainTab.Characters;
                WindowHarness.Pump();
                vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
                vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
                WindowHarness.Pump();
                var bust = (JigglePreview)window.FindName("PreviewControl");
                Check("bust", bust, () => bust.ShaderPasses);

                tabs.SelectedIndex = MainTab.Npcs;
                WindowHarness.Pump();
                vm.SelectedNpc = vm.Npcs.First();
                WindowHarness.Pump();
                window.UpdateLayout();
                var npc = Descendants<NpcPreview>(window).Single(n => n.IsVisible);
                Check("NPC", npc, () => npc.ShaderPasses);

                tabs.SelectedIndex = MainTab.Places;
                WindowHarness.Pump();
                vm.SelectedPlace = vm.Places.First();
                WindowHarness.Pump();
                ((CheckBox)window.FindName("PlaceShaders")).IsChecked = true;
                WindowHarness.Pump();
                var place = (PlacePreview)window.FindName("PlacePreviewMain");
                WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
                Assert.True(place.ShaderClockRunning, "the place's shaders are not running (and so this proves nothing)");
                Check("place", place, () => place.ShaderSteps);
            });
        }
        finally { PreviewQualityManager.Apply(was); }
    }

    private void Check(string preview, System.Windows.Media.Visual element, Func<int> drawn)
    {
        // The rate the preview itself paces to: its window's monitor's.
        int display = SMSModForge.Rendering.DisplayRefresh.RateOf(element);
        var rate = new Dictionary<string, double>();
        foreach (var quality in PreviewQualityManager.All)
        {
            PreviewQualityManager.Apply(quality);
            WindowHarness.Wait(TimeSpan.FromMilliseconds(300));
            int before = drawn();
            var clock = Stopwatch.StartNew();
            WindowHarness.Wait(TimeSpan.FromSeconds(2));
            rate[quality.Name] = (drawn() - before) / clock.Elapsed.TotalSeconds;
            _out.WriteLine($"{preview} at {quality.Name}: {rate[quality.Name]:0.0} drawn a second (the display: {display})");
            Assert.True(rate[quality.Name] <= display * 1.05 + 0.5,
                $"{preview} drew {rate[quality.Name]:0.0} a second at {quality.Name}, more than the display's {display}");
        }
        // The lightest level keeps up with the display; none is held below it
        // on purpose any more (the old Low drew 15 a second, the others 20 to 30).
        // 70% of it: the place preview's own work, which no level changes, holds
        // it at 59-70 a second on the author's 75 Hz monitor (2026-11-17, with
        // other work on the PC) - and every old cap is under half of any display.
        Assert.True(rate["Low"] >= display * 0.7, $"{preview} drew only {rate["Low"]:0.0} a second at Low, the display {display}");
        // Low not held below Ultra - where Ultra's work holds it under the display; where
        // both keep up with the display the two differ only by its pacing (the place
        // preview, whose work no quality changes: 61 and 70 a second, 2026-11-17).
        if (rate["Ultra"] < display * 0.8)
            Assert.True(rate["Low"] >= rate["Ultra"] * 0.9, $"{preview}: Low ({rate["Low"]:0.0}) draws fewer frames than Ultra ({rate["Ultra"]:0.0})");
    }
}
