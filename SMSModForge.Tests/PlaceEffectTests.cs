using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Effects in places (1.10.0, the author: "Do you think we could implement those options for Places too?"): "+ Effect"
/// makes an object of the place that plays an effect; the place preview draws it moving at the object - measured on
/// the picture, with the object's effect taken off as the control; Ctrl+Z takes it back; the panel beside the
/// picture changes it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceEffectTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-placefx-" + Guid.NewGuid().ToString("N"));

    public PlaceEffectTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static IEnumerable<T> Under<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Under<T>(c)) yield return d;
        }
    }

    /// <summary>Pixels the effects layer draws (not clear).</summary>
    private static int Drawn(Canvas layer)
    {
        layer.UpdateLayout();
        var bmp = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(layer);
        var px = new byte[1920 * 1080 * 4];
        bmp.CopyPixels(px, 1920 * 4, 0);
        int n = 0;
        for (int i = 3; i < px.Length; i += 4) if (px[i] > 4) n++;
        return n;
    }

    [Fact]
    public void AnEffectMadeInAPlacePlaysAtItsObjectInThePreview_AndCtrlZTakesItBack()
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            WindowHarness.Pump();
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            try
            {
                var place = vm.Places.First();
                vm.SelectedPlace = place;
                WindowHarness.Pump();
                var preview = Under<PlacePreview>(window).First(p => p.IsVisible);
                vm.Undo.Checkpoint();
                string before = PackRepository.Serialize(vm.Pack);

                // Control: no effect of the place's yet, nothing drawn on its layer.
                Assert.Equal(0, preview.EffectLayer.Children.Count);

                preview.AddEffectHere();
                WindowHarness.Pump();
                vm.Undo.Checkpoint();
                var made = place.GameObjects.Last(o => o.HasEffect);
                Assert.Equal("Effect", made.Name);
                Assert.Equal(Shared.Effects.VanillaPrefix + "fog", made.Model.Effect!.Template);
                Assert.Single(preview.EffectLayer.Children.OfType<EffectPreview>());

                // The fog, drawn on the layer; then a campfire - and none once the effect is taken off.
                var view = preview.EffectLayer.Children.OfType<EffectPreview>().Single();
                _out.WriteLine($"view {view.ActualWidth}x{view.ActualHeight} at {view.AtX:0.###},{view.AtY:0.###}, {view.Alive} out, effect {view.Effect?.Template}, layer {preview.EffectLayer.ActualWidth}x{preview.EffectLayer.ActualHeight} visible {preview.EffectLayer.IsVisible}");
                int fog = Drawn(preview.EffectLayer);
                made.Effect!.TemplateOption = EffectViewModel.TemplateOptions.First(t => t.Id == Shared.Effects.VanillaPrefix + "campfire");
                WindowHarness.Pump();
                int fire = Drawn(preview.EffectLayer);
                _out.WriteLine($"fog {fog} px, campfire {fire} px");
                Assert.True(fog > 50, "the place's fog drew nothing");
                Assert.True(fire > 20, "the place's campfire drew nothing");

                // Ctrl+Z, twice: the template back, then the object gone - and nothing drawn.
                vm.Undo.Checkpoint();
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                WindowHarness.Pump();
                var still = place.GameObjects.LastOrDefault(o => o.Name == "Effect");
                _out.WriteLine($"one Ctrl+Z ({vm.LastRestore}): {still?.Model.Effect?.Template ?? "(gone)"}");
                Assert.Equal(Shared.Effects.VanillaPrefix + "fog", still?.Model.Effect?.Template);
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                WindowHarness.Pump();
                _out.WriteLine($"two ({vm.LastRestore}): {place.GameObjects.LastOrDefault(o => o.Name == "Effect")?.Model.Effect?.Template ?? "(gone)"}");

                Assert.Equal(before, PackRepository.Serialize(vm.Pack));
                WindowHarness.Pump();
                Assert.Empty(preview.EffectLayer.Children);
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    [Fact]
    public void AnObjectsEffectIsSavedWithTheObject_AndOnlyItsTemplateWhenNothingIsChanged()
    {
        var o = new GameObjectDef { Name = "Hearth", Effect = EffectDef.ForAPlace(Shared.Effects.VanillaPrefix + "campfire") };
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(o);
        Assert.Contains("\"effect\":{\"template\":\"vanilla:campfire\"}", json);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<GameObjectDef>(json)!;
        Assert.Equal("vanilla:campfire", back.Effect?.Template);
        // Control: an object with no effect writes none.
        Assert.DoesNotContain("\"effect\"", Newtonsoft.Json.JsonConvert.SerializeObject(new GameObjectDef { Name = "Plain" }));
    }
}
