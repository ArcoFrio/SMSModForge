using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;
using Kind = SMSModForge.Shared.TextCheck.Kind;

namespace SMSModForge.Tests;

/// <summary>
/// The translation file: what a person can write in it, what the editor makes
/// of that, and what the check tells them. Everything here is about a file
/// somebody edited by hand, so every case is one a hand could produce.
/// </summary>
public class TranslationFileTests
{
    private readonly ITestOutputHelper _out;
    public TranslationFileTests(ITestOutputHelper output) => _out = output;

    private const string English = """
        # ModForge editor text: English

        [This file]

        language.name = English

        [Quests tab]

        # Button
        quests.addQuest = + Quest

        # Tooltip on "+ Quest"
        quests.addQuest.tip = Add a quest of your own.

        quests.remove = Remove '{name}'?

        menu.file = _File

        quests.changed.one = {count} quest changed
        quests.changed.other = {count} quests changed

        paths.plugin = It lives in BepInEx\plugins.
        paths.lines = First\nSecond
        """;

    private static TextFile En => TextFile.Parse(English);

    // ── Reading ─────────────────────────────────────────────────────────

    [Fact]
    public void ALineIsAKeyAndItsText_NotesAndHeadingsAreForReading()
    {
        var f = En;
        Assert.Empty(f.BadLines);
        Assert.Equal("+ Quest", f.Get("quests.addQuest"));
        Assert.Equal("Quests tab", f.Find("quests.addQuest")!.Heading);
        Assert.Equal(new[] { "Button" }, f.Find("quests.addQuest")!.Notes);
        Assert.Equal(new[] { "ModForge editor text: English" }, f.TopNotes);
        // Case is forgiven: a Replace All can change it.
        Assert.Equal("+ Quest", f.Get("QUESTS.ADDQUEST"));
    }

