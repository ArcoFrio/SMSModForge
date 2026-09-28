using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// The translation service itself, for real.
/// <para/>
/// Everything else about machine translation is tested against a fake, because
/// the parts worth testing — protecting markup, batching, pacing, keeping work
/// when a run is cut off — do not need a network. This is the one thing that
/// does: whether the free endpoint answers in the shape
/// <see cref="GoogleReply"/> expects, and whether the markers
/// <see cref="ProtectedText"/> puts in the text survive a real translator.
/// Both were written from observation rather than documentation, since there
/// is no documentation, and this is where that observation gets checked.
/// <para/>
/// <b>Off unless asked for.</b> Run on every build it would be flaky — the
/// service can refuse, block, or change — and it would be exactly the pattern of
/// traffic the rest of the code works to avoid, from every machine the tests
/// run on. So it does nothing unless told to:
/// <code>
///   set SMSMODFORGE_LIVE_TRANSLATE=1
///   dotnet test --filter LiveTranslationTests
/// </code>
/// </summary>
[Trait("Speed", "Slow")]   // talks to a real service; see CLAUDE.md
public sealed class LiveTranslationTests
{
    private readonly ITestOutputHelper _out;
    public LiveTranslationTests(ITestOutputHelper o) => _out = o;

    private bool Wanted()
    {
        if (Environment.GetEnvironmentVariable("SMSMODFORGE_LIVE_TRANSLATE") == "1") return true;
        _out.WriteLine("SMSMODFORGE_LIVE_TRANSLATE not set; nothing sent.");
        return false;
    }

    [Fact]
    public async Task ABatchComesBackAlignedFromTheRealService()
    {
        // The claim the engine is built on: lines joined by breaks go out as one
        // text and come back as the same number of lines, in order. Checked by
        // hand in September 2026; this is where it gets checked again.
        if (!Wanted()) return;

        using var translator = new GoogleTranslator();
        var lines = new[] { "Good morning.", "Where is the beach?", "Thank you very much.", "Yes." };
        var got = await translator.Send(lines, "en", "es", CancellationToken.None);

        for (int i = 0; i < lines.Length; i++) _out.WriteLine($"{lines[i]}  ->  {got[i]}");
        Assert.Equal(lines.Length, got.Count);
        Assert.All(got, g => Assert.False(string.IsNullOrWhiteSpace(g)));
    }

    [Fact]
    public async Task TheProbePassesAgainstTheRealService()
    {
        // The probe stops a run before anything is sent. If it were wrong about
        // a working service, it would block every run anybody started - so it
        // is checked against the real one, not only against fakes.
        if (!Wanted()) return;

        using var translator = new GoogleTranslator();
        string? why = await TranslationRun.Probe(translator.Send, "es");
        _out.WriteLine("probe: " + (why ?? "passed"));
        Assert.Null(why);
    }

    [Fact]
    public async Task OneLineAlsoReads()
    {
        // One text can come back in the segmented shape rather than a flat list.
        if (!Wanted()) return;

        using var translator = new GoogleTranslator();
        var got = await translator.Send(new[] { "Hello. How are you today?" }, "en", "es", CancellationToken.None);

        _out.WriteLine("-> " + string.Join(" | ", got));
        var one = Assert.Single(got);
        Assert.False(string.IsNullOrWhiteSpace(one));
        Assert.NotEqual("Hello. How are you today?", one);
    }

    [Fact]
    public async Task MarkupSurvivesARealTranslator()
    {
        // The claim ProtectedText is built on, tested against the thing it is
        // about. A marker a real translator mangles beyond recognition is a
        // gap in somebody's dialogue in a language they cannot read.
        if (!Wanted()) return;

        var lines = new[]
        {
            "Hello {name}, welcome back.",
            "You have <b>[PV:money]</b> left to spend.",
            "Give {name} the <color=#ff0000>red</color> hat, then talk to {other}.",
            "{name} told {name} everything.",
        };

        var masked = lines.Select(ProtectedText.Protect).ToList();
        foreach (var m in masked) _out.WriteLine("sent: " + m.Text);

        using var translator = new GoogleTranslator();
        var came = await translator.Send(masked.Select(m => m.Text).ToList(), "en", "es", CancellationToken.None);

        int kept = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var lost = new List<string>();
            string put = ProtectedText.Restore(came[i], masked[i].Codes, lost);
            bool whole = lost.Count == 0 && ProtectedText.KeptItsCodes(lines[i], put);
            if (whole) kept++;

            _out.WriteLine($"back: {came[i]}");
            _out.WriteLine($"   => {put}   {(whole ? "OK" : "LOST " + string.Join(", ", lost))}");
        }

        // All of them, not most. A line that loses its markup is not written,
        // so a marker that fails half the time would leave half a pack in the
        // author's own words and call it translated.
        Assert.Equal(lines.Length, kept);
    }

    [Fact]
    public async Task AWholeRunGoesThroughEndToEnd()
    {
        if (!Wanted()) return;

        var lines = Enumerable.Range(0, 12)
            .Select(i => new TranslationRun.Line("k" + i, $"This is line {i}, and {{name}} is in it."))
            .ToList();

        using var translator = new GoogleTranslator();
        var run = new TranslationRun(translator.Send, (t, c) => Task.Delay(t, c));
        var got = await run.Go(lines, "en", "pt-BR");

        _out.WriteLine($"{got.Count(r => r.Usable)} usable of {got.Count} in {run.Sent} request(s), "
                       + $"{run.Refused} refusal(s), stopped: {run.StoppedBecause ?? "no"}");
        foreach (var r in got.Take(4)) _out.WriteLine($"  {r.Key}: {r.Text}  {r.Trouble}");

        Assert.Null(run.StoppedBecause);
        Assert.All(got, r => Assert.True(r.Usable, r.Trouble));
    }
}
