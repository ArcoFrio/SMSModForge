using System.Collections.Generic;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Splits the lines of a pack into requests a translation service will accept.
/// <para/>
/// A pack is thousands of lines and a service takes so many characters at a
/// time. Sending them one at a time is thousands of requests, which is the
/// pattern that gets an address blocked; sending them all at once is a request
/// nothing will answer. So they are grouped — as many as fit under a character
/// budget, and never more than a fixed count however short they are.
/// <para/>
/// Kept apart from whatever does the sending so the rule can be checked. The
/// sending needs a network and a service that may or may not answer; the
/// splitting is arithmetic, and it is the half that decides whether a run of
/// five thousand lines is forty requests or five thousand.
/// </summary>
public static class Batches
{
    /// <summary>
    /// How many characters one request may carry.
    /// <para/>
    /// Deliberately well under what the endpoint accepts. The limit is on the
    /// whole request, the text is escaped on the way out — a single character
    /// can become nine — and a line that is over the limit after escaping is
    /// not refused politely, it comes back wrong.
    /// </summary>
    public const int Characters = 1800;

    /// <summary>
    /// How many lines one request may carry, whatever their length.
    /// <para/>
    /// A pack has thousands of one-word lines ("Yes.", "Leave.") and without
    /// this a single request would carry hundreds of them. Every line in a
    /// failed request is retried together, so a batch is also the unit of
    /// what gets lost and repeated.
    /// </summary>
    public const int Lines = 40;

    /// <summary>
    /// Group <paramref name="texts"/> into batches, in order, as indices into
    /// the list given.
    /// <para/>
    /// Indices rather than the strings themselves, because the caller has to
    /// put each answer back against the key it came from — and two identical
    /// lines in a pack are two keys, not one.
    /// </summary>
    public static List<List<int>> Of(IReadOnlyList<string>? texts)
    {
        var batches = new List<List<int>>();
        if (texts == null || texts.Count == 0) return batches;

        var current = new List<int>();
        int size = 0;

        for (int i = 0; i < texts.Count; i++)
        {
            int length = (texts[i] ?? "").Length;

            // A line that will not fit in an empty batch goes on its own rather
            // than being cut in half. It may well come back badly translated;
            // it will not come back as two halves of a sentence.
            bool wouldOverflow = current.Count > 0
                                 && (size + length > Characters || current.Count >= Lines);

            if (wouldOverflow)
            {
                batches.Add(current);
                current = new List<int>();
                size = 0;
            }

            current.Add(i);
            size += length;
        }

        if (current.Count > 0) batches.Add(current);
        return batches;
    }
}
