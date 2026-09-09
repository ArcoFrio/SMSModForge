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
    /// <summary>
    /// How many vanilla characters the last <see cref="Apply"/> recognised and
    /// linked to the game's cast.
    /// <para/>
    /// Reported separately from the actor fold because they are different news
    /// to an author: one says "your actors are characters now", the other says
    /// "the people you borrowed from the game are the game's own records now".
    /// </summary>
    public static int LastAdoptedVanilla { get; private set; }

    /// <summary>Merge in place. Returns how many legacy actors were folded in.</summary>
    public static int Apply(ModPack pack)
    {
        LastAdoptedVanilla = 0;
        if (pack == null) return 0;

        var actors = pack.Actors;
        if (actors == null || actors.Count == 0)
        {
            // Already merged. There is nothing to fold, but the player still has
            // to be guaranteed — and this is the common path, since a pack only
            // has actors to fold once. Returning early here is what stopped it
            // running at all.
            BackfillNames(pack);
            LastAdoptedVanilla = AdoptVanillaCharacters(pack);
            VanillaCastSeed.Seed(pack);
            EnsurePlayer(pack);
            return 0;
        }

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
        LastAdoptedVanilla = AdoptVanillaCharacters(pack);

        // The same finish as the already-merged path above. Seeding the cast
        // was missing here, so a pack old enough to still carry actors - the
        // very packs most likely to want the game's characters offered to them
        // - opened without a single one of them.
        VanillaCastSeed.Seed(pack);
        EnsurePlayer(pack);
        return folded;
    }

    /// <summary>
    /// Guarantee the reserved player character, exactly once.
    /// <para/>
    /// Called on every load, including for a pack with no actors to fold, so
    /// "You" is present whether or not anything declared it — that is what lets
    /// a dialogue address the player without every pack having to invent one,
    /// and what keeps two packs' players being the same person.
    /// <para/>
    /// An existing entry is adopted rather than replaced: a pack may have given
    /// it a name colour or a voice, and those are the author's.
    /// </summary>
    public static void EnsurePlayer(ModPack pack)
    {
        var existing = pack.Characters
            .Where(c => string.Equals(c.Key, CharacterDef.PlayerKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (existing.Count == 0)
        {
            pack.Characters.Insert(0, CharacterDef.NewPlayer());
            return;
        }

        // Fold any duplicates onto the first, so a pack that declared the
        // player twice under different casings ends up with one.
        var player = existing[0];
        for (int i = 1; i < existing.Count; i++) pack.Characters.Remove(existing[i]);

        // Overwritten, not filled in. The whole point is that every pack shows
        // the same person the same way, so a manifest carrying a divergent name
        // or colour — hand-edited, or written before this was reserved — is
        // corrected on load rather than honoured.
        var canonical = CharacterDef.NewPlayer();
        player.Key = canonical.Key;
        player.Name = canonical.Name;
        player.DisplayName = canonical.DisplayName;
        player.NameColor = canonical.NameColor;
        player.BustSource = canonical.BustSource;
        player.Outfits.Clear();
        player.GiftLikes.Clear();
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
    /// <summary>
    /// Recognise the game's own characters in a pack that predates them being
    /// available.
    /// <para/>
    /// Such a pack declared a character and listed a bust or two of the game's
    /// — "Anna", wearing Anna_Bust. Those busts say who it was all along, so
    /// the entry is pointed at the game's character and given the whole
    /// wardrobe instead of the corner of it somebody copied out.
    /// <para/>
    /// The pack's own KEY is untouched, deliberately. Everything already
    /// pointing at it — every dialogue line, every action — keeps working, and
    /// nothing has to be rewritten anywhere. Converting the key instead would
    /// mean rewriting every reference in the pack, and a reference missed
    /// there fails silently in game rather than loudly here.
    /// <para/>
    /// Returns how many were recognised.
    /// </summary>
    public static int AdoptVanillaCharacters(ModPack pack)
    {
        if (pack?.Characters == null) return 0;

        int adopted = 0;
        foreach (var character in pack.Characters)
        {
            if (character.BustSource != BustSource.Vanilla) continue;
            if (character.IsVanillaCharacter) continue;      // already adopted

            // Who they are is whichever of the game's characters owns the
            // busts they were wearing. The first that resolves settles it: a
            // pack mixing two characters' busts under one entry is not
            // something to guess at, and the first is the one the author
            // reached for.
            VanillaCharacters.VanillaCharacter? found = null;
            foreach (var outfit in character.Outfits)
            {
                found = VanillaCharacters.Owning(outfit.GameObjectName);
                if (found != null) break;
            }

            // Failing that, the name itself - a pack that called its entry
            // "Anna" and listed nothing recognisable is still plainly Anna.
            found ??= VanillaCharacters.Find(character.DisplayName)
                   ?? VanillaCharacters.Find(character.Key);
            if (found == null) continue;

            character.VanillaCharacter = found.Name;
            adopted++;
        }

        // Everybody's wardrobe, adopted just now or long since.
        //
        // Dressing only the newly adopted was enough while a saved manifest
        // carried every outfit; it is not now that saving drops the ones the
        // game supplies. A pack reopened would have kept whatever the author
        // added and quietly lost the other sixty-four, once.
        foreach (var character in pack.Characters)
        {
            if (!character.IsVanillaCharacter) continue;
            var wardrobe = VanillaCharacters.Find(character.VanillaCharacter);
            if (wardrobe != null) Dress(character, wardrobe);
        }

        return adopted;
    }

    /// <summary>
    /// Give a character the game's whole wardrobe, keeping whatever the pack
    /// already had.
    /// <para/>
    /// The outfits stay IN the manifest rather than being looked up at play
    /// time, because the runtime resolves a bust by the name written there and
    /// teaching it about the game's cast is a change with nothing to gain: an
    /// outfit is a key and a GameObject name, and a hundred of them is a few
    /// lines of a manifest that is already tens of thousands.
    /// <para/>
    /// An entry the pack already wrote is left exactly as it is - its key may
    /// be referenced by a dialogue, and its art settings are the author's.
    /// </summary>
    /// <summary>
    /// Give a character every bust the game has for them that they are not
    /// already carrying.
    /// <para/>
    /// Adds only what is missing, so running it on a character who has them
    /// all does nothing — which is what lets it run on every load rather than
    /// only on the one where the character was first recognised.
    /// </summary>
    private static void Dress(CharacterDef character,
                              VanillaCharacters.VanillaCharacter wardrobe)
    {
        var had = new HashSet<string>(
            character.Outfits.Select(o => o.GameObjectName ?? ""),
            System.StringComparer.OrdinalIgnoreCase);

        foreach (string outfit in wardrobe.Outfits)
        {
            if (string.IsNullOrEmpty(outfit) || had.Contains(outfit)) continue;
            character.Outfits.Add(new OutfitDef
            {
                Key = VanillaCharacters.KeyFor(outfit),
                GameObjectName = outfit,
            });
        }
    }

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
