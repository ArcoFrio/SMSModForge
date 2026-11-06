using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Rendering;

/// <summary>One glyph, placed. Coordinates are in drawing space — origin at the
/// top-left of the text's rectangle, y downwards.</summary>
public readonly record struct PlacedGlyph(
    int Unicode,
    TmpFont.Glyph Glyph,
    double X,
    double Y,
    double Width,
    double Height,
    int Line,
    int Run = 0,
    /// <summary>Where this glyph's character sits in the string that was laid
    /// out — the runs concatenated, which is what the reader sees. Carried
    /// rather than counted afterwards: wrapping drops the space it broke at,
    /// a trailing space is trimmed, and a character with no glyph in the atlas
    /// is skipped, so glyph number N is not character number N.</summary>
    int Index = 0)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>A laid-out string.</summary>
public sealed class TextLayout
{
    public List<PlacedGlyph> Glyphs { get; } = new();
    public List<double> LineWidths { get; } = new();
    public double Width => LineWidths.Count == 0 ? 0 : LineWidths.Max();
    public double Height { get; internal set; }
    public int Lines => LineWidths.Count;

    /// <summary>Characters with no glyph in this font or its fallbacks. Not an
    /// error — an atlas holds only what was baked into it, and this is how the
    /// preview can say so instead of silently dropping them.</summary>
    public List<int> Missing { get; } = new();
}

/// <summary>Where the text sits in its rectangle. TextMeshPro's own names.</summary>
public enum TextAlign { Left, Center, Right }

/// <summary>
/// The font style on one of the game's text objects, as Unity writes it:
/// "Bold, UpperCase". Nearly half of the game's labels have one, and leaving it
/// out showed "Quit" where the game says "QUIT". An object keeps its style when a
/// pack changes its words.
/// </summary>
public readonly record struct UiTextStyle(bool Bold, bool Italic, bool Underline, bool Strikethrough,
                                          bool UpperCase, bool LowerCase, bool SmallCaps, bool Legacy = false)
{
    /// <summary>Said of old-style text (UI.Text), whose bold is another thing.</summary>
    public const string LegacyMark = "Legacy, ";

    public static UiTextStyle Parse(string? unity)
    {
        bool Has(string flag) => unity != null && unity.Contains(flag, StringComparison.OrdinalIgnoreCase);
        return new UiTextStyle(Has("Bold"), Has("Italic"), Has("Underline"), Has("Strikethrough"),
                               Has("UpperCase"), Has("LowerCase"), Has("SmallCaps"), Has("Legacy"));
    }

    /// <summary>TextMeshPro's small capitals: small letters drawn as capitals
    /// at 80% of the size.</summary>
    private const double SmallCapsScale = 0.8;

    /// <summary>What it says once its case is applied.</summary>
    public string Cased(string text)
        => UpperCase ? text.ToUpperInvariant() : LowerCase ? text.ToLowerInvariant() : text;

    /// <summary>Small capitals as runs of two sizes.</summary>
    public IReadOnlyList<StyledRun> SmallCapRuns(string text)
    {
        var runs = new List<StyledRun>();
        var piece = new System.Text.StringBuilder();
        bool small = false;
        foreach (char c in text)
        {
            bool isSmall = char.IsLower(c);
            if (isSmall != small && piece.Length > 0)
            {
                runs.Add(new StyledRun(piece.ToString(), small ? SmallCapsScale : 1));
                piece.Clear();
            }
            small = isSmall;
            piece.Append(isSmall ? char.ToUpperInvariant(c) : c);
        }
        if (piece.Length > 0) runs.Add(new StyledRun(piece.ToString(), small ? SmallCapsScale : 1));
        return runs;
    }
}

/// <summary>
/// A laid-out string put into its rectangle, ready to draw: the one place the
/// renderers work out where text goes, so the two cannot disagree.
/// </summary>
public static class UiTextBlock
{
    /// <summary>A laid-out string at another scale, each way: what a scaled parent
    /// does to it. The lines have already been broken.</summary>
    public static TextLayout Scaled(TextLayout layout, double kx, double ky)
    {
        if (Math.Abs(kx - 1) < 0.0001 && Math.Abs(ky - 1) < 0.0001) return layout;
        var made = new TextLayout { Height = layout.Height * ky };
        foreach (var g in layout.Glyphs)
            made.Glyphs.Add(g with { X = g.X * kx, Y = g.Y * ky, Width = g.Width * kx, Height = g.Height * ky });
        foreach (double width in layout.LineWidths) made.LineWidths.Add(width * kx);
        made.Missing.AddRange(layout.Missing);
        return made;
    }

