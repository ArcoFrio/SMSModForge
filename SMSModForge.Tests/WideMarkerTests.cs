using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Services.Translation;
using Xunit;

namespace SMSModForge.Tests;

/// <summary>
/// Chinese writes a percent sign full-width, and the Google Translate page
/// hands markers back that way - "％％8000％％" for "%%8000%%". Read as they
/// were sent, or every line of a Chinese batch is lost.
/// </summary>
public sealed class WideMarkerTests
{
    [Fact]
    public void AFullWidthMarkerIsReadAsTheOneSent_AndALinesOwnPercentIsLeftAlone()
    {
        Assert.Equal("%%8000%% 你好，%%0%%！", GoogleTranslator.HalfWidthMarkers("％％8000％％ 你好，％％0％％！"));
        Assert.Equal("%%12%%", GoogleTranslator.HalfWidthMarkers("％％１２％％"));
        // Nothing wide: exactly as it was.
        Assert.Equal("50% off, 5% more", GoogleTranslator.HalfWidthMarkers("50% off, 5% more"));
        Assert.Equal("打折５０％", GoogleTranslator.HalfWidthMarkers("打折５０％"));
    }

    [Fact]
    public async Task ABatchThatComesBackFullWidth_IsStillSplitIntoItsLines()
    {
        // The page, as it answers in Chinese: the lines translated, and every
        // percent sign turned full-width.
        Task<string> Page(string text, string? from, string to, CancellationToken c)
            => Task.FromResult(text.Replace("Hello", "你好").Replace('%', '％'));

        var translator = new GoogleTranslator(Page, 4500);
        var sent = new[] { "Hello %%0%%!", "Hello there.", "Hello again." };
        var got = await translator.Send(sent, "en", "zh-Hans", CancellationToken.None);
        Assert.Equal(new[] { "你好 %%0%%!", "你好 there.", "你好 again." }, got);
    }

    [Fact]
    public async Task ChineseSpellingsOfNamesArrive()
    {
        Task<string> Page(string text, string? from, string to, CancellationToken c)
            => Task.FromResult(text.Replace("My name is ", "我的名字是").Replace("Hello, ", "你好，")
                                   .Replace("Amber", "安柏").Replace('%', '％'));

        var translator = new GoogleTranslator(Page, 4500);
        var got = await PackTranslationJob.SuggestSpellings(translator.Send, new[] { "Amber" }, "zh-Hans");
        Assert.Equal("安柏", got["Amber"]);
    }

    /// <summary>
    /// The sentence with the marker and the sentence with the name are two
    /// separate answers, and Chinese punctuates them as it pleases: "!" in
    /// one, "！" in the other, a comma wide or not, a full stop or none. Read
    /// as the same sentence - they are.
    /// </summary>
    [Theory]
    [InlineData("你好，%%0%%!", "你好,安柏！")]
    [InlineData("你好，%%0%%！", "你好，安柏")]
    [InlineData("我的名字是%%0%%。", "我的名字是安柏.")]
    [InlineData("我的名字是 %%0%%.", "我的名字是安柏。")]
    [InlineData("你好 %%0%%！", "你好　安柏!")]
    // Google's own answer (author, 2026-09-26): a third percent sign, taken
    // from the marker dividing it from the next line.
    [InlineData("我叫 %%0%%%。", "我叫安柏。")]
    [InlineData("我叫 %%%0%%。", "我叫 安柏。")]
    // ...and a stray one beside the name.
    [InlineData("我叫 %%0%%。", "我叫安柏%。")]
    public void AChineseAnswerPunctuatedDifferentlyStillGivesTheName(string frameAnswer, string nameAnswer)
    {
        // Two frames: the name read from whichever is asked; the other is
        // left unreadable here, so this answer alone has to give it.
        var answers = new[] { frameAnswer, "-", nameAnswer, "?" };
        var got = PackNames.ReadSuggestions(new[] { "Amber" }, answers, "zh-Hans");
        Assert.Equal("安柏", got["Amber"]);
    }

    [Theory]
    // The words around the name are different words: nothing is read.
    [InlineData("你好，%%0%%!", "再见，安柏！")]
    // Google left the name in Latin letters: not a spelling.
    [InlineData("我的名字是%%0%%。", "我的名字是Amber。")]
    // The same, with the extra percent sign Google wrote.
    [InlineData("我叫 %%0%%%。", "我叫 Amber。")]
    // Only punctuation where the name should be.
    [InlineData("你好，%%0%%!", "你好，！")]
    public void AnAnswerThatDoesNotMatchIsStillRejected(string frameAnswer, string nameAnswer)
    {
        var answers = new[] { frameAnswer, "-", nameAnswer, "?" };
        Assert.Empty(PackNames.ReadSuggestions(new[] { "Amber" }, answers, "zh-Hans"));
    }

    [Fact]
    public async Task WhenNothingCanBeReadOut_WhatGoogleAnsweredIsTold()
    {
        // Answered - in words that do not frame the name the same way.
        Task<IReadOnlyList<string>> Send(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => t.Contains("%%0%%") ? "我叫%%0%%" : "安柏是我的名字").ToList());

        string? told = null;
        var got = await PackTranslationJob.SuggestSpellings(Send, new[] { "Amber" }, "zh-Hans", unread: a => told = a);
        Assert.Empty(got);
        Assert.NotNull(told);
        Assert.Contains("我叫%%0%%", told);
        Assert.Contains("安柏是我的名字", told);

        // And said nothing when a spelling was read.
        told = null;
        Task<IReadOnlyList<string>> Good(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => t.Replace("My name is ", "我的名字是").Replace("Hello, ", "你好，")
                                                                          .Replace("Amber", "安柏")).ToList());
        got = await PackTranslationJob.SuggestSpellings(Good, new[] { "Amber" }, "zh-Hans", unread: a => told = a);
        Assert.Equal("安柏", got["Amber"]);
        Assert.Null(told);
    }
}
