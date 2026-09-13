using System.Linq;
using SMSModForge.Rendering;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Reading a dialogue line the way the game's renderer does.
/// <para/>
/// The editor showed a line flat, so a tag that would work looked exactly like
/// one the player would be shown verbatim. This is what lets the text box grey
/// the markup and style what it wraps.
/// <para/>
/// The rule the whole thing rests on, and the first test below: every character
/// of the line lands in exactly one span, in order. The spans drive an editable
/// control, so a parse that dropped or duplicated a character would quietly
/// corrupt somebody's script as they typed in it.
/// </summary>
public sealed class DialogueMarkupTests
{
    private readonly ITestOutputHelper _out;
    public DialogueMarkupTests(ITestOutputHelper o) => _out = o;

    private static string Rebuilt(string text)
        => string.Concat(DialogueMarkup.Parse(text).Select(s => s.Of(text)));

    [Theory]
    [InlineData("")]
    [InlineData("Just a plain line.")]
    [InlineData("<b>bold</b>")]
    [InlineData("a <b>b</b> c <i>d</i> e")]
    [InlineData("<size=70%>Tch. Well?")]
    [InlineData("unclosed <b>to the end")]
    [InlineData("</b>closing with nothing open")]
    [InlineData("a < b > c")]
    [InlineData("<notatag>left alone</notatag>")]
    [InlineData("<b><i><color=#f66><size=120%>all four</size></color></i></b>")]
    [InlineData("an unfinished <tag")]
    [InlineData("<<b>>doubled<</b>>")]
    [InlineData("Hello {PC}, said {M}.")]
    [InlineData("{PC}")]
    [InlineData("an unclosed {brace")]
    [InlineData("{PCC} is not one of them")]
    [InlineData("<b>{PC}</b>")]
    [InlineData("nested {a{PC}} braces")]
    public void EveryCharacterLandsInExactlyOneSpan(string text)
    {
        // The safety property. Everything else is presentation; this one is
        // what keeps the editor from eating an author's line.
        var spans = DialogueMarkup.Parse(text);
        _out.WriteLine($"{spans.Count} span(s) for: {text}");

        Assert.Equal(text, Rebuilt(text));

        int at = 0;
        foreach (var s in spans)
        {
            Assert.Equal(at, s.Start);
            Assert.True(s.Length > 0, "a zero-length span is a span that means nothing");
            at += s.Length;
        }
        Assert.Equal(text.Length, at);
    }

    [Fact]
    public void TagsAreMarkedAsTagsAndTextIsNot()
    {
        var spans = DialogueMarkup.Parse("a <b>x</b> z");
        foreach (var s in spans)
            _out.WriteLine($"   {(s.IsTag ? "tag " : "text")} '{s.Of("a <b>x</b> z")}'");

        Assert.Equal(new[] { false, true, false, true, false },
                     spans.Select(s => s.IsTag).ToArray());
    }

    [Fact]
    public void TheTextInsideATagCarriesItsStyle()
    {
        const string line = "plain <b>bold</b> plain";
        var spans = DialogueMarkup.Parse(line);

        var inside = spans.Single(s => !s.IsTag && s.Of(line) == "bold");
        Assert.True(inside.Style.Bold);

        foreach (var outside in spans.Where(s => !s.IsTag && s.Of(line) != "bold"))
            Assert.False(outside.Style.Bold);
    }

    [Fact]
    public void TagsStack()
    {
        // The case this is a parser for. Three styles over the middle words,
        // and each closing tag returns to what was underneath rather than to
        // nothing at all.
        const string line = "<b>one <i>two <color=#f66>three</color> four</i> five</b> six";
        var spans = DialogueMarkup.Parse(line).Where(s => !s.IsTag).ToArray();

        foreach (var s in spans)
            _out.WriteLine($"   '{s.Of(line)}'  bold={s.Style.Bold} italic={s.Style.Italic} "
                           + $"colour={s.Style.Color ?? "-"}");

        var three = spans.Single(s => s.Of(line) == "three");
        Assert.True(three.Style.Bold);
        Assert.True(three.Style.Italic);
        Assert.Equal("#f66", three.Style.Color);

        var four = spans.Single(s => s.Of(line) == " four");
        Assert.True(four.Style.Bold);
        Assert.True(four.Style.Italic);
        Assert.Null(four.Style.Color);          // the colour closed, the rest did not

        var five = spans.Single(s => s.Of(line) == " five");
        Assert.True(five.Style.Bold);
        Assert.False(five.Style.Italic);

        var six = spans.Single(s => s.Of(line) == " six");
        Assert.True(six.Style.IsPlain);
    }

    [Fact]
    public void AnUnclosedTagRunsToTheEnd()
    {
        const string line = "before <i>after";
        var after = DialogueMarkup.Parse(line).Single(s => !s.IsTag && s.Of(line) == "after");
        Assert.True(after.Style.Italic);
    }

    [Fact]
    public void ClosingSomethingThatWasNeverOpenedChangesNothing()
    {
        const string line = "</b>hello";
        var spans = DialogueMarkup.Parse(line);
        Assert.True(spans[0].IsTag);
        Assert.True(spans[1].Style.IsPlain);
    }

    [Theory]
    [InlineData("<size=70%>x", 0.7)]
    [InlineData("<size=150%>x", 1.5)]
    [InlineData("<size=19>x", 0.5)]        // half of the game's own 38pt
    [InlineData("<size=38>x", 1.0)]        // the size it already draws at
    [InlineData("<size=900%>x", 3.0)]      // clamped, so a typo cannot wreck the box
    [InlineData("<size=1%>x", 0.3)]
    public void SizeIsShownAsAMultipleOfNormal(string line, double expected)
    {
        var text = DialogueMarkup.Parse(line).Single(s => !s.IsTag);
        _out.WriteLine($"{line} -> x{text.Style.Scale:0.##}");
        Assert.Equal(expected, text.Style.Scale, 3);
    }

