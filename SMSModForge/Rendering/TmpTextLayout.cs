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
    int Line)
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
    public static TextLayout Measure(TmpFont font, string? text, double fontSize,
                                     double wrapWidth = 0,
                                     TextAlign align = TextAlign.Center,
                                     double lineSpacing = 0,
                                     double characterSpacing = 0,
                                     IReadOnlyList<TmpFont>? fallbacks = null)
    {
        var layout = new TextLayout();
        if (font == null || string.IsNullOrEmpty(text)) return layout;

        double scale = font.ScaleFor(fontSize);
        double lineHeight = font.Face.LineHeight * scale;
        double lineStep = lineHeight + lineSpacing / 100.0 * font.Face.PointSize * scale;
        double extraPerGlyph = characterSpacing / 100.0 * font.Face.PointSize * scale;

        // The baseline of the first line. Ascent is measured up from the
        // baseline, so in drawing coordinates the baseline sits that far DOWN
        // from the top of the text. Getting this sign wrong hangs every line one
        // glyph-height above where it belongs, which reads as a margin problem
        // rather than as a sign error.
        double baseline = font.Face.AscentLine * scale;

        var lines = Break(font, text!, scale, wrapWidth, extraPerGlyph, fallbacks, layout);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            double y = baseline + i * lineStep;
            double x = align switch
            {
                TextAlign.Right => wrapWidth - line.Width,
                TextAlign.Center => wrapWidth > 0 ? (wrapWidth - line.Width) / 2.0 : -line.Width / 2.0,
                _ => 0,
            };

            foreach (var placed in line.Glyphs)
            {
                var m = placed.Glyph.Metrics;
                double gx = x + placed.PenX + m.BearingX * scale * placed.Glyph.Scale;
                double gy = y - m.BearingY * scale * placed.Glyph.Scale;
                layout.Glyphs.Add(new PlacedGlyph(
                    placed.Unicode, placed.Glyph, gx, gy,
                    m.Width * scale * placed.Glyph.Scale,
                    m.Height * scale * placed.Glyph.Scale, i));
            }
            layout.LineWidths.Add(line.Width);
        }

        layout.Height = lines.Count == 0 ? 0 : (lines.Count - 1) * lineStep + lineHeight;
        return layout;
    }

    // ── Breaking into lines ──────────────────────────────────────────

    private readonly record struct Pending(int Unicode, TmpFont.Glyph Glyph, double PenX);

    private sealed class Line
    {
        public readonly List<Pending> Glyphs = new();
        public double Width;
    }

    private static List<Line> Break(TmpFont font, string text, double scale,
                                    double wrapWidth, double extraPerGlyph,
                                    IReadOnlyList<TmpFont>? fallbacks, TextLayout layout)
    {
        var lines = new List<Line>();
        var line = new Line();
        double pen = 0;
        TmpFont.Glyph? previous = null;

        // Where the line could be cut, and what the pen read there. Kept so a
        // word that overruns can be moved down whole rather than split.
        int breakAt = -1;
        double breakPen = 0;

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

            // Overrunning: move the last word down rather than splitting it. A
            // word longer than the whole width has nowhere to go and is allowed
            // to overrun, which is what TMP does too.
            if (wrapWidth > 0 && pen + kern + advance > wrapWidth && line.Glyphs.Count > 0)
            {
                if (breakAt > 0)
                {
                    var carried = line.Glyphs.Skip(breakAt).ToList();
                    line.Glyphs.RemoveRange(breakAt, line.Glyphs.Count - breakAt);
                    line.Width = breakPen;
                    lines.Add(line);

                    line = new Line();
                    pen = 0;
                    // Re-lay the carried word from the new left edge, dropping
                    // the space that ended the previous line.
                    foreach (var c in carried.SkipWhile(c => c.Unicode == ' '))
                    {
                        line.Glyphs.Add(c with { PenX = pen });
                        pen += c.Glyph.Metrics.Advance * scale * c.Glyph.Scale + extraPerGlyph;
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
            line.Glyphs.Add(new Pending(ch, glyph, pen));
            pen += advance;
            previous = glyph;
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
