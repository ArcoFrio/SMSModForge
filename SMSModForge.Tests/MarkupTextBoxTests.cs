using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The dialogue line shows its own formatting while it is being written.
/// <para/>
/// A RichTextBox underneath a TextBox-shaped surface, which is a trade: an
/// author gets to see that <c>&lt;b&gt;</c> is markup and <c>&lt;bold&gt;</c> is
/// four characters the player will read, and in exchange the control has to
/// keep a flow document and a plain string saying exactly the same thing.
/// <para/>
/// So the first tests here are not about formatting at all. They are about the
/// text surviving: this is the field an author spends their time in, and a
/// control that drops a character while somebody types would be far worse than
/// one that shows no formatting at all.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class MarkupTextBoxTests
{
    private readonly ITestOutputHelper _out;
    public MarkupTextBoxTests(ITestOutputHelper o) => _out = o;

    /// <summary>A realised control, on a window, the way the editor has it.</summary>
    private static MarkupTextBox Box(string text = "")
    {
        var box = new MarkupTextBox { Width = 300, MinHeight = 60, Text = text };
        var window = new Window
        {
            Width = 400, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, Content = box,
        };
        window.Show();
        WindowHarness.Pump();
        return box;
    }

    /// <summary>Every Run in the document, in order.</summary>
    private static Run[] Runs(MarkupTextBox box)
        => ((Paragraph)box.Document.Blocks.FirstBlock).Inlines.OfType<Run>().ToArray();

    [Theory]
    [InlineData("")]
    [InlineData("A plain line.")]
    [InlineData("<b>bold</b> and <i>italic</i>")]
    [InlineData("<size=70%>quiet")]
    [InlineData("line one\nline two")]
    [InlineData("<b>over\ntwo lines</b>")]
    [InlineData("trailing newline\n")]
    [InlineData("\nleading newline")]
    [InlineData("{PC} said [PV:gold] <color=#f66>in red</color>")]
    [InlineData("<notatag>printed verbatim</notatag>")]
    public void WhatGoesInComesBackOut(string text)
    {
        // The property everything else depends on. The document is a RENDERING
        // of the string; if reading it back does not reproduce the string, the
        // control is corrupting the pack as somebody types.
        WindowHarness.Run(_ =>
        {
            var box = Box(text);
            _out.WriteLine($"'{text}' -> {Runs(box).Length} run(s)");
            Assert.Equal(text, box.Text);
        });
    }

    [Fact]
    public void TypingAtTheEndKeepsEverythingBeforeIt()
    {
        // Driven through the document, which is what a keystroke does - not by
        // setting Text, which is the path a binding takes.
        WindowHarness.Run(_ =>
        {
            var box = Box("<b>bold</b> ");
            box.CaretPosition = box.Document.ContentEnd;
            box.CaretPosition.InsertTextInRun("more");
            WindowHarness.Pump();

            _out.WriteLine($"after typing: '{box.Text}'");
            Assert.Equal("<b>bold</b> more", box.Text);
        });
    }

    [Fact]
    public void TheCaretStaysWhereItWasWhenTheLineIsRestyled()
    {
        // The restyle rebuilds the document, so the caret has to be put back or
        // every keystroke inside a styled line would throw the author to the
        // end of it.
        WindowHarness.Run(_ =>
        {
            var box = Box("<b>bold</b> tail");
            box.CaretIndex = 7;                    // inside the word "bold"
            WindowHarness.Pump();

            box.Text = "<b>bold</b> tail!";        // as a binding update would
            WindowHarness.Pump();

            _out.WriteLine($"caret at {box.CaretIndex}");
            Assert.Equal(7, box.CaretIndex);
        });
    }

    [Fact]
    public void CharacterOffsetsSurviveLineBreaks()
    {
        // A LineBreak is one character of the string and one element of the
        // document, and the two counts have to agree or the spelling menu picks
        // the wrong word on any line but the first.
        WindowHarness.Run(_ =>
        {
            const string text = "one\ntwo";
            var box = Box(text);

            for (int i = 0; i <= text.Length; i++)
            {
                box.CaretIndex = i;
                Assert.Equal(i, box.CaretIndex);
            }
            _out.WriteLine($"all {text.Length + 1} offsets round-tripped");
        });
    }

    // ── What it actually shows ────────────────────────────────────────

    [Fact]
    public void TagsAreSetApartAndTheTextTheyWrapIsStyled()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("plain <b>bold</b> plain");
            var runs = Runs(box);
            foreach (var r in runs)
                _out.WriteLine($"   '{r.Text}'  weight={r.FontWeight}  fg={r.Foreground}");

            var tag = runs.Single(r => r.Text == "<b>");
            var inside = runs.Single(r => r.Text == "bold");
            var outside = runs.First(r => r.Text == "plain ");

            Assert.Equal(FontWeights.Bold, inside.FontWeight);
            Assert.NotEqual(FontWeights.Bold, tag.FontWeight);
            Assert.NotEqual(FontWeights.Bold, outside.FontWeight);

            // The tag gets a colour and a chip of its own; the words get
            // neither, and keep whatever the box is written in.
            Assert.NotNull(tag.Foreground);
            Assert.NotNull(tag.Background);
            Assert.Null(outside.ReadLocalValue(TextElement.ForegroundProperty) as Brush);
            Assert.Null(outside.ReadLocalValue(TextElement.BackgroundProperty) as Brush);
        });
    }

    // ── What a tag is written in ──────────────────────────────────────

    /// <summary>How far apart two colours read, by the ratio the accessibility
    /// guidelines use: 1 is the same colour and 21 is black on white. 4.5 is
    /// their bar for ordinary text.</summary>
    private static double Contrast(Color a, Color b)
    {
        double La = Luminance(a), Lb = Luminance(b);
        double hi = Math.Max(La, Lb), lo = Math.Min(La, Lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
    {
        double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static Color Of(object? resource)
    {
        Assert.NotNull(resource);
        return ((SolidColorBrush)resource!).Color;
    }

    [Fact]
    public void ATagIsLegibleOnEveryTheme()
    {
        // It was one flat grey on all ten, and grey is the colour that fails at
        // BOTH ends: too pale on the white boxes, too dark on the dark ones.
        // The control is at the bottom - that same grey, measured, failing.
        WindowHarness.Run(_ =>
        {
            var themes = SMSModForge.Services.ThemeManager.All;
            var was = SMSModForge.Services.ThemeManager.Current;
            try
            {
                foreach (var theme in themes)
                {
                    SMSModForge.Services.ThemeManager.Apply(theme);
                    WindowHarness.Pump();

                    var app = Application.Current.Resources;
                    var box = Of(app[SMSModForge.Services.ThemeManager.KeyControl]);
                    var ink = Of(app[SMSModForge.Services.ThemeManager.KeyMarkup]);
                    var chip = Of(app[SMSModForge.Services.ThemeManager.KeyMarkupBack]);

                    double onBox = Contrast(ink, box);
                    double onChip = Contrast(ink, chip);
                    _out.WriteLine($"   {theme.Name,-10} ink {ink} on box {box}: "
                                   + $"{onBox:0.00}, on its chip: {onChip:0.00}");

                    Assert.True(onBox >= 4.5,
                                $"{theme.Name}: a tag is {onBox:0.00} against the box it is "
                                + "written in");
                    Assert.True(onChip >= 4.0,
                                $"{theme.Name}: a tag is {onChip:0.00} against its own chip");

                    // The chip is a hint, not a highlighter: visible against the
                    // box, and nowhere near as strong as the ink on it.
                    double chipOnBox = Contrast(chip, box);
                    Assert.True(chipOnBox > 1.02, $"{theme.Name}: the chip is invisible");
                    Assert.True(chipOnBox < 2.0,
                                $"{theme.Name}: the chip is {chipOnBox:0.00} against the box - "
                                + "that is a highlighter, not a hint");
                }

                // The control, and the reason this changed: the grey these
                // replaced cannot clear the bar on either polarity.
                var grey = Color.FromRgb(0x88, 0x88, 0x88);
                double onWhite = Contrast(grey, Color.FromRgb(0xFF, 0xFF, 0xFF));
                double onDark = Contrast(grey, Color.FromRgb(0x33, 0x33, 0x38));
                _out.WriteLine($"   the old grey: {onWhite:0.00} on white, {onDark:0.00} on dark");
                Assert.True(onWhite < 4.5 && onDark < 4.5,
                            "the flat grey passes after all - this test proves nothing");
            }
            finally
            {
                // Somebody is running this on their own machine.
                SMSModForge.Services.ThemeManager.Apply(was);
                WindowHarness.Pump();
            }
        });
    }

    [Fact]
    public void TheTagTakesTheThemesMarkupColourAndNotAGrey()
    {
        // The wiring. The brushes above could be perfect and the box could
        // still be painting its tags with something else entirely - which is
        // exactly what it was doing, looking up a Theme.Muted that no theme
        // has ever defined and falling back to a hard-coded grey every time.
        WindowHarness.Run(_ =>
        {
            var box = Box("plain <b>bold</b> plain");
            var tag = Runs(box).Single(r => r.Text == "<b>");

            var ink = ((SolidColorBrush)tag.Foreground).Color;
            var chip = ((SolidColorBrush)tag.Background).Color;
            _out.WriteLine($"tag ink {ink} on chip {chip}");

            Assert.Equal(Of(Application.Current.Resources[
                                SMSModForge.Services.ThemeManager.KeyMarkup]), ink);
            Assert.Equal(Of(Application.Current.Resources[
                                SMSModForge.Services.ThemeManager.KeyMarkupBack]), chip);
            Assert.True(ink.R != ink.G || ink.G != ink.B,
                        "the tag is still a grey - it has no colour of its own");
        });
    }

    [Fact]
    public void StackedTagsBothApply()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("<b><i>both</i></b>");
            var inside = Runs(box).Single(r => r.Text == "both");

            _out.WriteLine($"weight={inside.FontWeight} style={inside.FontStyle}");
            Assert.Equal(FontWeights.Bold, inside.FontWeight);
            Assert.Equal(FontStyles.Italic, inside.FontStyle);
        });
    }

    [Fact]
    public void SizeAndColourReachTheRun()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("<size=50%><color=#f66>small and red</color></size>");
            var inside = Runs(box).Single(r => r.Text == "small and red");

            _out.WriteLine($"size={inside.FontSize:0.#} of {box.FontSize:0.#}, fg={inside.Foreground}");
            Assert.True(inside.FontSize < box.FontSize,
                        "a half-size tag did not make the text smaller");
            Assert.Equal(Color.FromRgb(0xFF, 0x66, 0x66),
                         ((SolidColorBrush)inside.Foreground).Color);
        });
    }

    [Fact]
    public void AnUnknownTagIsShownAsTheTextItWillBe()
    {
        // The control for the whole feature: a tag the game does not act on
        // must not be dressed up as one it does. It is printed at the player,
        // so it is shown as words.
        WindowHarness.Run(_ =>
        {
            var box = Box("<bold>not a tag</bold>");
            var runs = Runs(box);

            _out.WriteLine(string.Join(" | ", runs.Select(r => $"'{r.Text}'")));
            Assert.Single(runs);
            Assert.Equal("<bold>not a tag</bold>", runs[0].Text);
            Assert.NotEqual(FontWeights.Bold, runs[0].FontWeight);
        });
    }

    [Fact]
    public void APlainLineIsLeftAsOneOrdinaryRun()
    {
        // The common case by far, and the one that must stay cheap: no markup,
        // nothing to style, one run and no local formatting on it.
        WindowHarness.Run(_ =>
        {
            var box = Box("She looked at me and said nothing.");
            var runs = Runs(box);

            Assert.Single(runs);
            Assert.Null(runs[0].ReadLocalValue(TextElement.ForegroundProperty) as Brush);
            Assert.Equal(FontWeights.Normal, runs[0].FontWeight);
            Assert.Equal(FontStyles.Normal, runs[0].FontStyle);
        });
    }

    [Fact]
    public void AnUnreadableColourLeavesTheTextAlone()
    {
        // Better than painting a guess: the author sees it is not taking, which
        // is the truth.
        WindowHarness.Run(_ =>
        {
            var box = Box("<color=nonsense>hm</color>");
            var inside = Runs(box).Single(r => r.Text == "hm");
            Assert.Null(inside.ReadLocalValue(TextElement.ForegroundProperty) as Brush);
        });
    }

    [Theory]
    [InlineData("#f66", 0xFF, 0x66, 0x66)]
    [InlineData("#ff6666", 0xFF, 0x66, 0x66)]
    [InlineData("red", 0xFF, 0x00, 0x00)]
    [InlineData("#00ff00ff", 0x00, 0xFF, 0x00)]   // TMP writes alpha LAST
    public void ColoursAreReadTheWayTheGameWritesThem(string value, byte r, byte g, byte b)
    {
        var brush = (SolidColorBrush?)MarkupTextBox.BrushFor(value);
        Assert.NotNull(brush);
        _out.WriteLine($"{value} -> {brush!.Color}");
        Assert.Equal(Color.FromRgb(r, g, b),
                     Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B));
    }

    // ── The node list: the line as the player reads it ─────────────────

    private static MarkupTextBox Preview(string text)
    {
        var box = new MarkupTextBox { Width = 300, HidesTags = true, Text = text };
        var window = new Window
        {
            Width = 400, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, Content = box,
        };
        window.Show();
        WindowHarness.Pump();
        return box;
    }

    private static string Shown(MarkupTextBox box)
        => string.Concat(Runs(box).Select(r => r.Text));

    [Fact]
    public void ThePreviewDropsTheTagsAndKeepsTheirFormatting()
    {
        WindowHarness.Run(_ =>
        {
            var box = Preview("say <b>this</b> now");
            _out.WriteLine($"shown: '{Shown(box)}'");

            Assert.Equal("say this now", Shown(box));
            Assert.Equal(FontWeights.Bold, Runs(box).Single(r => r.Text == "this").FontWeight);
        });
    }

    [Fact]
    public void ThePreviewStillHoldsTheWholeLine()
    {
        // The hazard this mode brings, and the reason it is display-only: the
        // document is now SHORTER than the line. If the control wrote what it
        // is showing back to Text, the markup would be gone from the pack.
        WindowHarness.Run(_ =>
        {
            const string line = "say <b>this</b> now";
            var box = Preview(line);

            Assert.Equal("say this now", Shown(box));
            Assert.Equal(line, box.Text);

            // ...and it stays that way after the document is rebuilt.
            box.Text = line + "!";
            WindowHarness.Pump();
            Assert.Equal(line + "!", box.Text);
            Assert.Equal("say this now!", Shown(box));
        });
    }

    [Fact]
    public void EditingThePreviewsDocumentCannotEatTheMarkup()
    {
        // The one that matters, and the one a control has to earn: in this mode
        // the document is SHORTER than the line, so a document change taken as
        // the line would write "say this now" over "say <b>this</b> now" and
        // lose the markup out of the pack.
        //
        // The row is inert, so nothing reaches this by hand - which is exactly
        // why it is worth a test. An inert control is one property setting away
        // from not being inert.
        WindowHarness.Run(_ =>
        {
            const string line = "say <b>this</b> now";
            var box = Preview(line);

            // What a keystroke does, if one ever got here.
            box.CaretPosition = box.Document.ContentEnd;
            box.CaretPosition.InsertTextInRun("!");
            WindowHarness.Pump();

            _out.WriteLine($"after a document edit, Text is '{box.Text}'");
            Assert.Equal(line, box.Text);
        });
    }

    [Fact]
    public void AnUnknownTagIsStillShownInThePreview()
    {
        // It is not markup, so it is not hidden: the player will read those
        // characters, and a row that hid them would be the one place in the
        // editor that lies about it.
        WindowHarness.Run(_ =>
        {
            var box = Preview("<bold>kept</bold>");
            _out.WriteLine($"shown: '{Shown(box)}'");
            Assert.Equal("<bold>kept</bold>", Shown(box));
        });
    }

    [Fact]
    public void ATokenIsMarkedInTheEditor()
    {
        WindowHarness.Run(_ =>
        {
            var box = Box("Morning, {PC}.");
            var runs = Runs(box);
            foreach (var r in runs)
                _out.WriteLine($"   '{r.Text}' fg={r.Foreground} bg={r.Background}");

            var token = runs.Single(r => r.Text == "{PC}");
            var words = runs.First(r => r.Text.StartsWith("Morning"));

            Assert.NotNull(token.Foreground);
            Assert.NotNull(token.Background);
            Assert.Null(words.ReadLocalValue(TextElement.ForegroundProperty) as Brush);
        });
    }

    [Fact]
    public void ATokenStaysOnTheRowWhereTheTagsAreDropped()
    {
        // Both halves matter. The tag goes, because the player never sees it
        // and it costs room in a narrow row; the token STAYS, because the
        // player does see something there - and it stays marked, because what
        // they see is not the four characters written down.
        WindowHarness.Run(_ =>
        {
            var box = Preview("<b>Morning, {PC}.</b>");
            _out.WriteLine($"shown: '{Shown(box)}'");

            Assert.Equal("Morning, {PC}.", Shown(box));

            var token = Runs(box).Single(r => r.Text == "{PC}");
            Assert.NotNull(token.Background);

            // ...and the style around it still applies, because the name the
            // game puts there will be bold along with the sentence.
            Assert.Equal(FontWeights.Bold, token.FontWeight);
        });
    }

    [Fact]
    public void AWordThatIsNotATokenIsLeftAlone()
    {
        // The control for both of the above: the marking has to be able to NOT
        // happen, or "it is marked" means nothing.
        WindowHarness.Run(_ =>
        {
            var box = Box("Morning, {PCC}.");
            var runs = Runs(box);

            _out.WriteLine(string.Join(" | ", runs.Select(r => $"'{r.Text}'")));
            Assert.Single(runs);
            Assert.Null(runs[0].ReadLocalValue(TextElement.BackgroundProperty) as Brush);
        });
    }

    [Fact]
    public void TheEditorStillGreysTagsRatherThanHidingThem()
    {
        // The control for the mode. Two surfaces, two jobs: the list is for
        // reading a conversation, the editor is for writing one - and you
        // cannot edit a tag you cannot see.
        WindowHarness.Run(_ =>
        {
            var editing = Box("say <b>this</b> now");
            Assert.Equal("say <b>this</b> now", string.Concat(Runs(editing).Select(r => r.Text)));
            Assert.Contains(Runs(editing), r => r.Text == "<b>");
        });
    }
}
