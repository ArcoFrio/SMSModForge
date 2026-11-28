using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;
using SMSModForge.Rendering;
using SMSModForge.View;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A place's mask lands on the art it is painted over (the author: "some areas
/// inside that same area move differently from others ... If I paint too high,
/// I also see no movement at all on the uppermost branches of the tree"). The
/// level shader reads the mask at the art's own uv, so the studio has to show
/// the art spread over the whole mask; it showed it fitted inside the square,
/// between bands, from 1.1.0 on. Measured: where the studio DRAWS the art, a
/// stroke painted there by the mouse's own handlers, and which art the level
/// shader then moves.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class PlaceMaskOnTheArtTests : IDisposable
{
    private const int W = 2048, H = 1136;          // a place's art, as the game cuts it
    private readonly ITestOutputHelper _out;
    private readonly string _dir;

    public PlaceMaskOnTheArtTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmf-placemask-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "Locations"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* the temp folder goes in the end */ }
    }

    private enum Band { Top, Middle, Bottom }

    /// <summary>The top fifth red, the bottom fifth green, blue between - in
    /// stripes, so a sideways shift changes the pixels.</summary>
    private static Band BandAt(int y) => y < H / 5 ? Band.Top : y >= H - H / 5 ? Band.Bottom : Band.Middle;

    private static byte[] Art()
    {
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
        {
            var band = BandAt(y);
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                bool lit = x % 16 < 8;
                px[i] = (byte)(lit && band == Band.Middle ? 255 : 0);       // B
                px[i + 1] = (byte)(lit && band == Band.Bottom ? 255 : 0);   // G
                px[i + 2] = (byte)(lit && band == Band.Top ? 255 : 0);      // R
                px[i + 3] = 255;
            }
        }
        return px;
    }

    private static void SavePng(string path, byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        using var fs = File.Create(path);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        enc.Save(fs);
    }

    private static byte[] LoadNative(string path, out int w, out int h)
    {
        using var fs = File.OpenRead(path);
        var frame = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        w = bgra.PixelWidth; h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    /// <summary>The art pixels the game's level shader moves with this mask,
    /// over a few moments (a row can be still at one of them).</summary>
    private static bool[] Moved(byte[] art, byte[] mask, int maskW, int maskH)
    {
        var moved = new bool[W * H];
        var output = new byte[W * H * 4];
        foreach (float t in new[] { 0.3f, 0.75f, 1.2f, 1.9f })
        {
            MilkingShader.Render(art, W, H, mask, maskW, maskH, MilkingShader.Settings.Level, t, output, W, H);
            for (int i = 0, j = 0; i < moved.Length; i++, j += 4)
                if (Math.Abs(output[j] - art[j]) > 8 || Math.Abs(output[j + 1] - art[j + 1]) > 8 || Math.Abs(output[j + 2] - art[j + 2]) > 8)
                    moved[i] = true;
        }
        return moved;
    }

    private static string Shares(bool[] moved)
    {
        int top = 0, middle = 0, bottom = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (moved[y * W + x])
                    switch (BandAt(y)) { case Band.Top: top++; break; case Band.Middle: middle++; break; default: bottom++; break; }
        return $"top {top}, middle {middle}, bottom {bottom}";
    }

    private static (int first, int last) RowsMoved(bool[] moved)
    {
        int first = -1, last = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (moved[y * W + x]) { if (first < 0) first = y; last = y; break; }
        return (first, last);
    }

    private void WithStudio(PlaceViewModel place, Action<MaskStudio, IMaskEditorHost> body)
    {
        WindowHarness.Run(_ =>
        {
            var host = place.BaseMaskHost;
            var studio = new MaskStudio(host, _dir, "Mask: place", "test", null);
            var window = new Window
            {
                Content = studio, Width = 1300, Height = 820,
                Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false,
            };
            try
            {
                window.Show();
                WindowHarness.Pump();
                window.UpdateLayout();
                body(studio, host);
            }
            finally
            {
                studio.AnswerUnsaved = () => MessageBoxResult.No;
                studio.TryClose();
                window.Close();
                WindowHarness.Pump();
                Mouse.OverrideCursor = null;
            }
        });
    }

    /// <summary>The studio's canvas as drawn, at the canvas's own size.</summary>
    private static (byte[] px, int w, int h) Drawn(MaskStudio studio)
    {
        var image = (FrameworkElement)studio.FindName("MaskImage");
        image.UpdateLayout();
        int w = (int)Math.Round(image.ActualWidth), h = (int)Math.Round(image.ActualHeight);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new VisualBrush(image), null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return (px, w, h);
    }

    /// <summary>The canvas rows where the art's top band (red) is drawn.</summary>
    private static (int first, int last) RedRows(byte[] px, int w, int h)
    {
        int first = -1, last = -1;
        for (int y = 0; y < h; y++)
        {
            int red = 0;
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (px[i + 2] > 60 && px[i + 2] > px[i] + 40 && px[i + 2] > px[i + 1] + 40) red++;
            }
            if (red > w / 8) { if (first < 0) first = y; last = y; }
        }
        return (first, last);
    }

    private static MouseButtonEventArgs Button(MouseButton which, RoutedEvent routed)
        => new(Mouse.PrimaryDevice, Environment.TickCount, which) { RoutedEvent = routed };

    private static MouseEventArgs Move()
        => new(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent };

    /// <summary>
    /// A stroke across the middle of where the studio draws the top of the art
    /// moves the top of the art, and only it. With the art fitted inside the
    /// square (the bug) the stroke moved the middle of the art instead.
    /// </summary>
    [Fact]
    public void AStrokeOnTheTopOfThePlacesArt_MovesTheTopOfTheArt()
    {
        var art = Art();
        SavePng(Path.Combine(_dir, "Locations", "Art.png"), art, W, H);
        var place = new PlaceViewModel(new PlaceDef { Key = "P", BaseSprite = "Locations/Art.png" });
        WithStudio(place, (studio, host) =>
        {
            var (px, w, h) = Drawn(studio);
            var (redFirst, redLast) = RedRows(px, w, h);
            _out.WriteLine($"canvas {w}x{h}; the art's top fifth drawn on rows {redFirst}..{redLast}");
            Assert.True(redFirst >= 0, "the art's top band is not drawn on the canvas");

            // Drawn where the art is: its top fifth from the canvas's top edge down a fifth of it.
            Assert.True(redFirst <= 2, $"the art's top is drawn {redFirst} rows below the canvas's top: there is a band above it");
            Assert.InRange(redLast, h / 5 - 3, h / 5 + 2);

            // The stroke, by the mouse's handlers, across the middle of the red rows as drawn.
            var canvas = (FrameworkElement)studio.FindName("CanvasHost");
            ((Slider)studio.FindName("SizeSlider")).Value = 6;
            ((Slider)studio.FindName("HardnessSlider")).Value = 1;
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            double scaleX = canvas.ActualWidth / w, scaleY = canvas.ActualHeight / h;
            double row = (redFirst + redLast) / 2.0 * scaleY;
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
            Assert.True(studio.IsDirty, "the stroke painted nothing");

            var live = host.LiveMaskBgra;
            Assert.NotNull(live);
            var moved = Moved(art, live!, MaskBuffer.Size, MaskBuffer.Size);
            var (first, last) = RowsMoved(moved);
            _out.WriteLine($"the shader moves art rows {first}..{last} ({Shares(moved)}); the top fifth is rows 0..{H / 5 - 1}");
            int all = moved.Count(m => m);
            int onTop = 0;
            for (int y = 0; y < H / 5; y++)
                for (int x = 0; x < W; x++)
                    if (moved[y * W + x]) onTop++;
            Assert.True(all > 20000, $"the stroke moved only {all} art pixels");
            Assert.True(onTop >= all * 0.98, $"of the {all} art pixels the stroke moved, {all - onTop} are outside the top fifth it was painted over ({Shares(moved)})");
        });
    }

    /// <summary>
    /// A mask the size of the art (painted elsewhere), opened in the studio and
    /// saved, moves the same art it did. With the mask fitted inside the square
    /// (the bug) the saved one moved a squeezed stretch nearer the middle.
    /// </summary>
    [Fact]
    public void AMaskTheSizeOfTheArt_SavedFromTheStudio_MovesTheSameArt()
    {
        var art = Art();
        SavePng(Path.Combine(_dir, "Locations", "Art.png"), art, W, H);
        // Strong over the left half of the top fifth, nothing elsewhere.
        var mask = new byte[W * H * 4];
        for (int y = 0; y < H / 5; y++)
            for (int x = 0; x < W / 2; x++)
            {
                int i = (y * W + x) * 4;
                mask[i] = mask[i + 1] = mask[i + 2] = mask[i + 3] = 255;
            }
        string maskPath = Path.Combine(_dir, "Locations", "ArtMask.png");
        SavePng(maskPath, mask, W, H);
        var before = Moved(art, mask, W, H);

        var place = new PlaceViewModel(new PlaceDef { Key = "P", BaseSprite = "Locations/Art.png", MaskSprite = "Locations/ArtMask.png" });
        WithStudio(place, (studio, host) =>
        {
            Assert.True(studio.Save(), "the studio did not save the mask");
        });

        var saved = LoadNative(maskPath, out int mw, out int mh);
        var after = Moved(art, saved, mw, mh);
        int both = 0, either = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] && after[i]) both++;
            if (before[i] || after[i]) either++;
        }
        _out.WriteLine($"the mask as painted moves rows {RowsMoved(before)}, saved from the studio ({mw}x{mh}) rows {RowsMoved(after)}; "
                       + $"the same pixels {both} of {either}");
        Assert.True(either > 0 && both >= either * 0.97, $"the saved mask moves other art: the same pixels at only {both} of {either}");
    }
}
