using System.Collections.Generic;
using System.Linq;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Keeping a line's machinery intact through a machine translation.
/// <para/>
/// A pack's text is not only words: <c>{name}</c> is filled in at runtime,
/// <c>&lt;b&gt;</c> is markup the game reads, <c>[PV:money]</c> names a
/// variable. A translator treats all three as words — it translates what is
/// inside the braces, reorders or drops tags, and puts spaces inside tokens.
/// Every one of those breaks the line somewhere only the game shows, in a
/// language the author cannot read, across thousands of lines nobody is going
/// to check by hand.
/// <para/>
/// So the tests below are written as a translator BEHAVING BADLY. Each one
/// does something a real translator has been seen to do to a marker, and asks
/// whether the line survives it — or, where it cannot survive, whether it says
/// so instead of shipping something broken. A test that only fed clean input
/// back would pass forever and prove nothing.
/// </summary>
public sealed class ProtectedTextTests
{
    private readonly ITestOutputHelper _out;
    public ProtectedTextTests(ITestOutputHelper o) => _out = o;

    /// <summary>Protect, hand the masked text to <paramref name="translator"/>,
    /// and put the codes back — the whole round trip, as it will run.</summary>
    private string RoundTrip(string original, System.Func<string, string> translator,
                             out IReadOnlyList<string> lost)
    {
        var masked = ProtectedText.Protect(original);
        string sent = masked.Text;
        string came = translator(sent);
        var gone = new List<string>();
        string put = ProtectedText.Restore(came, masked.Codes, gone);
        lost = gone;

        _out.WriteLine($"  original: {original}");
        _out.WriteLine($"  masked  : {sent}");
        _out.WriteLine($"  back     : {came}");
        _out.WriteLine($"  restored: {put}");
        if (gone.Count > 0) _out.WriteLine($"  LOST    : {string.Join(", ", gone)}");
        return put;
    }

    private const string Line = "Hello {name}, you have <b>[PV:money]</b> left. Spend it on {thing}?";

    [Fact]
    public void TheWordsGoAndTheMachineryStays()
    {
        var masked = ProtectedText.Protect(Line);
        _out.WriteLine(masked.Text);

        // Nothing a translator would recognise as a word is left in the codes'
        // place, and every code was taken.
        Assert.Equal(5, masked.Codes.Count);
        Assert.Equal(new[] { "{name}", "<b>", "[PV:money]", "</b>", "{thing}" }, masked.Codes);
        Assert.DoesNotContain("{", masked.Text);
        Assert.DoesNotContain("PV:", masked.Text);

        // ...and the words themselves are untouched, or there is nothing to
        // translate.
        Assert.Contains("Hello", masked.Text);
        Assert.Contains("left", masked.Text);
    }

    [Fact]
    public void AnHonestTranslatorGetsTheLineBackExactly()
    {
        // The control. If this failed, every test below would be measuring the
        // round trip rather than the damage.
        string put = RoundTrip(Line, s => s, out var lost);
        Assert.Equal(Line, put);
        Assert.Empty(lost);
    }

    [Fact]
    public void ReorderingIsFineBecauseLanguagesReorder()
    {
        // German puts things elsewhere, and that is correct rather than damage.
        // The markers carry their own number, so each code lands where the new
        // language put it.
        var masked = ProtectedText.Protect("{a} then {b}");
        string swapped = masked.Text.Replace(ProtectedText.Marker(0), "TMP")
                                    .Replace(ProtectedText.Marker(1), ProtectedText.Marker(0))
                                    .Replace("TMP", ProtectedText.Marker(1));
        var lost = new List<string>();
        string put = ProtectedText.Restore(swapped, masked.Codes, lost);

        _out.WriteLine(put);
        Assert.Equal("{b} then {a}", put);
        Assert.Empty(lost);
    }

    [Theory]
    // Every one of these is a real thing translators do to a marker.
    [InlineData("pads it with spaces", " %% 0 %% ")]
    [InlineData("drops one percent sign", "%0%%")]
    [InlineData("drops the other", "%%0%")]
    [InlineData("puts a space inside", "% %0% %")]
    public void AMarkerThatComesBackBentIsStillUnderstood(string what, string bent)
    {
        _out.WriteLine(what + ": '" + bent + "'");
        var masked = ProtectedText.Protect("Give {name} a hat.");
        string came = masked.Text.Replace(ProtectedText.Marker(0), bent);

        var lost = new List<string>();
        string put = ProtectedText.Restore(came, masked.Codes, lost);

        _out.WriteLine("   -> " + put);
        Assert.Contains("{name}", put);
        Assert.Empty(lost);
    }

