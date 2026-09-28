using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Reads what the free translation endpoint sends back.
/// <para/>
/// It is not a documented API and its reply is not a documented shape: a
/// nested array of arrays with nulls in it, which differs depending on how
/// many texts went out and how the service felt about segmenting them. So this
/// is written to recognise the shapes it is known to answer in and to refuse
/// anything else, rather than to dig hopefully through whatever arrived.
/// <para/>
/// <b>Refusing is the important half.</b> A reply that cannot be read as
/// exactly the lines that were sent is not partially useful — there is no way
/// to tell which answer belongs to which line, and lining them up anyway would
/// file one line's words under another line's key, silently, for the rest of
/// the pack. So it returns null and the run stops, keeping what it had.
/// <para/>
/// Separate from anything that makes a request, because this is the part that
/// can be checked: the replies below were recorded, and a change to the
/// service shows up here as a test failure rather than as a pack full of
/// misaligned dialogue.
/// </summary>
public static class GoogleReply
{
    /// <summary>
    /// The translated lines, or null when the reply cannot be read as exactly
    /// <paramref name="expected"/> of them.
    /// </summary>
    public static IReadOnlyList<string>? Parse(string? json, int expected)
    {
        if (string.IsNullOrWhiteSpace(json) || expected <= 0) return null;

        JToken root;
        try { root = JToken.Parse(json); }
        catch (Newtonsoft.Json.JsonException) { return null; }

        if (!(root is JArray outer) || outer.Count == 0) return null;

        // Shape one: several texts sent, and the reply is a flat list of
        // strings, one per text.
        if (outer.Count == expected && AllStrings(outer))
            return Strings(outer);

        // Shape two: one text sent, and the reply is the segmented form -
        // [[["translated","original",...],["more","more",...]],null,"en",...].
        // The segments are the sentences it split the text into, and they are
        // one line again once joined.
        if (expected == 1 && outer[0] is JArray segments)
        {
            var made = new System.Text.StringBuilder();
            bool any = false;
            foreach (var segment in segments)
            {
                if (!(segment is JArray parts) || parts.Count == 0) continue;
                if (parts[0].Type != JTokenType.String) continue;
                made.Append((string)parts[0]);
                any = true;
            }
            if (any) return new[] { made.ToString() };
        }

        // Anything else. Deliberately not guessed at - see the type doc.
        return null;
    }

    /// <summary>
    /// Whether a reply is Google's page for a connection it has stopped
    /// serving, rather than a translation or an ordinary refusal.
    /// <para/>
    /// Recorded from the real thing. Both endpoints answer a flagged connection
    /// with an HTML page instead of JSON: one titled "Sorry..." saying the
    /// network "may be sending automated queries", the other built around a
    /// captcha. Both come with a 429, the same status as an ordinary "slow
    /// down" - and the difference matters, because a slow-down is worth waiting
    /// out and a block is not. Retrying into a block is more automated queries
    /// from a connection already accused of sending them, and the one thing
    /// that makes it last longer.
    /// </summary>
    public static bool IsBlockPage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;

        // JSON first: a translation that happens to contain the word "captcha"
        // is a translation, not a block.
        string start = body.TrimStart();
        if (start.StartsWith("[", System.StringComparison.Ordinal)
            || start.StartsWith("{", System.StringComparison.Ordinal))
            return false;

        return Contains(body, "captcha")
            || Contains(body, "automated queries")   // English on purpose: Google's own page, matched, never shown
            || Contains(body, "unusual traffic")     // English on purpose: Google's own page, matched, never shown
            || Contains(body, "<title>Sorry");
    }

    private static bool Contains(string body, string what)
        => body.IndexOf(what, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool AllStrings(JArray array)
    {
        foreach (var item in array)
            if (item.Type != JTokenType.String) return false;
        return true;
    }

    private static string[] Strings(JArray array)
    {
        var made = new string[array.Count];
        for (int i = 0; i < array.Count; i++) made[i] = (string)array[i] ?? "";
        return made;
    }
}
