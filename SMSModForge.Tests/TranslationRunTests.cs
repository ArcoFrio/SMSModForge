using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// Translating a pack, against a translation service that behaves badly.
/// <para/>
/// Every test here drives the real run with a fake service — no network, no
/// clock — because the failures that matter are not "the code threw". They are
/// a service that drops markup, answers with the wrong number of lines, or
/// refuses halfway through five thousand lines. Each of those, handled wrongly,
/// writes something plausible and broken into a language the author cannot
/// read, across a file nobody is going to check by hand.
/// <para/>
/// The property that everything else serves: <b>whatever it managed is kept,
/// and nothing damaged is written.</b>
/// </summary>
public sealed class TranslationRunTests
{
    private readonly ITestOutputHelper _out;
    public TranslationRunTests(ITestOutputHelper o) => _out = o;

    /// <summary>No waiting, and a note of how long it would have waited.</summary>
    private readonly List<TimeSpan> _waited = new();
    private Task NoWait(TimeSpan howLong, CancellationToken _)
    {
        _waited.Add(howLong);
        return Task.CompletedTask;
    }

    private TranslationRun Run(TranslationRun.Send send)
        => new(send, NoWait, new Random(1));

    /// <summary>
    /// <paramref name="count"/> lines, each with words of its own.
    /// <para/>
    /// Distinct on purpose. These used to be one sentence repeated, which was
    /// enough to make many batches - until the run learned to send a repeated
    /// line once, and a hundred copies of one sentence became one request. A
    /// real pack is mostly lines nobody else says, so that is what these are.
    /// </summary>
    private static List<TranslationRun.Line> Lines(int count, string text = "Hello {name}, welcome.")
        => Enumerable.Range(0, count)
                     .Select(i => new TranslationRun.Line("key" + i, text + " Line number " + i + "."))
                     .ToList();

    /// <summary>A service that translates honestly: it keeps every marker and
    /// puts a word in front.</summary>
    private static Task<IReadOnlyList<string>> Honest(IReadOnlyList<string> texts, string a, string b, CancellationToken c)
        => Task.FromResult<IReadOnlyList<string>>(texts.Select(t => "[fr] " + t).ToList());

    // ── The ordinary run ─────────────────────────────────────────────

    [Fact]
    public async Task EveryLineComesBackWithItsOwnKeyAndItsMarkupIntact()
    {
        var run = Run(Honest);
        var got = await run.Go(Lines(120), "en", "fr");

        _out.WriteLine($"{got.Count} line(s) in {run.Sent} request(s)");
        Assert.Equal(120, got.Count);
        Assert.All(got, r => Assert.True(r.Usable, r.Trouble));

        // Each result is against its own key, in order, and carries the gap
        // the original had.
        for (int i = 0; i < got.Count; i++)
        {
            Assert.Equal("key" + i, got[i].Key);
            Assert.Contains("{name}", got[i].Text);
        }

        // ...and it was not one request per line.
        Assert.True(run.Sent < 20, run.Sent + " requests for 120 lines");
        Assert.Null(run.StoppedBecause);
    }

    [Fact]
    public async Task ResultsArriveAsTheyAreTranslatedRatherThanAtTheEnd()
    {
        // This is what makes a run resumable: the caller writes them to the
        // file as they come, so a run cut off halfway has still saved half.
        var batches = new List<int>();
        var run = Run(Honest);
        await run.Go(Lines(200), "en", "fr", done: r => batches.Add(r.Count));

        _out.WriteLine("handed back in: " + string.Join(", ", batches));
        Assert.True(batches.Count > 1, "everything arrived in one go, so nothing could be saved early");
        Assert.Equal(200, batches.Sum());
    }

    // ── A service that mangles the text ──────────────────────────────

    [Fact]
    public async Task ALineThatLostItsMarkupIsReportedAndNotWritten()
    {
        // The quiet one. A translation missing its {name} looks like a
        // translation; it is a line that will render wrongly in the game.
        var run = Run((texts, a, b, c) =>
            Task.FromResult<IReadOnlyList<string>>(texts.Select(_ => "Bonjour !").ToList()));

        var got = await run.Go(Lines(3), "en", "fr");
        foreach (var r in got) _out.WriteLine($"{r.Key}: usable={r.Usable} '{r.Text}' {r.Trouble}");

        Assert.All(got, r => Assert.False(r.Usable));
        Assert.All(got, r => Assert.Contains("{name}", r.Trouble));   // the gap it lost is named
    }

    [Fact]
    public async Task AnEmptyTranslationIsNotWrittenEither()
    {
        var run = Run((texts, a, b, c) =>
            Task.FromResult<IReadOnlyList<string>>(texts.Select(_ => "   ").ToList()));

        var got = await run.Go(Lines(2, "Plain words, nothing to protect."), "en", "fr");
        Assert.All(got, r => Assert.False(r.Usable));
    }

