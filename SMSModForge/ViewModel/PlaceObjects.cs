using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;

namespace SMSModForge.ViewModel;

/// <summary>
/// The objects of a place, or of a game's level, as the preview's keys work on them (after 1.10.0, the author:
/// "copy/paste/duplicate (duplicating just duplicates at the same hierarchy level, while pasting pastes under
/// the selected GO). Deleting directly from there should also be an option"), and the recycle bin what is
/// deleted goes to ("just in case the user needs them again later").
/// <para/>
/// What it works on is a row of the tree - an object (<see cref="GameObjectViewModel"/>) or a character
/// standing on one (<see cref="NpcPlacementViewModel"/>) - and everything under it goes with it: a copy, a
/// duplicate, what is deleted and what is put back carry their children and the characters on them.
/// <para/>
/// Every change is in the pack's model, so an undo takes it back like any other edit; the bin is the place's
/// own list (<see cref="PlaceDef.RecycleBin"/>, <see cref="VanillaPlaceExtensionDef.RecycleBin"/>).
/// </summary>
public sealed class PlaceObjects
{
    private readonly ObservableCollection<GameObjectViewModel> _top;
    private readonly Func<List<BinnedObjectDef>> _bin;
    private readonly Func<GameObjectDef, int, GameObjectViewModel> _insertTop;
    private readonly Func<int> _topEnd;
    private readonly Func<GameObjectViewModel?> _npcHost;
    private readonly Func<string?> _levelPath;
    private readonly Action<GameObjectDef, bool>? _anchorCopy;
    private readonly Func<bool> _backReversed;

    /// <param name="top">The place's top rows.</param>
    /// <param name="bin">The place's recycle bin, as its model holds it now.</param>
    /// <param name="insertTop">A top object put in at a place among the top rows (kept where the place allows).</param>
    /// <param name="topEnd">Where a top object goes when it goes last: before a pack place's forced NPCs object.</param>
    /// <param name="npcHost">Where a character goes when what is picked cannot hold one.</param>
    /// <param name="levelPath">The level's path in the game's scene (<c>5_Levels/5_MyRoom</c>), for a copy of one of
    /// its objects; null when it has none known before the game runs (a pack place's).</param>
    /// <param name="anchorCopy">A copy of one of the game's objects given what the game says of the original, so the
    /// preview draws it: a game's level's own way (VanillaPlaceExtensionViewModel.AnchorCopy).</param>
    /// <param name="backReversed">Whether the level has a back picture that drifts reversed (<see cref="BackReversed"/>).</param>
    internal PlaceObjects(ObservableCollection<GameObjectViewModel> top, Func<List<BinnedObjectDef>> bin,
                          Func<GameObjectDef, int, GameObjectViewModel> insertTop, Func<int> topEnd,
                          Func<GameObjectViewModel?> npcHost, Func<string?>? levelPath = null,
                          Action<GameObjectDef, bool>? anchorCopy = null, Func<bool>? backReversed = null)
    {
        _top = top;
        _bin = bin;
        _insertTop = insertTop;
        _topEnd = topEnd;
        _npcHost = npcHost;
        _levelPath = levelPath ?? (() => null);
        _anchorCopy = anchorCopy;
        _backReversed = backReversed ?? (() => false);
        ReadBinAgain();
    }

    /// <summary>
    /// How an object or a character of the pack's drifts when its Reversed is not set: as the level's back picture
    /// does, whose effect it is cloned with or given a copy of (PlaceFactory); not reversed where the level has no back
    /// picture. What its Reversed tick shows then (after 1.10.0: it showed unticked, and drifted reversed in the game
    /// in the usual place).
    /// </summary>
    internal bool BackReversed => _backReversed();

    /// <summary>The back picture's reversal changed: every row that takes it says so to what shows its tick.</summary>
    internal void BackReversalChanged()
    {
        void Walk(IEnumerable<GameObjectViewModel> rows)
        {
            foreach (var row in rows)
            {
                row.ReversalMayHaveChanged();
                foreach (var npc in row.Npcs)
                {
                    npc.ReversalMayHaveChanged();
                    Walk(npc.Children);
                }
                Walk(row.Children);
            }
        }
        Walk(_top);
    }

    /// <summary>The bin's rows, oldest first, as its list has them.</summary>
    public ObservableCollection<BinnedObjectViewModel> Bin { get; } = new();

