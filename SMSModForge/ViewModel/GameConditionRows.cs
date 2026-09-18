using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// What the Quests and Dialogues tabs change about the game's own conditions,
/// in one place, so the two tabs show the same thing.
/// <para/>
/// Implemented by <see cref="MainViewModel"/>, which owns the pack (an undo
/// replaces it wholesale, so nothing here holds on to it) and knows every open
/// quest and conversation that has to be told.
/// </summary>
public interface IGameConditionEditor
{
    /// <summary>Whether the pack takes this condition out of the script's list.</summary>
    bool IsRemoved(string by, string script, JArray at, int index);

    /// <summary>Take a condition out of one of the game's scripts, or put it back.</summary>
    void SetRemoved(string by, string script, JArray at, int index, string type, string title, bool removed);

    /// <summary>A line's conditions as the pack leaves them: its version of
    /// the line, or the game's when it has none.</summary>
    IReadOnlyList<NodeConditionDef> LineConditions(string dialogue, long node);

    /// <summary>Give a line these conditions, changing the pack's version of
    /// the conversation - and making one if it has none.</summary>
    void SetLineConditions(string dialogue, long node, List<NodeConditionDef> conditions);
}

/// <summary>The editor the rows ask. Null outside a running editor, where
/// every row is read-only.</summary>
public static class GameConditionEditing
{
    public static IGameConditionEditor? Editor { get; set; }

    /// <summary>
    /// A line's conditions against the game's: which of the game's the pack
    /// has taken out (by position in the game's list), and which it has added.
    /// Matched in order, so a condition the game lists twice is only counted
    /// as there as many times as it is.
    /// </summary>
    internal static (HashSet<int> Removed, List<NodeConditionDef> Added) Compare(
        IReadOnlyList<NodeConditionDef> game, IReadOnlyList<NodeConditionDef> now)
    {
        var unmatched = now.Select(JsonConvert.SerializeObject).ToList();
        var used = new bool[unmatched.Count];
        var removed = new HashSet<int>();
        for (int i = 0; i < game.Count; i++)
        {
            string g = JsonConvert.SerializeObject(game[i]);
            int at = -1;
            for (int j = 0; j < unmatched.Count; j++)
                if (!used[j] && unmatched[j] == g) { at = j; break; }
            if (at < 0) removed.Add(i);
            else used[at] = true;
        }
        var added = new List<NodeConditionDef>();
        for (int j = 0; j < now.Count; j++)
            if (!used[j]) added.Add(now[j]);
        return (removed, added);
    }

    /// <summary>A line's conditions with one of the game's taken out or put
    /// back: the game's, in the game's order, less the ones taken out, then
    /// the pack's own.</summary>
    internal static List<NodeConditionDef> WithGameCondition(
        IReadOnlyList<NodeConditionDef> game, IReadOnlyList<NodeConditionDef> now, int index, bool removed)
    {
        var (gone, added) = Compare(game, now);
        if (removed) gone.Add(index); else gone.Remove(index);
        var list = new List<NodeConditionDef>();
        for (int i = 0; i < game.Count; i++)
            if (!gone.Contains(i)) list.Add(Clone(game[i]));
        list.AddRange(added);
        return list;
    }

    internal static NodeConditionDef Clone(NodeConditionDef c)
        => JsonConvert.DeserializeObject<NodeConditionDef>(JsonConvert.SerializeObject(c))!;
}

/// <summary>
/// One of the game's conditions, as a row that can be taken out and put back.
/// A row the pack cannot take out (where a script lives, or a test the game
/// does not keep in a list) is shown the same way, without the buttons.
/// </summary>
public sealed class GameConditionRowViewModel : ObservableObject
{
    private readonly Func<bool>? _isRemoved;
    private readonly Action<bool>? _setRemoved;

