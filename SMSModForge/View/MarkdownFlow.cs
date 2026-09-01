using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SMSModForge.View;

/// <summary>
/// Turns a release body into something worth reading.
/// <para/>
/// Release notes are markdown, and shown raw they are mostly punctuation: the
/// headings arrive as hashes, the emphasis as asterisks, and the reader has to
/// do the rendering in their head while deciding whether to install something.
/// <para/>
/// This covers what release notes are actually written in — headings, bullets,
/// paragraphs, bold, italic, inline code, links, strikethrough — and nothing
/// else. It is not a markdown implementation and should not grow into one: a
/// full one is a library, and the only document it will ever be given is a
/// CHANGELOG section.
/// <para/>
/// Anything it does not recognise comes through as the text that was written,
/// which is the failure worth having. Notes that render imperfectly are still
/// notes; notes that throw are a window that will not open.
/// </summary>
public static class MarkdownFlow
{
    /// <summary>Build a document, sized and coloured to sit in the editor.</summary>
    public static FlowDocument ToDocument(string markdown, double fontSize = 12)
    {
        var doc = new FlowDocument
        {
            FontSize = fontSize,
            PagePadding = new Thickness(8),
            // Without this the viewer lays long notes out in NEWSPAPER COLUMNS
            // as soon as the window is wide enough, and the reader has to work
            // out that the text carries on somewhere up and to the right.
            ColumnWidth = double.PositiveInfinity,
        };
        // Follows the theme rather than freezing whatever it is now.
        doc.SetResourceReference(TextElement.ForegroundProperty, "Theme.Text");

        foreach (var block in ParseBlocks(markdown ?? "", fontSize))
            doc.Blocks.Add(block);
        return doc;
    }

    // ── Blocks ────────────────────────────────────────────────────────