    [Fact]
    public async Task TheGoodLinesInABadBatchAreStillKept()
    {
        // A batch is forty lines. Throwing all forty away because one came back
        // damaged would lose a great deal of correct work.
        int n = 0;
        var run = Run((texts, a, b, c) =>
            Task.FromResult<IReadOnlyList<string>>(
                texts.Select(t => n++ % 2 == 0 ? "[fr] " + t : "Bonjour !").ToList()));

        var got = await run.Go(Lines(10), "en", "fr");
        _out.WriteLine($"{got.Count(r => r.Usable)} usable of {got.Count}");

        Assert.Contains(got, r => r.Usable);
        Assert.Contains(got, r => !r.Usable);
    }

    [Fact]
    public async Task AnAnswerWithTheWrongNumberOfLinesStopsTheRun()
    {
        // The dangerous one. If a service returns 39 lines where 40 were sent,
        // there is no way to tell which answer belongs to which line - and
        // lining them up anyway would file one line's words under another
        // line's key, silently, for the rest of the pack.
        var run = Run((texts, a, b, c) =>
            Task.FromResult<IReadOnlyList<string>>(texts.Skip(1).Select(t => "[fr] " + t).ToList()));

        var got = await run.Go(Lines(10), "en", "fr");
        _out.WriteLine(run.StoppedBecause);

        Assert.Empty(got);
        Assert.Equal(SMSModForge.Localization.Loc.T("packText.mt.miscounted"), run.StoppedBecause);
    }

    // ── A service that says no ───────────────────────────────────────

    [Fact]
    public async Task ARateLimitIsWaitedOutAndTheRunCarriesOn()
    {
        int calls = 0;
        var run = Run((texts, a, b, c) =>
        {
            if (++calls == 1) throw new TranslationRun.ServiceRefused(429, "slow down");
            return Honest(texts, a, b, c);
        });

        var got = await run.Go(Lines(60), "en", "fr");
        _out.WriteLine($"{got.Count} line(s), {run.Refused} refusal(s), waits: "
                       + string.Join(", ", _waited.Select(w => w.TotalSeconds + "s")));

        Assert.Equal(60, got.Count);
        Assert.Equal(1, run.Refused);
        Assert.Null(run.StoppedBecause);
        Assert.Contains(_waited, w => w >= Pacing.BackOff(1));
    }

    [Fact]
    public async Task AServiceThatKeepsRefusingIsGivenUpOn_AndTheWorkIsKept()
    {
        // Half the pack translated, then blocked. Throwing away what was done
        // would be the worst of both: the requests were still made.
        int calls = 0;
        var run = Run((texts, a, b, c) =>
        {
            if (++calls > 2) throw new TranslationRun.ServiceRefused(429, "too many requests");
            return Honest(texts, a, b, c);
        });

        var got = await run.Go(Lines(400), "en", "fr");
        _out.WriteLine($"{got.Count} of 400 kept; stopped because: {run.StoppedBecause}");

        Assert.NotEmpty(got);
        Assert.True(got.Count < 400);
        Assert.All(got, r => Assert.True(r.Usable));
        // Against the text's KEY rather than its words: what it says is copy,
        // and copy gets edited. What matters is that it is the "would not
        // answer, your work is kept" message and not some other one.
        Assert.Equal(SMSModForge.Localization.Loc.T("packText.mt.noAnswer"), run.StoppedBecause);

        // It gave up rather than hammering: attempts, not attempts per batch
        // for the rest of the pack.
        Assert.Equal(Pacing.Attempts, run.Refused);
    }

    [Fact]
    public async Task ABlockIsNeverRetried_EvenWhenItComesAsA429()
    {
        // Found against the real service: a flagged connection gets a 429, the
        // same status as "slow down", with a captcha page for a body. Treating
        // it as a slow-down meant four more requests into a block, each one
        // more of the automated traffic the block is about.
        int asked = 0;
        var run = Run((texts, a, b, c) =>
        {
            asked++;
            throw new TranslationRun.ServiceRefused(429, "blocked", blocked: true);
        });

        var got = await run.Go(Lines(10), "en", "fr");
        _out.WriteLine($"asked {asked} time(s), waited {_waited.Count} time(s): {run.StoppedBecause}");

        Assert.Equal(1, asked);
        Assert.Empty(got);
        Assert.Equal("blocked", run.StoppedBecause);
        Assert.DoesNotContain(_waited, w => w >= Pacing.BackOff(1));
    }