    /// <summary>The bin's rows made again from its list: after an undo, or the list taken from a step.</summary>
    internal void ReadBinAgain()
    {
        Bin.Clear();
        foreach (var entry in _bin()) Bin.Add(new BinnedObjectViewModel(entry));
    }

    // ── What a row is, and what can be done to it ───────────────────────

    /// <summary>
    /// What a row of the hierarchy is, as the keys treat it (the author, after 1.10.0: "We should be able to use
    /// the hotkeys on everything that appears on the Hierarchy really"):
    /// <list type="bullet">
    /// <item><b>Own</b> - an object the pack makes (a copy of one of the game's among them, and an object the
    /// author bound by name with nothing known of it) or a character standing in the place: copied as it is,
    /// deleted to the bin.</item>
    /// <item><b>GameLight</b> - one of the game's objects that is a light and nothing else (a pack place's
    /// Light 2D, a game's level's lamps): copied as a light of the pack's own, with its settings, where it
    /// stands; deleted by switching its light off, which the bin switches back on.</item>
    /// <item><b>GameObject</b> - any other of the game's objects, filled in from its level: copied as a copy
    /// of the game's object (CopyOf), drawn as the original; deleted by switching it off - a pack cannot take
    /// the game's object out of the game, and a row dropped only came back as the game has it - which the bin
    /// switches back on.</item>
    /// <item><b>NpcRoot</b> - a pack place's forced NPCs object, the one the game puts every character under:
    /// neither copied nor deleted. Pasting under it is how a character is put in the place.</item>
    /// </list>
    /// </summary>
    private enum Kind { None, Own, GameLight, GameObject, NpcRoot }

    private static Kind KindOf(object? row) => row switch
    {
        NpcPlacementViewModel => Kind.Own,
        GameObjectViewModel { IsNpcRoot: true } => Kind.NpcRoot,
        GameObjectViewModel g when g.IsGameLightOnly => Kind.GameLight,
        GameObjectViewModel g when g.Model.Bind && g.Model.Baseline != null => Kind.GameObject,
        GameObjectViewModel => Kind.Own,
        _ => Kind.None,
    };

    public bool CanCopy(object? row) => KindOf(row) is Kind.Own or Kind.GameLight or Kind.GameObject;

    public bool CanDuplicate(object? row) => CanCopy(row) && Find(row!) != null;

    /// <summary>Whether Delete - or the row's own − button - does something to it.</summary>
    public bool CanDelete(object? row) => KindOf(row) switch
    {
        Kind.Own => row is GameObjectViewModel g ? g.CanTakeOut : ((NpcPlacementViewModel)row!).RemoveCommand.CanExecute(null),
        Kind.GameLight => !((GameObjectViewModel)row!).Model.RemoveLight,
        Kind.GameObject => ((GameObjectViewModel)row!).StartActive,
        _ => false,
    };

    public static bool CanPaste => EditorClipboard.Has<GameObjectDef>() || EditorClipboard.Has<NpcPlacementDef>();

    /// <summary>The row to the editor's clipboard, as what pasting it makes. <paramref name="at"/>: where in the
    /// world one of the game's lights stands, for a light of the pack's own there (the preview knows).</summary>
    public void Copy(object row, float[]? at = null)
    {
        if (!CanCopy(row)) return;
        if (row is NpcPlacementViewModel p) EditorClipboard.SetItem(p.Model);
        else if (row is GameObjectViewModel g && AsOwn(g, at) is { } def) EditorClipboard.SetItem(def);
    }

    /// <summary>
    /// What was copied, under <paramref name="under"/> - an object last among its children; nothing picked, last
    /// among the place's own. A character goes on what is picked when it can hold one (an object in the NPCs
    /// part of the tree), beside a character picked, else on the place's NPCs object. The row made, or null.
    /// </summary>
    public object? Paste(object? under)
    {
        if (EditorClipboard.GetItem<GameObjectDef>() is { } def)
        {
            if (def.IsCopy) _anchorCopy?.Invoke(def, true);
            return PutObject(def, under, int.MaxValue, rename: true);
        }
        if (EditorClipboard.GetItem<NpcPlacementDef>() is { } npc) return PutCharacter(npc, under, int.MaxValue);
        return null;
    }

