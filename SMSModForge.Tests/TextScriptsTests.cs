using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Which writing systems a pack needs a font for.
/// <para/>
/// This was got wrong the obvious way first: the fonts were chosen from the
/// PLAYER's language setting. A Portuguese player opening a pack with Chinese
/// in it got Segoe UI — so Cyrillic and Greek drew, proving the fallback
/// mechanism worked, while every Chinese, Japanese and Korean line showed as
/// boxes, because no font that had those characters was ever asked for. The
/// text is the only thing that knows.
/// </summary>
public sealed class TextScriptsTests
{
    private readonly ITestOutputHelper _out;
    public TextScriptsTests(ITestOutputHelper o) => _out = o;

    [Theory]
    // The controls: everything the game already draws asks for nothing, and
    // that is every pack written today.
    [InlineData("Just an ordinary English line.")]
    [InlineData("¿Qué tal? Olá — coração, año, français, ÄÖÜ.")]
    [InlineData("Привет! Это проверка кириллицы.")]
    [InlineData("Γειά σου κόσμε — δοκιμή ελληνικών.")]
    [InlineData("")]
    [InlineData(null)]
    public void LatinCyrillicAndGreekNeedNothing(string text)
    {
        var scripts = TextScripts.Of(text);
        _out.WriteLine($"'{text}' -> {scripts.Count} script(s)");
        Assert.Empty(scripts);
    }

    [Fact]
    public void ChineseAsksForAChineseFont()
    {
        var scripts = TextScripts.Of("5 Chinese: 你好，这是一段中文测试。");
        Assert.Equal(new[] { TextScripts.Script.Chinese }, scripts);
        Assert.Contains("Microsoft YaHei", TextScripts.Fonts(TextScripts.Script.Chinese));
    }

    [Fact]
    public void KanaAsksForAJapaneseFontFirst()
    {
        // Japanese comes first so a Japanese font is searched first and shapes
        // the kanji the way a Japanese reader expects. The Chinese font behind
        // it is what catches the characters it does not have.
        var scripts = TextScripts.Of("こんにちは。これは日本語のテストです。漢字も。");
        _out.WriteLine(string.Join(", ", scripts));
        Assert.Equal(TextScripts.Script.Japanese, scripts[0]);
    }

    [Fact]
    public void KanaDoesNotSuppressTheChineseFont()
    {
        // The bug, and it was found in a running game rather than here: kana
        // used to SETTLE which of the two Han readings the text was, so one
        // Japanese line anywhere in a pack made the whole pack Japanese and no
        // Chinese font was ever loaded.
        //
        // It half worked, which is why nothing caught it. A Japanese font has
        // Han in it, so most of the Chinese drew and only the simplified-only
        // forms came out as boxes - scattered through lines that were otherwise
        // perfectly fine, reading as art that had not loaded.
        var scripts = TextScripts.Of("こんにちは。 你好，这是一段中文测试。");
        _out.WriteLine(string.Join(", ", scripts));

        Assert.Contains(TextScripts.Script.Japanese, scripts);
        Assert.Contains(TextScripts.Script.Chinese, scripts);
    }

    [Theory]
    // The characters off the screenshot that found this: every one of them is
    // a simplified form whose Japanese counterpart is written differently, so
    // a Japanese font draws everything around them and leaves these as boxes.
    [InlineData("这")] [InlineData("测")] [InlineData("试")] [InlineData("组")]
    [InlineData("应")] [InlineData("该")] [InlineData("显")] [InlineData("标")]
    [InlineData("红")] [InlineData("说")] [InlineData("戏")]
    public void TheCharactersThatBoxedOutAskForAChineseFont(string simplified)
    {
        // Each on its own, and each beside kana - which is the case that
        // failed. A test of the bare character would have passed all along.
        Assert.Contains(TextScripts.Script.Chinese, TextScripts.Of(simplified));
        Assert.Contains(TextScripts.Script.Chinese, TextScripts.Of("あ" + simplified));
    }

    [Fact]
    public void KoreanAsksForAKoreanFont()
    {
        var scripts = TextScripts.Of("안녕하세요. 이것은 한국어 테스트입니다.");
        Assert.Equal(new[] { TextScripts.Script.Korean }, scripts);
    }

    [Fact]
    public void OneLineCanNeedSeveral()
    {
        // The test pack's line 8, which is the case that found the bug.
        var scripts = TextScripts.Of("Mixed: English 中文 日本語 한국어 Русский Ελληνικά 1234");
        _out.WriteLine(string.Join(", ", scripts));
        Assert.Contains(TextScripts.Script.Chinese, scripts);
        Assert.Contains(TextScripts.Script.Korean, scripts);
        Assert.DoesNotContain(TextScripts.Script.Thai, scripts);

        // And what it does NOT ask for. There is no kana in this line - the
        // word 日本語 is three kanji - so nothing in it says Japanese. A
        // Chinese font draws those three characters; they are the same
        // characters. Only kana, or a translation of the pack into Japanese,
        // brings a Japanese font with it.
        Assert.DoesNotContain(TextScripts.Script.Japanese, scripts);
    }

    [Fact]
    public void EveryScriptNamesAFontToTryFirst()
    {
        foreach (TextScripts.Script script in System.Enum.GetValues(typeof(TextScripts.Script)))
        {
            var fonts = TextScripts.Fonts(script);
            _out.WriteLine($"{script}: {string.Join(", ", fonts)}");
            Assert.NotEmpty(fonts);
        }
    }

    [Fact]
    public void AWholeManifestIsScannedInOnePass()
    {
        // It runs over the pack's whole JSON, so it has to cope with megabytes
        // of Latin without finding anything. The control is that it still finds
        // the one line that matters inside it.
        string big = string.Concat(Enumerable.Repeat("An ordinary English line of dialogue. ", 20000));
        Assert.Empty(TextScripts.Of(big));
        Assert.Equal(new[] { TextScripts.Script.Chinese }, TextScripts.Of(big + "你好" + big));
    }
}
