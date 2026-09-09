using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// The game's own cast, as characters rather than as a list of busts.
/// <para/>
/// <see cref="VanillaBusts"/> is 285 bust GameObjects, each knowing which
/// character it belongs to. That is the wrong way round for an author: nobody
/// wants to declare a character called Anna and then go looking for which of 64
/// bust names is hers. Grouped the other way, the game's 119 characters are
/// simply THERE — Anna with her 64 outfits, Kate with her 7 — and a pack uses
/// one instead of re-describing it.
/// <para/>
/// Nothing here is invented. The grouping is the one the bust catalog already
/// carries, and a character is exactly the set of busts that name it.
/// </summary>
public static class VanillaCharacters
{
    /// <summary>One of the game's characters, and every bust it can wear.</summary>
    public sealed record VanillaCharacter(string Name, IReadOnlyList<string> Outfits)
    {
        /// <summary>
        /// The key a pack refers to this character by.
        /// <para/>
        /// Derived from the name rather than stored, so two packs naming the
        /// same character agree without having to know about each other —
        /// the same reasoning that fixes the player's key.
        /// </summary>
        public string Key => KeyFor(Name);

        /// <summary>The bust shown unless a line says otherwise: the first,
        /// which is the plain one for every character that has several.</summary>
        public string DefaultOutfit => Outfits.Count > 0 ? Outfits[0] : "";

        public string Summary => Outfits.Count == 1
            ? "1 outfit"
            : Outfits.Count + " outfits";
    }

    /// <summary>
    /// A character's key, from its name. Lower case, letters and digits only —
    /// "Doctor Frost" and "Himari (Father)" are display names, not identifiers.
    /// </summary>
    public static string KeyFor(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        return SMSModForge.Shared.VanillaCastData.KeyFor(name);
    }

    /// <summary>
    /// What the game calls people, where the bust catalog does not.
    /// <para/>
    /// A bust is filed by artwork; an actor carries the name the game actually
    /// says. Every entry here comes from an actor's own Actant name in the
    /// runtime extraction, and each fixes one of three things:
    /// <list type="bullet">
    ///   <item>A misspelling — the bust files for Chloe read "Choe".</item>
    ///   <item>A description standing in for a name — "El Bandito" and
    ///   "Succubus" describe a picture; the people are Frank and Zazia, and
    ///   Himari's parents are Mr. and Mrs. Kimura rather than versions of
    ///   their daughter.</item>
    ///   <item>A curator's note left in the name — "(Vacation)" says where
    ///   somebody appears, and "Anna (Mummy Outfit)" is an OUTFIT filed as a
    ///   person. Merged away, so a record reads like a pack's own does: one
    ///   person, wearing their things.</item>
    /// </list>
    /// <para/>
    /// Two names mapping to one is how a merge happens — Anna keeps her mummy
    /// outfit as an outfit. What is deliberately NOT merged is a parenthetical
    /// telling two people apart: "Mall Waifu (Blonde)" and "(Blue Hair)" are
    /// two women with no other name, and folding them together would lose one.
    /// <para/>
    /// A short reviewed list rather than the extraction pasted over the curated
    /// one, which is human-checked on purpose (see <see cref="VanillaBusts"/>).
    /// The characters with busts but no speaking part keep the catalog's
    /// description of them: nothing in the game ever names them.
    /// <para/>
    /// The table lives in <c>Shared/VanillaCastData.cs</c>, with the reasoning
    /// for each entry beside it, because the runtime plugin needs the same
    /// answers: a pack that says a line is spoken by "masterzhen" has to reach
    /// the same person in the game that the author picked in the editor.
    /// </summary>
    private static Dictionary<string, string> SpokenNames
        => SMSModForge.Shared.VanillaCastData.SpokenNames;

    /// <summary>What the game calls whoever a bust group describes.</summary>
    public static string SpokenName(string grouped)
        => grouped != null && SpokenNames.TryGetValue(grouped, out string? said) ? said : grouped ?? "";

    private static readonly Lazy<IReadOnlyList<VanillaCharacter>> Cast =
        new(() => VanillaBusts.All
            // Grouped by what the game CALLS them, not by how the artwork is
            // filed - otherwise Anna and "Anna (Mummy Outfit)" arrive as two
            // people with the same name.
            .GroupBy(b => SpokenName(b.Character), StringComparer.Ordinal)
            .Select(g => new VanillaCharacter(
                g.Key,
                g.Select(b => b.GoName).ToList()))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());

    /// <summary>Every character the game has, in name order.</summary>
    public static IReadOnlyList<VanillaCharacter> All => Cast.Value;

    /// <summary>One of them by name or by key, or null.</summary>
    public static VanillaCharacter? Find(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey)) return null;
        string wanted = nameOrKey.Trim();

        // Also under the name the bust files used, so a pack that was migrated
        // before a correction - and stored "Master" - still finds Master Zhen.
        string said = SpokenName(wanted);

        return All.FirstOrDefault(c =>
                   string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(c =>
                   string.Equals(c.Name, said, StringComparison.OrdinalIgnoreCase))
            ?? All.FirstOrDefault(c =>
                   string.Equals(c.Key, KeyFor(wanted), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Which of the game's characters a bust belongs to, or null for a name the
    /// game does not have.
    /// <para/>
    /// This is what lets a pack written before the cast existed be recognised:
    /// it declared a character and listed vanilla busts, and those busts say
    /// who it was all along.
    /// </summary>
    public static VanillaCharacter? Owning(string? outfitGoName)
    {
        if (string.IsNullOrWhiteSpace(outfitGoName)) return null;

        var bust = VanillaBusts.All.FirstOrDefault(b =>
            string.Equals(b.GoName, outfitGoName, StringComparison.OrdinalIgnoreCase));
        // Through the correction, since the cast is keyed by what the game
        // calls people rather than by how the artwork is filed - Choe_Base
        // belongs to Chloe, and looking for "Choe" finds nobody.
        return bust == null ? null : Find(SpokenName(bust.Character));
    }
}