    /// <summary>Lay out a string in the style its object has.</summary>
    public static TextLayout Measure(TmpFont font, string text, UiTextStyle style, double size, double wrapWidth,
                                     TextAlign align, double lineSpacing, double characterSpacing,
                                     IReadOnlyList<TmpFont>? fallbacks)
    {
        // Bold letters stand further apart as well as being heavier, by what the
        // font says. Old-style text is bold by another route, with no such gap.
        if (style.Bold && !style.Legacy) characterSpacing += font.BoldSpacing;
        return style.SmallCaps
            ? TmpTextLayout.MeasureRuns(font, style.SmallCapRuns(text), size, wrapWidth, align, lineSpacing, characterSpacing, fallbacks)
            : TmpTextLayout.Measure(font, style.Cased(text), size, wrapWidth, align, lineSpacing, characterSpacing, fallbacks);
    }

    /// <summary>
    /// The font TextMeshPro turns to when neither a font nor its own fallbacks
    /// have a letter: the game's settings name this one, and a curly apostrophe
    /// in a font without one comes from it.
    /// </summary>
    public const string LastResortFont = "LiberationSans SDF";   // English on purpose: the game's font name.

    /// <summary>A font's fallback chain as the game walks it.</summary>
    public static List<TmpFont> Fallbacks(UiFontSet set, IUiAssets assets)
    {
        var found = new List<TmpFont>();
        foreach (string name in set.Font.Fallbacks.Append(LastResortFont))
        {
            var also = assets.Font(name);
            if (also != null && !ReferenceEquals(also.Font, set.Font) && !found.Contains(also.Font)) found.Add(also.Font);
        }
        return found;
    }

    /// <summary>Draw what <see cref="Fit"/> made, into a new buffer. Letters from
    /// a fallback font are drawn from that font's atlas, and with its look.</summary>
    public static byte[] Draw(TextLayout laid, UiFontSet set, IUiAssets assets, int width, int height,
                              UiColor colour, UiTextStyle style)
    {
        var target = UiCompositor.NewLayer(width, height);
        foreach (var owned in laid.Glyphs.GroupBy(g => g.Glyph.Owner ?? set.Font))
        {
            var from = ReferenceEquals(owned.Key, set.Font) ? set : assets.Font(owned.Key.Name);
            if (from == null) continue;
            var part = new TextLayout { Height = laid.Height };
            part.Glyphs.AddRange(owned);
            UiTextRaster.DrawInto(target, width, height, part, from.Font, from.Alpha, from.Width, from.Height, colour,
                                  faceShift: style.Bold ? from.Font.BoldShift : -1,
                                  slant: style.Italic ? DialogueLook.ItalicSlant : 0,
                                  atlasColour: from.Colour);
        }
        if (style.Underline) Line(target, width, height, laid, set.Font, colour, set.Font.Face.UnderlineOffset, set.Font.Face.UnderlineThickness);
        if (style.Strikethrough) Line(target, width, height, laid, set.Font, colour, set.Font.Face.StrikethroughOffset, set.Font.Face.StrikethroughThickness);
        return target;
    }

