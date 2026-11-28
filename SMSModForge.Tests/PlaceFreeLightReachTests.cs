using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;
using Polygon = System.Windows.Shapes.Polygon;
using ShapePath = System.Windows.Shapes.Path;
using Path = System.IO.Path;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A free light shines out from its whole shape as far as its reach (after 1.10.0, the author: "a hybrid round
/// approach, where we shape how the source is going to be, and it emits outward like a round light (respecting the
/// shape of the origin)"), and the preview shows how far: a dashed line round the shape where its light is gone, and
/// a knob on it to drag. Measured on what is drawn - the line's place against where the look's own light ends, on
/// every side - and the knob dragged as a hand drags it: taken, it moves nothing; drawn out, the line follows the
/// pointer.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceFreeLightReachTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-reach-" + Guid.NewGuid().ToString("N"));

    public PlaceFreeLightReachTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private void WritePack()
    {
        string art = Path.Combine(_dir, "Places", "grey.png");
        Directory.CreateDirectory(Path.GetDirectoryName(art)!);
        int w = 2048, h = 1136;
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = 90; px[i + 1] = 90; px[i + 2] = 90; px[i + 3] = 255; }
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4)));
        using (var fs = File.Create(art)) enc.Save(fs);
        var pack = PackRepository.CreateEmpty("reach.pack");
        pack.Places.Add(new PlaceDef { Key = "hall", InternalName = "Hall", DisplayName = "Hall", BaseSprite = "Places/grey.png" });
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b) => ((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)!).Invoke();

    /// <summary>The dashed reach line as drawn, on the canvas.</summary>
    private static Rect ReachDrawn(PlacePreview preview)
        => Field<Canvas>(preview, "_gizmoLayer").Children.OfType<ShapePath>()
               .Single(p => p.StrokeDashArray.Count == 2 && p.StrokeDashArray[0] == 5).Data.Bounds;

    /// <summary>The free shape's own outline as drawn (the solid line through its corners).</summary>
    private static Polygon Outline(PlacePreview preview, LightViewModel light)
        => Field<Canvas>(preview, "_gizmoLayer").Children.OfType<Polygon>()
               .Single(p => !p.IsHitTestVisible && p.Opacity == 1 && p.StrokeThickness == 3 && p.Points.Count == light.Points.Count);

    /// <summary>The look's free light: how strong it is at a canvas point (its shape's alpha, as the look lights by it).</summary>
    private static float LitAt(PlacePreview preview, double cx, double cy)
    {
        var shaped = preview.PlaceLightsNow!.Lights.Single(l => l.Shape != null).Shape!;
        return shaped.Alpha((float)(cx * Rendering.PlaceLights.ScreenW / 1920.0), (float)((1080 - cy) * Rendering.PlaceLights.ScreenH / 1080.0));
    }

    [Fact]
    public void TheReachIsDrawnWhereTheLightEnds_AndItsKnobDragsIt()
    {
        WritePack();
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            vm.SelectedPlace = vm.Places.Single();
            WindowHarness.Pump();
            var place = vm.SelectedPlace!;
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            preview.AddLightHere();
            WindowHarness.Pump();
            var lamp = place.GameObjects.Single(g => g.Name == "Light");
            Click(Field<Button>(preview, "_shapeFree"));
            WindowHarness.Pump();
            var light = lamp.Light!;
            Assert.True(light.IsFree);
            _out.WriteLine($"the free light: {light.Points.Count} corners, reach {light.Edge}");

            // The line against the look's light, on every side: lit just inside it, nothing just outside.
            var reach = ReachDrawn(preview);
            var shape = Outline(preview, light).Points;
            double shapeRight = shape.Max(p => p.X);
            double midX = (reach.Left + reach.Right) / 2, midY = (reach.Top + reach.Bottom) / 2;
            _out.WriteLine($"the shape's right side at {shapeRight:F0}, its reach drawn {reach.Left:F0}..{reach.Right:F0} x {reach.Top:F0}..{reach.Bottom:F0}");
            Assert.True(reach.Right - shapeRight > 10, "control: the reach is drawn on the shape itself - nothing to tell apart");
            Assert.True(LitAt(preview, shapeRight + 3, midY) > 0, "control: the light ends at its shape - the reach is not lit");
            foreach (var (name, inside, outside) in new[]
            {
                ("right", new Point(reach.Right - 3, midY), new Point(reach.Right + 3, midY)),
                ("left", new Point(reach.Left + 3, midY), new Point(reach.Left - 3, midY)),
                ("top", new Point(midX, reach.Top + 3), new Point(midX, reach.Top - 3)),
                ("bottom", new Point(midX, reach.Bottom - 3), new Point(midX, reach.Bottom + 3)),
            })
            {
                float lit = LitAt(preview, inside.X, inside.Y), gone = LitAt(preview, outside.X, outside.Y);
                _out.WriteLine($"{name}: just inside the line lit {lit:F3}, just outside {gone:F3}");
                Assert.True(lit > 0, $"the light ends before its reach line on the {name}");
                Assert.Equal(0f, gone);
            }

            // A short reach: the knob stands out from its corner, and taking it moves nothing.
            light.Edge = 0.1f;
            WindowHarness.Pump();
            var hit = Field<Canvas>(preview, "_gizmoLayer").Children.OfType<Ellipse>()
                .Single(e => e.Fill == Brushes.Transparent && e.ToolTip as string == Loc.T("preview.light.edge.drag.tip"));
            var knob = new Point(Canvas.GetLeft(hit) + hit.Width / 2, Canvas.GetTop(hit) + hit.Height / 2);
            double before = ReachDrawn(preview).Right;
            var corner = Outline(preview, light).Points.OrderByDescending(p => p.X).First();
            var outward = knob - corner;
            outward.Normalize();
            preview.BeginHandle(PlacePreview.GizmoHandle.LightEdge, knob);
            preview.DragHandleTo(knob);
            WindowHarness.Pump();
            Assert.Equal(0.1f, light.Edge);

            // Drawn 40 canvas px further out: the line follows the pointer.
            preview.DragHandleTo(knob + outward * 40);
            preview.EndHandle();
            WindowHarness.Pump();
            double after = ReachDrawn(preview).Right;
            _out.WriteLine($"dragged 40 px out: the reach {light.Edge}, its line's right side {before:F1} -> {after:F1}");
            Assert.InRange(after - before, 38.5, 41.5);
        });
    }
}