    /// <summary>A copy of the row, with all under it, right after it among its siblings (see <see cref="Kind"/>).
    /// The row made, or null.</summary>
    public object? Duplicate(object row, float[]? at = null)
    {
        if (!CanCopy(row) || Find(row) is not { } spot) return null;
        if (row is GameObjectViewModel g && AsOwn(g, at) is { } def)
        {
            if (def.IsCopy) _anchorCopy?.Invoke(def, true);
            return PutObjectAt(def, spot.Node, spot.Npc, spot.Index + 1, rename: true);
        }
        if (row is NpcPlacementViewModel p && spot.Node != null)
            return spot.Node.AddNpcAt(Named(EditorClipboard.CloneOne(p.Model), spot.Node.Npcs.Select(n => n.Model.Name)), spot.Index + 1);
        return null;
    }

    /// <summary>
    /// What a copy of the row is, as an object of the pack's own (see <see cref="Kind"/>): the row itself, cloned;
    /// one of the game's lights as a light of the pack's own, standing at <paramref name="at"/> when given; one of
    /// the game's objects as a copy of it (CopyOf its path in the game's scene), with what the pack made under it
    /// but not the game's parts, which the copy is made with. Null when there is nothing to copy.
    /// </summary>
    private GameObjectDef? AsOwn(GameObjectViewModel g, float[]? at)
    {
        var def = EditorClipboard.CloneOne(g.Model);
        switch (KindOf(g))
        {
            case Kind.Own:
                return def;
            case Kind.GameLight:
                Unbind(def);
                def.Light = EditorClipboard.CloneOne(g.Model.Light ?? g.Model.BaselineLight ?? new LightDef());
                def.Sprite = "";
                if (at is { Length: >= 2 }) { def.X = at[0]; def.Y = at[1]; }
                return def;
            case Kind.GameObject:
                if (ScenePathOf(g) is not { } path) return def;
                Unbind(def);
                def.CopyOf = path;
                return def;
            default:
                return null;
        }
    }

    /// <summary>An object of the game's made the pack's own: nothing bound, the game's parts under it left out.</summary>
    private static void Unbind(GameObjectDef def)
    {
        def.Bind = false;
        def.CopyOf = "";
        def.CopyBeside = false;
        def.BaselineName = null;
        def.Role = "";
        def.RemoveLight = false;
        def.OverrideTransform = false;
        def.OverrideActive = false;
        def.OverrideParallax = false;
        def.Children.RemoveAll(c => c.Bind);
    }

    /// <summary>Where one of the game's objects is in the game's scene - the level's path, then each object of the
    /// game's down to it, by the names the game gives them - or null when that is not known (a pack place, whose
    /// level is named as the game loads it, or an object under one the pack made).</summary>
    internal string? ScenePathOf(GameObjectViewModel row)
    {
        if (_levelPath() is not { Length: > 0 } level) return null;
        var chain = ChainTo(row);
        if (chain == null || chain.Any(n => !n.Model.Bind || n.Model.Baseline == null)) return null;
        return level + "/" + string.Join("/", chain.Select(n => n.Model.BaselineName is { Length: > 0 } b ? b : n.Model.Name));
    }