    /// <summary>A rule along each line, at the height the font says, measured up
    /// from the baseline.</summary>
    private static void Line(byte[] target, int width, int height, TextLayout laid, TmpFont font, UiColor colour,
                             double offset, double thickness)
    {
        foreach (var line in laid.Glyphs.GroupBy(g => g.Line))
        {
            var first = line.FirstOrDefault(g => g.Glyph.Metrics.Width > 0 && g.Width > 0);
            if (first.Glyph == null) continue;
            double scale = first.Width / first.Glyph.Metrics.Width;
            double baseline = first.Y + first.Glyph.Metrics.BearingY * scale;
            double fontScale = scale / Math.Max(0.0001, first.Glyph.Scale);

            int x0 = Math.Max(0, (int)Math.Floor(line.Min(g => g.X)));
            int x1 = Math.Min(width, (int)Math.Ceiling(line.Max(g => g.Right)));
            int y0 = Math.Max(0, (int)Math.Round(baseline - offset * fontScale));
            int y1 = Math.Min(height, y0 + Math.Max(1, (int)Math.Round(thickness * fontScale)));
            double a = colour.A / 255.0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int t = (y * width + x) * 4;
                    double keep = 1 - a;
                    target[t] = (byte)(colour.B * a + target[t] * keep + 0.5);
                    target[t + 1] = (byte)(colour.G * a + target[t + 1] * keep + 0.5);
                    target[t + 2] = (byte)(colour.R * a + target[t + 2] * keep + 0.5);
                    target[t + 3] = (byte)(255 * a + target[t + 3] * keep + 0.5);
                }
        }
    }

    /// <summary>
    /// The size text set to shrink to fit ends up at: the largest between the
    /// two limits that fits, in steps of 0.05, which is where TextMeshPro's own
    /// search settles.
    /// </summary>
    public static double FitSize(TmpFont font, string text, UiTextStyle style, double width, double height, bool wrap,
                                 double min, double max, double lineSpacing, double characterSpacing,
                                 IReadOnlyList<TmpFont>? fallbacks)
    {
        if (max < min) max = min;
        if (Fits(max)) return max;

        double low = min, high = max;
        while (high - low > 0.05)
        {
            double middle = Math.Round((low + high) / 2 * 20) / 20;
            if (middle <= low || middle >= high) break;
            if (Fits(middle)) low = middle; else high = middle;
        }
        return low;

        bool Fits(double size)
        {
            var laid = Measure(font, text, style, size, wrap ? width : 0, TextAlign.Left,
                               lineSpacing, characterSpacing, fallbacks);
            return laid.Height - GapUnderLastLine(font, size) <= height && (wrap || laid.Width <= width);
        }
    }

    /// <summary>What a line's height allows below its descenders. The game's
    /// test stops at the descender of the last line.</summary>
    private static double GapUnderLastLine(TmpFont font, double size)
        => Math.Max(0, font.Face.LineHeight - (font.Face.AscentLine - font.Face.DescentLine)) * font.ScaleFor(size);

    /// <summary>
    /// Lines up a measured string in a box of the given size. Text is allowed
    /// to run past its box, as the game's does, so the buffer it is drawn into
    /// grows to hold it; <paramref name="Left"/> and <paramref name="Above"/> say
    /// how far that buffer reaches beyond the box.
    /// </summary>
    public static (TextLayout Laid, int Left, int Above, int Width, int Height) Fit(
        TextLayout layout, TmpFont font, double boxWidth, double boxHeight, bool wrapped, string? alignment)
    {
        string a = (alignment ?? "").ToLowerInvariant();

        // Unwrapped, the layout had no width to line up in, so it did it about
        // nought: left where it started, centred or right about the box's edge.
        double across = wrapped ? 0
                      : a.Contains("left") ? 0
                      : a.Contains("right") ? boxWidth
                      : boxWidth / 2.0;
        double down = a.Contains("top") ? 0
                    : a.Contains("bottom") ? boxHeight - layout.Height
                    : (boxHeight - layout.Height) / 2.0;

        double pad = font.Atlas.Padding * (layout.Glyphs.Count > 0 && layout.Glyphs[0].Glyph.Metrics.Width > 0
                                               ? layout.Glyphs[0].Width / layout.Glyphs[0].Glyph.Metrics.Width : 1) + 2;
        double lowest = 0, highest = boxWidth, upper = Math.Min(0, down), lower = Math.Max(boxHeight, down + layout.Height);
        foreach (var g in layout.Glyphs)
        {
            lowest = Math.Min(lowest, g.X + across - pad);
            highest = Math.Max(highest, g.Right + across + pad);
            upper = Math.Min(upper, g.Y + down - pad);
            lower = Math.Max(lower, g.Bottom + down + pad);
        }

        int left = (int)Math.Ceiling(Math.Max(0, -lowest)), above = (int)Math.Ceiling(Math.Max(0, -upper));
        int width = left + (int)Math.Ceiling(highest), height = above + (int)Math.Ceiling(lower);

        var laid = new TextLayout { Height = layout.Height };
        foreach (var g in layout.Glyphs) laid.Glyphs.Add(g with { X = g.X + across + left, Y = g.Y + down + above });
        laid.LineWidths.AddRange(layout.LineWidths);
        return (laid, left, above, width, height);
    }
}

