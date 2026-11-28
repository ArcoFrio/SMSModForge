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
/// A character's Wet part, picked in the place preview (after 1.10.0, the author):
/// <list type="bullet">
/// <item>"When selecting the wet effect on NPCs, can we get a box outline of its bounds?" - the game's box, 3.21 by
/// 8.45 units times the Wet object's own scale (the game leaves its parents' scale out - InGameSuiteTests.S17), round
/// the Wet object's handle.</item>
/// <item>"Might also be helpful to outline the actual droplets when we have the wet GO selected as well" - a ring round
/// each drop drawn, clear of the drop itself.</item>
/// <item>"we could be able to just drag the edges of the effect ... just pulling the actual side clicked ... not actually
/// changing the droplet size by doing that, just the area of the effect" - a side dragged moves that side alone.</item>
/// </list>
/// Controls: the body picked, no box and no rings; the rings looked for where the drops are not, not found.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceWetOutlineTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-wetbox-" + Guid.NewGuid().ToString("N"));

    public PlaceWetOutlineTests(ITestOutputHelper o)
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

    /// <summary>A place with one wet guard: scaled up, with the Wet object scaled on its own - the box has the Wet
    /// object's scale and not the body's.</summary>
    private void MakePack()
    {
        Png("Art/base.png", 2048, 1136, 40);
        Png("Art/back.png", 2048, 1136, 30);
        Png("Art/npc.png", 220, 420, 90);
        var pack = PackRepository.CreateEmpty("wetbox.pack");
        var guard = new NpcDef { Key = "guard", DisplayName = "Guard", Sprite = "Art/npc.png" };
        guard.Wet.Enabled = true;
        guard.Wet.StartActive = true;
        pack.Npcs.Add(guard);
        var place = new PlaceDef { Key = "p", InternalName = "P", DisplayName = "P", BaseSprite = "Art/base.png", SecondarySprite = "Art/back.png" };
        new PlaceViewModel(place);   // its NPCs node
        place.GameObjects.Single(g => g.IsNpcRoot).Npcs.Add(new NpcPlacementDef
        {
            Npc = "guard", Name = "Guard", Body = new NpcTransform { X = 3, Y = -1.5f, ScaleX = 1.5f, ScaleY = 1.5f },
            Wet = new NpcTransform { X = 0, Y = 2, ScaleX = 0.5f, ScaleY = 0.4f },
        });
        pack.Places.Add(place);
        PackRepository.Save(pack, _dir);
    }

    private sealed record Opened(MainWindow Window, MainViewModel Vm, PlacePreview Preview, NpcPlacementViewModel Placed, Canvas Gizmo);

    private void Run(Action<Opened> body)
    {
        MakePack();
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
            try { body(new Opened(window, vm, preview, placed, gizmo)); }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }

    private static bool WetBrush(Outline p) => p.Stroke is SolidColorBrush { Color: { R: 0x9F, G: 0xE0, B: 0xFF } };

    [Fact]
    public void Picking_a_characters_wet_part_outlines_the_box_its_drops_start_in()
    {
        Run(o =>
        {
            Assert.True(o.Preview.Pick(o.Placed), "the guard is not in the picture");
            WindowHarness.Pump();
            o.Preview.SetPart(PlacePreview.GizmoPart.Wet);
            WindowHarness.Pump();
            var box = o.Gizmo.Children.OfType<Outline>().Where(WetBrush).ToList();
            Assert.Equal(1, o.Preview.WetBoxesShown);
            Assert.Single(box);
            // Drawn: the outline's own bounds on the canvas (100 px a unit), and the Wet handle's middle.
            var drawn = box[0].Data.Bounds;
            var pip = o.Gizmo.Children.OfType<System.Windows.Shapes.Ellipse>().First();
            double pipX = Canvas.GetLeft(pip) + pip.Width / 2, pipY = Canvas.GetTop(pip) + pip.Height / 2;
            _out.WriteLine($"the box {drawn.Width:0.0} x {drawn.Height:0.0} round ({drawn.X + drawn.Width / 2:0.0}, {drawn.Y + drawn.Height / 2:0.0}); the Wet handle at ({pipX:0.0}, {pipY:0.0})");
            // The towel's two systems' boxes together: the first's round the handle, the second's lower by 2.04 of its
            // 8.45 (Anna's own): from half the height above the handle to half of it and 2.04 below.
            Assert.Equal(WetDrops.BoxW * 0.5 * 100, drawn.Width, 1);
            Assert.Equal(pipX, drawn.X + drawn.Width / 2, 1);
            Assert.Equal(pipY - WetDrops.BoxH / 2 * 0.4 * 100, drawn.Top, 1);
            Assert.Equal(pipY + (WetDrops.BoxH / 2 + 2.04) * 0.4 * 100, drawn.Bottom, 1);

            // Control: the body picked, no box.
            o.Preview.SetPart(PlacePreview.GizmoPart.Body);
            WindowHarness.Pump();
            Assert.Equal(0, o.Preview.WetBoxesShown);
            Assert.Empty(o.Gizmo.Children.OfType<Outline>().Where(WetBrush));
        });
    }

    // ── The drops, ringed ───────────────────────────────────────────────

    private readonly record struct Blob(int X0, int Y0, int X1, int Y1)
    {
        public int W => X1 - X0 + 1;
        public int H => Y1 - Y0 + 1;
        public bool Touches(int w, int h) => X0 <= 0 || Y0 <= 0 || X1 >= w - 1 || Y1 >= h - 1;
        public Blob Shifted(int dx) => new(X0 + dx, Y0, X1 + dx, Y1);
    }

    private const int CanvasW = 1920, CanvasH = 1080;

    /// <summary>Layers of the preview as drawn, at the canvas's own size (a canvas pixel a pixel), one over another.</summary>
    private static byte[] Picture(params Visual[] layers)
    {
        var rtb = new RenderTargetBitmap(CanvasW, CanvasH, 96, 96, PixelFormats.Pbgra32);
        foreach (var v in layers)
        {
            ((UIElement)v).UpdateLayout();
            rtb.Render(v);
        }
        var px = new byte[CanvasW * CanvasH * 4];
        rtb.CopyPixels(px, CanvasW * 4, 0);
        return px;
    }

    /// <summary>Every patch of pixels at least <paramref name="alpha"/> opaque, touching one another.</summary>
    private static List<Blob> Blobs(byte[] px, int alpha)
    {
        var seen = new bool[CanvasW * CanvasH];
        var blobs = new List<Blob>();
        var stack = new Stack<int>();
        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start] || px[start * 4 + 3] < alpha) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop(), x = i % CanvasW, y = i / CanvasW;
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= CanvasW || ny >= CanvasH) continue;
                    int n = ny * CanvasW + nx;
                    if (seen[n] || px[n * 4 + 3] < alpha) continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
            blobs.Add(new Blob(x0, y0, x1, y1));
        }
        return blobs;
    }

    /// <summary>The ring's colour (the box's, #9FE0FF) at a pixel of the rings' picture.</summary>
    private static bool RingAt(byte[] marks, int x, int y)
    {
        if (x < 0 || y < 0 || x >= CanvasW || y >= CanvasH) return false;
        int i = (y * CanvasW + x) * 4;
        return marks[i + 3] > 150 && marks[i] > 170 && marks[i + 1] > 150 && marks[i + 2] < marks[i];
    }

    /// <summary>Whether a ring is drawn round <paramref name="d"/>: the ring's colour a little out of it on all four
    /// sides, and nothing of the ring over the drop itself.</summary>
    private static bool Ringed(byte[] marks, Blob d)
    {
        for (int y = Math.Max(0, d.Y0); y <= Math.Min(CanvasH - 1, d.Y1); y++)
            for (int x = Math.Max(0, d.X0); x <= Math.Min(CanvasW - 1, d.X1); x++)
                if (marks[(y * CanvasW + x) * 4 + 3] > 0) return false;
        int cx = (d.X0 + d.X1) / 2, cy = (d.Y0 + d.Y1) / 2;
        bool Out(Func<int, (int X, int Y)> at) => Enumerable.Range(2, 19).Any(k => { var (x, y) = at(k); return RingAt(marks, x, y); });
        return Out(k => (d.X0 - k, cy)) && Out(k => (d.X1 + k, cy)) && Out(k => (cx, d.Y0 - k)) && Out(k => (cx, d.Y1 + k));
    }

    /// <summary>
    /// The drops as the WPF layer draws them, every drop fully in the picture: each spray's own drawing, which it
    /// keeps with the game's look shown too (the layer is hidden then, the look's picture having the same drops - of
    /// the same moment under tests). Nothing else is on it: any pixel is a drop's (faint - drawn from the small copy
    /// of the texture the drop's size on screen calls for, as the game does).
    /// </summary>
    private static List<Blob> DropsDrawn(PlacePreview preview)
    {
        var layer = (Canvas)typeof(PlacePreview).GetField("_wetLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        return Blobs(Picture(layer.Children.Cast<Visual>().ToArray()), 1).Where(b => !b.Touches(CanvasW, CanvasH)).ToList();
    }

    /// <summary>The look in, when it is shown at all: the rings follow the drops of the look's picture.</summary>
    private static void Settle(PlacePreview preview)
    {
        int from = preview.LookPasses;
        WindowHarness.Until(() => preview.LookPasses > from, TimeSpan.FromSeconds(5));
        WindowHarness.Wait(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void Picking_a_characters_wet_part_rings_each_drop_where_it_is_drawn()
    {
        Run(o =>
        {
            Assert.True(o.Preview.Pick(o.Placed), "the guard is not in the picture");
            WindowHarness.Pump();
            o.Preview.SetPart(PlacePreview.GizmoPart.Wet);
            WindowHarness.Pump();
            Settle(o.Preview);

            var drops = DropsDrawn(o.Preview);
            _out.WriteLine($"{o.Preview.SprayCount} sprays, look {o.Preview.LookShown}, "
                           + string.Join("; ", o.Preview.Emitters.Select(e => $"{e.Name} at ({e.Emitter.X:0},{e.Emitter.Y:0})")));
            var shown = o.Preview.DropMarksShown;
            Assert.NotNull(shown);
            var marks = Picture(shown!);
            var rings = Blobs(marks, 1).Where(b => !b.Touches(CanvasW, CanvasH)).ToList();
            foreach (var d in drops) _out.WriteLine($"a drop at ({d.X0},{d.Y0})-({d.X1},{d.Y1}), {d.W} x {d.H}: ringed {Ringed(marks, d)}");
            foreach (var r in rings) _out.WriteLine($"a ring at ({r.X0},{r.Y0})-({r.X1},{r.Y1})");
            Assert.True(drops.Count >= 1, "no drop of the guard's is drawn in the picture");
            Assert.All(drops, d => Assert.True(Ringed(marks, d), $"the drop at ({d.X0},{d.Y0}) has no ring round it"));
            // Every ring round a drop, none round nothing.
            Assert.All(rings, r => Assert.True(drops.Any(d => d.X0 > r.X0 && d.X1 < r.X1 && d.Y0 > r.Y0 && d.Y1 < r.Y1),
                                               $"the ring at ({r.X0},{r.Y0}) is round no drop"));
            // Control: the rings looked for where the drops are not.
            Assert.All(drops, d => Assert.False(Ringed(marks, d.Shifted(60)), "control: a ring is found where no drop is"));

            // Control: the body picked, no rings.
            o.Preview.SetPart(PlacePreview.GizmoPart.Body);
            WindowHarness.Pump();
            Assert.Null(o.Preview.DropMarksShown);
            Assert.DoesNotContain(o.Gizmo.Children.OfType<FrameworkElement>(), e => e.GetType().Name == "WetDropMarks");
        });
    }

    // ── A side dragged ──────────────────────────────────────────────────

    [Fact]
    public void Dragging_a_side_of_the_wet_box_moves_that_side_alone_and_leaves_the_drops_their_size()
    {
        Run(o =>
        {
            Assert.True(o.Preview.Pick(o.Placed), "the guard is not in the picture");
            WindowHarness.Pump();
            o.Preview.SetPart(PlacePreview.GizmoPart.Wet);
            WindowHarness.Pump();
            Settle(o.Preview);
            string State() => PackRepository.Serialize(o.Vm.Pack);
            Rect Box() => o.Gizmo.Children.OfType<Outline>().Where(WetBrush).Single().Data.Bounds;
            static double Median(IEnumerable<int> v) { var s = v.OrderBy(x => x).ToList(); return s.Count == 0 ? double.NaN : s[s.Count / 2]; }
            var dropsBefore = DropsDrawn(o.Preview);
            var before = Box();
            o.Vm.Undo.Checkpoint();
            string stateBefore = State();
            int depth = o.Vm.Undo.Depth;

            // The right side pulled out 50 canvas pixels, by its grip, the way it faces.
            var right = o.Preview.WetSidesDrawn[PlacePreview.GizmoHandle.WetAreaRight];
            o.Preview.BeginHandle(PlacePreview.GizmoHandle.WetAreaRight, right);
            o.Preview.DragHandleTo(right + new Vector(20, 7));
            o.Preview.DragHandleTo(right + new Vector(50, -12));   // across the side: nothing
            o.Preview.EndHandle();
            WindowHarness.Pump();
            var wider = Box();
            _out.WriteLine($"before {before}; the right side pulled 50 px: {wider}");
            Assert.Equal(before.Left, wider.Left, 1);
            Assert.Equal(before.Right + 50, wider.Right, 1);
            Assert.Equal(before.Top, wider.Top, 1);
            Assert.Equal(before.Bottom, wider.Bottom, 1);
            Assert.Equal(depth + 1, o.Vm.Undo.Depth);

            // The top side pushed in 100 pixels: the bottom stays.
            var top = o.Preview.WetSidesDrawn[PlacePreview.GizmoHandle.WetAreaTop];
            o.Preview.BeginHandle(PlacePreview.GizmoHandle.WetAreaTop, top);
            o.Preview.DragHandleTo(top + new Vector(0, 100));
            o.Preview.EndHandle();
            WindowHarness.Pump();
            var lower = Box();
            _out.WriteLine($"the top side pushed in 100 px: {lower}");
            Assert.Equal(wider.Left, lower.Left, 1);
            Assert.Equal(wider.Right, lower.Right, 1);
            Assert.Equal(wider.Top + 100, lower.Top, 1);
            Assert.Equal(wider.Bottom, lower.Bottom, 1);

            // The drops: their size as drawn the same, the Wet object's scale (which sizes them) untouched.
            var dropsAfter = DropsDrawn(o.Preview);
            _out.WriteLine($"drops drawn before: {string.Join(", ", dropsBefore.Select(d => d.W + "x" + d.H))}; after: {string.Join(", ", dropsAfter.Select(d => d.W + "x" + d.H))}");
            Assert.True(dropsBefore.Count >= 1 && dropsAfter.Count >= 1, "no drops drawn to measure");
            Assert.Equal(Median(dropsBefore.Select(d => d.W)), Median(dropsAfter.Select(d => d.W)), 0);
            Assert.InRange(Median(dropsAfter.Select(d => d.H)), Median(dropsBefore.Select(d => d.H)) - 1, Median(dropsBefore.Select(d => d.H)) + 1);
            Assert.Equal(0.5f, o.Placed.Wet.ScaleX);
            Assert.Equal(0.4f, o.Placed.Wet.ScaleY);
            Assert.Equal(WetDrops.BoxW + 50 / (0.5 * 100), o.Placed.WetAreaWidth, 3);
            // The drawn box is the two systems' together (1 + 2.04/8.45 the height): the top went 100 pixels of it.
            Assert.Equal(WetDrops.BoxH - 100 / (0.4 * 100) / (1 + WetDrops.SecondShift), o.Placed.WetAreaHeight, 3);

            // Through the real save: the area is in the pack read back.
            string saved = Path.Combine(_dir, "saved");
            PackRepository.Save(o.Vm.Pack, saved);
            var back = PackRepository.Load(saved).Places.Single().GameObjects.Single(g => g.IsNpcRoot).Npcs.Single();
            Assert.NotNull(back.WetEffect);
            Assert.Equal(o.Placed.WetAreaWidth, back.WetEffect!.Width!.Value, 4);
            Assert.Equal(o.Placed.WetAreaHeight, back.WetEffect.Height!.Value, 4);

            // Undone, both: the pack as it was.
            RealKeys.Press(o.Window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            RealKeys.Press(o.Window, Key.Z, ModifierKeys.Control);
            WindowHarness.Pump();
            Assert.Equal(stateBefore, State());
            // The character's row made again from the step (a step is put back by what differs): read it anew.
            var now = o.Vm.SelectedPlace!.NpcsNode!.Npcs.Single();
            Assert.Null(now.Model.WetEffect);
            Assert.Equal(WetDrops.BoxW, now.WetAreaWidth, 3);
            var undone = o.Gizmo.Children.OfType<Outline>().Where(WetBrush).ToList();
            if (undone.Count == 1) Assert.Equal(before.Width, undone[0].Data.Bounds.Width, 1);
        });
    }
}
