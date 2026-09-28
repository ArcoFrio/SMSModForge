using System.Collections.Generic;
using System.Linq;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Sending a batch of lines through an endpoint that takes one text.
/// <para/>
/// The service translates a batch as one document and is free to merge two
/// short lines, break a long one, or move a word across the join. So each line
/// goes out behind a numbered marker of its own and is claimed by that marker
/// on the way back - never by its position.
/// <para/>
/// These are written as a translator misbehaving, because that is the only
/// case worth testing: a line's words filed under another line's key looks
/// exactly like a translation, in a language the author may not read, across
/// thousands of lines nobody checks by hand.
/// </summary>
public sealed class GoogleBatchTests
{
    private readonly ITestOutputHelper _out;
    public GoogleBatchTests(ITestOutputHelper o) => _out = o;

    private static string M(int i) => GoogleTranslator.LineMarker(i);

    [Fact]
    public void EachLineGoesOutBehindItsOwnMarker()
    {
        string joined = GoogleTranslator.Join(new[] { "Good morning.", "Where is the beach?" });
        _out.WriteLine(joined.Replace("\n", " | "));

        Assert.StartsWith(M(0), joined);
        Assert.Contains(M(1), joined);
    }

    [Fact]
    public void AnHonestAnswerComesBackAsTheSameLines()
    {
        string back = $"{M(0)} Buen día.\n{M(1)} ¿Dónde está la playa?\n{M(2)} Hola %%0%%, bienvenido.";
        var got = GoogleTranslator.Split(back, 3);

        Assert.Equal(new[] { "Buen día.", "¿Dónde está la playa?", "Hola %%0%%, bienvenido." }, got);
    }

    [Fact]
    public void TwoLinesMergedIntoOneRowAreStillTwoLines()
    {
        // The case counting line breaks gets wrong. The translator joined lines
        // 0 and 1 into one sentence on one row - and broke line 2 into two rows.
        // The number of rows is still three, so a count would have said "fine"
        // and filed every line under its neighbour's key. The markers say whose
        // words are whose.
        string back = $"{M(0)} Buen día, {M(1)} ¿dónde está la playa?\n{M(2)} Hola,\nbienvenido.";
        var got = GoogleTranslator.Split(back, 3);
        foreach (var g in got!) _out.WriteLine("'" + g + "'");

        Assert.Equal("Buen día,", got[0]);
        Assert.Equal("¿dónde está la playa?", got[1]);
        Assert.StartsWith("Hola,", got[2]);
    }

    [Fact]
    public void LinesThatComeBackOutOfOrderStillFindTheirOwnWords()
    {
        string back = $"{M(1)} Segunda.\n{M(0)} Primera.";
        Assert.Equal(new[] { "Primera.", "Segunda." }, GoogleTranslator.Split(back, 2));
    }

    [Theory]
    // What a translator has been seen to do to a marker: pad it, drop a sign.
    [InlineData("% % 8000 %% Uno.\n%%8001% Dos.")]
    [InlineData("%%8000%%Uno.\n %% 8001 %%   Dos.")]
    public void ABentMarkerIsStillUnderstood(string back)
    {
        var got = GoogleTranslator.Split(back, 2);
        Assert.Equal(new[] { "Uno.", "Dos." }, got);
    }

    [Fact]
    public void AMissingMarkerRefusesTheWholeBatch()
    {
        // No way to know which line lost it, so no line's words can be trusted
        // to be its own. The caller halves the batch instead.
        Assert.Null(GoogleTranslator.Split($"{M(0)} Uno. Dos.\n{M(2)} Tres.", 3));
    }

    [Fact]
    public void AMarkerSeenTwiceRefusesTheWholeBatch()
    {
        Assert.Null(GoogleTranslator.Split($"{M(0)} Uno.\n{M(0)} Dos.", 2));
    }

    [Fact]
    public void AMarkerForALineTheBatchDoesNotHaveRefusesIt()
    {
        Assert.Null(GoogleTranslator.Split($"{M(0)} Uno.\n{M(7)} Dos.", 2));
    }

    [Fact]
    public void ALineWithItsOwnLineBreakKeepsIt()
    {
        string joined = GoogleTranslator.Join(new[] { "Hello there!\nNice day.", "Bye." });
        Assert.Contains(GoogleTranslator.BreakMarker, joined);

        var got = GoogleTranslator.Split($"{M(0)} ¡Hola!{GoogleTranslator.BreakMarker}Buen día.\n{M(1)} Adiós.", 2);
        Assert.Equal(new[] { "¡Hola!\nBuen día.", "Adiós." }, got);
    }

    [Fact]
    public void TheMarkersDoNotCollideWithTheMarkupsOwn()
    {
        // Three kinds of marker share one text: markup (0 upward), line (8000
        // upward) and an inner line break (9000). A line's markup must come
        // back as markup, not be read as the start of another line.
        string back = $"{M(0)} Hola %%0%%, tienes %%1%%.\n{M(1)} Adiós %%0%%.";
        var got = GoogleTranslator.Split(back, 2);
        Assert.Equal(new[] { "Hola %%0%%, tienes %%1%%.", "Adiós %%0%%." }, got);
    }

    // ── Words moved across a marker ──────────────────────────────────

    [Fact]
    public void ALineThatLostItsWordsToANeighbourIsDoubted()
    {
        // Every marker is there, so the split succeeds - but line 1 came back
        // empty and line 2 holds both. Only proportion shows it.
        var sent = new[]
        {
            "This is the first line of the conversation.",
            "And this is the second line of it here.",
            "The third line says something else again.",
            "The fourth line is about the same length.",
        };
        var came = new[]
        {
            "Esta es la primera línea de la conversación.",
            "Y",
            "Y esta es la segunda línea aquí. La tercera línea dice otra cosa de nuevo.",
            "La cuarta línea tiene más o menos la misma longitud.",
        };
        var doubtful = GoogleTranslator.Doubtful(sent, came);
        _out.WriteLine("doubtful: " + string.Join(", ", doubtful));

        Assert.Contains(1, doubtful);
        Assert.DoesNotContain(0, doubtful);
        Assert.DoesNotContain(3, doubtful);
    }

    [Fact]
    public void ALanguageThatRunsShorterIsNotDoubtedForIt()
    {
        // Chinese is far shorter than English. Measured against the batch's
        // own usual rather than a fixed ratio, so a whole batch of short
        // translations is not all doubted.
        var sent = new[]
        {
            "This is the first line of the conversation.",
            "And this is the second line of it here.",
            "The third line says something else again.",
            "The fourth line is about the same length.",
        };
        var came = new[] { "这是对话的第一行。", "这是第二行。", "第三行说了别的。", "第四行差不多长。" };

        Assert.Empty(GoogleTranslator.Doubtful(sent, came));
    }

    [Fact]
    public void WordsInAndNoneOutIsDoubtedAtAnyLength()
    {
        Assert.Equal(new[] { 0 }, GoogleTranslator.Doubtful(new[] { "Hi." }, new[] { "..." }));
    }

    [Fact]
    public void NothingSentIsNothingBack()
    {
        Assert.Null(GoogleTranslator.Split(null, 2));
        Assert.Null(GoogleTranslator.Split("Uno.", 0));
        Assert.Equal("", GoogleTranslator.Join(new List<string>()));
    }
}
