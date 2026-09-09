using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SMSModForge.Rendering;

/// <summary>
/// A TextMeshPro font asset, as the editor needs it: an atlas, the metrics to
/// place glyphs from it, and the kerning to space them.
/// <para/>
/// The game's fonts are not font files. Most of the source TTFs are not in the
/// project and never were — a TMP asset is self-contained once baked, and only
/// the baking needed the typeface. That turns out to be the better source
/// anyway: TextMeshPro does not read a TTF at runtime either, so drawing from
/// the atlas reproduces what the game draws, where drawing the same typeface
/// through WPF's text stack would reproduce something near it.
/// </summary>
public sealed class TmpFont
{
    // ── The shipped shape ────────────────────────────────────────────

    public sealed class FaceInfo
    {
        [JsonProperty("familyName")] public string FamilyName { get; set; } = "";
        [JsonProperty("styleName")] public string StyleName { get; set; } = "";

        /// <summary>The size the atlas was baked at. Every metric below is in
        /// its units, so drawing at any other size is a matter of scaling by
        /// <c>fontSize / pointSize</c>.</summary>
        [JsonProperty("pointSize")] public float PointSize { get; set; } = 1f;
        [JsonProperty("scale")] public float Scale { get; set; } = 1f;
        [JsonProperty("lineHeight")] public float LineHeight { get; set; }
        [JsonProperty("ascentLine")] public float AscentLine { get; set; }
        [JsonProperty("baseline")] public float Baseline { get; set; }
        [JsonProperty("descentLine")] public float DescentLine { get; set; }
        [JsonProperty("capLine")] public float CapLine { get; set; }
        [JsonProperty("meanLine")] public float MeanLine { get; set; }
        [JsonProperty("underlineOffset")] public float UnderlineOffset { get; set; }
        [JsonProperty("underlineThickness")] public float UnderlineThickness { get; set; }
        [JsonProperty("tabWidth")] public float TabWidth { get; set; }
    }

    public sealed class GlyphMetrics
    {
        [JsonProperty("width")] public float Width { get; set; }
        [JsonProperty("height")] public float Height { get; set; }

        /// <summary>Horizontal offset from the pen to the glyph's left edge.</summary>
        [JsonProperty("bearingX")] public float BearingX { get; set; }

        /// <summary>Height of the glyph's top ABOVE the baseline. Positive is
        /// up, which is the opposite of the direction screens count in — the
        /// one sign that has to be got right or every line of text sits a
        /// glyph-height away from where it belongs.</summary>
        [JsonProperty("bearingY")] public float BearingY { get; set; }

        /// <summary>How far the pen moves after drawing this glyph. Not the
        /// same as the width: letters overhang and tuck deliberately.</summary>
        [JsonProperty("advance")] public float Advance { get; set; }
    }

    public sealed class Glyph
    {
        [JsonProperty("index")] public int Index { get; set; }

        /// <summary>The glyph's box in the atlas, counted from the TOP, ready to
        /// crop from the PNG. The sibling "rect" counts from the bottom because
        /// that is how Unity addresses textures, and reading one with the
        /// other's convention puts every glyph in the wrong row while still
        /// looking like text.</summary>
        [JsonProperty("rectTopLeft")] public float[] RectTopLeft { get; set; } = new float[4];
        [JsonProperty("metrics")] public GlyphMetrics Metrics { get; set; } = new();
        [JsonProperty("scale")] public float Scale { get; set; } = 1f;
        [JsonProperty("page")] public int Page { get; set; }
    }

    public sealed class Character
    {
        [JsonProperty("unicode")] public int Unicode { get; set; }
        [JsonProperty("glyphIndex")] public int GlyphIndex { get; set; }
    }

    public sealed class KerningPair
    {
        [JsonProperty("first")] public int First { get; set; }
        [JsonProperty("second")] public int Second { get; set; }
        [JsonProperty("firstAdvance")] public float FirstAdvance { get; set; }
        [JsonProperty("secondAdvance")] public float SecondAdvance { get; set; }
    }

