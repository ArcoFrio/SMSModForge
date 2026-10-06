using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Localization;

namespace SMSModForge.Services;

/// <summary>What kind of thing a name names.</summary>
public enum RefKind
{
    /// <summary>A character's key — what a dialogue node's speaker field
    /// holds.</summary>
    Character,

    /// <summary>An outfit's GameObject name — what a node switches a character
    /// into, and what actions target a bust by.</summary>
    Outfit,

    /// <summary>An expression key.</summary>
    Expression,

    Scene,
    Music,
    Sfx,

    /// <summary>A place key — what navigator and map buttons travel to.</summary>
    Place,

    /// <summary>A dialogue key — what starts one.</summary>
    Dialogue,

    /// <summary>An NPC key.</summary>
    Npc,

    /// <summary>A pack quest's key - what the Quest action and the quest
    /// conditions name it by.</summary>
    Quest,
}

/// <summary>
/// Renaming as a refactor rather than a silent break.
/// <para/>
/// A pack is a web of names: a dialogue node holds a character key, an action
/// holds a bust name, a navigator button holds a place key. Renaming any of
/// them used to change the declaration and leave every reference pointing at
/// something that no longer exists — with nothing to say so until the pack ran
/// in the game and a character stopped speaking. Only variables were ever
/// followed (see <see cref="VariableRenamer"/>); everything else was on the
/// author to hunt down by eye.
/// <para/>
/// Typed params are found through the SCHEMAS rather than a hand-listed set of
/// action types, which is the same bargain the variable renamer strikes and for
/// the same reason: an action that takes a bust name is covered the day
/// somebody adds it, without anybody remembering to come back here.
/// <para/>
/// The structural references — a node's speaker, a character's default outfit —
/// are not params and are listed out below, per kind. That list is the part
/// that can go stale, so it is short and each entry says what it is.
/// </summary>
public static class ReferenceRenamer
{
    /// <summary>
    /// The param types that mean each kind. A param typed
    /// <see cref="ParamType.ActorRef"/> holds a character key, so renaming a
    /// character has to rewrite it.
    /// </summary>
    private static readonly Dictionary<RefKind, ParamType[]> Typed = new()
    {
        [RefKind.Character] = new[] { ParamType.ActorRef },
        [RefKind.Outfit] = new[] { ParamType.BustRef },
        [RefKind.Expression] = new[] { ParamType.ExpressionRef },
        [RefKind.Scene] = new[] { ParamType.SceneRef },
        [RefKind.Music] = new[] { ParamType.MusicRef },
        [RefKind.Sfx] = new[] { ParamType.SfxRef },
        [RefKind.Place] = Array.Empty<ParamType>(),
        [RefKind.Dialogue] = Array.Empty<ParamType>(),
        [RefKind.Npc] = Array.Empty<ParamType>(),
        [RefKind.Quest] = Array.Empty<ParamType>(),
    };

    /// <summary>
    /// Point every reference to <paramref name="oldName"/> at
    /// <paramref name="newName"/>. Returns how many were rewritten, which is
    /// what the caller tells the author.
    /// <para/>
    /// The DECLARATION is the caller's to change, and must be changed after
    /// this runs: while it still reads the old name, the old name is what the
    /// pack refers to.
    /// </summary>
    public static int Rename(ModPack? pack, RefKind kind, string oldName, string newName)
    {
        if (pack == null || string.IsNullOrEmpty(oldName) || oldName == newName) return 0;

        int n = 0;
        foreach (var (condition, _) in PackWalk.Conditions(pack))
        {
            n += RenameParams(condition.Params, ConditionSchemas.For(condition.Type), kind, oldName, newName);
            n += RenameTargets(condition.Params, ConditionSchemas.For(condition.Type), kind, oldName, newName, rewrite: true);
        }

        foreach (var (action, _) in PackWalk.Actions(pack))
        {
            n += RenameParams(action.Params, ActionSchemas.For(action.Type), kind, oldName, newName);
            n += RenameTargets(action.Params, ActionSchemas.For(action.Type), kind, oldName, newName, rewrite: true);
        }

        // An NPC placed without a name of its own is named after the NPC, so
        // a row aimed at it names the NPC's key. Done before the placements
        // themselves move on, while they still say which NPC they are.
        if (kind == RefKind.Npc)
            foreach (var (level, placement) in Placements(pack))
                if (placement.Npc == oldName && string.IsNullOrWhiteSpace(placement.Name))
                    n += RenameLevelObject(pack, placement, oldName, newName);

        n += RenameStructural(pack, kind, oldName, newName, rewrite: true);
        return n;
    }

