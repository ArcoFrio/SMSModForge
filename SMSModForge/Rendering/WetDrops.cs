using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMSModForge.Rendering;

/// <summary>
/// The wet drops a pack's NPC carries (after 1.10.0, the author: "Could we implement a proper preview of the wet
/// effects? Currently we have only an approximation that doesn't at all look like the game's"): the game's own
/// particle system (Anna_Towel/MBase1/Particle System, which NpcFactory.BuildWet clones), worked out from time alone.
/// <para/>
/// Every number is the game's, read with the Debug bridge (<c>particles</c>, <c>particles-live</c>) and held to its
/// screen by InGameSuiteTests.S17:
/// <list type="bullet">
/// <item>Two drops a second, each living five seconds, born at even steps of half a second - so no state is needed:
/// the drops at a moment are the ones born in the five seconds before it.</item>
/// <item>Each starts anywhere in a box 3.21 x 8.45 units round the Wet object, does not move at first, and falls
/// straight down at 9.81 units a second squared, however the Wet object is turned. Most are below the screen within
/// a second and a half: about two or three are seen at a time.</item>
/// <item>A drop is a picture 0.04 wide and 0.5 tall (a thin streak), white, the particle texture's alpha over it,
/// fading in over its first 14.4% of life and out over its last 12%.</item>
/// <item>The system is scaled by its own object only (scaling Local): the NPC's size and the level's are not in the
/// box, the drop or the fall. The Wet object's own scale is, axis by axis. Where it is and how it is turned come
/// from everything above it.</item>
/// <item>Drawn unlit, on the Bust sorting layer - over everything a level draws - blended in linear light over the
/// lit place, before the post-processing; read from the texture's smaller copy the drop's size on screen calls
/// for (at the game's 1080p screen a drop is 4 pixels wide, so its 64-pixel texture is read from the 4x4 copy:
/// the streak's even brightness along its length).</item>
/// </list>
/// Positions here are the place preview's canvas: 100 pixels a unit, y down.
/// </summary>
public static class WetDrops
{
    // ── The game's numbers ──────────────────────────────────────────────
    // The towel's first system (2_Bust_Manager/Anna_Towel/MBase1/Particle System), the one tests name.
    public const double Rate = 2.0, Life = 5.0;
    public const double BoxW = 3.21, BoxH = 8.45;
    public const double DropW = 0.04, DropH = 0.5;
    public const double Gravity = 9.81;
    /// <summary>The colour over life's alpha keys: 0 at birth, 1 from here...</summary>
    public const double FadeInEnd = 0.144;
    /// <summary>...to here, 0 at the end.</summary>
    public const double FadeOutStart = 0.88;
    /// <summary>Canvas pixels a unit (the camera's 10.8 units over the canvas's 1080).</summary>
    public const double PixelsPerUnit = 100.0;
    /// <summary>The preview shows the drops as after this long: a full five seconds of them out.</summary>
    public const double Steady = Life;
    /// <summary>The moment tests draw (TestMode): the same every time.</summary>
    public const double TestTime = Steady + 1.25;

    /// <summary>How far below the first towel system's box the second's sits, a share of the box's height: Anna's
    /// "Particle System (1)" is 0.51 under a body scaled 4 (2.04 units), against a box 8.45 tall. The plugin's
    /// WetEffects.TowelSecondShift.</summary>
    public const double SecondShift = 2.04 / 8.45;

    /// <summary>The shower's box (5_Levels/4_Bath/AnnaInShower/Particle System (1)'s shape) and its own scale there,
    /// which a Wet object's scale of 1 stands for.</summary>
    public const double ShowerW = 2.0, ShowerH = 1.0, ShowerOwnScale = 1.05321;

    /// <summary>Which of the game's wet effects.</summary>
    public enum Kind : byte { Towel, Shower }

