using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SMSModForge.Rendering;

/// <summary>
/// Draws the characters a dynamic font asset never baked, from the font file it
/// names - which is what TextMeshPro does in the game.
/// <para/>
/// <b>Why a game font can be missing a "9".</b> A TMP asset is either static,
/// every glyph baked into its atlas when it was made, or dynamic: its atlas
/// fills in as text needs characters, from the source font, while the game
/// runs. A dynamic asset ships holding whatever had been drawn by the time the
/// game was built. The dialogue font is one of those - its export names
/// "Curse Casual" as its source font, as the game's other dynamic assets do and
/// none of its static ones, and it holds 87 characters with no 8, 9, + or @ -
/// so the game draws an "8" and the export never saw one.
/// <para/>
/// <b>How it is drawn.</b> The same thing TMP stores: a distance field. Each
/// texel holds how far its centre is from the glyph's outline, 0.5 exactly on
/// it, rising by one over <see cref="TmpFont.SdfSpread"/> texels into the ink.
/// The distance is measured to the outline itself, flattened to straight
/// pieces, rather than read off a rendered bitmap, so it is exact. Metrics come
/// from the same file at the atlas's own point size, one texel to a pixel, as
/// TMP bakes them. The texels go into rows reserved below the atlas, so the
/// atlas a preview already holds is the one they appear in.
/// </summary>
public sealed class TmpGlyphBaker
{
    private readonly TmpFont _font;
    private readonly GlyphTypeface _face;
    private readonly byte[] _alpha;
    private readonly int _width;
    private readonly int _height;
    private readonly double _spread;
    private readonly int _padding;

    // Where the next glyph goes in the reserved rows: left to right along a
    // shelf as tall as the tallest glyph on it, then the next shelf down.
    private int _penX, _penY, _shelf;

    // Glyph indices for the baked ones, past any the asset uses.
    private int _nextIndex = 1_000_000;

    /// <summary>
    /// How far inside the true outline TMP's own bake puts the edge, in texels.
    /// Measured, not derived: the digits 0 to 7 drawn from the font file and
    /// from the game's atlas carry the same ink at 0.2, 4% too much at 0, 4%
    /// too little at 0.45 - TMP finds its edge on an antialiased raster rather
    /// than on the outline, which lands it a fraction inside.
    /// </summary>
    private const double EdgeInset = 0.2;

    private TmpGlyphBaker(TmpFont font, GlyphTypeface face, byte[] alpha, int width, int top, int height)
    {
        _font = font;
        _face = face;
        _alpha = alpha;
        _width = width;
        _height = height;
        _spread = font.SdfSpread ?? 1;
        _padding = Math.Max(0, font.Atlas.Padding);
        _penY = top;
    }

    /// <summary>
    /// Give <paramref name="font"/> the means to draw what it lacks, or leave
    /// it as it is: a static asset, a bitmap atlas or a source font that is not
    /// there all mean the game itself draws nothing more from it either, or
    /// that there is nothing to draw with.
    /// </summary>
    /// <param name="alpha">The atlas, one byte per texel, already grown by the
    /// rows the baked glyphs go into.</param>
    /// <param name="top">The first of those rows: the atlas's own height.</param>
    /// <param name="height">The grown height.</param>
    public static bool Attach(TmpFont font, string? fontFile, byte[] alpha, int width, int top, int height)
    {
        if (font == null || string.IsNullOrEmpty(fontFile) || !File.Exists(fontFile)) return false;
        if (!font.IsDistanceField || string.IsNullOrEmpty(font.SourceFontFile)) return false;
        if (font.Face.PointSize <= 0 || width <= 0 || height <= top || alpha.Length < width * height) return false;

        GlyphTypeface face;
        try { face = new GlyphTypeface(new Uri(Path.GetFullPath(fontFile))); }
        catch { return false; }

        var baker = new TmpGlyphBaker(font, face, alpha, width, top, height);
        font.Bake = baker.Bake;
        return true;
    }

