using System;
using System.IO;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using SMSModForge.Shared;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Each translation's standing, worked out when the pack opens and then only
/// again for what changed (the author, 1.6.3): the translations window had
/// spent a couple of seconds working every language out each time it opened.
/// </summary>
public sealed class TranslationSummaryCacheTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smsmodforge-sumcache-" + Guid.NewGuid().ToString("N"));

    public TranslationSummaryCacheTests(ITestOutputHelper o)
    {
        _out = o;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("cache.pack");
        var d = new DialogueDef { Key = "chat" };
        for (int i = 1; i <= 6; i++) d.Nodes.Add(new DialogueNodeDef { Id = i, Text = "Line " + i });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);
        foreach (string code in new[] { "de", "fr", "es" })
        {
            var source = PackTranslations.Source(pack);
            var file = new TextFile();
            foreach (var e in source.Entries.Take(3))
                file.Add(new TextFile.Entry { Key = e.Key, Text = code + " " + e.Text, English = e.Text });
            PackTranslations.Write(pack, _root, code, source, file);
        }
        return pack;
    }

    private static string Show(PackTranslations.Summary s) => $"{s.Code} {s.Translated}/{s.Total} ({s.OutOfDate} old)";

    [Fact]
    public void WarmedWhenThePackOpens_TheWindowWorksNothingOut_AndSeesWhatItWouldHave()
    {
        var pack = Pack();
        var cache = new TranslationSummaryCache();
        cache.Warm(pack, _root);
        cache.Warming.Wait();

        var got = cache.Get(pack, _root);
        _out.WriteLine(string.Join(", ", got.Select(Show)));
        Assert.Equal(0, cache.LastWorkedOut);
        Assert.Equal(PackTranslations.Summaries(pack, _root).Select(Show), got.Select(Show));
    }

    [Fact]
    public void OnlyATranslationWhoseFileChangedIsWorkedOutAgain()
    {
        var pack = Pack();
        var cache = new TranslationSummaryCache();
        cache.Warm(pack, _root);
        cache.Warming.Wait();

        // German gets another line translated, as the language being edited
        // is written out when the window opens.
        var path = PackTranslations.PathOf(_root, "de");
        var de = Loc.Read(path)!;
        var line = de.Find(PackTexts.LineKey("chat", "5"))!;
        line.Text = "de Zeile 5";
        PackTranslations.Write(pack, _root, "de", PackTranslations.Source(pack, de), de);

        var got = cache.Get(pack, _root);
        _out.WriteLine(string.Join(", ", got.Select(Show)));
        Assert.Equal(1, cache.LastWorkedOut);
        Assert.Equal(PackTranslations.Summaries(pack, _root).Select(Show), got.Select(Show));
    }

    [Fact]
    public void WhenThePacksOwnTextsChange_EveryTranslationIsWorkedOutAgain()
    {
        var pack = Pack();
        var cache = new TranslationSummaryCache();
        cache.Warm(pack, _root);
        cache.Warming.Wait();

        pack.Dialogues[0].Nodes[0].Text = "Line 1, reworded";
        var got = cache.Get(pack, _root);
        _out.WriteLine(string.Join(", ", got.Select(Show)));
        Assert.Equal(3, cache.LastWorkedOut);
        Assert.All(got, s => Assert.Equal(1, s.OutOfDate));

        // And nothing again, when nothing has changed since.
        cache.Get(pack, _root);
        Assert.Equal(0, cache.LastWorkedOut);
    }
}
