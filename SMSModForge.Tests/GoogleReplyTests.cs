using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Reading the free translation endpoint's reply.
/// <para/>
/// It is not a documented API and the reply is not a documented shape — nested
/// arrays with nulls in them, differing by how many texts went out. So the
/// shapes it is known to answer in are written down here as tests, and a change
/// to the service shows up as a failure here rather than as a pack full of
/// misaligned dialogue.
/// <para/>
/// Half these tests are about REFUSING. A reply that cannot be read as exactly
/// the lines that were sent is not partially useful: there is no way to tell
/// which answer belongs to which line, and lining them up anyway would file one
/// line's words under another line's key, quietly, for thousands of lines
/// nobody is going to read.
/// </summary>
public sealed class GoogleReplyTests
{
    private readonly ITestOutputHelper _out;
    public GoogleReplyTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void SeveralTextsComeBackAsAFlatList()
    {
        var got = GoogleReply.Parse(@"[""Hola"",""Adiós"",""Gracias""]", 3);
        Assert.Equal(new[] { "Hola", "Adiós", "Gracias" }, got);
    }

    [Fact]
    public void OneTextComesBackSegmented_AndTheSegmentsAreOneLineAgain()
    {
        // The endpoint splits a text into sentences and returns one entry per
        // sentence. They were one line going out and must be one coming back,
        // or a line of dialogue arrives in the pack as two.
        string reply = @"[[[""Hola. "",""Hello. "",null,null,10],"
                     + @"[""¿Qué tal?"",""How are you?"",null,null,3]],null,""en""]";
        var got = GoogleReply.Parse(reply, 1);

        _out.WriteLine(string.Join(" | ", got));
        Assert.Equal("Hola. ¿Qué tal?", Assert.Single(got));
    }

    [Fact]
    public void TheWrongNumberOfLinesIsRefused()
    {
        // The one that matters. Three sent, two back: there is no way to know
        // which two, so the answer is none of them.
        Assert.Null(GoogleReply.Parse(@"[""Hola"",""Adiós""]", 3));
        Assert.Null(GoogleReply.Parse(@"[""Hola"",""Adiós"",""Gracias""]", 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    // An error page, or the service asking for a captcha, arriving where JSON
    // was expected.
    [InlineData("<!DOCTYPE html><html><body>Sorry...</body></html>")]
    public void AnythingElseIsRefusedRatherThanGuessedAt(string reply)
    {
        _out.WriteLine($"'{reply}'");
        Assert.Null(GoogleReply.Parse(reply, 2));
    }

    [Fact]
    public void AListWithSomethingOtherThanStringsInItIsRefused()
    {
        // Right length, wrong contents. Taking the count alone as proof would
        // write "null" or "0" into somebody's pack as a translated line.
        Assert.Null(GoogleReply.Parse(@"[""Hola"",null]", 2));
        Assert.Null(GoogleReply.Parse(@"[""Hola"",123]", 2));
        Assert.Null(GoogleReply.Parse(@"[""Hola"",[""Adiós""]]", 2));
    }

    [Fact]
    public void NothingWasSentSoNothingCanComeBack()
    {
        Assert.Null(GoogleReply.Parse(@"[""Hola""]", 0));
        Assert.Null(GoogleReply.Parse(@"[""Hola""]", -1));
    }

    // ── Being blocked, which is not the same as being told to slow down ──

    /// <summary>What translate_a/single sent back to a flagged connection,
    /// recorded, with the styling cut.</summary>
    private const string SorryPage =
        "<html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=utf-8\"/>"
      + "<title>Sorry...</title></head><body><div>We're sorry... but your computer or network "
      + "may be sending automated queries. To protect our users, we can't process your request "
      + "right now.</div></body></html>";

    /// <summary>What translate_a/t sent back to the same connection: a
    /// different page with the same meaning.</summary>
    private const string CaptchaPage =
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01 Transitional//EN\"><html><head>"
      + "<title>https://translate.googleapis.com/translate_a/t</title></head>"
      + "<body onload=\"e=document.getElementById('captcha');if(e){e.focus();}\"></body></html>";

    [Fact]
    public void BothOfGooglesBlockPagesAreRecognised()
    {
        // Recorded from the real service, not imagined. Both arrive with a 429 -
        // the status an ordinary slow-down has - so the status cannot tell them
        // apart, and a block retried is a block made longer.
        Assert.True(GoogleReply.IsBlockPage(SorryPage));
        Assert.True(GoogleReply.IsBlockPage(CaptchaPage));

        // And neither is read as a translation.
        Assert.Null(GoogleReply.Parse(SorryPage, 1));
        Assert.Null(GoogleReply.Parse(CaptchaPage, 1));
    }

    [Theory]
    [InlineData(@"[""Hola""]")]
    [InlineData(@"[""El captcha es difícil"",""Hay tráfico inusual""]")]
    [InlineData(@"[[[""Lo siento...""  ,""Sorry..."",null,null,1]],null,""en""]")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<html><body>Service Unavailable</body></html>")]
    public void AnythingElseIsNotABlock(string? body)
    {
        // The controls, and the second one is the one that matters: a pack
        // about a captcha, translated, contains the word. A reply that parses
        // as JSON is a translation whatever it says.
        Assert.False(GoogleReply.IsBlockPage(body));
    }

    [Fact]
    public void ASingleTextInTheFlatShapeStillReads()
    {
        // Both shapes are valid for one text, depending on the endpoint used,
        // and the flat one must not be mistaken for a segmented one.
        Assert.Equal(new[] { "Hola" }, GoogleReply.Parse(@"[""Hola""]", 1));
    }
}
