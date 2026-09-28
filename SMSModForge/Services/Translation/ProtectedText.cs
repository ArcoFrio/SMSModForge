using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Hides the parts of a line that must not be translated, and puts them back.
/// <para/>
/// A pack's text is not only words. <c>{name}</c> is filled in at runtime,
/// <c>&lt;b&gt;</c> is markup the game reads, <c>[PV:money]</c> is a token that
/// names a variable. A machine translator treats all three as words: it
/// translates the word inside the braces, it reorders or drops the tags, and
/// it has been known to put a space in the middle of a token. Every one of
/// those breaks the line in a way that only shows up in the game, in a language
/// the author does not read.
/// <para/>
/// So each code is taken out before the text is sent, replaced by a marker, and
/// put back afterwards. The markers are numbered, so a translator that reorders
/// them - which is correct, since word order differs between languages - puts
/// each code back where its own language wants it.
/// <para/>
/// <b>Putting them back is deliberately forgiving.</b> Translators do small
/// things to a marker: pad it with spaces, take the spaces away, occasionally
/// swap the brackets around. Matching only the exact marker would throw away
/// the whole line over a space. Matching too loosely would put a code back in
/// the wrong place, which is worse, because it looks fine.
/// <para/>
/// <b>And when it cannot, it says so.</b> A translator that drops a marker
/// entirely leaves the line with a gap nothing can fill. That line is reported
/// rather than written, because a translation missing its <c>{name}</c> is not
/// a rough translation, it is a broken one - and the whole point of doing this
/// by machine is that nobody is going to read all five thousand lines.
/// </summary>
public static class ProtectedText
{
    /// <summary>
    /// A line with its codes taken out, and the codes in the order they were
    /// found. <see cref="Marker"/> n stands for <c>Codes[n]</c>.
    /// </summary>
    public sealed record Masked(string Text, IReadOnlyList<string> Codes)
    {
        /// <summary>Nothing needed hiding, so nothing has to be put back.</summary>
        public bool Plain => Codes.Count == 0;

        /// <summary>Which codes are character names (<see cref="PackNames"/>),
        /// put back in the language's spelling rather than as they were.
        /// Empty when none are.</summary>
        public IReadOnlyList<bool> IsName { get; init; } = Array.Empty<bool>();

        /// <summary>Whether any code is a name.</summary>
        public bool HasNames
        {
            get { foreach (bool b in IsName) if (b) return true; return false; }
        }
    }

    /// <summary>
    /// What a code is replaced by while it is away.
    /// <para/>
    /// Percent signs and a number, and no letters at all: a marker made of
    /// letters is a word, and a word gets translated. This one has nothing in
    /// it a translator recognises, so it is carried through untouched - and on
    /// the rare occasion it is not, <see cref="Restore"/> knows the shapes it
    /// comes back in.
    /// </summary>
    public static string Marker(int index) => "%%" + index + "%%";