    private GameConditionRowViewModel(NodeConditionDef? condition, string text, string note,
                                      Func<bool>? isRemoved, Action<bool>? setRemoved, bool isYours,
                                      string header = "", bool isRoom = false)
    {
        IsRoom = isRoom;
        Condition = condition == null
            ? null
            : new NodeConditionViewModel(condition, isLocked: true, lockedHeader: header);
        Text = text;
        Note = note;
        IsYours = isYours;
        _isRemoved = isRemoved;
        _setRemoved = setRemoved;
        RemoveCommand = new RelayCommand(() => Set(true), () => CanRemove && !IsRemoved);
        UndoCommand = new RelayCommand(() => Set(false), () => CanRemove && IsRemoved);
    }

    /// <summary>A condition the pack can take out - or, where it is only being
    /// shown (<paramref name="canEdit"/> false), one that says whether it has
    /// been.</summary>
    internal static GameConditionRowViewModel Removable(VanillaDialogueCatalog.Step step, string note,
                                                        Func<bool> isRemoved, Action<bool> setRemoved, bool canEdit)
        => new(VanillaDialogueConditions.Translate(step), step.Describe(), note, isRemoved,
               canEdit ? setRemoved : null, false);

    internal static GameConditionRowViewModel Removable(NodeConditionDef condition, string note,
                                                        Func<bool> isRemoved, Action<bool> setRemoved, bool canEdit,
                                                        string header = "")
        => new(condition, "", note, isRemoved, canEdit ? setRemoved : null, false, header);

    /// <summary>
    /// Where the game's script sits, on a place a quest can change: a
    /// condition of that place like any other, and one the pack can take out -
    /// but not one of the game's own lists, so taking it out changes nothing
    /// for anybody else and is not a change a player is warned about.
    /// </summary>
    internal static GameConditionRowViewModel Room(NodeConditionDef condition, string note,
                                                   Func<bool> isRemoved, Action<bool> setRemoved, bool canEdit)
        => new(condition, "", note, isRemoved, canEdit ? setRemoved : null, false,
               GameConditionGroups.LocationHeader, isRoom: true);

    /// <summary>A condition that is simply there.</summary>
    internal static GameConditionRowViewModel Fixed(NodeConditionDef? condition, string text, string note)
        => new(condition, text, note, null, null, false);

    /// <summary>
    /// Where the game's script is: not a condition in its list, but the room
    /// it sits on, which is the only place it runs. Shown so a place that plays
    /// a conversation does not read as one that runs anywhere.
    /// </summary>
    internal static GameConditionRowViewModel Location(NodeConditionDef condition, string note)
        => new(condition, "", note, null, null, false, GameConditionGroups.LocationHeader);

    /// <summary>A condition of the pack's own on the line, shown for
    /// completeness; it is edited where it was added.</summary>
    internal static GameConditionRowViewModel Yours(NodeConditionDef condition, string note)
        => new(condition, "", note, null, null, true);

    /// <summary>The condition as the editor's own row, or null when the editor
    /// has no condition like it - then <see cref="Text"/> is the game's words.</summary>
    public NodeConditionViewModel? Condition { get; }

    public bool IsTranslated => Condition != null;

    /// <summary>The game's own words, for a condition the editor cannot show
    /// as one of its own.</summary>
    public string Text { get; }

    /// <summary>What taking it out changes, or why it cannot be.</summary>
    public string Note { get; }

    /// <summary>One of the pack's own, rather than the game's.</summary>
    public bool IsYours { get; }

    /// <summary>Where the game's script sits, rather than something in one of
    /// its condition lists.</summary>
    public bool IsRoom { get; }

    public bool CanRemove => _setRemoved != null && GameConditionEditing.Editor != null;

    public bool IsRemoved => _isRemoved?.Invoke() ?? false;

    public RelayCommand RemoveCommand { get; }
    public RelayCommand UndoCommand { get; }

    private void Set(bool removed)
    {
        _setRemoved?.Invoke(removed);
        Refresh();
    }

    internal void Refresh()
    {
        OnPropertyChanged(nameof(IsRemoved));
        RemoveCommand.Raise();
        UndoCommand.Raise();
    }
}

