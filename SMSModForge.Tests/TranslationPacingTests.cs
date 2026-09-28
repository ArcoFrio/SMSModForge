using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Services.Translation;
using Xunit;
using Xunit.Abstractions;

namespace SMSModForge.Tests;

/// <summary>
/// How a pack's worth of text is sent, and what happens when the service says
/// no.
/// <para/>
/// The free translation endpoint is rate limited by address, and a block lands
/// on the person's home connection rather than on ModForge. Sending thousands
/// of lines one at a time, or retrying a refusal at full speed, is exactly the
/// pattern that earns one. So the rules are written down here rather than left
/// as a sleep inside a loop — the part that needs a network cannot be checked,
/// but how many requests a pack becomes, and whether a refusal is backed off
/// or hammered, is arithmetic.
/// </summary>
public sealed class TranslationPacingTests
{
    private readonly ITestOutputHelper _out;
    public TranslationPacingTests(ITestOutputHelper o) => _out = o;

    // ── Grouping lines into requests ─────────────────────────────────

    [Fact]
    public void APacksWorthOfLinesIsTensOfRequestsAndNotThousands()
    {
        // The whole point. A real pack: five thousand short lines of dialogue.
        var pack = Enumerable.Range(0, 5000)
                             .Select(i => "This is an ordinary line of dialogue number " + i + ".")
                             .ToList();

        var batches = Batches.Of(pack);
        _out.WriteLine($"{pack.Count} lines -> {batches.Count} request(s)");

        Assert.True(batches.Count < 200, batches.Count + " requests is too many");
        Assert.Equal(pack.Count, batches.Sum(b => b.Count));
    }

    [Fact]
    public void EveryLineIsSentExactlyOnceAndInOrder()
    {
        // The control for the grouping: a batcher that lost or duplicated a
        // line would leave a pack with holes in its translation, and a run of
        // five thousand lines is not something anybody reads afterwards.
        var texts = Enumerable.Range(0, 977).Select(i => new string('x', i % 300)).ToList();

        var seen = Batches.Of(texts).SelectMany(b => b).ToList();
        Assert.Equal(Enumerable.Range(0, texts.Count), seen);
    }

    [Fact]
    public void NoRequestGoesOverEitherLimit()
    {
        var texts = Enumerable.Range(0, 500).Select(i => new string('x', 10 + i % 400)).ToList();

        foreach (var batch in Batches.Of(texts))
        {
            int size = batch.Sum(i => texts[i].Length);
            Assert.True(batch.Count <= Batches.Lines, "too many lines in one request");

            // A single over-long line is allowed to exceed the budget, because
            // the alternative is cutting a sentence in half. Anything with more
            // than one line in it must fit.
            if (batch.Count > 1)
                Assert.True(size <= Batches.Characters, $"{size} characters in one request");
        }
    }

    [Fact]
    public void AnOverlongLineGoesOnItsOwnRatherThanBeingCut()
    {
        var texts = new List<string> { "short", new string('x', Batches.Characters * 3), "short" };
        var batches = Batches.Of(texts);
        foreach (var b in batches) _out.WriteLine("batch: " + string.Join(",", b));

        var big = batches.Single(b => b.Contains(1));
        Assert.Single(big);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NothingToSendIsNoRequests(int count)
    {
        var texts = Enumerable.Repeat("a line", count).ToList();
        Assert.Equal(count == 0 ? 0 : 1, Batches.Of(texts).Count);
        Assert.Empty(Batches.Of(null!));
    }

    // ── What happens when the service says no ────────────────────────

    [Fact]
    public void ARefusalIsWaitedOutForLongerEachTime()
    {
        // Retrying at the same rate is what turns a slow-down into a block.
        var waits = Enumerable.Range(0, Pacing.Attempts).Select(Pacing.BackOff).ToList();
        _out.WriteLine(string.Join(", ", waits.Select(w => w.TotalSeconds + "s")));

        Assert.Equal(TimeSpan.Zero, waits[0]);           // the first try does not wait
        for (int i = 2; i < waits.Count; i++)
            Assert.True(waits[i] >= waits[i - 1], "a later attempt waits no longer than an earlier one");
    }

    [Fact]
    public void GivingUpHappensWhileSomebodyIsStillWatching()
    {
        // Short on purpose. A run that waits half an hour on a rate limit is a
        // run somebody has walked away from, and the work is kept either way -
        // it resumes rather than starting over.
        double total = Enumerable.Range(0, Pacing.Attempts).Sum(a => Pacing.BackOff(a).TotalSeconds);
        _out.WriteLine($"{Pacing.Attempts} attempts span {total}s");
        Assert.InRange(total, 1, 120);
    }

    [Fact]
    public void ARateLimitIsWaitedOutAndABadRequestIsNot()
    {
        // The distinction that keeps a doomed request from becoming traffic
        // towards a block: "slow down" is worth waiting for, "you asked wrongly"
        // will be just as wrong in thirty seconds.
        Assert.True(Pacing.WorthRetrying(429));
        Assert.True(Pacing.WorthRetrying(503));
        Assert.True(Pacing.WorthRetrying(0));       // no answer at all

        Assert.False(Pacing.WorthRetrying(400));
        Assert.False(Pacing.WorthRetrying(403));
        Assert.False(Pacing.WorthRetrying(404));
        Assert.False(Pacing.WorthRetrying(200));
    }

    [Fact]
    public void ThereIsAlwaysAPauseBetweenRequestsAndItIsNotOnABeat()
    {
        var random = new Random(1);
        var waits = Enumerable.Range(0, 20).Select(_ => Pacing.Between(random).TotalMilliseconds).ToList();
        _out.WriteLine(string.Join(", ", waits));

        Assert.All(waits, w => Assert.True(w >= Pacing.BetweenRequests, "a request went out with no pause"));
        Assert.True(waits.Distinct().Count() > 1, "every pause is identical, so the run arrives on a beat");
    }
}