    /// <summary>
    /// The marker as it may come back: spaces worked into it, the percent signs
    /// doubled up or reduced to one. Everything except the number itself is
    /// allowed to have moved, because the number is the only part that carries
    /// meaning.
    /// </summary>
    private static readonly Regex MarkerBack =
        new(@"%\s*%?\s*(\d{1,4})\s*%?\s*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Take the codes out of a line.</summary>
    public static Masked Protect(string? text) => Protect(text, null);

    /// <summary>
    /// Take the codes out of a line, and the character names in
    /// <paramref name="names"/> with them (<see cref="PackNames.Find"/>). A name
    /// is only looked for between codes: one inside a <c>{gap}</c> or a tag is
    /// part of that code, and goes back with it.
    /// </summary>
    public static Masked Protect(string? text, IReadOnlyList<string>? names)
    {
        if (string.IsNullOrEmpty(text)) return new Masked(text ?? "", Array.Empty<string>());

        var codes = TextCodes.Of(text);
        if (names != null && names.Count > 0) return WithNames(text, codes, names);
        if (codes.Count == 0) return new Masked(text, Array.Empty<string>());

        // Walked rather than replaced one code at a time: the same code can
        // appear twice in a line ("{name} told {name}"), and replacing by value
        // would give both occurrences the first one's number.
        var sb = new StringBuilder(text.Length + codes.Count * 4);
        var kept = new List<string>(codes.Count);
        int at = 0;
        foreach (string code in codes)
        {
            int found = text.IndexOf(code, at, StringComparison.Ordinal);
            if (found < 0) continue;              // cannot happen; not worth crashing over
            sb.Append(text, at, found - at);
            sb.Append(Marker(kept.Count));
            kept.Add(code);
            at = found + code.Length;
        }
        sb.Append(text, at, text.Length - at);
        return new Masked(sb.ToString(), kept);
    }

    /// <summary>The codes and the names, walked in the order they are in the
    /// line: a name is looked for only in the stretches between codes.</summary>
    private static Masked WithNames(string text, IReadOnlyList<string> codes, IReadOnlyList<string> names)
    {
        var sb = new StringBuilder(text.Length + 8);
        var kept = new List<string>();
        var isName = new List<bool>();

        void Stretch(int from, int to)
        {
            string part = text.Substring(from, to - from);
            int at = 0;
            foreach (var name in PackNames.Find(part, names))
            {
                sb.Append(part, at, name.At - at);
                sb.Append(Marker(kept.Count));
                kept.Add(name.Name);
                isName.Add(true);
                at = name.At + name.Length;
            }
            sb.Append(part, at, part.Length - at);
        }

        int done = 0;
        foreach (string code in codes)
        {
            int found = text.IndexOf(code, done, StringComparison.Ordinal);
            if (found < 0) continue;
            Stretch(done, found);
            sb.Append(Marker(kept.Count));
            kept.Add(code);
            isName.Add(false);
            done = found + code.Length;
        }
        Stretch(done, text.Length);

        if (kept.Count == 0) return new Masked(text, Array.Empty<string>());
        return new Masked(sb.ToString(), kept) { IsName = isName };
    }

    /// <summary>
    /// Put the codes back into a translated line, each name in
    /// <paramref name="spellings"/>' spelling of it - or as it was written,
    /// when that has none.
    /// </summary>
    public static string Restore(string? translated, Masked masked, IReadOnlyDictionary<string, string>? spellings,
                                 IList<string>? lost)
    {
        if (masked == null || !masked.HasNames) return Restore(translated, masked?.Codes, lost);

        // A name the translator gave back twice is as wrong as one it lost:
        // the line would say somebody's name where they are not.
        var seenTimes = new int[masked.Codes.Count];
        foreach (System.Text.RegularExpressions.Match m in MarkerBack.Matches(translated ?? ""))
            if (int.TryParse(m.Groups[1].Value, out int n) && n >= 0 && n < seenTimes.Length) seenTimes[n]++;
        for (int i = 0; i < masked.Codes.Count; i++)
            if (i < masked.IsName.Count && masked.IsName[i] && seenTimes[i] > 1) lost?.Add(masked.Codes[i]);

        var back = new List<string>(masked.Codes.Count);
        for (int i = 0; i < masked.Codes.Count; i++)
        {
            string code = masked.Codes[i];
            bool name = i < masked.IsName.Count && masked.IsName[i];
            back.Add(name && spellings != null && spellings.TryGetValue(code, out var spelled)
                     && !string.IsNullOrWhiteSpace(spelled) ? spelled : code);
        }
        return Restore(translated, back, lost);
    }

    /// <summary>
    /// Put the codes back into a translated line.
    /// <para/>
    /// <paramref name="lost"/> receives any code the translated text has no
    /// marker for. A line with anything in that list has not been translated -
    /// it has been damaged - and the caller keeps the original instead.
    /// </summary>
    public static string Restore(string? translated, IReadOnlyList<string>? codes, IList<string>? lost)
    {
        if (codes == null || codes.Count == 0) return translated ?? "";
        if (string.IsNullOrEmpty(translated))
        {
            if (lost != null) foreach (string code in codes) lost.Add(code);
            return translated ?? "";
        }

        var seen = new bool[codes.Count];
        string put = MarkerBack.Replace(translated, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out int index)
                || index < 0 || index >= codes.Count)
                return match.Value;           // a number of the translator's own

            seen[index] = true;
            return codes[index];
        });

        if (lost != null)
            for (int i = 0; i < codes.Count; i++)
                if (!seen[i]) lost.Add(codes[i]);

        return put;
    }

    /// <summary>
    /// Whether a translated line kept everything the original had.
    /// <para/>
    /// The check that runs after the codes are back, on the finished line,
    /// rather than on the markers: it is the same question the translation
    /// checker asks about a file somebody translated by hand, and asking it the
    /// same way means a machine's work is held to the standard a person's is.
    /// </summary>
    public static bool KeptItsCodes(string? original, string? translated)
    {
        var was = TextCodes.Of(original ?? "");
        var now = TextCodes.Of(translated ?? "");
        if (was.Count != now.Count) return false;

        // Order is allowed to change - languages put things in different
        // places - so this is about which codes are there and how many of each.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string code in was) counts[code] = counts.TryGetValue(code, out int n) ? n + 1 : 1;
        foreach (string code in now)
        {
            if (!counts.TryGetValue(code, out int n) || n == 0) return false;
            counts[code] = n - 1;
        }
        return true;
    }
}