    /// <summary>
    /// One of the game's particle systems, as read in the game: drops a second and how long each lives (born at even
    /// steps; prewarmed ones have been falling since before the start), its gravity's share of 9.81, a drop's size
    /// (random between two numbers, each axis its own), its start colour (straight sRGB, random between two, one draw
    /// for all four), the colour over life's alpha (rising to 1 by <see cref="FadeIn"/>, 0 there meaning 1 from birth;
    /// falling from <see cref="FadeOut"/>), its box's middle below the Wet object's (a share of the box's height),
    /// whether a drop is a solid quad lit with the level (the shower's Sprite-Lit-Default: no texture) or the particle
    /// texture drawn over everything unlit (the towel's), and a number its random draws are told apart by.
    /// </summary>
    public sealed record WetSystem(double Rate, double Life, bool Prewarm, double Gravity,
                                   double SizeXMin, double SizeXMax, double SizeYMin, double SizeYMax,
                                   float R0, float G0, float B0, float A0, float R1, float G1, float B1, float A1,
                                   double FadeIn, double FadeOut, double Shift, bool Solid, uint Salt);

    public static readonly WetSystem TowelFirst = new(Rate, Life, false, 1.0, DropW, DropW, DropH, DropH,
                                                      1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, FadeInEnd, FadeOutStart, 0.0, false, 0u);
    /// <summary>Anna_Towel/MBase1/Particle System (1): 6 a second, 0.08 x 0.8, the rest the first's.</summary>
    public static readonly WetSystem TowelSecond = new(6.0, Life, false, 1.0, 0.08, 0.08, 0.8, 0.8,
                                                       1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, FadeInEnd, FadeOutStart, -SecondShift, false, 0x6C8E9CF5u);
    /// <summary>AnnaInShower/Particle System (1): 200 a second for 2 seconds, prewarmed, gravity 0.7, 0.01..0.005 x
    /// 0.5..0.2, between #2B3C6F3C and #6D81A4AC, alpha 1 until 0.77 of its life.</summary>
    public static readonly WetSystem Shower = new(200.0, 2.0, true, 0.7, 0.01, 0.005, 0.5, 0.2,
                                                  0x2B / 255f, 0x3C / 255f, 0x6F / 255f, 0x3C / 255f,
                                                  0x6D / 255f, 0x81 / 255f, 0xA4 / 255f, 0xAC / 255f, 0.0, 0.77, 0.0, true, 0x1B873593u);

    private static readonly WetSystem[] TowelSystems = { TowelFirst, TowelSecond }, ShowerSystems = { Shower };

    public static IReadOnlyList<WetSystem> SystemsOf(Kind kind) => kind == Kind.Shower ? ShowerSystems : TowelSystems;

    /// <summary>
    /// A Wet object on the canvas: where it is, the canvas direction of one of its own units along its x and along
    /// its y (up) - unit length, its turn only - its own scale (for the shower, times its 1.053), a seed of its own,
    /// how see-through it is drawn, the box its drops start in before its scale (the particle shape's scale), which
    /// kind it is, and the placement's settings: the shower's drops from the top side only, gone at the box's floor;
    /// a tint (straight sRGB) the start colour is multiplied by; times the drops a second, the size, the fall.
    /// </summary>
    public readonly record struct Emitter(double X, double Y, double AxX, double AxY, double AyX, double AyY,
                                          double ScaleX, double ScaleY, uint Seed, double Opacity = 1.0,
                                          double AreaW = BoxW, double AreaH = BoxH, Kind Of = Kind.Towel,
                                          bool FromTop = false, bool Floor = false,
                                          float TintR = 1f, float TintG = 1f, float TintB = 1f, float TintA = 1f,
                                          double Amount = 1.0, double Size = 1.0, double Fall = 1.0, bool Unlit = false);

    /// <summary>A drop: its centre on the canvas, its size there, its alpha (the emitter's opacity in it), its colour
    /// (straight sRGB), whether it is a solid quad (the shower's: no texture) rather than the particle texture, and
    /// whether it is lit with the place - blended into it before its lights, where the towel's go over it after.</summary>
    public struct Drop
    {
        public double X, Y, W, H;
        public float Alpha;
        public float R, G, B;
        public bool Solid, Lit;

        public Drop() { X = Y = W = H = 0; Alpha = 0f; R = G = B = 1f; Solid = false; Lit = false; }
    }

    /// <summary>The towel's colour over life's alpha at a share of a drop's life.</summary>
    public static double AlphaOver(double life) => AlphaOver(life, FadeInEnd, FadeOutStart);

    /// <summary>A system's colour over life's alpha at a share of a drop's life.</summary>
    public static double AlphaOver(double life, double fadeIn, double fadeOut)
    {
        if (!(life >= 0) || life >= 1) return 0;
        if (fadeIn > 0 && life < fadeIn) return life / fadeIn;
        if (life <= fadeOut) return 1;
        return (1 - life) / (1 - fadeOut);
    }