/// <summary>
/// One stretch of a string laid out at its own size.
/// <para/>
/// <paramref name="SizeScale"/> is a multiple of the size passed to
/// <see cref="TmpTextLayout.MeasureRuns(TmpFont, IReadOnlyList{StyledRun}, double, double, TextAlign, double, double, IReadOnlyList{TmpFont}, double)"/>
/// — 0.7 for a <c>&lt;size=70%&gt;</c>. <paramref name="Tag"/> is the caller's
/// own number, handed back on every glyph this run produced
/// (<see cref="PlacedGlyph.Run"/>) so the drawer can find out what colour or
/// weight the run wanted. Nothing here reads it.
/// </summary>
public readonly record struct StyledRun(string Text, double SizeScale = 1, int Tag = 0);

/// <summary>
/// Turning a string into positioned glyphs, the way TextMeshPro does.
/// <para/>
/// Everything here is arithmetic over the font's own metrics: a pen walks the
/// line, each glyph is placed by its bearings, the pen moves by the advance
/// plus any kerning for that pair, and a line ends when the next word will not
/// fit. Those metrics came out of the game's own font assets, so the result is
/// the game's spacing rather than an approximation of it.
/// <para/>
/// What this deliberately does NOT do is claim to be TextMeshPro. Its line
/// breaker has behaviours around punctuation, CJK and rich text that are not
/// reproduced here, and auto-sizing searches for a fit in a way that is not
/// documented. So: right font, right glyphs, right advances, right kerning,
/// and line breaks that agree with TMP for ordinary Latin text. Where a string
/// is unusual enough for that to matter, the answer is Test in game, which runs
/// the real thing.
/// </summary>
public static class TmpTextLayout
{
    /// <summary>Lay out <paramref name="text"/> in a rectangle.</summary>
    /// <param name="wrapWidth">Width to wrap at, or 0 for no wrapping.</param>
    /// <param name="lineSpacing">Extra spacing between lines, in the font's own
    /// percentage-of-line-height units, as TMP stores it.</param>
    /// <param name="characterSpacing">Extra advance after every glyph, same units.</param>
    /// <param name="firstLineIndent">How far in the FIRST line starts, in the
    /// same units as <paramref name="wrapWidth"/>. For text that shares its
    /// opening line with something drawn beside it — a speaker's name, say — so
    /// the wrap accounts for the room that name took rather than running under
    /// it.</param>
    public static TextLayout Measure(TmpFont font, string? text, double fontSize,
                                     double wrapWidth = 0,
                                     TextAlign align = TextAlign.Center,
                                     double lineSpacing = 0,
                                     double characterSpacing = 0,
                                     IReadOnlyList<TmpFont>? fallbacks = null,
                                     double firstLineIndent = 0)
        => MeasureRuns(font, new[] { new StyledRun(text ?? "") }, fontSize, wrapWidth, align,
                       lineSpacing, characterSpacing, fallbacks, firstLineIndent);

