using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Which faces the game already gives one of its own characters.
/// <para/>
/// One definition, because two things now depend on it and they have to agree:
/// the editor lists these greyed out in the Speech expressions box, and the
/// migration drops a pack's own entries that only restate them. If those two
/// ever disagreed, the migration would either throw away a row the editor still
/// shows or leave a duplicate of one it does.
/// </summary>
public static class VanillaFaces
{
    /// <summary>
    /// The faces, as the game names them, or empty for a character it never
    /// gave any — and for every character a pack drew itself.
    /// <para/>
    /// The character's OWN answer where the game gave them a speaking part,
    /// which is not always the four: two of them have a single <c>talk</c>, and
    /// many have none at all. Where there is no speaking part, the bust art
    /// answers instead — the four, if any outfit has them.
    /// <para/>
    /// <c>neutral</c> is left out on purpose. It is not a face; it means no
    /// face, which the editor already expresses as an empty Expression field on
    /// a node.
    /// </summary>
    public static IReadOnlyList<string> Of(CharacterDef? character)
    {
        if (character == null || !character.IsVanillaCharacter) return Array.Empty<string>();

        var speaker = Shared.VanillaSpeech.For(KeyOf(character));
        var faces = speaker != null
            ? speaker.Expressions
                .Where(e => !string.Equals(e, Neutral, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<string>();

        if (faces.Count == 0
            && character.Outfits.Any(o => Shared.VanillaBustExpressions.Has(o.GameObjectName ?? "")))
            faces = Shared.VanillaBustExpressions.Standard.ToList();

        // Nothing to pull a face with is nothing at all - not even the absence
        // of one. A character the game gave no expressions has an empty box,
        // which is the honest answer; listing "no face" on its own would read
        // as an ability they do not have.
        if (faces.Count == 0) return Array.Empty<string>();

        // ...and where they DO have faces, the absence of one is listed with
        // them. The game's own conversations ask for it by name seven hundred
        // times, and a pack's characters have carried a row for it all along;
        // leaving it off the game's characters made the two boxes disagree
        // about what a character can be asked to do.
        faces.Insert(0, Neutral);
        return faces;
    }

    /// <summary>The absence of a face, which the game's own conversations ask
    /// for by name seven hundred times.</summary>
    public const string Neutral = "neutral";

    /// <summary>Which of the game's characters this entry stands for, keyed the
    /// way the shared datasets are.</summary>
    public static string KeyOf(CharacterDef character)
        => character.IsVanillaCharacter
           ? Shared.VanillaCastData.KeyFor(
                 Shared.VanillaCastData.SpokenName(character.VanillaCharacter))
           : "";

    /// <summary>
    /// Whether this entry of a pack's own only restates what the game already
    /// does, so dropping it changes nothing.
    /// <para/>
    /// A face mapped to a child of its own name is what the runtime does with
    /// no entry at all. <c>neutral</c> mapped to nothing is the same: both end
    /// with no face showing.
    /// <para/>
    /// A face mapped to a DIFFERENT child is not this — that is an author
    /// pointing one of the game's names at their own art, and it is the whole
    /// reason the mapping exists.
    /// </summary>
    public static bool OnlyRestatesTheGame(ActorExpressionDef entry, IReadOnlyList<string> faces)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Key)) return false;
        string go = entry.ExpressionGoName ?? "";

        if (string.Equals(entry.Key, Neutral, StringComparison.OrdinalIgnoreCase))
            return go.Length == 0
                || string.Equals(go, Neutral, StringComparison.OrdinalIgnoreCase);

        return faces.Any(f => string.Equals(f, entry.Key, StringComparison.OrdinalIgnoreCase))
            && string.Equals(go, entry.Key, StringComparison.OrdinalIgnoreCase);
    }
}
