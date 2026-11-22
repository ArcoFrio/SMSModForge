using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The Dialogues list's rows draw their line in a text block since 1.10.0 (MarkupTextBlock: a tenth of the rich
/// text box's cost). Held to the box they replace and to the speller:
/// <list type="bullet">
/// <item>the same words in the same runs, each with the same colour, background, weight, slant and size as the
/// box's row mode draws - on lines with tags, colours, bracketed words, cues and a name; control: another line
/// is told apart;</item>
/// <item>a misspelled word is marked - red wavy pixels under it in the picture - once the speller has answered;
/// control: with spelling off, none.</item>
/// </list>
/// </summary>
[Trait("Speed", "Slow")]   // builds real windows; see CLAUDE.md
public sealed class RowTextBlockTests
{
    private readonly ITestOutputHelper _out;
    public RowTextBlockTests(ITestOutputHelper o) => _out = o;

    private static T Shown<T>(T element) where T : FrameworkElement
    {
        var window = new Window { Width = 700, Height = 160, Left = -10000, Top = -10000, ShowInTaskbar = false,
                                  Content = new Border { Background = Brushes.White, Child = element } };
        window.Show();
        WindowHarness.Pump();
        return element;
    }

    private static string Describe(IEnumerable<Run> runs)
        => string.Join(" | ", runs.Select(r =>
            $"'{r.Text}' {(r.Foreground as SolidColorBrush)?.Color} on {(r.Background as SolidColorBrush)?.Color} {r.FontWeight} {r.FontStyle} {r.FontSize:0.#} {(r.TextDecorations?.Count > 0 ? "underlined" : "")}"));

    [Theory]
    [InlineData("Well, <b>that</b> went better than <i>I</i> expected.")]
    [InlineData("Shall we try the <color=#F08080>other</color> one? (She laughs.)")]
    [InlineData("<size=70%>quietly</size> said {PC}, and <color=#33FF33>{name:anna}</color> nodded.")]
    [InlineData("Line one\nline two *plap* after")]
    public void TheRowShowsWhatTheBoxsRowModeShowed(string line)
    {
        WindowHarness.Run(_ =>
        {
            // The same text colour for both: what each takes it from (the box its style, the row the list) is the
            // theme's text in the editor's window - NodeRowDrawsOnceTests holds the row to that there.
            var ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
            var box = Shown(new MarkupTextBox { HidesTags = true, ReadableColours = true, Width = 600, FontSize = 13, Foreground = ink, Text = line });
            var block = Shown(new MarkupTextBlock { Width = 600, FontSize = 13, Foreground = ink, Line = line });
            string fromBox = Describe(box.LineRuns), fromBlock = Describe(block.LineRuns);
            _out.WriteLine("box:   " + fromBox);
            _out.WriteLine("block: " + fromBlock);
            Assert.Equal(fromBox, fromBlock);
            Assert.True(block.LineRuns.Any(), "nothing was drawn, so this proves nothing");

            // Control: another line is told apart.
            var other = Shown(new MarkupTextBlock { Width = 600, FontSize = 13, Foreground = ink, Line = line + " more" });
            Assert.NotEqual(fromBox, Describe(other.LineRuns));
        });
    }

    /// <summary>Red pixels in the band under the text.</summary>
    private static int RedUnder(FrameworkElement element)
    {
        element.UpdateLayout();
        var picture = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight) + 4, 96, 96, PixelFormats.Pbgra32);
        picture.Render(element);
        var px = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyPixels(px, picture.PixelWidth * 4, 0);
        int red = 0;
        for (int i = 0; i + 3 < px.Length; i += 4)
            if (px[i + 2] > 150 && px[i + 1] < 110 && px[i] < 110) red++;
        return red;
    }

    [Fact]
    public void AMisspelledWordIsMarkedOnceTheSpellerHasAnswered()
    {
        WindowHarness.Run(_ =>
        {
            const string line = "This sentance has one wrong word.";
            var bad = SMSModForge.Services.Speller.Check(line);
            if (bad.Count == 0)
            {
                _out.WriteLine("Windows' speller is not answering here - nothing to hold");
                return;
            }
            var marked = Shown(new MarkupTextBlock { Width = 600, FontSize = 14, ChecksSpelling = true, Line = line });
            var plain = Shown(new MarkupTextBlock { Width = 600, FontSize = 14, ChecksSpelling = false, Line = line });
            Assert.True(WindowHarness.Until(() => RedUnder(marked) > 5, TimeSpan.FromSeconds(10)), "the misspelled word was never marked");
            int red = RedUnder(marked), none = RedUnder(plain);
            _out.WriteLine($"red pixels under the words: {red} with spelling checked, {none} without; marked runs: "
                           + string.Join(", ", marked.LineRuns.Where(r => r.TextDecorations?.Count > 0).Select(r => "'" + r.Text + "'")));
            Assert.Equal(0, none);
            var markedWord = Assert.Single(marked.LineRuns, r => ReferenceEquals(r.TextDecorations, MarkupTextBlock.Squiggle));
            Assert.Equal(line.Substring(bad[0].At, bad[0].Length), markedWord.Text);
        });
    }
}
