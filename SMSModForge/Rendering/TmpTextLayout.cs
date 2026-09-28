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
            double extraPerGlyph = characterSpacing / 100.0 * font.Face.PointSize * scale;

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