    [Fact]
    public void BackslashNIsALineBreak_AndEveryOtherBackslashIsItself()
    {
        var f = En;
        Assert.Equal("First\nSecond", f.Get("paths.lines"));
        Assert.Equal(@"It lives in BepInEx\plugins.", f.Get("paths.plugin"));

        foreach (string s in new[] { @"C:\new\folder", "a\nb", @"ends in \", @"two \\ slashes", @"\n literally" })
            Assert.Equal(s, TextFile.Unescape(TextFile.Escape(s)));
    }

    [Fact]
    public void WhatTheReaderCannotReadIsListed_NotGuessedAt()
    {
        var f = TextFile.Parse("good = fine\nno equals sign here\nhas space = x\nhas key = y\ngood = again\n= nothing before");
        Assert.Equal("fine", f.Get("good"));
        var bad = f.BadLines.Select(b => b.Line).ToList();
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, bad.OrderBy(x => x));
        // The second "good" is a duplicate of line 1, and the first one stands.
        Assert.Equal(1, f.BadLines.Single(b => b.Line == 5).DuplicateOf);
    }

    [Fact]
    public void NothingAfterTheEqualsIsNotTranslated_ButTwoQuotesIsNothingOnPurpose()
    {
        var mine = TextFile.Parse("quests.addQuest =\nquests.addQuest.tip = \"\"");
        var t = new Texts(En, mine, "es");
        Assert.Equal("+ Quest", t.T("quests.addQuest"));
        Assert.Equal("", t.T("quests.addQuest.tip"));

        var check = TextCheck.Run(En, mine, "es");
        Assert.Contains(check.Findings, x => x.Kind == Kind.Empty && x.Key == "quests.addQuest");
        Assert.DoesNotContain(check.Findings, x => x.Key == "quests.addQuest.tip");
    }

    // ── Using it ────────────────────────────────────────────────────────

    [Fact]
    public void ATranslationShowsWhereItHasText_AndEnglishEverywhereElse()
    {
        var mine = TextFile.Parse("quests.addQuest = + Misión");
        var t = new Texts(En, mine, "es");
        Assert.Equal("+ Misión", t.T("quests.addQuest"));
        Assert.Equal("Add a quest of your own.", t.T("quests.addQuest.tip"));

        var missing = new List<string>();
        t.Missing = missing.Add;
        Assert.Equal("no.such.key", t.T("no.such.key"));
        Assert.Equal(new[] { "no.such.key" }, missing);
    }

    [Fact]
    public void AKeyOnlyATranslationHasIsNeverShown()
    {
        var mine = TextFile.Parse("invented.key = Hola");
        var t = new Texts(En, mine, "es");
        var missing = new List<string>();
        t.Missing = missing.Add;
        Assert.Equal("invented.key", t.T("invented.key"));
        Assert.Single(missing);
    }

    [Fact]
    public void GapsAreFilledByName_WhereverTheTranslationPutsThem()
    {
        var mine = TextFile.Parse("quests.remove = ¿Quitar «{name}»?");
        var t = new Texts(En, mine, "es");
        Assert.Equal("¿Quitar «Liz»?", t.F("quests.remove", "name", "Liz"));

        var unfilled = new List<string>();
        t.Unfilled = (key, gap) => unfilled.Add(key + ":" + gap);
        Assert.Equal("¿Quitar «{name}»?", t.F("quests.remove"));
        Assert.Equal(new[] { "quests.remove:name" }, unfilled);
    }

    [Fact]
    public void ACountPicksTheFormItsLanguageUses()
    {
        var en = new Texts(En, null, "en");
        Assert.Equal("1 quest changed", en.P("quests.changed", 1));
        Assert.Equal("0 quests changed", en.P("quests.changed", 0));

        var ru = new Texts(En, TextFile.Parse(
            "quests.changed.one = {count} задание изменено\n" +
            "quests.changed.few = {count} задания изменено\n" +
            "quests.changed.many = {count} заданий изменено"), "ru");
        Assert.Equal("1 задание изменено", ru.P("quests.changed", 1));
        Assert.Equal("3 задания изменено", ru.P("quests.changed", 3));
        Assert.Equal("5 заданий изменено", ru.P("quests.changed", 5));
        Assert.Equal("11 заданий изменено", ru.P("quests.changed", 11));
        Assert.Equal("21 задание изменено", ru.P("quests.changed", 21));

        var ja = new Texts(En, TextFile.Parse("quests.changed.other = {count} 件のクエストを変更"), "ja");
        Assert.Equal("1 件のクエストを変更", ja.P("quests.changed", 1));

        // A form the translation lacks falls to its "other", then to English.
        var partial = new Texts(En, TextFile.Parse("quests.changed.other = {count} misiones cambiadas"), "es");
        Assert.Equal("1 misiones cambiadas", partial.P("quests.changed", 1));
        var none = new Texts(En, TextFile.Parse(""), "es");
        Assert.Equal("2 quests changed", none.P("quests.changed", 2));
    }

    [Theory]
    [InlineData("en", 0, "other"), InlineData("en", 1, "one"), InlineData("en", 2, "other")]
    [InlineData("fr", 0, "one"), InlineData("fr", 1, "one"), InlineData("fr", 2, "other")]
    [InlineData("pt-BR", 0, "one"), InlineData("pt-BR", 1, "one"), InlineData("pt-BR", 2, "other")]
    [InlineData("es", 0, "other"), InlineData("de", 1, "one")]
    [InlineData("ru", 1, "one"), InlineData("ru", 2, "few"), InlineData("ru", 5, "many"), InlineData("ru", 11, "many"),
     InlineData("ru", 12, "many"), InlineData("ru", 22, "few"), InlineData("ru", 101, "one"), InlineData("ru", 111, "many")]
    [InlineData("pl", 1, "one"), InlineData("pl", 2, "few"), InlineData("pl", 21, "many"), InlineData("pl", 22, "few")]
    [InlineData("ja", 1, "other"), InlineData("zh-Hans", 1, "other"), InlineData("ko", 2, "other")]
    public void PluralRulesFollowCldrForWholeNumbers(string code, long n, string form)
        => Assert.Equal(form, PluralRules.FormOf(code, n));

    [Fact]
    public void EveryFormALanguageNeedsIsOneItsRuleCanPick()
    {
        foreach (string code in new[] { "en", "fr", "pt-BR", "es", "de", "ru", "pl", "cs", "ja", "zh-Hans", "ko" })
        {
            var picked = Enumerable.Range(0, 300).Select(n => PluralRules.FormOf(code, n)).Distinct().OrderBy(f => f);
            Assert.Equal(PluralRules.FormsFor(code).OrderBy(f => f), picked);
        }
    }

    // ── The fake language ───────────────────────────────────────────────

    [Fact]
    public void TheFakeLanguageMarksEveryText_AndKeepsItsGapsFillable()
    {
        var t = new Texts(En, null, PseudoText.Code);
        string shown = t.F("quests.remove", "name", "Liz");
        _out.WriteLine(shown);
        Assert.True(PseudoText.Marks(shown));
        Assert.Contains("Liz", shown);
        Assert.DoesNotContain("{name}", shown);
        Assert.True(shown.Length > "Remove 'Liz'?".Length + 2, "a third longer, so a clipped text shows");
        Assert.True(PseudoText.Marks(t.P("quests.changed", 2)));
        Assert.Contains("2", t.P("quests.changed", 2));
        // A text that did not come through it has no marks: that is the test.
        Assert.False(PseudoText.Marks("Remove 'Liz'?"));
    }

    // ── The check ───────────────────────────────────────────────────────

    [Fact]
    public void KeysAReplaceAllChangedAreReported_EachWithTheKeyItWas()
    {
        // What a translator does: start a file, then replace a word everywhere -
        // in the keys as well, because Notepad++ does not know which part is which.
        string started = TextFileWriter.Build(En, null, "es", new[] { "Español" });
        var mine = TextFile.Parse(started.Replace("quest", "misión").Replace("Quest", "Misión"));
        var check = TextCheck.Run(En, mine, "es");
        foreach (var f in check.Findings.Where(f => f.Kind == Kind.Unknown)) _out.WriteLine($"{f.Key} -> {f.Suggestion}");

        var guessed = check.Findings.Where(f => f.Kind == Kind.Unknown).ToDictionary(f => f.Key, f => f.Suggestion);
        Assert.Equal(new Dictionary<string, string>
        {
            ["misións.addMisión"] = "quests.addQuest",
            ["misións.addMisión.tip"] = "quests.addQuest.tip",
            ["misións.remove"] = "quests.remove",
            ["misións.changed.one"] = "quests.changed.one",
            ["misións.changed.other"] = "quests.changed.other",
        }, guessed);
        Assert.All(check.Findings.Where(f => f.Kind == Kind.Unknown), f => Assert.True(f.IsError));
    }

    [Fact]
    public void AKeyWithNoIntactNeighbours_IsStillGuessedFromHowItLooks()
    {
        var mine = TextFile.Parse("quests.addQuets = + Misión");
        var unknown = Assert.Single(TextCheck.Run(En, mine, "es").Findings, f => f.Kind == Kind.Unknown);
        Assert.Equal("quests.addQuest", unknown.Suggestion);
    }

    [Fact]
    public void LostGapsTagsAndTokensAreErrors()
    {
        var mine = TextFile.Parse("quests.remove = ¿Quitar «{nombre}»?");
        var codes = Assert.Single(TextCheck.Run(En, mine, "es").Findings, f => f.Kind == Kind.Codes);
        Assert.Equal(new[] { "{name}" }, codes.Lost);
        Assert.Equal(new[] { "{nombre}" }, codes.Added);
        Assert.True(codes.IsError);

        var fine = TextFile.Parse("quests.remove = ¿Quitar «{name}»?");
        Assert.DoesNotContain(TextCheck.Run(En, fine, "es").Findings, f => f.Kind == Kind.Codes);
    }

    [Fact]
    public void TheOneFormMayLeaveTheCountOut_ButTheOtherMayNot()
    {
        var en = TextFile.Parse("q.one = One quest changed\nq.other = {count} quests changed");
        var ok = TextFile.Parse("q.one = Una misión cambiada\nq.other = {count} misiones cambiadas");
        Assert.DoesNotContain(TextCheck.Run(en, ok, "es").Findings, f => f.Kind == Kind.Codes);

        var lost = TextFile.Parse("q.one = Una misión cambiada\nq.other = Misiones cambiadas");
        Assert.Contains(TextCheck.Run(en, lost, "es").Findings, f => f.Kind == Kind.Codes && f.Key == "q.other");
    }

    [Fact]
    public void AFormTheLanguageNeedsIsAskedFor()
    {
        var mine = TextFile.Parse("quests.changed.one = {count} задание\nquests.changed.many = {count} заданий");
        var form = Assert.Single(TextCheck.Run(En, mine, "ru").Findings, f => f.Kind == Kind.MissingForm);
        Assert.Equal("few", form.Suggestion);
    }

    [Fact]
    public void EnglishThatChangedSinceTheTranslation_IsSaidWithWhatItWas()
    {
        var mine = TextFile.Parse("# en: Add a quest.\nquests.addQuest.tip = Añade una misión.");
        var f = Assert.Single(TextCheck.Run(En, mine, "es").Findings, x => x.Kind == Kind.EnglishChanged);
        Assert.Equal("Add a quest.", f.Was);
        Assert.Equal("Add a quest of your own.", f.English);
        Assert.False(f.IsError);
    }

    [Fact]
    public void AMissingAccessKeyAndAnUntranslatedLineAreOnlyNotes()
    {
        var mine = TextFile.Parse("menu.file = Archivo\nquests.addQuest = + Quest");
        var check = TextCheck.Run(En, mine, "es");
        Assert.Contains(check.Findings, f => f.Kind == Kind.AccessKey && f.Key == "menu.file");
        Assert.Contains(check.Findings, f => f.Kind == Kind.Untranslated && f.Key == "quests.addQuest");
        Assert.DoesNotContain(check.Findings, f => f.IsError);
        // The file's own details are not texts to translate.
        Assert.DoesNotContain(TextCheck.Run(En, TextFile.Parse("language.name = English"), "es").Findings,
                              f => f.Kind == Kind.Untranslated);
    }

    [Fact]
    public void TheCountOfTranslatedTextsCountsAPluralOnce()
    {
        var check = TextCheck.Run(En, TextFile.Parse("quests.changed.one = a\nquests.changed.other = b\nquests.addQuest = c"), "es");
        Assert.Equal(8, check.Total);
        Assert.Equal(2, check.Translated);
    }

    // ── Writing ─────────────────────────────────────────────────────────

    [Fact]
    public void ANewTranslationIsTheEnglish_InTheEnglishOrder_WithWhatEachLineWas()
    {
        string written = TextFileWriter.Build(En, null, "ru", new[] { "Русский" });
        _out.WriteLine(written);
        var f = TextFile.Parse(written);
        Assert.Empty(f.BadLines);
        Assert.Equal(new[] { "Русский" }, f.TopNotes);
        Assert.Equal("+ Quest", f.Get("quests.addQuest"));
        Assert.Equal("+ Quest", f.Find("quests.addQuest")!.English);
        Assert.Equal("Quests tab", f.Find("quests.addQuest")!.Heading);
        Assert.Contains("Button", f.Find("quests.addQuest")!.Notes);
        // Russian gets the forms Russian has, each from the English that says the same.
        Assert.Equal("{count} quest changed", f.Get("quests.changed.one"));
        Assert.Equal("{count} quests changed", f.Get("quests.changed.few"));
        Assert.Equal("{count} quests changed", f.Get("quests.changed.many"));
        Assert.False(f.Has("quests.changed.other"));
        Assert.Contains("one: 1, 21, 31, 41", written);
        Assert.Equal(@"It lives in BepInEx\plugins.", f.Get("paths.plugin"));
        Assert.Equal("First\nSecond", f.Get("paths.lines"));
        // It checks clean apart from being untranslated.
        Assert.DoesNotContain(TextCheck.Run(En, f, "ru").Findings, x => x.IsError || x.Kind == Kind.Missing);
    }

    [Fact]
    public void BringingATranslationUpToDate_LosesNothingAnybodyWrote()
    {
        var old = TextFile.Parse("""
            # Hecho por Ana

            # en: + Quest
            quests.addQuest = + Misión

            # en: Add a quest.
            quests.addQuest.tip = Añade una misión.

            # en: Remove '{name}'?
            quests.remove = Remove '{name}'?

            # en: _File
            menu.file = ""

            quests.gone = Esto ya no existe
            """);
        var english = TextFile.Parse(English.Replace("Remove '{name}'?", "Take '{name}' out?"));
        var f = TextFile.Parse(TextFileWriter.Build(english, old, "es", new[] { "unused" }));

        Assert.Equal(new[] { "Hecho por Ana" }, f.TopNotes);
        // Unchanged English: kept as it was.
        Assert.Equal("+ Misión", f.Get("quests.addQuest"));
        Assert.Null(f.Find("quests.addQuest")!.ChangedFrom);
        // Changed English under a translation: kept, and marked.
        Assert.Equal("Añade una misión.", f.Get("quests.addQuest.tip"));
        Assert.Equal("Add a quest.", f.Find("quests.addQuest.tip")!.ChangedFrom);
        Assert.Equal("Add a quest of your own.", f.Find("quests.addQuest.tip")!.English);
        // Never translated: it follows the English.
        Assert.Equal("Take '{name}' out?", f.Get("quests.remove"));
        Assert.Null(f.Find("quests.remove")!.ChangedFrom);
        // Empty on purpose stays empty on purpose.
        Assert.True(f.Find("menu.file")!.Blank);
        // What English no longer has goes to the end, not away.
        Assert.Equal("Esto ya no existe", f.Get("quests.gone"));
        Assert.Equal(TextFileWriter.UnusedHeading, f.Find("quests.gone")!.Heading);

        var review = Assert.Single(TextCheck.Run(english, f, "es").Findings, x => x.Kind == Kind.NeedsReview);
        Assert.Equal("quests.addQuest.tip", review.Key);
    }

    [Fact]
    public void WritingAFileAndReadingItBackChangesNothing()
    {
        var once = TextFileWriter.Build(En, null, "es", new[] { "x" });
        var twice = TextFileWriter.Build(En, TextFile.Parse(once), "es", new[] { "x" });
        Assert.Equal(once, twice);
    }
}
