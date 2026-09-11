using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.Validation;

public enum Severity { Info, Warning, Error }

/// <summary>
/// One problem found in a pack.
/// <para/>
/// <paramref name="Code"/> is a stable identifier for the KIND of problem, and
/// exists so an author can silence one. Suppressing by message text would come
/// undone the moment a message is reworded, and silently: the issue would
/// simply reappear with no way to tell it from a new one. Codes are dotted and
/// terminal — <c>art.bustSize</c>, not a sentence.
/// <para/>
/// It is empty on checks that predate this, which costs nothing: such an issue
/// can still be dismissed individually through <see cref="Key"/>, it just
/// cannot be dismissed by type until it is given a code.
/// </summary>
public sealed record ValidationIssue(Severity Severity, string Where, string Message,
                                     string Code = "")
{
    /// <summary>
    /// Identifies this one issue for suppression. The code plus the place it
    /// was found, so silencing "this bust is 512x512" does not also silence a
    /// different bust with the same problem.
    /// <para/>
    /// Falls back to the message for issues with no code yet — less durable,
    /// but it is the only stable thing such an issue has.
    /// </summary>
    public string Key => Code.Length > 0 ? $"{Code}@{Placeless}" : $"{Placeless}|{Message}";

    /// <summary>
    /// <see cref="Where"/> with list POSITIONS dropped.
    /// <para/>
    /// Where now says which action or condition in a list an issue is about,
    /// so that double-clicking it can jump to that one. An ignore entry must
    /// not be that specific: it is written into the pack, and keying it on a
    /// position would come undone the moment an action is inserted above the
    /// one that was silenced. Dropping the digits also keeps the entries
    /// already saved in packs working.
    /// </summary>
    private string Placeless => IndexRx.Replace(Where ?? "", "");

    private static readonly Regex IndexRx =
        new(@"\[\d+\]", RegexOptions.Compiled);

    /// <summary>
    /// Set only on the issues returned by a listing that asked for silenced
    /// ones as well. A list that mixes live and silenced issues without
    /// distinguishing them is worse than either on its own — the author cannot
    /// tell which ones are already dealt with, and the un-silence action has no
    /// visible target.
    /// </summary>
    public bool Ignored { get; init; }
}

/// <summary>
/// Verifies that a pack on disk is internally consistent and ready to ship to
/// the mod. The mod is forgiving (will run with missing PNGs, just with bad
/// sprites), so most file-missing problems are warnings, not errors.
/// </summary>
public static class PackValidator
{
    /// <summary>
    /// Every problem in the pack, minus whatever the author has chosen not to
    /// hear about. Pass <paramref name="includeIgnored"/> to get the lot, which
    /// is how the editor offers to un-ignore something.
    /// </summary>
    public static List<ValidationIssue> Validate(ModPack pack, string packRoot,
                                                 bool includeIgnored = false)
    {
        var all = Collect(pack, packRoot);
        if (pack.IgnoredIssues.Count == 0) return all;

        var ignored = new HashSet<string>(pack.IgnoredIssues, System.StringComparer.Ordinal);
        bool IsSilenced(ValidationIssue i) =>
            ignored.Contains(i.Key) || (i.Code.Length > 0 && ignored.Contains(i.Code));

        if (!includeIgnored) return all.FindAll(i => !IsSilenced(i));

        // Stamped rather than filtered, so the caller can show which of the
        // rows it is listing are the silenced ones.
        return all.ConvertAll(i => IsSilenced(i) ? i with { Ignored = true } : i);
    }

    /// <summary>Whether this issue is currently silenced, and by which entry —
    /// the whole code, or just this one occurrence.</summary>
    /// <summary>
    /// Dialogue text that spells out a family word the player chose for
    /// themselves.
    /// <para/>
    /// The game lets a player decide what they call their mother, father and
    /// brother, and gives dialogue <c>{M}</c>, <c>{D}</c> and <c>{B}</c> to say
    /// it back to them. A line that writes "Mom" instead says Mom to everybody
    /// — including the player who picked something else, who has no way to tell
    /// a pack's line from the game getting their choice wrong.
    /// <para/>
    /// Only whole words count. "Mom" is caught; "moment" and "Brotherhood" are
    /// not, and neither is a word inside a token that is already correct.
    /// <para/>
    /// A warning, never an error. A character genuinely called Mom by somebody
    /// who is not their child is a legitimate line, which is exactly why this
    /// is a note the author can dismiss rather than a rule.
    /// </summary>
    /// <summary>
    /// The lines of a conversation that are the author's to answer for.
    /// <para/>
    /// All of them for a dialogue the pack wrote. For one that EXTENDS a
    /// vanilla conversation, only the lines the author added or changed: the
    /// rest are the game's, they shipped in it, and telling somebody their
    /// pack has a problem because Game Creator wrote a Choice with no options
    /// in 2019 is noise they cannot act on. Worse, it buries the one warning
    /// in a hundred that is actually theirs.
    /// <para/>
    /// "Changed" is decided by the same comparison that draws the change
    /// markers beside the lines, so what is validated and what is shown as
    /// edited can never drift apart.
    /// <para/>
    /// This is about REPORTING only. Every check that builds a set to
    /// cross-reference against — which ids exist, which tags are taken, which
    /// nodes a Choice offers — still walks the whole conversation, because a
    /// new line's parent and a new jump's target are usually vanilla ones.
    /// </summary>
    private static Func<DialogueNodeDef, bool> AuthorsOwn(DialogueDef dialogue)
    {
        if (dialogue == null || !dialogue.IsVanillaBased) return _ => true;

        var vanilla = VanillaDialogueCatalog.Open(dialogue.Source);
        if (vanilla == null) return _ => true;   // never heard of it: check it all

        var decided = new Dictionary<int, bool>();
        return node =>
        {
            if (node == null) return false;
            if (decided.TryGetValue(node.Id, out bool owned)) return owned;

            var baseline = vanilla.Node(node.Id);

            // A line the game does not have is one the author added.
            owned = baseline == null
                 || VanillaDialogueDelta.ChangedFields(node, baseline).Count > 0;

            decided[node.Id] = owned;
            return owned;
        };
    }

