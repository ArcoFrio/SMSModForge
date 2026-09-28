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
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The game's own characters' names in other alphabets: spelled as they sound,
/// in the right letters, and only for names - a description the game files a
/// character under is a word, and is translated.
/// </summary>
public sealed class CastNamesTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public CastNamesTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-cast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    [Fact]
    public void EveryNameIsOneOfTheGames_AndEverySpellingIsInItsLanguagesOwnLetters()
    {
        var cast = VanillaCastData.Busts.Select(b => VanillaCastData.SpokenName(b.Character)).Distinct().ToList();
        var letters = new Dictionary<string, LanguageGuess.Script[]>
        {
            ["ru"] = new[] { LanguageGuess.Script.Cyrillic },
            ["ja"] = new[] { LanguageGuess.Script.Kana },
            ["ko"] = new[] { LanguageGuess.Script.Hangul },
            ["zh-Hans"] = new[] { LanguageGuess.Script.Han },
        };

        // Named by the game's dialogue rather than by a bust: Doctor Frost is
        // Doctor Evelyn Frost (see VanillaCastData's notes).
        cast.Add("Doctor Evelyn Frost");

        foreach (string name in CastNames.All)
        {
            // A name the game has, whole - "Nina" of "Nurse Nina".
            Assert.True(cast.Any(c => PackNames.Find(c, new[] { name }).Count > 0), name + " is not one of the game's");

            foreach (var language in letters)
            {
                // Every one of them spelled: none is left for the author.
                string? spelled = CastNames.In(language.Key, name);
                Assert.True(spelled != null, $"{name} has no spelling in {language.Key}");
                var used = spelled.Where(char.IsLetter).Select(LanguageGuess.ScriptOf).Distinct().ToList();
                Assert.True(used.Count > 0 && used.All(s => language.Value.Contains(s)),
                            $"{name} in {language.Key} is '{spelled}', not only {string.Join("/", language.Value)}");
            }
        }

        // Latin-alphabet languages keep it as written: no spelling at all.
        Assert.Null(CastNames.In("de", "Anna"));
        Assert.Equal("Анна", CastNames.In("ru", "Anna"));
        // Traditional Chinese is not simplified Chinese.
        Assert.Null(CastNames.In("zh-Hant", "Anna"));
    }

    [Fact]
    public void ADescriptionIsAWord_NotAName()
    {
        foreach (string word in new[] { "Android", "Master", "Technician", "Ghost", "Park Woman", "Mobster 1", "The Bouncer" })
            Assert.False(CastNames.Has(word), word);

        var source = new TextFile();
        source.Add(new TextFile.Entry { Key = "dialogue.chat.1", Text = "Android units, report. The Technician is with Anna." });
        var names = PackNames.Of(source);
        Assert.Equal(new[] { "Anna" }, names);
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    [Fact]
    public async Task WithNamesTranslated_TheGamesAreStillKeptAndSpelled_AndThePacksAreTranslated()
    {
        var pack = PackRepository.CreateEmpty("cast.pack");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope" });
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "hope", Text = "Hope meets Anna." });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        var sent = new List<string>();
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent.AddRange(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[" + to + "] " + t).ToList());
        }

        // What the Translate window hands over with "translate names" ticked:
        // the game's names only.
        var kept = new PackTranslationJob.KeptNames
        {
            Names = new[] { "Anna" },
            Spellings = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["ru"] = new Dictionary<string, string> { ["Anna"] = CastNames.In("ru", "Anna")! },
            },
        };
        await PackTranslationJob.Run(pack, _root, new[] { "ru" }, Translator, NoWait, keepNames: kept);

        foreach (var s in sent) _out.WriteLine("sent: " + s);
        Assert.DoesNotContain(sent, s => s.Contains("Anna", StringComparison.Ordinal));
        Assert.Contains(sent, s => s.Contains("Hope", StringComparison.Ordinal));

        var ru = Loc.Read(PackTranslations.PathOf(_root, "ru"))!;
        Assert.Equal("[ru] Hope", ru.Translated("character.hope.name"));
        Assert.Equal("[ru] Hope meets Анна.", ru.Translated("dialogue.chat.1"));
    }
}
