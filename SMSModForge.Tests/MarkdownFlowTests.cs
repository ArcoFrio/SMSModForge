using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Documents;
using SMSModForge.View;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The release notes, rendered.
/// <para/>
/// Flattened back to a marked-up string so a test can say what it expects in
/// one line. <c>[b ...]</c> is bold, <c>[i ...]</c> italic, <c>[c ...]</c>
/// inline code, <c>[s ...]</c> struck through, <c>[link ...]</c> a hyperlink —
/// so the assertions read as the thing a person would see.
/// </summary>
public class MarkdownFlowTests
{
    private readonly ITestOutputHelper _out;
    public MarkdownFlowTests(ITestOutputHelper o) => _out = o;

    private static string Flat(IEnumerable<Inline> inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run when run.FontFamily?.Source?.StartsWith("Consolas") == true:
                    sb.Append("[c ").Append(run.Text).Append(']'); break;
                case Run run:
                    sb.Append(run.Text); break;
                case Bold bold:
                    sb.Append("[b ").Append(Flat(bold.Inlines)).Append(']'); break;
                case Italic italic:
                    sb.Append("[i ").Append(Flat(italic.Inlines)).Append(']'); break;
                case Hyperlink link:
                    sb.Append("[link ").Append(Flat(link.Inlines)).Append(']'); break;
                case Span span when span.TextDecorations?.Count > 0:
                    sb.Append("[s ").Append(Flat(span.Inlines)).Append(']'); break;
                case Span span:
                    sb.Append(Flat(span.Inlines)); break;
            }
        }
        return sb.ToString();
    }

    private static string Inline(string markdown) => Flat(MarkdownFlow.Spans(markdown));

    // ── The markup release notes are written in ───────────────────────

    [Theory]
    [InlineData("plain words", "plain words")]
    [InlineData("**bold**", "[b bold]")]
    [InlineData("a **bold** word", "a [b bold] word")]
    [InlineData("__also bold__", "[b also bold]")]
    [InlineData("*italic*", "[i italic]")]
    [InlineData("_italic_", "[i italic]")]
    [InlineData("`code`", "[c code]")]
    [InlineData("~~gone~~", "[s gone]")]
    [InlineData("**two** and **more**", "[b two] and [b more]")]
    [InlineData("[label](https://example.invalid)", "[link label]")]
    public void Reads_the_markup_that_gets_written(string markdown, string expected)
        => Assert.Equal(expected, Inline(markdown));

    [Fact]
    public void Bold_can_hold_code_which_is_how_the_changelog_writes_settings()
    {
        // e.g. "**Options ▸ `GameFolder`**" - the real notes nest these.
        Assert.Equal("[b a [c b] c]", Inline("**a `b` c**"));
    }

    [Fact]
    public void Code_is_literal_all_the_way_through()
    {
        // The one rule that matters most: asterisks inside backticks are
        // asterisks. A glob or a C# pointer in a note must not turn the rest of
        // the sentence italic.
        Assert.Equal("[c **not bold**]", Inline("`**not bold**`"));
        Assert.Equal("[c *.png]", Inline("`*.png`"));
    }

    [Theory]
    // Punctuation somebody typed, not markup they meant. Each of these came out
    // of writing release notes, and each must survive as itself.
    [InlineData("2 * 3 = 6")]
    [InlineData("a lone * asterisk")]
    [InlineData("**unclosed bold")]
    [InlineData("snake_case_name")]
    [InlineData("~~unclosed strike")]
    [InlineData("[not a link")]
    [InlineData("[label] (spaced)")]
    [InlineData("100% * nothing")]
    public void Punctuation_that_is_not_markup_survives_as_itself(string text)
        => Assert.Equal(text, Inline(text));

    [Fact]
    public void An_empty_emphasis_is_not_emphasis()
    {
        // "**" with nothing inside is two asterisks, and must not eat the rest
        // of the line looking for a partner.
        Assert.Equal("****", Inline("****"));
        Assert.Equal("a ** b", Inline("a ** b"));
    }

    // ── Blocks ────────────────────────────────────────────────────────

    private static FlowDocument Doc(params string[] lines)
        => MarkdownFlow.ToDocument(string.Join("\n", lines));

    [Fact]
    public void Headings_bullets_and_paragraphs_come_out_as_themselves()
    {
        var doc = Doc(
            "### Added",
            "",
            "- One thing.",
            "- Another thing.",
            "",
            "A closing paragraph.");

        var blocks = doc.Blocks.ToList();
        Assert.Equal(3, blocks.Count);

        var heading = Assert.IsType<Paragraph>(blocks[0]);
        Assert.Equal("Added", Flat(heading.Inlines));
        Assert.True(heading.FontSize > doc.FontSize, "a heading has to look like one");
        Assert.Equal(System.Windows.FontWeights.Bold, heading.FontWeight);

        var list = Assert.IsType<List>(blocks[1]);
        Assert.Equal(2, list.ListItems.Count);
        // WPF draws list markers inside the left padding, so a zero there is a
        // list with no bullets on it. That shipped once and read as a wall of
        // unrelated sentences, and nothing about the block tree showed it.
        Assert.True(list.Padding.Left > 0, "the bullets need room to be drawn in");
        Assert.Equal(System.Windows.TextMarkerStyle.Disc, list.MarkerStyle);

        Assert.IsType<Paragraph>(blocks[2]);
    }

    [Fact]
    public void A_wrapped_bullet_is_one_bullet()
    {
        // The CHANGELOG wraps at 80 columns, so most items run to three or four
        // lines and only the first carries the dash. Treating those as separate
        // items would turn every entry into a stack of fragments.
        var doc = Doc(
            "- An item that carries on",
            "  over another line",
            "  and one more.",
            "- A second item.");

        var list = Assert.IsType<List>(doc.Blocks.First());
        Assert.Equal(2, list.ListItems.Count);

        var first = Assert.IsType<Paragraph>(list.ListItems.First().Blocks.First());
        Assert.Equal("An item that carries on over another line and one more.", Flat(first.Inlines));
    }

    [Fact]
    public void The_hashes_and_dashes_do_not_survive_into_the_text()
    {
        // What was actually reported: the box was full of punctuation.
        var doc = Doc("### Added", "", "- **A thing** that `works`.");
        string all = string.Join(" ", doc.Blocks.Select(b => b switch
        {
            Paragraph p => Flat(p.Inlines),
            List l => string.Join(" ", l.ListItems.Select(
                i => Flat(((Paragraph)i.Blocks.First()).Inlines))),
            _ => "",
        }));

        _out.WriteLine(all);
        Assert.DoesNotContain("###", all);
        Assert.DoesNotContain("**", all);
        Assert.DoesNotContain("`", all);
        Assert.Contains("[b A thing]", all);
    }

    [Fact]
    public void The_real_release_notes_render_without_complaint()
    {
        // The document this will actually be handed: the 1.1.0 section of the
        // CHANGELOG, which is what the published release body is.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
            dir = dir.Parent;
        Assert.True(dir != null, "no CHANGELOG.md above " + AppContext.BaseDirectory);

        string changelog = File.ReadAllText(Path.Combine(dir!.FullName, "CHANGELOG.md"));
        var doc = MarkdownFlow.ToDocument(changelog);

        _out.WriteLine($"{changelog.Length} chars -> {doc.Blocks.Count} blocks, " +
                       $"{doc.Blocks.OfType<List>().Sum(l => l.ListItems.Count)} bullets");
        Assert.True(doc.Blocks.Count > 20, "the whole changelog is more than a few blocks");
        Assert.Contains(doc.Blocks, b => b is List);
    }

    [Fact]
    public void Nothing_at_all_is_an_empty_document_rather_than_a_crash()
    {
        Assert.Empty(MarkdownFlow.ToDocument("").Blocks);
        Assert.Empty(MarkdownFlow.ToDocument("   \n\n  ").Blocks);
        Assert.Empty(MarkdownFlow.ToDocument(null!).Blocks);
    }
}