    /// <summary>
    /// Lay out a string whose pieces are not all the same size.
    /// <para/>
    /// One pen walks the whole thing, so kerning and line breaking work ACROSS
    /// the joins: a <c>&lt;size=70%&gt;</c> in the middle of a sentence breaks
    /// the line where the sentence runs out, not where the run does.
    /// <para/>
    /// Only the SIZE is laid out here. Bold, italic and colour do not move a
    /// glyph, so they are the drawer's business — <see cref="StyledRun.Tag"/> is
    /// carried through to <see cref="PlacedGlyph.Run"/> for it to pick up.
    /// </summary>
    public static TextLayout MeasureRuns(TmpFont font, IReadOnlyList<StyledRun> runs,
                                         double fontSize,
                                         double wrapWidth = 0,
                                         TextAlign align = TextAlign.Center,
                                         double lineSpacing = 0,
                                         double characterSpacing = 0,
                                         IReadOnlyList<TmpFont>? fallbacks = null,
                                         double firstLineIndent = 0)
    {
        var layout = new TextLayout();
        if (font == null || runs == null || runs.Count == 0) return layout;

        double scale = font.ScaleFor(fontSize);
        var lines = Break(font, runs, scale, wrapWidth, lineSpacing, characterSpacing,
                          fallbacks, layout, firstLineIndent);

        // The top of the line being placed. Walked down a line at a time rather
        // than multiplied out, because with mixed sizes the lines are not all
        // the same height.
        double top = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            double s = line.MaxScale > 0 ? line.MaxScale : scale;

            // Ascent is measured up from the baseline, so in drawing
            // coordinates the baseline sits that far DOWN from the top of the
            // line. Getting this sign wrong hangs every line one glyph-height
            // above where it belongs, which reads as a margin problem rather
            // than as a sign error. The tallest thing on the line sets it, so
            // a big word and a small one still sit on one baseline.
            double baseline = top + font.Face.AscentLine * s;
            double lineHeight = font.Face.LineHeight * s;

            double x = align switch
            {
                TextAlign.Right => wrapWidth - line.Width,
                TextAlign.Center => wrapWidth > 0 ? (wrapWidth - line.Width) / 2.0 : -line.Width / 2.0,
                _ => 0,
            };

            foreach (var placed in line.Glyphs)
            {
                var m = placed.Glyph.Metrics;
                double g = placed.Scale * placed.Glyph.Scale;
                double gx = x + placed.PenX + m.BearingX * g;
                double gy = baseline - m.BearingY * g;
                layout.Glyphs.Add(new PlacedGlyph(
                    placed.Unicode, placed.Glyph, gx, gy,
                    m.Width * g, m.Height * g, i, placed.Run, placed.Index));
            }
            layout.LineWidths.Add(line.Width);

            // The gap after a line belongs between it and the next one; the
            // last line's height is where the text ends.
            top += i < lines.Count - 1
                 ? lineHeight + lineSpacing / 100.0 * font.Face.PointSize * s
                 : lineHeight;
        }

