using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Telling a translator who a line is about, for the languages whose words
/// change with it.
/// <para/>
/// "I'm tired, but I'm ready" is "Я устал, но я готов" from a man and "Я
/// устала, но я готова" from a woman; "Anna is my best friend" is "mi mejor
/// amiga". A translator is told neither - it sees one line, and a character's
/// name as a marker - and it guesses male. So each line goes out with a hint
/// in front of it, "She says:", from its speaker's pronouns, and each name the
/// translation keeps with "(she)" or "(he)" after it. Each hint sits between
/// markers of its own, and everything between them is cut off the answer
/// before the line is put back together.
/// <para/>
/// <b>Measured on the Google Translate website, 2026-09-27</b>, not assumed.
/// Russian: "Я устала, но я готова", "%%0%% (она) устала, но готова", "уже
/// ушла домой?", "вернулась". Spanish: "Estoy cansada, pero estoy lista", "mi
/// mejor amiga", "el nuevo maestro". The markers came back where they were put,
/// every time. Without the hints, every one of those was masculine - and a hint
/// in a sentence of its own ("%%0%% is a woman.") changed nothing, because a
/// marker is not a word the translator can agree anything with.
/// <para/>
/// It is not perfect: a line of two sentences can lose the speaker by the
/// second ("Estoy listo ahora"). That is a translator's miss on a line it was
/// told about, where before it was a miss on every line.
/// <para/>
/// <b>What it will not do</b> is put a hint's words into a line. A hint whose
/// markers come back out of place - the translator moved a word across one, or
/// dropped one - is not cut out by guesswork: <see cref="Remove"/> refuses the
/// line, and <see cref="TranslationRun"/> asks for it again without hints.
/// </summary>
public static class GenderHints
{
    /// <summary>The markers a hint sits between. Clear of the codes' (0
    /// upward), the lines' (8000 upward) and the break's (9000).</summary>
    public const int OpenNumber = 9501, CloseNumber = 9502;

    public static string Open => ProtectedText.Marker(OpenNumber);
    public static string Close => ProtectedText.Marker(CloseNumber);

    /// <summary>
    /// The languages whose words change with who is speaking or being spoken
    /// of - a verb's ending, an adjective's, a noun for a person. Only these
    /// are sent hints: to the others a hint is a chance of a damaged line
    /// that buys nothing.
    /// </summary>
    private static readonly HashSet<string> Agreeing = new(StringComparer.OrdinalIgnoreCase)
    {
        "ru", "uk", "be", "pl", "cs", "sk", "sl", "hr", "sr", "bs", "bg", "mk", "lt", "lv",
        "es", "pt", "fr", "it", "ca", "gl", "ro", "de", "he", "ar", "hi", "ur",
    };

    /// <summary>Whether lines into <paramref name="to"/> are sent hints.</summary>
    public static bool Needed(string? to) => Agreeing.Contains(PluralRules.LanguageOf(to ?? ""));

    /// <summary>A hint's words, in the language the pack is written in.</summary>
    public sealed record Words(string SheSays, string HeSays, string She, string He);