/// <summary>A set of the game's conditions that belong together: one line's,
/// or one script's.</summary>
public sealed class GameConditionGroupViewModel
{
    public GameConditionGroupViewModel(string title, string note, IReadOnlyList<GameConditionRowViewModel> rows)
    {
        Title = title;
        Note = note;
        Rows = rows;
    }

    public string Title { get; }
    public string Note { get; }
    public bool HasNote => Note.Length > 0;
    public IReadOnlyList<GameConditionRowViewModel> Rows { get; }

    /// <summary>How many of the GAME's conditions are taken out here. The room
    /// a script sits on is not one of them: it is where the script is, and
    /// taking it out changes nothing in the game's own lists.</summary>
    public int RemovedCount => Rows.Count(r => r.IsRemoved && !r.IsRoom);

    /// <summary>Put back every condition of the game's in this set.</summary>
    internal void RestoreAll()
    {
        foreach (var row in Rows.Where(r => r.IsRemoved && r.CanRemove))
            row.UndoCommand.Execute(null);
    }

    internal void Refresh()
    {
        foreach (var row in Rows) row.Refresh();
    }
}

/// <summary>
/// Builds the removable rows for the game's condition lists, the same way
/// for the Quests and Dialogues tabs.
/// </summary>
internal static class GameConditionGroups
{
    public const string ScriptNote =
        "Taking it out changes the game's script, so everything it guards - the conversation it plays, and "
        + "anything beside it - happens without it too.";

    public const string LineNote =
        "Taking it out changes this line in your version of the conversation, so the line itself is offered "
        + "without it too. The Dialogues tab shows the same change.";

    public const string LocationHeader = "Where the game's script is \u2014 it only runs while the player is here";

    public const string LevelNote =
        "The game's script sits on this place, so it only runs while the player is here. It is not one of the "
        + "conditions in the script's list, so there is nothing to take out, and the place is the game's: moving "
        + "it would mean moving the game's own script.";

    public const string RoomNote =
        "The game's script sits on this place, so the game only ever does this while the player is here. Take it "
        + "out and your pack's own conditions decide on their own, wherever the player is.";

    public const string PickedNote =
        "How the script picks this conversation among its children. Not a condition that can be taken out.";

    /// <summary>
    /// One place that plays a conversation, or one script that starts a quest:
    /// where it is, then each of its lists' conditions, then anything that is
    /// not in a list.
    /// </summary>
    /// <param name="showLocation">
    /// Whether the room the script is on is shown as a row of its own. The
    /// Dialogues tab shows it, since a conversation of the game's is where it
    /// is played. A quest's places do not: it is not one of the game's
    /// conditions, and their title names the room instead.
    /// </param>
    public static GameConditionGroupViewModel ForScript(string title, string by, string script,
                                                        IReadOnlyList<VanillaDialogueCatalog.Step> when,
                                                        IReadOnlyList<VanillaDialogueCatalog.GateLocation> gates,
                                                        bool canEdit, bool showLocation = true,
                                                        IReadOnlyList<VanillaDialogueCatalog.EarlierBranch>? ahead = null,
                                                        Func<bool>? isRoomOut = null, Action<bool>? setRoomOut = null)
    {
        var rows = new List<GameConditionRowViewModel>();
        if (showLocation)
        {
            // Where the script lives. On a quest's place it can be taken out,
            // and then the pack's rule for that place asks nothing about where
            // the player is; on a conversation it is only shown.
            foreach (var c in PlaceRules.RoomOf(by, script))
                rows.Add(isRoomOut != null && setRoomOut != null
                    ? GameConditionRowViewModel.Room(c, RoomNote, isRoomOut, setRoomOut, canEdit)
                    : GameConditionRowViewModel.Location(c, LevelNote));
        }

        int next = 0;
        foreach (var gate in gates ?? (IReadOnlyList<VanillaDialogueCatalog.GateLocation>)Array.Empty<VanillaDialogueCatalog.GateLocation>())
        {
            for (int i = 0; i < gate.Count && next < when.Count; i++, next++)
            {
                var step = when[next];
                int index = i;
                var at = gate.At;
                rows.Add(GameConditionRowViewModel.Removable(
                    step, ScriptNote,
                    () => GameConditionEditing.Editor?.IsRemoved(by, script, at, index) ?? false,
                    removed => GameConditionEditing.Editor?.SetRemoved(by, script, at, index, step.Type,
                                                                        step.Describe(), removed),
                    canEdit));
            }
        }
        for (; next < when.Count; next++)
        {
            var step = when[next];
            rows.Add(GameConditionRowViewModel.Fixed(VanillaDialogueConditions.Translate(step), step.Describe(),
                                                     PickedNote));
        }

        return new GameConditionGroupViewModel(title, EarlierNote(ahead), rows);
    }

