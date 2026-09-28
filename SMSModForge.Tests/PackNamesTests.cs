using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Character names kept out of a machine translation: never sent, put back
/// exactly where the translator put their marker, in the language's own
/// spelling - and a line that comes back without one, or with one twice, is
/// not written at all.
/// </summary>
public sealed class PackNamesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public PackNamesTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-names-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // ── Finding names ─────────────────────────────────────────────────

    [Fact]
    public void OnlyCapitalisedWholeWordsAreNames_AndTheLongerNameWins()
    {
        var names = new[] { "Nina", "Nurse Nina", "Hope" };
        var found = PackNames.Find("Hope met Nurse Nina; Nina's hopes, Hopeful, hope.", names);
        Assert.Equal(new[] { "Hope", "Nurse Nina", "Nina" }, found.Select(f => f.Name));
        Assert.Equal(0, found[0].At);
        // "Nina's" is Nina; "Hopeful", "hopes" and "hope" are not Hope.
        Assert.Equal("Nina", "Hope met Nurse Nina; Nina's hopes, Hopeful, hope.".Substring(found[2].At, found[2].Length));

        // A name written without a capital is never looked for.
        Assert.Empty(PackNames.Find("kiki waves", new[] { "kiki" }));
    }

    [Fact]
    public void ThePacksCharactersAreNames_AndTheGamesOnlyWhenALineMentionsThem()
    {
        var source = new TextFile();
        source.Add(new TextFile.Entry { Key = "character.hope.name", Text = "Hope" });
        source.Add(new TextFile.Entry { Key = "dialogue.chat.1", Text = "Anna says hi to Hope." });
        source.Add(new TextFile.Entry { Key = "dialogue.chat.2", Text = "The sakura trees are pretty." });

        // Every pack has the player, called "You": the word, not a name.
        source.Add(new TextFile.Entry { Key = "character.player.name", Text = "You" });

        var names = PackNames.Of(source);
        Assert.Contains("Hope", names);
        Assert.Contains("Anna", names);
        Assert.DoesNotContain("You", names);
        // Sakura is one of the game's characters, but only named in lower
        // case here - a tree, not her.
        Assert.DoesNotContain("Sakura", names);
    }

    [Theory]
    [InlineData("ru", true)]
    [InlineData("ja", true)]
    [InlineData("ko", true)]
    [InlineData("zh-Hans", true)]
    [InlineData("de", false)]
    [InlineData("fr", false)]
    [InlineData("pt-BR", false)]
    [InlineData("es", false)]
    public void LanguagesOfAnotherAlphabetSpellNamesAnew(string code, bool spelled)
        => Assert.Equal(spelled, PackNames.NeedsSpelling(code));

    // ── Keeping them through a translation ─────────────────────────────

    [Fact]
    public void ANameGoesBackInTheLanguagesSpelling_ButOneInsideACodeIsPartOfTheCode()
    {
        var masked = ProtectedText.Protect("Hope said {Hope} to <b>Hope</b>.", new[] { "Hope" });
        _out.WriteLine(masked.Text);
        // The {Hope} gap is a code of its own, not a name.
        Assert.Equal(new[] { "Hope", "{Hope}", "<b>", "Hope", "</b>" }, masked.Codes);
        Assert.Equal(new[] { true, false, false, true, false }, masked.IsName);
        Assert.DoesNotContain("Hope", masked.Text);

        var lost = new List<string>();
        string back = ProtectedText.Restore(masked.Text, masked, new Dictionary<string, string> { ["Hope"] = "Хоуп" }, lost);
        Assert.Empty(lost);
        Assert.Equal("Хоуп said {Hope} to <b>Хоуп</b>.", back);
        // No spelling given: as written.
        Assert.Equal("Hope said {Hope} to <b>Hope</b>.", ProtectedText.Restore(masked.Text, masked, null, lost));
    }

    [Fact]
    public void ANameLostOrGivenBackTwiceIsReported()
    {
        var masked = ProtectedText.Protect("Where is Hope?", new[] { "Hope" });
        var lost = new List<string>();
        ProtectedText.Restore("Wo ist sie?", masked, null, lost);
        Assert.Equal(new[] { "Hope" }, lost);

        lost.Clear();
        ProtectedText.Restore("Wo ist %%0%%, %%0%%?", masked, null, lost);
        Assert.Equal(new[] { "Hope" }, lost);
    }

    [Fact]
    public void ASpellingIsReadFromWhereTheNameSitsInTheAnswer()
    {
        var names = new[] { "Hope", "Kiki" };
        var sent = PackNames.SuggestionTexts(names);
        // Two frames with a marker, then each frame with each name.
        Assert.Equal(6, sent.Count);

        var answers = new List<string>
        {
            "Меня зовут %%0%%.", "Привет, %%0%%!",
            "Меня зовут Надежда.", "Привет, Надежда!",   // translated as a word: still Cyrillic, so still suggested - to be checked
            "Меня зовут Кики.", "Привет, Кики!",
        };
        var got = PackNames.ReadSuggestions(names, answers, "ru");
        Assert.Equal("Надежда", got["Hope"]);
        Assert.Equal("Кики", got["Kiki"]);

        // Left in Latin letters, or the sentence came back in another shape:
        // nothing is suggested rather than something wrong.
        answers[4] = "Меня зовут Kiki."; answers[5] = "Кики, привет!";
        Assert.False(PackNames.ReadSuggestions(names, answers, "ru").ContainsKey("Kiki"));
    }

    // ── A whole run ────────────────────────────────────────────────────

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("names.pack");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope" });
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "hope", Text = "Hope is here." });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "hope", Text = "I hope you are well." });
        d.Nodes.Add(new DialogueNodeDef { Id = 3, Actor = "hope", Text = "Hope!" });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);
        return pack;
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task NamesAreNeverSent_AndComeBackAsWrittenOrAsSpelled()
    {
        var pack = Pack();
        var sent = new List<string>();
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent.AddRange(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[" + to + "] " + t).ToList());
        }

        var kept = new PackTranslationJob.KeptNames
        {
            Names = PackNames.Of(PackTranslations.Source(pack)),
            Spellings = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["ru"] = new Dictionary<string, string> { ["Hope"] = "Хоуп" },
            },
        };
        await PackTranslationJob.Run(pack, _root, new[] { "de", "ru" }, Translator, NoWait, keepNames: kept);

        foreach (var s in sent) _out.WriteLine("sent: " + s);
        Assert.DoesNotContain(sent, s => s.Contains("Hope", StringComparison.Ordinal));
        Assert.Contains(sent, s => s.Contains("hope you are well", StringComparison.Ordinal));

        var de = Loc.Read(PackTranslations.PathOf(_root, "de"))!;
        Assert.Equal("Hope", de.Translated("character.hope.name"));
        Assert.True(de.Find("character.hope.name")!.Same);
        Assert.Equal("[de] Hope is here.", de.Translated("dialogue.chat.1"));
        Assert.Equal("Hope!", de.Translated("dialogue.chat.3"));

        var ru = Loc.Read(PackTranslations.PathOf(_root, "ru"))!;
        Assert.Equal("Хоуп", ru.Translated("character.hope.name"));
        Assert.Equal("[ru] Хоуп is here.", ru.Translated("dialogue.chat.1"));
        Assert.Equal("Хоуп!", ru.Translated("dialogue.chat.3"));

        // Done is done: nothing of it is waiting to be translated again.
        Assert.All(PackTranslationJob.StillToTranslate(pack, _root).Where(w => w.Code is "de" or "ru"),
                   w => Assert.Equal(0, w.Missing));
    }

    [Fact]
    public async Task ALineThatLosesItsNameIsNotWritten()
    {
        var pack = Pack();
        Task<IReadOnlyList<string>> Dropping(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[de] " + ProtectedTextStrip(t)).ToList());

        var kept = new PackTranslationJob.KeptNames { Names = new[] { "Hope" } };
        var done = await PackTranslationJob.Run(pack, _root, new[] { "de" }, Dropping, NoWait, keepNames: kept);

        var de = Loc.Read(PackTranslations.PathOf(_root, "de"))!;
        // "Hope is here." came back as "[de]  is here." - not written.
        Assert.NotEqual("[de]  is here.", de.Translated("dialogue.chat.1"));
        Assert.Equal("Hope is here.", de.Translated("dialogue.chat.1"));
        Assert.True(Assert.Single(done).Damaged >= 1);
    }

    private static string ProtectedTextStrip(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"%%\d+%%", "");
}