        layout.Height = top;
        return layout;
    }

    // ── Breaking into lines ──────────────────────────────────────────

    /// <summary>A glyph on a line, with the scale it was laid out at and the
    /// advance it cost. Both are kept so a word carried onto the next line can
    /// be re-laid without working them out again from a size that may not be
    /// the one it was measured at.</summary>
    private readonly record struct Pending(int Unicode, TmpFont.Glyph Glyph, double PenX,
                                           double Scale, double Advance, int Run, int Index);

    private sealed class Line
    {
        public readonly List<Pending> Glyphs = new();
        public double Width;

        /// <summary>The biggest scale anything on this line was laid out at,
        /// which is what sets the line's height and its baseline.</summary>
        public double MaxScale;

        public void Add(Pending glyph)
        {
            Glyphs.Add(glyph);
            if (glyph.Scale > MaxScale) MaxScale = glyph.Scale;
        }

        /// <summary>After glyphs have been taken off the end, the tallest
        /// remaining one sets the height - so a line does not stay as tall as
        /// the big word that was carried off it.</summary>
        public void Remeasure()
        {
            MaxScale = 0;
            foreach (var g in Glyphs) if (g.Scale > MaxScale) MaxScale = g.Scale;
        }
    }

    private static List<Line> Break(TmpFont font, IReadOnlyList<StyledRun> runs,
                                    double baseScale, double wrapWidth,
                                    double lineSpacing, double characterSpacing,
                                    IReadOnlyList<TmpFont>? fallbacks, TextLayout layout,
                                    double firstLineIndent = 0)
    {
        var lines = new List<Line>();
        var line = new Line();

        // The indent is spent out of the FIRST line's pen, so everything after
        // it - the wrap test included - sees a line that much shorter. Every
        // line after starts at the left edge, which is what makes it an indent
        // rather than a margin.
        double pen = firstLineIndent > 0 ? firstLineIndent : 0;
        TmpFont.Glyph? previous = null;

        // Where the line could be cut, and what the pen read there. Kept so a
        // word that overruns can be moved down whole rather than split.
        int breakAt = -1;
        double breakPen = 0;

        // How much of the whole string the runs before this one took up, so a
        // glyph can say where its character is in the text as read.
        int consumed = 0;

        foreach (var run in runs)
        {
            string text = run.Text ?? "";
            double scale = baseScale * (run.SizeScale > 0 ? run.SizeScale : 1);

            // By the size of the text, not of this run: small capitals are closer
            // to one another only because they are smaller.
            double extraPerGlyph = (characterSpacing + font.NormalSpacing) / 100.0 * font.Face.PointSize * baseScale;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (ch == '\r') continue;
                if (ch == '\n')
                {
                    line.Width = pen;
                    lines.Add(line);
                    line = new Line();
                    pen = 0;
                    previous = null;
                    breakAt = -1;
                    continue;
                }

                var glyph = Resolve(font, ch, fallbacks);
                if (glyph == null)
                {
                    if (!layout.Missing.Contains(ch)) layout.Missing.Add(ch);
                    continue;
                }

                double kern = font.KerningBetween(previous, glyph) * scale;
                double advance = glyph.Metrics.Advance * scale * glyph.Scale + extraPerGlyph;

                if (ch == ' ')
                {
                    breakAt = line.Glyphs.Count;
                    breakPen = pen;
                }

                // Overrunning: move the last word down rather than splitting it.
                // A word longer than the whole width has nowhere to go and is
                // allowed to overrun, which is what TMP does too.
                if (wrapWidth > 0 && pen + kern + advance > wrapWidth && line.Glyphs.Count > 0)
                {
                    if (breakAt > 0)
                    {
                        var carried = line.Glyphs.Skip(breakAt).ToList();
                        line.Glyphs.RemoveRange(breakAt, line.Glyphs.Count - breakAt);
                        line.Width = breakPen;
                        line.Remeasure();
                        lines.Add(line);

                        line = new Line();
                        pen = 0;
                        // Re-lay the carried word from the new left edge,
                        // dropping the space that ended the previous line. Each
                        // glyph carries the advance it was measured with, which
                        // is the only honest number when the word crosses a
                        // change of size.
                        foreach (var c in carried.SkipWhile(c => c.Unicode == ' '))
                        {
                            line.Add(c with { PenX = pen });
                            pen += c.Advance;
                        }
                        previous = line.Glyphs.Count > 0 ? line.Glyphs[^1].Glyph : null;
                        breakAt = -1;
                        kern = font.KerningBetween(previous, glyph) * scale;
                    }
                    else
                    {
                        line.Width = pen;
                        lines.Add(line);
                        line = new Line();
                        pen = 0;
                        previous = null;
                        breakAt = -1;
                        kern = 0;
                    }
                }

                pen += kern;
                line.Add(new Pending(ch, glyph, pen, scale, advance, run.Tag, consumed + i));
                pen += advance;
                previous = glyph;
            }

            consumed += text.Length;
        }

        line.Width = pen;
        lines.Add(line);

        // A trailing space should not widen the box it is measured into.
        foreach (var l in lines)
        {
            while (l.Glyphs.Count > 0 && l.Glyphs[^1].Unicode == ' ')
            {
                l.Width = l.Glyphs[^1].PenX;
                l.Glyphs.RemoveAt(l.Glyphs.Count - 1);
                l.Remeasure();
            }

            // Spacing goes between letters: none after the last, or a centred
            // line sits half a gap to the left of where the game puts it.
            if (characterSpacing + font.NormalSpacing != 0 && l.Glyphs.Count > 0)
                l.Width -= (characterSpacing + font.NormalSpacing) / 100.0 * font.Face.PointSize * baseScale;
        }
        return lines;
    }

    /// <summary>The glyph for a character, walking the fallback chain when this
    /// font never baked one. Reproducing the chain is what stops the preview
    /// failing where the game succeeds.</summary>
    private static TmpFont.Glyph? Resolve(TmpFont font, char ch,
                                          IReadOnlyList<TmpFont>? fallbacks)
    {
        var glyph = font.GlyphFor(ch);
        if (glyph != null) return glyph;
        if (fallbacks == null) return null;
        foreach (var fallback in fallbacks)
        {
            glyph = fallback.GlyphFor(ch);
            if (glyph != null) return glyph;
        }
        return null;
    }
}
