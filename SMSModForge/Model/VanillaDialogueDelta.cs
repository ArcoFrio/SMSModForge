using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Works out what a vanilla dialogue extension actually CHANGES, so the
/// manifest stores — and the runtime applies — only that.
/// <para/>
/// Authoring one means editing a conversation the pack does not own. It is
/// seeded whole from <see cref="VanillaDialogueSeed"/> so the author can read
/// what is there, and almost every line stays exactly as the game has it.
/// Writing all of them out would bloat the manifest and, worse, read as though
/// the pack were asserting lines it merely copied — which matters at the next
/// game update, when a re-asserted line silently pins a conversation to the
/// shape it had when the pack was written.
/// <para/>
/// So at save time every seeded node is compared against the vanilla one:
/// <list type="bullet">
///   <item>A node identical to the game's is dropped entirely; it would have
///   been a no-op at runtime.</item>
///   <item>A node the author changed is kept with ONLY the fields they
///   changed, named in <see cref="DialogueNodeDef.Overrides"/>. That is what
///   the runtime writes back, so a field the pack merely copied is never
///   re-asserted over the game's own - which is what would silently undo a
///   line the next game update rewrites.</item>
///   <item>A node the pack added — one whose id the game does not have — is
///   always kept.</item>
///   <item>A node the author DELETED is recorded as a deletion, because
///   "absent" already means "unchanged" and the two must not be confused.</item>
/// </list>
/// A dialogue with no catalog entry is left exactly as authored, so an editor
/// without the extraction still saves what it was given.
/// <para/>
/// The pass never touches the live model. It builds pruned copies and hands
/// back a restore action, so saving cannot quietly rewrite what is on screen —
/// the same contract as <see cref="VanillaDelta"/> and
/// <see cref="VanillaUiDelta"/>, for the same reason.
/// </summary>
public static class VanillaDialogueDelta
{
    /// <summary>
    /// Node ids the extension removes from the vanilla conversation.
    /// <para/>
    /// Kept as its own list because a pruned manifest holds only what changed,
    /// and a deleted node looks exactly like an unchanged one otherwise —
    /// both are simply not there.
    /// </summary>
    public const string RemovedNodesKey = Shared.VanillaDialogueKeys.RemovedNodes;

    /// <summary>
    /// Swap each vanilla dialogue extension's nodes for its pruned delta.
    /// Returns an action that puts the originals back — call it in a finally.
    /// </summary>
    public static Action PrepareForSave(ModPack? pack)
    {
        var restores = new List<Action>();
        if (pack?.Dialogues == null || !VanillaDialogueCatalog.IsAvailable)
            return () => { };

        foreach (var dialogue in pack.Dialogues)
        {
            if (!dialogue.IsVanillaBased) continue;

            var vanilla = VanillaDialogueCatalog.Open(dialogue.Source);
            if (vanilla == null) continue;      // no baseline → leave as authored

            var original = dialogue.Nodes;
            var originalRemoved = dialogue.RemovedNodes;

            var pruned = Prune(original, vanilla);
            var gone = RemovedNodes(original, vanilla);

            var target = dialogue;
            restores.Add(() =>
            {
                target.Nodes = original;
                target.RemovedNodes = originalRemoved;
            });
            dialogue.Nodes = pruned;
            dialogue.RemovedNodes = gone;
        }

        return () => { foreach (var restore in restores) restore(); };
    }

    /// <summary>
    /// Every field of a node that a pack may change, and how to tell whether it
    /// did.
    /// <para/>
    /// Named explicitly rather than reflected over, because this list IS the
    /// contract: a field here is one the runtime knows how to write into a
    /// vanilla line, and one that is not is one an extension cannot change.
    /// Blanking is what makes the manifest readable - a stored node shows the
    /// change and nothing else.
    /// </summary>
    private static readonly (string Name,
                             Func<DialogueNodeDef, object?> Read,
                             Action<DialogueNodeDef> Blank)[] Fields =
    {
        ("kind", n => n.Kind, n => n.Kind = DialogueNodeKind.Text),
        ("actor", n => n.Actor, n => n.Actor = ""),
        ("expression", n => n.Expression, n => n.Expression = ""),
        ("outfit", n => n.Outfit, n => n.Outfit = ""),
        ("text", n => n.Text, n => n.Text = ""),
        ("tag", n => n.Tag, n => n.Tag = null),
        ("children", n => n.Children, n => n.Children = new List<int>()),
        ("conditions", n => n.Conditions, n => n.Conditions = new List<NodeConditionDef>()),
        ("actionsOnStart", n => n.ActionsOnStart, n => n.ActionsOnStart = new List<NodeActionDef>()),
        ("actionsOnFinish", n => n.ActionsOnFinish, n => n.ActionsOnFinish = new List<NodeActionDef>()),
        ("jump", n => n.Jump, n => n.Jump = null),
        ("duration", n => n.Duration, n => n.Duration = NodeDurationMode.UntilInteraction),
        ("timeout", n => n.Timeout, n => n.Timeout = 3f),
    };

    /// <summary>
    /// The nodes worth storing, each carrying only what the author changed:
    /// the ones that differ from the game's, and the ones the game does not
    /// have.
    /// <para/>
    /// Always a new list. A kept node is a REDUCED COPY rather than the
    /// author's own object, because blanking the fields they did not change
    /// would otherwise empty what is on screen.
    /// </summary>
    public static List<DialogueNodeDef> Prune(
        List<DialogueNodeDef>? authored, VanillaDialogueCatalog.Dialogue vanilla)
    {
        if (authored == null) return new List<DialogueNodeDef>();

        var kept = new List<DialogueNodeDef>();
        foreach (var node in authored)
        {
            var baseline = vanilla.Node(node.Id);
            if (baseline == null)
            {
                kept.Add(node);          // the pack's own line, kept whole
                continue;
            }

            var changed = ChangedFields(node, baseline);
            if (changed.Count == 0) continue;
            kept.Add(Reduced(node, changed));
        }

        return kept;
    }

