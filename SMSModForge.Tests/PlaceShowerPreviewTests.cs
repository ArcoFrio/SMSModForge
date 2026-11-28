using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Outline = System.Windows.Shapes.Path;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using Path = System.IO.Path;

namespace SMSModForge.Tests;

/// <summary>
/// A placed character's shower in the place preview (after 1.10.0, the author: "the shower one might actually be the
/// one that resembles an actual shower ... apply the top and bottom settings to that one"; "I want them to disappear
/// abruptly at the floor of the box"; "tweak some of the key settings of the particle effect, including color"),
/// measured from what the preview draws: its box outlined at the shower's size (its own 1.053 in it); with "Drops stop
/// at the box's floor", no drop drawn under the box - control: without it, drops are; tinted red, the drops drawn red -
/// control: untinted, they are the game's blue-grey. The game holds the preview's shower to its own: InGameSuiteTests.S21.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceShowerPreviewTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-shower-" + Guid.NewGuid().ToString("N"));

    public PlaceShowerPreviewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private void Png(string rel, int w, int h, byte grey)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = grey; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using var fs = File.Create(Path.Combine(_dir, rel));
        enc.Save(fs);
    }

    private const int CanvasW = 1920, CanvasH = 1080;

    /// <summary>The sprays' own drawings, at the canvas's size (a canvas pixel a pixel), premultiplied BGRA.</summary>
    private static byte[] Drops(PlacePreview preview)
    {
        var layer = (Canvas)typeof(PlacePreview).GetField("_wetLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        var rtb = new RenderTargetBitmap(CanvasW, CanvasH, 96, 96, PixelFormats.Pbgra32);
        foreach (System.Windows.UIElement child in layer.Children)
        {
            child.InvalidateVisual();
            child.UpdateLayout();
            rtb.Render(child);
        }
        var px = new byte[CanvasW * CanvasH * 4];
        rtb.CopyPixels(px, CanvasW * 4, 0);
        return px;
    }

    [Fact]
    public void A_shower_stops_at_its_floor_and_takes_its_tint()
    {
        Png("Art/base.png", 2048, 1136, 40);
        Png("Art/back.png", 2048, 1136, 30);
        Png("Art/npc.png", 220, 420, 90);
        var pack = PackRepository.CreateEmpty("shower.pack");
        var guard = new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Art/npc.png" };
        guard.Wet.Enabled = true;
        guard.Wet.StartActive = true;
        pack.Npcs.Add(guard);
        var place = new PlaceDef { Key = "p", InternalName = "P", DisplayName = "P", BaseSprite = "Art/base.png", SecondarySprite = "Art/back.png" };
        new PlaceViewModel(place);   // its NPCs node
        place.GameObjects.Single(g => g.IsNpcRoot).Npcs.Add(new NpcPlacementDef
        {
            Npc = "guard", Name = "Guard", Body = new NpcTransform { X = 0, Y = -1.5f },
            Wet = new NpcTransform { X = 0, Y = 4, ScaleX = 1.2f, ScaleY = 1.2f },
            WetEffect = new WetEffectDef { Kind = WetEffectDef.KindShower, Width = 3, Height = 3, FromTop = true, Floor = true },
        });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);

        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.First();
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var placed = vm.SelectedPlace!.NpcsNode!.Npcs.Single();
            var gizmo = (Canvas)typeof(PlacePreview).GetField("_gizmoLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            try
            {
                Assert.True(preview.Pick(placed), "the guard is not in the picture");
                WindowHarness.Pump();
                preview.SetPart(PlacePreview.GizmoPart.Wet);
                WindowHarness.Pump();

                // The outline: the shower's box, 3 x 3 times the Wet part's scale and the shower's own 1.053.
                var box = gizmo.Children.OfType<Outline>().Single(p => p.Stroke is SolidColorBrush { Color: { R: 0x9F, G: 0xE0, B: 0xFF } }).Data.Bounds;
                _out.WriteLine($"the box {box}");
                Assert.Equal(3 * 1.2 * WetDrops.ShowerOwnScale * 100, box.Width, 1);
                Assert.Equal(3 * 1.2 * WetDrops.ShowerOwnScale * 100, box.Height, 1);

                // Drawn: no drop under the floor, none above the top but by half its length; then, the floor off, drops under it.
                (int Below, int Above, int Drawn, double R, double B) Measure()
                {
                    var px = Drops(preview);
                    int below = 0, above = 0, drawn = 0;
                    double r = 0, b = 0;
                    double tallest = WetDrops.Shower.SizeYMin * 1.2 * WetDrops.ShowerOwnScale * 100;
                    for (int y = 0; y < CanvasH; y++)
                        for (int x = (int)box.Left; x <= (int)box.Right; x++)
                        {
                            int i = (y * CanvasW + x) * 4;
                            if (px[i + 3] == 0) continue;
                            drawn++;
                            r += px[i + 2] / (double)px[i + 3]; b += px[i] / (double)px[i + 3];
                            if (y > box.Bottom + 1) below++;
                            if (y < box.Top - tallest / 2 - 1) above++;
                        }
                    return (below, above, drawn, drawn == 0 ? 0 : r / drawn, drawn == 0 ? 0 : b / drawn);
                }
                var withFloor = Measure();
                placed.WetFloor = false;
                WindowHarness.Pump();
                var without = Measure();
                placed.WetFloor = true;
                placed.WetTint = "#FF3030";
                WindowHarness.Pump();
                var tinted = Measure();
                _out.WriteLine($"with the floor: {withFloor.Drawn} drop pixels, {withFloor.Below} under it, {withFloor.Above} over the top; red {withFloor.R:0.00} blue {withFloor.B:0.00}");
                _out.WriteLine($"without: {without.Drawn} drop pixels, {without.Below} under the box");
                _out.WriteLine($"tinted #FF3030: red {tinted.R:0.00} blue {tinted.B:0.00}");
                Assert.True(withFloor.Drawn > 100, "the shower draws too little to tell");
                Assert.Equal(0, withFloor.Below);
                Assert.Equal(0, withFloor.Above);
                Assert.True(without.Below > 50, "control: without the floor no drop is drawn under the box either");
                Assert.True(withFloor.B > withFloor.R, "control: the game's drops are not blue-grey");
                Assert.True(tinted.R > tinted.B * 2, $"tinted red, the drops are drawn red {tinted.R:0.00} against blue {tinted.B:0.00}");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
