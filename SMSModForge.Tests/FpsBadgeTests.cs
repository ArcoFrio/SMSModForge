using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Options ▸ Show preview frame rate (1.9.1, the author: "a toggle to show
/// preview FPS, that defaults to false"): off to start with and no badge on
/// the bust preview; on, a badge whose rate is the pictures the preview put on
/// screen over the same time; off again, gone.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class FpsBadgeTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-fps-" + Guid.NewGuid().ToString("N"));
    private readonly bool _was = EditorPrefs.ShowPreviewFps;

    public FpsBadgeTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }

    public void Dispose()
    {
        FpsBadge.Shown = _was;
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static FpsBadge? BadgeOn(JigglePreview preview)
        => AdornerLayer.GetAdornerLayer(preview)?.GetAdorners(preview)?.OfType<FpsBadge>().FirstOrDefault();

    [Fact]
    public void OffByDefault_OnItShowsTheRateDrawn_OffAgainItIsGone()
    {
        Assert.False(new MainViewModel().ShowPreviewFps, "the frame rate is shown before anybody asked for it");
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "anna.png"), 256, 256, Color.FromRgb(0xC0, 0x80, 0x60));
        FakeSceneCatalog.WritePlain(Path.Combine(_dir, "Busts", "mask.png"), 256, 256, Color.FromRgb(200, 100, 50));
        var pack = PackRepository.CreateEmpty("fps.pack");
        var anna = new CharacterDef { Key = "anna", Name = "Anna", DisplayName = "Anna" };
        anna.Outfits.Add(new OutfitDef { Key = "casual", GameObjectName = "Anna_Casual", BaseSprite = "Busts/anna.png", MaskSprite = "Busts/mask.png" });
        pack.Characters.Add(anna);
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Characters;
            WindowHarness.Pump();
            vm.SelectedCharacter = vm.Characters.First(c => c.Model.Key == "anna");
            vm.SelectedOutfit = vm.SelectedCharacter.Outfits.First();
            var preview = (JigglePreview)window.FindName("PreviewControl");
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
            Assert.Null(BadgeOn(preview));

            vm.ShowPreviewFps = true;
            WindowHarness.Pump();
            var badge = BadgeOn(preview);
            Assert.True(badge != null, "no frame rate on the preview with the option on");
            int before = preview.FramesShown;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            WindowHarness.Wait(TimeSpan.FromMilliseconds(1600));
            double drawn = (preview.FramesShown - before) / clock.Elapsed.TotalSeconds;
            _out.WriteLine($"the badge says {badge!.Rate:0.0} a second; the preview put {drawn:0.0} a second on screen");
            Assert.True(badge.Rate > 5, "the badge shows no frames drawn");
            Assert.InRange(badge.Rate, drawn * 0.6, drawn * 1.4 + 2);

            vm.ShowPreviewFps = false;
            WindowHarness.Pump();
            Assert.Null(BadgeOn(preview));
        });
    }
}