    /// <summary>The fields of a line a pack may change, in the order they are
    /// shown — for a reset menu, and for anything else that needs to name
    /// them.</summary>
    public static IEnumerable<string> Fieldnames
    {
        get { foreach (var one in Fields) yield return one.Name; }
    }

    /// <summary>
    /// Put a line, or one field of it, back the way the game has it.
    /// <para/>
    /// Undo works on a step; this works on the thing an author is looking at,
    /// which is what they want after trying a line three ways and preferring
    /// the original. Afterwards the field asserts nothing and drops out of the
    /// pack on the next save.
    /// </summary>
    public static void ResetTo(DialogueNodeDef node, VanillaDialogueCatalog.Node baseline,
                               string? field = null)
    {
        if (node == null || baseline == null) return;

        var seeded = VanillaDialogueSeed.SeedNode(node.Id, baseline);
        string json = JsonConvert.SerializeObject(seeded);
        var fresh = JsonConvert.DeserializeObject<DialogueNodeDef>(json)!;

        foreach (var one in Fields)
        {
            if (field != null && one.Name != field) continue;
            Copy(one.Name, fresh, node);
        }

        node.Overrides = field == null
            ? new List<string>()
            : node.Overrides.Where(o => o != field).ToList();
    }

    /// <summary>One field, from one node to another. The table above says
    /// which fields exist; this says how to move one.</summary>
    public static void CopyField(string field, DialogueNodeDef from, DialogueNodeDef to)
        => Copy(field, from, to);

    private static void Copy(string field, DialogueNodeDef from, DialogueNodeDef to)
    {
        switch (field)
        {
            case "kind": to.Kind = from.Kind; break;
            case "actor": to.Actor = from.Actor; break;
            case "expression": to.Expression = from.Expression; break;
            case "outfit": to.Outfit = from.Outfit; break;
            case "text": to.Text = from.Text; break;
            case "tag": to.Tag = from.Tag; break;
            case "children": to.Children = from.Children; break;
            case "conditions": to.Conditions = from.Conditions; break;
            case "actionsOnStart": to.ActionsOnStart = from.ActionsOnStart; break;
            case "actionsOnFinish": to.ActionsOnFinish = from.ActionsOnFinish; break;
            case "jump": to.Jump = from.Jump; break;
            case "duration": to.Duration = from.Duration; break;
            case "timeout": to.Timeout = from.Timeout; break;
        }
    }

    /// <summary>Which of a node's fields say something the vanilla line does
    /// not.</summary>
    public static List<string> ChangedFields(
        DialogueNodeDef authored, VanillaDialogueCatalog.Node baseline)
    {
        var seeded = VanillaDialogueSeed.SeedNode(authored.Id, baseline);
        var changed = new List<string>();

        foreach (var field in Fields)
        {
            if (!Same(field.Read(authored), field.Read(seeded)))
                changed.Add(field.Name);
        }
        return changed;
    }

    /// <summary>A copy of the node holding the changed fields and nothing
    /// else, with those fields named so the runtime knows what to write.</summary>
    private static DialogueNodeDef Reduced(DialogueNodeDef node, List<string> changed)
    {
        var made = JsonConvert.DeserializeObject<DialogueNodeDef>(
            JsonConvert.SerializeObject(node))!;
        made.Id = node.Id;

        foreach (var field in Fields)
            if (!changed.Contains(field.Name)) field.Blank(made);

        made.Overrides = changed;
        return made;
    }

    /// <summary>Two field values, compared the way the manifest would write
    /// them — which is exactly the question being asked.</summary>
    private static bool Same(object? a, object? b)
        => string.Equals(JsonConvert.SerializeObject(a), JsonConvert.SerializeObject(b),
                         StringComparison.Ordinal);

    /// <summary>
    /// Which vanilla lines an extension no longer has.
    /// <para/>
    /// Worked out by comparing what was authored against what the game holds,
    /// rather than recorded as the author deletes — a list built by watching
    /// edits is wrong the moment a pack is loaded, edited and saved by
    /// something that did not watch.
    /// </summary>
    public static List<int> RemovedNodes(
        IEnumerable<DialogueNodeDef>? authored, VanillaDialogueCatalog.Dialogue vanilla)
    {
        var present = new HashSet<int>(authored?.Select(n => n.Id) ?? Enumerable.Empty<int>());
        var gone = new List<int>();

        foreach (string key in vanilla.Nodes.Keys)
        {
            if (!int.TryParse(key, System.Globalization.NumberStyles.Integer,
                              System.Globalization.CultureInfo.InvariantCulture, out int id))
                continue;
            if (!present.Contains(id)) gone.Add(id);
        }

        gone.Sort();
        return gone;
    }

    /// <summary>
    /// Whether an authored node says anything the vanilla one does not.
    /// <para/>
    /// Compared against a freshly seeded copy of the same vanilla node, so the
    /// comparison cannot drift from the seeding: whatever
    /// <see cref="VanillaDialogueSeed"/> puts in, this reads back out, and a
    /// field added to one is automatically covered by the other.
    /// </summary>
    public static bool Differs(DialogueNodeDef authored, VanillaDialogueCatalog.Node baseline)
        => ChangedFields(authored, baseline).Count > 0;
}