    private static IEnumerable<Block> ParseBlocks(string markdown, double fontSize)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var paragraph = new List<string>();   // a run of ordinary lines
        List<List<string>>? bullets = null;   // a run of list items, each its own lines
        var blocks = new List<Block>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(Para(string.Join(" ", paragraph),
                            margin: new Thickness(0, 0, 0, 6)));
            paragraph.Clear();
        }

        void FlushBullets()
        {
            if (bullets == null) return;
            var list = new List
            {
                Margin = new Thickness(0, 0, 0, 6),
                // WPF draws the marker INSIDE this padding, so a zero here is a
                // list with no bullets on it - which is what the first cut of
                // this shipped, and it read as a wall of unrelated sentences.
                Padding = new Thickness(18, 0, 0, 0),
                MarkerStyle = TextMarkerStyle.Disc,
            };
            foreach (var item in bullets)
                list.ListItems.Add(new ListItem(
                    Para(string.Join(" ", item), margin: new Thickness(0, 0, 0, 3))));
            blocks.Add(list);
            bullets = null;
        }

        foreach (var raw in lines)
        {
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushBullets();
                continue;
            }

            // Headings.
            int hashes = 0;
            while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
            if (hashes is > 0 and <= 6 && hashes < trimmed.Length && trimmed[hashes] == ' ')
            {
                FlushParagraph();
                FlushBullets();
                var heading = Para(trimmed.Substring(hashes + 1).Trim(),
                                   margin: new Thickness(0, blocks.Count == 0 ? 0 : 10, 0, 4));
                heading.FontWeight = FontWeights.Bold;
                // Only two steps, and only up: release notes nest three deep at
                // most, and a heading that dwarfs the text it introduces reads
                // as decoration.
                heading.FontSize = fontSize + (hashes <= 1 ? 4 : hashes == 2 ? 2 : 1);
                blocks.Add(heading);
                continue;
            }

            // Bullets. Both markers, because both get written.
            bool isBullet = (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                            && trimmed.Length > 2;
            if (isBullet)
            {
                FlushParagraph();
                bullets ??= new List<List<string>>();
                bullets.Add(new List<string> { trimmed.Substring(2).Trim() });
                continue;
            }

            // An indented line under a bullet is that bullet still going: the
            // CHANGELOG wraps at 80 columns, so most items are several lines and
            // only the first carries the dash.
            if (bullets is { Count: > 0 } && raw.StartsWith(" "))
            {
                bullets[^1].Add(trimmed);
                continue;
            }

            FlushBullets();
            paragraph.Add(trimmed);
        }

        FlushParagraph();
        FlushBullets();
        return blocks;
    }

    /// <summary>A paragraph of parsed inlines. Paragraph’s constructor takes
    /// a single Inline, so a sequence has to be added rather than passed.</summary>
    private static Paragraph Para(string text, Thickness margin)
    {
        var paragraph = new Paragraph { Margin = margin };
        foreach (var inline in Spans(text)) paragraph.Inlines.Add(inline);
        return paragraph;
    }

    // ── Inline runs ───────────────────────────────────────────────────

    /// <summary>
    /// The inline markup of one block's worth of text. Public because this is
    /// the part with edges — every way emphasis can be nested, unclosed, or
    /// merely punctuation — and those are worth checking directly.
    /// </summary>
    public static IEnumerable<Inline> Spans(string text)
    {
        var result = new List<Inline>();
        int i = 0;
        var plain = new System.Text.StringBuilder();

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            result.Add(new Run(plain.ToString()));
            plain.Clear();
        }

        while (i < text.Length)
        {
            char c = text[i];

            // Inline code first, and its contents are never looked at again:
            // `**` inside backticks is two asterisks, not emphasis.
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i + 1)
                {
                    FlushPlain();
                    result.Add(new Run(text.Substring(i + 1, close - i - 1))
                    {
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        Background = new SolidColorBrush(Color.FromArgb(28, 128, 128, 128)),
                    });
                    i = close + 1;
                    continue;
                }
            }
            else if (c == '[')
            {
                int close = text.IndexOf(']', i);
                if (close > i && close + 1 < text.Length && text[close + 1] == '(')
                {
                    int end = text.IndexOf(')', close + 2);
                    if (end > close)
                    {
                        FlushPlain();
                        string label = text.Substring(i + 1, close - i - 1);
                        string url = text.Substring(close + 2, end - close - 2);
                        result.Add(Link(label, url));
                        i = end + 1;
                        continue;
                    }
                }
            }
            else if (Marker(text, i, "**") || Marker(text, i, "__"))
            {
                string marker = text.Substring(i, 2);
                int close = text.IndexOf(marker, i + 2, StringComparison.Ordinal);
                // close > i + 2 and not i + 1: there has to be something
                // between the markers. "****" is four asterisks, not an
                // emphasis of nothing.
                if (close > i + 2 && Standalone(text, marker[0], i, close + 1))
                {
                    FlushPlain();
                    result.Add(new Bold(new Span(Wrap(Spans(text.Substring(i + 2, close - i - 2))))));
                    i = close + 2;
                    continue;
                }
            }
            else if (Marker(text, i, "~~"))
            {
                int close = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    FlushPlain();
                    var span = new Span(Wrap(Spans(text.Substring(i + 2, close - i - 2))))
                    {
                        TextDecorations = TextDecorations.Strikethrough,
                    };
                    result.Add(span);
                    i = close + 2;
                    continue;
                }
            }
            else if (c is '*' or '_')
            {
                int close = text.IndexOf(c, i + 1);
                // A lone asterisk, or one with nothing between it and the next,
                // is punctuation somebody typed. Leave it where it is.
                if (close > i + 1 && Standalone(text, c, i, close))
                {
                    FlushPlain();
                    result.Add(new Italic(new Span(Wrap(Spans(text.Substring(i + 1, close - i - 1))))));
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(c);
            i++;
        }

        FlushPlain();
        return result;
    }

    /// <summary>
    /// Whether an underscore run is emphasis rather than part of a word.
    /// <para/>
    /// snake_case_name is a name, and treating its underscores as emphasis
    /// italicised the middle of it — which matters here because the notes are
    /// full of identifiers. Asterisks have no such problem: nobody puts one
    /// inside a word, and requiring a boundary would break **bold**ing a
    /// prefix.
    /// </summary>
    private static bool Standalone(string text, char marker, int open, int close)
    {
        if (marker != '_') return true;
        bool beforeOk = open == 0 || !char.IsLetterOrDigit(text[open - 1]);
        bool afterOk = close + 1 >= text.Length || !char.IsLetterOrDigit(text[close + 1]);
        return beforeOk && afterOk;
    }

    private static bool Marker(string text, int at, string marker)
        => at + marker.Length <= text.Length
           && string.CompareOrdinal(text, at, marker, 0, marker.Length) == 0;

    /// <summary>A Span's constructor takes one Inline, so a sequence has to be
    /// hung off a first element and the rest added after it.</summary>
    private static Inline Wrap(IEnumerable<Inline> inlines)
    {
        var span = new Span();
        foreach (var inline in inlines) span.Inlines.Add(inline);
        return span;
    }

    private static Inline Link(string label, string url)
    {
        var link = new Hyperlink(new Run(label)) { ToolTip = url };
        link.RequestNavigate += (_, e) => Open(e.Uri.ToString());
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) link.NavigateUri = uri;
        else link.Click += (_, _) => Open(url);
        return link;
    }

    private static void Open(string url)
    {
        // Only the two schemes a release note has any business linking to. A
        // body is text from a server, and handing an arbitrary string to the
        // shell is how that becomes something else.
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser, or the author's machine said no */ }
    }
}
