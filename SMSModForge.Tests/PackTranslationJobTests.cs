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
/// Translating a whole pack by machine, into real files on disk.
/// <para/>
/// The property everything here is about: <b>a run that is cut off keeps its
/// work, and running it again does only what is left.</b> Five thousand lines
/// against a rate-limited service will be interrupted — that is the expected
/// case, not the exceptional one — so "start again from nothing" would mean the
/// feature never finishes for anybody with a large pack.
/// <para/>
/// And the other half: <b>the machine never writes over a person.</b> What is
/// still to do is read from the file, so a line somebody translated or
/// corrected by hand is, by construction, a line that is not sent again.
/// </summary>
public sealed class PackTranslationJobTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public PackTranslationJobTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-mt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>A pack with a handful of lines a player reads.</summary>
    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("mt.pack");
        var dialogue = new DialogueDef { Key = "chat" };
        for (int i = 0; i < 6; i++)
            dialogue.Nodes.Add(new DialogueNodeDef { Text = "Line number " + i + ", spoken aloud." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);
        return pack;
    }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static Task<IReadOnlyList<string>> Honest(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[es] " + t).ToList());

    private TextFile Read(string code) => Loc.Read(PackTranslations.PathOf(_root, code));

    [Fact]
    public async Task ItTranslatesWhatThePackSaysAndWritesItToTheFile()
    {
        var pack = Pack();
        var done = await PackTranslationJob.Run(pack, _root, new[] { "es" }, Honest, NoWait);

        var one = Assert.Single(done);
        _out.WriteLine($"{one.Code}: {one.Translated} translated, {one.Damaged} damaged, stopped={one.StoppedBecause}");
        Assert.True(one.Finished);
        Assert.True(one.Translated > 0);

        var file = Read("es");
        Assert.NotNull(file);
        Assert.Contains(file.Entries, e => (e.Text ?? "").StartsWith("[es] ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunningItAgainSendsNothing()
    {
        // The resume property, stated at its simplest: once a language is done,
        // a second run is not a second bill and not a second wait.
        var pack = Pack();
        await PackTranslationJob.Run(pack, _root, new[] { "es" }, Honest, NoWait);

        int asked = 0;
        Task<IReadOnlyList<string>> Counting(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        { asked++; return Honest(t, a, b, c); }

        var done = await PackTranslationJob.Run(pack, _root, new[] { "es" }, Counting, NoWait);
        _out.WriteLine($"second run asked {asked} time(s), translated {done[0].Translated}");

        Assert.Equal(0, asked);
        Assert.Equal(0, done[0].Translated);
    }

    [Fact]
    public async Task AnInterruptedRunKeepsWhatItGot_AndTheNextOneDoesTheRest()
    {
        // The case that actually happens: a rate limit partway through.
        var pack = Pack();

        int calls = 0;
        Task<IReadOnlyList<string>> RefusingAfterOne(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        {
            if (++calls > 1) throw new TranslationRun.ServiceRefused(429, "too many");
            return Honest(t, a, b, c);
        }

        // Batches are large, so force the interruption by translating a pack
        // whose lines are long enough to split - or, failing that, accept that
        // one batch did everything and this asserts the "kept" half only.
        var first = await PackTranslationJob.Run(pack, _root, new[] { "es" }, RefusingAfterOne, NoWait);
        int afterFirst = first[0].Translated;
        _out.WriteLine($"first run: {afterFirst} translated, stopped={first[0].StoppedBecause}");

        // Whatever it managed is on disk, not held in memory and lost.
        var file = Read("es");
        int written = file.Entries.Count(e => (e.Text ?? "").StartsWith("[es] ", StringComparison.Ordinal));
        _out.WriteLine($"{written} line(s) written to the file");
        Assert.Equal(afterFirst, written);

        // ...and a second run finishes the rest without redoing those.
        var second = await PackTranslationJob.Run(pack, _root, new[] { "es" }, Honest, NoWait);
        _out.WriteLine($"second run: {second[0].Translated} more");
        Assert.True(second[0].Finished);

        Assert.Empty(PackTranslationJob.Missing(PackTranslations.Source(pack), Read("es")));
    }

    [Fact]
    public async Task ALineSomebodyTranslatedByHandIsNeverSentAgain()
    {
        // The machine must not write over a person. Nothing enforces this
        // separately - it falls out of asking the file what is still empty -
        // but that is exactly why it is worth a test: a later change that
        // computed the work differently would break it silently.
        var pack = Pack();
        PackTranslations.CreateOrUpdate(pack, _root, "es");

        string path = PackTranslations.PathOf(_root, "es");
        var file = Read("es");
        string key = PackTranslations.Source(pack).Entries.First().Key;
        file.Find(key)!.Text = "Lo traduje yo a mano.";
        Loc.Write(path, TextFileWriter.Build(PackTranslations.Source(pack), file, "es", new List<string>()));

        var sent = new List<string>();
        Task<IReadOnlyList<string>> Watching(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        { sent.AddRange(t); return Honest(t, a, b, c); }

        await PackTranslationJob.Run(pack, _root, new[] { "es" }, Watching, NoWait);

        _out.WriteLine($"{sent.Count} line(s) sent");
        Assert.Equal("Lo traduje yo a mano.", Read("es").Translated(key));
    }

    [Fact]
    public async Task EveryLanguageGetsAnEntryForEveryTextThePackHas()
    {
        // The structural rule: adding something in one language adds it in all
        // of them, untranslated, rather than leaving some files short. What
        // makes that true is that each file is rebuilt from the pack.
        var pack = Pack();
        await PackTranslationJob.Run(pack, _root, new[] { "es", "pt-BR" }, Honest, NoWait);

        var source = PackTranslations.Source(pack);
        foreach (string code in new[] { "es", "pt-BR" })
        {
            var file = Read(code);
            foreach (var entry in source.Entries)
                Assert.True(file.Has(entry.Key), $"{code} has no line for {entry.Key}");
        }
    }

    [Fact]
    public async Task ALineThePackHasChangedIsTranslatedAgain()
    {
        // A translation of words the pack no longer says is worse than none:
        // it reads as current and is not.
        var pack = Pack();
        await PackTranslationJob.Run(pack, _root, new[] { "es" }, Honest, NoWait);

        pack.Dialogues[0].Nodes[0].Text = "Something else entirely now.";
        PackRepository.Save(pack, _root);

        var sent = new List<string>();
        Task<IReadOnlyList<string>> Watching(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        { sent.AddRange(t); return Honest(t, a, b, c); }

        var done = await PackTranslationJob.Run(pack, _root, new[] { "es" }, Watching, NoWait);
        _out.WriteLine($"{sent.Count} line(s) sent again: {string.Join(" | ", sent)}");

        Assert.Equal(1, done[0].Translated);
        Assert.Contains(sent, t => t.Contains("Something else entirely"));
    }

    [Fact]
    public async Task ADamagedLineIsCountedAndNotWritten()
    {
        var pack = Pack();
        var done = await PackTranslationJob.Run(pack, _root, new[] { "es" },
            (t, a, b, c) => Task.FromResult<IReadOnlyList<string>>(t.Select(_ => "").ToList()), NoWait);

        _out.WriteLine($"{done[0].Translated} translated, {done[0].Damaged} damaged");
        Assert.Equal(0, done[0].Translated);
        Assert.True(done[0].Damaged > 0);

        // ...and the file still shows the pack's own words, so it is all still
        // to do rather than done badly.
        Assert.NotEmpty(PackTranslationJob.Missing(PackTranslations.Source(pack), Read("es")));
    }

    [Fact]
    public void ThePacksLanguagesAreTheFilesItHas()
    {
        var pack = Pack();
        Assert.Empty(PackTranslationJob.Languages(_root));

        PackTranslations.CreateOrUpdate(pack, _root, "es");
        PackTranslations.CreateOrUpdate(pack, _root, "ja");
        Assert.Equal(new[] { "es", "ja" }, PackTranslationJob.Languages(_root));
    }

    /// <summary>A pack with one line the machine gives back as it is (a name),
    /// one with nothing to translate, and one ordinary line.</summary>
    private ModPack PackWithAName()
    {
        var pack = PackRepository.CreateEmpty("mt.names");
        var dialogue = new DialogueDef { Key = "chat" };
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 1, Text = "Anna" });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 2, Text = "..." });
        dialogue.Nodes.Add(new DialogueNodeDef { Id = 3, Text = "A line with words in it." });
        pack.Dialogues.Add(dialogue);
        PackRepository.Save(pack, _root);
        return pack;
    }

    /// <summary>Translates everything but "Anna", which comes back as it went.</summary>
    private static Task<IReadOnlyList<string>> KeepsNames(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => t == "Anna" ? t : "[x] " + t).ToList());

    [Fact]
    public async Task ALineTheMachineGivesBackUnchangedIsDone_AndNeverOfferedAgain()
    {
        // Before this, a name came back as itself, looked exactly like a line
        // nobody had touched, and was "still to translate" after every run -
        // so anything offering to finish the job offered it for ever.
        var pack = PackWithAName();
        await PackTranslationJob.Run(pack, _root, new[] { "de" }, KeepsNames, NoWait);

        var file = Read("de");
        string anna = PackTranslations.Source(pack).Entries.First(e => e.Text == "Anna").Key;
        Assert.True(file.Find(anna)!.Same, "the unchanged line is not marked");
        Assert.Contains("# " + TextFile.SameNote, File.ReadAllText(PackTranslations.PathOf(_root, "de")));
        Assert.Empty(PackTranslationJob.Missing(PackTranslations.Source(pack), file, "de"));

        int asked = 0;
        Task<IReadOnlyList<string>> Counting(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        { asked++; return KeepsNames(t, a, b, c); }
        await PackTranslationJob.Run(pack, _root, new[] { "de" }, Counting, NoWait);
        Assert.Equal(0, asked);

        // The mark is about the words the pack says NOW. Change them and the
        // line is waiting again, mark gone.
        pack.Dialogues[0].Nodes[0].Text = "Anna Maria";
        PackRepository.Save(pack, _root);
        PackTranslations.CreateOrUpdate(pack, _root, "de");
        var after = Read("de");
        Assert.False(after.Find(anna)!.Same, "the mark outlived the words it was about");
        Assert.Contains(PackTranslationJob.Missing(PackTranslations.Source(pack), after, "de"), l => l.Text == "Anna Maria");
    }

    [Fact]
    public void APersonCanMarkALineAsTheSame()
    {
        var pack = PackWithAName();
        PackTranslations.CreateOrUpdate(pack, _root, "fr");
        string path = PackTranslations.PathOf(_root, "fr");
        string anna = PackTranslations.Source(pack).Entries.First(e => e.Text == "Anna").Key;

        // The control: untouched, the name is waiting like any other line.
        Assert.Contains(PackTranslationJob.Missing(PackTranslations.Source(pack), Read("fr"), "fr"), l => l.Key == anna);

        // Written by hand above the line, the way a translator would.
        string text = File.ReadAllText(path);
        text = text.Replace(anna + " = Anna", "# " + TextFile.SameNote + "\r\n" + anna + " = Anna");
        File.WriteAllText(path, text);

        Assert.DoesNotContain(PackTranslationJob.Missing(PackTranslations.Source(pack), Read("fr"), "fr"), l => l.Key == anna);
        // ...and it survives the file being brought up to date.
        PackTranslations.CreateOrUpdate(pack, _root, "fr");
        Assert.True(Read("fr").Find(anna)!.Same);
    }

    [Fact]
    public async Task ALineWithNothingToTranslateIsNotWaiting_ExceptWhereTheLanguageWritesItsOwn()
    {
        // "..." is never sent, so it used to be "still to translate" after
        // every run. German writes it the same; Japanese writes "……", so there
        // it waits until the run puts that in.
        var pack = PackWithAName();
        var source = PackTranslations.Source(pack);

        Assert.DoesNotContain(PackTranslationJob.Missing(source, null, "de"), l => l.Text == "...");
        Assert.Contains(PackTranslationJob.Missing(source, null, "ja"), l => l.Text == "...");

        await PackTranslationJob.Run(pack, _root, new[] { "ja" }, KeepsNames, NoWait);
        var ja = Read("ja");
        Assert.Contains(ja.Entries, e => e.Text == "……");
        Assert.Empty(PackTranslationJob.Missing(PackTranslations.Source(pack), ja, "ja"));
    }

    [Fact]
    public async Task StillToTranslateCountsEveryLanguageModForgeHas_ButThePacksOwn()
    {
        var pack = PackWithAName();
        var before = PackTranslationJob.StillToTranslate(pack, _root);
        Assert.DoesNotContain(before, w => w.Code == "en");      // the pack's own language
        Assert.Contains(before, w => w.Code == "de");
        Assert.All(before, w => Assert.True(w.Missing > 0, $"{w.Code} has nothing waiting in a pack nobody translated"));

        await PackTranslationJob.Run(pack, _root, new[] { "de" }, KeepsNames, NoWait);
        var after = PackTranslationJob.StillToTranslate(pack, _root).ToDictionary(w => w.Code);
        Assert.Equal(0, after["de"].Missing);
        Assert.True(after["fr"].Missing > 0, "translating German finished French too");
    }

    [Fact]
    public async Task NoPackAndNoLanguagesDoNothingRatherThanThrow()
    {
        Assert.Empty(await PackTranslationJob.Run(null!, _root, new[] { "es" }, Honest, NoWait));
        Assert.Empty(await PackTranslationJob.Run(Pack(), _root, Array.Empty<string>(), Honest, NoWait));
    }
}
