using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// When Google stops to check that a person is asking, the person is shown the
/// check and the translation carries on once they have passed it. ModForge
/// never answers a check itself, and a block is still never retried on its
/// own: only a passed check lets the same request be asked again, once.
/// </summary>
public sealed class PersonCheckTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _root;

    public PersonCheckTests(ITestOutputHelper o)
    {
        _out = o;
        _root = Path.Combine(Path.GetTempPath(), "smsmodforge-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private const string Sorry = "https://www.google.com/sorry/index?continue=x";

    private static TranslationRun.ServiceRefused Blocked()
        => new(429, "blocked for now", blocked: true) { CheckAddress = Sorry };

    private static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    private static Task<IReadOnlyList<string>> Working(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[" + b + "] " + t).ToList());

    private static List<TranslationRun.Line> Lines(int count)
        => Enumerable.Range(0, count).Select(i => new TranslationRun.Line("k" + i, "Line number " + i + ".")).ToList();

    [Fact]
    public async Task ABlockMidRunIsShownToThePerson_AndTheRunCarriesOnOnceTheyPass()
    {
        bool blocked = true;
        var shown = new List<string>();
        Task<IReadOnlyList<string>> Send(IReadOnlyList<string> t, string a, string b, CancellationToken c)
            => blocked ? throw Blocked() : Working(t, a, b, c);

        var run = new TranslationRun(Send, NoWait, new Random(1))
        {
            Person = (address, _) => { shown.Add(address); blocked = false; return Task.FromResult(TranslationRun.CheckOutcome.Passed); },
        };
        var got = await run.Go(Lines(5), "en", "fr");

        Assert.Equal(new[] { Sorry }, shown);
        Assert.Null(run.StoppedBecause);
        Assert.Equal(5, got.Count(r => r.Usable));
    }

    [Fact]
    public async Task NotPassed_OrRefusedAgainRightAfter_TheRunStopsAsBefore()
    {
        // The person stopped instead.
        var run = new TranslationRun((t, a, b, c) => throw Blocked(), NoWait, new Random(1))
        {
            Person = (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NotPassed),
        };
        Assert.Empty(await run.Go(Lines(5), "en", "fr"));
        Assert.Equal("blocked for now", run.StoppedBecause);

        // Passed, and refused again at once: asked once, not in a loop.
        int asked = 0, shown = 0;
        run = new TranslationRun((t, a, b, c) => { asked++; throw Blocked(); }, NoWait, new Random(1))
        {
            Person = (_, _) => { shown++; return Task.FromResult(TranslationRun.CheckOutcome.Passed); },
        };
        Assert.Empty(await run.Go(Lines(5), "en", "fr"));
        _out.WriteLine($"asked {asked}, shown {shown}");
        Assert.Equal(1, shown);
        Assert.Equal(2, asked);
        Assert.Equal("blocked for now", run.StoppedBecause);

        // Nobody to ask: exactly as it always was - one request, then stop.
        asked = 0;
        run = new TranslationRun((t, a, b, c) => { asked++; throw Blocked(); }, NoWait, new Random(1));
        Assert.Empty(await run.Go(Lines(5), "en", "fr"));
        Assert.Equal(1, asked);
    }

    private ModPack Pack()
    {
        var pack = PackRepository.CreateEmpty("check.pack");
        var d = new DialogueDef { Key = "chat" };
        for (int i = 0; i < 4; i++) d.Nodes.Add(new DialogueNodeDef { Text = "A line, number " + i + "." });
        pack.Dialogues.Add(d);
        PackRepository.Save(pack, _root);
        return pack;
    }

    [Fact]
    public async Task ABlockBeforeTheRunIsShownToo_AndOncePassedTheWebsiteMayCarryOn()
    {
        // Blocked at the test sentence. Before, that was the end: going to the
        // website would have been a way round the block. Once a person has
        // passed the check, the connection is theirs, and the website carries
        // on when the free service still refuses.
        bool opened = false;
        int shown = 0;
        var done = await PackTranslationJob.Run(Pack(), _root, new[] { "es" },
            (t, a, b, c) => throw Blocked(), NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); },
            person: (_, _) => { shown++; return Task.FromResult(TranslationRun.CheckOutcome.Passed); });

        var one = Assert.Single(done);
        _out.WriteLine($"shown {shown}, website {opened}, translated {one.Translated}, stopped: {one.StoppedBecause}");
        Assert.Equal(1, shown);
        Assert.True(opened);
        Assert.True(one.ByFallback);
        Assert.True(one.Translated > 0);

        // Not passed: no website, as before.
        opened = false;
        done = await PackTranslationJob.Run(Pack(), _root, new[] { "fr" },
            (t, a, b, c) => throw Blocked(), NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); },
            person: (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NotPassed));
        Assert.False(opened);
        Assert.False(Assert.Single(done).Finished);
    }

    [Fact]
    public async Task NothingToAnswerMidRun_TheWebsiteTakesOver_ForTheRestOfTheRun()
    {
        // Seen for real: the free service's refusal page has no captcha on it,
        // only "try again later". Nobody can answer that, so the run goes on
        // through the website rather than stopping.
        int websiteAsked = 0, shown = 0;
        Task<IReadOnlyList<string>> Website(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        {
            websiteAsked++;
            return Working(t, a, b, c);
        }
        var run = new TranslationRun((t, a, b, c) => throw Blocked(), NoWait, new Random(1))
        {
            Person = (_, _) => { shown++; return Task.FromResult(TranslationRun.CheckOutcome.NothingToAnswer); },
            Instead = _ => Task.FromResult<TranslationRun.Send?>(Website),
        };
        var got = await run.Go(Lines(5), "en", "fr");

        Assert.Equal(1, shown);
        Assert.True(websiteAsked > 0);
        Assert.Null(run.StoppedBecause);
        Assert.Equal(5, got.Count(r => r.Usable));
        Assert.NotNull(run.SwitchedTo);

        // No way to the website: stopped, as before.
        run = new TranslationRun((t, a, b, c) => throw Blocked(), NoWait, new Random(1))
        {
            Person = (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NothingToAnswer),
        };
        Assert.Empty(await run.Go(Lines(5), "en", "fr"));
        Assert.Equal("blocked for now", run.StoppedBecause);
    }

    [Fact]
    public async Task NothingToAnswerAtTheStart_TheWebsiteIsUsed()
    {
        bool opened = false;
        var done = await PackTranslationJob.Run(Pack(), _root, new[] { "es", "fr" },
            (t, a, b, c) => throw Blocked(), NoWait,
            probeFirst: true,
            fallback: _ => { opened = true; return Task.FromResult<(TranslationRun.Send?, string?)>((Working, null)); },
            person: (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NothingToAnswer));

        Assert.True(opened);
        Assert.All(done, d => { Assert.True(d.ByFallback); Assert.True(d.Translated > 0); Assert.True(d.Finished); });
    }

    [Fact]
    public async Task NothingToAnswerForTheNames_TheWebsiteIsAsked()
    {
        var names = new[] { "Kiki" };
        Task<IReadOnlyList<string>> Website(IReadOnlyList<string> t, string a, string b, CancellationToken c)
            => Task.FromResult<IReadOnlyList<string>>(t.Select(x => x.Replace("My name is", "Меня зовут")
                                                                    .Replace("Hello,", "Привет,")
                                                                    .Replace("Kiki", "Кики")).ToList());
        var got = await PackTranslationJob.SuggestSpellings((t, a, b, c) => throw Blocked(), names, "ru",
            person: (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NothingToAnswer),
            instead: _ => Task.FromResult<TranslationRun.Send?>(Website));
        Assert.Equal("Кики", got["Kiki"]);
    }

    /// <summary>The free service as it answered in Chinese (author,
    /// 2026-09-26): "Solid Snake" left in Latin letters. Tia spelled, for the
    /// test to see that what it did spell is kept.</summary>
    private static Task<IReadOnlyList<string>> FreeChinese(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(t.Select(x => x.Replace("My name is ", "我叫 ").Replace(".", "。")
                                                                .Replace("Hello, ", "你好，").Replace("!", "！")
                                                                .Replace("Tia", "蒂亚")).ToList());

    [Fact]
    public async Task NamesTheFreeServiceLeftInLatin_AreAskedOfTheWebsite_AndOnlyThose()
    {
        var sentToWebsite = new List<string>();
        Task<IReadOnlyList<string>> Website(IReadOnlyList<string> t, string a, string b, CancellationToken c)
        {
            sentToWebsite.AddRange(t);
            return Task.FromResult<IReadOnlyList<string>>(t.Select(x => x.Replace("My name is ", "我的名字是").Replace(".", "。")
                                                                        .Replace("Hello, ", "你好，").Replace("!", "！")
                                                                        .Replace("Solid Snake", "索利德·斯内克")).ToList());
        }
        int opened = 0;
        var got = await PackTranslationJob.SuggestSpellings(FreeChinese, new[] { "Solid Snake", "Tia" }, "zh-Hans",
            instead: _ => { opened++; return Task.FromResult<TranslationRun.Send?>(Website); });

        Assert.Equal("索利德·斯内克", got["Solid Snake"]);
        Assert.Equal("蒂亚", got["Tia"]);
        Assert.Equal(1, opened);
        Assert.DoesNotContain(sentToWebsite, t => t.Contains("Tia"));
    }

    [Fact]
    public async Task TooManyRequestsWithNoCheckToShow_TheWebsiteIsAskedForTheNames()
    {
        // A plain 429: no page, nothing for a person to answer. The column used
        // to stay empty with "could not suggest spellings".
        bool personAsked = false;
        var got = await PackTranslationJob.SuggestSpellings(
            (t, a, b, c) => throw new TranslationRun.ServiceRefused(429, "too many"), new[] { "Tia" }, "zh-Hans",
            person: (_, _) => { personAsked = true; return Task.FromResult(TranslationRun.CheckOutcome.Passed); },
            instead: _ => Task.FromResult<TranslationRun.Send?>(FreeChinese));
        Assert.Equal("蒂亚", got["Tia"]);
        Assert.False(personAsked);

        // With no website to ask, the refusal is still what the author sees.
        await Assert.ThrowsAsync<TranslationRun.ServiceRefused>(() => PackTranslationJob.SuggestSpellings(
            (t, a, b, c) => throw new TranslationRun.ServiceRefused(429, "too many"), new[] { "Tia" }, "zh-Hans"));
    }

    [Fact]
    public async Task APersonStoppingTheCheck_DoesNotGoOnToTheWebsite()
    {
        bool opened = false;
        await Assert.ThrowsAsync<TranslationRun.ServiceRefused>(() => PackTranslationJob.SuggestSpellings(
            (t, a, b, c) => throw Blocked(), new[] { "Tia" }, "zh-Hans",
            person: (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NotPassed),
            instead: _ => { opened = true; return Task.FromResult<TranslationRun.Send?>(FreeChinese); }));
        Assert.False(opened);
    }

    [Fact]
    public async Task EveryNameSpelledByTheFreeService_TheWebsiteIsNotOpened()
    {
        int opened = 0;
        var got = await PackTranslationJob.SuggestSpellings(FreeChinese, new[] { "Tia" }, "zh-Hans",
            instead: _ => { opened++; return Task.FromResult<TranslationRun.Send?>(FreeChinese); });
        Assert.Equal("蒂亚", got["Tia"]);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task TheWebsiteAlreadyAnswered_ItIsNotAskedAgain()
    {
        int opened = 0;
        var got = await PackTranslationJob.SuggestSpellings((t, a, b, c) => throw Blocked(), new[] { "Solid Snake", "Tia" }, "zh-Hans",
            person: (_, _) => Task.FromResult(TranslationRun.CheckOutcome.NothingToAnswer),
            instead: _ => { opened++; return Task.FromResult<TranslationRun.Send?>(FreeChinese); });
        Assert.Equal("蒂亚", got["Tia"]);
        Assert.False(got.ContainsKey("Solid Snake"));
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task TheWebsiteRefusing_KeepsWhatTheFreeServiceSpelled()
    {
        var got = await PackTranslationJob.SuggestSpellings(FreeChinese, new[] { "Solid Snake", "Tia" }, "zh-Hans",
            instead: _ => Task.FromResult<TranslationRun.Send?>((t, a, b, c) => throw Blocked()));
        Assert.Equal("蒂亚", got["Tia"]);
        Assert.False(got.ContainsKey("Solid Snake"));
    }

    [Fact]
    public void WhatAPassedCheckGivesIsSentWithTheFreeServicesRequests_AndNothingElseIs()
    {
        using var translator = new GoogleTranslator();
        translator.TakeCookies(new[]
        {
            new Cookie("GOOGLE_ABUSE_EXEMPTION", "pass", "/", ".googleapis.com"),
            new Cookie("NID", "n", "/", ".google.com"),
            new Cookie("tracker", "t", "/", ".example.com"),
        });

        var sent = translator.Cookies!.GetCookies(new Uri("https://translate.googleapis.com/translate_a/single"));
        Assert.Equal("pass", sent["GOOGLE_ABUSE_EXEMPTION"]?.Value);
        Assert.Null(translator.Cookies.GetCookies(new Uri("https://www.example.com/"))["tracker"]);
    }
}
