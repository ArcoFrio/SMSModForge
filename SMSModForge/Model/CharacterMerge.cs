using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Folds a pack's legacy <c>actors</c> array into its <c>characters</c>.
/// <para/>
/// Runs on load and is idempotent: a pack already saved in the merged shape has
/// no actors left to fold, so it passes straight through. Nothing is written
/// back until the author saves, so opening an old pack and closing it again
/// changes nothing on disk.
/// <para/>
/// The one rule throughout is that NOTHING existing gets renamed. A character's
/// key and GameObject name are baked into dialogue references and into the live
/// scene, so migration preserves whatever a pack already had and only derives
/// names where there were none to preserve.
/// </summary>
public static class CharacterMerge
{
    /// <summary>Merge in place. Returns how many legacy actors were folded in.</summary>
    public static int Apply(ModPack pack)
    {
        if (pack == null) return 0;
        var actors = pack.Actors;
        if (actors == null || actors.Count == 0) return 0;

        var byName = new Dictionary<string, CharacterDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in pack.Characters)
            if (!string.IsNullOrWhiteSpace(c.Name)) byName[c.Name] = c;

        int folded = 0;
        foreach (var a in actors)
        {
            // An actor is matched to a bust by the bust it defaults to, NOT by
            // name: "solidsnake" wears "Snek" and "mobster" wears "S_Mobster1",
            // so matching on the key would have stranded both.
            CharacterDef? owner = FindOwner(pack, byName, a);

            if (owner == null)
            {
                // No pack bust behind it — either a vanilla one, or nothing at
                // all (a speaking part like John Dick, or the player).
                owner = new CharacterDef
                {
                    // Left blank on purpose: there is no existing GameObject
                    // name to preserve here, so BackfillNames derives a tidy one
                    // from the display name rather than inheriting the actor key
                    // and leaving "mobster" and "johndick" in the data.
                    Name = "",
                    BustSource = string.IsNullOrWhiteSpace(a.DefaultBustKey)
                        ? BustSource.None : BustSource.Vanilla,
                    DefaultOutfit = a.DefaultBustKey ?? "",
                };
                // A vanilla character's outfits are the bust names themselves —
                // same list as a pack character's, just with nothing to ship.
                foreach (var bust in BustNames(a))
                    owner.Outfits.Add(new OutfitDef { Key = bust, GameObjectName = bust });
                pack.Characters.Add(owner);
            }
            else
            {
                owner.BustSource = BustSource.Pack;
                owner.DefaultOutfit = a.DefaultBustKey ?? "";
                // The actor's outfit list was the bust's own outfits written out
                // a second time, so it is simply dropped. Anything in it that
                // ISN'T one of them was a vanilla bust the character could also
                // wear — that becomes an outfit like any other, carrying a name
                // and no art.
                var own = new HashSet<string>(owner.Outfits.Select(o => o.GameObjectName),
                                              StringComparer.OrdinalIgnoreCase);
                foreach (var bust in BustNames(a))
                    if (!own.Contains(bust))
                        owner.Outfits.Add(new OutfitDef { Key = bust, GameObjectName = bust });
            }

            // Actor fields have no counterpart on a bust, so they transfer whole.
            owner.Key = a.Key ?? "";
            if (!string.IsNullOrWhiteSpace(a.DisplayName)) owner.DisplayName = a.DisplayName;
            owner.NameColor = a.NameColor;
            owner.Expressions = a.Expressions ?? new List<ActorExpressionDef>();
            owner.Typewriter = a.Typewriter;
            folded++;
        }

        pack.Actors = new List<ActorDef>();
        BackfillNames(pack);
        return folded;
    }

    /// <summary>Every bust an actor could be shown as: its default first, then
    /// the rest, deduplicated and in order.</summary>
    private static IEnumerable<string> BustNames(ActorDef a)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(a.DefaultBustKey) && seen.Add(a.DefaultBustKey))
            yield return a.DefaultBustKey;
        foreach (var o in a.Outfits ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(o) && seen.Add(o))
                yield return o;
    }

    /// <summary>
    /// The pack character an actor speaks through, or null when it borrows a
    /// vanilla bust or has none. Matched on the default bust first, then on any
    /// outfit it lists, then on the key as a last resort.
    /// </summary>
    private static CharacterDef? FindOwner(ModPack pack,
                                           Dictionary<string, CharacterDef> byName,
                                           ActorDef a)
    {
        if (!string.IsNullOrWhiteSpace(a.DefaultBustKey))
        {
            var viaOutfit = pack.Characters.FirstOrDefault(
                c => c.Outfits.Any(o => string.Equals(o.Key, a.DefaultBustKey, StringComparison.OrdinalIgnoreCase)));
            if (viaOutfit != null) return viaOutfit;
        }
        foreach (var name in a.Outfits ?? new List<string>())
        {
            var viaAny = pack.Characters.FirstOrDefault(
                c => c.Outfits.Any(o => string.Equals(o.Key, name, StringComparison.OrdinalIgnoreCase)));
            if (viaAny != null) return viaAny;
        }
        return byName.TryGetValue(a.Key ?? "", out var byKey) ? byKey : null;
    }

    /// <summary>
    /// Give a key or a GameObject name to anything that reached here without
    /// one — a bust that never had an actor, or a freshly merged pack. Existing
    /// values are never touched.
    /// </summary>
    public static void BackfillNames(ModPack pack)
    {
        var keys = new HashSet<string>(
            pack.Characters.Where(c => !string.IsNullOrWhiteSpace(c.Key)).Select(c => c.Key),
            StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(
            pack.Characters.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var c in pack.Characters)
        {
            if (string.IsNullOrWhiteSpace(c.Key))
            {
                c.Key = CharacterDef.UniqueIdentifier(c.DisplayName, keys);
                keys.Add(c.Key);
            }
            if (string.IsNullOrWhiteSpace(c.Name))
            {
                c.Name = CharacterDef.UniqueIdentifier(c.DisplayName, names);
                names.Add(c.Name);
            }
            foreach (var o in c.Outfits)
                if (string.IsNullOrWhiteSpace(o.GameObjectName)) o.GameObjectName = o.Key;
        }
    }
}
