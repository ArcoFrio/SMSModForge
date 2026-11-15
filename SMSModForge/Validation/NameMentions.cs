using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Validation;

/// <summary>
/// A change Validate can make for the author when they ask for it: one stretch of a
/// line's text written differently (1.9.0, the name token). Checked again against the
/// line before it is made, so a line edited since Validate ran is left alone.
/// </summary>
public sealed record TextFix(string Dialogue, long Node, int Start, string Was, string Becomes);

/// <summary>
/// The names of the game's characters in a pack's own texts (1.9.0).
/// <para/>
/// A pack can give one of the game's characters a new name (<see cref="CharacterDef.NewName"/>),
/// and a line naming them with <c>{name:key}</c> follows it, in this pack and every
/// other. A name typed out does not: it stays exactly as typed, which is what keeps
/// a pack's own Sakura safe from a rename of the game's. So each one typed out is
/// listed, with the words around it, and a line's can be switched to the token by
/// the author, one at a time. Nothing is changed unasked.
/// <para/>
/// Tied to one character, or not listed: a name two of the pack's characters
/// answer to (its own Sakura and the game's) is nobody's in particular, and a
/// token for the wrong one would be worse than the plain word.
/// <para/>
/// Also: a token naming nobody (shown to the player as typed), and a new name that
/// another character already has (two people with one name, and one colour, on
/// screen).
/// </summary>
public static class NameMentions
{
    public const string PlainMention = "names.plainMention";
    public const string UnknownToken = "names.unknownToken";
    public const string NewNameTaken = "names.newNameTaken";

    /// <summary>Words of a pack's text that are not words: tags, tokens, variables.</summary>
    private static readonly Regex Protected = new(@"<[^>]*>|\{[^}]*\}|\[[A-Za-z]+:[^\]]*\]", RegexOptions.Compiled);

    public static void Check(List<ValidationIssue> issues, ModPack pack) => Check(issues, pack, null);

    /// <summary>The same, sharing a Validate's writing-out of the pack.</summary>
    internal static void Check(List<ValidationIssue> issues, ModPack pack, SavedPack? saved)
    {
        if (pack?.Characters == null) return;
        CheckNewNames(issues, pack);

        var json = saved?.Json ?? JObject.Parse(PackRepository.SerializeAsSaved(pack));
        var sites = PackTexts.Of(json).Where(s => s.Kind != PackTexts.Kind.CharacterName).ToList();
        var gameQuests = new HashSet<string>(
            (json["quests"] as JArray ?? new JArray()).OfType<JObject>()
                .Where(q => !string.IsNullOrEmpty((string?)q["source"]))
                .Select(q => (string?)q["key"] ?? ""),
            StringComparer.OrdinalIgnoreCase);

        var names = Mentionable(pack);
        foreach (var site in sites)
        {
            string text = site.Text;
            if (string.IsNullOrEmpty(text)) continue;
            string where = TranslationValidation.Where(site, pack, gameQuests);

            foreach (var token in NameTokens.FindAll(text))
                if (Whom(pack, token.Key) == null)
                    issues.Add(new ValidationIssue(Severity.Warning, where,
                        Loc.F("validation.names.unknownToken", "token", text.Substring(token.Start, token.Length)),
                        UnknownToken));

            foreach (var (start, name, key) in Mentions(text, names))
            {
                string token = NameTokens.Make(key);
                issues.Add(new ValidationIssue(Severity.Info, where,
                    Loc.F("validation.names.plainMention", "name", name, "line", Around(text, start, name.Length),
                          "token", token),
                    PlainMention)
                {
                    // Not for a stuttered name ("A-Anna"): the token cannot carry the
                    // stutter, which would then read "A-Bella" once Anna is renamed.
                    Fix = site.Kind == PackTexts.Kind.Line && long.TryParse(site.Detail, out long id)
                          && !NameStutters.Stuttered(text, start, name)
                        ? new TextFix(site.Owner, id, start, name, token)
                        : null,
                });
            }
        }
    }

