using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// A character's pronouns: chosen by the author for the pack's own, the
/// game's for the player and the game's cast, reported until chosen, and
/// told to the translator for the languages whose words change with them.
/// </summary>
public sealed class PronounsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public PronounsTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-pronouns-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    /// <summary>A translator into Russian as the website behaves: the words
    /// changed, every marker left where it was - so a speaker's hint stays
    /// first, and "[ru]" comes after it.</summary>
    private static string Ru(string t)
    {
        t = t.Replace("She says:", "Она говорит:").Replace("He says:", "Он говорит:")
             .Replace("(she)", "(она)").Replace("(he)", "(он)");
        const string close = "%%9502%% ";
        return t.StartsWith("%%9501%%") ? t.Insert(t.IndexOf(close) + close.Length, "[ru] ") : "[ru] " + t;
    }

    // ── Saved ─────────────────────────────────────────────────────────

    [Fact]
    public void ChosenPronounsAreSaved_UnsetOnesAreNot_AndAnOlderPackLoadsWithNone()
    {
        var pack = PackRepository.CreateEmpty("pronouns.pack");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope", Pronouns = Pronouns.Female });
        pack.Characters.Add(new CharacterDef { Key = "ash", DisplayName = "Ash" });
        PackRepository.Save(pack, _root);

        var saved = JObject.Parse(File.ReadAllText(Path.Combine(_root, PackRepository.ManifestFileName)));
        var chars = saved["characters"]!.Children<JObject>().ToDictionary(c => (string)c["key"]!);
        Assert.Equal("female", (string?)chars["hope"]["pronouns"]);
        Assert.Null(chars["ash"]["pronouns"]);

        var loaded = PackRepository.Load(_root);
        Assert.Equal(Pronouns.Female, loaded.Characters.Single(c => c.Key == "hope").Pronouns);
        // Written before the field existed: none, to be chosen.
        Assert.Equal(Pronouns.Unset, loaded.Characters.Single(c => c.Key == "ash").Pronouns);
    }

    [Theory]
    [InlineData("Male", Pronouns.Male)]
    [InlineData("NEUTRAL", Pronouns.Neutral)]
    [InlineData("they", Pronouns.Unset)]
    [InlineData("7", Pronouns.Unset)]
    public void AValueItDoesNotKnowLoadsAsUnset_NeverFailsThePack(string written, Pronouns expected)
    {
        var pack = PackRepository.CreateEmpty("pronouns.odd");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope" });
        PackRepository.Save(pack, _root);
        string path = Path.Combine(_root, PackRepository.ManifestFileName);
        var json = JObject.Parse(File.ReadAllText(path));
        ((JObject)json["characters"]!.First(c => (string?)c["key"] == "hope"))["pronouns"] = written;
        File.WriteAllText(path, json.ToString());

        Assert.Equal(expected, PackRepository.Load(_root).Characters.Single(c => c.Key == "hope").Pronouns);
    }

    [Fact]
    public void ThePlayerAndTheGamesCharactersHaveTheGames_AndNoneIsWritten()
    {
        var player = CharacterDef.NewPlayer();
        Assert.Equal(Pronouns.Male, player.EffectivePronouns);

        // A colour of the pack's own, so the entry is saved at all.
        var anna = new CharacterDef { Key = "anna", DisplayName = "Anna", VanillaCharacter = "Anna",
                                      BustSource = BustSource.Vanilla, NameColor = "#FF0000", Pronouns = Pronouns.Male };
        // The game's answer, whatever the field says.
        Assert.Equal(Pronouns.Female, anna.EffectivePronouns);

        var pack = PackRepository.CreateEmpty("pronouns.cast");
        pack.Characters.Add(anna);
        string written = PackRepository.SerializeAsSaved(pack);
        Assert.Contains("#FF0000", written);
        Assert.DoesNotContain("\"pronouns\"", written);
    }

    // ── Reported ──────────────────────────────────────────────────────

    [Fact]
    public void ACharacterOfThePacksOwnWithNoPronounsIsAnError_ChosenOrTheGamesIsNot()
    {
        var pack = PackRepository.CreateEmpty("pronouns.check");
        pack.Characters.Add(new CharacterDef { Key = "ash", DisplayName = "Ash", BustSource = BustSource.None });
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope", BustSource = BustSource.None, Pronouns = Pronouns.Neutral });
        pack.Characters.Add(new CharacterDef { Key = "anna", DisplayName = "Anna", VanillaCharacter = "Anna", BustSource = BustSource.Vanilla });

        var issues = PackValidator.Validate(pack, "").Where(i => i.Code == "character.pronounsMissing").ToList();
        var one = Assert.Single(issues);
        Assert.Equal(Severity.Error, one.Severity);
        Assert.Contains("Ash", one.Message);
        Assert.Contains("ash", one.Where);
    }

    // ── The game's cast ───────────────────────────────────────────────

    [Fact]
    public void EveryOneOfTheGamesCharactersHasPronouns_AndTheTableNamesNobodyElse()
    {
        var cast = VanillaCharacters.All.Select(c => c.Name).ToList();
        var missing = cast.Where(n => VanillaPronouns.Of(n) == Pronouns.Unset).ToList();
        Assert.True(missing.Count == 0, "No pronouns for: " + string.Join(", ", missing));

        var strangers = VanillaPronouns.Named.Where(n => !cast.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.True(strangers.Count == 0, "Not in the cast: " + string.Join(", ", strangers));

        _out.WriteLine($"male {cast.Count(n => VanillaPronouns.Of(n) == Pronouns.Male)}, "
                       + $"female {cast.Count(n => VanillaPronouns.Of(n) == Pronouns.Female)}, "
                       + $"neutral {cast.Count(n => VanillaPronouns.Of(n) == Pronouns.Neutral)}");
    }

    [Theory]
    [InlineData("Anna", Pronouns.Female)]
    [InlineData("masterzhen", Pronouns.Male)]          // by key
    [InlineData("DrFrost", Pronouns.Female)]           // as the dialogue files spell her
    [InlineData("HimariDad", Pronouns.Male)]
    [InlineData("Subject IX-Delta", Pronouns.Neutral)]
    [InlineData("Nobody At All", Pronouns.Unset)]
    public void TheGamesCharacterByAnyOfTheirNames(string name, Pronouns expected)
        => Assert.Equal(expected, VanillaPronouns.Of(name));

    [Theory]
    [InlineData("Nina", Pronouns.Female)]      // Nurse Nina
    [InlineData("Zhen", Pronouns.Male)]        // Master Zhen
    [InlineData("Evelyn", Pronouns.Female)]    // Doctor Evelyn Frost
    [InlineData("Kimura", Pronouns.Unset)]     // Mr. and Mrs. Kimura: two people, no answer
    public void AGivenNameInALineIsWhoeverItMeans(string name, Pronouns expected)
        => Assert.Equal(expected, VanillaPronouns.OfGivenName(name));

    // ── Told to the translator ────────────────────────────────────────

    private static GenderHints.Who Hope(Pronouns speaker = Pronouns.Female)
        => new()
        {
            Speakers = new Dictionary<string, Pronouns> { ["line"] = speaker },
            Names = new Dictionary<string, Pronouns> { ["Hope"] = Pronouns.Female, ["Sam"] = Pronouns.Neutral },
        };

    [Fact]
    public void TheSpeakerGoesInFront_AndEachNameWithPronounsGetsItsOwn()
    {
        var words = GenderHints.WordsFor("en")!;
        var masked = ProtectedText.Protect("I told Hope and Sam.", new[] { "Hope", "Sam" });
        string sent = GenderHints.Add(masked.Text, masked, Pronouns.Female, Hope(), words);
        Assert.Equal("%%9501%% She says: %%9502%% I told %%0%% %%9501%% (she) %%9502%% and %%1%%.", sent);

        // Neutral and unchosen are not hinted at all.
        Assert.Equal("I told %%0%% %%9501%% (she) %%9502%% and %%1%%.",
                     GenderHints.Add(masked.Text, masked, Pronouns.Unset, Hope(), words));
    }

    [Fact]
    public void OnlyLanguagesWhoseWordsChangeGetHints_FromALanguageThereAreHintsIn()
    {
        Assert.True(GenderHints.Needed("ru"));
        Assert.True(GenderHints.Needed("pt-BR"));
        Assert.True(GenderHints.Needed("de"));
        Assert.False(GenderHints.Needed("ja"));
        Assert.False(GenderHints.Needed("zh-Hans"));
        Assert.False(GenderHints.Needed("en"));
        Assert.NotNull(GenderHints.WordsFor("en-GB"));
        Assert.NotNull(GenderHints.WordsFor("pt-BR"));
        Assert.Null(GenderHints.WordsFor("ja"));
    }

    /// <summary>What the Google Translate website answered, 2026-09-27.</summary>
    [Theory]
    [InlineData("%%9501%% She says: %%9502%% I'm tired, but I'm ready.",
                "%%9501%% Ella dice: %%9502%% Estoy cansada, pero estoy lista.",
                "Estoy cansada, pero estoy lista.")]
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired, but ready.",
                "%%0%% %%9501%% (она) %%9502%% устала, но готова.",
                "%%0%% устала, но готова.")]
    [InlineData("Did %%0%% %%9501%% (she) %%9502%% go home already?",
                "¿%%0%% %%9501%% (ella) %%9502%% ya se fue a casa?",
                "¿%%0%% ya se fue a casa?")]
    [InlineData("%%9501%% She says: %%9502%% I saw %%0%% %%9501%% (she) %%9502%% yesterday; she looked exhausted.",
                "%%9501%% Она говорит: %%9502%% Я видела %%0%% %%9501%% (она) %%9502%% вчера; она выглядела измученной.",
                "Я видела %%0%% вчера; она выглядела измученной.")]
    [InlineData("%%0%% %%9501%% (she) %%9502%% is my best friend.",
                "%%0%% %%9501%%(ella)%%9502%%, es mi mejor amiga.",
                "%%0%%, es mi mejor amiga.")]
    public void TheHintsComeOutOfTheAnswer_AndNothingOfThemIsLeft(string sent, string answer, string expected)
        => Assert.Equal(expected, GenderHints.Remove(answer, sent));

    [Theory]
    // A marker lost.
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired.", "%%0%% (она) %%9502%% устала.")]
    // The hint's words moved out, leaving its markers empty.
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired.", "%%0%% (она) %%9501%% %%9502%% устала.")]
    // The line's own words inside the markers.
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired.", "%%0%% %%9501%% (она) устала, но готова ко всему, что будет %%9502%%.")]
    // The speaker's hint no longer first.
    [InlineData("%%9501%% She says: %%9502%% I'm ready.", "Я готова. %%9501%% Она говорит: %%9502%%")]
    // A name's hint no longer after a name.
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired.", "%%9501%% (она) %%9502%% %%0%% устала.")]
    // A code inside the hint.
    [InlineData("%%0%% %%9501%% (she) %%9502%% is tired.", "%%9501%% %%0%% (она) %%9502%% устала.")]
    public void AHintThatCannotBeTakenOutForCertainRefusesTheLine(string sent, string answer)
        => Assert.Null(GenderHints.Remove(answer, sent));

    [Fact]
    public async Task ARunIntoRussianSendsTheHints_WritesNoneOfThem_AndAsksAgainWhereOneCameBackWrong()
    {
        var sent = new List<string>();
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent.AddRange(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t =>
                // One line's hint comes back mangled the first time.
                t.Contains("broken") && t.Contains("9501") ? t.Replace("%%9502%%", "") : Ru(t)).ToList());
        }

        var lines = new List<TranslationRun.Line>
        {
            new("line", "Hope is ready."),
            new("broken", "Hope is broken."),
        };
        var who = new GenderHints.Who
        {
            Speakers = new Dictionary<string, Pronouns> { ["line"] = Pronouns.Female, ["broken"] = Pronouns.Male },
            Names = new Dictionary<string, Pronouns> { ["Hope"] = Pronouns.Female },
        };
        var run = new TranslationRun(Translator, NoWait);
        var results = await run.Go(lines, "en", "ru", names: new[] { "Hope" }, who: who);

        foreach (var s in sent) _out.WriteLine("sent: " + s);
        Assert.Contains(sent, s => s.Contains("She says:") && s.Contains("(she)"));
        Assert.All(results, r => Assert.True(r.Usable, r.Trouble));
        Assert.All(results, r => Assert.DoesNotContain("9501", r.Text));
        Assert.All(results, r => Assert.DoesNotContain("says", r.Text));
        Assert.Equal("[ru] Hope is ready.", results.Single(r => r.Key == "line").Text);
        // Its hints came out: it was not asked again.
        Assert.DoesNotContain(sent, s => s == "%%0%% is ready.");
        // Asked again, without its hints.
        Assert.Contains(sent, s => s == "%%0%% is broken.");
        Assert.Equal("[ru] Hope is broken.", results.Single(r => r.Key == "broken").Text);
    }

    [Fact]
    public async Task ARunIntoJapaneseSendsNoHints()
    {
        var sent = new List<string>();
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent.AddRange(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[ja] " + t).ToList());
        }
        var who = new GenderHints.Who { Speakers = new Dictionary<string, Pronouns> { ["line"] = Pronouns.Female } };
        await new TranslationRun(Translator, NoWait).Go(new[] { new TranslationRun.Line("line", "I'm ready.") }, "en", "ja", who: who);
        Assert.Equal(new[] { "I'm ready." }, sent);
    }

    [Fact]
    public async Task APacksLinesGoOutWithTheirSpeakersAndNames_AndTheFileHasNeither()
    {
        var pack = PackRepository.CreateEmpty("pronouns.run");
        pack.Characters.Add(new CharacterDef { Key = "hope", DisplayName = "Hope", BustSource = BustSource.None, Pronouns = Pronouns.Female });
        pack.Characters.Add(new CharacterDef { Key = "ash", DisplayName = "Ash", BustSource = BustSource.None, Pronouns = Pronouns.Male });
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Actor = "hope", Text = "I'm ready, Ash." });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Actor = "ash", Text = "Anna is waiting." });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        var sent = new List<string>();
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent.AddRange(texts);
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(Ru).ToList());
        }
        var kept = new PackTranslationJob.KeptNames { Names = new[] { "Hope", "Ash", "Anna" } };
        await PackTranslationJob.Run(pack, _root, new[] { "ru" }, Translator, NoWait, keepNames: kept);

        foreach (var s in sent) _out.WriteLine("sent: " + s);
        Assert.Contains(sent, s => s.StartsWith("%%9501%% She says: %%9502%%") && s.Contains("%%9501%% (he) %%9502%%"));
        // Anna is the game's: female, from the game.
        Assert.Contains(sent, s => s.StartsWith("%%9501%% He says: %%9502%%") && s.Contains("%%9501%% (she) %%9502%%"));

        var ru = Loc.Read(PackTranslations.PathOf(_root, "ru"))!;
        Assert.Equal("[ru] I'm ready, Ash.", ru.Translated("dialogue.chat.1"));
        Assert.Equal("[ru] Anna is waiting.", ru.Translated("dialogue.chat.2"));
    }
}
