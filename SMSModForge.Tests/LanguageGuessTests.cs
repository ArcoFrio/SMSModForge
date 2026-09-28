using System.Collections.Generic;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The guess behind "this does not look like the pack's language", measured on
/// real text rather than on sentences made up to pass.
/// <para/>
/// Where it must never fire: the game's own English dialogue, thousands of
/// lines of exactly what a pack's lines look like, read as an English pack; and
/// each of ModForge's shipped translations read as a pack in its own language.
/// Where it must: the same translations read as an English pack. A wrong
/// warning on somebody's correct line is the failure that matters, so those
/// are held at zero.
/// </summary>
public sealed class LanguageGuessTests
{
    private readonly ITestOutputHelper _out;
    public LanguageGuessTests(ITestOutputHelper o) => _out = o;

    private static List<string> Shipped(string code, bool english = false)
    {
        var file = TextFile.Parse(File.ReadAllText(Path.Combine(Loc.ShippedFolder, code + ".txt")));
        return file.Entries.Select(e => english ? e.English : e.Text)
                   .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
    }

    private static List<string> GamesEnglish()
    {
        var lines = new List<string>();
        foreach (var entry in VanillaDialogueCatalog.All)
        {
            var d = VanillaDialogueCatalog.Open(entry.Token);
            if (d == null) continue;
            lines.AddRange(d.Nodes.Values.Select(n => n.PlainText).Where(t => !string.IsNullOrWhiteSpace(t))!);
        }
        return lines;
    }

    /// <summary>How many of <paramref name="lines"/> read as a pack in
    /// <paramref name="own"/> are said to be in each language (or "?" for
    /// "not the pack's, cannot tell which").</summary>
    private Dictionary<string, int> Measure(string what, IEnumerable<string> lines, string own)
    {
        var said = new Dictionary<string, int>();
        int total = 0;
        var examples = new List<string>();
        foreach (string line in lines)
        {
            total++;
            var found = LanguageGuess.NotIn(line, own);
            if (found == null) continue;
            string key = found.Language ?? "?";
            said[key] = said.GetValueOrDefault(key) + 1;
            if (examples.Count < 8) examples.Add($"  [{key}] {line}");
        }
        _out.WriteLine($"{what} as {own}: {total} lines, " +
                       (said.Count == 0 ? "nothing said" : string.Join(", ", said.Select(s => $"{s.Value} said {s.Key}"))));
        foreach (var e in examples) _out.WriteLine(e);
        return said;
    }

    [Fact]
    public void TheGamesOwnEnglishIsNeverCalledAnythingElse()
    {
        var lines = GamesEnglish();
        Assert.True(lines.Count > 1000, "the game's dialogue should be here: " + lines.Count);
        Assert.Empty(Measure("the game's English", lines, "en"));
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("zh-Hans", "zh-Hans")]
    public void EachShippedTranslationIsItsOwnLanguage(string file, string own)
    {
        Assert.Empty(Measure(file, Shipped(file), own));
    }

    [Fact]
    public void TheEnglishTheyWereMadeFromIsEnglish()
    {
        Assert.Empty(Measure("en (notes)", Shipped("es", english: true), "en"));
    }

    // The floors are what was measured on 2026-09-24, a little under: they
    // are here to catch the guess getting worse, not to promise a rate. It
    // misses about half of the Spanish and Portuguese on purpose - a line has
    // to be plainly in the other language, because the failure that matters
    // is a warning on a correct line, and that is held at zero above.
    [Theory]
    // Spanish, Portuguese and Chinese typed into an English pack. (The Chinese
    // is ModForge's own, full of English names of things - GameObject,
    // MoveGameObject - which is why a third of it is not called Chinese.
    // A line of dialogue has far fewer.)
    [InlineData("es", "en", "es", 0.55)]       // measured 59%
    [InlineData("pt-BR", "en", "pt", 0.45)]    // measured 48%
    [InlineData("zh-Hans", "en", "?", 0.5)]    // measured 58%
    // English typed into a Spanish or Portuguese pack.
    [InlineData("en", "es", "en", 0.75)]       // measured 78%
    [InlineData("en", "pt-BR", "en", 0.75)]    // measured 79%
    // And Spanish and Portuguese, close as they are, kept apart.
    [InlineData("es", "pt-BR", "es", 0.55)]    // measured 60%
    [InlineData("pt-BR", "es", "pt", 0.45)]    // measured 48%
    public void AnotherLanguageIsCaught(string file, string own, string expected, double atLeast)
    {
        var lines = file == "en" ? Shipped("es", english: true) : Shipped(file);
        // Only lines long enough to judge: a two-word label is every language.
        var judgeable = lines.Where(l => LanguageGuess.Plain(l).Split(' ', System.StringSplitOptions.RemoveEmptyEntries).Length >= 8).ToList();
        var said = Measure(file + " (8+ words)", judgeable, own);

        double rate = said.GetValueOrDefault(expected) / (double)judgeable.Count;
        _out.WriteLine($"caught: {rate:P0}");
        Assert.True(rate >= atLeast, $"{rate:P0} of {judgeable.Count} said {expected}");

        // Never the wrong one of the three.
        foreach (var other in new[] { "en", "es", "pt" }.Where(o => o != expected && o != Shared.PluralRules.LanguageOf(own)))
            Assert.Equal(0, said.GetValueOrDefault(other));
    }

    [Theory]
    [InlineData("こんにちは、今日は元気ですか？", "en", "ja")]
    [InlineData("안녕하세요, 오늘 기분이 어때요?", "en", "ko")]
    [InlineData("Привет, как у тебя дела сегодня?", "en", null)]
    [InlineData("今日は元気ですか、また明日", "zh-Hans", "ja")]      // kana in Chinese is Japanese
    [InlineData("Hello there, how are you doing today?", "ja", "en")]
    public void AnotherAlphabetIsCertain(string text, string own, string? expected)
    {
        var found = LanguageGuess.NotIn(text, own);
        Assert.NotNull(found);
        Assert.Equal(expected, found!.Language);
    }

    [Theory]
    [InlineData("OK!", "ja")]                                   // too short to be anything
    [InlineData("今日は元気ですか", "ja")]
    [InlineData("你好，你今天怎么样？", "zh-Hans")]
    [InlineData("Bonjour, comment allez-vous aujourd'hui ?", "fr")]  // a language not guessed within
    [InlineData("Hello there, <b>{name}</b>, [PV:shells] shells!", "en")]
    public void TheRightAlphabetOrTooLittleSaysNothing(string text, string own)
    {
        Assert.Null(LanguageGuess.NotIn(text, own));
    }
}
