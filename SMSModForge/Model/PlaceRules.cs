using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Shared;
using Refs = SMSModForge.Model.VanillaQuestReferences;

namespace SMSModForge.Model;

/// <summary>
/// What one of the game's start or reset places comes to once a pack has
/// changed it.
/// <para/>
/// A place nobody has touched is the game's own: the conversation or script
/// gets there, and the quest starts. As soon as the pack changes anything at
/// it - one of the game's conditions taken out, the room taken out, or
/// conditions of the pack's added - the pack owns that place, and what is left
/// of it becomes a RULE: the runtime asks it continuously and starts the quest
/// (or puts it back to not started) the moment it all passes, whether or not
/// the game's own conversation or script ever gets there. That is what an
/// author means by taking every condition out and putting their own in.
/// <para/>
/// The runtime has no extraction to read, so the rule is written into the
/// manifest as the pack is saved, and taken back out afterwards, like every
/// other worked-out thing (<see cref="VanillaDelta"/>). It copies none of the
/// game's own conditions: it names the LISTS and LINES they live in, so the
/// runtime reads the very conditions the game reads - less whatever the pack
/// has taken out of them, and whatever a game update leaves there.
/// </summary>
public static class PlaceRules
{
    /// <summary>
    /// Write the rule of every changed place into the pack. Returns an action
    /// that takes them out again - call it in a finally, as the save does.
    /// </summary>
    public static Action PrepareForSave(ModPack pack)
    {
        if (pack?.Quests == null || !Refs.IsAvailable) return () => { };

        var written = new List<SiteConditionsDef>();
        var added = new List<KeyValuePair<QuestDef, SiteConditionsDef>>();
        foreach (var quest in pack.Quests.Where(q => q.IsVanillaExtension).ToList())
            foreach (var found in SitesOf(quest))
            {
                var place = PlaceOf(quest, found.Site, found.Does);
                if (!IsChanged(pack, found.Site, place)) continue;

                if (place == null)
                {
                    // Changed only by conditions taken out of the game's own
                    // lists, which are kept with the script or the conversation
                    // rather than here - so the entry that says "this place is
                    // the pack's now" is made for the save.
                    place = NewPlace(found.Site, found.Does);
                    quest.SiteConditions.Add(place);
                    added.Add(new KeyValuePair<QuestDef, SiteConditionsDef>(quest, place));
                }
                place.Rule = Build(found.Site, place);
                written.Add(place);
            }

        return () =>
        {
            foreach (var place in written) place.Rule = null;
            foreach (var pair in added) pair.Key.SiteConditions.Remove(pair.Value);
        };
    }

    /// <summary>Every place the game starts or resets this quest at.</summary>
    public static IEnumerable<(Refs.Site Site, string Does)> SitesOf(QuestDef quest)
    {
        var sites = Refs.For(quest?.Source);
        if (sites == null) yield break;
        foreach (var site in sites.Starts) yield return (site, GameConditionEdits.Starts);
        foreach (var site in sites.Resets) yield return (site, GameConditionEdits.Resets);
    }

    /// <summary>The entry a quest keeps for one of its places, or null.</summary>
    public static SiteConditionsDef? PlaceOf(QuestDef quest, Refs.Site site, string does)
    {
        string key = NewPlace(site, does).Key;
        return quest.SiteConditions.FirstOrDefault(s => s.Key == key);
    }

    /// <summary>The entry a place would have, with nothing in it.</summary>
    public static SiteConditionsDef NewPlace(Refs.Site site, string does)
        => site.IsDialogue
            ? new SiteConditionsDef
              {
                  Does = does, Dialogue = site.Dialogue ?? "", Node = site.Node, Moment = site.Moment ?? "",
              }
            : new SiteConditionsDef { Does = does, By = site.By ?? "", Script = site.Script ?? "" };

    /// <summary>
    /// Whether the pack changes anything about this place: its own conditions,
    /// the room taken out, one of the game's conditions taken out of a list
    /// that guards it, or out of one of the lines it is on.
    /// </summary>
    public static bool IsChanged(ModPack pack, Refs.Site site, SiteConditionsDef? place)
    {
        if (place != null && (place.Conditions.Count > 0 || place.RoomsOut.Count > 0)) return true;
        if (pack == null) return false;

        var taken = new HashSet<string>(
            pack.VanillaGates.Where(g => g.Removed.Count > 0).Select(g => g.Key), StringComparer.Ordinal);
        foreach (var scope in ScopesOf(site))
            if (scope.Gates.Any(g => taken.Contains(
                    GameConditionEdits.GateKey(scope.By, scope.Script, GameConditionEdits.PathText(g.At)))))
                return true;

        return LinesChanged(pack, site);
    }

