using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;
using C = SMSModForge.Shared.SaveLoadChecks;

namespace SMSModForge.Tests;

/// <summary>
/// What the plugin shows players - the pack list's tags and the save warning
/// - comes through the translation, every word of it, and a translation's
/// words are the ones that show.
/// </summary>
public class GameTextsTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    public GameTextsTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => GameTexts.Use(null!, null!);

    /// <summary>Every sentence the save warning and the pack list can say,
    /// with every branch taken.</summary>
    private static List<string> EverythingShown()
    {
        var shown = new List<string>();
        void Add(SaveWarningText? text)
        {
            Assert.NotNull(text);
            shown.Add(text!.Title);
            shown.AddRange(text.Paragraphs);
            foreach (var section in text.Details) { shown.Add(section.Heading); shown.AddRange(section.Items); }
            shown.AddRange(text.After);
        }

        var all = new Dictionary<string, List<string>>
        {
            [C.Dialogues] = new() { C.ConversationsPart, C.QuestStartsPart, C.ConversationPlaysPart },
        };
        var one = new C.PackChanges("Alpha", new[] { C.Quests, C.Dialogues }, all);
        var older = new C.PackChanges("Beta", new[] { C.Dialogues }) { SaveBeforeMarks = true };
        var quests = Enum.GetValues(typeof(QuestTreeEdits.SaveProgress)).Cast<QuestTreeEdits.SaveProgress>()
                         .Select((p, i) => new C.ChangedQuest("Quest " + i, p)).ToList();

        Add(C.Warning(new[] { one }, new[] { "Gamma" }, quests));
        Add(C.Warning(new[] { older }, null!, quests.Take(1).ToList()));
        Add(C.Warning(new[] { one, older }, new[] { "Gamma", "Delta" }));
        Add(C.Warning(new[] { one, new C.PackChanges("Epsilon", new[] { C.Quests }) }, null!));
        Add(C.Warning(new List<C.PackChanges>(), new[] { "Gamma" }));
        // A pack for each reason one is not running, each under its heading.
        Add(C.WarningFor(new List<C.PackChanges>(), new List<C.AbsentPack>
        {
            new("Beta", "1.0.0", SaveRecord.SwitchedOff),
            new("Delta", "", SaveRecord.NotLoaded),
            new("Gamma", "", SaveRecord.NotInstalled),
        }));
        shown.Add(SaveWarningText.ContinueLabel);
        shown.Add(SaveWarningText.ReturnLabel);

        // Choosing a language on the main menu, and the warning after it, with
        // a pack partly translated and one not at all.
        var shortPacks = new List<LanguageChoice.Coverage>
        {
            new() { Pack = "Alpha", HasTranslation = true, Translated = 1, Total = 4 },
            new() { Pack = "Beta", Total = 3 },
        };
        Add(LanguageChoice.Warning("Español", shortPacks));
        Add(LanguageChoice.Warning("Español", shortPacks.Take(1).ToList()));
        shown.Add(GameTexts.F("game.language.keep", "language", "Español"));
        shown.Add(GameTexts.F("game.language.back", "language", "English"));
        shown.Add(GameTexts.T("game.language.flagsHeading"));

        // The notice about the game's own lines, for one pack and for two.
        Add(GameLineNotice.Notice(new[] { "Alpha" }, "Español"));
        Add(GameLineNotice.Notice(new[] { "Alpha", "Beta" }, "Español"));

        foreach (var facts in new[]
                 {
                     new PackStatus.Facts { Readable = false },
                     // Two entries, because a game-version mismatch now
                     // replaces the ModForge complaint rather than sitting
                     // beside it - so one pack can no longer show both tags.
                     new PackStatus.Facts { GameVersion = "1.7A", RunningGameVersion = "1.8E" },
                     new PackStatus.Facts { ForgeVersion = "99.0.0" },
                     new PackStatus.Facts { ForgeVersion = "0.1.0", Folder = "Mods", ShadowedIn = "BepInEx/plugins" },
                     new PackStatus.Facts { ForgeVersion = "" },
                     // Translated, but not into what the player is reading.
                     new PackStatus.Facts
                     {
                         Language = "es",
                         Translations = new List<string> { "fr" },
                     },
                 })
            shown.AddRange(PackStatus.Of(facts).Tags);
        return shown;
    }

    [Fact]
    public void EveryWordThePluginShowsPlayersComesThroughTheTranslation()
    {
        var missing = new List<string>();
        var pseudo = new Texts(GameTexts.English, null, PseudoText.Code) { Missing = missing.Add };
        GameTexts.Use(pseudo);
        var shown = EverythingShown();
        foreach (string s in shown) _out.WriteLine(s);

        Assert.Empty(missing);
        Assert.True(shown.Count > 20, shown.Count + " texts");
        Assert.All(shown, s => Assert.True(PseudoText.Marks(s), "not through the translation: " + s));
    }

    [Fact]
    public void TheEnglishTheGameShows_IsTheEditorsEnglishFile()
    {
        // Built into both under one name; the tests see the editor's copy,
        // which is the same file the plugin project embeds.
        Assert.Equal(Loc.English.Entries.Count, GameTexts.English.Entries.Count);
        Assert.Equal("Before you continue", GameTexts.T("game.save.title"));
        Assert.All(GameTexts.English.Entries.Where(e => e.Key.StartsWith("game.", StringComparison.Ordinal)),
                   e => Assert.Equal("In the game", e.Heading));
    }

    [Fact]
    public void ATranslationsWordsAreTheOnesThatShow_AndItsOwnQuotesAndLists()
    {
        var es = TextFile.Parse(
            "game.quoted = «{name}»\n" +
            "common.listAnd = {first} y {second}\n" +
            "game.save.title = Antes de continuar\n" +
            "game.save.missing.other = Esta partida tiene datos de paquetes que ahora no están en marcha.\n" +
            "game.save.notInstalled = No instalados:\n" +
            "game.pack.needsForge = Necesita ModForge {version}\n");
        GameTexts.Use(es, "es");

        var text = C.Warning(new List<C.PackChanges>(), new[] { "Gamma", "Delta" })!;
        Assert.Equal("Antes de continuar", text.Title);
        Assert.Equal("Esta partida tiene datos de paquetes que ahora no están en marcha.", Assert.Single(text.Paragraphs));
        var listed = Assert.Single(text.Details);
        Assert.Equal("No instalados:", listed.Heading);
        Assert.Equal(new[] { "«Gamma»", "«Delta»" }, listed.Items);
        Assert.Equal("«Gamma» y «Delta»", GameTexts.JoinAnd(new List<string> { "«Gamma»", "«Delta»" }));
        Assert.Equal("Necesita ModForge 9.0.0",
                     Assert.Single(PackStatus.Of(new PackStatus.Facts { ForgeVersion = "9.0.0", RuntimeForgeVersion = "1.0.0" }).Tags));
        // What it has not translated is English, not a key.
        Assert.Equal("Continue", SaveWarningText.ContinueLabel);
    }
}
