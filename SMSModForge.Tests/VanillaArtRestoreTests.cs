using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Bringing a shipped bust thumbnail back to the size it was authored at.
/// <para/>
/// The vanilla art ships downscaled — half a gigabyte of somebody else's
/// artwork is a poor trade for a sharper preview — so every bust makes a round
/// trip: reduced by 1.5 on the way into the build, and restored on the way onto
/// the screen. Both halves used to point-sample, and 1.5 is not an integer, so
/// each half dropped rows at uneven intervals. It did not read as low
/// resolution art; it deformed faces, and gave a character one eye larger than
/// the other.
/// <para/>
/// What is checked here is that the restore INTERPOLATES, because that is the
/// half that lives in this codebase and the half that can silently go back to
/// point sampling in one edit.
/// </summary>
public sealed class VanillaArtRestoreTests
{
    private readonly ITestOutputHelper _out;
    public VanillaArtRestoreTests(ITestOutputHelper o) => _out = o;

    /// <summary>A shipped bust thumbnail from the build output, or null when
    /// the art is not alongside the test run.</summary>
    private static string? AShippedBust()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "VanillaBustArt");
        if (!Directory.Exists(root)) return null;

        return Directory.EnumerateFiles(root, "Base.PNG", SearchOption.AllDirectories)
                        .OrderBy(p => p, StringComparer.Ordinal)
                        .FirstOrDefault();
    }

    private static BitmapSource Load(string path)
    {
        var made = new BitmapImage();
        using var file = File.OpenRead(path);
        made.BeginInit();
        made.CacheOption = BitmapCacheOption.OnLoad;
        made.StreamSource = file;
        made.EndInit();
        made.Freeze();
        return made;
    }

    /// <summary>Every distinct colour in an image.</summary>
    private static HashSet<uint> ColoursOf(BitmapSource img)
    {
        var conv = new FormatConvertedBitmap(img, System.Windows.Media.PixelFormats.Bgra32,
                                             null, 0);
        int stride = conv.PixelWidth * 4;
        var bytes = new byte[stride * conv.PixelHeight];
        conv.CopyPixels(bytes, stride, 0);

        var found = new HashSet<uint>();
        for (int i = 0; i < bytes.Length; i += 4)
            found.Add((uint)(bytes[i] | (bytes[i + 1] << 8) | (bytes[i + 2] << 16)
                             | (bytes[i + 3] << 24)));
        return found;
    }

    [Fact]
    public void AThumbnailComesBackAtTheSizeItWasAuthoredAt()
    {
        string? art = AShippedBust();
        if (art == null) { _out.WriteLine("no shipped bust art beside the tests"); return; }

        var thumb = Load(art);
        var restored = VanillaArtSizes.RestoreIfThumbnail(art, thumb);

        _out.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(art))}: "
                       + $"{thumb.PixelWidth}x{thumb.PixelHeight} -> "
                       + $"{restored.PixelWidth}x{restored.PixelHeight}");

        // The manifest records what it was before shipping, because the
        // division rounds and the original cannot be recovered from the
        // thumbnail. Everything downstream computes world size from these
        // numbers.
        Assert.True(restored.PixelWidth > thumb.PixelWidth);
        Assert.Equal(restored.PixelWidth, restored.PixelHeight);
    }

    [Fact]
    public void TheRestoreInterpolatesRatherThanRepeatingPixels()
    {
        // The whole point, and it is directly observable: a point upscale can
        // only ever repeat colours that were already there, so it introduces
        // none. Interpolating between neighbours produces values that appear
        // nowhere in the source - and that is what stops a 1.5x round trip
        // reading as damage.
        string? art = AShippedBust();
        if (art == null) { _out.WriteLine("no shipped bust art beside the tests"); return; }

        var thumb = Load(art);
        var restored = VanillaArtSizes.RestoreIfThumbnail(art, thumb);
        if (restored.PixelWidth == thumb.PixelWidth)
        {
            _out.WriteLine("this build ships full-resolution art; nothing to restore");
            return;
        }

        var before = ColoursOf(thumb);
        var after = ColoursOf(restored);
        var invented = after.Except(before).ToList();

        _out.WriteLine($"{before.Count} colours in the thumbnail, {after.Count} after "
                       + $"restoring, {invented.Count} of them new");

        Assert.NotEmpty(invented);
    }

    [Fact]
    public void ArtWithNoRecordedSizeIsHandedBackUntouched()
    {
        // The control. Pack art - an author's own busts - has no entry in the
        // manifest and must not be resampled on the way to the screen: it is
        // already the size it was drawn at, and touching it would soften
        // somebody's work for no reason.
        string dir = Path.Combine(Path.GetTempPath(), "smsforge-restore-"
                                                      + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "Base.PNG");
            var made = new WriteableBitmap(64, 64, 96, 96,
                                           System.Windows.Media.PixelFormats.Bgra32, null);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(made));
            using (var file = File.Create(path)) png.Save(file);

            var loaded = Load(path);
            var back = VanillaArtSizes.RestoreIfThumbnail(path, loaded);

            Assert.Same(loaded, back);
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