    /// <summary>Where in its box drop <paramref name="k"/> starts: each a share of the box from its middle, -0.5..0.5.</summary>
    public static (double X, double Y) Spawn(uint seed, long k)
    {
        ulong h = Mix(((ulong)seed << 32) ^ (ulong)k ^ 0x9E3779B97F4A7C15UL);
        double a = (h >> 11) * (1.0 / (1UL << 53));
        h = Mix(h ^ 0xBF58476D1CE4E5B9UL);
        double b = (h >> 11) * (1.0 / (1UL << 53));
        return (a - 0.5, b - 0.5);
    }

    /// <summary>Drop <paramref name="k"/>'s draw number <paramref name="i"/> (its size's, its colour's), 0..1.</summary>
    public static double Draw(uint seed, long k, int i)
    {
        ulong h = Mix(((ulong)seed << 32) ^ (ulong)k ^ (0xD6E8FEB86659FD93UL * (ulong)(i + 1)));
        return (Mix(h ^ 0x94D049BB133111EBUL) >> 11) * (1.0 / (1UL << 53));
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Every drop of <paramref name="e"/> out <paramref name="t"/> seconds after it started, whose picture reaches
    /// into the canvas rectangle given, into <paramref name="into"/> - each of its kind's systems in turn.
    /// </summary>
    public static void At(double t, in Emitter e, List<Drop> into, double left, double top, double right, double bottom)
    {
        if (e.Opacity <= 0 || !(e.Amount > 0)) return;
        foreach (var s in SystemsOf(e.Of)) At(t, e, s, into, left, top, right, bottom);
    }

    private static void At(double t, in Emitter e, WetSystem s, List<Drop> into, double left, double top, double right, double bottom)
    {
        double rate = s.Rate * e.Amount, life = s.Life;
        long first = (long)Math.Floor((t - life) * rate) + 1, last = (long)Math.Floor(t * rate);
        if (!s.Prewarm) first = Math.Max(0, first);
        double fall = 0.5 * Gravity * s.Gravity * e.Fall * PixelsPerUnit;
        uint seed = e.Seed ^ s.Salt;
        for (long k = first; k <= last; k++)
        {
            double age = t - k / rate;
            if (age < 0 || age >= life) continue;
            var (bx, by) = Spawn(seed, k);
            // Its start in the Wet object's own units: in the box, or on its top side.
            double ownX = bx * e.AreaW, ownY = (e.FromTop ? 0.5 : by) * e.AreaH + s.Shift * e.AreaH;
            double lx = ownX * e.ScaleX * PixelsPerUnit, ly = ownY * e.ScaleY * PixelsPerUnit;
            double x = e.X + lx * e.AxX + ly * e.AyX;
            double y = e.Y + lx * e.AxY + ly * e.AyY + fall * age * age;
            double sw = Lerp(s.SizeXMin, s.SizeXMax, s.SizeXMin == s.SizeXMax ? 0 : Draw(seed, k, 0)) * e.Size;
            double sh = Lerp(s.SizeYMin, s.SizeYMax, s.SizeYMin == s.SizeYMax ? 0 : Draw(seed, k, 1)) * e.Size;
            double w = sw * PixelsPerUnit * Math.Abs(e.ScaleX), h = sh * PixelsPerUnit * Math.Abs(e.ScaleY);
            // Gone at the floor: its lower end out of the box (it falls straight down, so out through the floor or a
            // lower side of a turned box).
            if (e.Floor && !InBox(e, s, x, y + h / 2)) continue;
            if (x + w / 2 < left || x - w / 2 > right || y + h / 2 < top || y - h / 2 > bottom) continue;
            float c = s.R0 == s.R1 && s.G0 == s.G1 && s.B0 == s.B1 && s.A0 == s.A1 ? 0f : (float)Draw(seed, k, 2);
            float a = (float)(AlphaOver(age / life, s.FadeIn, s.FadeOut) * e.Opacity) * (s.A0 + (s.A1 - s.A0) * c) * e.TintA;
            if (!(a > 0f)) continue;
            into.Add(new Drop
            {
                X = x, Y = y, W = w, H = h, Alpha = a, Solid = s.Solid, Lit = s.Solid && !e.Unlit,
                R = (s.R0 + (s.R1 - s.R0) * c) * e.TintR, G = (s.G0 + (s.G1 - s.G0) * c) * e.TintG, B = (s.B0 + (s.B1 - s.B0) * c) * e.TintB,
            });
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>The box the drops start in, the Wet object's own units from its middle: the towel's two systems'
    /// together (the second's lower by <see cref="SecondShift"/> of the height), the shower's as it is.</summary>
    public static (double Left, double Right, double Bottom, double Top) Extent(in Emitter e)
    {
        double low = -e.AreaH / 2, high = e.AreaH / 2;
        foreach (var s in SystemsOf(e.Of))
        {
            low = Math.Min(low, -e.AreaH / 2 + s.Shift * e.AreaH);
            high = Math.Max(high, e.AreaH / 2 + s.Shift * e.AreaH);
        }
        return (-e.AreaW / 2, e.AreaW / 2, low, high);
    }

    /// <summary>Whether canvas point (<paramref name="px"/>, <paramref name="py"/>) is in system
    /// <paramref name="s"/>'s box.</summary>
    private static bool InBox(in Emitter e, WetSystem s, double px, double py)
    {
        var (ox, oy) = OwnUnits(e, px, py);
        double mid = s.Shift * e.AreaH;
        return Math.Abs(ox) <= e.AreaW / 2 + 1e-9 && oy >= mid - e.AreaH / 2 - 1e-9 && oy <= mid + e.AreaH / 2 + 1e-9;
    }

    /// <summary>A canvas point in the Wet object's own units (its scale out, its turn undone).</summary>
    public static (double X, double Y) OwnUnits(in Emitter e, double px, double py)
    {
        double rx = px - e.X, ry = py - e.Y;
        double det = e.AxX * e.AyY - e.AxY * e.AyX;
        if (Math.Abs(det) < 1e-12) return (double.NaN, double.NaN);
        double a = (rx * e.AyY - ry * e.AyX) / det, b = (e.AxX * ry - e.AxY * rx) / det;
        double sx = e.ScaleX * PixelsPerUnit, sy = e.ScaleY * PixelsPerUnit;
        return (Math.Abs(sx) < 1e-12 ? double.NaN : a / sx, Math.Abs(sy) < 1e-12 ? double.NaN : b / sy);
    }

    /// <summary>The box drops start in (<see cref="Extent"/>), on the canvas: its four corners - top left, top right,
    /// bottom right, bottom left of the Wet object's own frame - to draw where a Wet object is.</summary>
    public static (double X, double Y)[] Box(in Emitter e)
    {
        var (l, r, b, t) = Extent(e);
        var corners = new (double, double)[4];
        int i = 0;
        foreach (var (ox, oy) in new[] { (l, t), (r, t), (r, b), (l, b) })
        {
            double lx = ox * e.ScaleX * PixelsPerUnit, ly = oy * e.ScaleY * PixelsPerUnit;
            corners[i++] = (e.X + lx * e.AxX + ly * e.AyX, e.Y + lx * e.AxY + ly * e.AyY);
        }
        return corners;
    }

    /// <summary>A straight sRGB channel in linear light (what the game blends a drop's colour in).</summary>
    public static float Linear(float srgb) => srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);

    // ── The particle texture and its smaller copies ─────────────────────

    /// <summary>The texture's alpha (its colour is white throughout) and every smaller copy, each the mean of four
    /// texels of the one before, as the game's were made - one array, a level after another.</summary>
    public sealed class Mips
    {
        public readonly float[] Alpha;
        public readonly int[] At, Size;
        public int Levels => Size.Length;
        public int Full => Size[0];

        public Mips(float[] full, int size)
        {
            var levels = new List<float[]> { full };
            var sizes = new List<int> { size };
            while (sizes[^1] > 1)
            {
                int s = sizes[^1], n = s / 2;
                var up = levels[^1];
                var down = new float[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        down[y * n + x] = (up[2 * y * s + 2 * x] + up[2 * y * s + 2 * x + 1] + up[(2 * y + 1) * s + 2 * x] + up[(2 * y + 1) * s + 2 * x + 1]) * 0.25f;
                levels.Add(down);
                sizes.Add(n);
            }
            At = new int[sizes.Count];
            Size = sizes.ToArray();
            int total = 0;
            for (int i = 0; i < sizes.Count; i++) { At[i] = total; total += sizes[i] * sizes[i]; }
            Alpha = new float[total];
            for (int i = 0; i < levels.Count; i++) Array.Copy(levels[i], 0, Alpha, At[i], levels[i].Length);
        }

        /// <summary>The copy a picture <paramref name="wide"/> x <paramref name="tall"/> pixels on screen is read
        /// from: as a card picks it (the nearest level to log2 of the most texels a pixel covers).</summary>
        public int LevelFor(float wide, float tall)
        {
            float t = MathF.Max(Full / MathF.Max(wide, 1e-6f), Full / MathF.Max(tall, 1e-6f));
            if (!(t > 1f)) return 0;
            int level = (int)MathF.Floor(MathF.Log2(t) + 0.5f);
            return Math.Clamp(level, 0, Levels - 1);
        }

        /// <summary>Between the four nearest texels of a level (texel centres at the halves, clamped at the edges),
        /// as PlaceLit.hlsl reads it.</summary>
        public float Sample(int level, float u, float v)
        {
            int s = Size[level], o = At[level];
            float fx = u * s - 0.5f, fy = v * s - 0.5f;
            float x0 = MathF.Floor(fx), y0 = MathF.Floor(fy);
            float ax = fx - x0, ay = fy - y0;
            int xa = Clamp(x0, s), xb = Clamp(x0 + 1f, s), ya = Clamp(y0, s), yb = Clamp(y0 + 1f, s);
            float top = Alpha[o + ya * s + xa] * (1f - ax) + Alpha[o + ya * s + xb] * ax;
            float bottom = Alpha[o + yb * s + xa] * (1f - ax) + Alpha[o + yb * s + xb] * ax;
            return top * (1f - ay) + bottom * ay;
        }

        private static int Clamp(float i, int n) => i < 0f ? 0 : i > n - 1 ? n - 1 : (int)i;
    }

    private static Mips? _texture;
    private static bool _textureTried;
    private static readonly object Gate = new();

    /// <summary>The game's particle texture (Default-ParticleSystem, shipped in VanillaFx), or null without it.</summary>
    public static Mips? Texture
    {
        get
        {
            lock (Gate)
            {
                if (_textureTried) return _texture;
                _textureTried = true;
                try
                {
                    string file = Path.Combine(AppContext.BaseDirectory, "VanillaFx", "Default_ParticleSystem.png");
                    if (!File.Exists(file)) return null;
                    var decoder = BitmapDecoder.Create(new Uri(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var bgra = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
                    int w = bgra.PixelWidth, h = bgra.PixelHeight;
                    if (w != h || (w & (w - 1)) != 0) return null;
                    var px = new byte[w * h * 4];
                    bgra.CopyPixels(px, w * 4, 0);
                    var alpha = new float[w * h];
                    for (int i = 0; i < alpha.Length; i++) alpha[i] = px[i * 4 + 3] / 255f;
                    _texture = new Mips(alpha, w);
                }
                catch (Exception) { _texture = null; }
                return _texture;
            }
        }
    }

    private static BitmapSource?[]? _pictures;

    /// <summary>Each level as a picture WPF can draw (white, its alpha): what the preview draws a drop with while
    /// the game's look is off.</summary>
    public static BitmapSource? Picture(int level)
    {
        var m = Texture;
        if (m == null) return null;
        lock (Gate)
        {
            _pictures ??= new BitmapSource?[m.Levels];
            if (_pictures[level] is { } made) return made;
            int s = m.Size[level], o = m.At[level];
            var px = new byte[s * s * 4];
            for (int i = 0; i < s * s; i++)
            {
                byte a = (byte)Math.Clamp((int)Math.Round(m.Alpha[o + i] * 255f), 0, 255);
                px[i * 4] = px[i * 4 + 1] = px[i * 4 + 2] = px[i * 4 + 3] = a;   // premultiplied white
            }
            var bmp = BitmapSource.Create(s, s, 96, 96, PixelFormats.Pbgra32, null, px, s * 4);
            bmp.Freeze();
            return _pictures[level] = bmp;
        }
    }

    // ── Over the lit place ─────────────────────────────────────────────

    /// <summary>
    /// A pixel of the camera's target (linear light, the 11-11-10-bit format), at canvas point
    /// (<paramref name="cx"/>, <paramref name="cy"/>), with every drop over it blended in as the game blends them
    /// (white times the texture's alpha and the drop's, over what is there). <paramref name="perCanvas"/> is the
    /// target's pixels to a canvas pixel: it says which copy of the texture each drop is read from. PlaceLit.hlsl
    /// does the same on the card, in the same order and in single precision.
    /// </summary>
    public static void Blend(ref float r, ref float g, ref float b, float cx, float cy, float perCanvas, Drop[] drops, int count, Mips m,
                             bool? lit = null)
    {
        for (int i = 0; i < count; i++)
            if (lit == null || drops[i].Lit == lit) BlendOne(ref r, ref g, ref b, cx, cy, perCanvas, drops[i], m);
    }

    /// <summary>One drop over canvas point (<paramref name="cx"/>, <paramref name="cy"/>), as <see cref="Blend"/> does
    /// each in turn.</summary>
    public static void BlendOne(ref float r, ref float g, ref float b, float cx, float cy, float perCanvas, in Drop d, Mips m)
    {
        float x = (float)d.X, y = (float)d.Y, w = (float)d.W, h = (float)d.H;
        float left = x - w * 0.5f, top = y - h * 0.5f;
        float u = (cx - left) / w, v = (cy - top) / h;
        if (!(u >= 0f && u < 1f && v >= 0f && v < 1f)) return;
        float a = d.Alpha;
        if (!d.Solid) a *= m.Sample(m.LevelFor(w * perCanvas, h * perCanvas), u, v);
        if (!(a > 0f)) return;
        r = GamePost.Small(r + (Linear(d.R) - r) * a, 6);
        g = GamePost.Small(g + (Linear(d.G) - g) * a, 6);
        b = GamePost.Small(b + (Linear(d.B) - b) * a, 5);
    }

    /// <summary>A drop's alpha at canvas point (<paramref name="cx"/>, <paramref name="cy"/>), as <see cref="BlendOne"/>
    /// takes it: 0 off the drop.</summary>
    private static float AlphaAt(float cx, float cy, float perCanvas, in Drop d, Mips m)
    {
        float x = (float)d.X, y = (float)d.Y, w = (float)d.W, h = (float)d.H;
        float u = (cx - (x - w * 0.5f)) / w, v = (cy - (y - h * 0.5f)) / h;
        if (!(u >= 0f && u < 1f && v >= 0f && v < 1f)) return 0f;
        return d.Solid ? d.Alpha : d.Alpha * m.Sample(m.LevelFor(w * perCanvas, h * perCanvas), u, v);
    }

    /// <summary>
    /// The drops of <paramref name="o"/> into the camera's target <paramref name="t"/> (the whole screen, rows from the
    /// bottom), where each is and nowhere else: each pixel as <see cref="Over.Screen"/> gives it - one drop after
    /// another in their order is every drop over it in that order. The bust preview's frame (JigglePreview).
    /// </summary>
    internal static void BlendInto(GamePost.Target t, Over o) => BlendInto(t, o, null, null);

    /// <summary>
    /// As <see cref="BlendInto(GamePost.Target, Over)"/>, each drop's pixels on the bust's frame counted as drawn over
    /// in <paramref name="coverage"/> (BackdropRender's, rows from the top) - what the lamp's glow, added after, takes
    /// as drawn over (it is added over a drop as over the bust; over the room alone the room's own frame stands).
    /// </summary>
    internal static void BlendInto(GamePost.Target t, Over o, BackdropScene? scene, float[]? coverage)
    {
        float perCanvas = t.W / o.CanvasW;
        const int N = BackdropRender.FrameSize;
        for (int i = 0; i < o.Count; i++)
        {
            ref readonly var d = ref o.Drops[i];
            double x0 = (d.X - d.W * 0.5) * t.W / o.CanvasW, x1 = (d.X + d.W * 0.5) * t.W / o.CanvasW;
            double y0 = (o.CanvasH - (d.Y + d.H * 0.5)) * t.H / o.CanvasH, y1 = (o.CanvasH - (d.Y - d.H * 0.5)) * t.H / o.CanvasH;
            int left = Math.Max(0, (int)Math.Floor(x0) - 1), right = Math.Min(t.W - 1, (int)Math.Ceiling(x1) + 1);
            int bottom = Math.Max(0, (int)Math.Floor(y0) - 1), top = Math.Min(t.H - 1, (int)Math.Ceiling(y1) + 1);
            for (int y = bottom; y <= top; y++)
                for (int x = left; x <= right; x++)
                {
                    int p = (y * t.W + x) * 4;
                    float cx = (x + 0.5f) * o.CanvasW / t.W, cy = o.CanvasH - (y + 0.5f) * o.CanvasH / t.H;
                    if (coverage != null && scene != null)
                    {
                        float a = AlphaAt(cx, cy, perCanvas, d, o.Mips);
                        int fx = x - scene.Left, fy = N - 1 - (y - scene.FrameBottom);
                        if (a > 0f && fx >= 0 && fx < N && fy >= 0 && fy < N)
                        {
                            int c = fy * N + fx;
                            coverage[c] = 1f - (1f - coverage[c]) * (1f - a);
                        }
                    }
                    BlendOne(ref t.Px[p], ref t.Px[p + 1], ref t.Px[p + 2], cx, cy, perCanvas, d, o.Mips);
                }
        }
    }

    // ── On a bust ──────────────────────────────────────────────────────

    /// <summary>
    /// A pack bust's Wet object (BustFactory clones Anna_Towel's particle system under its body, MBase1), measured in
    /// the game (InGameSuiteTests.S20, after 1.10.0): at the body's own place - the middle of its art, the sprite's
    /// pivot - not turned, its own scale 1 inside a body scaled 4 (which, scaling Local, leaves the box and the drops
    /// alone: 3.21 by 8.45 units, as on an NPC). <paramref name="frameLeft"/> and <paramref name="frameBottom"/> are
    /// the bust's square on a screen <paramref name="screenH"/> pixels tall (rows from the bottom), <paramref name="frameSize"/>
    /// its side, <paramref name="screenPpu"/> the screen's pixels a unit (the game's 960x540: 50); the emitter is on
    /// that screen's canvas (<see cref="PixelsPerUnit"/> a unit, y down).
    /// </summary>
    public static Emitter OnBust(double frameLeft, double frameBottom, int frameSize, double screenH, double screenPpu, uint seed,
                                 Kind kind = Kind.Towel)
    {
        double toCanvas = PixelsPerUnit / screenPpu;
        // The shower: at the middle of the picture's top side, its own scale of 1.053, drawn unlit (the plugin gives a
        // bust's shower the towel's unlit material - the Bust layer's lights are not the room's, and the preview has
        // no picture of them): InGameSuiteTests.S22.
        if (kind == Kind.Shower)
            return new Emitter((frameLeft + frameSize / 2.0) * toCanvas, (screenH - (frameBottom + frameSize)) * toCanvas,
                               1, 0, 0, -1, ShowerOwnScale, ShowerOwnScale, seed, AreaW: ShowerW, AreaH: ShowerH, Of: Kind.Shower,
                               Unlit: true);
        return new Emitter((frameLeft + frameSize / 2.0) * toCanvas, (screenH - (frameBottom + frameSize / 2.0)) * toCanvas,
                           1, 0, 0, -1, 1, 1, seed);
    }

    /// <summary>A bust's drops at moment <paramref name="t"/> over a canvas <paramref name="canvasW"/> x
    /// <paramref name="canvasH"/>, or null with no texture or none out.</summary>
    public static Over? OnBustAt(double t, in Emitter e, double canvasW, double canvasH)
    {
        var mips = Texture;
        if (mips == null) return null;
        var list = new List<Drop>();
        At(t, e, list, 0, 0, canvasW, canvasH);
        return list.Count == 0 ? null : new Over(list.ToArray(), list.Count, mips, canvasW, canvasH);
    }

    /// <summary>
    /// The drops over a bust drawn alone (no backdrop: <paramref name="bgra"/>, premultiplied, <paramref name="size"/>
    /// square, rows from the top), each as the game blends it - white through its alpha, in linear light - with the
    /// picture's own alpha growing under it, there being nothing behind.
    /// </summary>
    internal static void BlendAlone(byte[] bgra, int size, Over o)
    {
        float perCanvas = size / o.CanvasW;
        for (int i = 0; i < o.Count; i++)
        {
            ref readonly var d = ref o.Drops[i];
            double x0 = (d.X - d.W * 0.5) * size / o.CanvasW, x1 = (d.X + d.W * 0.5) * size / o.CanvasW;
            double y0 = (d.Y - d.H * 0.5) * size / o.CanvasH, y1 = (d.Y + d.H * 0.5) * size / o.CanvasH;
            int left = Math.Max(0, (int)Math.Floor(x0) - 1), right = Math.Min(size - 1, (int)Math.Ceiling(x1) + 1);
            int top = Math.Max(0, (int)Math.Floor(y0) - 1), bottom = Math.Min(size - 1, (int)Math.Ceiling(y1) + 1);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    float cx = (x + 0.5f) * o.CanvasW / size, cy = (y + 0.5f) * o.CanvasH / size;
                    float a = AlphaAt(cx, cy, perCanvas, d, o.Mips);
                    if (!(a > 0f)) continue;
                    int p = (y * size + x) * 4;
                    float under = bgra[p + 3] / 255f;
                    float outA = a + under * (1f - a);
                    float Lin(int c) => under > 0f ? SrgbToLinear[Math.Clamp((int)MathF.Round(bgra[p + c] / under), 0, 255)] : 0f;
                    float Mix(float drop, float lin) => (drop * a + lin * under * (1f - a)) / outA;
                    float nb = Mix(Linear(d.B), Lin(0)), ng = Mix(Linear(d.G), Lin(1)), nr = Mix(Linear(d.R), Lin(2));
                    bgra[p] = (byte)Math.Clamp((int)MathF.Round(ToSrgb(nb) * 255f * outA), 0, 255);
                    bgra[p + 1] = (byte)Math.Clamp((int)MathF.Round(ToSrgb(ng) * 255f * outA), 0, 255);
                    bgra[p + 2] = (byte)Math.Clamp((int)MathF.Round(ToSrgb(nr) * 255f * outA), 0, 255);
                    bgra[p + 3] = (byte)Math.Clamp((int)MathF.Round(outA * 255f), 0, 255);
                }
        }
    }

    private static readonly float[] SrgbToLinear = MakeSrgbToLinear();

    private static float[] MakeSrgbToLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float v = i / 255f;
            t[i] = v <= 0.04045f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        }
        return t;
    }

    private static float ToSrgb(float l)
        => l <= 0.0031308f ? 12.92f * l : 1.055f * MathF.Pow(Math.Clamp(l, 0f, 1f), 1f / 2.4f) - 0.055f;

    /// <summary>
    /// The drops of a frame, over the lit place: what the look's passes blend in after the lights and before the
    /// post (GamePost.LookOverView, GpuPost.LookOverView; on the card PlaceLit.hlsl, from the frame's own list).
    /// </summary>
    public sealed class Over
    {
        public readonly Drop[] Drops;
        public readonly int Count;
        public readonly Mips Mips;
        public readonly float CanvasW, CanvasH;

        public Over(Drop[] drops, int count, Mips mips, double canvasW, double canvasH)
        {
            Drops = drops; Count = count; Mips = mips; CanvasW = (float)canvasW; CanvasH = (float)canvasH;
        }

        /// <summary>Whether any drop is lit with the place (a shower's): those are blended in before the lights.</summary>
        public bool AnyLit
        {
            get
            {
                for (int i = 0; i < Count; i++) if (Drops[i].Lit) return true;
                return false;
            }
        }

        /// <summary>A pixel of the whole screen at <paramref name="w"/> x <paramref name="h"/>, rows from the bottom:
        /// the lit drops or the others (<paramref name="lit"/>), or all.</summary>
        public void Screen(ref float r, ref float g, ref float b, int x, int y, int w, int h, bool? lit = false)
        {
            if (Count == 0) return;
            float cx = (x + 0.5f) * CanvasW / w, cy = CanvasH - (y + 0.5f) * CanvasH / h;
            Blend(ref r, ref g, ref b, cx, cy, w / CanvasW, Drops, Count, Mips, lit);
        }

        /// <summary>A pixel of a view, at uv (<paramref name="u"/>, <paramref name="v"/>) on the screen (v up), a
        /// view pixel being <paramref name="canvasPerPixel"/> of the canvas's pixels.</summary>
        public void View(ref float r, ref float g, ref float b, float u, float v, float canvasPerPixel, bool? lit = false)
        {
            if (Count == 0) return;
            Blend(ref r, ref g, ref b, u * CanvasW, (1f - v) * CanvasH, 1f / canvasPerPixel, Drops, Count, Mips, lit);
        }
    }
}
