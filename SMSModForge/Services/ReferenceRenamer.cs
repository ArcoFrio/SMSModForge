using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

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
            n += RenameParams(condition.Params, ConditionSchemas.For(condition.Type), kind, oldName, newName);

        foreach (var (action, _) in PackWalk.Actions(pack))
            n += RenameParams(action.Params, ActionSchemas.For(action.Type), kind, oldName, newName);

        n += RenameStructural(pack, kind, oldName, newName, rewrite: true);
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
            if (CountParams(condition.Params, ConditionSchemas.For(condition.Type), kind, name) > 0)
                hits.Add(where);

        foreach (var (action, where) in PackWalk.Actions(pack))
            if (CountParams(action.Params, ActionSchemas.For(action.Type), kind, name) > 0)
                hits.Add(where);

        int structural = RenameStructural(pack, kind, name, name, rewrite: false);
        if (structural > 0)
            hits.Add(structural == 1 ? "1 other reference" : $"{structural} other references");

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

            case RefKind.Npc:
                foreach (var p in pack.Places)
                    foreach (var placement in NpcPlacements(p))
                    {
                        var x = placement;
                        Field(() => x.Npc, v => x.Npc = v);
                    }
                break;
        }
        return n;
    }

    /// <summary>Every NPC placement a place holds, wherever the tree keeps
    /// them.</summary>
    private static IEnumerable<NpcPlacementDef> NpcPlacements(PlaceDef place)
    {
        foreach (var node in Descend(place.GameObjects))
            foreach (var placement in node.Npcs)
                yield return placement;
    }

    private static IEnumerable<GameObjectDef> Descend(IEnumerable<GameObjectDef>? nodes)
    {
        if (nodes == null) yield break;
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Descend(node.Children)) yield return child;
        }
    }
}
