using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using SMSModForge.Validation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The language a pack is written in, and a text that has words only in one of
/// its translations.
/// <para/>
/// Both decided by the author (2026-09-24): a pack may be written in any
/// language, and a line typed while looking at a translation belongs to that
/// translation - the pack's own words for it stay empty, the pack's check says
/// so, and in game it is never blank.
/// </summary>
public sealed class PackLanguageTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForgePackLanguage", Guid.NewGuid().ToString("N"));
    private string PackRoot => Path.Combine(_root, "pack");

    public PackLanguageTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(PackRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static JObject Manifest(string json) => JObject.Parse(json);

    /// <summary>Two buttons to one level, the first of them empty.</summary>
    private const string TwoButtons = @"{
        ""places"": [ { ""key"": ""cove"", ""navigatorButtons"": [
            { ""target"": ""vanilla:Beach"", ""label"": """" },
            { ""target"": ""vanilla:Beach"", ""label"": ""Back to the beach"" },
            { ""target"": ""vanilla:Beach"", ""label"": ""The other way"" } ] } ],
        ""dialogues"": [ { ""key"": ""beach"", ""nodes"": [
            { ""id"": 1, ""text"": ""Hello there!"" },
            { ""id"": 2, ""text"": """" } ] } ]
    }";

    private static TextFile File(params (string Key, string Text, string? English)[] entries)
    {
        var file = new TextFile();
        foreach (var (key, text, english) in entries)
            file.Add(new TextFile.Entry { Key = key, Text = text, English = english });
        return file;
    }

    // ── Keys ─────────────────────────────────────────────────────────

    [Fact]
    public void ListingTheEmptyTextsMovesNoOtherKey()
    {
        var without = PackTexts.Of(Manifest(TwoButtons));
        var with = PackTexts.Of(Manifest(TwoButtons), withEmpty: true);
        foreach (var s in with) _out.WriteLine($"{s.Key}  '{s.Text}'");

        // Every key a text with words had, it still has.
        foreach (var site in without)
            Assert.Contains(with, w => w.Key == site.Key && w.Text == site.Text);

        // The empty button comes last in the numbering, not first.
        Assert.Equal("navigator.cove.vanilla_Beach-3.label", with.Single(s => s.Kind == PackTexts.Kind.NavigatorLabel && s.Text == "").Key);
        Assert.Equal("navigator.cove.vanilla_Beach.label", with.Single(s => s.Text == "Back to the beach").Key);
        Assert.Contains(with, s => s.Key == "dialogue.beach.2");

        // And not asked for, not listed - what every existing caller gets.
        Assert.Equal(without.Count + 2, with.Count);
        Assert.DoesNotContain(without, s => PackTexts.IsEmpty(s.Text));
    }

    // ── In game ──────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyTextTakesThePlayersLanguage()
    {
        var manifest = Manifest(TwoButtons);
        var spanish = File(("dialogue.beach.2", "Una línea nueva.", ""),
                           ("dialogue.beach.1", "¡Hola!", "Hello there!"));

        var applied = PackTexts.Apply(manifest, spanish);

        Assert.Equal("Una línea nueva.", (string?)manifest.SelectToken("dialogues[0].nodes[1].text"));
        Assert.Equal("¡Hola!", (string?)manifest.SelectToken("dialogues[0].nodes[0].text"));
        Assert.Equal(2, applied.Translated);
        Assert.Empty(applied.Borrowed);
    }

    [Fact]
    public void AnEmptyTextTheLanguageLacksBorrowsAnotherTranslationsWords()
    {
        // Never blank. A player reading French gets the Spanish words of a line
        // typed only in Spanish - worse than French, better than nothing.
        var manifest = Manifest(TwoButtons);
        var french = File(("dialogue.beach.1", "Salut !", "Hello there!"));
        var spanish = File(("dialogue.beach.2", "Una línea nueva.", ""));
        var german = File(("dialogue.beach.2", "Eine neue Zeile.", ""));
        int asked = 0;

        var applied = PackTexts.Apply(manifest, french, () => { asked++; return new[] { german, spanish }; });

        // In the order given - the plugin gives them by code.
        Assert.Equal("Eine neue Zeile.", (string?)manifest.SelectToken("dialogues[0].nodes[1].text"));
        Assert.Equal("Salut !", (string?)manifest.SelectToken("dialogues[0].nodes[0].text"));
        Assert.Equal(new[] { "dialogue.beach.2" }, applied.Borrowed);
        Assert.Equal(1, asked);   // read once, not once per text
    }

    [Fact]
    public void APackPlayedInItsOwnWordsStillFillsItsEmptyTexts()
    {
        var manifest = Manifest(TwoButtons);
        var spanish = File(("dialogue.beach.2", "Una línea nueva.", ""));

        PackTexts.Apply(manifest, null, () => new[] { spanish });

        Assert.Equal("Una línea nueva.", (string?)manifest.SelectToken("dialogues[0].nodes[1].text"));
        Assert.Equal("Hello there!", (string?)manifest.SelectToken("dialogues[0].nodes[0].text"));
    }

    [Fact]
    public void NoTranslationsMeansNothingIsAskedFor()
    {
        // The control: an empty text nobody wrote anywhere stays empty, and a
        // pack with no empty texts never reads its other files at all.
        var manifest = Manifest(@"{ ""dialogues"": [ { ""key"": ""d"", ""nodes"": [ { ""id"": 1, ""text"": ""Hi."" } ] } ] }");
        bool asked = false;
        PackTexts.Apply(manifest, null, () => { asked = true; return new List<TextFile>(); });
        Assert.False(asked);

        var empty = Manifest(TwoButtons);
        PackTexts.Apply(empty, null, () => new List<TextFile>());
        Assert.Equal("", (string?)empty.SelectToken("dialogues[0].nodes[1].text"));
    }

    [Fact]
    public void AWordEmptiedByThePackIsNotRefilledFromAnOldTranslation()
    {
        // The Spanish was made from words the pack has since deleted. The
        // author emptied the line; putting old Spanish back would undo that.
        var manifest = Manifest(TwoButtons);
        var spanish = File(("dialogue.beach.2", "Una línea vieja.", "An old line."));

        PackTexts.Apply(manifest, spanish, () => new[] { spanish });

        Assert.Equal("", (string?)manifest.SelectToken("dialogues[0].nodes[1].text"));
    }

    [Theory]
    // An English pack, as every pack before this was.
    [InlineData("es-MX", "en", "es,fr", "es")]
    [InlineData("en", "en", "es,fr", null)]
    [InlineData("de", "en", "es,fr", null)]
    // A Brazilian pack read by a Brazilian: its own words, not the pt-PT file.
    [InlineData("pt-BR", "pt-BR", "pt-PT,en", null)]
    // ...and by somebody reading English: the English translation.
    [InlineData("en-GB", "pt-BR", "pt-PT,en", "en")]
    // ...and by a Portuguese reader: the closer file still wins over the pack's own.
    [InlineData("pt-PT", "pt-BR", "pt-PT,en", "pt-PT")]
    public void WhichTranslationAPlayerIsGiven(string player, string own, string files, string? expected)
    {
        Assert.Equal(expected, PackTexts.Choose(player, own, files.Split(',')));
    }

    // ── The file ─────────────────────────────────────────────────────

    private ModPack PackWithAnEmptyLine()
    {
        var pack = new ModPack { PackId = "lang.test" };
        var d = new DialogueDef { Key = "beach", DisplayName = "At the beach" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Hello there!" });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Text = "" });
        d.Nodes.Add(new DialogueNodeDef { Id = 3, Text = "" });
        pack.Dialogues.Add(d);
        return pack;
    }

    [Fact]
    public void WritingTheFileKeepsALineThatIsOnlyInIt()
    {
        // Without the pack's empty line in the source, the writer would find a
        // key the pack "does not have" and move it under "Not used".
        var pack = PackWithAnEmptyLine();
        PackRepository.Save(pack, PackRoot);
        var spanish = File(("dialogue.beach.1", "¡Hola!", "Hello there!"),
                           ("dialogue.beach.2", "Una línea nueva.", ""));

        string path = PackTranslations.Write(pack, PackRoot, "es", PackTranslations.Source(pack, spanish), spanish);
        string written = System.IO.File.ReadAllText(path, Encoding.UTF8);
        _out.WriteLine(written);

        var back = Loc.Read(path)!;
        Assert.Equal("Una línea nueva.", back.Translated("dialogue.beach.2"));
        Assert.NotEqual(TextFileWriter.UnusedHeading, back.Find("dialogue.beach.2")!.Heading);
        // Only the one with words: the other empty line is not a text.
        Assert.Null(back.Find("dialogue.beach.3"));

        // And the check has nothing to say about it: no codes to hold it to,
        // no words it could still be equal to.
        var check = TextCheck.Run(PackTranslations.Source(pack, back), back, "es");
        foreach (var f in check.Findings) _out.WriteLine(f.Kind + " " + f.Key);
        Assert.DoesNotContain(check.Findings, f => f.Key == "dialogue.beach.2");
    }

    [Fact]
    public void ThroughTheRealSaveAndExportTheLineReachesThePlayer()
    {
        var pack = PackWithAnEmptyLine();
        PackRepository.Save(pack, PackRoot);
        var spanish = File(("dialogue.beach.2", "Una línea nueva.", ""));
        PackTranslations.Write(pack, PackRoot, "es", PackTranslations.Source(pack, spanish), spanish);

        string smspack = Path.Combine(_root, "out.smspack");
        PackExporter.Export(PackRoot, smspack);
        using var zip = ZipFile.OpenRead(smspack);
        string Read(string name)
        {
            using var reader = new StreamReader(zip.Entries.First(e => e.FullName.Replace('\\', '/') == name).Open(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        var manifest = JObject.Parse(Read("modpack.json"));
        var line = manifest.SelectToken("dialogues[0].nodes[?(@.id == 2)]") as JObject;
        Assert.NotNull(line);
        Assert.True(PackTexts.IsEmpty((string?)line!["text"]));            // the pack's own words: none

        // A player reading French, which the pack has no file for.
        var files = PackTexts.Files(zip.Entries.Select(e => e.FullName.Replace('\\', '/')));
        PackTexts.Apply(manifest, null, () => files.Values.Select(p => TextFile.Parse(Read(p))).ToList());
        Assert.Equal("Una línea nueva.", (string?)manifest.SelectToken("dialogues[0].nodes[?(@.id == 2)].text"));
    }

    [Fact]
    public void TheMachineLeavesWhatYouWroteAndTakesUpWhatYouEmptied()
    {
        // The author's rule (2026-09-24): a line somebody wrote in the
        // translation is translated; one they later deleted entirely is not,
        // and is the machine's to do again. Through the editor, the way it
        // happens: typed in the language, then cleared.
        var pack = PackWithAnEmptyLine();
        PackRepository.Save(pack, PackRoot);
        string path = PackTranslations.PathOf(PackRoot, "es");
        var start = File(("dialogue.beach.1", "¡Hola!", "Hello there!"));
        PackTranslations.Write(pack, PackRoot, "es", PackTranslations.Source(pack, start), start);

        TextFile Written(LanguageSession s)
        {
            var file = s.Translation(pack, Loc.Read(path));
            TextFile source;
            using (s.OwnWords(pack)) source = PackTranslations.Source(pack, file);
            PackTranslations.Write(pack, PackRoot, "es", source, file);
            return Loc.Read(path)!;
        }
        List<string> Offered(TextFile file)
        {
            TextFile source;
            source = PackTranslations.Source(pack, file);
            return PackTranslationJob.Missing(source, file).Select(m => m.Key).ToList();
        }

        var session = LanguageSession.Enter(pack, "es", Loc.Read(path));
        var line = pack.Dialogues[0].Nodes[0];
        Assert.Equal("¡Hola!", line.Text);

        line.Text = "¡Hola, qué tal!";                 // written by the author
        var file = Written(session);
        session.Restore();
        Assert.DoesNotContain("dialogue.beach.1", Offered(file));

        session = LanguageSession.Enter(pack, "es", Loc.Read(path));
        pack.Dialogues[0].Nodes[0].Text = "";          // and then deleted entirely
        file = Written(session);
        session.Restore();
        Assert.Contains("dialogue.beach.1", Offered(file));
        Assert.Equal("Hello there!", pack.Dialogues[0].Nodes[0].Text);   // the default text never moved
    }

    // ── The check ────────────────────────────────────────────────────

    [Fact]
    public void ThePacksCheckCallsALineOnlyInATranslationAnError()
    {
        var pack = PackWithAnEmptyLine();
        PackRepository.Save(pack, PackRoot);
        var spanish = File(("dialogue.beach.2", "Una línea nueva.", ""));
        PackTranslations.Write(pack, PackRoot, "es", PackTranslations.Source(pack, spanish), spanish);

        var issues = PackValidator.Validate(pack, PackRoot);
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} {i.Where}: {i.Message}");

        var issue = Assert.Single(issues, i => i.Code == TranslationValidation.OnlyInATranslation);
        Assert.Equal(Severity.Error, issue.Severity);
        Assert.Equal("dialogues[beach].nodes[id=2].text", issue.Where);
        Assert.Contains(TranslationFiles.NativeName("es")!, issue.Message);

        // The control: once the pack has words of its own for it, nothing.
        pack.Dialogues[0].Nodes[1].Text = "A new line.";
        Assert.DoesNotContain(PackValidator.Validate(pack, PackRoot), i => i.Code == TranslationValidation.OnlyInATranslation);
    }

    private static ModPack PackOfLines(params string[] lines)
    {
        var pack = new ModPack { PackId = "lang.test" };
        var d = new DialogueDef { Key = "beach", DisplayName = "At the beach" };
        for (int i = 0; i < lines.Length; i++) d.Nodes.Add(new DialogueNodeDef { Id = i + 1, Text = lines[i] });
        pack.Dialogues.Add(d);
        return pack;
    }

    [Fact]
    public void ALineInAnotherLanguageIsPointedAt()
    {
        var pack = PackOfLines(
            "Hello there, how are you doing today?",
            "¿Qué tal estás? Yo estoy muy bien, gracias por preguntar.",
            "I think the weather is nice for the beach.");

        var issues = PackValidator.Validate(pack, PackRoot);
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} {i.Where}: {i.Message}");

        var issue = Assert.Single(issues, i => i.Code == TranslationValidation.OtherLanguage);
        Assert.Equal(Severity.Warning, issue.Severity);
        Assert.Equal("dialogues[beach].nodes[id=2].text", issue.Where);
        Assert.Contains(TranslationFiles.NativeName("es")!, issue.Message);
    }

    [Fact]
    public void APackInAnotherLanguageIsOneIssueNotHundreds()
    {
        var portuguese = Enumerable.Range(1, 12)
            .Select(i => $"Olá, você está bem hoje? Eu não sei o que fazer com isso agora, número {i}.").ToArray();
        var pack = PackOfLines(portuguese);

        var issues = PackValidator.Validate(pack, PackRoot);
        foreach (var i in issues) _out.WriteLine($"{i.Severity} {i.Code} {i.Where}: {i.Message}");

        Assert.Single(issues, i => i.Code == TranslationValidation.PackInOtherLanguage);
        Assert.DoesNotContain(issues, i => i.Code == TranslationValidation.OtherLanguage);

        // The control: said to be Portuguese, it is fine.
        pack.Language = "pt-BR";
        Assert.DoesNotContain(PackValidator.Validate(pack, PackRoot),
            i => i.Code is TranslationValidation.PackInOtherLanguage or TranslationValidation.OtherLanguage);
    }

    // ── The pack's own language ─────────────────────────────────────

    [Fact]
    public void AnEnglishPackDoesNotGainALineWhenSaved()
    {
        // Every pack made before a pack could say its language is English, and
        // saving one must not add a field it never had - that would be a
        // change the author did not make, in every pack there is.
        var pack = PackWithAnEmptyLine();
        PackRepository.Save(pack, PackRoot);
        string saved = System.IO.File.ReadAllText(Path.Combine(PackRoot, "modpack.json"));
        Assert.DoesNotContain("\"" + PackTexts.LanguageField + "\"", saved);

        // The control: a pack that says otherwise, says so, and reads back.
        pack.Language = "pt-BR";
        PackRepository.Save(pack, PackRoot);
        saved = System.IO.File.ReadAllText(Path.Combine(PackRoot, "modpack.json"));
        Assert.Contains("\"" + PackTexts.LanguageField + "\": \"pt-BR\"", saved);
        Assert.Equal("pt-BR", PackRepository.Load(PackRoot).OwnLanguage);
        Assert.Equal("pt-BR", PackTexts.LanguageOf(JObject.Parse(saved)));
        Assert.Equal("en", PackTexts.LanguageOf(new JObject()));
    }

    [Fact]
    public void TheMachineIsToldThePacksLanguageOnlyWhenThePackSaysOne()
    {
        // "auto" for a pack that does not say: an older Portuguese pack
        // declared English would be translated as though it were.
        Assert.Equal("auto", PackTranslationJob.SourceLanguage(new ModPack()));
        Assert.Equal("pt-BR", PackTranslationJob.SourceLanguage(new ModPack { Language = "pt-BR" }));
    }

    [Fact]
    public async System.Threading.Tasks.Task APackIsNotTranslatedIntoItsOwnLanguage()
    {
        var pack = PackWithAnEmptyLine();
        pack.Language = "es";
        PackRepository.Save(pack, PackRoot);
        int sent = 0;

        var done = await PackTranslationJob.Run(pack, PackRoot, new[] { "es" },
            (lines, from, to, cancel) => { sent++; return System.Threading.Tasks.Task.FromResult<IReadOnlyList<string>>(lines.ToList()); },
            (howLong, cancel) => System.Threading.Tasks.Task.CompletedTask);

        Assert.Equal(0, sent);
        Assert.Single(done);
        Assert.False(System.IO.File.Exists(PackTranslations.PathOf(PackRoot, "es")));
    }

    [Fact]
    public void TheCheckMeasuresUntranslatedAgainstThePacksLanguage()
    {
        // A Spanish pack's English file with a line still in Spanish is
        // untranslated; one of ModForge's own, English to English, is not.
        var source = File(("dialogue.d.1", "Hola, ¿qué tal?", null));
        var english = File(("dialogue.d.1", "Hola, ¿qué tal?", "Hola, ¿qué tal?"));

        var asPack = TextCheck.Run(source, english, "en", sourceCode: "es");
        var asModForge = TextCheck.Run(source, english, "en");

        Assert.Equal(1, asPack.Of(TextCheck.Kind.Untranslated));
        Assert.Equal(0, asModForge.Of(TextCheck.Kind.Untranslated));
    }
}
