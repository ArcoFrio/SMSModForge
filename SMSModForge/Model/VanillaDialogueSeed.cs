using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Fills a <see cref="DialogueDef"/> from a vanilla conversation, so an author
/// edits what the game actually has rather than a blank page.
/// <para/>
/// This is the seed half of the seed/prune pattern the level and UI extensions
/// already use: everything is copied in, the author changes a line or two, and
/// <see cref="VanillaDialogueDelta"/> works out at save time which of it was
/// actually theirs. Copying it all in is what makes "show me the defaults, and
/// which of them I have changed" answerable at all — the baseline has to be
/// present to be compared against.
/// <para/>
/// Nothing is invented here. A node the editor cannot model keeps Game
/// Creator's own description (see <see cref="VanillaDialogueActions.Represent"/>),
/// and the ids are the game's own, which is what lets a change bind to a line
/// rather than to a position in a list.
/// </summary>
public static class VanillaDialogueSeed
{
    /// <summary>
    /// The whole conversation as an editable dialogue, or null when the
    /// catalog has no such thing — an editor without the extraction offers
    /// nothing to extend rather than failing.
    /// </summary>
    public static DialogueDef? Seed(string? tokenOrPath)
    {
        var entry = VanillaDialogueCatalog.Find(tokenOrPath);
        var vanilla = VanillaDialogueCatalog.Open(tokenOrPath);
        if (entry == null || vanilla == null) return null;

        var made = new DialogueDef
        {
            Key = Key(entry.Id),
            DisplayName = entry.Name,
            Source = entry.Token,
        };

        // Start conditions are deliberately NOT seeded onto the model.
        //
        // They belong to the room, not to the conversation: the game decides
        // whether to play this, and an extension never plays it at all. Seeded
        // in, they were written to the manifest as gates the pack asserts and
        // nothing evaluates - an untouched extension shipped "Day = 2" and
        // "anna-beach = false" as though the author had meant them. Read them
        // with StartGates instead, which is display and nothing more.

        foreach (long root in vanilla.Roots)
            made.RootNodeIds.Add(unchecked((int)root));

        foreach (var pair in vanilla.Nodes)
        {
            if (!long.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture,
                               out long id))
                continue;
            made.Nodes.Add(SeedNode(unchecked((int)id), pair.Value));
        }

