using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Works out which part of a pack's version a release should move.
/// <para/>
/// The question is only ever "did this pack GAIN something, or did it change
/// something it already had", and that is answered by comparing the pack as it
/// is now against the manifest of the last PUBLISHED release - not the last
/// save. Saves are not releases, so nothing about them belongs in a number
/// players read. Records are matched by
/// their key, so a rename reads as one record leaving and another arriving —
/// which is the honest reading: to a player, a renamed thing is a new thing.
/// <para/>
/// Major is never decided here. "This is a new version of the pack" is a claim
/// about the work rather than about the diff, and no amount of counting records
/// can make it.
/// </summary>
public static class VersionBump
{
    /// <summary>What a save turned out to be.</summary>
    public enum Change
    {
        /// <summary>Nothing meaningful moved; the version stands.</summary>
        None,

        /// <summary>Something the pack already had is different.</summary>
        Patch,

        /// <summary>The pack has something it did not have before.</summary>
        Minor,
    }

    /// <summary>
    /// The collections a pack keeps its records in, and the field that
    /// identifies one within its collection.
    /// <para/>
    /// Listed rather than discovered, because "what counts as a record" is a
    /// judgement: a pack's dialogues and places are things a player can
    /// encounter, while its folder layout and its window widths are not, and a
    /// tidy-up of the sidebar should not announce itself as a new release.
    /// </summary>
    private static readonly (string Collection, string Key)[] Records =
    {
        ("characters", "key"),
        ("places", "key"),
        ("dialogues", "key"),
        ("scenes", "key"),
        ("npcs", "key"),
        ("wallpapers", "key"),
        ("music", "key"),
        ("sfx", "key"),
        ("variables", "name"),
        ("mapButtons", "target"),
        ("integrationRules", "key"),
        ("uis", "id"),
        ("vanillaExtensions", "source"),
    };

    /// <summary>
    /// Compare two manifests and say what the difference amounts to.
    /// <para/>
    /// Both are the JSON a save would write, so what is compared is exactly
    /// what lands on disk — no view-model state, no editor-only scaffolding,
    /// and nothing that gets pruned on the way out.
    /// </summary>
    public static Change Classify(string? previousJson, string? nextJson)
    {
        if (string.IsNullOrWhiteSpace(nextJson)) return Change.None;
        if (string.IsNullOrWhiteSpace(previousJson)) return Change.Minor;   // the first save

        JObject before, after;
        try
        {
            before = JObject.Parse(previousJson!);
            after = JObject.Parse(nextJson!);
        }
        catch (Newtonsoft.Json.JsonException) { return Change.None; }

        // The version itself moving is not a change to the pack.
        before.Remove("version");
        after.Remove("version");

        // Neither is the tool's. An author who updates ModForge and saves has
        // not altered their pack, and a release numbered for it would be
        // announcing somebody else's work.
        before.Remove("forgeVersion");
        after.Remove("forgeVersion");

        if (JToken.DeepEquals(before, after)) return Change.None;

        var change = Change.Patch;
        foreach (var (collection, key) in Records)
        {
            var was = KeysOf(before, collection, key);
            var now = KeysOf(after, collection, key);

            // Anything the pack did not have before is an addition, and one is
            // enough - a save that adds a scene and fixes a line is, on the
            // whole, an addition.
            if (now.Except(was, StringComparer.OrdinalIgnoreCase).Any()) return Change.Minor;
        }

        // A change to one of the GAME's records counts as an addition too: the
        // pack did not alter that conversation before and does now, which from
        // a player's side is a new thing the pack does rather than a fix to an
        // old one. Only the FIRST such change - once the pack owns it, later
        // edits are ordinary changes, and the key comparison above has already
        // caught the arrival.
        return change;
    }

    /// <summary>The identifying values of one collection's records.</summary>
    private static HashSet<string> KeysOf(JObject manifest, string collection, string key)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest[collection] is not JArray rows) return found;

        foreach (var row in rows)
        {
            string? id = (string?)row[key];
            if (!string.IsNullOrEmpty(id)) found.Add(id!);
        }
        return found;
    }

    /// <summary>
    /// The version a release should carry, given what changed since the last
    /// one.
    /// <para/>
    /// Returns the version unchanged when nothing did, so publishing twice
    /// without touching anything does not climb a number for it.
    /// </summary>
    public static PackVersion Next(PackVersion current, Change change) => change switch
    {
        Change.Minor => current.NextMinor(),
        Change.Patch => current.NextPatch(),
        _ => current,
    };
}