    [Fact]
    public void AnUnknownTagIsLeftAsOrdinaryText()
    {
        // The honest thing to show: the game prints it at the player, so the
        // editor showing it as markup would be a promise nobody keeps.
        const string line = "<sprite=3>hello<marquee>";
        var spans = DialogueMarkup.Parse(line);

        _out.WriteLine(string.Join(" | ", spans.Select(s => (s.IsTag ? "tag:" : "text:") + s.Of(line))));
        Assert.All(spans, s => Assert.False(s.IsTag));
        Assert.Single(spans);
    }

    [Fact]
    public void AnOpeningTagMissingItsValueIsNotATag()
    {
        // <color> with nothing after it colours nothing, so it is shown as the
        // text the game will print. Its CLOSING partner is still a tag, though:
        // </color> is well-formed and the game consumes it either way, so
        // showing it as text would be the lie in the other direction.
        const string line = "<color>x</color>";
        var spans = DialogueMarkup.Parse(line);
        foreach (var s in spans)
            _out.WriteLine($"   {(s.IsTag ? "tag " : "text")} '{s.Of(line)}'");

        Assert.False(spans.Any(s => s.IsTag && s.Of(line) == "<color>"));
        Assert.True(spans.Any(s => s.IsTag && s.Of(line) == "</color>"));

        // ...and with a value it is a tag, colouring what it wraps.
        const string good = "<color=#fff>x</color>";
        Assert.Equal("#fff", DialogueMarkup.Parse(good).Single(s => !s.IsTag).Style.Color);

        // b and i never take a value at all.
        Assert.True(DialogueMarkup.HasMarkup("<b>x</b>"));
    }

    [Fact]
    public void QuotedAndCasedTagsAreStillTags()
    {
        const string line = "<COLOR=\"red\">shout</COLOR>";
        var text = DialogueMarkup.Parse(line).Single(s => !s.IsTag);
        Assert.Equal("red", text.Style.Color);
    }

    // ── The game's own tokens ─────────────────────────────────────────

    [Fact]
    public void AKnownTokenIsToldApartFromTheWordsAroundIt()
    {
        // The point of the whole thing: {PC} is not four characters the player
        // reads, it is wherever their own name goes. A row that showed it flat
        // says nothing about that.
        const string line = "Morning, {PC}.";
        var spans = DialogueMarkup.Parse(line);
        foreach (var s in spans)
            _out.WriteLine($"   {s.Kind,-5} '{s.Of(line)}'");

        var token = spans.Single(s => s.IsToken);
        Assert.Equal("{PC}", token.Of(line));
        Assert.All(spans.Where(s => !s.IsToken), s => Assert.True(s.IsWords));
    }

    [Theory]
    [InlineData("{PC}")]
    [InlineData("{M}")]
    [InlineData("{D}")]
    [InlineData("{B}")]
    [InlineData("{S}")]
    [InlineData("{DA}")]
    [InlineData("{F}")]
    public void EveryTokenOnTheListIsRecognised(string token)
    {
        string line = "a " + token + " b";
        Assert.Equal(token, DialogueMarkup.Parse(line).Single(s => s.IsToken).Of(line));
    }

    [Theory]
    [InlineData("{PCC}")]
    [InlineData("{}")]
    [InlineData("{pc}")]
    [InlineData("{ PC }")]
    [InlineData("{PC")]
    public void ABracedWordThatIsNotOneOfThemIsLeftAsWords(string braced)
    {
        // The control, and the same rule an unknown TAG gets: the editor cannot
        // promise the game does anything with it, and dressing up a typo as a
        // working token is how the typo reaches the player.
        string line = "say " + braced + " now";
        var spans = DialogueMarkup.Parse(line);

        _out.WriteLine($"{line} -> {spans.Count} span(s), "
                       + $"{spans.Count(s => s.IsToken)} token(s)");
        Assert.DoesNotContain(spans, s => s.IsToken);
    }

    [Fact]
    public void ATokenInsideATagCarriesTheTagsStyle()
    {
        // What the game will put there is bold along with the rest of the
        // sentence, so the mark for it has to be able to say so.
        const string line = "<b>Morning, {PC}.</b>";
        var token = DialogueMarkup.Parse(line).Single(s => s.IsToken);

        Assert.True(token.Style.Bold);
        Assert.Equal("{PC}", token.Of(line));
    }

    [Fact]
    public void TwoTokensInOneLineAreBothFound()
    {
        const string line = "{M} told {D} about it";
        var tokens = DialogueMarkup.Parse(line).Where(s => s.IsToken).Select(s => s.Of(line));
        Assert.Equal(new[] { "{M}", "{D}" }, tokens.ToArray());
    }

    [Fact]
    public void APlainLineHasNoMarkupAtAll()
    {
        // The control for HasMarkup, and the common case by far: most lines are
        // words, and the editor should do nothing special to them.
        Assert.False(DialogueMarkup.HasMarkup("She looked at me and said nothing."));
        Assert.False(DialogueMarkup.HasMarkup(null));
        Assert.False(DialogueMarkup.HasMarkup(""));

        // A pack token is not markup either - different syntax, different job.
        Assert.False(DialogueMarkup.HasMarkup("Hello {PC}, said [PV:name]."));
    }
}