        return made;
    }

    /// <summary>
    /// The stored delta laid back over the game's own conversation, so an
    /// author opening a saved pack sees all of it again.
    /// <para/>
    /// The other end of the round trip. Saving keeps only what changed, which
    /// means a re-opened extension is two lines of a hundred and eighteen until
    /// the rest is put back. A stored line overwrites the seeded one field by
    /// field, following the same "overrides" list the runtime follows — so what
    /// the editor shows and what the game does are decided by one thing.
    /// <para/>
    /// A stored line the game no longer has is kept as the pack's own: it may
    /// be a line the pack added, or a line an update removed, and dropping it
    /// silently would lose an author's work either way.
    /// <para/>
    /// Deletions come in separately, because a saved extension lists only the
    /// lines that CHANGED — so absence alone cannot tell "the author left this
    /// alone" from "the author took it out".
    /// </summary>
    public static List<DialogueNodeDef> Merge(string? tokenOrPath,
                                              List<DialogueNodeDef>? stored,
                                              IEnumerable<int>? removedIds,
                                              out int removed)
    {
        removed = 0;
        var gone = removedIds == null ? new HashSet<int>() : new HashSet<int>(removedIds);
        var vanilla = VanillaDialogueCatalog.Open(tokenOrPath);
        if (vanilla == null) return stored ?? new List<DialogueNodeDef>();

        var byId = new Dictionary<int, DialogueNodeDef>();
        foreach (var node in stored ?? new List<DialogueNodeDef>())
            byId[node.Id] = node;

        var merged = new List<DialogueNodeDef>();
        var seen = new HashSet<int>();

        foreach (var pair in vanilla.Nodes)
        {
            if (!int.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture,
                              out int id))
                continue;

            var line = SeedNode(id, pair.Value);
            if (byId.TryGetValue(id, out var change))
            {
                Apply(change, line);
                seen.Add(id);
            }
            else if (gone.Contains(id))
            {
                removed++;
                continue;                    // the author took this line out
            }
            merged.Add(line);
        }

        // Anything stored that the game has no line for.
        foreach (var node in stored ?? new List<DialogueNodeDef>())
            if (!seen.Contains(node.Id) && vanilla.Node(node.Id) == null)
                merged.Add(node);

        return merged;
    }

    /// <summary>One stored line's changes, over the seeded one — only the
    /// fields it names.</summary>
    private static void Apply(DialogueNodeDef change, DialogueNodeDef onto)
    {
        // A stored line with no override list is one the pack owns outright
        // (or one written before the list existed), so all of it counts.
        var fields = change.Overrides != null && change.Overrides.Count > 0
            ? (IEnumerable<string>)change.Overrides
            : VanillaDialogueDelta.Fieldnames;

        foreach (string field in fields)
            VanillaDialogueDelta.CopyField(field, change, onto);
    }

    /// <summary>
    /// What the game checks before it plays this conversation, for showing an
    /// author beside the lines they are editing.
    /// <para/>
    /// Display only, and never stored: the gate is the room's, an extension has
    /// no say in it, and a pack that wrote it down would be asserting something
    /// it does not control. Several places may play one conversation, and they
    /// are alternatives, so they read as an Any-of rather than a list that
    /// would mean "all at once".
    /// </summary>
    public static List<NodeConditionDef> StartGates(string? tokenOrPath)
    {
        var vanilla = VanillaDialogueCatalog.Open(tokenOrPath);
        var gates = new List<NodeConditionDef>();
        if (vanilla == null) return gates;

        foreach (var start in vanilla.Starts)
        {
            var when = new List<NodeConditionDef>();

            // Where it is played from is itself a condition, and the one an
            // author is most likely to want: a room talk only runs while its
            // room is the one on screen. The game does not write that down
            // because it does not have to - the component lives on the room -
            // so it reads as a conversation with no gate at all unless it is
            // said out loud here.
            var level = LevelOf(start);
            if (level != null) when.Add(level);

            when.AddRange(VanillaDialogueConditions.TranslateAll(start.When, out _));
            if (when.Count == 0) continue;

            gates.Add(when.Count == 1
                ? when[0]
                : new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = when });
        }

        return gates.Count > 1
            ? new List<NodeConditionDef>
              {
                  new() { Type = NodeConditionTypes.GroupAny, Conditions = gates },
              }
            : gates;
    }

    /// <summary>
    /// The level a start site implies, as a condition — or null when the site
    /// says nothing about where the player is.
    /// <para/>
    /// The game groups its room conversations under <c>8_Room_Talk/&lt;room&gt;</c>,
    /// one object per room, holding the Conditions component that decides which
    /// of that room's conversations plays. Being on that object IS the "and the
    /// player is here" half of the gate; only the other half is written down.
    /// <para/>
    /// Null for a start that is not a room talk — an ending, a button, a
    /// trigger somewhere else — because those are reached from anywhere, and a
    /// level named for one would be an invention rather than a reading.
    /// </summary>
    private static NodeConditionDef? LevelOf(VanillaDialogueCatalog.Start start)
    {
        const string root = "8_Room_Talk/";
        string by = start?.By ?? "";
        if (!by.StartsWith(root, StringComparison.Ordinal)) return null;

        // The room, and nothing under it: a conversation nested deeper is
        // still that room's.
        string room = by.Substring(root.Length);
        int slash = room.IndexOf('/');
        if (slash >= 0) room = room.Substring(0, slash);

        var place = VanillaPlaces.All.FirstOrDefault(
            p => string.Equals(p.RoomTalkName, room, StringComparison.OrdinalIgnoreCase));
        if (place == null) return null;

        return new NodeConditionDef
        {
            Type = NodeConditionTypes.LevelActive,
            Params = new Dictionary<string, string> { ["level"] = "vanilla:" + place.GoName },
        };
    }

    /// <summary>
    /// One vanilla line as an authorable node.
    /// <para/>
    /// Public because <see cref="VanillaDialogueDelta"/> compares against it:
    /// deciding whether an author changed a line means asking what the line
    /// looked like before they touched it, and that has to be the same answer
    /// this gave when it seeded it.
    /// </summary>
    public static DialogueNodeDef SeedNode(int id, VanillaDialogueCatalog.Node from)
    {
        var made = new DialogueNodeDef
        {
            Id = id,
            Kind = Kind(from.Kind),

            // The game's actor and expression, not a pack's. Both are shown so
            // the author can see who speaks and how; neither is written back
            // unless changed, because the delta only carries a difference and
            // the game's own node keeps whatever is not overridden.
            Actor = from.Actor ?? "",

            // The expression by NAME. Seeded as the raw index, the editor
            // showed "4" in a field that means a name like "Flirty" - a number
            // nobody could act on, in a box that says it wants a key. A node
            // whose index points past its actor's list keeps nothing rather
            // than being given a face it does not have.
            Expression = from.ExpressionName ?? "",

            // What the speaker is wearing, replayed from what the conversation
            // stages rather than read off the node - the game keeps no such
            // field. Empty for the lines whose bust was switched on somewhere
            // outside the dialogue, which is honest: the editor showed empty
            // for all of them before, and empty still means "whatever the game
            // already had on screen".
            Outfit = from.Outfit ?? "",
            Text = from.PlainText ?? "",
            Tag = string.IsNullOrEmpty(from.Tag) ? null : from.Tag,
            Children = from.Children.Select(c => unchecked((int)c)).ToList(),
            Conditions = VanillaDialogueConditions.TranslateAll(from.Conditions, out _),
            ActionsOnStart = VanillaDialogueActions.RepresentAll(from.OnStart, out _),
            ActionsOnFinish = VanillaDialogueActions.RepresentAll(from.OnFinish, out _),
        };

        // What happens after the line, and how it advances.
        //
        // Left out at first, and wrongly: 666 of the game's lines Exit or jump
        // rather than continuing, and 381 time out rather than waiting - so
        // every one of those was shown to the author as the opposite of what it
        // does. The delta compared seed against seed and never noticed, which
        // is exactly how a wrong default hides.
        if (from.Jump != null)
        {
            string mode = (string?)from.Jump["mode"] ?? "Continue";
            if (mode == "Exit")
                made.Jump = new JumpDef { Mode = JumpMode.Exit };
            else if (mode == "Jump")
                made.Jump = new JumpDef
                {
                    Mode = JumpMode.Jump,
                    TargetTag = (string?)from.Jump["to"] ?? "",
                };
        }

        if (from.Duration == "Timeout")
        {
            made.Duration = NodeDurationMode.Timeout;
            if (from.Timeout is Newtonsoft.Json.Linq.JValue seconds
                && (seconds.Type == Newtonsoft.Json.Linq.JTokenType.Float
                    || seconds.Type == Newtonsoft.Json.Linq.JTokenType.Integer))
                made.Timeout = System.Convert.ToSingle(seconds.Value, CultureInfo.InvariantCulture);
        }

        return made;
    }

    /// <summary>What the game calls a node type, as what the editor calls
    /// one.</summary>
    private static DialogueNodeKind Kind(string? kind)
    {
        switch (kind)
        {
            case "choice": return DialogueNodeKind.Choice;
            case "random": return DialogueNodeKind.Random;
            default: return DialogueNodeKind.Text;
        }
    }

    /// <summary>
    /// A pack-local key from the conversation's path.
    /// <para/>
    /// The path rather than the name, because 35 dialogue names in this game
    /// belong to more than one conversation and "defaultdialogue" would collide
    /// thirty times over.
    /// </summary>
    public static string Key(string id)
    {
        var built = new System.Text.StringBuilder(id.Length);
        foreach (char c in id)
            built.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');

        string key = built.ToString().Trim('-');
        while (key.Contains("--")) key = key.Replace("--", "-");
        return key.Length == 0 ? "vanilla-dialogue" : key;
    }
}