    /// <summary>The font file a dynamic asset names, in the first of
    /// <paramref name="folders"/> that has it, or null when none does.</summary>
    public static string? SourceFileIn(IEnumerable<string> folders, TmpFont font)
    {
        if (string.IsNullOrEmpty(font.SourceFontFile)) return null;
        foreach (string folder in folders)
            foreach (string ext in new[] { ".ttf", ".otf" })
            {
                string path = Path.Combine(folder, font.SourceFontFile + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    /// <summary>One character's glyph, or null when the font file has none
    /// either, or the reserved rows are full.</summary>
    internal TmpFont.Glyph? Bake(int unicode)
    {
        if (!_face.CharacterToGlyphMap.TryGetValue(unicode, out ushort index)) return null;

        double em = _font.Face.PointSize;
        double advance = _face.AdvanceWidths[index] * em;

        // The outline at the atlas's size, its origin on the baseline and y
        // counting down, as WPF draws it.
        var outline = _face.GetGlyphOutline(index, em, em);
        var bounds = outline.Bounds;
        var glyph = new TmpFont.Glyph
        {
            Index = _nextIndex++,
            Scale = 1f,
            Metrics = new TmpFont.GlyphMetrics { Advance = (float)advance },
        };

        // A space: nothing to draw, only somewhere for the pen to move.
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return glyph;

        int cols = (int)Math.Ceiling(bounds.Width) + _padding * 2;
        int rows = (int)Math.Ceiling(bounds.Height) + _padding * 2;
        if (!Place(cols, rows, out int ox, out int oy)) return null;

        var segments = Segments(outline, out bool evenOdd);
        double left = bounds.Left - _padding, top = bounds.Top - _padding;
        for (int j = 0; j < rows; j++)
        {
            double y = top + j + 0.5;
            int row = (oy + j) * _width + ox;
            for (int i = 0; i < cols; i++)
            {
                double x = left + i + 0.5;
                double distance = Math.Sqrt(NearestSquared(segments, x, y));
                if (!Inside(segments, x, y, evenOdd)) distance = -distance;
                double value = 0.5 + (distance - EdgeInset) / _spread;
                _alpha[row + i] = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
            }
        }

        glyph.Metrics.Width = (float)bounds.Width;
        glyph.Metrics.Height = (float)bounds.Height;
        glyph.Metrics.BearingX = (float)bounds.Left;
        glyph.Metrics.BearingY = (float)-bounds.Top;
        glyph.RectTopLeft = new[] { ox + _padding, oy + _padding, (float)bounds.Width, (float)bounds.Height };
        return glyph;
    }

    private bool Place(int cols, int rows, out int x, out int y)
    {
        if (cols > _width) { x = y = 0; return false; }
        if (_penX + cols > _width)
        {
            _penX = 0;
            _penY += _shelf;
            _shelf = 0;
        }
        if (_penY + rows > _height) { x = y = 0; return false; }
        x = _penX;
        y = _penY;
        _penX += cols + 1;
        _shelf = Math.Max(_shelf, rows + 1);
        return true;
    }

    private readonly record struct Segment(double X0, double Y0, double X1, double Y1);

    private static List<Segment> Segments(Geometry outline, out bool evenOdd)
    {
        var flat = outline.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
        evenOdd = flat.FillRule == FillRule.EvenOdd;
        var list = new List<Segment>();
        foreach (var figure in flat.Figures)
        {
            var start = figure.StartPoint;
            var at = start;
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        list.Add(new Segment(at.X, at.Y, line.Point.X, line.Point.Y));
                        at = line.Point;
                        break;
                    case PolyLineSegment poly:
                        foreach (var p in poly.Points)
                        {
                            list.Add(new Segment(at.X, at.Y, p.X, p.Y));
                            at = p;
                        }
                        break;
                }
            }
            // A glyph's contours are closed whether or not the figure says so.
            if (at != start) list.Add(new Segment(at.X, at.Y, start.X, start.Y));
        }
        return list;
    }

    private static double NearestSquared(List<Segment> segments, double x, double y)
    {
        double best = double.MaxValue;
        foreach (var s in segments)
        {
            double dx = s.X1 - s.X0, dy = s.Y1 - s.Y0;
            double length = dx * dx + dy * dy;
            double t = length <= 0 ? 0 : Math.Clamp(((x - s.X0) * dx + (y - s.Y0) * dy) / length, 0, 1);
            double px = s.X0 + t * dx - x, py = s.Y0 + t * dy - y;
            double d = px * px + py * py;
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>Whether a point is in the ink, by the outline's own fill rule:
    /// a ray to the right, counting crossings by direction.</summary>
    private static bool Inside(List<Segment> segments, double x, double y, bool evenOdd)
    {
        int winding = 0, crossings = 0;
        foreach (var s in segments)
        {
            bool up = s.Y0 <= y && s.Y1 > y;
            bool down = s.Y1 <= y && s.Y0 > y;
            if (!up && !down) continue;
            double cx = s.X0 + (y - s.Y0) / (s.Y1 - s.Y0) * (s.X1 - s.X0);
            if (cx <= x) continue;
            crossings++;
            winding += up ? 1 : -1;
        }
        return evenOdd ? (crossings & 1) == 1 : winding != 0;
    }
}