    private static void CheckKinWordsInText(List<ValidationIssue> issues, ModPack pack)
    {
        // The word, and the token that should almost always replace it.
        var kin = new (string Word, string Token)[]
        {
            ("mom", "{M}"), ("mum", "{M}"), ("mother", "{M}"),
            ("dad", "{D}"), ("father", "{D}"),
            ("brother", "{B}"),
        };

        foreach (var d in pack.Dialogues)
        {
            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                string text = n.Text;
                if (string.IsNullOrWhiteSpace(text)) continue;

                foreach (var (word, token) in kin)
                {
                    if (!ContainsWord(text, word)) continue;
                    issues.Add(new(Severity.Warning,
                        $"dialogues[{d.Key}].nodes[{n.Id}].text",
                        $"This line writes \"{word}\". Players choose what they call their " +
                        $"family, so {token} says it back the way they picked it — writing the " +
                        "word says it to everyone, including whoever chose something else.",
                        "dialogue.kinWord"));
                    break;   // one note per line is enough to make the point
                }
            }
        }
    }

    /// <summary>
    /// Everyone the game's own version of a conversation has a part for.
    /// <para/>
    /// Its declared roles plus anybody a line is actually given to — the two
    /// are usually the same, and where they differ it is a line spoken by
    /// somebody the conversation never declared, who is still plainly in it.
    /// <para/>
    /// Empty for a dialogue of the pack's own, and for one whose conversation
    /// this build has never heard of, so neither stops being checked.
    /// </summary>
    private static HashSet<string> VanillaActors(DialogueDef dialogue)
    {
        var found = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (dialogue == null || !dialogue.IsVanillaBased) return found;

        var vanilla = VanillaDialogueCatalog.Open(dialogue.Source);
        if (vanilla == null) return found;

        foreach (string role in vanilla.Roles)
            if (!string.IsNullOrEmpty(role)) found.Add(role);
        foreach (var node in vanilla.Nodes.Values)
            if (!string.IsNullOrEmpty(node.Actor)) found.Add(node.Actor!);

        return found;
    }

    /// <summary>Whole-word, case-insensitive. Matching anywhere would flag
    /// "moment" and "Brotherhood", and an author who is told about those stops
    /// reading the warnings.</summary>
    private static bool ContainsWord(string text, string word)
    {
        int i = 0;
        while ((i = text.IndexOf(word, i, System.StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool startsClean = i == 0 || !char.IsLetter(text[i - 1]);
            int after = i + word.Length;
            bool endsClean = after >= text.Length || !char.IsLetter(text[after]);
            if (startsClean && endsClean) return true;
            i = after;
        }
        return false;
    }

    /// <summary>
    /// On-start actions on a Choice's options, which the editor no longer
    /// offers and older packs may still carry.
    /// <para/>
    /// The reason they went is that nobody can currently say WHEN one of those
    /// nodes counts as having started. If it is when the menu is drawn rather
    /// than when the player picks an option, then every option's on-start
    /// actions run every time the choice appears — including the ones nobody
    /// took. A pack built on that assumption looks correct while it is being
    /// written and behaves strangely in play, which is the worst shape a bug
    /// can have.
    /// <para/>
    /// A warning rather than an error: the actions are still in the manifest
    /// and the runtime will still run them, so nothing is silently dropped.
    /// It is a decision the author needs to make, not one to make for them.
    /// </summary>
    private static void CheckChoiceOptionActions(List<ValidationIssue> issues, ModPack pack)
    {
        foreach (var d in pack.Dialogues)
        {
            // A node is an option when a Choice lists it as a child.
            var optionIds = new HashSet<int>();
            foreach (var n in d.Nodes)
                if (n.Kind == DialogueNodeKind.Choice)
                    foreach (var childId in n.Children)
                        optionIds.Add(childId);

            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                if (!optionIds.Contains(n.Id)) continue;
                if (n.ActionsOnStart == null || n.ActionsOnStart.Count == 0) continue;

                issues.Add(new(Severity.Warning,
                    $"dialogues[{d.Key}].nodes[{n.Id}].actionsOnStart",
                    $"This node is one of a Choice's options and has {n.ActionsOnStart.Count} " +
                    "action(s) on start. When an option counts as STARTED is not something the " +
                    "pack can rely on — it may be when the menu is drawn rather than when the " +
                    "player picks it, in which case these run for options nobody chose. Move " +
                    "them to Actions on finish, which happens once the option has been taken.",
                    "dialogue.choiceOptionOnStart"));
            }
        }
    }

    /// <summary>
    /// A Choice node with no children — a menu with nothing to pick.
    /// <para/>
    /// Worth checking because the editor used to produce these by itself: a
    /// node added with + Child or + Sibling inherited the kind of the node it
    /// was added from, so every option under a Choice became a Choice too, and
    /// so did everything under those. New nodes no longer inherit the kind, but
    /// packs authored before that carry the damage in the manifest, where it is
    /// invisible until the conversation is played.
    /// <para/>
    /// A warning rather than an error: the fix is one dropdown, and the author
    /// is the one who knows whether a menu was intended here.
    /// </summary>
    private static void CheckChoicesWithoutOptions(List<ValidationIssue> issues, ModPack pack)
    {
        foreach (var d in pack.Dialogues)
        {
            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                if (n.Kind != DialogueNodeKind.Choice) continue;
                if (n.Children != null && n.Children.Count > 0) continue;

                issues.Add(new(Severity.Warning,
                    $"dialogues[{d.Key}].nodes[{n.Id}].kind",
                    "This node is a Choice with no children, so it is a menu offering " +
                    "nothing to pick. If it is meant to be a line, set Kind to Text. " +
                    "Older packs collect these on their own: adding a node with + Child " +
                    "or + Sibling used to copy the kind of the node it came from, so " +
                    "every option under a Choice was saved as a Choice as well.",
                    "dialogue.choiceWithoutOptions"));
            }
        }
    }

    /// <summary>
    /// <c>[PV:name]</c> tokens that will not do what they look like.
    /// <para/>
    /// Two failures, both silent, which is the whole reason to check them:
    /// <list type="bullet">
    ///   <item>A name no variable in this pack declares. The runtime asks the
    ///   store for it and the store answers with an empty string, so the token
    ///   does not stay on screen looking wrong — it VANISHES, and the line just
    ///   reads oddly with a word missing.</item>
    ///   <item>A token that never closes: <c>[PV:gold</c>, or a bracket that
    ///   turned into a brace. The pattern needs a closing <c>]</c>, so a
    ///   mistyped one matches nothing and is printed to the player exactly as
    ///   it was typed.</item>
    /// </list>
    /// Warnings, not errors: a name can legitimately be filled in later, and a
    /// stray bracket in ordinary prose is the author's business.
    /// </summary>
    private static void CheckTextTokens(List<ValidationIssue> issues, ModPack pack)
    {
        var declared = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var v in pack.Variables) declared.Add(v.Name);

        void Sweep(string text, string where)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("[PV:", System.StringComparison.Ordinal) < 0)
                return;

            foreach (Match m in TokenRx.Matches(text))
            {
                string name = m.Groups[1].Value.Trim();
                // A templated name ({item} inside a repeating rule) is resolved
                // before the runtime ever sees it, so it cannot be checked here.
                if (name.Length == 0 || IsTemplated(name) || declared.Contains(name)) continue;
                issues.Add(new(Severity.Warning, where,
                    $"Text uses [PV:{name}], but no variable called '{name}' is declared in " +
                    "this pack. An undeclared name reads as an empty string, so the token " +
                    "disappears from the line rather than showing up as a mistake.",
                    "text.unknownPackVar"));
            }

            // Every "[PV:" that TokenRx did not consume never closed.
            int opens = CountOccurrences(text, "[PV:");
            int closed = TokenRx.Matches(text).Count;
            if (opens > closed)
                issues.Add(new(Severity.Warning, where,
                    "Text has a [PV: that never closes with a ]. The token only counts " +
                    "when it ends in a square bracket, so this one is shown to the player " +
                    "exactly as it was typed.",
                    "text.malformedToken"));
        }

        foreach (var d in pack.Dialogues)
        {
            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
                if (mine(n)) Sweep(n.Text, $"dialogues[{d.Key}].nodes[{n.Id}].text");
        }

        // Button labels resolve the same syntax, live, every tick.
        foreach (var b in pack.MapButtons)
            Sweep(b.Label, $"mapButtons[{b.Target}].label");

        foreach (var pl in pack.Places)
            for (int i = 0; i < pl.NavigatorButtons.Count; i++)
                Sweep(pl.NavigatorButtons[i].Label,
                      $"places[{pl.Key}].navigatorButtons[{i}].label");

        foreach (var e in pack.VanillaExtensions)
            for (int i = 0; i < e.NavigatorButtons.Count; i++)
                Sweep(e.NavigatorButtons[i].Label,
                      $"vanillaExtensions[{e.Source}].navigatorButtons[{i}].label");
    }

    /// <summary>Same pattern the runtime substitutes with — see
    /// TextPlaceholders in the plugin. Kept identical on purpose: a checker
    /// that accepted more than the runtime does would pass tokens that then
    /// fail in game.</summary>
    private static readonly Regex TokenRx = new(@"\[PV:([^\]]+)\]", RegexOptions.Compiled);

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        { n++; i += needle.Length; }
        return n;
    }
    /// <summary>
    /// References that name one of the pack's own units — a track, an effect,
    /// a scene — and no longer find it.
    /// <para/>
    /// These were checked for being PRESENT and never for pointing at
    /// anything. That was survivable while an author typed keys by hand and
    /// rarely changed them; it is not now that a runtime name follows the
    /// display name, because renaming a track silently orphans every
    /// reference to it. Place references were already covered — a navigator
    /// target reports as unreachable — which is why renaming a place was loud
    /// and renaming a track was not.
    /// <para/>
    /// Warnings, not errors. A rule can carry a templated value that resolves
    /// to a real key at runtime, and the music fields also accept the names of
    /// the game's own tracks, which this pack knows nothing about.
    /// </summary>
    private static void CheckUnitReferences(List<ValidationIssue> issues, ModPack pack)
    {

        var sfx = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var f in pack.Sfx) sfx.Add(f.Key);
        var scenes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var sc in pack.Scenes) scenes.Add(sc.Key);

        // A value the runtime resolves for itself cannot be checked here.
        static bool Deferred(string v) =>
            string.IsNullOrWhiteSpace(v) || v.StartsWith("$") || IsTemplated(v);

        void Ref(string value, HashSet<string> known, string what, string code, string where,
                 string extra = "")
        {
            if (Deferred(value) || known.Contains(value.Trim())) return;
            issues.Add(new(Severity.Warning, where,
                $"'{value}' is not {what} in this pack. A runtime name follows its display " +
                "name, so renaming one leaves references behind." + extra, code));
        }

        void Action(NodeActionDef a, string where)
        {
            if (a == null) return;
            string aw = $"{where}.{a.Type}";

            // No check on SwitchMusic's music. It resolves ANY child of
            // 12_AudioPlayer by name, and the game's own tracks live there
            // beside the pack's, so a name this pack does not declare is
            // ordinary authoring rather than a mistake. Warning on it flagged
            // correct work, which is worse than not checking: the editor has
            // no catalog of the game's audio objects to check against.

            if (a.Type == NodeActionTypes.PlaySFX &&
                a.Params.TryGetValue("clip", out var clip))
                Ref(clip, sfx, "an effect", "action.unknownSfx", aw);

            // Scene is a category on the targeting row rather than a type of
            // its own, so the kind decides whether target names a scene.
            if (a.Params.TryGetValue("kind", out var kind) &&
                string.Equals(kind, "Scene", System.StringComparison.OrdinalIgnoreCase) &&
                a.Params.TryGetValue("target", out var scene))
                Ref(scene, scenes, "a scene", "action.unknownScene", aw);

            // Animation is a Scenes-category feature, and saying so here is
            // the whole point: the runtime refuses it elsewhere, and an author
            // who only finds out by reading a log has already shipped.
            //
            // A bust or a level layer is drawn on a rig with its own materials,
            // masks and overlays, and animating one would break it in ways
            // nothing here could explain. "Not yet" is a much better answer
            // than a scene that silently does not move.
            if (a.Type == NodeActionTypes.SetSprite &&
                a.Params.TryGetValue("sprite", out var art) &&
                SMSModForge.Shared.MediaKinds.IsAnimated(art ?? ""))
            {
                a.Params.TryGetValue("kind", out var target);
                if (!string.Equals(target, "Scene", System.StringComparison.OrdinalIgnoreCase))
                    issues.Add(new(Severity.Error, $"{aw}.sprite",
                        $"'{art}' is animated, and animation is only supported for the Scenes " +
                        $"category — this action targets '{target ?? "nothing"}'. Point it at a " +
                        "scene, or use a still image here.",
                        "action.animatedSpriteCategory"));
            }

            if (a.Branches != null)
                for (int i = 0; i < a.Branches.Count; i++)
                    Action(a.Branches[i].Action, $"{aw}.branch[{i}]");
        }

        foreach (var d in pack.Dialogues)
        {
            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                string nw = $"dialogues[{d.Key}].nodes[{n.Id}]";
                for (int i = 0; i < n.ActionsOnStart.Count; i++)
                    Action(n.ActionsOnStart[i], $"{nw}.actionsOnStart[{i}]");
                for (int i = 0; i < n.ActionsOnFinish.Count; i++)
                    Action(n.ActionsOnFinish[i], $"{nw}.actionsOnFinish[{i}]");
            }
        }

        foreach (var r in pack.IntegrationRules)
        {
            string rw = $"integrationRules.{r.Key}";
            for (int i = 0; i < r.Actions.Count; i++) Action(r.Actions[i], $"{rw}.actions[{i}]");
            for (int b = 0; b < r.Branches.Count; b++)
                for (int i = 0; i < r.Branches[b].Actions.Count; i++)
                    Action(r.Branches[b].Actions[i], $"{rw}.branches[{b}].actions[{i}]");
        }

        // Level hooks run the same action vocabulary on a place's edges.
        foreach (var pl in pack.Places)
        {
            for (int h = 0; h < pl.OnEnter.Count; h++)
                for (int i = 0; i < pl.OnEnter[h].Actions.Count; i++)
                    Action(pl.OnEnter[h].Actions[i], $"places[{pl.Key}].onEnter[{h}].actions[{i}]");
            for (int h = 0; h < pl.OnExit.Count; h++)
                for (int i = 0; i < pl.OnExit[h].Actions.Count; i++)
                    Action(pl.OnExit[h].Actions[i], $"places[{pl.Key}].onExit[{h}].actions[{i}]");
        }

        // The buttons' Music boxes are not checked either, for the same
        // reason: they name a child of 12_AudioPlayer, which may be one of
        // the game's.
    }
    public static bool IsIgnored(ModPack pack, ValidationIssue issue)
        => pack.IgnoredIssues.Contains(issue.Key) ||
           (issue.Code.Length > 0 && pack.IgnoredIssues.Contains(issue.Code));

    private static List<ValidationIssue> Collect(ModPack pack, string packRoot)
    {
        var issues = new List<ValidationIssue>();

        // Art sizes. Kept in their own file: it is the only check that opens
        // files rather than reading the manifest, and the only one whose cost
        // grows with the size of the pack.
        try { ArtDimensions.CheckAll(issues, pack, packRoot); }
        catch (System.Exception) { /* never let a bad file stop the rest */ }

        CheckChoiceOptionActions(issues, pack);
        CheckChoicesWithoutOptions(issues, pack);
        CheckKinWordsInText(issues, pack);
        CheckTextTokens(issues, pack);
        CheckUnitReferences(issues, pack);

        if (string.IsNullOrWhiteSpace(pack.PackId))
            issues.Add(new(Severity.Error, "$.packId", "packId is required", "pack.idMissing"));

        var seenKeys = new HashSet<string>();
        foreach (var character in pack.Characters)
        {
            var where = $"characters[{character.Name}]";
            if (string.IsNullOrWhiteSpace(character.Name))
                issues.Add(new(Severity.Error, where, "Character name is required", "character.nameMissing"));

            foreach (var outfit in character.Outfits)
            {
                var oWhere = $"{where}.outfits[{outfit.Key}]";

                if (string.IsNullOrWhiteSpace(outfit.Key))
                    issues.Add(new(Severity.Error, oWhere, "Outfit key is required", "outfit.keyMissing"));
                else if (!seenKeys.Add(outfit.Key))
                    issues.Add(new(Severity.Error, oWhere, $"Duplicate outfit key '{outfit.Key}'", "outfit.duplicateKey"));

                if (string.IsNullOrWhiteSpace(outfit.GameObjectName))
                    issues.Add(new(Severity.Error, oWhere, "gameObjectName is required", "outfit.gameObjectNameMissing"));

                // A borrowed character's own outfits are the game's bust names
                // and carry no art on purpose, so there is nothing to check for
                // on disk — and demanding sprites for them would bury the real
                // issues under a warning for every file such an outfit will
                // never have. What CAN be checked is the part the pack does
                // supply: the textures it replaces on that bust.
                //
                // A bust the pack ADDED to one of the game's characters is not
                // one of these. It is the pack's own art in every respect, and
                // falls through to the same checks a pack character's outfit
                // gets.
                if (character.BustSource != BustSource.Pack && !outfit.PackArt)
                {
                    if (VanillaBusts.FindByGoName(outfit.GameObjectName) == null)
                        issues.Add(new(Severity.Warning, oWhere,
                            $"'{outfit.GameObjectName}' isn't a bust in the 1.8E catalog — the runtime will still look for it under 2_Bust_Manager, but check the name", "outfit.unknownVanillaBust"));

                    var slotsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var replaced in outfit.SpriteOverrides)
                    {
                        string label = SMSModForge.Shared.SpriteSlotNames.Label(replaced.Slot);
                        string rWhere = $"{oWhere}.spriteOverrides[{replaced.Slot}]";

                        if (string.IsNullOrWhiteSpace(replaced.Slot))
                        {
                            issues.Add(new(Severity.Error, rWhere,
                                "A replaced texture has no slot, so nothing will be replaced", "outfit.overrideNoSlot"));
                            continue;
                        }
                        if (!slotsSeen.Add(replaced.Slot))
                            issues.Add(new(Severity.Error, rWhere,
                                $"'{label}' is replaced twice — only one of them will be used", "outfit.overrideDuplicate"));

                        // Ticked and nothing chosen. Kept rather than dropped
                        // on save, because it is an author part-way through
                        // something rather than a mistake in the data — but the
                        // game keeps its own texture until art appears, and
                        // nothing else would say so.
                        if (string.IsNullOrWhiteSpace(replaced.Sprite))
                            issues.Add(new(Severity.Warning, rWhere,
                                $"'{label}' is ticked for replacement with no art chosen — the game keeps its own", "outfit.overrideNoArt"));
                        else
                            CheckFile(packRoot, replaced.Sprite, rWhere, issues);
                    }
                    continue;
                }

                // The collision the runtime cannot resolve: a bust the pack
                // draws, named after one the game already has. Both would be
                // called the same thing under 2_Bust_Manager, and every
                // reference to that name — the game's own included — would
                // reach whichever won.
                if (outfit.PackArt && VanillaBusts.FindByGoName(outfit.GameObjectName) != null)
                    issues.Add(new(Severity.Error, oWhere,
                        $"'{outfit.GameObjectName}' is already one of the game's busts — give this one a name of its own", "outfit.collidesWithVanillaBust"));

                CheckFile(packRoot, outfit.BaseSprite,  $"{oWhere}.baseSprite",  issues);
                CheckOptionalFile(packRoot, outfit.MaskSprite, $"{oWhere}.maskSprite", issues);
                if (outfit.BlinkEnabled)
                    CheckFile(packRoot, outfit.BlinkSprite, $"{oWhere}.blinkSprite", issues);

                if (outfit.Mouth.Enabled)
                {
                    for (int i = 1; i <= 4; i++)
                        CheckFile(packRoot, outfit.Mouth.Prefix + i + ".PNG",
                                  $"{oWhere}.mouth[{i}]", issues);
                }
                if (outfit.Expression.Enabled)
                {
                    // The faces this CHARACTER has, not a fixed four. A face
                    // the author added is loaded from the same prefix as the
                    // rest - {prefix}Smirk.PNG - and before this it was the one
                    // kind of missing art nothing mentioned: the expression
                    // simply did nothing in the game.
                    foreach (var name in FacesOf(character))
                        CheckFile(packRoot, outfit.Expression.Prefix + name + ".PNG",
                                  $"{oWhere}.expression[{name}]", issues);
                }

                // Jiggle sanity bounds. Values outside these still load — we just warn.
                var j = outfit.Jiggle;
                if (j.Strength < -0.5f || j.Strength > 0.5f)
                    issues.Add(new(Severity.Warning, $"{oWhere}.jiggle.strength",
                        $"strength {j.Strength} is outside the usual -0.5..0.5 range", "outfit.jiggleStrengthRange"));
                if (j.NoiseStrength < 0f || j.NoiseStrength > 0.5f)
                    issues.Add(new(Severity.Warning, $"{oWhere}.jiggle.noiseStrength",
                        $"noiseStrength {j.NoiseStrength} is outside the usual 0..0.5 range", "outfit.jiggleNoiseRange"));
            }
        }

        // ── Places ───────────────────────────────────────────────
        var seenPlaceKeys = new HashSet<string>();
        var placeKeysInPack = new HashSet<string>();
        foreach (var p in pack.Places) placeKeysInPack.Add(p.Key);

        // NPC keys placements reference.
        var npcKeysInPack = new HashSet<string>();
        foreach (var n in pack.Npcs)
            if (!string.IsNullOrWhiteSpace(n.Key)) npcKeysInPack.Add(n.Key);

        foreach (var place in pack.Places)
        {
            var pWhere = $"places[{place.Key}]";
            if (string.IsNullOrWhiteSpace(place.Key))
                issues.Add(new(Severity.Error, pWhere, "Place key is required", "place.keyMissing"));
            else if (!seenPlaceKeys.Add(place.Key))
                issues.Add(new(Severity.Error, pWhere, $"Duplicate place key '{place.Key}'", "place.duplicateKey"));

            if (string.IsNullOrWhiteSpace(place.InternalName))
                issues.Add(new(Severity.Warning, $"{pWhere}.internalName",
                    "internalName is empty; loader will fall back to the key", "place.internalNameEmpty"));

            CheckFile(packRoot, place.BaseSprite,      $"{pWhere}.baseSprite",      issues);
            CheckFile(packRoot, place.SecondarySprite, $"{pWhere}.secondarySprite", issues);
            CheckOptionalFile(packRoot, place.MaskSprite, $"{pWhere}.maskSprite",     issues);

            // 1.5 is a real vanilla value (a backdrop that overshoots the room in
            // front of it), so the old 0..1 ceiling flagged legitimate settings.
            CheckParallax(place.ParallaxStrength, $"{pWhere}.parallaxStrength", issues);
            if (place.ParallaxSecondaryStrength.HasValue)
                CheckParallax(place.ParallaxSecondaryStrength.Value, $"{pWhere}.parallaxSecondaryStrength", issues);

            foreach (var btn in place.NavigatorButtons)
                ValidateNavigatorButton(btn, pWhere, placeKeysInPack, issues);

            // NPC placements: reference an existing NPC, and each GameObject
            // name must be unique within this level (two same-named NPCs would
            // collide in the hierarchy and SetGameObjectActive couldn't tell
            // them apart). Placements live anywhere in the place's GameObject
            // tree, so collect them across the whole hierarchy.
            var placements = new List<(NpcPlacementDef Placement, string Path)>();
            CollectPlacements(place.GameObjects, "", placements);
            var seenPlacementNames = new HashSet<string>();
            for (int pi = 0; pi < placements.Count; pi++)
            {
                var pl = placements[pi].Placement;
                var plWhere = $"{pWhere}.{placements[pi].Path}.npcs[{pi}]";
                if (string.IsNullOrWhiteSpace(pl.Npc))
                    issues.Add(new(Severity.Error, plWhere, "NPC placement has no npc key", "place.npcPlacementNoKey"));
                else if (!npcKeysInPack.Contains(pl.Npc))
                    issues.Add(new(Severity.Error, plWhere,
                        $"NPC placement references unknown NPC '{pl.Npc}'", "place.npcPlacementUnknown"));

                string effName = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                if (!string.IsNullOrWhiteSpace(effName) && !seenPlacementNames.Add(effName))
                    issues.Add(new(Severity.Warning, plWhere,
                        $"Two NPC placements resolve to the same GameObject name '{effName}' in this level — " +
                        "give one an explicit Name so they don't collide", "place.npcPlacementNameClash"));
            }
        }

        // ── Vanilla extensions ───────────────────────────────────
        var seenExtensionSources = new HashSet<string>();
        // (see CollectPlacements below for the tree walk placements use)
        for (int i = 0; i < pack.VanillaExtensions.Count; i++)
        {
            var ext = pack.VanillaExtensions[i];
            var eWhere = $"vanillaExtensions[{i}:{ext.Source}]";

            if (!PlaceTargetRef.TryParse(ext.Source, out var sourceRef) ||
                sourceRef.Kind != PlaceTargetKind.Vanilla)
            {
                issues.Add(new(Severity.Error, $"{eWhere}.source",
                    $"Vanilla source '{ext.Source}' is malformed (expected 'vanilla:<goName>')", "ext.sourceMalformed"));
            }
            else
            {
                if (VanillaPlaces.FindByGoName(sourceRef.Key) == null)
                    issues.Add(new(Severity.Warning, $"{eWhere}.source",
                        $"Vanilla level '{sourceRef.Key}' is not in the 1.8E catalog", "ext.unknownVanillaLevel"));
                if (!seenExtensionSources.Add(ext.Source))
                    issues.Add(new(Severity.Warning, $"{eWhere}.source",
                        $"Multiple vanilla extensions target '{ext.Source}' — buttons will pile up on the same nav strip; consider consolidating", "ext.duplicateSource"));
            }

            foreach (var btn in ext.NavigatorButtons)
                ValidateNavigatorButton(btn, eWhere, placeKeysInPack, issues);
        }

        // ── NPCs ──────────────────────────────────────────────────
        var seenNpcKeys = new HashSet<string>();
        for (int i = 0; i < pack.Npcs.Count; i++)
        {
            var npc = pack.Npcs[i];
            var nWhere = $"npcs[{i}:{npc.Key}]";
            if (string.IsNullOrWhiteSpace(npc.Key))
                issues.Add(new(Severity.Error, nWhere, "NPC key is required", "npc.keyMissing"));
            else if (!seenNpcKeys.Add(npc.Key))
                issues.Add(new(Severity.Error, nWhere, $"Duplicate NPC key '{npc.Key}'", "npc.duplicateKey"));

            if (string.IsNullOrWhiteSpace(npc.Sprite))
                issues.Add(new(Severity.Error, $"{nWhere}.sprite", "NPC sprite path is required", "npc.spriteMissing"));
            else
                CheckFile(packRoot, npc.Sprite, $"{nWhere}.sprite", issues);

            if (!string.IsNullOrWhiteSpace(npc.Mask))
                CheckFile(packRoot, npc.Mask, $"{nWhere}.mask", issues);
            if (!string.IsNullOrWhiteSpace(npc.Blink.Sprite))
                CheckFile(packRoot, npc.Blink.Sprite, $"{nWhere}.blink.sprite", issues);

            if (npc.Blink.MaxWait < npc.Blink.MinWait)
                issues.Add(new(Severity.Warning, $"{nWhere}.blink",
                    $"Blink max wait ({npc.Blink.MaxWait}) is below min ({npc.Blink.MinWait})", "npc.blinkWaitRange"));
        }

        // ── Map buttons (World Map radial entries) ────────────────
        for (int i = 0; i < pack.MapButtons.Count; i++)
        {
            var mb = pack.MapButtons[i];
            var mWhere = $"mapButtons[{i}:{mb.District}→{mb.Target}]";

            if (!PlaceTargetRef.TryParse(mb.Target, out var tref))
            {
                issues.Add(new(Severity.Error, $"{mWhere}.target",
                    $"Map button target '{mb.Target}' is malformed " +
                    "(expected 'vanilla:<goName>', 'pack:<packId>.<key>', or 'self:<key>')", "map.targetMalformed"));
            }
            else
            {
                switch (tref.Kind)
                {
                    case PlaceTargetKind.Vanilla:
                        if (VanillaPlaces.FindByGoName(tref.Key) == null)
                            issues.Add(new(Severity.Warning, $"{mWhere}.target",
                                $"Vanilla level '{tref.Key}' is not in the 1.8E catalog", "map.unknownVanillaLevel"));
                        break;
                    case PlaceTargetKind.Self:
                        if (!placeKeysInPack.Contains(tref.Key))
                            issues.Add(new(Severity.Error, $"{mWhere}.target",
                                $"self target '{tref.Key}' has no matching place in this pack", "map.selfTargetMissing"));
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(mb.District))
                issues.Add(new(Severity.Error, $"{mWhere}.district",
                    "Map button district is required", "map.districtMissing"));
            else if (WorldMapDistricts.FindByGoName(mb.District) == null)
                issues.Add(new(Severity.Warning, $"{mWhere}.district",
                    $"District '{mb.District}' is not in the 1.8E World Map catalog (Seaside, TheLine, NeonRow, Shopside, Foundry)", "map.unknownDistrict"));
        }

        // ── Variables ─────────────────────────────────────────────
        var seenVarNames = new HashSet<string>();
        var packVarNames = new HashSet<string>();
        foreach (var v in pack.Variables) packVarNames.Add(v.Name);
        foreach (var v in pack.Variables)
        {
            var vWhere = $"variables[{v.Name}]";
            if (string.IsNullOrWhiteSpace(v.Name))
                issues.Add(new(Severity.Error, vWhere, "Variable name is required", "variable.nameMissing"));
            else if (!seenVarNames.Add(v.Name))
                issues.Add(new(Severity.Error, vWhere, $"Duplicate variable name '{v.Name}'", "variable.duplicateName"));

            // Verify defaultValue parses for the chosen type.
            switch (v.Type)
            {
                case PackVariableType.Bool:
                    if (!bool.TryParse(v.DefaultValue, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Bool default '{v.DefaultValue}' does not parse as true/false; runtime will treat as false", "variable.boolDefault"));
                    break;
                case PackVariableType.Int:
                    if (!int.TryParse(v.DefaultValue, System.Globalization.NumberStyles.Integer,
                                      System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Int default '{v.DefaultValue}' does not parse as an integer; runtime will treat as 0", "variable.intDefault"));
                    break;
                case PackVariableType.Float:
                    if (!float.TryParse(v.DefaultValue, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Warning, $"{vWhere}.defaultValue",
                            $"Float default '{v.DefaultValue}' does not parse as a number; runtime will treat as 0", "variable.floatDefault"));
                    break;
                case PackVariableType.String:
                    // Anything is a valid string.
                    break;
            }
        }

        // ── Actors ────────────────────────────────────────────────
        var seenActorKeys = new HashSet<string>();
        var actorKeysInPack = new HashSet<string>();
        var bustNamesInPack = new HashSet<string>();
        foreach (var c in pack.Characters) actorKeysInPack.Add(c.Key);
        foreach (var ch in pack.Characters)
            foreach (var o in ch.Outfits)
                bustNamesInPack.Add(o.GameObjectName);

        foreach (var a in pack.Characters)
        {
            var aWhere = $"characters[{a.Key}]";
            if (string.IsNullOrWhiteSpace(a.Key))
                issues.Add(new(Severity.Error, aWhere, "Character key is required", "character.keyMissing"));
            else if (!seenActorKeys.Add(a.Key))
                issues.Add(new(Severity.Error, aWhere, $"Duplicate character key '{a.Key}'", "character.duplicateKey"));

            // Having no bust is a declared choice now rather than an omission,
            // so it is only worth reporting when a character claims a source it
            // has not actually filled in.
            string defaultBust = string.IsNullOrWhiteSpace(a.DefaultOutfit)
                ? a.Outfits.FirstOrDefault()?.GameObjectName ?? ""
                : a.DefaultOutfit;

            if (a.BustSource != BustSource.None && a.Outfits.Count == 0)
            {
                issues.Add(new(Severity.Warning, $"{aWhere}.outfits",
                    a.BustSource == BustSource.Vanilla
                        ? "Set to borrow vanilla busts, but none is chosen — nothing will be shown"
                        : "Set to use this pack's own outfits, but has none — add one, or switch the bust source to vanilla or none", "character.noVanillaBustChosen"));
            }
            else if (!string.IsNullOrWhiteSpace(defaultBust) &&
                     !bustNamesInPack.Contains(defaultBust) &&
                     VanillaBusts.FindByGoName(defaultBust) == null)
            {
                issues.Add(new(Severity.Warning, $"{aWhere}.defaultBust",
                    $"Default bust '{defaultBust}' isn't a vanilla bust in the 1.8E catalog and isn't a GameObjectName from any outfit in this pack — runtime will still try GameObject.Find under 2_Bust_Manager, but verify the name", "character.unknownDefaultBust"));
            }

            var seenExprKeys = new HashSet<string>();
            foreach (var e in a.Expressions)
            {
                if (string.IsNullOrWhiteSpace(e.Key))
                    issues.Add(new(Severity.Error, $"{aWhere}.expressions[empty]",
                        "Expression key is required", "character.expressionKeyMissing"));
                else if (!seenExprKeys.Add(e.Key))
                    issues.Add(new(Severity.Error, $"{aWhere}.expressions[{e.Key}]",
                        $"Duplicate expression key '{e.Key}' on actor", "character.duplicateExpressionKey"));
            }
        }

        // ── Dialogues ─────────────────────────────────────────────
        var seenDialogueKeys = new HashSet<string>();
        foreach (var d in pack.Dialogues)
        {
            var dWhere = $"dialogues[{d.Key}]";
            if (string.IsNullOrWhiteSpace(d.Key))
                issues.Add(new(Severity.Error, dWhere, "Dialogue key is required", "dialogue.keyMissing"));
            else if (!seenDialogueKeys.Add(d.Key))
                issues.Add(new(Severity.Error, dWhere, $"Duplicate dialogue key '{d.Key}'", "dialogue.duplicateKey"));

            // Where and when a dialogue starts is only the pack's business
            // when the pack is the one starting it.
            //
            // An extension of one of the game's own conversations is never
            // scheduled by the plugin at all: the room plays it, on the game's
            // own gate, and the pack has no level condition BECAUSE it has no
            // say. Asking one for a level is asking a question with no answer -
            // and it is unanswerable, so the error could not be cleared. Read
            // the gate beside the lines instead; VanillaDialogueSeed.StartGates
            // names the room's level where there is one.
            //
            // The level is what a dialogue is anchored to now — the roomtalk is
            // derived from it and only used for "Prioritize over vanilla", so
            // there is nothing to validate about the roomtalk itself.
            if (d.IsVanillaBased)
            {
                // Nothing here is theirs to get wrong.
            }
            else if (string.IsNullOrWhiteSpace(d.LevelToken))
            {
                issues.Add(new(Severity.Error, $"{dWhere}.startConditions",
                    "Pick a level — the required level condition decides where this dialogue can start", "dialogue.noStartLevel"));
            }
            else if (d.DisableVanillaTrigger && !d.VanillaRoomTalkAvailable)
            {
                // Not an error: the dialogue still works, the checkbox just has
                // nothing to act on, which is worth saying out loud because the
                // author ticked it expecting an effect.
                issues.Add(new(Severity.Warning, $"{dWhere}.disableVanillaTrigger",
                    "'Prioritize this dialogue over vanilla' has no effect here — this level has no vanilla entry dialogue to suppress", "dialogue.prioritizeNoEffect"));
            }

            for (int si = 0; si < d.StartConditions.Count; si++)
                ValidateNodeCondition(d.StartConditions[si], $"{dWhere}.startConditions[{si}]",
                                      packVarNames, issues);

            // Build node-id set + tag set so jumps/children can be cross-checked.
            //
            // Every node goes into the sets, including the game's own: a new
            // line's parent and a new jump's target are usually vanilla ones,
            // and a set built from the author's lines alone would call those
            // missing. Only the COMPLAINT is held back - an id or a tag the
            // game reused is not something a pack can fix.
            var mine = AuthorsOwn(d);
            var nodeIds = new HashSet<int>();
            var tags = new HashSet<string>();
            foreach (var n in d.Nodes)
            {
                bool ours = mine(n);
                if (!nodeIds.Add(n.Id) && ours)
                    issues.Add(new(Severity.Error, $"{dWhere}.nodes[id={n.Id}]",
                        $"Duplicate node id {n.Id}", "dialogue.duplicateNodeId"));
                if (!string.IsNullOrEmpty(n.Tag) && !tags.Add(n.Tag) && ours)
                    issues.Add(new(Severity.Warning, $"{dWhere}.nodes[id={n.Id}].tag",
                        $"Tag '{n.Tag}' is used by multiple nodes — jumps target the first one found", "dialogue.duplicateTag"));
            }

            // Root list references must exist.
            foreach (var rid in d.RootNodeIds)
                if (!nodeIds.Contains(rid))
                    issues.Add(new(Severity.Error, $"{dWhere}.rootNodeIds",
                        $"Root id {rid} doesn't exist among the nodes", "dialogue.rootIdMissing"));
            if (d.Nodes.Count > 0 && d.RootNodeIds.Count == 0)
                issues.Add(new(Severity.Warning, $"{dWhere}.rootNodeIds",
                    "Dialogue has nodes but no root — runtime will pick the first node as root", "dialogue.noRoot"));

            // Which nodes are options on a Choice. A choice child's text is
            // the label on the button the player clicks, so an empty one is a
            // blank button rather than a missing line.
            var choiceChildren = new HashSet<int>();
            foreach (var n in d.Nodes)
                if (n.Kind == DialogueNodeKind.Choice)
                    foreach (var cid in n.Children) choiceChildren.Add(cid);

            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                var nWhere = $"{dWhere}.nodes[id={n.Id}]";

                // An empty line is the one thing that reaches the game looking
                // like a crash rather than a mistake, and nothing here used to
                // catch it. A node carrying only actions is a real pattern
                // though, so silence is fine when it has some.
                if (string.IsNullOrWhiteSpace(n.Text))
                {
                    if (choiceChildren.Contains(n.Id))
                        issues.Add(new(Severity.Error, $"{nWhere}.text",
                            "This node is an option on a Choice, so its text is the button label — an empty one shows the player a blank button", "node.choiceOptionNoText"));
                    else if (n.Kind == DialogueNodeKind.Choice)
                        issues.Add(new(Severity.Warning, $"{nWhere}.text",
                            "A Choice node's text is the prompt shown beneath its options, so an empty one leaves a blank line under them", "node.choicePromptEmpty"));
                    else if (n.Kind == DialogueNodeKind.Text &&
                             n.ActionsOnStart.Count == 0 && n.ActionsOnFinish.Count == 0)
                        issues.Add(new(Severity.Warning, $"{nWhere}.text",
                            "Text node has no line and no actions, so it has nothing to do", "node.emptyTextNode"));
                }

                // Actor + expression sanity.
                //
                // A change to one of the game's own conversations speaks with
                // the game's own cast: Anna and Adrian are not this pack's
                // actors and never will be, and a line the author has not
                // touched would otherwise be reported as a problem for saying
                // what it already said. The conversation's own roles are
                // therefore as good as a declared actor - which is also what
                // lets a NEW line be given to somebody already in the scene.
                if (!string.IsNullOrEmpty(n.Actor)
                    && !actorKeysInPack.Contains(n.Actor)
                    && !VanillaActors(d).Contains(n.Actor))
                    issues.Add(new(Severity.Warning, $"{nWhere}.actor",
                        $"Actor '{n.Actor}' isn't defined in this pack, and isn't in the "
                        + "conversation this extends", "node.unknownActor"));

                // Children must exist.
                foreach (var cid in n.Children)
                    if (!nodeIds.Contains(cid))
                        issues.Add(new(Severity.Error, $"{nWhere}.children",
                            $"Child id {cid} doesn't exist among the nodes", "node.childIdMissing"));

                // Jump tag must exist when the mode is Jump.
                if (n.Jump != null && n.Jump.Mode == JumpMode.Jump)
                {
                    if (string.IsNullOrWhiteSpace(n.Jump.TargetTag))
                        issues.Add(new(Severity.Error, $"{nWhere}.jump",
                            "Jump mode is Jump but targetTag is empty", "node.jumpTagEmpty"));
                    else if (!tags.Contains(n.Jump.TargetTag))
                        issues.Add(new(Severity.Error, $"{nWhere}.jump",
                            $"Jump target tag '{n.Jump.TargetTag}' isn't a tag on any node in this dialogue", "node.jumpTagUnknown"));
                }

                // A node's own conditions are evaluated once, when GC2 reaches
                // the node — unlike the dialogue's start conditions above,
                // which the dispatcher polls every frame.
                // Indexed, so an issue on the third action says the THIRD. Without
                // it every action of the same type on a node produced the same
                // Where, and double-clicking any of them jumped to the first.
                for (int ci = 0; ci < n.Conditions.Count; ci++)
                    ValidateNodeCondition(n.Conditions[ci], $"{nWhere}.conditions[{ci}]",
                                          packVarNames, issues, ConditionContext.OneShot);
                for (int ai = 0; ai < n.ActionsOnStart.Count; ai++)
                    ValidateNodeAction(n.ActionsOnStart[ai], $"{nWhere}.actionsOnStart[{ai}]",
                                       packVarNames, actorKeysInPack, issues);
                for (int ai = 0; ai < n.ActionsOnFinish.Count; ai++)
                    ValidateNodeAction(n.ActionsOnFinish[ai], $"{nWhere}.actionsOnFinish[{ai}]",
                                       packVarNames, actorKeysInPack, issues);
            }
        }

        // Wallpaper unlock conditions — the standard condition list, polled
        // per frame by the runtime's visibility tick.
        foreach (var w in pack.Wallpapers)
            for (int ui = 0; ui < w.UnlockConditions.Count; ui++)
                ValidateNodeCondition(w.UnlockConditions[ui],
                                      $"wallpapers.{w.Key}.unlockConditions[{ui}]",
                                      packVarNames, issues);

        // Integration rules — the IF conditions plus every else-if branch.
        // These carry the Rule context, the one host where a Timer's interval
        // actually restarts.
        foreach (var r in pack.IntegrationRules)
        {
            var rWhere = $"integrationRules.{r.Key}";
            for (int ci = 0; ci < r.Conditions.Count; ci++)
                ValidateNodeCondition(r.Conditions[ci], $"{rWhere}.conditions[{ci}]",
                                      packVarNames, issues, ConditionContext.Rule);
            for (int i = 0; i < r.Branches.Count; i++)
                for (int ci = 0; ci < r.Branches[i].Conditions.Count; ci++)
                    ValidateNodeCondition(r.Branches[i].Conditions[ci],
                                          $"{rWhere}.branches[{i}].conditions[{ci}]", packVarNames,
                                          issues, ConditionContext.Rule);
        }

        CheckLevelTokens(pack, issues);

        return issues;
    }

    // ── Level tokens ─────────────────────────────────────────────────────

    /// <summary>
    /// Params that name a level but aren't declared on a schema: the Set-Active
    /// family's targeting row supplies <c>overlayLevel</c> itself, so a
    /// schema-driven sweep alone would miss it.
    /// </summary>
    private static readonly string[] UnschemadLevelParams = { "overlayLevel" };

    /// <summary>
    /// Resolves every level token in the pack and reports the ones that can't
    /// point at anything.
    /// <para/>
    /// This is the quietest failure the format has. A <c>LevelActive</c> whose
    /// token names no real level simply never passes, so the dialogue never
    /// starts — no error, no warning, nothing in the log, just a scene that
    /// doesn't happen. Real examples cost a playtest each:
    /// <c>vanilla:Downtown</c> for <c>26_Downtown</c>, and <c>vanilla:54_Mall</c>
    /// where the Mall is <c>25_Mall</c>.
    /// <para/>
    /// Driven off <see cref="ParamType.LevelRef"/> rather than a list of param
    /// names, so a new action or condition that takes a level is covered the day
    /// it's added.
    /// <para/>
    /// Deliberately NOT checking <c>roomTalk</c>: it also uses a
    /// <c>vanilla:</c> prefix but names a roomtalk NODE
    /// (<c>vanilla:Mall</c>), not a level GameObject (<c>vanilla:25_Mall</c>).
    /// Both are right in their own field, and validating one against the other's
    /// catalog would flag most of the pack.
    /// </summary>
    private static void CheckLevelTokens(ModPack pack, List<ValidationIssue> issues)
    {
        var goNames = new HashSet<string>(
            VanillaPlaces.All.Select(p => p.GoName), System.StringComparer.OrdinalIgnoreCase);
        if (VanillaLevelCatalog.IsAvailable)
            foreach (var l in VanillaLevelCatalog.All) goNames.Add(l.GoName);
        var placeKeys = new HashSet<string>(
            pack.Places.Select(p => p.Key), System.StringComparer.OrdinalIgnoreCase);

        void CheckToken(string token, string where)
        {
            if (string.IsNullOrWhiteSpace(token)) return;
            int colon = token.IndexOf(':');
            if (colon <= 0) return;                       // not a token; other rules cover shape
            var scheme = token.Substring(0, colon);
            var body = token.Substring(colon + 1);
            if (body.Length == 0 || IsTemplated(body) || body.StartsWith("$")) return;

            if (scheme.Equals("vanilla", System.StringComparison.OrdinalIgnoreCase))
            {
                if (goNames.Contains(body)) return;
                // The usual slip is dropping or mistyping the numeric prefix, so
                // match on the part after it and offer the real name.
                var bare = body.Contains('_') ? body.Substring(body.IndexOf('_') + 1) : body;
                var near = goNames.Where(v =>
                        v.Equals(bare, System.StringComparison.OrdinalIgnoreCase) ||
                        v.EndsWith("_" + bare, System.StringComparison.OrdinalIgnoreCase))
                    .OrderBy(v => v).Take(3).ToList();
                issues.Add(new(Severity.Error, where,
                    $"Level '{token}' doesn't exist — this condition can never pass" +
                    (near.Count > 0 ? $". Did you mean {string.Join(" or ", near.Select(n => "vanilla:" + n))}?" : ""), "level.unknownVanilla"));
            }
            else if (scheme.Equals("place", System.StringComparison.OrdinalIgnoreCase))
            {
                if (!placeKeys.Contains(body))
                    issues.Add(new(Severity.Error, where,
                        $"Level '{token}' names no place in this pack — this condition can never pass", "level.unknownPlace"));
            }
        }

        void CheckParams(System.Collections.Generic.Dictionary<string, string> ps,
                         ParamSchema[] schema, string where)
        {
            if (ps == null) return;
            foreach (var s in schema)
                if (s.Type == ParamType.LevelRef && ps.TryGetValue(s.Key, out var v))
                    CheckToken(v, $"{where}.{s.Key}");
            foreach (var key in UnschemadLevelParams)
                if (ps.TryGetValue(key, out var v)) CheckToken(v, $"{where}.{key}");
        }

        void WalkCondition(NodeConditionDef c, string where)
        {
            if (c == null) return;
            var cw = $"{where}.{c.Type}";
            if (NodeConditionTypes.IsGroup(c.Type))
            {
                if (c.Conditions != null)
                    foreach (var child in c.Conditions) WalkCondition(child, cw);
                return;
            }
            CheckParams(c.Params, ConditionSchemas.For(c.Type), cw);
        }

        void WalkAction(NodeActionDef a, string where)
        {
            if (a == null) return;
            var aw = $"{where}.{a.Type}";
            CheckParams(a.Params, ActionSchemas.For(a.Type), aw);
            if (a.Branches != null)
                for (int i = 0; i < a.Branches.Count; i++)
                    WalkAction(a.Branches[i].Action, $"{aw}.branch[{i}]");
        }

        foreach (var d in pack.Dialogues)
        {
            var dw = $"dialogues.{d.Key}";
            for (int i = 0; i < d.StartConditions.Count; i++)
                WalkCondition(d.StartConditions[i], $"{dw}.startConditions[{i}]");
            var mine = AuthorsOwn(d);
            foreach (var n in d.Nodes)
            {
                if (!mine(n)) continue;
                var nw = $"{dw}.nodes[{n.Id}]";
                for (int i = 0; i < n.Conditions.Count; i++)
                    WalkCondition(n.Conditions[i], $"{nw}.conditions[{i}]");
                for (int i = 0; i < n.ActionsOnStart.Count; i++)
                    WalkAction(n.ActionsOnStart[i], $"{nw}.actionsOnStart[{i}]");
                for (int i = 0; i < n.ActionsOnFinish.Count; i++)
                    WalkAction(n.ActionsOnFinish[i], $"{nw}.actionsOnFinish[{i}]");
            }
        }

        foreach (var r in pack.IntegrationRules)
        {
            var rw = $"integrationRules.{r.Key}";
            for (int i = 0; i < r.Conditions.Count; i++)
                WalkCondition(r.Conditions[i], $"{rw}.conditions[{i}]");
            for (int i = 0; i < r.Actions.Count; i++)
                WalkAction(r.Actions[i], $"{rw}.actions[{i}]");
            for (int i = 0; i < r.Branches.Count; i++)
            {
                for (int k = 0; k < r.Branches[i].Conditions.Count; k++)
                    WalkCondition(r.Branches[i].Conditions[k], $"{rw}.branches[{i}].conditions[{k}]");
                for (int k = 0; k < r.Branches[i].Actions.Count; k++)
                    WalkAction(r.Branches[i].Actions[k], $"{rw}.branches[{i}].actions[{k}]");
            }
        }

        foreach (var w in pack.Wallpapers)
            for (int wi = 0; wi < w.UnlockConditions.Count; wi++)
                WalkCondition(w.UnlockConditions[wi], $"wallpapers.{w.Key}.unlockConditions[{wi}]");
    }

    /// <summary>
    /// Validates one <see cref="NodeConditionDef"/>. Checks the type is
    /// known and that the params look reasonable for it (referenced
    /// variables exist, etc.). Unknown types are errors so a typo can't
    /// silently produce a no-op condition at runtime.
    /// </summary>
    /// <summary>Accepted <c>ListCount</c> comparison labels. Read off the schema
    /// so the validator can't drift from what the editor's dropdown offers.</summary>
    private static readonly string[] ListComparisons =
        ConditionSchemas.For(NodeConditionTypes.ListCount)
                        .First(s => s.Key == "comparison").FixedOptions;

    private static void ValidateNodeCondition(NodeConditionDef c, string whereParent,
        HashSet<string> packVarNames, List<ValidationIssue> issues,
        ConditionContext context = ConditionContext.Polled)
    {
        var cWhere = $"{whereParent}.{c.Type}";
        if (string.IsNullOrEmpty(c.Type))
        {
            issues.Add(new(Severity.Error, cWhere, "Condition type is required", "condition.typeMissing"));
            return;
        }
        // AND/OR groups carry nested children instead of params. They're not in
        // AllRecognized (they're not leaf types, and the Type combo excludes
        // them), so check them before the unknown-type guard and recurse —
        // otherwise a perfectly valid group reads as an unknown type, and its
        // children escape validation entirely.
        if (NodeConditionTypes.IsGroup(c.Type))
        {
            if (c.Conditions != null)
                foreach (var child in c.Conditions)
                    ValidateNodeCondition(child, cWhere, packVarNames, issues, context);
            return;
        }
        if (System.Array.IndexOf(NodeConditionTypes.AllRecognized, c.Type) < 0)
        {
            issues.Add(new(Severity.Error, cWhere, $"Unknown condition type '{c.Type}'", "condition.unknownType"));
            return;
        }

        switch (c.Type)
        {
            case NodeConditionTypes.VariableEquals:
            case NodeConditionTypes.VariableGreaterThan:
            case NodeConditionTypes.VariableLessThan:
            case NodeConditionTypes.VariableGreaterOrEqual:
            case NodeConditionTypes.VariableLessOrEqual:
            case NodeConditionTypes.VariableExists:
            {
                bool vanilla = c.Params.TryGetValue("source", out var src) &&
                               string.Equals(src, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!c.Params.TryGetValue("name", out var n) || string.IsNullOrWhiteSpace(n))
                    issues.Add(new(Severity.Error, cWhere, "Param 'name' is required", "condition.paramNameMissing"));
                else if (vanilla)
                {
                    if (!VanillaGameVariables.Contains(n))
                        issues.Add(new(Severity.Warning, cWhere,
                            $"Vanilla variable '{n}' isn't in the 1.8E catalog", "condition.unknownVanillaVariable"));
                }
                else if (!IsTemplated(n) && !packVarNames.Contains(n))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{n}' isn't declared in this pack — condition will read the default", "condition.undeclaredVariable"));
                if (c.Type != NodeConditionTypes.VariableExists &&
                    (!c.Params.ContainsKey("value") || string.IsNullOrWhiteSpace(c.Params["value"])))
                    issues.Add(new(Severity.Warning, cWhere, "Param 'value' is required for this condition", "condition.paramValueMissing"));
                break;
            }
            case NodeConditionTypes.GameVariableEquals:
                if (!c.Params.ContainsKey("name")) issues.Add(new(Severity.Error, cWhere, "Param 'name' is required", "condition.paramNameMissing"));
                if (!c.Params.ContainsKey("value")) issues.Add(new(Severity.Warning, cWhere, "Param 'value' is required", "condition.paramValueMissing"));
                break;
            case NodeConditionTypes.LevelActive:
                if (!c.Params.ContainsKey("level")) issues.Add(new(Severity.Error, cWhere, "Param 'level' is required", "condition.paramLevelMissing"));
                break;
            case NodeConditionTypes.InputKey:
            {
                if (!c.Params.TryGetValue("key", out var ikey) || string.IsNullOrWhiteSpace(ikey))
                    issues.Add(new(Severity.Error, cWhere, "Param 'key' is required", "input.keyMissing"));
                else if (!InputKeys.IsKnown(ikey))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"'{ikey}' is not one of the keys the picker offers. The runtime " +
                        "resolves any Unity KeyCode name, so a hand-written one may still " +
                        "work — but a misspelling never matches anything and never says so.",
                        "input.unknownKey"));

                string phase = c.Params.TryGetValue("phase", out var ph) ? ph : "";
                if (phase.Length > 0 && !InputPhases.All.Contains(phase))
                    issues.Add(new(Severity.Error, cWhere,
                        $"'{phase}' is not one of Pressed, Down, Released or Up.", "input.badPhase"));
                // An edge on a node's own conditions is this condition's one real
                // trap: GC2 checks those once, when it reaches the node, so "was it
                // pressed in that exact instant" is a coin flip the author will
                // read as the condition being broken.
                else if (context == ConditionContext.OneShot && InputPhases.IsEdge(phase))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"{phase} is true for one moment, but a node's own conditions are " +
                        "checked once, when the conversation reaches the node. The two will " +
                        "almost never line up. Use Down or Up here, or move the check to an " +
                        "integration rule, which is re-tested every frame.",
                        "input.edgeInOneShot"));
                break;
            }
            case NodeConditionTypes.GameObjectActive:
                // 'target' is canonical. 'path' is the spelling from before the
                // category row and the runtime still reads it, so a pack that
                // predates the change is complete as it stands.
                if (!c.Params.ContainsKey("target") && !c.Params.ContainsKey("path"))
                    issues.Add(new(Severity.Error, cWhere, "Param 'target' is required", "condition.paramTargetMissing"));
                break;
            case NodeConditionTypes.VariableStartsWith:
            {
                bool vanillaPfx = c.Params.TryGetValue("source", out var psrc) &&
                                  string.Equals(psrc, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!c.Params.TryGetValue("name", out var pn) || string.IsNullOrWhiteSpace(pn))
                    issues.Add(new(Severity.Error, cWhere, "Param 'name' is required", "condition.paramNameMissing"));
                else if (vanillaPfx)
                {
                    if (!VanillaGameVariables.Contains(pn))
                        issues.Add(new(Severity.Warning, cWhere,
                            $"Vanilla variable '{pn}' isn't in the 1.8E catalog", "condition.unknownVanillaVariable"));
                }
                else if (!IsTemplated(pn) && !packVarNames.Contains(pn))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{pn}' isn't declared in this pack — condition will read the default", "condition.undeclaredVariable"));

                // An empty prefix would match everything, so the runtime refuses
                // it; that's almost always a half-filled row rather than intent.
                if (!c.Params.TryGetValue("value", out var pv) || string.IsNullOrEmpty(pv))
                    issues.Add(new(Severity.Warning, cWhere,
                        "Param 'value' (the prefix) is empty — this condition never passes", "condition.emptyPrefix"));
                break;
            }
            case NodeConditionTypes.ListContains:
            case NodeConditionTypes.ListCount:
            {
                if (!c.Params.TryGetValue("list", out var ln) || string.IsNullOrWhiteSpace(ln))
                    issues.Add(new(Severity.Error, cWhere, "Param 'list' is required", "condition.paramListMissing"));
                else if (!IsTemplated(ln) && !packVarNames.Contains(ln))
                    issues.Add(new(Severity.Warning, cWhere,
                        $"Variable '{ln}' isn't declared in this pack — the condition will read an empty list", "condition.undeclaredListVariable"));

                if (c.Type == NodeConditionTypes.ListContains)
                {
                    if (!c.Params.TryGetValue("value", out var lv) || string.IsNullOrEmpty(lv))
                        issues.Add(new(Severity.Warning, cWhere,
                            "Param 'value' is empty — this will never match an entry", "condition.emptyListValue"));
                }
                else
                {
                    if (!c.Params.TryGetValue("value", out var cv) ||
                        !float.TryParse(cv, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out _))
                        issues.Add(new(Severity.Error, cWhere, "Param 'value' must be a number", "condition.valueNotNumber"));

                    // Unknown comparison silently falls back to "equals" at
                    // runtime, which is a quiet wrong answer — flag the typo.
                    if (c.Params.TryGetValue("comparison", out var cmp) && !string.IsNullOrEmpty(cmp) &&
                        System.Array.IndexOf(ListComparisons, cmp) < 0)
                        issues.Add(new(Severity.Error, cWhere,
                            $"Unknown comparison '{cmp}' — expected one of: {string.Join(", ", ListComparisons)}", "condition.unknownComparison"));
                }
                break;
            }
            case NodeConditionTypes.Timer:
            {
                // Only an integration rule has a "fired" event to restart the
                // interval on. Anywhere else the timer elapses once and then
                // stays permanently true — silently a no-op, so flag it.
                if (context != ConditionContext.Rule)
                    issues.Add(new(Severity.Warning, cWhere,
                        "'Timer' only restarts when an integration rule fires. In this " +
                        "host there's no fire event, so it elapses once and then passes " +
                        "forever. Move it to an integration rule.", "condition.timerOutsideRule"));

                bool randomized = c.Params.TryGetValue("randomize", out var rz) &&
                                  string.Equals(rz, "true", System.StringComparison.OrdinalIgnoreCase);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var num = System.Globalization.NumberStyles.Float;

                if (randomized)
                {
                    bool okMin = c.Params.TryGetValue("minSeconds", out var mn) &&
                                 float.TryParse(mn, num, inv, out var mnV) && mnV >= 0;
                    bool okMax = c.Params.TryGetValue("maxSeconds", out var mx) &&
                                 float.TryParse(mx, num, inv, out var mxV) && mxV >= 0;
                    if (!okMin) issues.Add(new(Severity.Error, cWhere,
                        "Param 'minSeconds' must be a number >= 0 when Randomize is on", "condition.timerMinNotNumber"));
                    if (!okMax) issues.Add(new(Severity.Error, cWhere,
                        "Param 'maxSeconds' must be a number >= 0 when Randomize is on", "condition.timerMaxNotNumber"));
                    if (okMin && okMax &&
                        float.TryParse(c.Params["minSeconds"], num, inv, out var a) &&
                        float.TryParse(c.Params["maxSeconds"], num, inv, out var b) && b < a)
                        issues.Add(new(Severity.Warning, cWhere,
                            $"'maxSeconds' ({b}) is below 'minSeconds' ({a}) — the runtime " +
                            "swaps them, but the range is probably backwards.", "condition.timerMaxBelowMin"));
                }
                else if (!c.Params.TryGetValue("seconds", out var sec) ||
                         !float.TryParse(sec, num, inv, out var secV) || secV < 0)
                {
                    issues.Add(new(Severity.Error, cWhere, "Param 'seconds' must be a number >= 0", "condition.timerSecondsNotNumber"));
                }
                break;
            }
            case NodeConditionTypes.Random:
                // Fine in a one-shot host (node conditions, level hooks) — it's
                // rolled exactly once there. In a polled host it re-rolls every
                // frame, so the authored chance is meaningless: flag those.
                if (context == ConditionContext.Polled)
                    issues.Add(new(Severity.Warning, cWhere,
                        "'Random' re-rolls on every evaluation, and this condition is " +
                        "re-checked every frame (~60×/sec), so its chance doesn't mean what " +
                        "it says. Replace with 'DailyChance' (rolls once per in-game day), " +
                        "or a LevelRandom variable + a numeric comparison (once per visit). " +
                        "'Random' is fine on a dialogue NODE's conditions or a level hook, " +
                        "which are evaluated once.", "condition.randomPerFrame"));
                if (!c.Params.TryGetValue("chance", out var ch) ||
                    !float.TryParse(ch, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var chV) ||
                    chV < 0f || chV > 1f)
                    issues.Add(new(Severity.Warning, cWhere, "Param 'chance' should be a float in [0,1]", "condition.chanceRange"));
                break;
            case NodeConditionTypes.DailyChance:
                if (!c.Params.TryGetValue("chance", out var dch) ||
                    !float.TryParse(dch, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var dchV) ||
                    dchV < 0f || dchV > 100f)
                    issues.Add(new(Severity.Warning, cWhere,
                        "Param 'chance' should be a whole percentage in [0,100]", "condition.dailyChanceRange"));
                break;
        }
    }

    /// <summary>Validates one <see cref="NodeActionDef"/>. Mirror of <see cref="ValidateNodeCondition"/>.</summary>
    private static void ValidateNodeAction(NodeActionDef a, string whereParent,
        HashSet<string> packVarNames, HashSet<string> actorKeysInPack, List<ValidationIssue> issues)
    {
        var aWhere = $"{whereParent}.{a.Type}";
        if (string.IsNullOrEmpty(a.Type))
        {
            issues.Add(new(Severity.Error, aWhere, "Action type is required", "action.typeMissing"));
            return;
        }
        // A vanilla step is known but not offered: it is placed by seeding a
        // vanilla extension, never by an author, so it is missing from All on
        // purpose and must not read as a typo here.
        if (a.Type == NodeActionTypes.VanillaStep) return;

        if (System.Array.IndexOf(NodeActionTypes.All, a.Type) < 0)
        {
            issues.Add(new(Severity.Error, aWhere, $"Unknown action type '{a.Type}'", "action.unknownType"));
            return;
        }

        switch (a.Type)
        {
            case NodeActionTypes.DiceRoll:
                {
                    if (a.Branches == null || a.Branches.Count < 2)
                    {
                        issues.Add(new(Severity.Error, aWhere,
                            "DiceRoll needs at least 2 branches", "action.diceTooFewBranches"));
                        break;
                    }
                    int total = 0;
                    for (int i = 0; i < a.Branches.Count; i++)
                    {
                        var b = a.Branches[i];
                        if (b.Chance < 1)
                            issues.Add(new(Severity.Error, aWhere,
                                $"Branch {i + 1} chance must be at least 1%", "action.diceBranchChanceLow"));
                        total += b.Chance;
                        if (b.Action == null)
                            issues.Add(new(Severity.Error, aWhere,
                                $"Branch {i + 1} has no action", "action.diceBranchNoAction"));
                        else
                            // 0-based, like actionsOnStart[i] and the rest. The
                            // message above stays 1-based because that one is read
                            // by a person.
                            ValidateNodeAction(b.Action, $"{aWhere}.branch[{i}]",
                                               packVarNames, actorKeysInPack, issues);
                    }
                    if (total != 100)
                        issues.Add(new(Severity.Error, aWhere,
                            $"Branch chances sum to {total}% — must be exactly 100%", "action.diceChancesWrongTotal"));
                    break;
                }
            case NodeActionTypes.SetVariable:
            case NodeActionTypes.IncrementVariable:
                bool varVanilla = a.Params.TryGetValue("source", out var aSrc) &&
                                  string.Equals(aSrc, "vanilla", System.StringComparison.OrdinalIgnoreCase);
                if (!a.Params.TryGetValue("name", out var n) || string.IsNullOrWhiteSpace(n))
                    issues.Add(new(Severity.Error, aWhere, "Param 'name' is required", "action.paramNameMissing"));
                else if (varVanilla)
                {
                    if (!VanillaGameVariables.Contains(n))
                        issues.Add(new(Severity.Warning, aWhere,
                            $"Vanilla variable '{n}' isn't in the 1.8E catalog", "action.unknownVanillaVariable"));
                }
                else if (!IsTemplated(n) && !packVarNames.Contains(n))
                    issues.Add(new(Severity.Warning, aWhere,
                        $"Variable '{n}' isn't declared in this pack", "action.undeclaredVariable"));
                // An explicit empty value is legitimate — it clears the variable.
                // Only a wholly ABSENT key reads as an unfilled row.
                if (a.Type == NodeActionTypes.SetVariable && !a.Params.ContainsKey("value"))
                    issues.Add(new(Severity.Warning, aWhere,
                        "Param 'value' is required — leave the field blank to clear the " +
                        "variable and it will be stored as an explicit empty value", "action.paramValueMissing"));
                if (a.Type == NodeActionTypes.IncrementVariable && !a.Params.ContainsKey("delta"))
                    issues.Add(new(Severity.Warning, aWhere, "Param 'delta' is required", "action.paramDeltaMissing"));
                break;
            case NodeActionTypes.LeaveBust:
                if (!a.Params.TryGetValue("actor", out var act) || string.IsNullOrWhiteSpace(act))
                    issues.Add(new(Severity.Error, aWhere, "Param 'actor' is required", "action.paramActorMissing"));
                else if (!actorKeysInPack.Contains(act))
                    issues.Add(new(Severity.Warning, aWhere,
                        $"Actor '{act}' isn't defined in this pack", "action.unknownActor"));
                break;
            case NodeActionTypes.SetGameObjectActive:
                // Unified Set-Active uses 'target' (+ 'kind'); 'path' is the
                // legacy param, still accepted for pre-unify packs.
                if (!a.Params.ContainsKey("target") && !a.Params.ContainsKey("path"))
                    issues.Add(new(Severity.Error, aWhere, "Param 'target' is required", "action.paramTargetMissing"));
                if (!a.Params.ContainsKey("active")) issues.Add(new(Severity.Warning, aWhere, "Param 'active' (true/false) is required", "action.paramActiveMissing"));
                break;
            case NodeActionTypes.EmitSignal:
                if (!a.Params.ContainsKey("signal")) issues.Add(new(Severity.Error, aWhere, "Param 'signal' is required", "action.paramSignalMissing"));
                break;
            case NodeActionTypes.SwitchMusic:
                if (!a.Params.ContainsKey("music")) issues.Add(new(Severity.Error, aWhere, "Param 'music' is required", "action.paramMusicMissing"));
                break;
            case NodeActionTypes.Wait:
                if (!a.Params.TryGetValue("seconds", out var s) ||
                    !float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var sv) ||
                    sv < 0f)
                    issues.Add(new(Severity.Error, aWhere, "Param 'seconds' must be a non-negative float", "action.secondsNotNumber"));
                break;
        }
    }

    /// <summary>
    /// True when a name carries a <c>{placeholder}</c> — a parameterized rule's
    /// per-value substitution (see <see cref="UpdateRuleDef.ForEach"/>). The real
    /// name only exists at runtime, so "is this variable declared?" can't be
    /// answered here and the check is skipped rather than reported as a miss.
    /// </summary>
    private static bool IsTemplated(string name)
        => !string.IsNullOrEmpty(name) && name.IndexOf('{') >= 0 && name.IndexOf('}') > name.IndexOf('{');

    /// <summary>
    /// Every NPC placement in a GameObject tree, paired with the slash path of
    /// the node it hangs under — placements live wherever they're nested, so
    /// per-level checks (unknown NPC key, colliding names) walk the hierarchy.
    /// </summary>
    private static void CollectPlacements(List<GameObjectDef> nodes, string prefix,
        List<(NpcPlacementDef Placement, string Path)> into)
    {
        if (nodes == null) return;
        foreach (var n in nodes)
        {
            string name = string.IsNullOrWhiteSpace(n.Name) ? "(unnamed)" : n.Name;
            string path = string.IsNullOrEmpty(prefix) ? name : prefix + "/" + name;
            foreach (var pl in n.Npcs)
            {
                into.Add((pl, path));
                // An NPC's own children are GameObjects that may host NPCs too.
                string plName = string.IsNullOrWhiteSpace(pl.Name) ? pl.Npc : pl.Name;
                CollectPlacements(pl.Children, path + "/" + plName, into);
            }
            CollectPlacements(n.Children, path, into);
        }
    }

    /// <summary>
    /// Shared check for a single <see cref="NavigatorButtonDef"/> target. The
    /// per-place and per-vanilla-extension paths in <see cref="Validate"/>
    /// both go through this method so the rules stay in sync.
    /// </summary>
    private static void ValidateNavigatorButton(NavigatorButtonDef btn, string whereParent,
        HashSet<string> placeKeysInPack, List<ValidationIssue> issues)
    {
        var bWhere = $"{whereParent}.navigatorButtons[→{btn.Target}]";

        if (!PlaceTargetRef.TryParse(btn.Target, out var tref))
        {
            issues.Add(new(Severity.Error, bWhere,
                $"Navigator target '{btn.Target}' is malformed " +
                "(expected 'vanilla:<goName>', 'pack:<packId>.<key>', or 'self:<key>')", "nav.targetMalformed"));
            return;
        }

        switch (tref.Kind)
        {
            case PlaceTargetKind.Vanilla:
                if (VanillaPlaces.FindByGoName(tref.Key) == null)
                    issues.Add(new(Severity.Warning, bWhere,
                        $"Vanilla level '{tref.Key}' is not in the 1.8E catalog", "nav.unknownVanillaLevel"));
                break;
            case PlaceTargetKind.Self:
                if (!placeKeysInPack.Contains(tref.Key))
                    issues.Add(new(Severity.Error, bWhere,
                        $"self target '{tref.Key}' has no matching place in this pack", "nav.selfTargetMissing"));
                break;
            case PlaceTargetKind.Pack:
                // We can't verify a cross-pack reference at author time;
                // surface it as info so the user knows the dependency is implicit.
                issues.Add(new(Severity.Info, bWhere,
                    $"Cross-pack target '{tref.PackId}.{tref.Key}' — relies on the other pack being installed", "nav.crossPackTarget"));
                break;
        }
    }

    /// <summary>Flag a parallax strength outside the range the game itself uses.
    /// Vanilla's 221 ParallaxMouseEffect instances span 0 to 1.5, so that is the
    /// band — anything past it is not wrong, just far outside anything shipped.</summary>
    private static void CheckParallax(float strength, string where, List<ValidationIssue> issues)
    {
        if (strength < 0f || strength > 1.5f)
            issues.Add(new(Severity.Warning, where,
                $"parallax strength {strength} is outside the 0..1.5 range vanilla levels use", "place.parallaxRange"));
    }

    /// <summary>
    /// Like <see cref="CheckFile"/>, but an empty path is a choice rather than
    /// an omission — used for the mask fields, where blank means "this sprite
    /// does not jiggle" and the runtime binds a fully transparent mask. A path
    /// that IS set still has to resolve, because that is a typo either way.
    /// </summary>
    private static void CheckOptionalFile(string packRoot, string relPath, string where, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(relPath)) return;
        CheckFile(packRoot, relPath, where, issues);
    }

    /// <summary>
    /// The faces one character's busts carry art for: the four every bust is
    /// built with, plus any the pack declared of its own.
    /// <para/>
    /// Kept in step with what the runtime loads (see <c>BustFactory.FacesOf</c>)
    /// and with what the editor shows under the prefix box. An entry mapping to
    /// an empty name is not a face - that is how every pack spells
    /// <c>neutral</c>, which means no face at all.
    /// </summary>
    private static IEnumerable<string> FacesOf(CharacterDef character)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in ExpressionSpec.Names)
            if (seen.Add(name)) yield return name;

        foreach (var e in character.Expressions)
        {
            // An empty child name is not a face. That is how every pack spells
            // <c>neutral</c> - it means no expression showing, the bust's own
            // face - and reading it as one had the validator asking every pack
            // character for a neutral.png that was never supposed to exist.
            if (string.IsNullOrEmpty(e.ExpressionGoName)) continue;
            if (seen.Add(e.ExpressionGoName)) yield return e.ExpressionGoName;
        }
    }

    private static void CheckFile(string packRoot, string relPath, string where, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(relPath))
        {
            issues.Add(new(Severity.Warning, where, "Empty path", "art.emptyPath"));
            return;
        }
        // A pack that has never been saved has no folder to resolve against, so
        // there is nothing to check the file against yet. Previously this threw
        // out of Path.Combine and took the whole validation pass with it —
        // reachable simply by typing a sprite path before the first save.
        if (string.IsNullOrWhiteSpace(packRoot)) return;

        var abs = Path.Combine(packRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs))
            issues.Add(new(Severity.Warning, where, $"File not found: {abs}", "art.fileNotFound"));
    }
}
