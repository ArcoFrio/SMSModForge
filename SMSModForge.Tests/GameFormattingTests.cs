using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SMSModForge.Rendering;
using SMSModForge.View.Controls;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What the game does to a line by itself, and the sounds in it (the author,
/// 1.8.0): text in brackets in the game's own blue in the Game look - read
/// from its ParenthesesHighlighter, nesting and an author's colour inside
/// counted the way it counts them - and a sound's pattern marked wherever a
/// line is shown.
/// </summary>
public class GameFormattingTests
{
    private static List<(string Text, bool Bracketed)> Bracketing(string line)
        => DialogueLinePreview.Pieces(line).Where(p => !p.Token).Select(p => (p.Text, p.Bracketed)).ToList();

    [Fact]
    public void TextInBracketsTakesTheGamesColour()
        => Assert.Equal(new[] { ("Hi ", false), ("(softly)", true), (" there", false) }, Bracketing("Hi (softly) there"));

    [Fact]
    public void NestedBracketsAreOneStretchAndAnUnclosedOneRunsToTheEnd()
    {
        Assert.Equal(new[] { ("((a) b)", true), (" c", false) }, Bracketing("((a) b) c"));
        Assert.Equal(new[] { ("x ", false), ("(open to the end", true) }, Bracketing("x (open to the end"));
        Assert.Equal(new[] { ("a) b", false) }, Bracketing("a) b"));   // a lone close is just a character
    }

    [Fact]
    public void AnAuthorsColourInsideTheBracketsWinsOneOutsideDoesNot()
    {
        // Inside: the game's tag wraps the author's, so the author's is innermost.
        Assert.Equal(new[] { ("(", true), ("red", false), (" rest)", true) },
                     Bracketing("(<color=red>red</color> rest)"));
        // Outside: the game's tag is the innermost one.
        Assert.Equal(new[] { ("(all)", true) }, Bracketing("<color=red>(all)</color>"));
    }

    [Fact]
    public void ASoundsPatternIsMarkedAndTheLineIsUnchanged()
    {
        var was = DialogueMarkup.CuePatterns;
        try
        {
            DialogueMarkup.CuePatterns = () => new[] { "*plap*", "*plap plap*" };
            const string line = "Hey <b>*PLAP*</b> and *plap plap* now";
            var spans = DialogueMarkup.Parse(line);
            Assert.Equal(line, string.Concat(spans.Select(s => s.Of(line))));
            var cues = spans.Where(s => s.Style.Cue).Select(s => s.Of(line)).ToList();
            Assert.Equal(new[] { "*PLAP*", "*plap plap*" }, cues);   // any case; the longer pattern wins
            Assert.All(spans.Where(s => s.Style.Cue), s => Assert.True(s.IsWords));   // still words the player reads

            DialogueMarkup.CuePatterns = null;
            Assert.DoesNotContain(DialogueMarkup.Parse(line), s => s.Style.Cue);   // the control
        }
        finally { DialogueMarkup.CuePatterns = was; }
    }
}

/// <summary>The same, as drawn: the colours on the Game look's row.</summary>
[Trait("Speed", "Slow")]   // builds windows on the harness thread
public sealed class GameFormattingDrawnTests
{
    private readonly ITestOutputHelper _out;
    public GameFormattingDrawnTests(ITestOutputHelper o) => _out = o;

    private static int Near(byte[] px, string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        int n = 0;
        for (int i = 0; i + 3 < px.Length; i += 4)
            if (Math.Abs(px[i] - c.B) < 28 && Math.Abs(px[i + 1] - c.G) < 28 && Math.Abs(px[i + 2] - c.R) < 28) n++;
        return n;
    }

    private static byte[] Drawn(string line)
    {
        var preview = new DialogueLinePreview { Width = 420, HorizontalAlignment = HorizontalAlignment.Left,
                                                VerticalAlignment = VerticalAlignment.Top, Line = line };
        var window = new Window { Width = 500, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, Content = preview };
        window.Show();
        window.UpdateLayout();
        WindowHarness.Pump();
        var render = new RenderTargetBitmap(Math.Max(1, (int)preview.ActualWidth), Math.Max(1, (int)preview.ActualHeight),
                                            96, 96, PixelFormats.Pbgra32);
        render.Render(preview);
        var px = new byte[render.PixelWidth * render.PixelHeight * 4];
        render.CopyPixels(px, render.PixelWidth * 4, 0);
        window.Close();
        return px;
    }

    [Fact]
    public void TheBracketsAndTheCueAreDrawnInTheirColours()
    {
        WindowHarness.Run(_ =>
        {
            if (!VanillaUiLibrary.IsAvailable) { _out.WriteLine("no font extraction - skipping"); return; }
            var was = DialogueMarkup.CuePatterns;
            try
            {
                DialogueMarkup.CuePatterns = () => new[] { "*plap*" };
                int plainBlue = Near(Drawn("so softly said"), DialogueLook.BracketHex);
                int bracketBlue = Near(Drawn("so (softly) said"), DialogueLook.BracketHex);
                int plainAmber = Near(Drawn("so softly said"), DialogueLook.CueHex);
                int cueAmber = Near(Drawn("so *plap* said"), DialogueLook.CueHex);
                _out.WriteLine($"bracket blue {plainBlue} -> {bracketBlue}; cue amber {plainAmber} -> {cueAmber}");
                Assert.True(bracketBlue > plainBlue + 20, "no bracket blue was drawn");
                Assert.True(cueAmber > plainAmber + 6, "no cue mark was drawn");
            }
            finally { DialogueMarkup.CuePatterns = was; }
        });
    }
}
