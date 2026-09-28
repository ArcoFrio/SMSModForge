using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SMSModForge.Validation;

/// <summary>
/// Whether a text is plainly NOT in the language its pack says it is written
/// in - for the warning that catches an author typing Spanish into the pack's
/// own words while meaning to type it into the Spanish translation.
/// <para/>
/// Two kinds of evidence, and only two, because a wrong warning on somebody's
/// correct line teaches them to stop reading warnings:
/// <list type="bullet">
/// <item><b>The alphabet.</b> Japanese kana, Korean hangul, Chinese characters,
/// Cyrillic, Greek, Arabic, Hebrew, Thai and Devanagari cannot be mistaken for
/// one another or for the Latin alphabet. Certain.</item>
/// <item><b>Little words, between English, Spanish and Portuguese only.</b> "the",
/// "and", "you" against "el", "los", "¿" against "você", "não", "uma". Only
/// these three because they are the three with enough real text to measure the
/// guess against: the game's own English lines, where it must never fire, and
/// ModForge's shipped Spanish and Portuguese, where it must. A guess between
/// French and Italian that nobody has measured is not written.</item>
/// </list>
/// Anything short is not judged at all: "OK!" is every language.
/// </summary>
public static class LanguageGuess
{
    public enum Script { None, Latin, Cyrillic, Greek, Hangul, Kana, Han, Arabic, Hebrew, Thai, Devanagari }

    /// <summary>What a text looks like instead of the pack's language.</summary>
    /// <param name="Language">The language it looks like, when that can be
    /// told; null when only "not the pack's" can.</param>
    public sealed record Finding(string? Language);

    /// <summary>Fewer letters than this is not a sentence to judge.</summary>
    public const int FewestLetters = 8;

    /// <summary>Fewer words than this is not a sentence to judge by its little words.</summary>
    public const int FewestWords = 5;

    /// <summary>Whether <paramref name="text"/> has enough letters to be
    /// judged at all.</summary>
    public static bool Judgeable(string? text) => CountScripts(Plain(text)).Values.Sum() >= FewestLetters;

    /// <summary>
    /// How <paramref name="text"/> is not in <paramref name="own"/>, or null
    /// when it is, or could be, or is too short to say.
    /// </summary>
    public static Finding? NotIn(string? text, string own)
    {
        string plain = Plain(text);
        var expected = ScriptsOf(own);
        if (expected == null) return null;   // a language whose alphabet this does not know

        var counts = CountScripts(plain);
        int letters = counts.Values.Sum();
        if (letters < FewestLetters) return null;

        int inExpected = counts.Where(c => expected.Contains(c.Key)).Sum(c => c.Value);

        // Kana never appears in Chinese or Korean: two of them are Japanese.
        if (!expected.Contains(Script.Kana) && counts.GetValueOrDefault(Script.Kana) >= 2)
            return new Finding("ja");

        if (inExpected * 10 >= letters * 3)
        {
            // Mostly the right alphabet. Within the Latin one, the little words.
            if (!expected.Contains(Script.Latin)) return null;
            string? guess = LatinGuess(plain, own);
            return guess == null ? null : new Finding(guess);
        }

        // Mostly another alphabet. Say which language only where the alphabet
        // says it: hangul is Korean.
        //
        // Latin letters in a pack written in another alphabet are the one
        // exception to "certain": a name, "ModForge", a [PV:shells] the author
        // spelled out - a Chinese line can be mostly Latin letters and still
        // be Chinese. Only a line whose little words say it is English,
        // Spanish or Portuguese is called that.
        var main = counts.OrderByDescending(c => c.Value).First().Key;
        if (main == Script.Latin)
        {
            string? latin = LatinGuess(plain, own, anyOwn: true);
            return latin == null ? null : new Finding(latin);
        }
        return main == Script.Hangul ? new Finding("ko") : new Finding(null);
    }

    // ── The alphabet ─────────────────────────────────────────────────

    /// <summary>The alphabets a language is written in, or null for a language
    /// this does not know the alphabet of - which is then never judged.</summary>
    public static HashSet<Script>? ScriptsOf(string code)
    {
        string language = Shared.PluralRules.LanguageOf(code ?? "");
        switch (language)
        {
            case "ja": return new() { Script.Kana, Script.Han };
            case "zh": return new() { Script.Han };
            case "ko": return new() { Script.Hangul, Script.Han };
            case "ru": case "uk": case "be": case "bg": case "sr": case "mk":
            case "kk": case "ky": case "mn": case "tg":
                return new() { Script.Cyrillic };
            case "el": return new() { Script.Greek };
            case "ar": case "fa": case "ur": case "ps": return new() { Script.Arabic };
            case "he": case "yi": return new() { Script.Hebrew };
            case "th": return new() { Script.Thai };
            case "hi": case "mr": case "ne": case "sa": return new() { Script.Devanagari };
        }
        return LatinLanguages.Contains(language) ? new() { Script.Latin } : null;
    }

