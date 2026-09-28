using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Shared;
using SMSModForge.Validation;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Character names, kept out of a machine translation.
/// <para/>
/// A translator reads a name as the word it looks like: a character called
/// Hope comes back as the German for hope, and every line that mentions her
/// with it. So, unless the author asks for names to be translated, a name is
/// never sent at all:
/// <list type="bullet">
/// <item>A character's name field is filled in directly - as written, in a
/// language that uses the same alphabet; as the author has checked it, in one
/// that does not.</item>
/// <item>In a line, each name is taken out before the line is sent and put back
/// afterwards, the same way a <c>{name}</c> or a tag is
/// (<see cref="ProtectedText"/>) - in that language's spelling. A line the
/// translator hands back without one of its names is not written, so a name
/// cannot end up anywhere but where the translator put it.</item>
/// </list>
/// <para/>
/// Only capitalised names, and only whole words: "Kiki" and "Kiki's", but not
/// "kiki", and not the "Kiki" inside "Kikis". A lower-case word that happens to
/// be somebody's name is left to the translator.
/// </summary>
public static class PackNames
{
    /// <summary>
    /// Whether a text of the pack is a character's name field. Not the
    /// player's: every pack has them, called "You" - which is the word, not a
    /// name, and is translated like any other; kept as a name, it would stay in
    /// English at the start of every sentence that begins with it.
    /// </summary>
    public static bool IsNameKey(string? key)
        => key != null && key.StartsWith("character.", StringComparison.Ordinal)
                       && key.EndsWith(".name", StringComparison.Ordinal)
                       && key != PlayerNameKey;

    private static readonly string PlayerNameKey = "character." + PackTexts.Segment(Model.CharacterDef.PlayerKey) + ".name";

