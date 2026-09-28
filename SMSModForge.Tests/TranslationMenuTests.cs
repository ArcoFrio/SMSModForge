using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// What Language ▸ New or update a translation and Check a translation do to
/// real files: a fresh one works at once, an update keeps everything a person
/// wrote, and damaged keys are put back without touching the words.
/// </summary>
public class TranslationMenuTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SMSModForgeLanguages", Guid.NewGuid().ToString("N"));
    private string Mine => Path.Combine(_root, "mine");
    private string Shipped => Path.Combine(_root, "shipped");

    public TranslationMenuTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(Mine);
        Directory.CreateDirectory(Shipped);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ANewTranslationIsEveryEnglishText_NamedInItsOwnLanguage()
    {
        var (path, existed) = TranslationFiles.CreateOrUpdate("es", Mine, Shipped);
        Assert.False(existed);
        var file = Loc.Read(path)!;
        Assert.Empty(file.BadLines);
        Assert.Equal("Español", file.Get("language.name"));
        Assert.StartsWith("ModForge editor text: Español (es)", file.TopNotes[0]);

        var check = TextCheck.Run(Loc.English, file, "es");
        _out.WriteLine(TranslationFiles.Report(check).Split('\n')[0]);
        // Every text there, nothing broken - just not translated yet.
        Assert.DoesNotContain(check.Findings, f => f.IsError || f.Kind == TextCheck.Kind.Missing);
        Assert.Equal(check.Total, check.Translated);
        // It is written as UTF-8 with a byte-order mark, for old Notepad.
        byte[] head = File.ReadAllBytes(path).Take(3).ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, head);
    }

    [Fact]
    public void ANewTranslationStartsFromTheShippedOne_WhenThereIsOne()
    {
        File.WriteAllText(Path.Combine(Shipped, "es.txt"),
            "language.name = Español\n# en: + Quest\nquests.addQuest = + Misión\n");
        var (path, _) = TranslationFiles.CreateOrUpdate("es", Mine, Shipped);
        Assert.Equal("+ Misión", Loc.Read(path)!.Get("quests.addQuest"));
    }

    [Fact]
    public void UpdatingKeepsWhatWasWritten_AndTheShippedFileIsNotConsulted()
    {
        File.WriteAllText(Path.Combine(Mine, "es.txt"),
            "# Mine\n\nlanguage.name = Español\n\n# en: + Quest\nquests.addQuest = + Misión mía\n");
        File.WriteAllText(Path.Combine(Shipped, "es.txt"), "quests.addQuest = + Misión\n");
        var (path, existed) = TranslationFiles.CreateOrUpdate("es", Mine, Shipped);
        Assert.True(existed);
        var file = Loc.Read(path)!;
        Assert.Equal("+ Misión mía", file.Get("quests.addQuest"));
        Assert.Equal(new[] { "Mine" }, file.TopNotes);
    }

    /// <summary>
    /// An update rewrites a file somebody may have spent days in, so the one
    /// being replaced is kept beside it. The control is the other half: a file
    /// that did not exist has nothing to keep, and must not leave a .bak of the
    /// English behind for somebody to mistake for their own work.
    /// </summary>
    [Fact]
    public void UpdatingKeepsTheFileItReplaces_AndANewOneLeavesNoBackup()
    {
        var (path, _) = TranslationFiles.CreateOrUpdate("es", Mine, Shipped);
        Assert.False(File.Exists(path + ".bak"), "a new translation should not leave a backup");

        File.WriteAllText(path, "language.name = Español\nquests.addQuest = + Misión mía\n");
        TranslationFiles.CreateOrUpdate("es", Mine, Shipped);

        Assert.True(File.Exists(path + ".bak"), "the file the update replaced was not kept");
        Assert.Contains("+ Misión mía", File.ReadAllText(path + ".bak"));
        // ...and the backup is not itself offered as a language.
        Assert.DoesNotContain(Directory.EnumerateFiles(Mine, "*.txt"), p => p.EndsWith(".bak"));
    }

    [Fact]
    public void AnUnknownCodeIsRefused()
    {
        Assert.Null(TranslationFiles.NativeName("xx-notalanguage"));
        Assert.Null(TranslationFiles.NativeName("has space"));
        Assert.Equal("Español", TranslationFiles.NativeName("es"));
        Assert.NotNull(TranslationFiles.NativeName("zh-Hans"));
        Assert.NotNull(TranslationFiles.NativeName("pt-BR"));
    }

    [Fact]
    public void KeysAReplaceAllChangedArePutBack_AndOnlyTheKeys()
    {
        var (path, _) = TranslationFiles.CreateOrUpdate("es", Mine, Shipped);
        // Translate the Quests tab's own words, then Replace All "quest"
        // across the file, as somebody would with Notepad++.
        string text = File.ReadAllText(path).Replace("quests.addQuest = + Quest", "quests.addQuest = + Misión");
        File.WriteAllText(path, text.Replace("quests.", "misiones."));

        var damaged = Loc.Read(path)!;
        var check = TextCheck.Run(Loc.English, damaged, "es");
        int unknown = check.Of(TextCheck.Kind.Unknown);
        _out.WriteLine(unknown + " keys damaged");
        Assert.True(unknown > 50);
        Assert.Contains("Replace All", TranslationFiles.Report(check));

        int put = TranslationFiles.PutKeysBack(path, check);
        Assert.Equal(unknown, put);
        Assert.True(File.Exists(path + ".bak"));

        var fixedFile = Loc.Read(path)!;
        Assert.Equal("+ Misión", fixedFile.Get("quests.addQuest"));
        Assert.DoesNotContain(TextCheck.Run(Loc.English, fixedFile, "es").Findings, f => f.Kind == TextCheck.Kind.Unknown);
    }

    [Fact]
    public void TheReportSaysEveryKindOfProblemInWords()
    {
        var file = TextFile.Parse(
            "not a line\n" +
            "quests.addQuest = + Quest\nquests.addQuest = again\n" +
            "misc.nothing = x\n" +
            "home.issuesCount = Asuntos\n");
        var report = TranslationFiles.Report(TextCheck.Run(Loc.English, file, "es"));
        _out.WriteLine(report);
        Assert.Contains("Line 1", report);
        Assert.Contains("line 2", report);
        Assert.Contains("'misc.nothing'", report);
        Assert.Contains("{0}", report);
        Assert.DoesNotContain("{line}", report);
        Assert.DoesNotContain("{key}", report);
    }
}
