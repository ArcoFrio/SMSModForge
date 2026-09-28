using System;
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
/// A dialogue line drawn the way the game draws it.
/// <para/>
/// Every number behind this was read off the live TMP component in a running
/// game and put in <see cref="DialogueLook"/>. What is left to check is that
/// they are actually being used — a preview that silently drew nothing, or drew
/// in the wrong font, would look like a feature that had not been switched on.
/// <para/>
/// So these measure PIXELS. Asserting that the control was handed the right
/// font name would pass just as well against a control that never draws.
/// </summary>
[Trait("Speed", "Slow")]   // builds a real window; see CLAUDE.md
public sealed class DialogueLinePreviewTests
{
    private readonly ITestOutputHelper _out;
    public DialogueLinePreviewTests(ITestOutputHelper o) => _out = o;

    private const int Width = 420;

    private static DialogueLinePreview Shown(string line, string speaker = "",
                                             string colour = "", bool spelling = false)
    {
        // Pinned to the top left so the control sits at its parent's origin.
        // RenderTargetBitmap.Render draws a visual WITH its offset inside its
        // parent, so a centred child comes back shifted - which reads as an
        // unpainted strip down the left of the capture and has nothing to do
        // with what the control drew.
        var preview = new DialogueLinePreview
        {
            Width = Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Line = line,
            Speaker = speaker,
            SpeakerColor = colour,
            ChecksSpelling = spelling,
        };
        var window = new Window
        {
            Width = 500, Height = 200, Left = -10000, Top = -10000,
            ShowInTaskbar = false, Content = preview,
        };
        window.Show();
        window.UpdateLayout();
        WindowHarness.Pump();
        // Twice: a row's spelling marks are worked out after it is first
        // drawn, and it is drawn again with them - see CheckSoon.
        window.UpdateLayout();
        WindowHarness.Pump();
        return preview;
    }

