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
/// How far along the Edit window says a translation is (the author,
/// 2026-09-28: "none of the translations ever show as 100%"). A finished
/// German translation of their pack said 99%: fourteen lines like
/// "&lt;size=60%&gt;..." and "{PC}..." were counted as untranslated for the
/// letters in their markup, while the Translate window rightly had nothing
/// left to send for them - and lines whose default text had changed since
/// counted as done here and as still to do there.
/// </summary>
public sealed class PackTranslationSummaryTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-summary-" + Guid.NewGuid().ToString("N"));

    public PackTranslationSummaryTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static readonly string[] Lines =
    {
        "Hello there.",
        "<size=60%>...",
        "{PC}...",
        "<size=150%>!!!",
        "<size=80%>(...)",
        "[PV:Money]",
        "<b>Careful</b> now.",
    };

    /// <summary>The pack, and a German translation as a machine run leaves
    /// it: every text with words translated, the rest as they were.</summary>
    private ModPack TranslatedPack()
    {
        var pack = PackRepository.CreateEmpty("summary.pack");
        var d = new DialogueDef { Key = "chat" };
        for (int i = 0; i < Lines.Length; i++) d.Nodes.Add(new DialogueNodeDef { Id = i + 1, Text = Lines[i] });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        var source = PackTranslations.Source(pack);
        var de = new TextFile();
        foreach (var e in source.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Text)
                                                    && !TranslationRun.NothingToTranslate(e.Text, "de")))
            de.Add(new TextFile.Entry { Key = e.Key, Text = "DE " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _root, "de", source, de);
        return pack;
    }

    [Fact]
    public void AFinishedTranslationIs100_LinesWithNothingToTranslateIncluded()
    {
        var pack = TranslatedPack();
        var de = PackTranslations.Summaries(pack, _root).Single(s => s.Code == "de");
        _out.WriteLine($"de: {de.Translated} of {de.Total} = {de.Percent}%");
        Assert.Equal(de.Total, de.Translated);
        Assert.Equal(100, de.Percent);
    }

    [Fact]
    public void ItAgreesWithTheTranslateWindow_ALineChangedSinceIsStillToDo()
    {
        var pack = TranslatedPack();
        pack.Dialogues[0].Nodes[0].Text = "Hello there, reworded.";

        var de = PackTranslations.Summaries(pack, _root).Single(s => s.Code == "de");
        var waiting = PackTranslationJob.StillToTranslate(pack, _root).Single(w => w.Code == "de");
        _out.WriteLine($"Edit window: {de.Translated} of {de.Total}, {de.OutOfDate} out of date; Translate window: {waiting.Missing} of {waiting.Total} to do");
        Assert.Equal(1, de.OutOfDate);
        Assert.True(de.Percent < 100);
        Assert.Equal(waiting.Total, de.Total);
        Assert.Equal(waiting.Missing, de.Total - de.Translated);
    }

    [Fact]
    public async Task ALineEndingInASpace_IsDoneOnceTranslated_AndNeverSentAgain()
    {
        // Lines of the author's pack, as they are: a translation file trims
        // every line it reads, so the note of what each was translated from
        // lost its space - and all 43 like them were out of date again after
        // every run, in every language.
        var pack = PackRepository.CreateEmpty("spaces.pack");
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Yeah! " });
        d.Nodes.Add(new DialogueNodeDef { Id = 2, Text = "\t\n Raptures are excellent sources of protein... " });
        d.Nodes.Add(new DialogueNodeDef { Id = 3, Text = "Neon " });   // a name: comes back as it went
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        int sent = 0;
        Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        {
            sent += texts.Count;
            return Task.FromResult<IReadOnlyList<string>>(
                texts.Select(t => t.Trim() == "Neon" ? "Neon" : "DE " + t.Trim()).ToList());
        }
        static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

        await PackTranslationJob.Run(pack, _root, new[] { "de" }, Translator, NoWait);
        _out.WriteLine($"first run sent {sent}");
        Assert.True(sent >= 3);

        void AllDone()
        {
            var de = PackTranslations.Summaries(pack, _root).Single(s => s.Code == "de");
            _out.WriteLine($"de: {de.Translated} of {de.Total} = {de.Percent}%, {de.OutOfDate} out of date");
            Assert.Equal(100, de.Percent);
            Assert.Equal(0, de.OutOfDate);
            Assert.Equal(0, PackTranslationJob.StillToTranslate(pack, _root).Single(w => w.Code == "de").Missing);
            var check = PackTranslations.CheckAll(pack, _root).Single(c => c.Code == "de").Result;
            Assert.DoesNotContain(check.Findings, f => f.Kind is TextCheck.Kind.EnglishChanged
                                                              or TextCheck.Kind.NeedsReview or TextCheck.Kind.Untranslated);
        }
        AllDone();

        // Written again from what the file says now, as every later save
        // does: the note read back has lost its space, and that is not a change.
        var file = Loc.Read(PackTranslations.PathOf(_root, "de"))!;
        PackTranslations.Write(pack, _root, "de", PackTranslations.Source(pack, file), file);
        AllDone();

        sent = 0;
        await PackTranslationJob.Run(pack, _root, new[] { "de" }, Translator, NoWait);
        Assert.Equal(0, sent);
    }

    [Fact]
    public void AChangedFromLeftForOnlyASpace_IsNoChange_AndGoesOnTheNextSave()
    {
        // As files written before the fix have it: the save after the first
        // compared the note it read back, trimmed, with the pack's words, and
        // marked the line as changed from what it already said. Twelve of the
        // author's Spanish lines were waiting for a review of nothing.
        var pack = PackRepository.CreateEmpty("changed.pack");
        var d = new DialogueDef { Key = "chat" };
        d.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Shut up. " });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);
        string key = PackTranslations.Source(pack).Entries.Single(e => e.Key.StartsWith("dialogue.")).Key;
        string path = PackTranslations.PathOf(_root, "es");
        var es = new TextFile();
        foreach (var e in PackTranslations.Source(pack).Entries)
            es.Add(new TextFile.Entry { Key = e.Key, Text = "ES " + e.Text.Trim(), English = e.Text });
        PackTranslations.Write(pack, _root, "es", PackTranslations.Source(pack), es);
        File.WriteAllText(path, File.ReadAllText(path).Replace("# " + TextFile.EnglishNote + " Shut up.",
            "# " + TextFile.EnglishNote + " Shut up.\r\n# " + TextFile.ChangedNote + " Shut up."));
        Assert.Equal("Shut up.", Loc.Read(path)!.Find(key)!.ChangedFrom);

        Assert.Equal(0, PackTranslationJob.StillToTranslate(pack, _root).Single(w => w.Code == "es").Missing);
        Assert.Equal(100, PackTranslations.Summaries(pack, _root).Single(s => s.Code == "es").Percent);
        Assert.DoesNotContain(PackTranslations.CheckAll(pack, _root).Single(c => c.Code == "es").Result.Findings,
                              f => f.Kind == TextCheck.Kind.NeedsReview);

        var file = Loc.Read(path)!;
        PackTranslations.Write(pack, _root, "es", PackTranslations.Source(pack, file), file);
        Assert.Null(Loc.Read(path)!.Find(key)!.ChangedFrom);
        Assert.Equal("ES Shut up.", Loc.Read(path)!.Get(key));
    }

    [Fact]
    public void TheCheckDoesNotCallMarkupUntranslated_ButStillCallsWordsSo()
    {
        var english = new TextFile();
        var german = new TextFile();
        foreach (var (key, text) in new[] { ("dots", "<size=60%>..."), ("name", "{PC}..."), ("token", "[PV:Money]"),
                                            ("words", "<b>Careful</b> now.") })
        {
            english.Add(new TextFile.Entry { Key = key, Text = text });
            german.Add(new TextFile.Entry { Key = key, Text = text, English = text });
        }
        var untranslated = TextCheck.Run(english, german, "de").Findings
                                    .Where(f => f.Kind == TextCheck.Kind.Untranslated).Select(f => f.Key).ToList();
        _out.WriteLine("untranslated: " + string.Join(", ", untranslated));
        Assert.Equal(new[] { "words" }, untranslated);
    }
}
