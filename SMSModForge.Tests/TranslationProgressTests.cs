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
/// How far a machine translation is: across the whole run, counted in texts,
/// and in the language it is on - the two bars of the Translate window
/// (2026-09-28). One line of text had said which language and how far into it,
/// and nothing about how much of the whole was left.
/// </summary>
public sealed class TranslationProgressTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-progress-" + Guid.NewGuid().ToString("N"));

    public TranslationProgressTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static Task<IReadOnlyList<string>> Translator(IReadOnlyList<string> texts, string from, string to, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[" + to + "] " + t).ToList());

    /// <summary>A hundred lines, and a German translation already half done:
    /// the two languages have very different amounts left.</summary>
    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("progress.pack");
        var d = new DialogueDef { Key = "chat" };
        for (int i = 1; i <= 100; i++) d.Nodes.Add(new DialogueNodeDef { Id = i, Text = "Line number " + i + " of the chat." });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);

        var source = PackTranslations.Source(pack);
        var de = new TextFile();
        foreach (var e in source.Entries.Take(50))
            de.Add(new TextFile.Entry { Key = e.Key, Text = "DE " + e.Text, English = e.Text });
        PackTranslations.Write(pack, _root, "de", source, de);
        return pack;
    }

    [Fact]
    public async Task TheWholeRunIsCountedInTexts_AndEachLanguageOnItsOwn()
    {
        var pack = Pack();
        int esLeft = PackTranslationJob.Left(pack, _root, "es");
        int deLeft = PackTranslationJob.Left(pack, _root, "de");
        _out.WriteLine($"left before the run: es {esLeft}, de {deLeft}");
        Assert.True(esLeft > deLeft && deLeft > 0, "the two languages should have different amounts left");

        var seen = new List<PackTranslationJob.Progress>();
        await PackTranslationJob.Run(pack, _root, new[] { "es", "de" }, Translator, NoWait, stepped: seen.Add);
        foreach (var p in seen) _out.WriteLine(p.ToString());

        Assert.NotEmpty(seen);
        // The whole has its size from the first step: both languages' texts.
        Assert.Equal(esLeft + deLeft, seen[0].AllTotal);
        Assert.Equal(0, seen[0].AllDone);

        // It only ever moves forward, and ends full.
        for (int i = 1; i < seen.Count; i++) Assert.True(seen[i].AllDone >= seen[i - 1].AllDone);
        Assert.Equal(seen[^1].AllTotal, seen[^1].AllDone);
        Assert.Equal(esLeft + deLeft, seen[^1].AllDone);

        // Each language starts empty and ends full, numbered in order.
        foreach (var (code, number, left) in new[] { ("es", 1, esLeft), ("de", 2, deLeft) })
        {
            var mine = seen.Where(p => p.Code == code).ToList();
            Assert.All(mine, p => Assert.Equal(number, p.Number));
            Assert.All(mine, p => Assert.Equal(2, p.Languages));
            Assert.Equal(0, mine[0].Done);
            Assert.Equal(left, mine[^1].Done);
            Assert.Equal(left, mine[^1].Total);
        }

        // German starts where Spanish ended: half of it was already there, so
        // it is worth a third of the run, not half.
        var firstDe = seen.First(p => p.Code == "de");
        Assert.Equal(esLeft, firstDe.AllDone);
    }
}