    [Fact]
    public void AMarkerThatNeverCameBackIsReported()
    {
        // The one that matters most. A translator that swallows a marker leaves
        // a line with a gap nothing fills - and a line missing its {name} is
        // not a rough translation, it is a broken one. Saying so is what lets
        // the caller keep the author's own words instead.
        string put = RoundTrip("Hello {name}!", s => "Bonjour !", out var lost);

        Assert.Equal("{name}", Assert.Single(lost));
        Assert.DoesNotContain("{name}", put);
    }

    [Fact]
    public void OnlySomeOfThemComingBackIsAlsoReported()
    {
        var masked = ProtectedText.Protect(Line);
        // Keeps the first, loses the rest - the commonest real failure, because
        // the tags are what a translator strips.
        string came = ProtectedText.Marker(0) + " something in French";

        var lost = new List<string>();
        ProtectedText.Restore(came, masked.Codes, lost);

        _out.WriteLine(string.Join(", ", lost));
        Assert.Equal(4, lost.Count);
        Assert.DoesNotContain("{name}", lost);
    }

    [Fact]
    public void ANumberTheTranslatorInventedIsLeftAlone()
    {
        // A line about percentages, or a marker number beyond what was sent.
        // Treating it as a code would put somebody else's markup into the text.
        var masked = ProtectedText.Protect("Give {name} a hat.");
        var lost = new List<string>();
        string put = ProtectedText.Restore(masked.Text + " 50%%9%% off", masked.Codes, lost);

        _out.WriteLine(put);
        Assert.Contains("%%9%%", put);
        Assert.Contains("{name}", put);
        Assert.Empty(lost);
    }

    [Fact]
    public void TheSameCodeTwiceIsTwoMarkers()
    {
        // Replacing by value would give both occurrences the first one's
        // number, and the line would come back with one of them missing.
        var masked = ProtectedText.Protect("{name} told {name} about it.");
        _out.WriteLine(masked.Text);

        Assert.Equal(2, masked.Codes.Count);
        Assert.Contains(ProtectedText.Marker(0), masked.Text);
        Assert.Contains(ProtectedText.Marker(1), masked.Text);

        var lost = new List<string>();
        Assert.Equal("{name} told {name} about it.",
                     ProtectedText.Restore(masked.Text, masked.Codes, lost));
        Assert.Empty(lost);
    }

    [Theory]
    [InlineData("An ordinary line with nothing in it.")]
    [InlineData("")]
    [InlineData(null)]
    public void ALineWithNothingToProtectIsNotTouched(string text)
    {
        var masked = ProtectedText.Protect(text);
        Assert.True(masked.Plain);
        Assert.Equal(text ?? "", masked.Text);
    }

    // ── The check that runs on the finished line ─────────────────────

    [Fact]
    public void TheFinishedLineIsHeldToTheSameStandardAsAPersons()
    {
        Assert.True(ProtectedText.KeptItsCodes(Line, Line));
        Assert.True(ProtectedText.KeptItsCodes("{a} and {b}", "{b} et {a}"));

        Assert.False(ProtectedText.KeptItsCodes("Hello {name}", "Bonjour"));
        Assert.False(ProtectedText.KeptItsCodes("Hello {name}", "Bonjour {nom}"));
        Assert.False(ProtectedText.KeptItsCodes("{a} and {b}", "{a} et {a}"));
    }

    [Fact]
    public void TheRealLineGoesRoundAndComesBackWhole()
    {
        // A translator that does everything at once: translates the words,
        // reorders the markers, pads two of them and leaves the rest alone.
        var masked = ProtectedText.Protect(Line);
        string came = $"Tu as {ProtectedText.Marker(1)}{ProtectedText.Marker(2)}"
                    + $"{ProtectedText.Marker(3)} restant, %% 0 %%. "
                    + $"Le dépenser en {ProtectedText.Marker(4)} ?";

        var lost = new List<string>();
        string put = ProtectedText.Restore(came, masked.Codes, lost);
        _out.WriteLine(put);

        Assert.Empty(lost);
        Assert.Contains("{name}", put);
        Assert.Contains("<b>[PV:money]</b>", put);
    }
}