    private static readonly HashSet<string> LatinLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en", "es", "pt", "fr", "de", "it", "nl", "pl", "cs", "sk", "sl", "hr", "bs", "ro", "hu",
        "fi", "et", "lv", "lt", "sv", "da", "nb", "nn", "no", "is", "fo", "ga", "cy", "eu", "ca",
        "gl", "tr", "az", "uz", "id", "ms", "vi", "tl", "fil", "sw", "af", "sq", "mt", "lb", "eo",
    };

    public static Script ScriptOf(char c)
    {
        if (!char.IsLetter(c)) return Script.None;
        int u = c;
        if (u < 0x0250 || (u >= 0x1E00 && u <= 0x1EFF)) return Script.Latin;
        if ((u >= 0x0370 && u <= 0x03FF) || (u >= 0x1F00 && u <= 0x1FFF)) return Script.Greek;
        if (u >= 0x0400 && u <= 0x052F) return Script.Cyrillic;
        if (u >= 0x0590 && u <= 0x05FF) return Script.Hebrew;
        if ((u >= 0x0600 && u <= 0x06FF) || (u >= 0x0750 && u <= 0x077F)) return Script.Arabic;
        if (u >= 0x0900 && u <= 0x097F) return Script.Devanagari;
        if (u >= 0x0E00 && u <= 0x0E7F) return Script.Thai;
        if ((u >= 0x1100 && u <= 0x11FF) || (u >= 0x3130 && u <= 0x318F) || (u >= 0xAC00 && u <= 0xD7AF)) return Script.Hangul;
        if ((u >= 0x3040 && u <= 0x30FF) || (u >= 0x31F0 && u <= 0x31FF) || (u >= 0xFF66 && u <= 0xFF9F)) return Script.Kana;
        if ((u >= 0x4E00 && u <= 0x9FFF) || (u >= 0x3400 && u <= 0x4DBF) || (u >= 0xF900 && u <= 0xFAFF)) return Script.Han;
        return Script.None;
    }

    private static Dictionary<Script, int> CountScripts(string text)
    {
        var counts = new Dictionary<Script, int>();
        foreach (char c in text)
        {
            var s = ScriptOf(c);
            if (s == Script.None) continue;
            counts[s] = counts.GetValueOrDefault(s) + 1;
        }
        return counts;
    }

    // ── The little words ─────────────────────────────────────────────

    /// <summary>
    /// Words that are common in one of the three languages and are not words at
    /// all in the other two - "no", "a", "de", "as", "do" and "me" are in two
    /// or more of them, and so are in none of these lists.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> LittleWords = new()
    {
        ["en"] = new(StringComparer.Ordinal)
        {
            "the", "and", "you", "is", "are", "was", "were", "what", "this", "that", "with", "have",
            "not", "for", "it's", "i'm", "don't", "your", "just", "but", "of", "to", "my", "we",
            "be", "can", "will", "there", "it", "she", "they", "i", "if", "so", "how", "why",
            "all", "out", "about", "would", "could", "know", "think", "want", "get", "got",
        },
        ["es"] = new(StringComparer.Ordinal)
        {
            "el", "los", "las", "del", "una", "es", "pero", "muy", "qué", "cómo", "y", "tú",
            "eres", "estoy", "tengo", "esto", "eso", "también", "aquí", "sí", "ella", "ellos",
            "puedes", "quieres", "ahora", "cuando", "donde", "dónde",
            "hacer", "tiene", "tienes", "gracias", "hola", "bueno", "entonces", "hasta",
        },
        ["pt"] = new(StringComparer.Ordinal)
        {
            "os", "das", "uma", "não", "é", "você", "vocês", "muito", "muita", "em", "na",
            "nas", "ao", "à", "isso", "isto", "também", "eu", "ele", "estou", "tenho", "são",
            "então", "já", "até", "pra", "meu", "minha", "com", "agora",
            "quando", "onde", "fazer", "tem", "obrigado", "obrigada", "olá", "bom",
        },
    };

    private static readonly Regex Word = new(@"[\p{L}']+", RegexOptions.Compiled);

    /// <summary>
    /// Which of English, Spanish and Portuguese a Latin-alphabet text is
    /// plainly in instead of <paramref name="own"/>, or null.
    /// <para/>
    /// Plainly: at least three of that language's little words, none of the
    /// pack's own, and twice as many as either of the others. With
    /// <paramref name="anyOwn"/> the pack's own is not one of the three (a
    /// Japanese pack's line in the Latin alphabet), and only the first two
    /// conditions that make sense apply.
    /// </summary>
    private static string? LatinGuess(string plain, string own, bool anyOwn = false)
    {
        string ownLanguage = Shared.PluralRules.LanguageOf(own ?? "");
        if (!anyOwn && !LittleWords.ContainsKey(ownLanguage)) return null;   // not one we have measured

        var words = Word.Matches(plain.ToLowerInvariant()).Select(m => m.Value.Trim('\'')).Where(w => w.Length > 0).ToList();
        if (words.Count < FewestWords) return null;

        var score = LittleWords.ToDictionary(l => l.Key, l => words.Count(w => l.Value.Contains(w)));
        // Spanish opens a question and an exclamation; nothing else here does.
        score["es"] += plain.Count(c => c == '¿' || c == '¡');

        if (!anyOwn && score[ownLanguage] > 0) return null;

        var best = score.Where(s => s.Key != ownLanguage).OrderByDescending(s => s.Value).First();
        int others = score.Where(s => s.Key != best.Key && s.Key != ownLanguage).Select(s => s.Value).DefaultIfEmpty(0).Max();
        if (best.Value < 3 || best.Value < 2 * others) return null;
        return best.Key;
    }

    // ── Plain words ──────────────────────────────────────────────────

    private static readonly Regex Markup = new(@"<[^>]*>|\[[A-Za-z]+:[^\]]*\]|\{[^}]*\}", RegexOptions.Compiled);

    /// <summary>The words a player reads, without tags, variables or gaps -
    /// none of which are in any language.</summary>
    public static string Plain(string? text) => Markup.Replace(text ?? "", " ");
}
