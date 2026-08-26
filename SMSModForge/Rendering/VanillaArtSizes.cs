using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace SMSModForge.Rendering;

/// <summary>
/// Puts the shipped vanilla art back to the size it was extracted at.
/// <para/>
/// The vanilla art ships at quarter scale — 450 MB of the game's artwork
/// reduced to 55 MB, since it exists only so a preview can show what a pack
/// borrows. Nothing else reads it: the pack plugin never touches it, and a
/// pack builds, validates and runs identically without it.
/// <para/>
/// The preview lays images out by their pixel dimensions
/// (<c>PixelWidth / ppu * WorldPpu</c>), and vanilla art travels through the
/// very same path as the author's own art, at full size. Left alone, a
/// borrowed level would therefore draw at a quarter the size of everything
/// around it. Rescaling on load — rather than teaching the layout about two
/// different scales — keeps that difference from leaking anywhere: every
/// caller sees the dimensions it always saw, and only sharpness changed.
/// <para/>
/// The original sizes come from a manifest rather than from multiplying by
/// four, because the downscale rounds down: a 223px sign becomes 55, and 55
/// times four is 220. Small enough not to see, large enough to be a drift
/// nobody would ever track down.
/// </summary>
internal static class VanillaArtSizes
{
    private sealed class Manifest
    {
        // "scale" is in the file too, but deliberately not modelled here: it
        // became a per-folder object when the two art sets started using
        // different ratios, and a mismatched type would throw and take the
        // whole manifest with it. Nothing needs it — the originals are what
        // the restore reads, and they are absolute.
        [JsonProperty("originals")] public Dictionary<string, int[]>? Originals { get; set; }
    }

    private static readonly object Gate = new();
    private static bool _loaded;
    private static Dictionary<string, int[]>? _originals;

    /// <summary>Manifest keys are relative to the Resources folder the
    /// generator walked, e.g. <c>VanillaLevelArt/3_LivingRoom/Base.PNG</c>.
    /// At run time the same files sit directly beside the exe, so the key is
    /// the path below the output directory.</summary>
    private static string? KeyFor(string absPath)
    {
        string root = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(root)) return null;

        string full;
        try { full = Path.GetFullPath(absPath); } catch { return null; }
        root = Path.GetFullPath(root);

        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return full.Substring(root.Length)
                   .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Replace('\\', '/');
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "VanillaArtSizes.json");
                if (!File.Exists(path)) return;      // full-res build; nothing to undo
                var m = JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(path));
                if (m?.Originals != null)
                    _originals = new Dictionary<string, int[]>(m.Originals, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // A missing or malformed manifest means the art draws at its
                // own size — softer and smaller, never absent.
            }
        }
    }

    /// <summary>
    /// The image at its original dimensions when it is a shipped thumbnail,
    /// otherwise exactly what was passed in.
    /// <para/>
    /// Pack art, and any build that still ships full-resolution vanilla art,
    /// fall through untouched — there is no entry for them in the manifest.
    /// </summary>
    public static BitmapSource RestoreIfThumbnail(string absPath, BitmapSource img)
    {
        EnsureLoaded();
        if (_originals == null || img == null) return img;

        string? key = KeyFor(absPath);
        if (key == null || !_originals.TryGetValue(key, out var wh) || wh.Length != 2) return img;

        int w = wh[0], h = wh[1];
        if (w <= 0 || h <= 0) return img;
        if (img.PixelWidth == w && img.PixelHeight == h) return img;

        return PointUpscale(img, w, h);
    }

    /// <summary>
    /// Nearest-neighbour resize to <paramref name="w"/>x<paramref name="h"/>.
    /// <para/>
    /// <c>TransformedBitmap</c> would be less code, but it interpolates, and a
    /// four-times upscale of a thumbnail through a smoothing filter looks like
    /// a blurred photograph rather than like art shown at low resolution. Point
    /// sampling is honest about what it is: blocky, obviously a preview, and
    /// never mistakable for the real asset.
    /// <para/>
    /// The previews already set <c>BitmapScalingMode.NearestNeighbor</c> for
    /// their on-screen scaling, so this keeps the two halves of the pipeline
    /// agreeing rather than smoothing here and point-sampling a step later.
    /// </summary>
    private static BitmapSource PointUpscale(BitmapSource img, int w, int h)
    {
        var conv = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
        int sw = conv.PixelWidth, sh = conv.PixelHeight;
        if (sw <= 0 || sh <= 0) return img;

        int srcStride = sw * 4;
        var src = new byte[srcStride * sh];
        conv.CopyPixels(src, srcStride, 0);

        int dstStride = w * 4;
        var dst = new byte[dstStride * h];
        for (int y = 0; y < h; y++)
        {
            int sy = (int)((long)y * sh / h);
            int srcRow = sy * srcStride;
            int dstRow = y * dstStride;
            for (int x = 0; x < w; x++)
            {
                int si = srcRow + (int)((long)x * sw / w) * 4;
                int di = dstRow + x * 4;
                dst[di]     = src[si];
                dst[di + 1] = src[si + 1];
                dst[di + 2] = src[si + 2];
                dst[di + 3] = src[si + 3];
            }
        }

        var outBmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, dst, dstStride);
        outBmp.Freeze();
        return outBmp;
    }
}