    /// <summary>What the control actually drew, as premultiplied BGRA.</summary>
    private static byte[]? Pixels(DialogueLinePreview preview, out int width, out int height)
    {
        width = height = 0;
        var render = new RenderTargetBitmap(
            Math.Max(1, (int)preview.ActualWidth), Math.Max(1, (int)preview.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        render.Render(preview);
        width = render.PixelWidth;
        height = render.PixelHeight;
        if (width <= 0 || height <= 0) return null;

        var buffer = new byte[width * height * 4];
        render.CopyPixels(buffer, width * 4, 0);
        return buffer;
    }

    /// <summary>How many pixels are not the dark panel — i.e. how much text
    /// landed.</summary>
    private static int Lit(byte[] pixels)
    {
        var panel = (Color)ColorConverter.ConvertFromString(DialogueLook.PanelHex)!;
        int lit = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            // Anything meaningfully brighter than the panel is ink.
            if (Math.Abs(pixels[i] - panel.B) > 24
                || Math.Abs(pixels[i + 1] - panel.G) > 24
                || Math.Abs(pixels[i + 2] - panel.R) > 24) lit++;
        }
        return lit;
    }

    /// <summary>
    /// Whether there is anything to test against, and NOT a way for a wrong
    /// font name to make the whole class pass quietly.
    /// <para/>
    /// Two different situations were one condition here, and that was a bug in
    /// the tests: a machine with no art extraction has nothing to draw with and
    /// should skip, but a machine that HAS the extraction and cannot find
    /// <see cref="DialogueLook.FontName"/> has a wrong font name, which is the
    /// single most important thing this file exists to catch. Changing the name
    /// to nonsense used to turn all six tests green.
    /// </summary>
    private static bool Available
    {
        get
        {
            if (!VanillaUiLibrary.IsAvailable) return false;

            var set = VanillaUiLibrary.Assets.Font(DialogueLook.FontName);
            Assert.True(set?.Font != null,
                        $"the art extraction is here but '{DialogueLook.FontName}' is not one of "
                        + $"its {VanillaUiLibrary.FontNames.Count()} fonts — the game's dialogue "
                        + "would be drawn in nothing at all");
            return true;
        }
    }

    [Fact]
    public void TheLineIsActuallyDrawn()
    {
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var preview = Shown("Finally. There you are.");
            var pixels = Pixels(preview, out int w, out int h);
            Assert.NotNull(pixels);

            int lit = Lit(pixels!);
            _out.WriteLine($"{w}x{h}, {lit} pixels of text");
            Assert.True(lit > 100, "the panel is there but nothing was written on it");
        });
    }

    [Fact]
    public void AnEmptyLineDrawsNoText()
    {
        // The control for the one above: the counter has to be able to say
        // "nothing", or "something was drawn" means nothing either.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var preview = Shown("");
            var pixels = Pixels(preview, out int w, out int h);
            int lit = pixels == null ? 0 : Lit(pixels);

            _out.WriteLine($"empty line: {w}x{h}, {lit} of {w * h} pixels not the panel");
            Assert.True(lit < 100, $"an empty line drew something: {lit} lit in {w}x{h}");
        });
    }

    [Fact]
    public void MarkupIsAppliedAndNotDrawn()
    {
        // The player never sees the tags, so the preview must not either - and
        // the check is that the two render the SAME, not merely that one of
        // them renders.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var plain = Pixels(Shown("say hello now"), out _, out _);
            var tagged = Pixels(Shown("say <color=#ffffff>hello</color> now"), out _, out _);
            Assert.NotNull(plain);
            Assert.NotNull(tagged);

            int a = Lit(plain!), b = Lit(tagged!);
            _out.WriteLine($"plain {a} lit, with markup {b} lit");

            // Same words, same white, so the same ink. A few pixels of slack for
            // the outline's antialiasing landing differently is not the point;
            // drawing "<color=#ffffff>" would be hundreds more.
            Assert.True(Math.Abs(a - b) < a * 0.05,
                        $"the markup looks like it was drawn: {a} vs {b}");
        });
    }

    [Fact]
    public void TheSpeakersNameIsDrawnInTheirColour()
    {
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var preview = Shown("a line", speaker: "Adrian", colour: "#FF0000");
            var pixels = Pixels(preview, out int w, out int h);
            Assert.NotNull(pixels);

            // Red ink, somewhere: the name, in the colour the pack gave them.
            int red = 0;
            for (int i = 0; i + 3 < pixels!.Length; i += 4)
                if (pixels[i + 2] > 140 && pixels[i + 1] < 90 && pixels[i] < 90) red++;

            _out.WriteLine($"{w}x{h}, {red} red pixel(s)");
            Assert.True(red > 20, "the speaker's colour never reached the name");
        });
    }

    [Fact]
    public void TheNameAndTheLineShareOneRow()
    {
        // One line, speaker then words - the way the row this replaces has
        // always read. They go into ONE buffer, so getting this wrong means
        // either overlapping them into a smear or stacking them, and the row is
        // capped so its height cannot be what tells the difference.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var pixels = Pixels(Shown("the spoken line", speaker: "Adrian", colour: "#FF0000"),
                                out int w, out int h);
            Assert.NotNull(pixels);

            int nameLeft = int.MaxValue, nameRight = -1, nameTop = int.MaxValue, nameBottom = -1;
            int bodyLeft = int.MaxValue, bodyTop = int.MaxValue, bodyBottom = -1;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                byte b = pixels![i], g = pixels[i + 1], r = pixels[i + 2];

                if (r > 140 && g < 90 && b < 90)
                {
                    nameLeft = Math.Min(nameLeft, x); nameRight = Math.Max(nameRight, x);
                    nameTop = Math.Min(nameTop, y); nameBottom = Math.Max(nameBottom, y);
                }
                else if (b > 170 && g > 170 && r > 170)
                {
                    bodyLeft = Math.Min(bodyLeft, x);
                    bodyTop = Math.Min(bodyTop, y); bodyBottom = Math.Max(bodyBottom, y);
                }
            }

            _out.WriteLine($"{w}x{h}: name x{nameLeft}..{nameRight} y{nameTop}..{nameBottom}, "
                           + $"line from x{bodyLeft} y{bodyTop}..{bodyBottom}");

            Assert.True(nameRight > 0, "the name was never drawn");
            Assert.True(bodyTop < int.MaxValue, "the line was never drawn");

            // Side by side: the name finishes before the words start...
            Assert.True(nameRight < bodyLeft,
                        "the words start before the name has finished - they overlap");

            // ...and on the same line, which means their vertical extents meet.
            Assert.True(nameTop <= bodyBottom && bodyTop <= nameBottom,
                        "the name and the line are on separate rows, not one");
        });
    }

    [Fact]
    public void TheTextIsSizedFromTheRowHeight()
    {
        // The control for the cap: a row could satisfy it by drawing at some
        // fixed size and getting lucky. Halve the budget and the words have to
        // get smaller, because the size is solved from it.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var roomy = Shown("the same words here");
            var cramped = Shown("the same words here");
            cramped.RowHeight = DialogueLook.MaxRowHeight / 2;
            cramped.UpdateLayout();
            WindowHarness.Pump();

            int big = Lit(Pixels(roomy, out _, out _)!);
            int small = Lit(Pixels(cramped, out _, out _)!);

            _out.WriteLine($"{DialogueLook.MaxRowHeight:0}px row: {big} lit; "
                           + $"{DialogueLook.MaxRowHeight / 2:0}px row: {small} lit");
            Assert.True(small < big, "halving the row height did not shrink the words");
        });
    }

    [Theory]
    [InlineData("a line", "")]
    [InlineData("a line", "Adrian")]
    [InlineData("<b>short</b> <i>with</i> markup", "Adrian")]
    public void ALineThatFitsTakesOneCappedRow(string line, string speaker)
    {
        // The point of sizing from the row instead of from the width. A list is
        // for scanning a conversation, and a row that grew to fit its content
        // would be a list you can see half as much of. What a long line is
        // allowed to do is take a SECOND row - see below - not to make the
        // first one taller.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var preview = Shown(line, speaker);
            double height = preview.DesiredSize.Height;

            _out.WriteLine($"'{line}' {(speaker.Length > 0 ? "with" : "without")} a name: "
                           + $"{height:0.0}px of {DialogueLook.MaxRowHeight:0}");
            Assert.True(height <= DialogueLook.MaxRowHeight + 1,
                        $"the row is {height:0.0}px, past the {DialogueLook.MaxRowHeight:0}px cap");
        });
    }

    private const string LongLine =
        "A much, much longer line than this column can hold, which used to be cut "
        + "off at the edge and taken on trust by whoever wrote it.";

    [Fact]
    public void ALineTooLongForTheColumnWrapsRatherThanBeingCutOff()
    {
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            double one = Shown("a line").DesiredSize.Height;

            var preview = Shown(LongLine);
            double tall = preview.DesiredSize.Height;
            var pixels = Pixels(preview, out int w, out int h);
            Assert.NotNull(pixels);

            // Ink below the first line's worth of height: the row being taller
            // on its own would be satisfied by empty space.
            int below = 0;
            for (int y = (int)Math.Ceiling(one); y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (pixels![i] > 170 && pixels[i + 1] > 170 && pixels[i + 2] > 170) below++;
            }

            _out.WriteLine($"one line {one:0.0}px, the long one {tall:0.0}px ({w}x{h}), "
                           + $"{below} lit pixel(s) past the first line");
            Assert.True(tall > one + 1, "a line too long for the column did not take a second row");
            Assert.True(below > 50, "the row grew but the rest of the line was not written in it");
        });
    }

    [Fact]
    public void ANarrowerColumnTakesMoreRows()
    {
        // The control for wrapping, and the one that cannot pass by accident:
        // with no wrapping at all the same words are the same one line however
        // narrow the column gets, and both heights come back equal.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var wide = Shown(LongLine);
            var narrow = Shown(LongLine);
            narrow.Width = Width / 2.0;
            narrow.UpdateLayout();
            WindowHarness.Pump();

            double a = wide.DesiredSize.Height, b = narrow.DesiredSize.Height;
            _out.WriteLine($"{Width}px column: {a:0.0}px tall; {Width / 2}px column: {b:0.0}px");
            Assert.True(b > a, "halving the column did not cost the line another row");
        });
    }

    [Fact]
    public void TheWordsStayInsideTheColumn()
    {
        // Wrapping that overshoots is worse than not wrapping: the row would be
        // two lines AND still be cut off at the edge.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var pixels = Pixels(Shown(LongLine, speaker: "Adrian"), out int w, out int h);
            Assert.NotNull(pixels);

            int rightmost = -1;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                if (Lit(pixels!, i)) rightmost = Math.Max(rightmost, x);
            }

            _out.WriteLine($"{w}x{h}: the last ink is at x{rightmost}");
            Assert.True(rightmost > 0, "nothing was drawn at all");
            Assert.True(rightmost < w - 1, $"the line still runs to the edge at x{rightmost} of {w}");
        });
    }

    [Fact]
    public void TheTextStartsInFromTheEdge()
    {
        // Whatever is drawn first - the speaker, the words, or the "(no text)"
        // an empty node shows - starts a little way in. Flush against the panel
        // edge reads as clipped even when the line is whole.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            foreach (var (line, speaker) in new[]
                     {
                         ("Finally. There you are.", ""),
                         ("Finally. There you are.", "Adrian"),
                         ("(no text)", ""),
                     })
            {
                int left = Leftmost(Shown(line, speaker));
                _out.WriteLine($"'{line}'{(speaker.Length > 0 ? $" [{speaker}]" : "")}: "
                               + $"first ink at x{left}");
                Assert.True(left >= 3, $"the text starts at x{left}, hard against the edge");
            }
        });
    }

    [Fact]
    public void TheInsetIsProportionalToTheText()
    {
        // The control for the one above. An inset that happened to be there
        // because of a glyph's own left bearing would not grow with the text;
        // this one is a share of the size, so a taller row indents further.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var small = Shown("Finally.");
            var large = Shown("Finally.");
            large.RowHeight = DialogueLook.MaxRowHeight * 3;
            large.UpdateLayout();
            WindowHarness.Pump();

            int a = Leftmost(small), b = Leftmost(large);
            _out.WriteLine($"{DialogueLook.MaxRowHeight:0}px row starts at x{a}; "
                           + $"{DialogueLook.MaxRowHeight * 3:0}px row starts at x{b}");
            Assert.True(b > a + 2,
                        $"the inset did not grow with the text: x{a} against x{b}");
        });
    }

    // ── What the markup does to it ───────────────────────────────────

    /// <summary>How many pixels are near a colour, ignoring the antialiased
    /// fringe by asking for a close match rather than an exact one.</summary>
    private static int Near(byte[] pixels, string hex, int tolerance = 28)
    {
        var want = (Color)ColorConverter.ConvertFromString(hex)!;
        int hits = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] - want.B) <= tolerance
                && Math.Abs(pixels[i + 1] - want.G) <= tolerance
                && Math.Abs(pixels[i + 2] - want.R) <= tolerance) hits++;
        return hits;
    }

    [Fact]
    public void ASizeTagChangesHowBigTheWordsAre()
    {
        // The tag is not drawn - see MarkupIsAppliedAndNotDrawn - so the only
        // way to see it took is the words themselves.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            double plain = Shown("the same words").DesiredSize.Height;
            double small = Shown("<size=50%>the same words").DesiredSize.Height;
            double large = Shown("<size=200%>the same words").DesiredSize.Height;

            _out.WriteLine($"row height: {small:0.0}px at half, {plain:0.0}px plain, "
                           + $"{large:0.0}px at double");
            Assert.True(large > plain, "a double-size tag did not make the line any bigger");
            Assert.True(small < plain, "a half-size tag did not make it any smaller");
        });
    }

    [Fact]
    public void AColourTagPaintsTheWordsItWraps()
    {
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var tagged = Pixels(Shown("say <color=#FF0000>this</color> now"), out _, out _);
            var plain = Pixels(Shown("say this now"), out _, out _);
            Assert.NotNull(tagged);
            Assert.NotNull(plain);

            int red = Red(tagged!), none = Red(plain!);
            _out.WriteLine($"{red} red pixel(s) with the tag, {none} without it");
            Assert.True(red > 20, "the colour tag never reached the words");
            Assert.True(none < 5, "the same line is red WITHOUT the tag - so this proves nothing");
        });
    }

    private static int Red(byte[] pixels)
    {
        int red = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
            if (pixels[i + 2] > 140 && pixels[i + 1] < 90 && pixels[i] < 90) red++;
        return red;
    }

    [Fact]
    public void BoldIsDrawnHeavierThanPlain()
    {
        // TextMeshPro has no bold atlas and neither does this: the same glyphs
        // are drawn at the heavier of the two weights the material carries. So
        // the words do not move, they thicken - which is ink, and countable.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            int plain = Lit(Pixels(Shown("the same words here"), out _, out _)!);
            int bold = Lit(Pixels(Shown("<b>the same words here</b>"), out _, out _)!);

            _out.WriteLine($"{plain} lit plain, {bold} lit in bold");
            Assert.True(bold > plain * 1.02, $"bold drew {bold} against a plain {plain}");
        });
    }

    [Fact]
    public void ItalicLeansTheWords()
    {
        // Measured as a lean rather than as "different pixels": a shear moves
        // the top of a letter right of its foot, and leaves the foot alone.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            double Lean(string line)
            {
                var pixels = Pixels(Shown(line), out int w, out int h);
                Assert.NotNull(pixels);

                // The FACE of the glyph, not its outline or its shadow: those
                // spill below the baseline, where a shear leans the other way,
                // and mixing the two measures nothing.
                bool Face(int y, int x)
                {
                    int i = (y * w + x) * 4;
                    return pixels![i] > 170 && pixels[i + 1] > 170 && pixels[i + 2] > 170;
                }

                int first = -1, last = -1;
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Face(y, x)) { if (first < 0) first = y; last = y; break; }

                Assert.True(last > first, "the row has no ink to measure");

                // The average x of the ink across the top fifth of the letters
                // against the bottom fifth of them.
                double band = (last - first) / 5.0;
                double topSum = 0, topN = 0, footSum = 0, footN = 0;
                for (int y = first; y <= last; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!Face(y, x)) continue;
                    if (y <= first + band) { topSum += x; topN++; }
                    else if (y >= last - band) { footSum += x; footN++; }
                }
                Assert.True(topN > 0 && footN > 0, "the letters are too short to measure");
                return topSum / topN - footSum / footN;
            }

            double upright = Lean("HHHH HHHH");
            double leaning = Lean("<i>HHHH HHHH</i>");

            _out.WriteLine($"top-against-foot: {upright:0.0}px upright, {leaning:0.0}px italic");
            Assert.True(leaning > upright + 1.0,
                        $"the italic line leans {leaning:0.0} against an upright {upright:0.0}");
        });
    }

    // ── The game's own tokens ────────────────────────────────────────

    [Fact]
    public void ATokenIsMarkedOnTheRow()
    {
        // The one thing a row cannot say by drawing the line the way the player
        // will read it: the player sees their own name here, not "{PC}".
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var withToken = Pixels(Shown("Morning, {PC}."), out _, out _);
            var without = Pixels(Shown("Morning, Alex."), out _, out _);
            Assert.NotNull(withToken);
            Assert.NotNull(without);

            int marked = Near(withToken!, SMSModForge.Rendering.DialogueLook.TokenHex);
            int plain = Near(without!, SMSModForge.Rendering.DialogueLook.TokenHex);

            _out.WriteLine($"{marked} token-coloured pixel(s) with {{PC}}, {plain} without");
            Assert.True(marked > 20, "the token was drawn the same as the words around it");
            Assert.True(plain < 5, "an ordinary line is token-coloured too - so this says nothing");
        });
    }

    [Fact]
    public void ATokenSitsOnAChipOfItsOwn()
    {
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var pixels = Pixels(Shown("Morning, {PC}."), out _, out _);
            Assert.NotNull(pixels);

            int chip = Near(pixels!, SMSModForge.Rendering.DialogueLook.TokenChipHex, 10);
            _out.WriteLine($"{chip} pixel(s) of chip behind the token");
            Assert.True(chip > 40, "there is no chip behind the token");
        });
    }

    [Fact]
    public void AWordThatIsNotATokenIsDrawnAsWords()
    {
        // The control. A brace pair the game does not resolve is four
        // characters the player will read, and marking it would be the editor
        // promising something nobody checked.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var pixels = Pixels(Shown("Morning, {PCC}."), out _, out _);
            Assert.NotNull(pixels);

            int marked = Near(pixels!, SMSModForge.Rendering.DialogueLook.TokenHex);
            _out.WriteLine($"{marked} token-coloured pixel(s) for {{PCC}}");
            Assert.True(marked < 5, "a brace pair nobody has confirmed was dressed up as a token");
        });
    }

    /// <summary>The first column holding any ink.</summary>
    private static int Leftmost(DialogueLinePreview preview)
    {
        var pixels = Pixels(preview, out int w, out int h);
        Assert.NotNull(pixels);

        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            if (Lit(pixels!, (y * w + x) * 4)) return x;

        throw new Xunit.Sdk.XunitException("nothing was drawn at all");
    }

    /// <summary>Whether the pixel at <paramref name="i"/> is ink rather than
    /// panel.</summary>
    private static bool Lit(byte[] pixels, int i)
    {
        var panel = (Color)ColorConverter.ConvertFromString(DialogueLook.PanelHex)!;
        return Math.Abs(pixels[i] - panel.B) > 24
               || Math.Abs(pixels[i + 1] - panel.G) > 24
               || Math.Abs(pixels[i + 2] - panel.R) > 24;
    }

    [Fact]
    public void ItLooksTheSameWhateverTheEditorsThemeIs()
    {
        // The whole point: the game has no light mode, so neither does this.
        // Nothing here reads a theme brush, and this is what says so.
        WindowHarness.Run(host =>
        {
            if (!Available) { _out.WriteLine("no font extraction - skipping"); return; }

            var themes = SMSModForge.Services.ThemeManager.All;
            var was = SMSModForge.Services.ThemeManager.Current;
            Assert.True(themes.Count > 1, "there is only one theme to compare");

            try
            {
                int? first = null;
                foreach (var theme in themes)
                {
                    SMSModForge.Services.ThemeManager.Apply(theme);
                    WindowHarness.Pump();

                    var drawn = Pixels(Shown("Finally. There you are."), out _, out _);
                    Assert.NotNull(drawn);
                    int lit = Lit(drawn!);
                    _out.WriteLine($"   {theme.Name,-12} {lit} lit");

                    first ??= lit;
                    Assert.Equal(first.Value, lit);
                }
            }
            finally
            {
                // Somebody is running this on their own machine.
                SMSModForge.Services.ThemeManager.Apply(was);
                WindowHarness.Pump();
            }
        });
    }

    // ── Spelling ───────────────────────────────────────────────────

    /// <summary>
    /// Where the red marks are, as a fraction across the row: 0 is the left
    /// edge, 1 the right. Measured off the drawn pixels rather than off the
    /// layout, because whether the mark lands under the word it belongs to is
    /// the whole question.
    /// </summary>
    private static (int Count, double Left, double Right) Marks(byte[] pixels, int width, int height)
    {
        var red = (Color)ColorConverter.ConvertFromString(DialogueLook.SpellingHex)!;
        int count = 0, min = int.MaxValue, max = int.MinValue;

        // Tight, because a <color> tag can paint the WORDS a red of its own and
        // the pixels where one of those meets the panel pass through shades on
        // the way down. A loose match counted two of those and called it a
        // spelling mark.
        const int Tolerance = 24;

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            if (Math.Abs(pixels[i] - red.B) > Tolerance) continue;
            if (Math.Abs(pixels[i + 1] - red.G) > Tolerance) continue;
            if (Math.Abs(pixels[i + 2] - red.R) > Tolerance) continue;
            count++;
            if (x < min) min = x;
            if (x > max) max = x;
        }
        return count == 0 ? (0, 0, 0) : (count, (double)min / width, (double)max / width);
    }

    [Fact]
    public void AMisspelledWordIsUnderlined()
    {
        if (!Available) return;
        WindowHarness.Run(_ =>
        {
            var drawn = Pixels(Shown("I finaly got here.", spelling: true), out int w, out int h);
            Assert.NotNull(drawn);
            var marks = Marks(drawn!, w, h);
            _out.WriteLine($"{marks.Count} red pixel(s), from {marks.Left:P0} to {marks.Right:P0} across");
            Assert.True(marks.Count > 0, "nothing was underlined");
        });
    }

    [Fact]
    public void ItIsUnderTheWrongWordAndNotTheWholeLine()
    {
        // The measurement that matters. Glyphs are dropped at a wrap and where
        // the atlas has no character, so counting glyphs would drift the mark
        // off the word — this is what says it did not.
        if (!Available) return;
        WindowHarness.Run(_ =>
        {
            var first = Pixels(Shown("finaly is a long correct sentence here", spelling: true),
                               out int w1, out int h1);
            var last = Pixels(Shown("this is a long correct sentence finaly", spelling: true),
                              out int w2, out int h2);
            Assert.NotNull(first);
            Assert.NotNull(last);

            var early = Marks(first!, w1, h1);
            var late = Marks(last!, w2, h2);
            _out.WriteLine($"word first: {early.Left:P0}..{early.Right:P0}");
            _out.WriteLine($"word last:  {late.Left:P0}..{late.Right:P0}");

            Assert.True(early.Count > 0 && late.Count > 0, "one of the lines was not marked");
            // The same misspelling, at opposite ends of the same sentence.
            Assert.True(early.Right < late.Left,
                        "the mark did not move with the word it belongs to");
        });
    }

    [Fact]
    public void ACorrectLineIsLeftClean()
    {
        // The control. Without it, a mark drawn under every row would pass the
        // test above.
        if (!Available) return;
        WindowHarness.Run(_ =>
        {
            var drawn = Pixels(Shown("I got here.", spelling: true), out int w, out int h);
            Assert.NotNull(drawn);
            var marks = Marks(drawn!, w, h);
            _out.WriteLine($"correct line: {marks.Count} red pixel(s)");
            Assert.Equal(0, marks.Count);
        });
    }

    [Fact]
    public void AndNothingIsMarkedWhenTheOptionIsOff()
    {
        // The other control: the same misspelling, checking switched off.
        if (!Available) return;
        WindowHarness.Run(_ =>
        {
            var drawn = Pixels(Shown("I finaly got here.", spelling: false), out int w, out int h);
            Assert.NotNull(drawn);
            var marks = Marks(drawn!, w, h);
            _out.WriteLine($"option off: {marks.Count} red pixel(s)");
            Assert.Equal(0, marks.Count);
        });
    }

    [Fact]
    public void TagsAreNotHandedToTheSpeller()
    {
        // The row shows the line with its markup applied and the tags gone, so
        // that is what gets checked. Handing over the raw line would report
        // "color" and "FF6666" as misspellings of nothing the author can fix.
        if (!Available) return;
        WindowHarness.Run(_ =>
        {
            var drawn = Pixels(Shown("I <color=#FF6666>got</color> here.", spelling: true),
                               out int w, out int h);
            Assert.NotNull(drawn);
            var marks = Marks(drawn!, w, h);
            _out.WriteLine($"with markup: {marks.Count} red pixel(s)");
            Assert.Equal(0, marks.Count);
        });
    }
}
