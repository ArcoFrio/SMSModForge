using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A stroke in the mask studio moves the art it is painted over in the studio's own place preview, for the front
/// layer's mask and the back layer's alike, whatever size the mask's file is (after 1.10.0, the author: "I'm trying
/// to paint the front mask now and the issue is still there"). The place as an author's: 2048x1136 art, a front mask
/// the size of the art, a back mask 256x256. Opened from the Places tab's own Edit buttons; the stroke painted by the
/// studio's mouse handlers across the canvas rows where the art's top fifth is drawn; what is measured is which art
/// rows the preview's level pass then moves.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceMaskStudioPreviewTests : IDisposable
{
    private const int W = 2048, H = 1136;
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmf-maskstudio-" + Guid.NewGuid().ToString("N"));

    public PlaceMaskStudioPreviewTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    /// <summary>Stripes down the whole art, red in its top fifth, blue below: a sideways shift changes the pixels.</summary>
    private static byte[] Art()
    {
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                bool lit = x % 16 < 8;
                px[i] = (byte)(lit && y >= H / 5 ? 255 : 0);
                px[i + 2] = (byte)(lit && y < H / 5 ? 255 : 0);
                px[i + 3] = 255;
            }
        return px;
    }

    private static void Png(string path, byte[] bgra, int w, int h)
    {
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4)));
        enc.Save(fs);
    }

    /// <summary>A mask that moves nothing: alpha 0, white (as a painting program leaves an empty layer).</summary>
    private static byte[] Blank(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = px[i + 1] = px[i + 2] = 255; }
        return px;
    }

    private void MakePack()
    {
        var art = Art();
        Png(Path.Combine(_dir, "Locations", "Room.png"), art, W, H);
        Png(Path.Combine(_dir, "Locations", "RoomB.png"), art, W, H);
        Png(Path.Combine(_dir, "Locations", "RoomMask.png"), Blank(W, H), W, H);
        Png(Path.Combine(_dir, "Locations", "RoomBMask.png"), Blank(256, 256), 256, 256);
        var pack = PackRepository.CreateEmpty("maskstudio.pack");
        pack.Places.Add(new PlaceDef
        {
            Key = "Room", InternalName = "Room", DisplayName = "Room",
            BaseSprite = "Locations/Room.png", MaskSprite = "Locations/RoomMask.png",
            SecondarySprite = "Locations/RoomB.png", SecondaryMaskSprite = "Locations/RoomBMask.png",
        });
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static MouseButtonEventArgs Button(MouseButton which, RoutedEvent routed)
        => new(Mouse.PrimaryDevice, Environment.TickCount, which) { RoutedEvent = routed };

    private static MouseEventArgs Move() => new(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stroke_on_the_top_of_the_art_moves_the_top_of_the_art_in_the_studios_preview(bool back)
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
            var edit = (Button)window.FindName(back ? "EditPlaceSecondaryMaskButton" : "EditPlaceMaskButton");
            edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Pump();
            var studio = window.CurrentMaskStudio;
            Assert.NotNull(studio);
            Assert.True(WindowHarness.Until(() => ((FrameworkElement)studio!.FindName("CanvasHost")).ActualWidth > 0, TimeSpan.FromSeconds(10)));
            WindowHarness.Pump();
            var preview = FindPreview(studio!);
            Assert.NotNull(preview);

            // The canvas: where it draws the art's top fifth (red).
            var canvas = (FrameworkElement)studio!.FindName("CanvasHost");
            ((Slider)studio.FindName("SizeSlider")).Value = 6;
            ((Slider)studio.FindName("HardnessSlider")).Value = 1;
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            double row = canvas.ActualHeight / 10;   // the middle of the top fifth
            var at = new Point(canvas.ActualWidth * 0.1, row);
            bool held = true;
            studio.PointerOnCanvas = _ => at;
            studio.LeftHeld = _ => held;
            canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
            for (int step = 1; step <= 8; step++)
            {
                at = new Point(canvas.ActualWidth * (0.1 + 0.1 * step), row);
                canvas.RaiseEvent(Move());
            }
            held = false;
            canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseUpEvent));
            WindowHarness.Pump();
            Assert.True(studio.IsDirty, "the stroke painted nothing");

            // The preview's level pass for the layer painted: the mask it reads, and the rows it moves.
            var layer = Field<Image>(preview!, back ? "_secondaryImage" : "_baseImage");
            var target = ((IEnumerable)Field<object>(preview!, "_shaderTargets")).Cast<object>()
                .FirstOrDefault(t => ReferenceEquals(t.GetType().GetField("Image")!.GetValue(t), layer));
            Assert.True(target != null, "the layer has no level pass in the preview");
            object F(string name) => target!.GetType().GetField(name)!.GetValue(target)!;
            var basePx = (byte[])F("Base"); var mask = (byte[])F("Mask");
            int srcW = (int)F("SrcW"), srcH = (int)F("SrcH"), maskW = (int)F("MaskW"), maskH = (int)F("MaskH");
            _out.WriteLine($"{(back ? "back" : "front")}: the pass reads a {maskW}x{maskH} mask (live {F("LiveMask")}) over {srcW}x{srcH} art");
            var output = new byte[srcW * srcH * 4];
            int first = -1, last = -1, moved = 0, inTop = 0;
            var rows = new bool[srcH];
            foreach (float t in new[] { 0.3f, 0.75f, 1.2f, 1.9f })
            {
                MilkingShader.Render(basePx, srcW, srcH, mask, maskW, maskH, MilkingShader.Settings.Level, t, output, srcW, srcH);
                for (int y = 0; y < srcH; y++)
                    for (int x = 0; x < srcW; x++)
                    {
                        int i = (y * srcW + x) * 4;
                        if (Math.Abs(output[i] - basePx[i]) > 8 || Math.Abs(output[i + 2] - basePx[i + 2]) > 8) rows[y] = true;
                    }
            }
            for (int y = 0; y < srcH; y++)
                if (rows[y]) { if (first < 0) first = y; last = y; moved++; if (y < srcH / 5) inTop++; }
            _out.WriteLine($"   it moves art rows {first}..{last} ({moved} rows, {inTop} in the top fifth 0..{srcH / 5 - 1})");
            studio.AnswerUnsaved = () => MessageBoxResult.No;
            studio.TryClose();
            WindowHarness.Pump();
            Assert.True(moved > 20, "the stroke moves nothing in the preview");
            Assert.True(inTop >= moved * 0.98, $"the stroke over the art's top fifth moves rows {first}..{last} in the preview");
        });
    }

    private static PlacePreview? FindPreview(DependencyObject root)
    {
        if (root is PlacePreview p) return p;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindPreview(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        if (root is ContentControl { Content: DependencyObject content }) return FindPreview(content);
        return null;
    }
}