    public sealed class AtlasInfo
    {
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("textureWidth")] public int TextureWidth { get; set; }
        [JsonProperty("textureHeight")] public int TextureHeight { get; set; }
        [JsonProperty("padding")] public int Padding { get; set; }
        [JsonProperty("renderMode")] public string RenderMode { get; set; } = "";
        [JsonProperty("pages")] public List<string> Pages { get; set; } = new();
    }

    // ── The asset ────────────────────────────────────────────────────

    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("sourceFontFile")] public string SourceFontFile { get; set; } = "";
    [JsonProperty("atlasPopulationMode")] public string AtlasPopulationMode { get; set; } = "";
    [JsonProperty("fallbacks")] public List<string> Fallbacks { get; set; } = new();
    [JsonProperty("atlas")] public AtlasInfo Atlas { get; set; } = new();
    [JsonProperty("face")] public FaceInfo Face { get; set; } = new();
    [JsonProperty("glyphs")] public List<Glyph> Glyphs { get; set; } = new();
    [JsonProperty("characters")] public List<Character> Characters { get; set; } = new();
    [JsonProperty("kerning")] public List<KerningPair> Kerning { get; set; } = new();
    [JsonProperty("material")] public Dictionary<string, object> Material { get; set; } = new();

    // ── Lookups, built once ──────────────────────────────────────────

    private Dictionary<int, Glyph>? _byChar;
    private Dictionary<long, float>? _kerning;

    private void Index()
    {
        if (_byChar != null) return;

        var byGlyphIndex = new Dictionary<int, Glyph>();
        foreach (var g in Glyphs) byGlyphIndex[g.Index] = g;

        var byChar = new Dictionary<int, Glyph>();
        foreach (var c in Characters)
            if (byGlyphIndex.TryGetValue(c.GlyphIndex, out var g))
                byChar[c.Unicode] = g;
        _byChar = byChar;

        var kerning = new Dictionary<long, float>();
        foreach (var k in Kerning)
        {
            // Both halves of a pair can carry an adjustment; what matters to
            // the pen is their sum.
            float total = k.FirstAdvance + k.SecondAdvance;
            if (total != 0) kerning[Pair(k.First, k.Second)] = total;
        }
        _kerning = kerning;
    }

    private static long Pair(int first, int second) => ((long)first << 32) | (uint)second;

    /// <summary>The glyph for a character, or null when this atlas never baked
    /// one. Not an error: an atlas holds only what was baked into it, and the
    /// game resolves the rest through <see cref="Fallbacks"/>.</summary>
    public Glyph? GlyphFor(int unicode)
    {
        Index();
        return _byChar!.TryGetValue(unicode, out var g) ? g : null;
    }

    public bool Has(int unicode) => GlyphFor(unicode) != null;

    /// <summary>Extra advance between two glyphs, in font units. Without it,
    /// every pair the designer tightened draws slightly too wide, and across a
    /// line that accumulates into a visible difference from the game.</summary>
    public float KerningBetween(Glyph? first, Glyph? second)
    {
        if (first == null || second == null) return 0f;
        Index();
        return _kerning!.TryGetValue(Pair(first.Index, second.Index), out float v) ? v : 0f;
    }

    // ── What kind of atlas this is ───────────────────────────────────

    /// <summary>
    /// How far, in atlas texels, the stored field ramps across the glyph edge —
    /// or null when this atlas is a plain bitmap rather than a distance field.
    /// <para/>
    /// Measured, not assumed. Across every font in the game the alpha spans its
    /// full range over exactly <c>2 × GradientScale</c> texels: padding 5 with
    /// gradient 6 measures 11.9, padding 9 with gradient 10 measures 19.6,
    /// padding 10 with gradient 11 measures 22.2. Curse Casual is the case that
    /// proves it is the gradient and not the padding — padding 9, gradient 16,
    /// measured 31.8.
    /// </summary>
    public double? SdfSpread
    {
        get
        {
            double? gradient = MaterialNumber("GradientScale");
            return gradient is > 0 ? gradient * 2 : null;
        }
    }

    /// <summary>
    /// Whether this atlas holds a distance field or a pre-rendered bitmap.
    /// <para/>
    /// Seven of the game's fonts are bitmaps — every <c>Alata-Regular-Outline
    /// &lt;N&gt;</c>, whose name says SDF and whose shader says Bitmap Custom
    /// Atlas. That is why there are seven of them at 28, 32, 40, 50, 72, 120 and
    /// 210: a bitmap atlas does not scale, so each size is a separate bake.
    /// Putting an SDF threshold through one of those would wreck it.
    /// <para/>
    /// The test is the presence of GradientScale rather than the shader's name.
    /// A material either has the property that makes a distance field
    /// interpretable or it does not, and that is a fact about the asset instead
    /// of a string that a future TMP could rename.
    /// </summary>
    public bool IsDistanceField => SdfSpread.HasValue;

    public double? MaterialNumber(string key)
    {
        if (Material == null || !Material.TryGetValue(key, out object? raw) || raw == null)
            return null;
        try { return Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    /// <summary>
    /// Turn a stored alpha into ink coverage at a given drawing scale.
    /// <para/>
    /// A distance field says how far a texel is from the glyph's edge, with 0.5
    /// sitting exactly on it. Converting that to coverage needs to know how big
    /// a destination pixel is in texels, because the edge should fade across
    /// about one pixel however far the text is scaled — which is the whole
    /// reason a field is stored instead of a bitmap.
    /// <para/>
    /// A bitmap atlas is already coverage, and is returned untouched.
    /// </summary>
    /// <param name="alpha">Stored value, 0..1.</param>
    /// <param name="pixelsPerTexel">Destination pixels per atlas texel.</param>
    public double Coverage(double alpha, double pixelsPerTexel)
        => Coverage(alpha, pixelsPerTexel, 0, 0);

    /// <summary>
    /// As <see cref="Coverage(double, double)"/>, with the edge moved and the
    /// ramp blunted - which is all any of the material's face, outline and
    /// underlay settings actually do.
    /// </summary>
    /// <param name="shift">Moves the edge outwards, in stored-alpha units.</param>
    /// <param name="softness">Flattens the ramp. 0 leaves it as sharp as the
    /// drawing scale allows.</param>
    public double Coverage(double alpha, double pixelsPerTexel,
                           double shift, double softness)
    {
        double? spread = SdfSpread;
        if (spread is not > 0) return alpha < 0 ? 0 : alpha > 1 ? 1 : alpha;

        // How many destination pixels the field crosses per unit of alpha.
        double sharpness = spread.Value * pixelsPerTexel;
        if (softness > 0) sharpness /= 1 + softness * sharpness;

        double coverage = 0.5 + (alpha - 0.5 + shift) * sharpness;
        return coverage < 0 ? 0 : coverage > 1 ? 1 : coverage;
    }

    // -- The material: how the glyph is drawn, not which glyph ---------
    //
    // A TextMeshPro font asset carries a material as well as an atlas, and the
    // material is half of what the text looks like. Ignoring it draws the bare
    // glyph shape: no thickening, no outline, no shadow - which is thinner and
    // flatter than the same font in the game, and reads as the wrong typeface
    // rather than as a missing effect.
    //
    // The shader turns the stored distance into coverage with
    //
    //     coverage = 0.5 + (alpha - 0.5 + shift) * spread * pixelsPerTexel
    //
    // which is Coverage() below. Every one of these settings is a SHIFT of that
    // edge, in the same units as the stored alpha, so they all go through the
    // one function rather than each growing its own maths.

    /// <summary>How much thicker than its outline the face is drawn. TMP calls
    /// it dilate; it moves the edge outwards, so a positive value fattens every
    /// glyph.</summary>
    public double FaceShift
        => (Number("WeightNormal") / 4.0 + Number("FaceDilate")) * Number("ScaleRatioA", 1) * 0.5;

    /// <summary>Half the outline's width. It is centred ON the edge - half
    /// outside the glyph and half eaten out of the face - which is why an
    /// outline makes letters look no bigger, only heavier.</summary>
    public double OutlineShift
        => Number("OutlineWidth") * Number("ScaleRatioA", 1) * 0.5;

    public UiColor OutlineColor => Color("OutlineColor");

    public bool HasOutline => OutlineShift > 0 && OutlineColor.A > 0;

    /// <summary>Softness blunts the edge by flattening the ramp, so it divides
    /// the sharpness rather than moving anything.</summary>
    public double OutlineSoftness => Number("OutlineSoftness") * Number("ScaleRatioA", 1);

    /// <summary>The drop shadow. TMP calls it an underlay: the same glyph drawn
    /// again, offset, fattened and blurred, behind the text.</summary>
    public UiColor UnderlayColor => Color("UnderlayColor");

    public double UnderlayShift => Number("UnderlayDilate") * Number("ScaleRatioC", 1) * 0.5;

    public double UnderlaySoftness => Number("UnderlaySoftness") * Number("ScaleRatioC", 1);

    /// <summary>How far the shadow moves, in atlas texels. Positive X is right
    /// and positive Y is up, as in the material inspector.</summary>
    public (double X, double Y) UnderlayOffsetTexels
    {
        get
        {
            double k = Number("ScaleRatioC", 1) * Number("GradientScale", 1);
            return (Number("UnderlayOffsetX") * k, Number("UnderlayOffsetY") * k);
        }
    }

    public bool HasUnderlay
    {
        get
        {
            if (UnderlayColor.A == 0) return false;
            var (x, y) = UnderlayOffsetTexels;
            return x != 0 || y != 0 || UnderlayShift != 0;
        }
    }

    private double Number(string key, double fallback = 0) => MaterialNumber(key) ?? fallback;

    /// <summary>
    /// A colour the material names, or nothing at all.
    /// <para/>
    /// Absent has to mean transparent here, not <see cref="UiColor.Parse"/>'s
    /// opaque white. White is the right answer for a missing TINT, which is
    /// what Parse is for - it leaves a sprite its own colours. For an outline
    /// or a shadow there is no underlying colour to leave alone, so the same
    /// answer would paint a white halo round every letter of a font that never
    /// asked for one.
    /// </summary>
    private UiColor Color(string key)
        => Material != null && Material.TryGetValue(key, out object? raw) && raw is string hex
           ? UiColor.Parse(hex)
           : default;

    /// <summary>Destination pixels per font unit when drawing at this size.
    /// Every metric in the asset is in units of <see cref="FaceInfo.PointSize"/>,
    /// so this one number converts all of them.</summary>
    public double ScaleFor(double fontSize)
        => Face.PointSize <= 0 ? 1.0 : fontSize / Face.PointSize * Face.Scale;

    // ── Loading ──────────────────────────────────────────────────────

    public static TmpFont? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonConvert.DeserializeObject<TmpFont>(File.ReadAllText(path));
        }
        catch
        {
            // A font that will not parse leaves text undrawn, which is visible
            // and recoverable. Taking the editor down with it is not.
            return null;
        }
    }
}