    /// <summary>
    /// The hints in each language a pack can be written in that has them: the
    /// translator reads the hint in the language it is translating FROM.
    /// </summary>
    // English on purpose: like every entry here, in the language it is for - sent to the translator with the line, cut off its answer, never shown.
    private static readonly Dictionary<string, Words> InLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new("She says:", "He says:", "(she)", "(he)"),
        ["pt"] = new("Ela diz:", "Ele diz:", "(ela)", "(ele)"),
        ["es"] = new("Ella dice:", "Él dice:", "(ella)", "(él)"),
        ["fr"] = new("Elle dit :", "Il dit :", "(elle)", "(il)"),
        ["de"] = new("Sie sagt:", "Er sagt:", "(sie)", "(er)"),
        ["ru"] = new("Она говорит:", "Он говорит:", "(она)", "(он)"),
    };

    /// <summary>The hints for a pack written in <paramref name="from"/>, or null
    /// when there are none in that language - its lines then go without.</summary>
    public static Words? WordsFor(string? from)
        => InLanguage.TryGetValue(PluralRules.LanguageOf(from ?? ""), out var w) ? w : null;

    /// <summary>
    /// Who says which line, and how each kept name is spoken of. Built from a
    /// pack by <see cref="For"/>; a key or a name it does not have gets no hint.
    /// </summary>
    public sealed class Who
    {
        public IReadOnlyDictionary<string, Pronouns> Speakers { get; init; }
            = new Dictionary<string, Pronouns>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, Pronouns> Names { get; init; }
            = new Dictionary<string, Pronouns>(StringComparer.Ordinal);

        /// <summary>The language the pack is written in, which the hints are
        /// written in too - known even when the translator is left to detect
        /// it; null to go by what the translator is told.</summary>
        public string? WrittenIn { get; init; }
    }

    /// <summary>
    /// The line with its hints: the speaker's in front, each name's after it.
    /// Only male and female are hinted - neutral and unchosen leave the
    /// language to speak of them as it does of anybody it is not told about.
    /// </summary>
    public static string Add(string masked, ProtectedText.Masked codes, Pronouns speaker, Who who, Words words)
    {
        if (string.IsNullOrEmpty(masked)) return masked ?? "";
        var sb = new StringBuilder(masked.Length + 32);

        string? says = speaker == Pronouns.Female ? words.SheSays : speaker == Pronouns.Male ? words.HeSays : null;
        if (says != null) sb.Append(Open).Append(' ').Append(says).Append(' ').Append(Close).Append(' ');

        if (!codes.HasNames)
        {
            sb.Append(masked);
            return sb.ToString();
        }

        // After each name's marker. The masked text holds exactly the markers
        // Protect put there, so each one is found as written.
        int at = 0;
        foreach (Match m in MarkerAt.Matches(masked))
        {
            if (!int.TryParse(m.Groups[1].Value, out int n) || n < 0 || n >= codes.Codes.Count) continue;
            if (n >= codes.IsName.Count || !codes.IsName[n]) continue;
            if (!who.Names.TryGetValue(codes.Codes[n], out var p)) continue;
            string? hint = p == Pronouns.Female ? words.She : p == Pronouns.Male ? words.He : null;
            if (hint == null) continue;
            sb.Append(masked, at, m.Index + m.Length - at);
            sb.Append(' ').Append(Open).Append(' ').Append(hint).Append(' ').Append(Close);
            at = m.Index + m.Length;
        }
        sb.Append(masked, at, masked.Length - at);
        return sb.ToString();
    }

    /// <summary>Whether a line was sent any hint - the answer then has some to
    /// take out.</summary>
    public static bool Has(string? sent) => sent != null && sent.IndexOf(Open, StringComparison.Ordinal) >= 0;

    private static readonly Regex MarkerAt = new(@"%%(\d{1,4})%%", RegexOptions.CultureInvariant);

    /// <summary>A hint's marker as it may come back: the same forgiveness
    /// <see cref="ProtectedText"/> gives every marker.</summary>
    private static readonly Regex OpenBack = new(@"%\s*%?\s*9501\s*%?\s*%", RegexOptions.CultureInvariant);
    private static readonly Regex CloseBack = new(@"%\s*%?\s*9502\s*%?\s*%", RegexOptions.CultureInvariant);
    private static readonly Regex AnyMarker = new(@"%\s*%?\s*\d{1,4}\s*%?\s*%", RegexOptions.CultureInvariant);

    /// <summary>A hint is a few words. Anything longer between its markers is a
    /// line's own words that ended up inside them.</summary>
    private const int LongestHint = 40;

    /// <summary>
    /// The answer with every hint taken out - or null when a hint cannot be
    /// taken out for certain: a marker lost or doubled, words of the line
    /// inside one, nothing inside one (its words moved outside), a speaker's
    /// hint anywhere but first, or a name's anywhere but straight after a
    /// marker. Null means ask again without hints; it never means cut and hope.
    /// </summary>
    /// <param name="sent">What was sent, to know how many hints came back, and
    /// whether the first was the speaker's.</param>
    public static string? Remove(string? answer, string sent)
    {
        if (answer == null) return null;
        int wanted = Count(OpenBack, sent);
        if (Count(OpenBack, answer) != wanted || Count(CloseBack, answer) != wanted) return null;
        if (wanted == 0) return answer;

        bool speakerFirst = sent.StartsWith(Open, StringComparison.Ordinal);
        var result = new StringBuilder(answer.Length);
        int at = 0;
        bool first = true;
        while (true)
        {
            var open = OpenBack.Match(answer, at);
            if (!open.Success) break;
            var close = CloseBack.Match(answer, open.Index + open.Length);
            if (!close.Success) return null;
            // Closed before the next one opens, or the two are tangled.
            var nextOpen = OpenBack.Match(answer, open.Index + open.Length);
            if (nextOpen.Success && nextOpen.Index < close.Index) return null;

            string inside = answer.Substring(open.Index + open.Length, close.Index - open.Index - open.Length).Trim();
            if (AnyMarker.IsMatch(inside) || inside.Length == 0 || inside.Length > LongestHint) return null;

            string before = answer.Substring(at, open.Index - at);
            if (first && speakerFirst)
            {
                // The speaker's: nothing of the line may come before it.
                if (before.Trim().Length > 0) return null;
            }
            else if (!MarkerAtEnd.IsMatch(before))
            {
                // A name's: straight after the name's marker, or it is not
                // known what it was attached to.
                return null;
            }
            // The hint goes, and the space in front of it with it.
            result.Append(before.TrimEnd());
            at = close.Index + close.Length;
            first = false;
        }

        string rest = answer.Substring(at);
        // A word straight after a name's hint still needs its space.
        if (result.Length > 0 && rest.Length > 0 && char.IsLetterOrDigit(rest[0])) result.Append(' ');
        result.Append(rest);

        // A speaker's hint was the start of the line: what follows it is. A
        // colon left there is the hint's ("She says:"), moved out of its markers.
        string cleaned = result.ToString();
        return speakerFirst ? cleaned.TrimStart().TrimStart(':', '：').TrimStart() : cleaned;
    }

    private static int Count(Regex marker, string text) => marker.Matches(text).Count;

    private static readonly Regex MarkerAtEnd = new(@"%\s*%?\s*\d{1,4}\s*%?\s*%\s*$", RegexOptions.CultureInvariant);

    // ── From a pack ──────────────────────────────────────────────────

    /// <summary>
    /// Who says each of the pack's lines, and how each name the translation can
    /// keep is spoken of: the pack's own characters by the pronouns chosen for
    /// them, the game's - in the pack's lines and in the game's lines of a
    /// conversation the pack extends - by the game's.
    /// </summary>
    public static Who For(ModPack pack)
    {
        var speakers = new Dictionary<string, Pronouns>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, Pronouns>(StringComparer.Ordinal);
        if (pack == null) return new Who { Speakers = speakers, Names = names };
        string writtenIn = pack.OwnLanguage;

        var byKey = new Dictionary<string, CharacterDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in pack.Characters)
            if (!string.IsNullOrEmpty(c.Key)) byKey[c.Key] = c;

        Pronouns OfSpeaker(string? actor)
        {
            if (string.IsNullOrWhiteSpace(actor)) return Pronouns.Unset;
            if (byKey.TryGetValue(actor, out var c)) return c.EffectivePronouns;
            if (string.Equals(actor, CharacterDef.PlayerKey, StringComparison.OrdinalIgnoreCase)) return VanillaPronouns.Player;
            return VanillaPronouns.Of(actor);
        }

        var json = Newtonsoft.Json.Linq.JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in PackTexts.Of(json, withEmpty: true))
        {
            taken.Add(site.Key);
            if (site.Kind != PackTexts.Kind.Line) continue;
            var p = OfSpeaker(site.Speaker);
            if (p != Pronouns.Unset) speakers[site.Key] = p;
        }
        foreach (var line in GameLines.Of(pack, taken))
        {
            string actor = line.Node.Actor ?? "";
            // The game's own "You" and "Player" are the player.
            var p = actor is "You" or "Player" ? VanillaPronouns.Player : OfSpeaker(actor);
            if (p != Pronouns.Unset) speakers[line.Key] = p;
        }

        // The game's first, the pack's over them: a pack character named like
        // one of the game's is the one its lines mean.
        foreach (string name in CastNames.All)
        {
            var p = VanillaPronouns.OfGivenName(name);
            if (p != Pronouns.Unset) names[name] = p;
        }
        foreach (var c in pack.Characters)
        {
            string name = (c.DisplayName ?? "").Trim();
            if (name.Length == 0 || c.IsPlayer) continue;
            var p = c.EffectivePronouns;
            if (p == Pronouns.Unset) names.Remove(name);
            else names[name] = p;
        }
        return new Who { Speakers = speakers, Names = names, WrittenIn = writtenIn };
    }
}