    /// <summary>
    /// Every name a character of the pack answers to, each with the key of the game's
    /// character it means - their name in the game, or a new name this pack gave them -
    /// or null where it is not listed: a name of the pack's own characters, one more
    /// than one character answers to, and a description standing in for a name
    /// (<see cref="VanillaNameWords"/>: "the Cop arrived" is no place for the token of
    /// the game's Cop, which a rename would turn into "the Officer Dan arrived").
    /// Those are still matched, so
    /// the longest name wins: a pack's own "Anna Smith" holds the "Anna" in it, which
    /// was listed - with a Fix to write the game's Anna into the pack's character's name.
    /// </summary>
    private static Dictionary<string, string?> Mentionable(ModPack pack)
    {
        var claims = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Claim(string? name, string key)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (!claims.TryGetValue(name, out var keys)) claims[name] = keys = new(StringComparer.OrdinalIgnoreCase);
            keys.Add(key);
        }

        foreach (var c in pack.Characters)
        {
            if (c.IsPlayer) continue;
            Claim(c.DisplayName, c.Key);
            if (c.IsVanillaCharacter) Claim(c.NewName, c.Key);
        }

        var games = new HashSet<string>(pack.Characters.Where(c => c.IsVanillaCharacter && !c.IsPlayer).Select(c => c.Key),
                                        StringComparer.OrdinalIgnoreCase);
        return claims.ToDictionary(
            p => p.Key,
            p => p.Value.Count == 1 && games.Contains(p.Value.First()) && !VanillaNameWords.IsDescription(p.Key)
                ? p.Value.First() : null,
            StringComparer.Ordinal);
    }

    /// <summary>Each listed name in <paramref name="text"/> as a whole word, outside tags
    /// and tokens, the longest where two overlap - an unlisted one included, which
    /// takes its words from any shorter name inside it.</summary>
    private static IEnumerable<(int Start, string Name, string Key)> Mentions(string text, Dictionary<string, string?> names)
    {
        var shut = new bool[text.Length];
        foreach (Match m in Protected.Matches(text))
            for (int i = m.Index; i < m.Index + m.Length; i++) shut[i] = true;

        var found = new List<(int Start, string Name, string Key)>();
        foreach (var (name, key) in names.OrderByDescending(p => p.Key.Length))
        {
            int at = 0;
            while ((at = text.IndexOf(name, at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + name.Length;
                bool whole = (at == 0 || !IsWordChar(text[at - 1])) && (end == text.Length || !IsWordChar(text[end]));
                bool free = !Enumerable.Range(at, name.Length).Any(i => shut[i]);
                if (whole && free)
                {
                    if (key != null) found.Add((at, name, key));
                    for (int i = at; i < end; i++) shut[i] = true;
                }
                at = end;
            }
        }
        return found.OrderBy(f => f.Start);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The words around a mention, so the list says which line it is.</summary>
    private static string Around(string text, int start, int length)
    {
        string plain = text.Replace('\n', ' ');
        int from = Math.Max(0, start - 30), to = Math.Min(plain.Length, start + length + 30);
        return (from > 0 ? "…" : "") + plain[from..to].Trim() + (to < plain.Length ? "…" : "");
    }

    /// <summary>Who a <c>{name:key}</c> in this pack names, as the runtime finds them:
    /// the pack's character under that key, else the game's.</summary>
    internal static string? Whom(ModPack pack, string key)
    {
        var c = pack.Characters.FirstOrDefault(x => !x.IsPlayer && string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (c != null) return c.ShownName;
        return VanillaCastData.NameOfKey(key);
    }

    /// <summary>A new name another of the pack's characters, or another of the game's,
    /// already goes by.</summary>
    private static void CheckNewNames(List<ValidationIssue> issues, ModPack pack)
    {
        foreach (var c in pack.Characters.Where(c => c.IsVanillaCharacter && !string.IsNullOrWhiteSpace(c.NewName)))
        {
            string name = c.NewName!.Trim();
            var other = pack.Characters.FirstOrDefault(o => !ReferenceEquals(o, c) && !o.IsPlayer
                && (string.Equals(o.ShownName?.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(o.DisplayName?.Trim(), name, StringComparison.OrdinalIgnoreCase)));
            string? taken = other?.ShownName
                ?? VanillaCharacters.All.FirstOrDefault(v => !string.Equals(v.Name, c.VanillaCharacter, StringComparison.Ordinal)
                                                            && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
            if (taken == null) continue;
            issues.Add(new ValidationIssue(Severity.Warning, $"actors[{c.Key}].displayName",
                Loc.F("validation.names.newNameTaken", "game", c.DisplayName, "name", name),
                NewNameTaken));
        }
    }
}
