using System;

namespace SMSModForge.Services.Translation;

/// <summary>
/// How long to wait between requests, and what to do when one is refused.
/// <para/>
/// The free translation endpoint is not a supported API and is rate limited by
/// address. Sending a pack's worth of text as fast as the network allows is
/// the exact pattern it blocks — and a block lands on the person's home
/// connection, not on ModForge, which is why this is written down and tested
/// rather than left as a sleep somewhere in a loop.
/// <para/>
/// Three rules, and all three matter:
/// <list type="bullet">
/// <item>A pause between every request, jittered, so a run does not arrive as
/// a machine-perfect pulse.</item>
/// <item>On a refusal, wait longer each time rather than trying again at the
/// same rate — which is what turns a slow-down into a block.</item>
/// <item>Give up after a few refusals instead of going on. Whatever has been
/// translated is kept, and the run is resumed later; hammering a service that
/// has just said no is how the address stops working for everything.</item>
/// </list>
/// </summary>
public static class Pacing
{
    /// <summary>The ordinary wait between two requests, in milliseconds.</summary>
    public const int BetweenRequests = 400;

    /// <summary>How much of that wait is random, so a long run does not send on
    /// an exact beat.</summary>
    public const int Jitter = 250;

    /// <summary>How many times a refused batch is tried again before the run
    /// stops. Small on purpose: see the type doc.</summary>
    public const int Attempts = 4;

    /// <summary>
    /// The wait before attempt <paramref name="attempt"/> of a batch, counting
    /// the first attempt as 0.
    /// <para/>
    /// Doubling from one second, capped, so four attempts span about half a
    /// minute rather than half an hour — long enough for a rate limit to lift,
    /// short enough that somebody is still watching.
    /// </summary>
    public static TimeSpan BackOff(int attempt)
    {
        if (attempt <= 0) return TimeSpan.Zero;
        double seconds = Math.Min(16, Math.Pow(2, attempt - 1) * 2);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// The ordinary wait before the next request, jittered.
    /// <para/>
    /// <paramref name="random"/> is passed in so a test can pin it; nothing
    /// here owns a random number generator.
    /// </summary>
    public static TimeSpan Between(Random? random)
        => TimeSpan.FromMilliseconds(BetweenRequests + (random?.Next(Jitter) ?? 0));

    /// <summary>
    /// Whether this is worth trying again, or the run should stop and keep
    /// what it has.
    /// <para/>
    /// A refusal to serve — too many requests, or a service saying no — is
    /// worth waiting out. Anything that says the REQUEST was wrong will be
    /// wrong again however long it waits, and retrying it is only more traffic
    /// towards a block.
    /// </summary>
    public static bool WorthRetrying(int httpStatus)
    {
        if (httpStatus == 429) return true;                 // rate limited
        if (httpStatus >= 500 && httpStatus <= 599) return true;
        if (httpStatus == 0) return true;                   // no answer at all
        return false;
    }
}
