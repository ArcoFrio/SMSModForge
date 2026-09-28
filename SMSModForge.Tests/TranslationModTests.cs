using SMSModForge.Shared;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// The rules behind two things the plugin does in the game: timing a pack
/// line's typing by what shows of it, and working beside XUnity.AutoTranslator
/// - which language of its is which of ModForge's, what its settings say, and
/// which texts it is asked to leave alone.
/// </summary>
public sealed class TranslationModTests
{
    // ── What shows of a line ──────────────────────────────────────────

    [Theory]
    [InlineData("Hello", 5)]
    [InlineData("<b>Hello</b>", 5)]
    [InlineData("<color=#FF0000>Hi</color> there", 8)]
    [InlineData("<#F00>Hi</color>", 2)]                        // the short colour form
    [InlineData("<size=150%><i>Big</i></size>", 3)]
    [InlineData("One<br>Two", 7)]                              // a line break is one character
    [InlineData("A <sprite=3> B", 5)]                          // so is a picture
    [InlineData("1 < 2 and 3 > 2", 15)]                        // not markup: shown as written
    [InlineData("<heart>", 7)]                                 // not a tag the text engine knows
    [InlineData("<noparse><b></noparse>", 3)]                  // inside noparse everything is text
    [InlineData("Line\\nNext", 9)]                             // the \n escape is one character
    [InlineData("😀!", 2)]                                     // one letter, two UTF-16 units
    [InlineData("", 0)]
    public void ALineIsTimedByWhatShowsOfIt(string text, int visible)
    {
        Assert.Equal(visible, RichText.VisibleLength(text));
    }

    // ── Texts with places filled when shown ───────────────────────────

    [Fact]
    public void ATextIsKnownWhateverFillsItsPlaces()
    {
        var texts = new TextTemplates();
        texts.Add("<b>Hi</b>, {PCName}! You have [PV:Coins] coins.");
        texts.Add("Plain line.");

        Assert.True(texts.Contains("Plain line."));
        Assert.True(texts.Contains("<b>Hi</b>, Anna! You have 12 coins."));
        Assert.True(texts.Contains("<b>Hi</b>, {PCName}! You have [PV:Coins] coins."));   // not filled yet
        Assert.True(texts.Contains("<b>Hi</b>, ! You have  coins."));                     // filled with nothing

        // The words around the places have to be the same.
        Assert.False(texts.Contains("<b>Hi</b>, Anna! You have 12 coins"));
        Assert.False(texts.Contains("Hi, Anna! You have 12 coins."));
        Assert.False(texts.Contains("Plain line"));
        // Markup in the words is matched as it is, not read as a pattern.
        texts.Add("(a+b)* {x}");
        Assert.True(texts.Contains("(a+b)* 7"));
        Assert.False(texts.Contains("aab 7"));

        texts.Clear();
        Assert.False(texts.Contains("Plain line."));
    }

    // ── XUnity.AutoTranslator ─────────────────────────────────────────

    private const string Ini =
        "[Service]\r\n" +
        "Endpoint=GoogleTranslateV2\r\n" +
        "\r\n" +
        "[General]\r\n" +
        "Language=zh-CN   ;The language to translate into\r\n" +
        "FromLanguage=en\r\n" +
        "\r\n" +
        "[Files]\r\n" +
        "Language=not this one\r\n";

    [Fact]
    public void ItsLanguagesAreReadFromItsSettings_InTheirSection()
    {
        Assert.Equal("zh-CN", XUnityLanguage.Read(Ini, "General", "Language"));
        Assert.Equal("en", XUnityLanguage.Read(Ini, "general", "fromlanguage"));
        Assert.Equal("GoogleTranslateV2", XUnityLanguage.Read(Ini, "Service", "Endpoint"));
        Assert.Null(XUnityLanguage.Read(Ini, "General", "Nothing"));
        Assert.Null(XUnityLanguage.Read("", "General", "Language"));
        Assert.Null(XUnityLanguage.Read(null!, "General", "Language"));
    }

    [Fact]
    public void ItsCodesAndOursMeanTheSameLanguages()
    {
        Assert.True(XUnityLanguage.Same("zh-Hans", "zh-CN"));
        Assert.True(XUnityLanguage.Same("pt-BR", "pt"));
        Assert.True(XUnityLanguage.Same("de", "de"));
        Assert.False(XUnityLanguage.Same("zh-Hans", "zh-TW"));   // the script is what matters
        Assert.False(XUnityLanguage.Same("en", "ja"));
        Assert.False(XUnityLanguage.Same("", "en"));
    }
}
