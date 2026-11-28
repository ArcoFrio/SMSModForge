using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// A floating picture in the place preview (after 1.10.0, the author: "monsters in vats ... move up and down in a smooth
/// way. Please add it with editable parameters so we can change how the movement looks"): an object with a
/// FloatingSprite of Height 1 rises from its lowest to its highest by one of its units as drawn - two thirds of the
/// drawn distance to its child, which stands 1.5 of them above it - and carries that child with it; control: an
/// object beside it without one stays where it is. Measured on where the preview draws them.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceFloatPreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-float-" + Guid.NewGuid().ToString("N"));

    public PlaceFloatPreviewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static void Png(string path, int w, int h, Color c)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = c.B; px[i + 1] = c.G; px[i + 2] = c.R; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void Settle(Window window)
    {
        WindowHarness.Pump();
        window.UpdateLayout();
        WindowHarness.Pump();
    }

    [Fact]
    public void A_floating_object_rises_and_falls_by_its_height_and_carries_its_children()
    {
        Png(Path.Combine(_dir, "Locations", "Lab.png"), 64, 36, Color.FromRgb(0x30, 0x38, 0x40));
        Png(Path.Combine(_dir, "Locations", "Monster.png"), 60, 100, Color.FromRgb(0x50, 0xA0, 0x50));
        var pack = PackRepository.CreateEmpty("float");
        var place = new PlaceDef { Key = "lab", InternalName = "Lab", BaseSprite = "Locations/Lab.png" };
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "Monster", Sprite = "Locations/Monster.png", X = -2f, SortingOrder = 5,
            Components = { new ComponentDef { Type = PackComponentType.FloatingSprite, FloatHeight = 1f, FloatPeriod = 2f, FloatStart = 0f, FloatRandomStart = false } },
            Children = { new GameObjectDef { Name = "Bubble", Sprite = "Locations/Monster.png", Y = 1.5f, SortingOrder = 6 } },
        });
        place.GameObjects.Add(new GameObjectDef { Name = "Vat", Sprite = "Locations/Monster.png", X = 2f, SortingOrder = 5 });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            window.Width = 1700;
            window.Height = 1000;
            var vm = (MainViewModel)window.DataContext;
            try
            {
                vm.OpenPackFromPath(_dir);
                Settle(window);
                ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
                Settle(window);
                vm.SelectedPlace = vm.Places.Single();
                Settle(window);
                var preview = (PlacePreview)window.FindName("PlacePreviewMain");
                double t = 0;
                preview.AnimClockForTests = () => t;
                // Where an object's picture is drawn on the preview's canvas: its top and its height.
                (double Top, double Height, double Left) At(string name)
                {
                    var img = preview.ObjectImageFor(name)!;
                    var a = img.TranslatePoint(new Point(0, 0), preview.SceneCanvas);
                    var b = img.TranslatePoint(new Point(0, img.Height), preview.SceneCanvas);
                    return (Math.Min(a.Y, b.Y), Math.Abs(b.Y - a.Y), a.X);
                }
                (double, double, double) Moment(double seconds, string name)
                {
                    t = seconds;
                    preview.AnimateAtForTests(t);
                    Settle(window);
                    return At(name);
                }
                Assert.True(preview.AnimatedElements >= 2, $"only {preview.AnimatedElements} elements float - the object and its child should");
                var top = Moment(0.5, "Monster");       // a quarter in: at its highest
                var bottom = Moment(1.5, "Monster");    // three quarters: its lowest
                var childTop = Moment(0.5, "Bubble");
                var childBottom = Moment(1.5, "Bubble");
                var vatTop = Moment(0.5, "Vat");
                var vatBottom = Moment(1.5, "Vat");
                double rise = bottom.Item1 - top.Item1;
                // One of the monster's units as drawn: its child stands 1.5 of them above it.
                double unit = Math.Abs(top.Item1 - childTop.Item1) / 1.5;
                _out.WriteLine($"the monster: its top at {top.Item1:0.#} at its highest and {bottom.Item1:0.#} at its lowest - {rise:0.#} apart, a unit drawn {unit:0.#}; "
                               + $"its child {childBottom.Item1 - childTop.Item1:0.#} apart; the vat {vatBottom.Item1 - vatTop.Item1:0.#}");
                Assert.True(unit > 10, "the child is not drawn above the monster - the unit cannot be measured");
                Assert.True(Math.Abs(rise - unit) < 1, $"the monster rises {rise:0.#} canvas pixels, a unit of it is {unit:0.#}");
                Assert.True(Math.Abs(top.Item3 - bottom.Item3) < 0.01, "the monster sways though its sideways is 0");
                Assert.True(Math.Abs((childBottom.Item1 - childTop.Item1) - rise) < 0.5, "its child is not carried with it");
                Assert.True(Math.Abs(vatBottom.Item1 - vatTop.Item1) < 0.01, "control: the vat, which does not float, moves too");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