    [Fact]
    public async Task ARequestThatWasSimplyWrongIsNotRetriedAtAll()
    {
        // A 400 will be just as wrong in thirty seconds, and retrying it is
        // only more traffic towards a block on somebody's own connection.
        var run = Run((texts, a, b, c) =>
            throw new TranslationRun.ServiceRefused(400, "bad request"));

        await run.Go(Lines(10), "en", "fr");
        _out.WriteLine($"{run.Refused} refusal(s): {run.StoppedBecause}");

        Assert.Equal(1, run.Refused);
        Assert.Equal(SMSModForge.Localization.Loc.F("packText.mt.refused",
                                                    "status", "400", "why", "bad request"),
                     run.StoppedBecause);
    }

    [Fact]
    public async Task StoppingItKeepsWhatItHad()
    {
        var stop = new CancellationTokenSource();
        int calls = 0;
        var run = Run((texts, a, b, c) =>
        {
            if (++calls == 2) stop.Cancel();
            return Honest(texts, a, b, c);
        });

        var got = await run.Go(Lines(400), "en", "fr", cancel: stop.Token);
        _out.WriteLine($"{got.Count} line(s) kept after stopping");

        Assert.NotEmpty(got);
        Assert.Equal(TranslationRun.Cancelled, run.StoppedBecause);
    }

    // ── Sending less ─────────────────────────────────────────────────

    [Fact]
    public async Task ALineSaidTwiceIsSentOnce_AndBothGetTheAnswer()
    {
        // A fifth of a real pack's lines repeat another. Sending each again
        // buys nothing but another chance of being blocked.
        var sent = new List<string>();
        var run = Run((texts, a, b, c) => { sent.AddRange(texts); return Honest(texts, a, b, c); });

        var lines = new List<TranslationRun.Line>
        {
            new("a", "Just passing by."),
            new("b", "Wanna hang out?"),
            new("c", "Just passing by."),
            new("d", "Just passing by."),
        };
        var got = await run.Go(lines, "en", "es");

        _out.WriteLine("sent: " + string.Join(" | ", sent));
        Assert.Equal(2, sent.Count);
        Assert.Equal(4, got.Count);
        Assert.All(got.Where(r => r.Key is "a" or "c" or "d"),
                   r => Assert.Equal("[fr] Just passing by.", r.Text));
    }

    [Fact]
    public async Task ALineWithNoWordsInItIsNotSentAtAll()
    {
        // "..." is the same in every language, and one pack had a hundred and
        // forty of them. A line that is only a {name} is the same too - once
        // the markup is taken out, nothing is left to translate.
        var sent = new List<string>();
        var run = Run((texts, a, b, c) => { sent.AddRange(texts); return Honest(texts, a, b, c); });

        var lines = new List<TranslationRun.Line>
        {
            new("dots", "..."), new("shout", "?!"), new("name", "{name}!"),
            new("real", "Hmmm, interesting."),
        };
        var got = await run.Go(lines, "en", "es");

        _out.WriteLine($"sent {sent.Count}, {run.Wordless} left out");
        Assert.Equal(new[] { "Hmmm, interesting." }, sent);
        Assert.Equal(3, run.Wordless);
        Assert.Equal("real", Assert.Single(got).Key);
    }

    // ── Punctuation-only lines, in the languages that write them differently ──

    [Theory]
    [InlineData("...", "ja", "……")]
    [InlineData("...", "zh-Hans", "……")]
    [InlineData("…", "zh-Hans", "……")]
    [InlineData("?!", "ja", "？！")]
    [InlineData("......?", "zh-Hans", "……？")]
    [InlineData("...", "ko", "...")]        // Korean writes them as English does
    [InlineData("...", "es", "...")]
    [InlineData("?!", "ru", "?!")]
    [InlineData("~", "ja", "~")]            // more than one accepted form: left alone
    public void PunctuationIsWrittenTheWayTheLanguageWritesIt(string text, string to, string expected)
        => Assert.Equal(expected, TranslationRun.Punctuation(text, to));

    [Fact]
    public async Task PunctuationOnlyLinesAreFilledForChineseWithoutAsking()
    {
        // Not sent - there is nothing to translate - but not left as they are
        // either, because Chinese writes them with characters of its own.
        var sent = new List<string>();
        var run = Run((texts, a, b, c) => { sent.AddRange(texts); return Honest(texts, a, b, c); });

        var got = await run.Go(new List<TranslationRun.Line>
        {
            new("dots", "..."), new("name", "{name}!"), new("real", "Hello there, friend."),
        }, "en", "zh-Hans");

        foreach (var r in got) _out.WriteLine($"{r.Key}: {r.Text}");
        Assert.Equal(new[] { "Hello there, friend." }, sent);
        Assert.Equal("……", got.Single(r => r.Key == "dots").Text);
        Assert.Equal("{name}！", got.Single(r => r.Key == "name").Text);   // markup put back around it
    }

