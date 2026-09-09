using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How large the Scenes preview draws a scene's art.
/// <para/>
/// Reported with two screenshots: the same scene correct in game, and in the
/// preview with the art around twice the size — spilling well past the frame
/// and clipped by the edge of the pane. Twice is the number that gives it
/// away: art authored at 512x512 rather than 256x256, drawn at its own pixel
/// size instead of at the size a scene occupies.
/// <para/>
/// The preview scales art the way the runtime does, so these check the two
/// agree — an author judging a scene against a preview that draws it at a
/// different size is worse served than one with no preview at all.
/// </summary>
public sealed class ScenePreviewScaleTests
{
    private readonly ITestOutputHelper _out;
    public ScenePreviewScaleTests(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// The art layer's width on screen.
    /// <para/>
    /// Measured through the LAYOUT size rather than the pixel count, because
    /// those are not the same number and only one of them is what the author
    /// sees. Stretch.None lays a picture out at its dpi-corrected size, so a
    /// file claiming 72 dpi is a third wider than its pixels - and pixel
    /// arithmetic here would have reported the right answer for art that was
    /// visibly spilling out of its frame.
    /// </summary>
    private static double DrawnWidth(ScenePreview preview) => Drawn(preview).Width;

    /// <summary>The art layer's drawn size.</summary>
    private static Size Drawn(ScenePreview preview)
    {
        var art = preview.Children.OfType<Image>().First();
        var source = (BitmapSource?)art.Source;
        Assert.NotNull(source);

        art.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return art.DesiredSize;
    }

    private static void WritePng(string path, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    [Fact]
    public void ArtOfAnySizeIsDrawnAtTheSizeOfA256Scene()
    {
        using var dir = new Scratch();
        WritePng(Path.Combine(dir.Path, "small.png"), 256, 256);
        WritePng(Path.Combine(dir.Path, "big.png"), 512, 512);
        WritePng(Path.Combine(dir.Path, "huge.png"), 1024, 1024);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path };

            preview.SceneSprite = "small.png";
            WindowHarness.Pump();
            double reference = DrawnWidth(preview);
            _out.WriteLine($"256 -> {reference}px");

            foreach (string bigger in new[] { "big.png", "huge.png" })
            {
                preview.SceneSprite = bigger;
                WindowHarness.Pump();
                double drawn = DrawnWidth(preview);
                _out.WriteLine($"{bigger} -> {drawn}px");

                // The reported symptom exactly: art at twice the size.
                Assert.Equal(reference, drawn, 3);
            }
        });
    }

    [Fact]
    public void UnevenArtKeepsItsShapeAndStaysInsideTheFrame()
    {
        // Raised alongside the video work: "the video I'm using is an uneven
        // resolution". The long side reaches the frame and the short side
        // falls inside it, rather than the art being stretched square.
        using var dir = new Scratch();
        WritePng(Path.Combine(dir.Path, "wide.png"), 512, 256);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview { PackRoot = dir.Path, SceneSprite = "wide.png" };
            WindowHarness.Pump();

            var drawn = Drawn(preview);
            double w = drawn.Width, h = drawn.Height;
            _out.WriteLine($"512x256 -> {w}x{h}px");

            // One factor for both axes: the aspect it was drawn at is the
            // aspect it is shown at.
            Assert.Equal(2.0, w / h, 3);

            // And the long side matches what a 256 square would occupy, so it
            // sits inside the frame rather than spilling past it.
            WritePng(Path.Combine(dir.Path, "square.png"), 256, 256);
            preview.SceneSprite = "square.png";
            WindowHarness.Pump();
            Assert.Equal(DrawnWidth(preview), w, 3);
        });
    }

    [Fact]
    public void TheArtIsDrawnBehindTheFrame()
    {
        // The other half of why oversized art looked so wrong: the border is
        // meant to cover the art's edges, which only works if it is in front.
        using var dir = new Scratch();
        WritePng(Path.Combine(dir.Path, "art.png"), 256, 256);

        WindowHarness.Run(_ =>
        {
            var preview = new ScenePreview
            {
                PackRoot = dir.Path,
                SceneSprite = "art.png",
                VanillaFrame = "PhotoFrame.png",
            };
            WindowHarness.Pump();

            var images = preview.Children.OfType<Image>().ToList();
            Assert.True(images.Count >= 2, "the frame layer is missing");

            // Later children draw on top, so the art must come first.
            int art = preview.Children.IndexOf(images[0]);
            int frame = preview.Children.IndexOf(images[1]);
            Assert.True(art < frame, "the frame is behind the art, so it cannot cover its edges");
        });
    }

    private sealed class Scratch : IDisposable
    {
        public string Path { get; }
        public Scratch()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                                          "smsmodforge-preview-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }
        public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
    }
}