    /// <summary>The objects from the top down to the row, the row last; null when it is not in the tree or stands on
    /// a character.</summary>
    private List<GameObjectViewModel>? ChainTo(GameObjectViewModel row)
    {
        var path = new List<GameObjectViewModel>();
        bool Walk(IList<GameObjectViewModel> rows)
        {
            foreach (var r in rows)
            {
                path.Add(r);
                if (ReferenceEquals(r, row) || Walk(r.Children)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }
        return Walk(_top) ? path : null;
    }

    /// <summary>
    /// The row out of the place, as its − button does (see <see cref="Kind"/>): what the pack made to the bin
    /// with all under it, and where it was; one of the game's lights or objects switched off where it stands,
    /// the bin keeping what it was to switch it back on. False when nothing was done.
    /// </summary>
    public bool Delete(object row)
    {
        if (!CanDelete(row)) return false;
        switch (row)
        {
            case GameObjectViewModel g when KindOf(g) == Kind.GameLight:
                if (Find(g) is { } lamp) Keep(SwitchedOff(g), lamp);
                g.RemoveLight();
                return true;
            case GameObjectViewModel g when KindOf(g) == Kind.GameObject:
                if (Find(g) is { } game) Keep(SwitchedOff(g), game);
                g.OverrideActive = true;
                g.StartActive = false;
                return true;
            case GameObjectViewModel g:
                if (Find(g) is { } at) Keep(new BinnedObjectDef { Object = g.Model }, at);
                g.TakeOut();
                return true;
            case NpcPlacementViewModel p:
                if (Find(p) is { } there) Keep(new BinnedObjectDef { Character = p.Model }, there);
                p.TakeOut();
                return true;
        }
        return false;
    }

    /// <summary>What the bin keeps of one of the game's objects switched off: its name, and what switching it back on
    /// gives back - whether it was on and the pack said so, and its light.</summary>
    private static BinnedObjectDef SwitchedOff(GameObjectViewModel g) => new()
    {
        SwitchedOff = true,
        Object = new GameObjectDef
        {
            Name = g.Model.Name, Bind = true,
            OverrideActive = g.Model.OverrideActive, StartActive = g.Model.StartActive,
            Light = g.Model.Light == null ? null : EditorClipboard.CloneOne(g.Model.Light),
        },
    };

    private void Keep(BinnedObjectDef entry, Spot at)
    {
        entry.Under = at.Under;
        entry.UnderNames = at.UnderNames;
        entry.At = at.Index;
        _bin().Add(entry);
        Bin.Add(new BinnedObjectViewModel(entry));
    }

    /// <summary>
    /// What the bin row holds, back where it was: under the same object, at the same place among its siblings -
    /// found by where it hung, or by the names along the way when the place has changed since, else last among
    /// the place's own (a character: on the NPCs object). Its name made new when another has it now. One of the
    /// game's objects switched off is switched back on where it stands. The row, or null when it could not go
    /// anywhere (the game's object it was is no longer in the tree).
    /// </summary>
    public object? Restore(BinnedObjectViewModel binned)
    {
        var entry = binned.Model;
        var (node, npc, found) = Resolve(entry.Under, entry.UnderNames);
        object? made = null;
        if (entry.SwitchedOff)
            made = found && entry.Object != null ? SwitchBackOn(entry.Object, node, npc, entry.At) : null;
        else if (entry.Object is { } def)
        {
            // Kept in the file, a copy has only what the pack changes: the rest is the original's.
            if (def.IsCopy && def.Baseline == null) _anchorCopy?.Invoke(def, false);
            made = found ? PutObjectAt(def, node, npc, entry.At, rename: true) : PutObject(def, null, int.MaxValue, rename: true);
        }
        else if (entry.Character is { } character)
            made = PutCharacter(character, found && node != null && node.CanHostNpcs ? node : null, found ? entry.At : int.MaxValue);
        if (made == null) return null;
        Forget(binned);
        return made;
    }

    private GameObjectViewModel? SwitchBackOn(GameObjectDef was, GameObjectViewModel? node, NpcPlacementViewModel? npc, int at)
    {
        IList<GameObjectViewModel> rows = node?.Children ?? npc?.Children ?? (IList<GameObjectViewModel>)_top;
        bool Is(GameObjectViewModel r) => r.Model.Bind && string.Equals(r.Model.Name, was.Name, StringComparison.Ordinal);
        var target = at >= 0 && at < rows.Count && Is(rows[at]) ? rows[at] : rows.FirstOrDefault(Is);
        if (target == null) return null;
        if (target.IsGameLightOnly)
            target.AddLight(EditorClipboard.CloneOne(was.Light ?? target.Model.BaselineLight ?? new LightDef()));
        else
        {
            target.OverrideActive = was.OverrideActive;
            target.StartActive = was.OverrideActive ? was.StartActive : target.Model.Baseline?.ActiveSelf ?? true;
        }
        return target;
    }

    // ── New ones (the GameObjects list's buttons, moved beside the picture) ──

    /// <summary>
    /// A new object of the pack's own, among the place's own (before a pack place's NPCs object), standing at
    /// <paramref name="at"/> - the middle of what the preview shows - and called "GameObject", "GameObject 2"...,
    /// apart from every other object's name, since dialogues reach an object by it. Its row.
    /// </summary>
    public GameObjectViewModel AddObject(float[]? at = null)
    {
        var def = new GameObjectDef { Name = LightObjects.NewName(LightObjects.Names(_top), "GameObject") };   // English on purpose: an object's name, which dialogues address.
        if (at is { Length: >= 2 }) { def.X = at[0]; def.Y = at[1]; }
        return _insertTop(def, _topEnd());
    }

    /// <summary>A new character, with no NPC chosen yet: on what is picked when it can hold one, beside a character
    /// picked, else on the place's NPCs object (as a paste puts one). Its row, or null with nowhere to stand it.</summary>
    public NpcPlacementViewModel? AddCharacter(object? under) => PutCharacter(new NpcPlacementDef(), under, int.MaxValue) as NpcPlacementViewModel;

    /// <summary>The bin row gone for good, and what it held.</summary>
    public void Forget(BinnedObjectViewModel binned)
    {
        _bin().Remove(binned.Model);
        Bin.Remove(binned);
    }

    /// <summary>Everything in the bin gone for good.</summary>
    public void EmptyBin()
    {
        _bin().Clear();
        Bin.Clear();
    }

    // ── Putting things in ───────────────────────────────────────────────

    private object PutObject(GameObjectDef def, object? under, int index, bool rename) => under switch
    {
        GameObjectViewModel g => PutObjectAt(def, g, null, index, rename),
        NpcPlacementViewModel p => PutObjectAt(def, null, p, index, rename),
        _ => PutObjectAt(def, null, null, index, rename),
    };

    private object PutObjectAt(GameObjectDef def, GameObjectViewModel? node, NpcPlacementViewModel? npc, int index, bool rename)
    {
        if (node != null)
        {
            if (rename) Named(def, node.Children.Select(c => c.Model.Name));
            return node.AdoptAt(def, index);
        }
        if (npc != null)
        {
            if (rename) Named(def, npc.Children.Select(c => c.Model.Name));
            return npc.AdoptAt(def, index);
        }
        if (rename) Named(def, _top.Select(c => c.Model.Name));
        return _insertTop(def, Math.Min(index, _topEnd()));
    }

    private object? PutCharacter(NpcPlacementDef def, object? under, int index)
    {
        GameObjectViewModel? host = null;
        switch (under)
        {
            case GameObjectViewModel g when g.CanHostNpcs:
                host = g;
                break;
            case NpcPlacementViewModel p when Find(p) is { Node: { } standsOn } at:
                host = standsOn;
                index = at.Index + 1;
                break;
        }
        host ??= _npcHost();
        if (host == null) return null;
        return host.AddNpcAt(Named(def, host.Npcs.Select(n => n.Model.Name)), index);
    }

    /// <summary>Its name, unless one beside it has it: then "_copy", "_copy2"... as a pasted row's.</summary>
    private static GameObjectDef Named(GameObjectDef def, IEnumerable<string> beside)
    {
        def.Name = UniqueName(def.Name, beside, "GameObject");
        return def;
    }

    private static NpcPlacementDef Named(NpcPlacementDef def, IEnumerable<string> beside)
    {
        // A character with no name of its own is called by its NPC's: nothing to keep apart.
        if (!string.IsNullOrWhiteSpace(def.Name)) def.Name = UniqueName(def.Name, beside, def.Name);
        return def;
    }

    private static string UniqueName(string name, IEnumerable<string> beside, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name)) name = fallback;
        var taken = new HashSet<string>(beside.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        string root = System.Text.RegularExpressions.Regex.Replace(name, @"_copy\d*$", "");
        for (int i = 1; ; i++)
        {
            string candidate = i == 1 ? root + "_copy" : root + "_copy" + i;
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    // ── Where a row is ──────────────────────────────────────────────────

    /// <summary>Where a row hangs: what it is under (an object, a character, or neither: the top), its place
    /// there, and the way to it (<see cref="BinnedObjectDef.Under"/>).</summary>
    internal sealed record Spot(GameObjectViewModel? Node, NpcPlacementViewModel? Npc, int Index, string Under, string UnderNames);

    /// <summary>Where the row is in the tree, or null when it is not in it.</summary>
    internal Spot? Find(object row)
    {
        Spot? In(IList<GameObjectViewModel> rows, GameObjectViewModel? node, NpcPlacementViewModel? npc, string path, string names)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (ReferenceEquals(r, row)) return new Spot(node, npc, i, path, names);
                string here = Join(path, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                string hereNames = Join(names, r.Model.Name);
                if (In(r.Children, r, null, here, hereNames) is { } deeper) return deeper;
                for (int k = 0; k < r.Npcs.Count; k++)
                {
                    var p = r.Npcs[k];
                    if (ReferenceEquals(p, row)) return new Spot(r, null, k, here, hereNames);
                    if (In(p.Children, null, p, Join(here, "n" + k.ToString(System.Globalization.CultureInfo.InvariantCulture)), Join(hereNames, p.Model.Name))
                        is { } onIt) return onIt;
                }
            }
            return null;
        }
        return In(_top, null, null, "", "");
    }

    private static string Join(string path, string step) => path.Length == 0 ? step : path + "/" + step;

    /// <summary>
    /// What <paramref name="under"/> leads to now: the object or character there when its names still match
    /// <paramref name="names"/>, else the first one of those names at each step. <c>found</c> false when
    /// neither leads anywhere; an empty way is the top, always found.
    /// </summary>
    private (GameObjectViewModel? Node, NpcPlacementViewModel? Npc, bool Found) Resolve(string under, string names)
    {
        if (under.Length == 0) return (null, null, true);
        var steps = under.Split('/');
        var called = names.Split('/');
        GameObjectViewModel? node = null;
        NpcPlacementViewModel? npc = null;
        bool byPlace = true, byName = true;
        GameObjectViewModel? nodeByName = null;
        NpcPlacementViewModel? npcByName = null;
        for (int s = 0; s < steps.Length; s++)
        {
            string name = s < called.Length ? called[s] : "";
            bool isNpc = steps[s].StartsWith("n", StringComparison.Ordinal);
            int.TryParse(isNpc ? steps[s][1..] : steps[s], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int i);
            if (byPlace)
            {
                (node, npc, byPlace) = Step(node, npc, isNpc, i, name, byIndex: true);
            }
            if (byName)
            {
                (nodeByName, npcByName, byName) = Step(nodeByName, npcByName, isNpc, i, name, byIndex: false);
            }
            if (!byPlace && !byName) return (null, null, false);
        }
        return byPlace ? (node, npc, true) : (nodeByName, npcByName, true);
    }

    private (GameObjectViewModel?, NpcPlacementViewModel?, bool) Step(GameObjectViewModel? node, NpcPlacementViewModel? npc,
                                                                       bool isNpc, int index, string name, bool byIndex)
    {
        if (isNpc)
        {
            if (node == null) return (null, null, false);
            var standing = byIndex
                ? (index >= 0 && index < node.Npcs.Count && node.Npcs[index].Model.Name == name ? node.Npcs[index] : null)
                : node.Npcs.FirstOrDefault(p => p.Model.Name == name);
            return standing == null ? (null, null, false) : (null, standing, true);
        }
        IList<GameObjectViewModel> rows = node?.Children ?? npc?.Children ?? (IList<GameObjectViewModel>)_top;
        var next = byIndex
            ? (index >= 0 && index < rows.Count && rows[index].Model.Name == name ? rows[index] : null)
            : rows.FirstOrDefault(r => r.Model.Name == name);
        return next == null ? (null, null, false) : (next, null, true);
    }
}

/// <summary>A row of a place's recycle bin: what it holds, called as it was, and what it was under.</summary>
public sealed class BinnedObjectViewModel
{
    public BinnedObjectDef Model { get; }

    public BinnedObjectViewModel(BinnedObjectDef model) => Model = model;

    public bool IsCharacter => Model.Object == null && Model.Character != null;

    /// <summary>Its name, as its row in the tree showed it.</summary>
    public string Display
    {
        get
        {
            if (Model.Object is { } o) return string.IsNullOrWhiteSpace(o.Name) ? Localization.Loc.T("gameObject.unnamed") : o.Name;
            var c = Model.Character;
            string label = c == null ? "" : string.IsNullOrWhiteSpace(c.Name) ? c.Npc : c.Name;
            return string.IsNullOrWhiteSpace(label) ? "(unset)" : label;
        }
    }

    /// <summary>The names of what it was under, top first, or nothing: it was one of the place's own.</summary>
    public string Under => Model.UnderNames.Replace("/", " › ");
}
