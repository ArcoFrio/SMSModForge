using System;
using System.Collections;
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
using SMSModForge.Rendering;
using SMSModForge.View;
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's objects with masks of their own, painted in the mask studio, and what covers a layer being painted
/// (after 1.10.0, the author: "do you think we could implement a way to add masks to other GOs in the place? We should
/// be able to choose between the jiggle and level masks, and edit like we'd edit either"; and the front mask that
/// seemed not to move: an object in front hid it). Through the window: the object picked in the place preview, its
/// kind chosen in the panel beside the picture, its Edit button, a stroke by the studio's own mouse handlers - and
/// what is measured is what the studio's place preview then moves, and what the canvas draws.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceObjectMaskStudioTests : IDisposable
{
    private const int W = 2048, H = 1136, SW = 512, SH = 256;
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "smsmf-objmask-" + Guid.NewGuid().ToString("N"));

    public PlaceObjectMaskStudioTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* temp */ } }

    /// <summary>A checker, red in the top fifth and blue below: a shift either way changes the pixels (the jiggle's
    /// bounce moves a picture up and down).</summary>
    private static byte[] Stripes(int w, int h, byte alpha = 255)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool lit = (x % 16 < 8) ^ (y % 16 < 8);
                px[i] = (byte)(lit && y >= h / 5 ? 255 : 0);
                px[i + 2] = (byte)(lit && y < h / 5 ? 255 : 0);
                px[i + 3] = alpha;
            }
        return px;
    }

    private void Png(string rel, byte[] bgra, int w, int h)
    {
        using var fs = File.Create(Path.Combine(_dir, rel));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4)));
        enc.Save(fs);
    }

    /// <summary>A place: the front and back art, a near layer over its left half (in front, order -9: solid green, to
    /// tell it from the layers' stripes where it is drawn), and a sign with its own picture.</summary>
    private void MakePack()
    {
        Png("Art/front.png", Stripes(W, H), W, H);
        Png("Art/back.png", Stripes(W, H), W, H);
        var near = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W / 2; x++)
            {
                int i = (y * W + x) * 4;
                near[i + 1] = 255;
                near[i + 3] = 255;
            }
        Png("Art/near.png", near, W, H);
        Png("Art/sign.png", Stripes(SW, SH), SW, SH);
        var pack = PackRepository.CreateEmpty("objmask.pack");
        pack.Places.Add(new PlaceDef
        {
            Key = "Room", InternalName = "Room", DisplayName = "Room", BaseSprite = "Art/front.png", SecondarySprite = "Art/back.png",
            GameObjects =
            {
                new GameObjectDef { Name = "Near", Sprite = "Art/near.png", SortingOrder = -9 },
                new GameObjectDef { Name = "Sign", Sprite = "Art/sign.png", SortingOrder = 5, X = 3, Y = 1 },
            },
        });
        PackRepository.Save(pack, _dir);
    }

    private static T Field<T>(object o, string name)
        => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var d in Descendants<T>(c)) yield return d;
        }
    }

    private static MouseButtonEventArgs Button(MouseButton which, RoutedEvent routed)
        => new(Mouse.PrimaryDevice, Environment.TickCount, which) { RoutedEvent = routed };

    /// <summary>A stroke across the canvas at a height (a share of it), by the studio's own mouse handlers.</summary>
    private static void Stroke(MaskStudio studio, double rowShare)
    {
        var canvas = (FrameworkElement)studio.FindName("CanvasHost");
        ((Slider)studio.FindName("SizeSlider")).Value = 6;
        ((Slider)studio.FindName("HardnessSlider")).Value = 1;
        ((Slider)studio.FindName("OpacitySlider")).Value = 1;
        double row = canvas.ActualHeight * rowShare;
        var at = new Point(canvas.ActualWidth * 0.1, row);
        bool held = true;
        studio.PointerOnCanvas = _ => at;
        studio.LeftHeld = _ => held;
        canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
        for (int step = 1; step <= 8; step++)
        {
            at = new Point(canvas.ActualWidth * (0.1 + 0.1 * step), row);
            canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent });
        }
        held = false;
        canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseUpEvent));
        WindowHarness.Pump();
    }

    /// <summary>The preview's pass over an element's picture, and the rows of the art it moves.</summary>
    private (int First, int Last, int Rows, int Height, bool Jiggle) MovedRows(PlacePreview preview, Image image)
    {
        var target = ((IEnumerable)Field<object>(preview, "_shaderTargets")).Cast<object>()
            .FirstOrDefault(t => ReferenceEquals(t.GetType().GetField("Image")!.GetValue(t), image));
        Assert.True(target != null, "the picture has no pass in the preview");
        object F(string n) => target!.GetType().GetField(n)!.GetValue(target)!;
        var basePx = (byte[])F("Base");
        var mask = (byte[])F("Mask");
        int w = (int)F("SrcW"), h = (int)F("SrcH");
        bool jiggle = target!.GetType().GetField("Params")!.GetValue(target) != null;
        var rows = new bool[h];
        var output = new byte[w * h * 4];
        foreach (float t in new[] { 0.3f, 0.75f, 1.2f, 1.9f })
        {
            if (jiggle)
            {
                // The live buffer laid on the art's grid, as the preview's pass does before each draw.
                if (F("LiveSource") is byte[] live) PlacePreview.OnArtGrid(live, w, h, mask);
                JiggleShader.Render(basePx, mask, w, h, (JiggleParams)F("Params"), (ValueTuple<float, float, float, float>)F("Tint"), t,
                                    output, w, h, JiggleShader.Sampling.Game);
            }
            else MilkingShader.Render(basePx, w, h, mask, (int)F("MaskW"), (int)F("MaskH"), MilkingShader.Settings.Level, t, output, w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (Math.Abs(output[i] - basePx[i]) > 8 || Math.Abs(output[i + 2] - basePx[i + 2]) > 8) rows[y] = true;
                }
        }
        int first = -1, last = -1, count = 0;
        for (int y = 0; y < h; y++) if (rows[y]) { if (first < 0) first = y; last = y; count++; }
        return (first, last, count, h, jiggle);
    }

    private void InPlaces(Action<MainWindow, MainViewModel, PlaceViewModel> body)
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
            body(window, vm, vm.SelectedPlace!);
        });
    }

    private static MaskStudio Opened(MainWindow window)
    {
        var studio = window.CurrentMaskStudio;
        Assert.NotNull(studio);
        Assert.True(WindowHarness.Until(() => ((FrameworkElement)studio!.FindName("CanvasHost")).ActualWidth > 0, TimeSpan.FromSeconds(10)));
        WindowHarness.Pump();
        return studio!;
    }

    private static PlacePreview PreviewIn(MaskStudio studio)
        => Descendants<PlacePreview>(studio).FirstOrDefault() ?? (PlacePreview)((ContentControl)studio.FindName("PreviewSlot")).Content;

    /// <summary>The mean colour the preview draws over a share of its front layer's picture (<paramref name="x0"/> to
    /// <paramref name="x1"/> across it, <paramref name="y0"/> to <paramref name="y1"/> down), and how much its pixels
    /// differ from that (the standard deviation of their brightness: the front's checker showing, or a flat colour),
    /// read from the preview as drawn: where the layer is comes from the layout, its colour from the pixels.</summary>
    private static (double R, double G, double B, double Spread) DrawnOverFront(PlacePreview p, double x0, double x1, double y0, double y1)
    {
        p.UpdateLayout();
        var front = Field<Image>(p, "_baseImage");
        var box = front.TransformToAncestor(p).TransformBounds(new Rect(0, 0, front.ActualWidth, front.ActualHeight));
        int w = (int)Math.Ceiling(p.ActualWidth), h = (int)Math.Ceiling(p.ActualHeight);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(p);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        double r = 0, g = 0, b = 0, sum = 0, squares = 0;
        int n = 0;
        for (int y = (int)(box.Top + box.Height * y0); y < (int)(box.Top + box.Height * y1); y++)
            for (int x = (int)(box.Left + box.Width * x0); x < (int)(box.Left + box.Width * x1); x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                int i = (y * w + x) * 4;
                b += px[i]; g += px[i + 1]; r += px[i + 2]; n++;
                double bright = px[i] + px[i + 1] + px[i + 2];
                sum += bright; squares += bright * bright;
            }
        Assert.True(n > 100, $"the front layer is drawn at {box}, outside the preview's {w}x{h}");
        double mean = sum / n;
        return (r / n, g / n, b / n, Math.Sqrt(Math.Max(0, squares / n - mean * mean)));
    }

    /// <summary>
    /// The front layer's mask, where the near layer (solid green) hides the front's left half: the studio says the near
    /// layer covers it, and on the right half only the sign (in front too). The studio's canvas shows the layer alone -
    /// both halves alike - and the place preview beside it draws the near layer see-through, the front layer's stripes
    /// showing through it (the author: "the preview would show the layer on the front a bit transparent so we could see
    /// what we're painting behind it ... I really don't like the front layer overlayed on the actual painting screen").
    /// Controls: the Places tab's own preview draws the near layer solid there; the back layer's studio, under the front
    /// layer itself as well, says nearly all of it is covered.
    /// </summary>
    [Fact]
    public void The_front_masks_studio_shows_the_layer_alone_and_its_preview_sees_through_what_is_in_front()
    {
        InPlaces((window, vm, place) =>
        {
            ((Button)window.FindName("EditPlaceMaskButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Pump();
            var studio = Opened(window);
            Assert.True(WindowHarness.Until(() => studio.Cover != null, TimeSpan.FromSeconds(20)), "the studio never learnt what covers the layer");
            var cover = studio.Cover!;
            const int size = MaskBuffer.Size;
            int leftCovered = 0, rightCovered = 0, rightBySign = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    if (cover.Covered[y * size + x])
                    {
                        if (x < size / 2) leftCovered++;
                        else { rightCovered++; if (cover.By[y * size + x] == "Sign") rightBySign++; }
                    }
            _out.WriteLine($"front: {cover.Share:P1} covered - left half {leftCovered}, right half {rightCovered} ({rightBySign} by the sign); by '{cover.By[10 * size + 10]}'");
            Assert.True(leftCovered >= size * size / 2 * 0.97, "the near layer's half is not all covered");
            Assert.Equal("Near", cover.By[10 * size + 10]);
            Assert.True(rightCovered > 0 && rightBySign == rightCovered, "on the right half something but the sign is counted covering");
            // The sign: 512 x 256 art at the level's own scale, a share of the right half.
            Assert.InRange(rightCovered, size * size / 2 / 20, size * size / 2 / 4);

            // The canvas as drawn: the covered half darker than the same stripes on the other half.
            var image = (FrameworkElement)studio.FindName("MaskImage");
            int w = (int)Math.Round(image.ActualWidth), h = (int)Math.Round(image.ActualHeight);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(image), null, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var px = new byte[w * h * 4];
            rtb.CopyPixels(px, w * 4, 0);
            if (Environment.GetEnvironmentVariable("SMSMODFORGE_SHOT_DIR") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                using var fs = File.Create(Path.Combine(shots, "studio-cover-canvas.png"));
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                enc.Save(fs);
                var whole = new RenderTargetBitmap((int)studio.ActualWidth, (int)studio.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                whole.Render(studio);
                using var fs2 = File.Create(Path.Combine(shots, "studio-cover-whole.png"));
                var enc2 = new PngBitmapEncoder();
                enc2.Frames.Add(BitmapFrame.Create(whole));
                enc2.Save(fs2);
            }
            double Bright(int x0, int x1)
            {
                double sum = 0; int n = 0;
                for (int y = h / 3; y < h * 2 / 3; y++)
                    for (int x = x0; x < x1; x++) { int i = (y * w + x) * 4; sum += px[i] + px[i + 1] + px[i + 2]; n++; }
                return sum / n;
            }
            // The right of the right half, clear of the sign.
            double left = Bright(w / 16, w * 7 / 16), right = Bright(w * 12 / 16, w * 15 / 16);
            _out.WriteLine($"the canvas: the covered half {left:0.0}, the other {right:0.0}");
            // The same stripes at the same brightness on both halves: nothing laid over the covered one (until the
            // author's word it was dimmed to 0.66 of the other, with a hatch).
            Assert.InRange(left, right * 0.95, right * 1.05);

            // The preview beside it: the near layer see-through over the front's left half, its stripes showing.
            var side = PreviewIn(studio);
            Assert.True(WindowHarness.Until(() => side.SeenThrough > 0, TimeSpan.FromSeconds(10)), "nothing in the studio's preview is see-through");
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
            var seen = DrawnOverFront(side, 0.15, 0.4, 0.4, 0.6);
            bool sideLook = side.LookShown;
            studio.AnswerUnsaved = () => MessageBoxResult.No;
            studio.TryClose();
            WindowHarness.Pump();
            var main = (PlacePreview)window.FindName("PlacePreviewMain");
            Assert.True(WindowHarness.Until(() => main.IsVisible && main.ActualWidth > 0, TimeSpan.FromSeconds(10)));
            WindowHarness.Wait(TimeSpan.FromMilliseconds(500));
            Assert.Equal(0, main.SeenThrough);
            var solid = DrawnOverFront(main, 0.15, 0.4, 0.4, 0.6);
            _out.WriteLine($"over the front's left half: beside the studio R{seen.R:0} G{seen.G:0} B{seen.B:0} spread {seen.Spread:0.0} (look {sideLook}); "
                           + $"the Places tab's R{solid.R:0} G{solid.G:0} B{solid.B:0} spread {solid.Spread:0.0} (look {main.LookShown})");
            // Both drawn the same way (the game's look grades the colours alike in each).
            Assert.Equal(main.LookShown, sideLook);
            // The control: the near layer drawn as it is, one flat green - nothing of the front's checker under it.
            Assert.True(solid.Spread < 10, $"control: the Places tab's preview does not draw the near layer flat over the front (spread {solid.Spread:0.0})");
            // Beside the studio: the green fainter, the front's checker (half its squares lit) showing through it.
            Assert.True(seen.G < solid.G * 0.6, $"the near layer is drawn solid beside the studio (green {seen.G:0} against {solid.G:0})");
            Assert.True(seen.Spread > 40, $"the front layer's checker does not show through the near layer beside the studio (spread {seen.Spread:0.0})");

            // Control: the back layer, under the front layer and the near layer.
            ((Button)window.FindName("EditPlaceSecondaryMaskButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Pump();
            var back = Opened(window);
            Assert.True(WindowHarness.Until(() => back.Cover != null, TimeSpan.FromSeconds(20)), "the back layer's studio never learnt what covers it");
            _out.WriteLine($"back: {back.Cover!.Share:P1} covered");
            Assert.True(back.Cover.Share > 0.95, "control: the back layer under the opaque front layer is not counted covered");
            // Beside it, the front layer, the near layer and the sign are all in front of the back layer.
            var backSide = PreviewIn(back);
            Assert.True(WindowHarness.Until(() => backSide.SeenThrough >= 3, TimeSpan.FromSeconds(10)),
                        $"beside the back layer's studio {backSide.SeenThrough} pictures are see-through, not the front, the near layer and the sign");
            back.AnswerUnsaved = () => MessageBoxResult.No;
            back.TryClose();
            WindowHarness.Pump();
        });
    }

    /// <summary>
    /// The sign given a mask of its own, the level's kind, then the jiggle: chosen in the panel beside the picture,
    /// opened with its Edit button, a stroke across where the studio draws the top of its art - and the studio's place
    /// preview moves the top of the sign's art, and nothing else of it. Saved, the pack says so.
    /// </summary>
    [Theory]
    [InlineData(GameObjectDef.MaskLevel)]
    [InlineData(GameObjectDef.MaskJiggle)]
    public void An_objects_own_mask_painted_in_the_studio_moves_its_picture_in_the_preview(string kind)
    {
        InPlaces((window, vm, place) =>
        {
            var sign = place.GameObjects.First(g => g.Name == "Sign");
            vm.PickInPreview(sign);
            WindowHarness.Pump();
            var preview = (PlacePreview)window.FindName("PlacePreviewMain");
            var kindBox = Descendants<ComboBox>(preview).First(c => TutorialAnchor.GetId(c) == "field:objectMaskKind");
            kindBox.SelectedValue = kind;
            WindowHarness.Pump();
            Assert.Equal(kind, sign.MaskKind);
            var edit = Descendants<Button>(preview).First(b => TutorialAnchor.GetId(b) == "btn:editObjectMask");
            Assert.True(edit.IsVisible, "the Edit button is not shown for a mask of its own");
            edit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            WindowHarness.Pump();
            var studio = Opened(window);
            Assert.Equal(kind == GameObjectDef.MaskJiggle ? MaskKind.BustRgb : MaskKind.LevelAlpha, studio.Host.MaskKind);
            Stroke(studio, 0.1);
            Assert.True(studio.IsDirty, "the stroke painted nothing");
            Assert.NotNull(sign.LiveMask);
            WindowHarness.Pump();

            var studioPreview = PreviewIn(studio);
            var images = Field<Dictionary<GameObjectViewModel, Image>>(studioPreview, "_objectImages");
            Assert.True(images.ContainsKey(sign), "the studio's preview does not draw the sign");
            var (first, last, rows, height, jiggle) = MovedRows(studioPreview, images[sign]);
            _out.WriteLine($"{kind}: the {(jiggle ? "jiggle" : "level")} pass moves the sign's rows {first}..{last} ({rows} of {height})");
            Assert.Equal(kind == GameObjectDef.MaskJiggle, jiggle);
            Assert.True(rows > 5, "the stroke moves nothing of the sign");
            Assert.True(last < height / 4, $"the stroke over the top of the sign moves its rows {first}..{last}");

            studio.AskSavePath = () => Path.Combine(_dir, "Art", "sign-mask.png");
            studio.AnswerUnsaved = () => MessageBoxResult.Yes;
            studio.TryClose();
            WindowHarness.Pump();
            Assert.Equal("Art/sign-mask.png", sign.Mask);
            vm.SavePackCommand.Execute(null);
            var saved = PackRepository.Load(_dir).Places[0].GameObjects.First(g => g.Name == "Sign");
            Assert.Equal(kind, saved.MaskMeans);
            Assert.Equal("Art/sign-mask.png", saved.Mask);
            Assert.Equal(PlaceDef.CurrentObjectMasks, PackRepository.Load(_dir).Places[0].ObjectMasks);
        });
    }
}
