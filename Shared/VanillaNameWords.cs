namespace SMSModForge.Shared
{
    /// <summary>
    /// The names of the game's characters that are not, by themselves, a name in a
    /// sentence (ModForge 1.9.0, renaming the game's characters). One list for both
    /// places that look for names in words: Tools/Names/game_mentions.py reads it from
    /// this file to tie the game's own texts, and Validate uses it in a pack's.
    /// <para/>
    /// <b>Descriptions</b> stand in for a name ("Cop", "Cool Guy"): in a sentence they
    /// are ordinary words ("the cop"), and the character is renamed over their own
    /// lines only. Never tied, never listed.
    /// <para/>
    /// <b>Names that are also words</b>, or another thing's name (River, Clay, Ghost,
    /// Sakura...): which one a line means takes a person reading it. The game's texts
    /// are tied where one did (Tools/Names/reviewed.tsv). Validate lists them in a
    /// pack's lines like any name - the author reading the note is that person, and
    /// nothing is changed unless they ask.
    /// <para/>
    /// Hand-kept (reviewed 2026-11-14). The game_mentions script reads the two lists
    /// below by their names, one string per entry.
    /// </summary>
    public static class VanillaNameWords
    {
        public static readonly string[] Descriptions =
        {
            "Agent", "Big-Butt City Woman", "BigFoot", "Black Guy", "Blonde Neon Row Worker", "Chubby Backpack Guy",
            "City Ginger Glasses", "Civilian", "Cool Guy", "Cop", "Crazy Old Man", "Creature", "Fan: Asian",
            "Fan: Blue Shirt", "Fan: Femboy", "Fan: Girl", "Fan: Jeff", "Fan: Muscle", "Fan: Old Man", "Flower Customer",
            "Mall Waifu (Blonde)", "Mall Waifu (Blue Hair)", "Mobster 1", "Mobster 2", "Neon Row Pink Worker",
            "Normal Woman", "Old City Gentleman", "Park Man", "Park Woman", "Pool Chick (Blonde)", "Pool Chick (Reddie)",
            "Red Demon", "Red-Shirt City Guy", "Rich CEO", "Rich Customer", "Rich Woman (Blonde)", "Rich Woman (Pink)",
            "Sex Worker", "Shy City Guy", "Technician", "The Bouncer", "Trenchcoat MILF", "Wolfgirl", "Wolfguy",
        };

        public static readonly string[] NeedAPerson =
        {
            "River", "Clay", "Jack", "Phoenix", "Ghost", "Sakura", "Frank", "Celeste", "Master Zhen",
        };

        /// <summary>Whether <paramref name="name"/>, as written, is a description standing
        /// in for a name: never a name in a sentence.</summary>
        public static bool IsDescription(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var d in Descriptions) if (d == name) return true;
            return false;
        }
    }
}
