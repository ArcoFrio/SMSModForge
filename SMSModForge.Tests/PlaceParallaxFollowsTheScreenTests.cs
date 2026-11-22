using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SMSModForge.Model;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Preview parallax follows the mouse over the whole screen (1.10.0,
/// PlacePreview.Parallax.cs), measured on what is drawn - where each drifting
/// thing of the place has been put - with the cursor handed to the same method
/// the preview hands it to each frame:
/// <list type="bullet">
/// <item>the cursor in the middle of the screen: nothing has moved; at one
/// side: things have, and at the other side as far the other way - control:
/// with the box unticked nothing moves wherever the cursor is;</item>
/// <item>anywhere on the screen counts, not only over the preview: two places
/// far from the preview give two different drifts;</item>
/// <item>off the screen (another monitor): further on, in step with the cursor,
/// as the game's drift goes (the author: "similar to how the game already
/// behaves");</item>
/// <item>up and down moves a thing half as far as left and right, the game's
/// own rule;</item>
/// <item>a place built again while drifted (something in it changed) is
/// drawn drifted, not where it was made;</item>
/// <item>unticked: everything back where it was made.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceParallaxFollowsTheScreenTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmodforge-parallax-" + Guid.NewGuid().ToString("N"));

    public PlaceParallaxFollowsTheScreenTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly FieldInfo Targets = typeof(PlacePreview).GetField("_parallaxTargets", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>How far each drifting thing is drawn from where it was made, in the picture's own units.</summary>
    private static List<(double X, double Y)> Moved(PlacePreview preview)
    {
        var moved = new List<(double, double)>();
        foreach (object target in (IEnumerable)Targets.GetValue(preview)!)
        {
            var fields = target.GetType().GetFields();
            var element = (UIElement)fields[0].GetValue(target)!;
            var made = (Matrix)fields[1].GetValue(target)!;
            var now = (element.RenderTransform as MatrixTransform)?.Matrix ?? Matrix.Identity;
            moved.Add((now.OffsetX - made.OffsetX, now.OffsetY - made.OffsetY));
        }
        return moved;
    }

    private static double Furthest(List<(double X, double Y)> moved) => moved.Count == 0 ? 0 : moved.Max(m => Math.Max(Math.Abs(m.X), Math.Abs(m.Y)));

    [Fact]
    public void TheDriftFollowsTheCursorOverTheWholeScreen()
    {
        PackRepository.Save(UndoCrawlPack.Make(_dir), _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            var place = vm.Places.OrderByDescending(p => p.GameObjects.Count).First();
            vm.SelectedPlace = place;
            WindowHarness.Pump();
            // Something of the pack's own that drifts, whatever else the place has.
            var drifter = place.AddGameObject();
            drifter.Name = "Drifter";
            drifter.ParallaxEnabled = true;
            drifter.ParallaxStrength = 0.6f;
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var tick = (CheckBox)window.FindName("PlaceParallaxPreview");
            var screen = new Rect(-1920, 0, 1920, 1080);      // a screen to the left of the main one

            // Control: unticked, the cursor moves nothing.
            Assert.False(tick.IsChecked == true);
            preview.FollowCursor(-1900, 20, screen);
            WindowHarness.Pump();
            Assert.Equal(0, Furthest(Moved(preview)), 6);
            Assert.Null(preview.Drift);

            tick.IsChecked = true;
            WindowHarness.Pump();
            int things = Moved(preview).Count;
            _out.WriteLine($"{things} things of the place drift");
            Assert.True(things > 0, "control: nothing in this place drifts - the check cannot tell");

            // The middle of the screen: the place as it was made.
            preview.FollowCursor(-960, 540, screen);
            WindowHarness.Pump();
            Assert.Equal(0, Furthest(Moved(preview)), 6);

            // One side, then the other: as far, the other way.
            preview.FollowCursor(-1920, 540, screen);
            WindowHarness.Pump();
            var left = Moved(preview);
            preview.FollowCursor(0, 540, screen);
            WindowHarness.Pump();
            var right = Moved(preview);
            _out.WriteLine($"at the left edge the furthest thing is {Furthest(left):0.00} from where it was made, at the right {Furthest(right):0.00}");
            Assert.True(Furthest(left) > 1, "nothing moved with the cursor at the edge of the screen");
            for (int i = 0; i < things; i++)
            {
                Assert.Equal(-left[i].X, right[i].X, 6);
                Assert.Equal(0, left[i].Y, 6);
            }

            // Anywhere on the screen counts: two places far from the preview, two different drifts.
            preview.FollowCursor(-1800, 1000, screen);
            WindowHarness.Pump();
            var corner = Moved(preview);
            preview.FollowCursor(-200, 100, screen);
            WindowHarness.Pump();
            var other = Moved(preview);
            Assert.True(Enumerable.Range(0, things).Any(i => Math.Abs(corner[i].X - other[i].X) > 1 && Math.Abs(corner[i].Y - other[i].Y) > 0.5),
                        "two places on the screen far from each other drift the place the same");

            // Off the screen, on another monitor: further on, in step with the cursor.
            preview.FollowCursor(2500, 540, screen);
            WindowHarness.Pump();
            var beyond = Moved(preview);
            double past = ((2500 - screen.X) / screen.Width - 0.5) / 0.5;     // as far as the right edge, times this
            _out.WriteLine($"on the next monitor, {past:0.00} times as far as at the edge");
            for (int i = 0; i < things; i++) Assert.Equal(right[i].X * past, beyond[i].X, 6);
            Assert.True(Furthest(beyond) > Furthest(right) * 3, "the drift stopped at the edge of the screen");

            // Up and down moves a thing half as far as left and right does: the game's own rule.
            preview.FollowCursor(-960, 0, screen);
            WindowHarness.Pump();
            var top = Moved(preview);
            int most = Enumerable.Range(0, things).OrderByDescending(i => Math.Abs(left[i].X)).First();
            Assert.Equal(Math.Abs(left[most].X) / 2, Math.Abs(top[most].Y), 6);
            Assert.Equal(0, top[most].X, 6);

            // The place built again while drifted: drawn drifted.
            preview.FollowCursor(-1920, 540, screen);
            WindowHarness.Pump();
            double before = Furthest(Moved(preview));
            drifter.X += 1;
            WindowHarness.Pump();
            WindowHarness.Pump();
            double rebuilt = Furthest(Moved(preview));
            _out.WriteLine($"drifted {before:0.00}; built again, {rebuilt:0.00}");
            Assert.True(rebuilt > 1, "a place built again while drifted is drawn where it was made until the cursor next moves");
            Assert.Equal(before, rebuilt, 6);

            // Unticked: everything back where it was made.
            tick.IsChecked = false;
            WindowHarness.Pump();
            Assert.Equal(0, Furthest(Moved(preview)), 6);
            Assert.Null(preview.Drift);

            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }

    private static void WritePng(string path, int w, int h, Color colour)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = colour.B; px[i + 1] = colour.G; px[i + 2] = colour.R; px[i + 3] = 255; }
        var bmp = System.Windows.Media.Imaging.BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        png.Save(f);
    }

    /// <summary>
    /// Switching places while the preview drifts leaves a place's art with its objects (the author, 2026-11-21:
    /// "switching between places causes the textures to all misalign"). The level's two pictures outlive a rebuild,
    /// and were tracked from wherever they stood - already drifted - so every rebuild while drifted added the drift
    /// again, and the art walked away from the objects drawn fresh. Measured where each is drawn, not against what
    /// the preview recorded (which is what went wrong): the same cursor, the same place, the same spots.
    /// </summary>
    [Fact]
    public void SwitchingPlacesWhileDriftedKeepsTheArtWhereItBelongs()
    {
        WritePng(Path.Combine(_dir, "Art", "room.png"), 1024, 568, Colors.SteelBlue);
        WritePng(Path.Combine(_dir, "Art", "lamp.png"), 64, 64, Colors.Gold);
        var pack = PackRepository.CreateEmpty("drift.pack");
        foreach (string key in new[] { "RoomA", "RoomB" })
            pack.Places.Add(new PlaceDef
            {
                Key = key, InternalName = key, DisplayName = key, BaseSprite = "Art/room.png", SecondarySprite = "Art/room.png",
                GameObjects = { new GameObjectDef { Name = "Lamp", Sprite = "Art/lamp.png", X = 2, Y = 1 } },
            });
        PackRepository.Save(pack, _dir);
        WindowHarness.Run(window =>
        {
            var vm = (MainViewModel)window.DataContext;
            vm.OpenPackFromPath(_dir);
            ((TabControl)window.FindName("MainTabs")).SelectedIndex = MainTab.Places;
            WindowHarness.Pump();
            var a = vm.Places.Single(p => p.Key == "RoomA");
            var b = vm.Places.Single(p => p.Key == "RoomB");
            vm.SelectedPlace = a;
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var tick = (CheckBox)window.FindName("PlaceParallaxPreview");
            var screen = new Rect(0, 0, 1920, 1080);
            var front = (Image)typeof(PlacePreview).GetField("_baseImage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            var back = (Image)typeof(PlacePreview).GetField("_secondaryImage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            Point At(UIElement e) { preview.UpdateLayout(); return e.TranslatePoint(new Point(0, 0), preview); }
            double Apart(Point p, Point q) => Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y));

            var made = At(front);
            tick.IsChecked = true;
            WindowHarness.Pump();
            preview.FollowCursor(0, 540, screen);     // the left edge: as far as one screen drifts it
            WindowHarness.Pump();
            var drifted = (Front: At(front), Back: At(back));
            _out.WriteLine($"made at {made}; drifted to {drifted.Front} (back {drifted.Back})");
            Assert.True(Apart(made, drifted.Front) > 1, "control: the art did not drift - the check cannot tell");

            // Away and back, the cursor where it was: the art is where it was.
            vm.SelectedPlace = b;
            WindowHarness.Pump();
            WindowHarness.Pump();
            var atB = At(front);
            vm.SelectedPlace = a;
            WindowHarness.Pump();
            WindowHarness.Pump();
            _out.WriteLine($"in RoomB the art is at {atB}; back in RoomA, {At(front)} (back {At(back)})");
            Assert.True(Apart(drifted.Front, atB) < 0.5, $"RoomB's art, the same picture drifted the same, is at {atB}, not {drifted.Front}");
            Assert.True(Apart(drifted.Front, At(front)) < 0.5, $"back in RoomA the art is at {At(front)}, not {drifted.Front}");
            Assert.True(Apart(drifted.Back, At(back)) < 0.5, $"back in RoomA the back art is at {At(back)}, not {drifted.Back}");

            // Unticked: back where it was made.
            tick.IsChecked = false;
            WindowHarness.Pump();
            Assert.True(Apart(made, At(front)) < 0.5, $"unticked, the art is at {At(front)}, not where it was made ({made})");
            typeof(MainViewModel).GetMethod("MarkSaved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, null);
        });
    }
}