    // ── An engine that has stopped working ───────────────────────────

    [Fact]
    public async Task AnEngineReturningDamageLineAfterLineIsStopped()
    {
        // It does not refuse and does not throw - it answers, with text that
        // fails the markup check every time. Past a point that is not bad
        // lines, it is a changed service, and going on would spend the whole
        // run on lines that are all thrown away.
        int asked = 0;
        var run = Run((texts, a, b, c) =>
        {
            asked++;
            return Task.FromResult<IReadOnlyList<string>>(texts.Select(_ => "Bonjour !").ToList());
        });

        var got = await run.Go(Lines(150), "en", "fr");
        _out.WriteLine($"asked {asked} time(s); stopped: {run.StoppedBecause}");

        Assert.Equal(1, asked);   // judged after the first batch, not after all four
        Assert.NotNull(run.StoppedBecause);
        Assert.All(got, r => Assert.False(r.Usable));
    }

    [Fact]
    public async Task AFewHardLinesDoNotStopARun()
    {
        // The control. One damaged line in forty is a hard line, not a broken
        // engine, and a run that stopped for it would never finish a real pack.
        int n = 0;
        var run = Run((texts, a, b, c) => Task.FromResult<IReadOnlyList<string>>(
            texts.Select(t => ++n % 40 == 0 ? "Bonjour !" : "[fr] " + t).ToList()));

        var got = await run.Go(Lines(160), "en", "fr");
        Assert.Null(run.StoppedBecause);
        Assert.Equal(160, got.Count);
    }

    [Theory]
    [InlineData(10, 9, false)]    // nineteen lines: not enough seen to judge, however bad
    [InlineData(15, 5, false)]    // exactly a quarter: not past it
    [InlineData(14, 6, true)]
    [InlineData(0, 40, true)]
    public void WhereTheLineIsDrawn(int usable, int damaged, bool stops)
        => Assert.Equal(stops, TranslationRun.NotWorking(usable, damaged));

    // ── The probe before a run ───────────────────────────────────────

    [Fact]
    public async Task AnHonestEngineIsLetThrough()
    {
        Assert.Null(await TranslationRun.Probe(
            (t, a, b, c) => Task.FromResult<IReadOnlyList<string>>(t.Select(x => "[es] " + x).ToList()), "es"));
    }

    [Fact]
    public async Task AnEngineThatHandsTheTextBackUnchangedIsCaught()
    {
        // Passes every other check: right count, markup intact, words present.
        // Only asking whether it translated anything catches it.
        Assert.NotNull(await TranslationRun.Probe(
            (t, a, b, c) => Task.FromResult<IReadOnlyList<string>>(t.ToList()), "es"));
    }

    [Fact]
    public async Task AnEngineThatDropsMarkupIsCaught()
    {
        Assert.NotNull(await TranslationRun.Probe(
            (t, a, b, c) => Task.FromResult<IReadOnlyList<string>>(t.Select(_ => "Hola.").ToList()), "es"));
    }

    [Fact]
    public async Task AnEngineThatRefusesSaysWhy()
    {
        string? why = await TranslationRun.Probe(
            (t, a, b, c) => throw new TranslationRun.ServiceRefused(429, "blocked", blocked: true), "es");
        Assert.Equal("blocked", why);
    }

    [Theory]
    [InlineData("...", false)]
    [InlineData("%%0%%!", false)]
    [InlineData("123 456", false)]
    [InlineData("Hmmm...", true)]
    [InlineData("你好", true)]         // a letter in any script is a letter
    [InlineData("Привет", true)]
    public void WhatCountsAsHavingWords(string text, bool words)
        => Assert.Equal(words, TranslationRun.HasWords(text));

    // ── Nothing to do ────────────────────────────────────────────────

    [Fact]
    public async Task NothingToTranslateSendsNothing()
    {
        // The control for resuming: the second run of a finished pack is given
        // an empty list, and must not make a request at all.
        bool asked = false;
        var run = Run((texts, a, b, c) => { asked = true; return Honest(texts, a, b, c); });

        Assert.Empty(await run.Go(new List<TranslationRun.Line>(), "en", "fr"));
        Assert.Empty(await run.Go(null, "en", "fr"));
        Assert.False(asked, "a request went out with nothing to translate");
        Assert.Equal(0, run.Sent);
    }

    [Fact]
    public async Task ALineWithNoMarkupGoesThroughUntouched()
    {
        var run = Run(Honest);
        var got = await run.Go(
            new List<TranslationRun.Line> { new("plain", "Just some ordinary words.") }, "en", "fr");

        var one = Assert.Single(got);
        Assert.True(one.Usable);
        Assert.Equal("[fr] Just some ordinary words.", one.Text);
    }
}