    /// <summary>
    /// Point every row aimed at one expression of one character at the
    /// expression's new key.
    /// <para/>
    /// An expression key is only unique within its character - everybody has a
    /// Happy - so the character is part of the match: a line Sarah speaks
    /// asking for "Smirk" is about Sarah's Smirk, and Anna's lines are left
    /// alone.
    /// </summary>
    public static int RenameExpression(ModPack? pack, string characterKey, string oldKey, string newKey)
    {
        if (pack == null || string.IsNullOrEmpty(oldKey) || oldKey == newKey) return 0;

        int n = 0;
        foreach (var d in pack.Dialogues)
            foreach (var node in d.Nodes)
                if (string.Equals(node.Actor, characterKey, StringComparison.OrdinalIgnoreCase)
                    && node.Expression == oldKey)
                {
                    node.Expression = newKey;
                    n++;
                }

        // A row that names both - an actor param and an expression param.
        void Row(Dictionary<string, string>? ps, IEnumerable<ParamSchema> schemas)
        {
            if (ps == null) return;
            var all = schemas.ToList();
            var actor = all.FirstOrDefault(s => s.Type == ParamType.ActorRef);
            if (actor == null || !ps.TryGetValue(actor.Key, out var who)
                || !string.Equals(who, characterKey, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var s in all.Where(s => s.Type == ParamType.ExpressionRef))
                if (ps.TryGetValue(s.Key, out var face) && face == oldKey)
                {
                    ps[s.Key] = newKey;
                    n++;
                }
        }
        foreach (var (c, _) in PackWalk.Conditions(pack)) Row(c.Params, ConditionSchemas.For(c.Type));
        foreach (var (a, _) in PackWalk.Actions(pack)) Row(a.Params, ActionSchemas.For(a.Type));
        return n;
    }

    // ── Objects inside a level ────────────────────────────────────────────

    /// <summary>
    /// Point every row aimed at one object in a level - one of a place's
    /// GameObjects, or an NPC placed there - at the object's new name.
    /// <para/>
    /// Those rows name it by a path (<c>NPCs &gt; Shower &gt; Anis</c>) or a bare
    /// name, inside the level their Level field picks. Each is followed the way
    /// the game follows it, and only a path that leads THROUGH this object
    /// changes - another object of the same name elsewhere in the level is a
    /// different object, and so is one in another level.
    /// <para/>
    /// <paramref name="holder"/> is the <see cref="GameObjectDef"/> or
    /// <see cref="NpcPlacementDef"/> renamed; the model may already carry the
    /// new name, since the lookup reads it as <paramref name="oldName"/>.
    /// </summary>
    public static int RenameLevelObject(ModPack? pack, object holder, string oldName, string newName)
    {
        if (pack == null || holder == null || string.IsNullOrEmpty(oldName) || oldName == newName) return 0;

        int n = 0;
        foreach (var (token, roots) in Levels(pack))
        {
            var tree = LevelTree(roots, holder, oldName);
            if (!Contains(tree, holder)) continue;

            void Row(Dictionary<string, string>? ps)
            {
                if (ps == null) return;
                string category = Category(ps);
                if (category != "GameObjects" && category != "NPCs") return;
                ps.TryGetValue("overlayLevel", out var level);
                // A row naming no level is looked up wherever the object is,
                // which includes here.
                if (!string.IsNullOrEmpty(level) && !string.Equals(level, token, StringComparison.OrdinalIgnoreCase))
                    return;
                if (!ps.TryGetValue("target", out var target) || string.IsNullOrEmpty(target)) return;

                var (segments, separators) = SplitPath(target);
                var chain = Resolve(tree, segments);
                if (chain == null) return;
                int at = chain.FindIndex(link => ReferenceEquals(link.Object.Holder, holder));
                if (at < 0) return;
                segments[chain[at].Segment] = newName;
                ps["target"] = JoinPath(segments, separators);
                n++;
            }

            foreach (var (c, _) in PackWalk.Conditions(pack)) Row(c.Params);
            foreach (var (a, _) in PackWalk.Actions(pack)) Row(a.Params);
        }
        return n;
    }

    /// <summary>One object in a level as the game names it, and what is
    /// parented under it.</summary>
    private sealed class LevelObject
    {
        public object Holder = null!;
        public string Name = "";
        public List<LevelObject> Children = new();
    }

    /// <summary>Every level the pack fills with objects, by the token a row's
    /// Level field holds.</summary>
    private static IEnumerable<(string Token, List<GameObjectDef> Roots)> Levels(ModPack pack)
    {
        foreach (var p in pack.Places) yield return ("place:" + p.Key, p.GameObjects);
        foreach (var v in pack.VanillaExtensions) yield return (v.Source, v.GameObjects);
    }

    /// <summary>Every NPC placement, with the level it is in.</summary>
    private static IEnumerable<(string Token, NpcPlacementDef Placement)> Placements(ModPack pack)
    {
        foreach (var (token, roots) in Levels(pack))
            foreach (var placement in PlacementsIn(roots))
                yield return (token, placement);
    }

    private static IEnumerable<NpcPlacementDef> PlacementsIn(IEnumerable<GameObjectDef>? nodes)
    {
        if (nodes == null) yield break;
        foreach (var node in nodes)
        {
            foreach (var placement in node.Npcs)
            {
                yield return placement;
                foreach (var inner in PlacementsIn(placement.Children)) yield return inner;
            }
            foreach (var inner in PlacementsIn(node.Children)) yield return inner;
        }
    }

    /// <summary>A level's objects as the game has them: each node with its
    /// children, then the NPCs placed under it - named by the placement, or
    /// after the NPC when it has no name of its own.</summary>
    private static List<LevelObject> LevelTree(IEnumerable<GameObjectDef>? nodes, object renamed, string oldName)
    {
        var list = new List<LevelObject>();
        if (nodes == null) return list;
        foreach (var node in nodes)
        {
            var o = new LevelObject
            {
                Holder = node,
                Name = ReferenceEquals(node, renamed) ? oldName : node.Name ?? "",
            };
            o.Children.AddRange(LevelTree(node.Children, renamed, oldName));
            foreach (var placement in node.Npcs)
            {
                string own = string.IsNullOrWhiteSpace(placement.Name) ? placement.Npc : placement.Name;
                var p = new LevelObject
                {
                    Holder = placement,
                    Name = ReferenceEquals(placement, renamed) ? oldName : own ?? "",
                };
                p.Children.AddRange(LevelTree(placement.Children, renamed, oldName));
                o.Children.Add(p);
            }
            list.Add(o);
        }
        return list;
    }

    private static bool Contains(IEnumerable<LevelObject> tree, object holder)
        => PreOrder(tree).Any(o => ReferenceEquals(o.Holder, holder));

    private static IEnumerable<LevelObject> PreOrder(IEnumerable<LevelObject> tree)
    {
        foreach (var o in tree)
        {
            yield return o;
            foreach (var c in PreOrder(o.Children)) yield return c;
        }
    }

    /// <summary>
    /// What a target leads to, the way the game looks it up
    /// (<c>TransformExtensions.FindDescendantIncludingInactive</c>): its first
    /// name anywhere in the level, then each further name as a child of the
    /// last. Case for case. Null when it leads nowhere; otherwise each object
    /// on the way with the segment that named it.
    /// </summary>
    private static List<(LevelObject Object, int Segment)>? Resolve(List<LevelObject> tree, List<string> segments)
    {
        int first = segments.FindIndex(s => s.Length > 0);
        if (first < 0) return null;
        foreach (var anchor in PreOrder(tree))
        {
            if (anchor.Name != segments[first]) continue;
            var chain = new List<(LevelObject, int)> { (anchor, first) };
            var at = anchor;
            for (int i = first + 1; i < segments.Count && at != null; i++)
            {
                if (segments[i].Length == 0) continue;
                at = at.Children.FirstOrDefault(c => c.Name == segments[i]);
                if (at != null) chain.Add((at, i));
            }
            if (at != null) return chain;
        }
        return null;
    }

    /// <summary>A target's names, and what stood between them - kept, so a
    /// rewritten path is spelled the way the author's was.</summary>
    private static (List<string> Segments, List<string> Separators) SplitPath(string target)
    {
        var segments = new List<string>();
        var separators = new List<string>();
        var parts = System.Text.RegularExpressions.Regex.Split(target, @"(\s*>\s*|/)");
        for (int i = 0; i < parts.Length; i++)
            if (i % 2 == 0) segments.Add(parts[i]);
            else separators.Add(parts[i]);
        return (segments, separators);
    }

    private static string JoinPath(List<string> segments, List<string> separators)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            sb.Append(segments[i]);
            if (i < separators.Count) sb.Append(separators[i]);
        }
        return sb.ToString();
    }

    // ── The category row ──────────────────────────────────────────────────

    /// <summary>The category a row's target picker is set to, under the name
    /// it has now: rows written before a category was renamed say the old one
    /// until they are next saved.</summary>
    private static string Category(Dictionary<string, string> ps)
    {
        if (!ps.TryGetValue("kind", out var k) || string.IsNullOrEmpty(k))
            ps.TryGetValue("targetKind", out k);
        return k switch
        {
            "Level Overlay" or "Extra GameObjects" => "GameObjects",   // English on purpose: values packs stored.
            null => "",
            _ => k,
        };
    }

    /// <summary>
    /// The names a row holds outside its typed params: the target picker shared
    /// by switching things on and off, sprite swaps, fades, moves and the
    /// matching condition (Category, Level and Target, stored as <c>kind</c>,
    /// <c>overlayLevel</c> and <c>target</c>), and the level tokens
    /// (<c>place:Key</c>) a Level field holds.
    /// <para/>
    /// Not schema types, which is why renames used to miss them: the row draws
    /// them itself. A scene renamed while a Set-Active row pointed at it left
    /// the row pointing at nothing.
    /// </summary>
    private static int RenameTargets(Dictionary<string, string>? ps, IEnumerable<ParamSchema> schemas,
                                     RefKind kind, string oldName, string newName, bool rewrite)
    {
        if (ps == null) return 0;
        int n = 0;

        void Value(string key, string from, string to)
        {
            if (!ps.TryGetValue(key, out var v) || v != from) return;
            n++;
            if (rewrite) ps[key] = to;
        }

        string category = Category(ps);
        switch (kind)
        {
            case RefKind.Scene:
                if (category == "Scene") Value("target", oldName, newName);
                break;

            case RefKind.Outfit:
                if (category == "Bust") Value("target", oldName, newName);
                break;

            case RefKind.Place:
                string from = PlaceToken + oldName, to = PlaceToken + newName;
                if (category == "Places") Value("target", from, to);
                Value("overlayLevel", from, to);
                foreach (var s in schemas.Where(s => s.Type == ParamType.LevelRef))
                    Value(s.Key, from, to);
                break;
        }
        return n;
    }

    /// <summary>How a Level field names one of the pack's places.</summary>
    private const string PlaceToken = "place:";

    /// <summary>
    /// Point every row that names one task of one of the pack's quests at the
    /// task's new key. Returns how many were rewritten.
    /// <para/>
    /// A task key is only unique within its quest, so the quest is part of the
    /// match: another quest's task of the same name is a different task.
    /// </summary>
    public static int RenameQuestTask(ModPack? pack, string questKey, string oldTask, string newTask)
        => RenameQuestTask(pack, questKey, oldTask, newTask, vanilla: false);

    /// <summary>
    /// The same, for a task a pack adds to one of the game's quests: rows name
    /// that quest on the Vanilla side, by the game's name, and the task by the
    /// pack's key.
    /// </summary>
    public static int RenameQuestTask(ModPack? pack, string questKey, string oldTask, string newTask, bool vanilla)
    {
        if (pack == null || string.IsNullOrEmpty(oldTask) || oldTask == newTask) return 0;

        int n = 0;
        void Row(Dictionary<string, string> ps)
        {
            if (ps == null || QuestReferences.IsVanilla(ps) != vanilla) return;
            if (QuestReferences.Param(ps, Shared.QuestVocabulary.QuestParam) != questKey) return;
            if (QuestReferences.Param(ps, Shared.QuestVocabulary.TaskParam) != oldTask) return;
            ps[Shared.QuestVocabulary.TaskParam] = newTask;
            n++;
        }

        foreach (var (c, _) in PackWalk.Conditions(pack))
            if (c.Type == NodeConditionTypes.QuestState || c.Type == NodeConditionTypes.QuestCounter)
                Row(c.Params);
        foreach (var (a, _) in PackWalk.Actions(pack))
            foreach (var each in WithBranches(a))
                if (each.Type == NodeActionTypes.Quest) Row(each.Params);
        return n;
    }

    /// <summary>
    /// Where a name is used, in the author's terms, without changing anything.
    /// <para/>
    /// For telling somebody what a rename is about to touch — or what a delete
    /// would strand.
    /// </summary>
    public static List<string> FindReferences(ModPack? pack, RefKind kind, string name)
    {
        var hits = new List<string>();
        if (pack == null || string.IsNullOrEmpty(name)) return hits;

        foreach (var (condition, where) in PackWalk.Conditions(pack))
        {
            var schemas = ConditionSchemas.For(condition.Type);
            if (CountParams(condition.Params, schemas, kind, name) > 0
                || RenameTargets(condition.Params, schemas, kind, name, name, rewrite: false) > 0)
                hits.Add(where);
        }

        foreach (var (action, where) in PackWalk.Actions(pack))
        {
            var schemas = ActionSchemas.For(action.Type);
            if (CountParams(action.Params, schemas, kind, name) > 0
                || RenameTargets(action.Params, schemas, kind, name, name, rewrite: false) > 0)
                hits.Add(where);
        }

        int structural = RenameStructural(pack, kind, name, name, rewrite: false);
        if (structural > 0)
            hits.Add(Loc.P("walk.otherReferences", structural));

        return hits.Distinct().ToList();
    }

    // ── Typed params ──────────────────────────────────────────────────────

    private static int RenameParams(Dictionary<string, string>? ps, IEnumerable<ParamSchema> schemas,
                                    RefKind kind, string oldName, string newName)
    {
        if (ps == null) return 0;
        if (!Typed.TryGetValue(kind, out var types) || types.Length == 0) return 0;

        int count = 0;
        foreach (var s in schemas)
        {
            if (Array.IndexOf(types, s.Type) < 0) continue;
            if (!ps.TryGetValue(s.Key, out var val) || val != oldName) continue;
            ps[s.Key] = newName;
            count++;
        }
        return count;
    }

    private static int CountParams(Dictionary<string, string>? ps, IEnumerable<ParamSchema> schemas,
                                   RefKind kind, string name)
    {
        if (ps == null) return 0;
        if (!Typed.TryGetValue(kind, out var types) || types.Length == 0) return 0;

        return schemas.Count(s => Array.IndexOf(types, s.Type) >= 0
                               && ps.TryGetValue(s.Key, out var val) && val == name);
    }

    // ── The references that are not params ───────────────────────────────

    /// <summary>
    /// Fields that hold a name outright rather than through a schema.
    /// <para/>
    /// Counting and rewriting share one method so the two can never disagree
    /// about what a reference is — a "what will this touch" that missed a field
    /// the rewrite then changed would be worse than not offering one.
    /// </summary>
    private static int RenameStructural(ModPack pack, RefKind kind,
                                        string oldName, string newName, bool rewrite)
    {
        int n = 0;

        void Field(Func<string> read, Action<string> write)
        {
            if (read() != oldName) return;
            n++;
            if (rewrite && oldName != newName) write(newName);
        }

        void QuestParam(Dictionary<string, string> ps)
        {
            if (ps == null || QuestReferences.IsVanilla(ps)) return;
            Field(() => QuestReferences.Param(ps, Shared.QuestVocabulary.QuestParam),
                  v => ps[Shared.QuestVocabulary.QuestParam] = v);
        }

        switch (kind)
        {
            case RefKind.Character:
                foreach (var d in pack.Dialogues)
                    foreach (var node in d.Nodes)
                    {
                        var x = node;
                        Field(() => x.Actor, v => x.Actor = v);   // who speaks the line
                    }
                break;

            case RefKind.Outfit:
                foreach (var d in pack.Dialogues)
                    foreach (var node in d.Nodes)
                    {
                        var x = node;
                        Field(() => x.Outfit, v => x.Outfit = v);   // switched into mid-line
                    }
                foreach (var c in pack.Characters)
                {
                    var x = c;
                    Field(() => x.DefaultOutfit, v => x.DefaultOutfit = v);
                }
                // The pre-merge shape, still readable and still loadable.
                foreach (var a in pack.Actors)
                {
                    var x = a;
                    Field(() => x.DefaultBustKey, v => x.DefaultBustKey = v);
                    for (int i = 0; i < x.Outfits.Count; i++)
                    {
                        int at = i;
                        Field(() => x.Outfits[at], v => x.Outfits[at] = v);
                    }
                }
                break;

            case RefKind.Expression:
                foreach (var d in pack.Dialogues)
                    foreach (var node in d.Nodes)
                    {
                        var x = node;
                        Field(() => x.Expression, v => x.Expression = v);
                    }
                break;

            case RefKind.Place:
                foreach (var b in pack.MapButtons)
                {
                    var x = b;
                    Field(() => x.Target, v => x.Target = v);
                }
                foreach (var p in pack.Places)
                    foreach (var b in p.NavigatorButtons)
                    {
                        var x = b;
                        Field(() => x.Target, v => x.Target = v);
                    }
                foreach (var v in pack.VanillaExtensions)
                    foreach (var b in v.NavigatorButtons)
                    {
                        var x = b;
                        Field(() => x.Target, v2 => x.Target = v2);
                    }
                break;

            case RefKind.Dialogue:
                // A pack's room talks name the dialogues they offer.
                for (int i = 0; i < pack.CustomRoomTalks.Count; i++)
                {
                    int at = i;
                    Field(() => pack.CustomRoomTalks[at], v => pack.CustomRoomTalks[at] = v);
                }
                break;

            case RefKind.Quest:
                // Not a schema type: which quest a row names depends on its
                // source, and a game quest that happens to share the name is a
                // different quest that must be left alone.
                foreach (var (c, _) in PackWalk.Conditions(pack))
                    if (c.Type == NodeConditionTypes.QuestState || c.Type == NodeConditionTypes.QuestCounter)
                        QuestParam(c.Params);
                foreach (var (a, _) in PackWalk.Actions(pack))
                    foreach (var each in WithBranches(a))
                        if (each.Type == NodeActionTypes.Quest) QuestParam(each.Params);
                break;

            case RefKind.Npc:
                foreach (var (_, placement) in Placements(pack))
                {
                    var x = placement;
                    Field(() => x.Npc, v => x.Npc = v);
                }
                break;

            case RefKind.Music:
                // What a button changes the music to on the way.
                foreach (var b in pack.MapButtons)
                {
                    var x = b;
                    Field(() => x.Music, v => x.Music = v);
                }
                foreach (var b in pack.Places.SelectMany(p => p.NavigatorButtons)
                                  .Concat(pack.VanillaExtensions.SelectMany(v => v.NavigatorButtons)))
                {
                    var x = b;
                    Field(() => x.Music, v => x.Music = v);
                }
                break;

            case RefKind.Sfx:
                // What a screen's buttons, or one of them, sound like.
                foreach (var ui in pack.Uis)
                {
                    var x = ui;
                    Field(() => x.ButtonSound, v => x.ButtonSound = v);
                    foreach (var node in UiNodes(ui.Nodes))
                    {
                        var y = node;
                        Field(() => y.ClickSound, v => y.ClickSound = v);
                    }
                }
                break;
        }
        return n;
    }

    private static IEnumerable<UiNodeDef> UiNodes(IEnumerable<UiNodeDef>? nodes)
    {
        if (nodes == null) yield break;
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in UiNodes(node.Children)) yield return child;
        }
    }

    /// <summary>An action and every action inside its dice branches, which
    /// the walk hands over as one.</summary>
    private static IEnumerable<NodeActionDef> WithBranches(NodeActionDef? action)
    {
        if (action == null) yield break;
        yield return action;
        if (action.Branches == null) yield break;
        foreach (var b in action.Branches)
            foreach (var inner in WithBranches(b.Action))
                yield return inner;
    }
}