    /// <summary>
    /// What the script tries before this, in a line - empty when nothing comes
    /// first. Game Creator runs the first branch of a list that runs and stops
    /// there, so taking every condition out of a branch still leaves it waiting
    /// behind the ones before it: the thing a pack author finds out when
    /// another conversation plays instead.
    /// </summary>
    internal static string EarlierNote(IReadOnlyList<VanillaDialogueCatalog.EarlierBranch>? ahead)
    {
        if (ahead == null || ahead.Count == 0) return "";
        var parts = ahead.Where(a => !string.IsNullOrEmpty(a.Plays))
                         .Select(a => a.Plays!.Substring(a.Plays!.LastIndexOf('/') + 1))
                         .Distinct(StringComparer.Ordinal)
                         .ToList();
        int others = ahead.Count(a => string.IsNullOrEmpty(a.Plays));
        if (others > 0) parts.Add(others == 1 ? "1 other branch" : others + " other branches");
        return "The game tries " + (parts.Count == 1 && others == 0 ? "this" : "these") + " first: "
               + JoinAnd(parts) + ". If one of them runs, this one doesn't.";
    }

    private static string JoinAnd(IReadOnlyList<string> parts)
        => parts.Count <= 1 ? string.Join("", parts)
           : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];

    /// <summary>
    /// One line's conditions: the game's, each removable, and any the pack
    /// added to the line on the Dialogues tab.
    /// </summary>
    public static GameConditionGroupViewModel? ForLine(string title, string dialogue, long nodeId, bool canEdit)
    {
        var node = VanillaDialogueCatalog.Open(dialogue)?.Node(nodeId);
        if (node == null) return null;
        var game = VanillaDialogueConditions.TranslateAll(node.Conditions, out _);

        var rows = new List<GameConditionRowViewModel>();
        for (int i = 0; i < game.Count; i++)
        {
            int index = i;
            rows.Add(GameConditionRowViewModel.Removable(
                game[i], LineNote,
                () => IsLineConditionRemoved(dialogue, nodeId, game, index),
                removed => SetLineConditionRemoved(dialogue, nodeId, game, index, removed),
                canEdit));
        }

        var now = GameConditionEditing.Editor?.LineConditions(dialogue, nodeId);
        if (now != null)
            foreach (var added in GameConditionEditing.Compare(game, now).Added)
                rows.Add(GameConditionRowViewModel.Yours(added,
                    "Added to this line in your version of the conversation. Change it on the Dialogues tab."));

        if (rows.Count == 0) return null;
        return new GameConditionGroupViewModel(title, "", rows);
    }

    private static bool IsLineConditionRemoved(string dialogue, long node, List<NodeConditionDef> game, int index)
    {
        var now = GameConditionEditing.Editor?.LineConditions(dialogue, node);
        return now != null && GameConditionEditing.Compare(game, now).Removed.Contains(index);
    }

    private static void SetLineConditionRemoved(string dialogue, long node, List<NodeConditionDef> game,
                                                int index, bool removed)
    {
        var editor = GameConditionEditing.Editor;
        var now = editor?.LineConditions(dialogue, node);
        if (editor == null || now == null) return;
        editor.SetLineConditions(dialogue, node, GameConditionEditing.WithGameCondition(game, now, index, removed));
    }
}