    /// <summary>Whether the pack's version of the conversation has taken one of
    /// the game's conditions out of the line this place is on, or of a line on
    /// the way to it.</summary>
    public static bool LinesChanged(ModPack pack, Refs.Site site)
    {
        if (!site.IsDialogue || site.Node == null) return false;
        var entry = VanillaDialogueCatalog.Find(site.Dialogue);
        if (entry == null) return false;
        var mine = pack.Dialogues.FirstOrDefault(
            d => string.Equals(d.Source, entry.Token, StringComparison.OrdinalIgnoreCase));
        var game = mine == null ? null : VanillaDialogueCatalog.Open(entry.Id);
        if (game == null) return false;

        foreach (long id in LinesOf(site))
        {
            var line = mine!.Nodes.FirstOrDefault(n => n.Id == unchecked((int)id));
            var theirs = game.Node(id);
            if (line == null || theirs == null) continue;
            var translated = VanillaDialogueConditions.TranslateAll(theirs.Conditions, out _);
            if (Removed(translated, line.Conditions)) return true;
        }
        return false;
    }

    /// <summary>Whether any of the game's conditions is missing from the pack's
    /// version of the list. Matched in order, so a condition the game lists
    /// twice counts twice.</summary>
    private static bool Removed(IReadOnlyList<NodeConditionDef> game, IReadOnlyList<NodeConditionDef> now)
    {
        var unmatched = now.Select(JsonConvert.SerializeObject).ToList();
        foreach (var condition in game)
        {
            int at = unmatched.IndexOf(JsonConvert.SerializeObject(condition));
            if (at < 0) return true;
            unmatched.RemoveAt(at);
        }
        return false;
    }

    /// <summary>The lines a place needs the game to be on: the ones leading to
    /// it, then the line itself.</summary>
    public static IEnumerable<long> LinesOf(Refs.Site site)
    {
        foreach (var lead in site.After) yield return lead.Node;
        if (site.Node != null) yield return site.Node.Value;
    }

    /// <summary>One thing that has to run for the place to happen: the script
    /// the step is in, or each script that plays its conversation - any one of
    /// those is enough.</summary>
    private sealed class Scope
    {
        public string By = "";
        public string Script = "";
        public List<VanillaDialogueCatalog.Step> When = new();
        public List<VanillaDialogueCatalog.GateLocation> Gates = new();
    }

    private static IEnumerable<Scope> ScopesOf(Refs.Site site)
    {
        if (site.IsDialogue)
        {
            foreach (var play in site.Plays)
                yield return new Scope { By = play.By, Script = play.Script, When = play.When, Gates = play.Gates };
            yield break;
        }
        yield return new Scope
        {
            By = site.By ?? "", Script = site.Script ?? "", When = site.When, Gates = site.Gates,
        };
    }

    private static JObject Build(Refs.Site site, SiteConditionsDef place)
    {
        var rule = new JObject();

        var any = new JArray();
        foreach (var scope in ScopesOf(site)) any.Add(ScopeJson(scope, place));
        if (any.Count > 0) rule[GameConditionEdits.AnyKey] = any;

        if (site.IsDialogue)
        {
            var lines = new JArray();
            foreach (long id in LinesOf(site))
                lines.Add(new JObject
                {
                    [GameConditionEdits.DialogueKey] = site.Dialogue ?? "",
                    [GameConditionEdits.NodeKey] = id,
                });
            if (lines.Count > 0) rule[GameConditionEdits.LinesKey] = lines;
        }
        return rule;
    }

    private static JObject ScopeJson(Scope scope, SiteConditionsDef place)
    {
        var json = new JObject
        {
            [GameConditionEdits.ByKey] = scope.By,
            [GameConditionEdits.ScriptKey] = scope.Script,
        };

        var lists = new JArray();
        foreach (var gate in scope.Gates) lists.Add(gate.At.DeepClone());
        if (lists.Count > 0) json[GameConditionEdits.ListsKey] = lists;

        if (!place.RoomsOut.Contains(scope.By, StringComparer.Ordinal))
        {
            var room = RoomOf(scope.By, scope.Script);
            if (room.Count > 0) json[GameConditionEdits.RoomKey] = Written(room);
        }

        // Whatever is checked that the game does not keep in a list - a "play
        // the child at this position" test - as the editor reads it. There is
        // no list to read it from at runtime, so this one is copied.
        int inLists = scope.Gates.Sum(g => g.Count);
        var picked = VanillaDialogueConditions.TranslateAll(scope.When.Skip(inLists).ToList(), out _);
        if (picked.Count > 0) json[GameConditionEdits.FixedKey] = Written(picked);

        return json;
    }

    /// <summary>Where a script lives, as a condition: a room's script only runs
    /// while the player is in that room.</summary>
    public static List<NodeConditionDef> RoomOf(string by, string script)
        => VanillaDialogueSeed.GateOf(new VanillaDialogueCatalog.Start { By = by, Script = script }, null);

    private static JArray Written(IEnumerable<NodeConditionDef> conditions)
        => JArray.Parse(JsonConvert.SerializeObject(conditions, PackRepository.ManifestSettings));
}
