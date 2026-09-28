using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Services.Translation;

namespace SMSModForge.Model;

/// <summary>
/// How the game speaks of each of its characters, and of the player.
/// <para/>
/// Read out of the game rather than decided here. First from its lines: what
/// the other characters say about somebody straight after naming them ("Adrian
/// spilled his milk", "Go bother Kirby, she lives for company"), how a
/// character speaks of themselves, and a description the game names them by
/// ("Park Woman", "Mr. Kimura", "Pop Pop"). Where a character is too minor for
/// any line to say - a fan, a mobster, a customer - from the bust the game
/// draws them with. Two worked out from a single line each: Mei has a husband,
/// and Riku is "She" to Adrian. River is male, by the pack author who knows
/// the game (2026-09-27): no line says, and the bust reads either way - it was
/// wrongly female here at first.
/// <para/>
/// Subject IX-Delta is <see cref="Pronouns.Neutral"/>: a creature the game
/// never gives a sex, whose maker insists "This is not an 'it.'"
/// <para/>
/// The player is male: the game's lines speak of them as "he" ("{PC} is an
/// adult, capable of making his own decisions").
/// </summary>
public static class VanillaPronouns
{
    /// <summary>The player, as the game speaks of them.</summary>
    public const Pronouns Player = Pronouns.Male;

    // English on purpose: the game's own names for its characters, matched against the cast, never shown.
    private static readonly string[] Men =
    {
        "Adrian", "BigFoot", "Black Guy", "Chubby Backpack Guy", "Clay", "Cool Guy", "Cop", "Crazy Old Man",
        "Creature", "Daiju", "Diego", "Fan: Asian", "Fan: Blue Shirt", "Fan: Femboy", "Fan: Jeff", "Fan: Muscle",
        "Fan: Old Man", "Felix", "Flower Customer", "Frank", "Gabriel", "Hiroji", "Jack", "Jasper", "Joey", "Josef",
        "Judge Benjamin", "Ken", "Liam", "Mario", "Master Zhen", "Mobster 1", "Mobster 2", "Mr. Kimura",
        "Old City Gentleman", "Park Man", "Pop Pop", "Red Demon", "Red-Shirt City Guy", "Rich CEO", "River", "Robert",
        "Samuel", "Shy City Guy", "Sora", "Technician", "The Bouncer", "Toshiro", "Tristan", "Wolfguy",
    };

    // English on purpose: the game's own names for its characters, matched against the cast, never shown.
    private static readonly string[] Women =
    {
        "Agent", "Alice", "Amelia", "Anna", "Astrid", "Big-Butt City Woman", "Blonde Neon Row Worker", "Carina",
        "Celeste", "Cerise", "Charlotte", "Chihiro", "Chloe", "City Ginger Glasses", "Civilian", "Clara", "Claudia",
        "Doctor Frost", "Elfina", "Emma", "Fan: Girl", "Freya", "Ghost", "Haniya", "Himari", "Isabella", "Katarina",
        "Kate", "Kirby", "Lady Noire", "Leilani", "Liz", "Mall Waifu (Blonde)", "Mall Waifu (Blue Hair)", "Megan",
        "Mei", "Michelle", "Miss Zero", "Mrs. Kimura", "Nadia", "Neon Row Pink Worker", "Nightheart", "Nikki",
        "Norah", "Normal Woman", "Nurse Nina", "Nyxara", "Park Woman", "Phoenix", "Pool Chick (Blonde)",
        "Pool Chick (Reddie)", "Rich Customer", "Rich Woman (Blonde)", "Rich Woman (Pink)", "Riku", "Roxy",
        "Sakura", "Samantha", "Sex Worker", "Sofia", "Tasha", "Toni", "Trenchcoat MILF", "Vanessa", "Wolfgirl",
        "Zazia", "Zuri",
    };

    // English on purpose: the game's own name for it, matched against the cast, never shown.
    private static readonly string[] Neither = { "Subject IX-Delta" };

    private static readonly Dictionary<string, Pronouns> Table = Build();

    private static Dictionary<string, Pronouns> Build()
    {
        var table = new Dictionary<string, Pronouns>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in Men) table[name] = Pronouns.Male;
        foreach (string name in Women) table[name] = Pronouns.Female;
        foreach (string name in Neither) table[name] = Pronouns.Neutral;
        return table;
    }

    /// <summary>Every character the table has an answer for, by name.</summary>
    public static IReadOnlyCollection<string> Named => Table.Keys;

    /// <summary>
    /// One of the game's characters' pronouns, by name or key - or as the
    /// dialogue files spell them ("DrFrost", "V_Mei") - or
    /// <see cref="Pronouns.Unset"/> for somebody the game does not have.
    /// </summary>
    public static Pronouns Of(string? nameOrKey)
    {
        if (string.IsNullOrWhiteSpace(nameOrKey)) return Pronouns.Unset;
        var one = VanillaCharacters.Find(nameOrKey)
                  ?? VanillaCharacters.Find(VanillaDialogueCatalog.SpokenActorName(nameOrKey));
        return one != null && Table.TryGetValue(one.Name, out var p) ? p : Pronouns.Unset;
    }

    /// <summary>Names a line can call somebody by that are not part of their
    /// name in the cast: Doctor Frost is Doctor Evelyn Frost.</summary>
    // English on purpose: names the game's lines use, never shown.
    private static readonly Dictionary<string, string> AlsoCalled = new(StringComparer.Ordinal)
    {
        ["Evelyn"] = "Doctor Frost",
    };

    /// <summary>
    /// The pronouns of whoever a given name in a line means - "Nina" of Nurse
    /// Nina, "Zhen" of Master Zhen. <see cref="Pronouns.Unset"/> when the name
    /// is nobody's, or when it is two people's and they differ: "Kimura" is
    /// Mr. and Mrs. Kimura both.
    /// </summary>
    public static Pronouns OfGivenName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Pronouns.Unset;
        if (AlsoCalled.TryGetValue(name, out string? who)) return Of(who);

        var found = VanillaCharacters.All
            .Where(c => PackNames.Find(c.Name, new[] { name }).Count > 0)
            .Select(c => Table.TryGetValue(c.Name, out var p) ? p : Pronouns.Unset)
            .Distinct()
            .ToList();
        return found.Count == 1 ? found[0] : Pronouns.Unset;
    }
}
