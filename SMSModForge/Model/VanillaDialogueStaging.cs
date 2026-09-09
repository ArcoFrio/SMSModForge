using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Which bust each vanilla line is spoken with.
/// <para/>
/// A Game Creator node names its actor and its expression, and says nothing at
/// all about what that actor is WEARING. The game decides that elsewhere: the
/// trigger that opens a conversation switches busts on before it hands over,
/// and lines switch them again mid-scene when somebody undresses. So the
/// outfit is not a field to be read — it is a running state to be replayed.
/// <para/>
/// That is what this does. Start from what the conversation's own trigger
/// staged, walk the lines in the order a player meets them, and apply every
/// <c>SetActive</c> naming a bust. A line's outfit is whatever its speaker was
/// last switched into.
/// <para/>
/// It answers about seven lines in ten, and it declines the rest rather than
/// guessing: a conversation that never stages a bust is one where somebody
/// else — the room, an NPC's own trigger — put the character on screen, and
/// nothing in the dialogue can say which one. Those keep no outfit, which is
/// also what they meant before this existed.
/// </summary>
public static class VanillaDialogueStaging
{
    private const string BustRoot = "2_Bust_Manager/";

    /// <summary>
    /// Work out every line's outfit and write it onto the conversation.
    /// <para/>
    /// Done once, where the catalog loads it, so the seeded value and the one
    /// the delta compares against are the same object's — a derivation run
    /// twice is a derivation that can disagree with itself.
    /// </summary>
    public static void Apply(VanillaDialogueCatalog.Dialogue? dialogue)
    {
        if (dialogue == null) return;

        // What the trigger switched on before the first line.
        var worn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in dialogue.Starts)
            foreach (var step in start.Before)
                Wear(step, worn);

        foreach (var pair in dialogue.InOrder())
        {
            var node = pair.Value;

            foreach (var step in node.OnStart) Wear(step, worn);

            string? who = Character(node.Actor);
            if (who != null && worn.TryGetValue(who, out string? bust))
                node.Outfit = bust;

            foreach (var step in node.OnFinish) Wear(step, worn);
        }
    }

    /// <summary>
    /// Which of the game's characters an actor is, or null for one that is not
    /// a character at all.
    /// <para/>
    /// Two names for the same person have to be brought together first: a node
    /// names the actor ASSET ("DrFrost"), a bust is filed under the artwork's
    /// character ("Doctor Frost"), and neither is the other. The catalog
    /// carries what the game says out loud, and the cast is keyed by it.
    /// <para/>
    /// Null for "Continue" and "You" — the two actors that exist to drive the
    /// dialogue box rather than to stand on the stage. Nobody wears anything.
    /// </summary>
    public static string? Character(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return null;

        string said = VanillaDialogueCatalog.SpokenActorName(actor!);
        return VanillaCharacters.Find(said)?.Name;
    }

    /// <summary>One step, applied to who is wearing what.</summary>
    private static void Wear(VanillaDialogueCatalog.Step? step,
                             Dictionary<string, string> worn)
    {
        string? bust = Bust(step);
        if (bust == null) return;

        var owner = VanillaCharacters.Owning(bust);
        if (owner == null) return;

        bool? on = Switch(step);
        if (on == null) return;                       // set from a variable

        if (on.Value) worn[owner.Name] = bust;
        else if (worn.TryGetValue(owner.Name, out string? had)
                 && string.Equals(had, bust, StringComparison.OrdinalIgnoreCase))
            worn.Remove(owner.Name);
    }

    /// <summary>The bust a step switches, or null when it switches something
    /// else. A step naming a CHILD of a bust is not the bust.</summary>
    private static string? Bust(VanillaDialogueCatalog.Step? step)
    {
        if (step == null || step.Kind != "setActive" || step.Fields == null) return null;

        string? path = (string?)step.Fields["m_GameObject"]?["path"];
        if (path == null || !path.StartsWith(BustRoot, StringComparison.Ordinal)) return null;

        string name = path.Substring(BustRoot.Length);
        return name.Length == 0 || name.Contains('/') ? null : name;
    }

    /// <summary>On or off, or null when the game reads it from a variable and
    /// this cannot know.</summary>
    private static bool? Switch(VanillaDialogueCatalog.Step step)
    {
        var active = step.Fields?["m_Active"];
        if (active == null || (string?)active["kind"] != "value") return null;

        var value = active["value"];
        return value is Newtonsoft.Json.Linq.JValue set
               && set.Type == Newtonsoft.Json.Linq.JTokenType.Boolean
            ? (bool)set.Value!
            : (bool?)null;
    }

    /// <summary>
    /// Every bust a character is switched into anywhere in one conversation.
    /// <para/>
    /// One means the derivation is not really a derivation: the character wears
    /// that and nothing else, whichever way the branches go. More than one
    /// means the scene changes their clothes, and the answer depends on where a
    /// line sits — true of thirty-nine conversations in the game, and every one
    /// of them a deliberate reveal.
    /// </summary>
    public static IReadOnlyDictionary<string, List<string>> Wardrobe(
        VanillaDialogueCatalog.Dialogue? dialogue)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (dialogue == null) return found;

        void Note(VanillaDialogueCatalog.Step step)
        {
            string? bust = Bust(step);
            if (bust == null || Switch(step) != true) return;

            var owner = VanillaCharacters.Owning(bust);
            if (owner == null) return;

            if (!found.TryGetValue(owner.Name, out var list))
                found[owner.Name] = list = new List<string>();
            if (!list.Contains(bust, StringComparer.OrdinalIgnoreCase)) list.Add(bust);
        }

        foreach (var start in dialogue.Starts)
            foreach (var step in start.Before) Note(step);

        foreach (var node in dialogue.Nodes.Values)
        {
            foreach (var step in node.OnStart) Note(step);
            foreach (var step in node.OnFinish) Note(step);
        }

        return found;
    }
}
