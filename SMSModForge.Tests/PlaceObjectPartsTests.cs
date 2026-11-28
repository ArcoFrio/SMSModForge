using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;
using FX = SMSModForge.Shared.Effects;
using Polygon = System.Windows.Shapes.Polygon;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;

namespace SMSModForge.Tests;

/// <summary>
/// An object's light and effect as parts of it in the place preview, as a character's shadow and blink are (after
/// 1.10.0, the author: "when we add an effect to a GO, can we have the effect appear like if it were an extra GO in
/// an NPC ... a scalable box around it. And if we add a light ... select it and edit like we would a normal light ...
/// we'd also need to have a button for the main GO"). Read off what is drawn:
/// <list type="bullet">
/// <item>A lamp with a picture offers Object and Light: the Object part draws the object's arrows and none of the
/// light's handles, the Light part the light's handles and no arrows; its reach dragged there grows the light and
/// moves nothing. Clicking its sun picks the Light part. Controls: a light alone keeps both together and no chips;
/// a picture alone has none.</item>
/// <item>An object with an effect offers Object and Effect. The Effect part's box is where the particles are drawn
/// (every particle inside it, and they reach out to it). Dragged inside, the box and the particles go with the
/// pointer, the object stays; the object moved, the effect goes with it; the object made twice as big, the effect
/// twice as far from it - the object's own units, as its copy's local position is in the game (S27). A grip
/// dragged twice as far out doubles the effect's Scale, the grip under the pointer, the particles playing on rather
/// than starting again (control: another setting starts them again). Ctrl+Z takes each back.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceObjectPartsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-objparts-" + Guid.NewGuid().ToString("N"));

    public PlaceObjectPartsTests(ITestOutputHelper o)
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

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Click(Button b)
        => ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(b)
               .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!).Invoke();

    private static T Static<T>(string name)
        => (T)typeof(PlacePreview).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Fact]
    public void AnObjectsLightAndEffectArePartsOfItToPick_TheEffectInABoxThatMovesAndSizesIt()
    {
        Png(Path.Combine(_dir, "Locations", "Room.png"), 64, 36, Color.FromRgb(0x30, 0x30, 0x38));
        Png(Path.Combine(_dir, "Locations", "Thing.png"), 40, 40, Color.FromRgb(0xC0, 0x80, 0x40));
        var pack = PackRepository.CreateEmpty("objparts");
        var place = new PlaceDef { Key = "room", InternalName = "Room", BaseSprite = "Locations/Room.png" };
        place.GameObjects.Add(new GameObjectDef { Name = "Lamp", Sprite = "Locations/Thing.png", X = -4, Y = 2, SortingOrder = 5, Light = new LightDef() });
        place.GameObjects.Add(new GameObjectDef { Name = "BareLight", X = -4, Y = -2, Light = new LightDef() });
        place.GameObjects.Add(new GameObjectDef { Name = "Plain", Sprite = "Locations/Thing.png", X = 5, Y = 2, SortingOrder = 5 });
        // Still particles (no speed), many of them: where they are drawn is where they started.
        place.GameObjects.Add(new GameObjectDef
        {
            Name = "Fire", Sprite = "Locations/Thing.png", X = 1, Y = -1, SortingOrder = 5,
            Effect = new EffectDef { Key = "", DisplayName = "", Template = FX.VanillaPrefix + "goldenAura", Speed = 0f, Amount = 4f },
        });
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
                var rows = vm.SelectedPlace!.GameObjects;
                GameObjectViewModel Row(string name) => rows.Single(g => g.Name == name);
                var layer = Field<Canvas>(preview, "_gizmoLayer");
                var modeRow = Field<WrapPanel>(preview, "_modeRow");
                var gx = Static<Brush>("GX");
                int Arrows() => layer.Children.OfType<Polygon>().Count(p => ReferenceEquals(p.Fill, gx));
                // A light handle is its knob and a see-through ring round it to take: the knobs.
                int Reaches() => layer.Children.OfType<Shape>().Count(s => s.ToolTip as string == Loc.T("preview.light.reach.tip")
                                                                           && !ReferenceEquals(s.Fill, Brushes.Transparent));
                void Pick(string name) { Assert.True(preview.Pick(Row(name)), name + " is not in the picture"); Settle(window); }
                void Part(PlacePreview.ObjectPart p) { preview.SetObjectPart(p); Settle(window); }

                // ── A lamp with a picture: Object and Light ──
                Pick("Lamp");
                _out.WriteLine($"the lamp offers {string.Join(", ", preview.ObjectPartsOffered)}; picked {preview.PickedObjectPart}: "
                               + $"{Arrows()} arrow, {Reaches()} reach handle");
                Assert.Equal(new[] { PlacePreview.ObjectPart.Object, PlacePreview.ObjectPart.Light }, preview.ObjectPartsOffered);
                Assert.Equal(PlacePreview.ObjectPart.Object, preview.PickedObjectPart);
                Assert.Equal(1, Arrows());
                Assert.Equal(0, Reaches());
                Assert.Equal(Visibility.Visible, modeRow.Visibility);
                Part(PlacePreview.ObjectPart.Light);
                _out.WriteLine($"its Light part: {Arrows()} arrow, {Reaches()} reach handle, the ways of moving {modeRow.Visibility}");
                Assert.Equal(0, Arrows());
                Assert.Equal(1, Reaches());
                Assert.NotEqual(Visibility.Visible, modeRow.Visibility);
                // Its reach dragged there: the light's, the object stays.
                var lamp = Row("Lamp");
                var knob = layer.Children.OfType<Shape>().First(s => s.ToolTip as string == Loc.T("preview.light.reach.tip")
                                                                     && !ReferenceEquals(s.Fill, Brushes.Transparent));
                var at = new Point(Canvas.GetLeft(knob) + knob.Width / 2, Canvas.GetTop(knob) + knob.Height / 2);
                float reach = lamp.Light!.Model.OuterRadius, lampX = lamp.X, lampY = lamp.Y;
                preview.BeginHandle(PlacePreview.GizmoHandle.LightOuter, at);
                preview.DragHandleTo(at + new Vector(60, 0));
                preview.EndHandle();
                Settle(window);
                _out.WriteLine($"its reach dragged 60 px out: {reach} -> {lamp.Light!.Model.OuterRadius}; the lamp at {lamp.X},{lamp.Y}");
                Assert.True(lamp.Light!.Model.OuterRadius > reach + 0.1f, "the Light part's reach handle does not drag the light");
                Assert.Equal((lampX, lampY), (lamp.X, lamp.Y));

                // Controls: a light alone, both together and no chips; a picture alone, no chips.
                Pick("BareLight");
                _out.WriteLine($"a light alone: chips {preview.ObjectPartsShown}, {Arrows()} arrow, {Reaches()} reach handle");
                Assert.False(preview.ObjectPartsShown);
                Assert.Equal(1, Arrows());
                Assert.Equal(1, Reaches());
                Pick("Plain");
                Assert.False(preview.ObjectPartsShown);
                Assert.Equal(1, Arrows());
                // Given an effect from its panel, its parts are offered at once; taken off, they go.
                Click(Field<Button>(preview, "_effectGive"));
                Settle(window);
                Assert.Equal(new[] { PlacePreview.ObjectPart.Object, PlacePreview.ObjectPart.Effect }, preview.ObjectPartsOffered);
                Click(Field<Button>(preview, "_effectRemove"));
                Settle(window);
                Assert.False(preview.ObjectPartsShown);

                // The lamp's sun clicked: the lamp, its Light part.
                Part(PlacePreview.ObjectPart.Object);
                var sun = Field<Canvas>(preview, "_lightLayer").Children.OfType<FrameworkElement>().Single(s => s.ToolTip as string == "Lamp");
                sun.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                Settle(window);
                Assert.Same(lamp, preview.PickedRow);
                Assert.Equal(PlacePreview.ObjectPart.Light, preview.PickedObjectPart);

                // ── An object with an effect: Object and Effect ──
                Pick("Fire");
                var fire = Row("Fire");
                var effect = fire.Effect!;
                Assert.Equal(new[] { PlacePreview.ObjectPart.Object, PlacePreview.ObjectPart.Effect }, preview.ObjectPartsOffered);
                // Light was chosen last, and the fire has none: its Object part.
                Assert.Equal(PlacePreview.ObjectPart.Object, preview.PickedObjectPart);
                Point objectAt = preview.HandlesAt!.Value;
                Part(PlacePreview.ObjectPart.Effect);
                Assert.Equal(0, Arrows());
                var view = preview.EffectLayer.Children.OfType<EffectPreview>().Single();

                // The dashed outline as drawn, and the grips.
                Rect Box()
                {
                    var o = layer.Children.OfType<Rectangle>().Single(r => r.StrokeDashArray.Count == 2 && !r.IsHitTestVisible);
                    return new Rect(Canvas.GetLeft(o), Canvas.GetTop(o), o.Width, o.Height);
                }
                Point[] Grips() => layer.Children.OfType<Rectangle>()
                    .Where(r => r.ToolTip as string == Loc.T("preview.effect.grip.tip") && !ReferenceEquals(r.Fill, Brushes.Transparent))
                    .Select(r => new Point(Canvas.GetLeft(r) + r.Width / 2, Canvas.GetTop(r) + r.Height / 2)).ToArray();
                Point Mid(Rect r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);
                // Where the effect's particles are drawn on the canvas (the view is the canvas's size, at its corner).
                Rect Particles()
                {
                    var bmp = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(view);
                    int w = bmp.PixelWidth, h = bmp.PixelHeight;
                    var px = new byte[w * h * 4];
                    bmp.CopyPixels(px, w * 4, 0);
                    int x0 = w, y0 = h, x1 = -1, y1 = -1;
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                            if (px[(y * w + x) * 4 + 3] > 20) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
                    Assert.True(x1 >= 0, "control: the effect draws nothing");
                    return new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                }

                var box = Box();
                var drawn = Particles();
                _out.WriteLine($"the fire at {objectAt.X:F1},{objectAt.Y:F1}; its effect's box {box}; its particles drawn over {drawn}");
                Assert.True((Mid(box) - objectAt).Length < 0.5, "the effect's box is not at its object");
                // Every particle inside the box (give or take half a particle), and reaching out to it.
                var outer = box; outer.Inflate(10, 10);
                var inner = box; inner.Inflate(-30, -30);
                Assert.True(outer.Contains(drawn), $"particles drawn outside the box: {drawn} against {box}");
                Assert.True(drawn.Contains(inner), $"the particles do not reach out to the box: {drawn} against {box}");

                // Dragged inside: the box and the particles go with the pointer; the object stays.
                vm.Undo.Checkpoint();
                float fireX = fire.X, fireY = fire.Y;
                var move = new Vector(100, -50);
                preview.BeginHandle(PlacePreview.GizmoHandle.EffectMove, Mid(box));
                preview.DragHandleTo(Mid(box) + move * 0.5);
                preview.DragHandleTo(Mid(box) + move);
                preview.EndHandle();
                Settle(window);
                var moved = Box();
                var movedDrawn = Particles();
                _out.WriteLine($"dragged {move}: the box {moved}, the particles {movedDrawn}; offset {effect.LocalX},{effect.LocalY}; the fire at {fire.X},{fire.Y}");
                Assert.True((Mid(moved) - (Mid(box) + move)).Length < 0.5, "the box does not follow the pointer");
                Assert.True((Mid(movedDrawn) - (Mid(drawn) + move)).Length < 1.5, "the particles do not go with the box");
                Assert.Equal((fireX, fireY), (fire.X, fire.Y));
                Assert.NotEqual(0, effect.LocalX);

                // The object moved: the effect with it.
                Part(PlacePreview.ObjectPart.Object);
                var from = preview.HandlesAt!.Value;
                preview.BeginHandle(PlacePreview.GizmoHandle.MoveFree, from);
                preview.DragHandleTo(from + new Vector(60, 0));
                preview.EndHandle();
                Settle(window);
                Part(PlacePreview.ObjectPart.Effect);
                var carried = Box();
                _out.WriteLine($"the fire moved 60 px: its effect's box {carried}");
                Assert.True((Mid(carried) - (Mid(moved) + new Vector(60, 0))).Length < 0.5, "the effect does not move with its object");

                // The object twice as big: the effect twice as far from it (the object's own units).
                Part(PlacePreview.ObjectPart.Object);
                var fireAt = preview.HandlesAt!.Value;
                double away = (Mid(carried) - fireAt).Length;
                fire.ScaleX *= 2; fire.ScaleY *= 2;
                Settle(window);
                Part(PlacePreview.ObjectPart.Effect);
                double twice = (Mid(Box()) - fireAt).Length;
                _out.WriteLine($"the fire twice as big: its effect {away:F1} -> {twice:F1} px from it");
                Assert.InRange(twice / away, 1.99, 2.01);
                fire.ScaleX /= 2; fire.ScaleY /= 2;
                Settle(window);

                // A grip dragged twice as far out: twice the Scale, the grip under the pointer, the particles playing on.
                box = Box();
                var right = Grips().OrderByDescending(g => g.X).ThenBy(g => Math.Abs(g.Y - Mid(box).Y)).First();
                var to = Mid(box) + (right - Mid(box)) * 2;
                object sim = Field<object>(view, "_sim");
                vm.Undo.Checkpoint();
                int depth = vm.Undo.Depth;
                preview.BeginHandle(PlacePreview.GizmoHandle.EffectSize, right);
                preview.DragHandleTo(Mid(box) + (right - Mid(box)) * 1.5);
                bool playedOnInTheDrag = ReferenceEquals(sim, Field<object>(view, "_sim"));
                preview.DragHandleTo(to);
                preview.EndHandle();
                Settle(window);
                var sized = Box();
                var rightNow = Grips().OrderByDescending(g => g.X).ThenBy(g => Math.Abs(g.Y - Mid(sized).Y)).First();
                _out.WriteLine($"its right grip dragged from {right} to {to}: Scale {effect.Scale}, the box {sized}, the grip at {rightNow}; "
                               + $"the same particles in the drag {playedOnInTheDrag}, after it {ReferenceEquals(sim, Field<object>(view, "_sim"))}");
                Assert.Equal(2, effect.Scale, 2);
                Assert.InRange(sized.Width / box.Width, 1.99, 2.01);
                Assert.InRange(sized.Height / box.Height, 1.99, 2.01);
                Assert.True((rightNow - to).Length < 1, "the grip is not under the pointer");
                Assert.True(playedOnInTheDrag, "the effect started again as its box was sized");
                Assert.Same(sim, Field<object>(view, "_sim"));
                Assert.Equal(depth + 1, vm.Undo.Depth);
                // Control: another of its settings starts it again.
                effect.Size = 1.5;
                Settle(window);
                Assert.NotSame(sim, Field<object>(view, "_sim"));
                effect.Size = 1;
                Settle(window);

                // Ctrl+Z: the size, then the object's move, then the effect's move.
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                Settle(window);
                var back = Box();
                _out.WriteLine($"Ctrl+Z: Scale {Row("Fire").Effect!.Scale}, the box {back}, the part {preview.PickedObjectPart}");
                Assert.Equal(1, Row("Fire").Effect!.Scale, 3);
                Assert.InRange(back.Width / box.Width, 0.999, 1.001);
                Assert.Equal(PlacePreview.ObjectPart.Effect, preview.PickedObjectPart);
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                Settle(window);
                RealKeys.Press(window, Key.Z, ModifierKeys.Control);
                Settle(window);
                fire = Row("Fire");
                _out.WriteLine($"twice more: the fire at {fire.X},{fire.Y}, its effect's offset {fire.Effect!.LocalX},{fire.Effect!.LocalY}");
                Assert.Equal((fireX, fireY), (fire.X, fire.Y));
                Assert.Equal(0, fire.Effect!.LocalX);
                Assert.Equal(0, fire.Effect!.LocalY);
                Assert.True((Mid(Box()) - objectAt).Length < 0.5, "taken back, the effect's box is not at its object");
            }
            finally { typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null); }
        });
    }
}
