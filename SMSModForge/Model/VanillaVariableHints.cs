using System;
using System.Collections.Generic;
using SMSModForge.Localization;

namespace SMSModForge.Model;

/// <summary>
/// What each of the game's own variables means, shown under its name in the
/// variable dropdowns and searched along with it (1.9.0).
/// <para/>
/// Every hint comes from research on the running game, not from the names:
/// every place that sets, reads or shows the variable was taken from the game
/// itself, a hint was written from those, a second pass tried to prove it
/// wrong, and a third settled what the two disagreed on
/// (<c>Tools/Research/Variables</c>, README there). A wrong hint is worse than
/// none - an author builds a quest on it and it never comes true - so a hint
/// says only what the game shows, and one worked out with a step the files do
/// not show outright carries a mark saying so.
/// <para/>
/// The texts are in the language files under <c>[The game's variables]</c>,
/// one key per variable (<see cref="TextKeyOf"/>), written there with the
/// table in <c>VanillaVariableHints.Data.cs</c> by <c>ship_hints.py</c>. The
/// variables the game never touches share one text.
/// </summary>
public static partial class VanillaVariableHints
{
    /// <summary>The one text every variable the game never sets or reads shares.</summary>
    public const string NotUsedKey = "vhint.notUsed";

    /// <summary>Added after a hint worked out with a step the game's files do not show outright.</summary>
    public const string InferredKey = "vhint.mark.inferred";

    /// <summary>Added after a hint the research could not follow to the end.</summary>
    public const string UnconfirmedKey = "vhint.mark.unconfirmed";

    /// <summary>
    /// The key whose text is this variable's hint: the shared one for a
    /// variable the game never uses, else its own -
    /// <c>vhint.&lt;list&gt;.&lt;name&gt;</c>, a list's space made an
    /// underscore (a key cannot hold a space), and numbered for the second of
    /// two names that differ only in capitals (a key ignores case; the game
    /// does not).
    /// </summary>
    public static string TextKeyOf(string list, string name)
    {
        string address = Address(list, name);
        if (NotUsed.Contains(address)) return NotUsedKey;
        return Renamed.TryGetValue(address, out string? key) ? key : "vhint." + list.Replace(' ', '_') + "." + name;
    }

    /// <summary>Whether the game never sets or reads this variable (1.8E).</summary>
    public static bool IsNotUsed(string list, string name) => NotUsed.Contains(Address(list, name));

    /// <summary>
    /// The hint under a name in the picker, in the editor's language, or
    /// empty for a name the game does not have.
    /// <para/>
    /// The name exactly as the game spells it: one with other capitals is
    /// another variable or none (<see cref="VanillaGameVariables.AllNames"/>).
    /// The picker offers each name once and a pack reaches the FIRST list
    /// that has it (<see cref="VanillaGameVariables.ListOf"/>), so that list's
    /// variable is the one described: Comments' "1", not Gallery's "1".
    /// Measured in the game (2026-11-13): Game Creator searches its lists in
    /// its variables repository's order, and for all 121 names in more than one
    /// list the first is the catalog's first; `get 1` read Comments' value.
    /// </summary>
    public static string For(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        lock (Shown)
        {
            if (Shown.TryGetValue(name!, out string? known)) return known;
            string hint = Make(name!);
            Shown[name!] = hint;
            return hint;
        }
    }

    // A search asks for every item's hint on every keystroke, 1,600-odd names:
    // each is made once per language and kept.
    private static readonly Dictionary<string, string> Shown = MakeCache();

    private static Dictionary<string, string> MakeCache()
    {
        var made = new Dictionary<string, string>(StringComparer.Ordinal);
        Loc.Changed += () => { lock (made) made.Clear(); };
        return made;
    }

    private static string Make(string name)
    {
        string list = VanillaGameVariables.ListOf(name);
        if (list.Length == 0) return "";
        string key = TextKeyOf(list, name);
        if (!Loc.English.Has(key)) return "";
        string hint = Loc.T(key);
        if (Uncertain.TryGetValue(Address(list, name), out char how))
            hint += " " + Loc.T(how == 'u' ? UnconfirmedKey : InferredKey);
        return hint;
    }

    private static string Address(string list, string name) => list + "[" + name + "]";
}
