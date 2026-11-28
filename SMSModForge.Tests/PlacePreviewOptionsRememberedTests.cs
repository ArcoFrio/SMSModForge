using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Preview parallax and Apply shaders are remembered (after 1.10.0, the author: "Currently the shaders and parallax
/// preview buttons always start unchecked ... They should start as inactive, but if an user checks them it should
/// stay active across all places and modpacks, and vice versa"). Ticked on one place, they are ticked on every
/// other place, on a game's level, and on the pack opened again - the tick boxes as drawn and what the preview does;
/// unticked anywhere, off everywhere.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlacePreviewOptionsRememberedTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-previewopts-" + Guid.NewGuid().ToString("N"));

    public PlacePreviewOptionsRememberedTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    /// <summary>A tick box ticked or unticked as a click does it: through its own toggling, which its binding hears.</summary>
    private static void Click(ToggleButton box)
        => ((System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(box)).Toggle();

    [Fact]
    public void ParallaxAndShaders_AreKeptAcrossPlacesLevelsAndPacks()
    {
        var pack = PackRepository.CreateEmpty("previewopts");
        pack.Places.Add(new PlaceDef { Key = "first", InternalName = "First" });
        pack.Places.Add(new PlaceDef { Key = "second", InternalName = "Second" });
        pack.VanillaExtensions.Add(new VanillaPlaceExtensionDef { Source = "vanilla:26_Downtown" });
        PackRepository.Save(pack, _dir);

        bool wasParallax = EditorPrefs.PlacePreviewParallax, wasShaders = EditorPrefs.PlacePreviewShaders;
        EditorPrefs.PlacePreviewParallax = false;
        EditorPrefs.PlacePreviewShaders = false;
        try
        {
            WindowHarness.Run(window =>
            {
                var vm = (MainViewModel)window.DataContext;
                try
                {
                    vm.OpenPackFromPath(_dir);
                    WindowHarness.Pump();
                    ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                    vm.SelectedPlace = vm.Places[0];
                    Settle(window);
                    var placeParallax = (CheckBox)window.FindName("PlaceParallaxPreview");
                    var placeShaders = (CheckBox)window.FindName("PlaceShaders");
                    var levelParallax = (CheckBox)window.FindName("ExtParallaxPreview");
                    var levelShaders = (CheckBox)window.FindName("ExtShaders");
                    var placePreview = (PlacePreview)window.FindName("PlacePreviewMain");
                    var levelPreview = (PlacePreview)window.FindName("ExtPreview");
                    Assert.False(placeParallax.IsChecked == true || placeShaders.IsChecked == true, "unticked to begin with, and ticked");

                    Click(placeParallax);
                    Click(placeShaders);
                    Settle(window);
                    Assert.True(placePreview.ParallaxPreview && placePreview.Shaders, "ticked, and the preview does not do it");

                    vm.SelectedPlace = vm.Places[1];
                    Settle(window);
                    _out.WriteLine($"the second place: parallax {placeParallax.IsChecked}, shaders {placeShaders.IsChecked}");
                    Assert.True(placeParallax.IsChecked == true && placeShaders.IsChecked == true, "the second place does not keep them");
                    Assert.True(placePreview.ParallaxPreview && placePreview.Shaders);

                    vm.SelectedVanillaExtension = vm.VanillaExtensions[0];
                    Settle(window);
                    _out.WriteLine($"the game's level: parallax {levelParallax.IsChecked}, shaders {levelShaders.IsChecked}");
                    Assert.True(levelParallax.IsChecked == true && levelShaders.IsChecked == true, "the game's level does not keep them");
                    Assert.True(levelPreview.ParallaxPreview && levelPreview.Shaders);

                    // The pack opened again: still ticked.
                    typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
                    vm.OpenPackFromPath(_dir);
                    WindowHarness.Pump();
                    vm.SelectedPlace = vm.Places[0];
                    Settle(window);
                    Assert.True(placeParallax.IsChecked == true && placeShaders.IsChecked == true, "the pack opened again does not keep them");
                    Assert.True(EditorPrefs.PlacePreviewParallax && EditorPrefs.PlacePreviewShaders, "not kept for the next session");

                    // Unticked on the game's level: off on the place too (the control: they follow either way).
                    vm.SelectedVanillaExtension = vm.VanillaExtensions[0];
                    Settle(window);
                    Click(levelShaders);
                    Settle(window);
                    vm.SelectedPlace = vm.Places[0];
                    Settle(window);
                    Assert.False(placeShaders.IsChecked == true, "unticked on the game's level, still ticked on the place");
                    Assert.True(placeParallax.IsChecked == true, "the other box went with it");
                    Assert.False(placePreview.Shaders);
                    Assert.False(EditorPrefs.PlacePreviewShaders);
                }
                finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
            });
        }
        finally
        {
            EditorPrefs.PlacePreviewParallax = wasParallax;
            EditorPrefs.PlacePreviewShaders = wasShaders;
        }
    }
}