    /// <summary>
    /// The names to keep: every character of the pack's, and every one of the
    /// game's own cast that a line of the pack mentions. Longest first, so
    /// "Nurse Nina" is found before "Nina".
    /// </summary>
    public static List<string> Of(TextFile? source)
    {
        var names = new List<string>();
        if (source == null) return names;

        void Add(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }

        foreach (var e in source.Entries)
            if (IsNameKey(e.Key)) Add(e.Text);

        // The game's own characters, by their names only - not the descriptions
        // the game files some of them under, which are words.
        var lines = source.Entries.Where(e => !IsNameKey(e.Key) && !string.IsNullOrEmpty(e.Text))
                                  .Select(e => e.Text).ToList();
        foreach (string cast in CastNames.All)
            if (lines.Any(line => Find(line, new[] { cast }).Count > 0)) Add(cast);

        return names.OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>The names of the pack's own characters, as their name fields
    /// hold them - the ones only the author can spell.</summary>
    public static HashSet<string> OwnNames(TextFile? source)
    {
        var own = new HashSet<string>(StringComparer.Ordinal);
        if (source == null) return own;
        foreach (var e in source.Entries)
            if (IsNameKey(e.Key) && !string.IsNullOrWhiteSpace(e.Text)) own.Add(e.Text.Trim());
        return own;
    }

    /// <summary>One name found in a line.</summary>
    public readonly record struct Found(int At, int Length, string Name);

    /// <summary>
    /// Where <paramref name="names"/> are in <paramref name="text"/>:
    /// capitalised, whole words, never overlapping - the longer name wins.
    /// In the order they appear.
    /// </summary>
    public static List<Found> Find(string? text, IReadOnlyList<string>? names)
    {
        var found = new List<Found>();
        if (string.IsNullOrEmpty(text) || names == null || names.Count == 0) return found;

        var taken = new bool[text.Length];
        foreach (string name in names.OrderByDescending(n => n?.Length ?? 0))
        {
            // Only a name written with a capital is looked for at all.
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) continue;

            int from = 0;
            while (from <= text.Length - name.Length)
            {
                int at = text.IndexOf(name, from, StringComparison.Ordinal);
                if (at < 0) break;
                int end = at + name.Length;
                bool whole = (at == 0 || !IsWordChar(text[at - 1])) && (end == text.Length || !IsWordChar(text[end]));
                bool free = true;
                for (int i = at; i < end && free; i++) if (taken[i]) free = false;
                if (whole && free)
                {
                    for (int i = at; i < end; i++) taken[i] = true;
                    found.Add(new Found(at, name.Length, name));
                    from = end;
                }
                else from = at + 1;
            }
        }
        found.Sort((a, b) => a.At.CompareTo(b.At));
        return found;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Whether a name has to be spelled anew in <paramref name="code"/>: the
    /// language is written in an alphabet of its own. A language written in
    /// the Latin alphabet, or one this does not know the alphabet of, keeps a
    /// name exactly as it is written.
    /// </summary>
    public static bool NeedsSpelling(string? code)
    {
        var scripts = LanguageGuess.ScriptsOf(code ?? "");
        return scripts != null && !scripts.Contains(LanguageGuess.Script.Latin);
    }

    // ── Asking the translator how a name is spelled ─────────────────────

    /// <summary>
    /// Sentences that make a word plainly a person's name, so a translator
    /// spells it in its alphabet rather than translating it. Each is asked
    /// twice: with the name, and with a marker in its place - which tells
    /// where the name sits in the answer.
    /// </summary>
    private static readonly string[] Frames =
    {
        "My name is {0}.",   // English on purpose: sent from "en" to the translator, never shown
        "Hello, {0}!",       // English on purpose: sent from "en" to the translator, never shown
    };

    /// <summary>What to send to ask for <paramref name="names"/>' spellings:
    /// each frame with the marker, then each frame with each name.</summary>
    public static List<string> SuggestionTexts(IReadOnlyList<string> names)
    {
        var texts = new List<string>();
        foreach (string frame in Frames) texts.Add(string.Format(frame, ProtectedText.Marker(0)));
        foreach (string name in names)
            foreach (string frame in Frames) texts.Add(string.Format(frame, name));
        return texts;
    }

    /// <summary>
    /// The spellings the answers to <see cref="SuggestionTexts"/> suggest, by
    /// name - only those that could be read out and that are written in the
    /// language's own alphabet. A name the translator left as it was, or
    /// turned into a sentence, is not suggested at all.
    /// </summary>
    public static Dictionary<string, string> ReadSuggestions(IReadOnlyList<string> names,
                                                             IReadOnlyList<string>? answers, string code)
    {
        var suggested = new Dictionary<string, string>(StringComparer.Ordinal);
        if (names == null || answers == null || answers.Count != Frames.Length * (names.Count + 1)) return suggested;

        var scripts = LanguageGuess.ScriptsOf(code ?? "") ?? new HashSet<LanguageGuess.Script>();
        for (int n = 0; n < names.Count; n++)
        {
            for (int f = 0; f < Frames.Length; f++)
            {
                string? spelled = Between(answers[f], answers[Frames.Length * (n + 1) + f]);
                if (spelled == null || spelled.Length > names[n].Length * 4 + 8) continue;
                // In the language's own letters, and none of the Latin ones left.
                var letters = spelled.Where(char.IsLetter).Select(LanguageGuess.ScriptOf).ToList();
                if (letters.Count == 0 || letters.Any(s => s == LanguageGuess.Script.Latin)
                    || !letters.Any(scripts.Contains)) continue;
                suggested[names[n]] = spelled;
                break;
            }
        }
        return suggested;
    }

    /// <summary>What the answer has where the frame's answer has its marker,
    /// or null when the two do not share the words around it.</summary>
    private static string? Between(string? frame, string? answer)
    {
        if (string.IsNullOrEmpty(frame) || string.IsNullOrEmpty(answer)) return null;

        // Compared as plain text. Chinese and Japanese can end one of the two
        // answers with "！" or "。" and the other with "!" or "." - the same
        // sentence either way - and a comma or a space can differ the same way.
        // What a sentence starts and ends with is not compared at all: it is
        // punctuation.
        string loose = Loose(frame);
        // Any number of percent signs: Google answered "我叫 %%0%%%。" in
        // Chinese - one taken from the marker that divides it from the next
        // line - and every name was lost to that one sign.
        var marker = System.Text.RegularExpressions.Regex.Match(loose, @"%+\s*0\s*%+");
        if (!marker.Success) return null;
        string before = loose.Substring(0, marker.Index).Trim(Edge);
        string after = loose.Substring(marker.Index + marker.Length).Trim(Edge);
        string said = Loose(answer).Trim(Edge);
        if (!said.StartsWith(before, StringComparison.Ordinal) || !said.EndsWith(after, StringComparison.Ordinal)
            || said.Length < before.Length + after.Length)
            return null;
        string middle = said.Substring(before.Length, said.Length - before.Length - after.Length).Trim(Edge);
        return middle.Length == 0 ? null : middle;
    }

    /// <summary>
    /// What is taken off either end of a sentence, and of the name read out of
    /// it: spaces, the punctuation a sentence ends with, in its ordinary form,
    /// and a percent sign - a stray one from the marker next to it. None of
    /// them can begin or end a name.
    /// </summary>
    private static readonly char[] Edge = { ' ', '.', '!', '?', ',', ';', ':', '%' };

    /// <summary>
    /// The full-width forms of ordinary letters, digits and punctuation
    /// (！, ，, ％) and the ideographic full stop, comma and space written as
    /// the ordinary ones, character for character - the length does not change.
    /// </summary>
    private static string Loose(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= '\uFF01' && c <= '\uFF5E') sb.Append((char)(c - 0xFEE0));
            else if (c == '\u3002') sb.Append('.');
            else if (c == '\u3001') sb.Append(',');
            else if (c == '\u3000') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
