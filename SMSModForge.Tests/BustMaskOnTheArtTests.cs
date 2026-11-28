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
using SMSModForge.View.Controls;
using SMSModForge.ViewModel;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A bust's or an NPC's mask lands on the art it is painted over when the art is
/// not square (as PlaceMaskOnTheArtTests, for places). The game reads every mask
/// at its art's own uv - the plugin cuts the sprite from the whole texture and
/// binds the mask to its material's _MaskTex, and the bust program reads it at
/// uv x _MaskTex_ST - so a 256x256 mask is spread over a 512x1024 pose. The mask
/// studio showed such art fitted inside the square, between bands, and the
/// Busts tab's preview laid a square mask over the whole square rather than over
/// the art fitted in it. Measured: where the studio draws the art, a stroke by
/// the mouse's own handlers, and what the bust shader then moves; the preview's
/// mask as its render path reads it.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class BustMaskOnTheArtTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir;
    private static readonly (float, float, float, float) White = (1f, 1f, 1f, 1f);

    public BustMaskOnTheArtTests(ITestOutputHelper o)
    {
        _out = o;
        _dir = Path.Combine(Path.GetTempPath(), "smsmf-bustmask-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "Art"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* the temp folder goes in the end */ }
    }

    // ───────────────────────── art and files

    /// <summary>A checker, so a shift any way changes pixels; the left fifth red,
    /// the right fifth green, blue between.</summary>
    private static byte[] Pose(int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bool lit = (x / 8 + y / 8) % 2 == 0;
                int band = x < w / 5 ? 0 : x >= w - w / 5 ? 2 : 1;
                px[i] = (byte)(lit && band == 1 ? 255 : 0);
                px[i + 1] = (byte)(lit && band == 2 ? 255 : 0);
                px[i + 2] = (byte)(lit && band == 0 ? 255 : 0);
                px[i + 3] = 255;
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

    /// <summary>A mask read as a texture is read at the art's uv: at each art
    /// pixel's centre, the mask texel there (point-sampled).</summary>
    private static byte[] AtTheArtsUv(byte[] mask, int mw, int mh, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int my = Math.Min(mh - 1, (int)((y + 0.5) / h * mh));
            for (int x = 0; x < w; x++)
            {
                int mx = Math.Min(mw - 1, (int)((x + 0.5) / w * mw));
                Buffer.BlockCopy(mask, (my * mw + mx) * 4, px, (y * w + x) * 4, 4);
            }
        }
        return px;
    }

    /// <summary>The pixels the bust shader moves with this mask over this art
    /// (both on one grid), against the same art with no mask, over a few moments.</summary>
    private static bool[] Moved(byte[] art, byte[] mask, int w, int h, int outW, int outH)
    {
        var p = JiggleParams.Defaults;
        var still = new byte[outW * outH * 4];
        var now = new byte[outW * outH * 4];
        JiggleShader.Render(art, new byte[mask.Length], w, h, p, White, 0f, still, outW, outH);
        var moved = new bool[outW * outH];
        foreach (float t in new[] { 0.2f, 0.55f, 0.9f, 1.4f })
        {
            JiggleShader.Render(art, mask, w, h, p, White, t, now, outW, outH);
            for (int i = 0, j = 0; i < moved.Length; i++, j += 4)
                if (Math.Abs(now[j] - still[j]) > 8 || Math.Abs(now[j + 1] - still[j + 1]) > 8 || Math.Abs(now[j + 2] - still[j + 2]) > 8)
                    moved[i] = true;
        }
        return moved;
    }

    // ───────────────────────── the studio

    private void WithStudio(IMaskEditorHost host, Action<MaskStudio> body)
    {
        WindowHarness.Run(_ =>
        {
            var studio = new MaskStudio(host, _dir, "Mask: n", "test", null);
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
                body(studio);
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

    /// <summary>The canvas columns where the art's left fifth (red) is drawn.</summary>
    private static (int first, int last) RedColumns(byte[] px, int w, int h)
    {
        int first = -1, last = -1;
        for (int x = 0; x < w; x++)
        {
            int red = 0;
            for (int y = 0; y < h; y++)
            {
                int i = (y * w + x) * 4;
                if (px[i + 2] > 60 && px[i + 2] > px[i] + 40 && px[i + 2] > px[i + 1] + 40) red++;
            }
            if (red > h / 8) { if (first < 0) first = x; last = x; }
        }
        return (first, last);
    }

    private static MouseButtonEventArgs Button(MouseButton which, RoutedEvent routed)
        => new(Mouse.PrimaryDevice, Environment.TickCount, which) { RoutedEvent = routed };

    private static MouseEventArgs Move()
        => new(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseMoveEvent };

    /// <summary>
    /// On an NPC pose twice as tall as wide (the author's shower poses are
    /// 512x1024), a stroke down the middle of where the studio draws the pose's
    /// left fifth moves that fifth, and nothing else, in the game's uv. With the
    /// pose fitted inside the square (the bug) it moved the middle instead.
    /// </summary>
    [Fact]
    public void AStrokeOnATallPose_MovesWhatItWasPaintedOver()
    {
        const int W = 512, H = 1024;
        var pose = Pose(W, H);
        SavePng(Path.Combine(_dir, "Art", "Pose.png"), pose, W, H);
        var npc = new NpcViewModel(new NpcDef { Key = "n", Sprite = "Art/Pose.png" });
        WithStudio(npc, studio =>
        {
            var (px, w, h) = Drawn(studio);
            var (first, last) = RedColumns(px, w, h);
            _out.WriteLine($"canvas {w}x{h}; the pose's left fifth drawn on columns {first}..{last}");
            Assert.True(first >= 0, "the pose's left fifth is not drawn on the canvas");
            Assert.True(first <= 2, $"the pose's left edge is drawn {first} columns in from the canvas's: there is a band beside it");
            Assert.InRange(last, w / 5 - 3, w / 5 + 2);

            var canvas = (FrameworkElement)studio.FindName("CanvasHost");
            ((Slider)studio.FindName("SizeSlider")).Value = 6;
            ((Slider)studio.FindName("HardnessSlider")).Value = 1;
            ((Slider)studio.FindName("OpacitySlider")).Value = 1;
            double column = (first + last) / 2.0 * canvas.ActualWidth / w;
            var at = new Point(column, canvas.ActualHeight * 0.1);
            bool held = true;
            studio.PointerOnCanvas = _ => at;
            studio.LeftHeld = _ => held;
            canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseDownEvent));
            for (int step = 1; step <= 8; step++)
            {
                at = new Point(column, canvas.ActualHeight * (0.1 + 0.1 * step));
                canvas.RaiseEvent(Move());
            }
            held = false;
            canvas.RaiseEvent(Button(MouseButton.Left, UIElement.MouseUpEvent));
            Assert.True(studio.IsDirty, "the stroke painted nothing");

            // As the game draws it: the pose at its own size, the mask read at its uv.
            var live = npc.LiveMaskBgra;
            Assert.NotNull(live);
            var moved = Moved(pose, AtTheArtsUv(live!, MaskBuffer.Size, MaskBuffer.Size, W, H), W, H, W, H);
            int all = 0, left = 0, firstX = W, lastX = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (moved[y * W + x])
                    {
                        all++;
                        if (x < W / 5) left++;
                        firstX = Math.Min(firstX, x); lastX = Math.Max(lastX, x);
                    }
            _out.WriteLine($"the shader moves pose columns {firstX}..{lastX}, {left} of {all} pixels in the left fifth (columns 0..{W / 5 - 1})");
            Assert.True(all > 5000, $"the stroke moved only {all} pixels of the pose");
            Assert.True(left >= all * 0.98, $"of the {all} pose pixels the stroke moved, {all - left} are outside the left fifth it was painted over");
        });
    }

    /// <summary>
    /// An NPC's mask at its pose's size (painted elsewhere, as the author's
    /// shower and sunbathing poses' are), opened in the studio and saved, moves
    /// the same pixels of the pose in the game as it did. With the mask fitted
    /// inside the square (the bug) the saved one moved a squeezed copy.
    /// </summary>
    [Fact]
    public void ATallNpcMask_SavedFromTheStudio_MovesTheSamePose()
    {
        const int W = 512, H = 1024;
        var pose = Pose(W, H);
        SavePng(Path.Combine(_dir, "Art", "Pose.png"), pose, W, H);
        // Bounce over the left fifth's top half, nothing elsewhere.
        var mask = new byte[W * H * 4];
        for (int y = 0; y < H / 2; y++)
            for (int x = 0; x < W / 5; x++)
            {
                int i = (y * W + x) * 4;
                mask[i + 2] = 255;
                mask[i + 3] = 255;
            }
        string maskPath = Path.Combine(_dir, "Art", "PoseMask.png");
        SavePng(maskPath, mask, W, H);
        var before = Moved(pose, mask, W, H, W, H);

        var npc = new NpcViewModel(new NpcDef { Key = "n", Sprite = "Art/Pose.png", Mask = "Art/PoseMask.png" });
        WithStudio(npc, studio => Assert.True(studio.Save(), "the studio did not save the mask"));

        var saved = LoadNative(maskPath, out int mw, out int mh);
        var after = Moved(pose, AtTheArtsUv(saved, mw, mh, W, H), W, H, W, H);
        int both = 0, either = 0;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] && after[i]) both++;
            if (before[i] || after[i]) either++;
        }
        _out.WriteLine($"saved {mw}x{mh}: the same pose pixels moved at {both} of {either}");
        Assert.True(either > 0 && both >= either * 0.95, $"the saved mask moves other pixels of the pose: the same at only {both} of {either}");
    }

    // ───────────────────────── the Busts tab's preview

    /// <summary>The share of the pixels the preview's shader moves that lie in
    /// the art's top quarter: the art 512x256, fitted in the frame (its rows
    /// 64..191 of 256, 128..383 of the 512 drawn).</summary>
    private static (int all, int inTopQuarter) MovedOnWideArt(byte[] frameBase, byte[] frameMask)
    {
        int n = JiggleShader.RenderSize;
        var moved = Moved(frameBase, frameMask, JiggleShader.Size, JiggleShader.Size, n, n);
        int all = 0, top = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (moved[y * n + x])
                {
                    all++;
                    double f = (y + 0.5 - n / 4.0) / (n / 2.0);   // down the art, 0 at its top
                    if (f >= 0 && f < 0.25) top++;
                }
        return (all, top);
    }

    /// <summary>A 256x256 mask with bounce over its top quarter: in the game,
    /// over the art's top quarter whatever the art's shape.</summary>
    private static byte[] TopQuarterMask()
    {
        int s = MaskBuffer.Size;
        var mask = new byte[s * s * 4];
        for (int y = 0; y < s / 4; y++)
            for (int x = 0; x < s; x++)
            {
                int i = (y * s + x) * 4;
                mask[i + 2] = 255;
                mask[i + 3] = 255;
            }
        return mask;
    }

    /// <summary>
    /// On bust art twice as wide as tall, the Busts tab's preview moves the art
    /// the mask covers in the game - its file's, and the studio's while it is
    /// being painted. Until now the square mask lay over the whole square, the
    /// art fitted in its middle: its top quarter moved nothing of the art.
    /// </summary>
    [Fact]
    public void OnWideBustArt_ThePreviewMovesWhatTheMaskCoversInTheGame()
    {
        SavePng(Path.Combine(_dir, "Art", "Wide.png"), Pose(512, 256), 512, 256);
        SavePng(Path.Combine(_dir, "Art", "WideMask.png"), TopQuarterMask(), MaskBuffer.Size, MaskBuffer.Size);
        var outfit = new OutfitViewModel(new OutfitDef
        {
            Key = "o", GameObjectName = "O", BaseSprite = "Art/Wide.png", MaskSprite = "Art/WideMask.png",
        });
        WindowHarness.Run(_ =>
        {
            var preview = new JigglePreview { PackRoot = _dir, Outfit = outfit };
            WindowHarness.Pump();
            var loaded = preview.Loaded();
            Assert.NotNull(loaded.Base);

            var (all, top) = MovedOnWideArt(loaded.Base!, preview.MaskDrawn());
            _out.WriteLine($"the mask's file: {top} of the {all} pixels moved lie in the art's top quarter");
            Assert.True(all > 5000, $"the mask's top quarter moved only {all} pixels of the art");
            Assert.True(top >= all * 0.98, $"of the {all} pixels the mask moved, {all - top} lie outside the art's top quarter");

            // While the studio paints: its buffer, on the art's uv as the studio's grid is.
            outfit.LiveMaskBgra = TopQuarterMask();
            var (liveAll, liveTop) = MovedOnWideArt(loaded.Base!, preview.MaskDrawn());
            _out.WriteLine($"the mask being painted: {liveTop} of the {liveAll} pixels moved lie in the art's top quarter");
            Assert.True(liveAll > 5000, $"the mask being painted moved only {liveAll} pixels of the art");
            Assert.True(liveTop >= liveAll * 0.98, $"of the {liveAll} pixels the mask being painted moved, {liveAll - liveTop} lie outside the art's top quarter");
            outfit.LiveMaskBgra = null;
        });
    }
}
