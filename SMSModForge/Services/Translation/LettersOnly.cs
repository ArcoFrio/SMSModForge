using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SMSModForge.Validation;

namespace SMSModForge.Services.Translation;

/// <summary>
/// A line whose words are kept and only its letters change (the author,
/// 1.6.3): a made-up language, say, which a translator would read as whatever
/// real words it resembles.
/// <para/>
/// Where the language is written in the same alphabet, the line is left as it
/// is. Where it has an alphabet of its own, each word is spelled out in it -
/// asked of the translator one word at a time, framed as somebody's name
/// (<see cref="PackNames.SuggestionTexts"/>), which is how a translator is got
/// to spell a word rather than translate it - and the line is put back
/// together word for word, its tags, gaps and punctuation where they were.
/// </summary>
public static class LettersOnly
{
    /// <summary>A word: letters, with an apostrophe or a hyphen allowed between
    /// two of them - "Vel'nar" and "ith-ril" are one word each.</summary>
    private static readonly Regex Word =
        new(@"\p{L}(?:[\p{L}\p{M}]|['’\-](?=\p{L}))*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The words of <paramref name="texts"/> that need spelling out: every
    /// distinct one written in Latin letters, in the order they are met. What a
    /// tag or a <c>{gap}</c> holds is not a word of the line and is left out.
    /// </summary>
    public static List<string> Words(IEnumerable<string> texts)
    {
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string text in texts ?? Enumerable.Empty<string>())
        {
            var masked = ProtectedText.Protect(text);
            foreach (Match m in Word.Matches(masked.Text))
                if (IsLatin(m.Value) && seen.Add(m.Value)) words.Add(m.Value);
        }
        return words;
    }

    /// <summary>
    /// <paramref name="text"/> with each word that has a spelling in
    /// <paramref name="spelled"/> written that way, and everything else - the
    /// words it has none for, spaces, punctuation, tags and gaps - as it was.
    /// </summary>
    public static string Respell(string text, IReadOnlyDictionary<string, string> spelled)
    {
        if (string.IsNullOrEmpty(text) || spelled == null || spelled.Count == 0) return text ?? "";
        var masked = ProtectedText.Protect(text);
        string respelled = Word.Replace(masked.Text,
            m => spelled.TryGetValue(m.Value, out var s) && !string.IsNullOrWhiteSpace(s) ? s : m.Value);
        return ProtectedText.Restore(respelled, masked.Codes, null);
    }

    /// <summary>Whether a line was spelled out in full: none of its words is
    /// still in Latin letters.</summary>
    public static bool Complete(string text, IReadOnlyDictionary<string, string> spelled)
        => Words(new[] { text }).All(w => spelled.TryGetValue(w, out var s) && !string.IsNullOrWhiteSpace(s));

    private static bool IsLatin(string word)
        => word.Any(c => char.IsLetter(c) && LanguageGuess.ScriptOf(c) == LanguageGuess.Script.Latin);
}
