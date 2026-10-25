using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Model;

namespace SMSModForge.Rendering;

/// <summary>
/// A character's face, small, for the Actor picker (the author, 1.8.0: "very
/// small previews in the dropdown options ... maybe adding just the face of the
/// characters but I don't know if we're able to determine what the
/// character's face should really be").
/// <para/>
/// Where the face is, is read off the bust itself: its blink picture is the
/// eyes and nothing else, drawn on the same canvas as the base, so the square
/// around them is the face - for the game's busts and a pack's alike. A blink
/// that does not land on the bust (Evelyn's alien borrows a crocodile-man's,
/// floating above its head) or no blink at all falls back to the top of the
/// visible art, which is a head on every bust that has one and still the
/// creature on one that does not. Nothing visible there at all, or no art: no
/// face, and the picker shows a neutral one.
/// <para/>
/// Safe off the UI thread: everything here works on pixels and hands back a
/// frozen picture. Remembered by file, size and time written.
/// </summary>
public static class CharacterFace
{
    /// <summary>Pixels a side of the face handed back; drawn at half that on
    /// a normal screen, so it stays sharp on a scaled one.</summary>
    public const int Side = 48;

    /// <summary>How wide the bust is decoded to find the face: the game's own
    /// copies are this size, and a pack's two-thousand-pixel bust needs no more
    /// to find a pair of eyes.</summary>
    private const int Work = 256;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, BitmapSource?> Known = new();

    /// <summary>The bust files a character's face is cut from: the outfit it
    /// wears by default, else its first. Null when it has no bust.</summary>
    public static (string Base, string? Blink)? FilesOf(CharacterDef character, ModPack pack, string? packRoot)
    {
        var outfit = character.Outfits.FirstOrDefault(o => string.Equals(o.Key, character.DefaultOutfit, StringComparison.OrdinalIgnoreCase))
                     ?? character.Outfits.FirstOrDefault();
        if (outfit == null || string.IsNullOrEmpty(outfit.GameObjectName)) return null;
        string? basePath = VanillaArtResolver.FindBaseSpritePath(outfit.GameObjectName, pack, packRoot);
        if (basePath == null) return null;
        return (basePath, VanillaArtResolver.FindBlinkSpritePath(outfit, packRoot));
    }

    /// <summary>The face in <paramref name="basePath"/>, using
    /// <paramref name="blinkPath"/> to find it; null when there is none to show.</summary>
    public static BitmapSource? Cut(string basePath, string? blinkPath)
    {
        string key = Stamp(basePath) + "|" + (blinkPath == null ? "" : Stamp(blinkPath));
        lock (Gate)
            if (Known.TryGetValue(key, out var known)) return known;

        BitmapSource? face = null;
        try { face = CutNow(basePath, blinkPath); }
        catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or InvalidOperationException
                                    or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }

        lock (Gate) Known[key] = face;
        return face;
    }

    private static string Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : path;
    }

    private sealed class Pixels
    {
        public int Width, Height;
        public byte[] Bgra = Array.Empty<byte>();
        public byte Alpha(int x, int y) => Bgra[(y * Width + x) * 4 + 3];
    }

    private static Pixels Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = Work;
        image.EndInit();
        image.Freeze();
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var p = new Pixels { Width = bgra.PixelWidth, Height = bgra.PixelHeight };
        p.Bgra = new byte[p.Width * p.Height * 4];
        bgra.CopyPixels(p.Bgra, p.Width * 4, 0);
        return p;
    }

    /// <summary>The smallest box holding every pixel more than faintly drawn,
    /// or null.</summary>
    private static (int L, int T, int R, int B)? Drawn(Pixels p, int left = 0, int top = 0, int right = -1, int bottom = -1)
    {
        if (right < 0) right = p.Width;
        if (bottom < 0) bottom = p.Height;
        int l = int.MaxValue, t = int.MaxValue, r = -1, b = -1;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                if (p.Alpha(x, y) > 16)
                {
                    if (x < l) l = x;
                    if (x > r) r = x;
                    if (y < t) t = y;
                    if (y > b) b = y;
                }
        return r < 0 ? null : (l, t, r + 1, b + 1);
    }

    private static BitmapSource? CutNow(string basePath, string? blinkPath)
    {
        var bust = Load(basePath);
        var art = Drawn(bust);
        if (art == null) return null;

        double cx, top, side;
        var eyes = blinkPath == null ? null : EyesOn(bust, Load(blinkPath));
        if (eyes is { } e)
        {
            // About two and a half eye-spans is a face, a little more below the
            // eyes than above them.
            cx = (e.L + e.R) / 2.0;
            side = Math.Max((e.R - e.L) * 2.4, 24);
            top = (e.T + e.B) / 2.0 - side * 0.42;
        }
        else
        {
            // The top of whatever is drawn, centred on what is drawn in its top
            // band: a head, or the top of something with none.
            var (l, t, r, b) = art.Value;
            side = Math.Min(r - l, b - t) * 0.5;
            var band = Drawn(bust, l, t, r, Math.Min(b, t + Math.Max(1, (b - t) * 15 / 100))) ?? art.Value;
            cx = (band.L + band.R) / 2.0;
            top = t;
        }

        return Square(bust, cx - side / 2, top, side);
    }

    /// <summary>The blink's eyes, in the bust's pixels - or null when they do
    /// not land on anything the bust draws.</summary>
    private static (int L, int T, int R, int B)? EyesOn(Pixels bust, Pixels blink)
    {
        var drawn = Drawn(blink);
        if (drawn is not { } d) return null;
        double sx = (double)bust.Width / blink.Width, sy = (double)bust.Height / blink.Height;
        var eyes = ((int)(d.L * sx), (int)(d.T * sy), (int)Math.Ceiling(d.R * sx), (int)Math.Ceiling(d.B * sy));
        int cx = Math.Clamp((eyes.Item1 + eyes.Item3) / 2, 0, bust.Width - 1);
        int cy = Math.Clamp((eyes.Item2 + eyes.Item4) / 2, 0, bust.Height - 1);
        return bust.Alpha(cx, cy) > 16 ? eyes : null;
    }

    /// <summary>The square from (<paramref name="left"/>, <paramref name="top"/>)
    /// of side <paramref name="side"/>, averaged down to <see cref="Side"/>;
    /// what falls outside the bust is clear. Null when almost nothing in it is
    /// drawn.</summary>
    private static BitmapSource? Square(Pixels bust, double left, double top, double side)
    {
        var output = new byte[Side * Side * 4];
        double step = side / Side;
        int drawn = 0;
        for (int oy = 0; oy < Side; oy++)
            for (int ox = 0; ox < Side; ox++)
            {
                // Area average over the source pixels this one covers, with
                // alpha weighting so a clear edge does not darken the colour.
                double x0 = left + ox * step, y0 = top + oy * step;
                int xa = (int)Math.Floor(x0), ya = (int)Math.Floor(y0);
                int xb = Math.Max(xa + 1, (int)Math.Ceiling(x0 + step)), yb = Math.Max(ya + 1, (int)Math.Ceiling(y0 + step));
                double a = 0, r = 0, g = 0, bl = 0; int n = 0;
                for (int y = ya; y < yb; y++)
                    for (int x = xa; x < xb; x++)
                    {
                        n++;
                        if (x < 0 || y < 0 || x >= bust.Width || y >= bust.Height) continue;
                        int i = (y * bust.Width + x) * 4;
                        double pa = bust.Bgra[i + 3] / 255.0;
                        a += pa; bl += bust.Bgra[i] * pa; g += bust.Bgra[i + 1] * pa; r += bust.Bgra[i + 2] * pa;
                    }
                int o = (oy * Side + ox) * 4;
                if (a > 0)
                {
                    output[o] = (byte)(bl / a); output[o + 1] = (byte)(g / a); output[o + 2] = (byte)(r / a);
                    output[o + 3] = (byte)Math.Round(255 * a / n);
                    if (output[o + 3] > 16) drawn++;
                }
            }
        if (drawn < Side * Side / 10) return null;
        var bitmap = BitmapSource.Create(Side, Side, 96, 96, PixelFormats.Bgra32, null, output, Side * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
