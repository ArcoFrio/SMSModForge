using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.ViewModel;

/// <summary>
/// One task in a quest row's task list. The box shows <see cref="Name"/>, and
/// the search finds it by that too; the list row shows <see cref="Label"/>,
/// indented under its parent; what is stored is <see cref="Token"/> - which for
/// one of the game's tasks is an id nobody should have to read.
/// </summary>
public sealed record QuestTaskOption(string Token, string Name, string Label) : ISearchText
{
    public override string ToString() => Name;
    public string SearchText => Name;
}

/// <summary>
/// The part of a quest action or condition row that says WHICH quest and which
/// task: Source, Quest, Task, and a note on what the chosen task will do.
/// <para/>
/// Shared by the Quest action and both quest conditions, because they name a
/// quest the same way and a task list that followed its quest on one row but
/// not another would be a bug in whichever was written second. Reads and writes
/// the row's own params dictionary, so nothing here is state of its own.
/// </summary>
public sealed class QuestPickerViewModel : ObservableObject
{
    private readonly Dictionary<string, string> _params;
    private readonly Action _changed;

    /// <param name="offersWholeQuest">Whether the task list starts with "the
    /// quest itself" - a state condition can ask about either.</param>
    public QuestPickerViewModel(Dictionary<string, string> ps, Action changed, bool offersWholeQuest)
    {
        _params = ps;
        _changed = changed;
        OffersWholeQuest = offersWholeQuest;
    }

    /// <summary>
    /// The pack's quests, installed by MainViewModel - its own and its entries
    /// extending the game's, which change what a game quest's task list holds.
    /// A hook rather than a reference for the same reason as the variable
    /// lookup: a row is built from its own definition and has no route to the
    /// pack.
    /// </summary>
    internal static Func<IReadOnlyList<QuestDef>>? PackQuests;

    private static IReadOnlyList<QuestDef> Quests => PackQuests?.Invoke() ?? Array.Empty<QuestDef>();

    public bool OffersWholeQuest { get; }

    private bool _showsTask = true;

    /// <summary>
    /// Whether the Task row is on screen. The owner decides: the action hides it
    /// for the operations that act on the whole quest, where a task would be a
    /// field that does nothing.
    /// </summary>
    public bool ShowsTask
    {
        get => _showsTask;
        set { if (_showsTask == value) return; _showsTask = value; OnPropertyChanged(); }
    }

    // ── Source ───────────────────────────────────────────────────────

    /// <summary>The same two words a variable row uses for the same question.</summary>
    public static IReadOnlyList<string> Sources { get; } = new[] { "Pack", "Vanilla" };

    public bool IsVanilla => QuestReferences.IsVanilla(_params);

    public string Source
    {
        get => IsVanilla ? "Vanilla" : "Pack";
        set
        {
            bool vanilla = string.Equals(value, "Vanilla", StringComparison.OrdinalIgnoreCase);
            if (vanilla == IsVanilla) return;
            if (vanilla) _params[V.SourceParam] = V.Vanilla;
            else _params.Remove(V.SourceParam);

            // A pack quest's key means nothing among the game's quests, and a
            // game task's id means nothing among a pack quest's tasks. Kept,
            // either would sit in the box looking chosen.
            _params.Remove(V.QuestParam);
            _params.Remove(V.TaskParam);
            RaiseAll();
        }
    }

    // ── Quest ────────────────────────────────────────────────────────

    public string QuestKey
    {
        get => QuestReferences.Param(_params, V.QuestParam);
        set
        {
            value = (value ?? "").Trim();
            if (value == QuestKey) return;
            if (value.Length == 0) _params.Remove(V.QuestParam);
            else _params[V.QuestParam] = value;

            // The task follows its quest. One that the new quest also has - the
            // same key in two pack quests - is kept, since it is still a real
            // choice; anything else goes.
            if (TaskKey.Length > 0 && SelectedTask == null) _params.Remove(V.TaskParam);
            RaiseAll();
        }
    }

    /// <summary>The quests on the chosen side. Built on read: the pack's list
    /// changes on the Quests tab, and a list held from the first read would not.</summary>
    public IReadOnlyList<NavigatorTargetOption> QuestOptions
    {
        get
        {
            if (IsVanilla)
                return VanillaQuests.All
                    .OrderBy(q => q.PlainTitle, StringComparer.OrdinalIgnoreCase)
                    .Select(q => new NavigatorTargetOption(q.Name, QuestReferences.QuestLabel(null, true, q.Name)))
                    .ToList();

            return Quests
                .Where(q => !q.IsVanillaExtension)
                .OrderBy(q => q.Title, StringComparer.OrdinalIgnoreCase)
                .Select(q => new NavigatorTargetOption(q.Key, QuestReferences.QuestLabel(Quests, false, q.Key)))
                .ToList();
        }
    }

    /// <summary>What the named quest is called, or why it is not found. Empty
    /// while nothing is named.</summary>
    public string QuestNote
    {
        get
        {
            string quest = QuestKey;
            if (quest.Length == 0) return "";
            if (!QuestReferences.QuestExists(Quests, IsVanilla, quest))
                return IsVanilla ? "not one of the game's quests" : "no quest with this name on the Quests tab";
            string label = QuestReferences.QuestLabel(Quests, IsVanilla, quest);
            return label == quest ? "" : label;
        }
    }

    // ── Task ─────────────────────────────────────────────────────────

    /// <summary>What the whole-quest entry stores: nothing.</summary>
    public const string WholeQuestToken = "";

    public string TaskKey
    {
        get => QuestReferences.Param(_params, V.TaskParam);
        set
        {
            value = value ?? "";
            if (value == TaskKey) return;
            if (value.Length == 0) _params.Remove(V.TaskParam);
            else _params[V.TaskParam] = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TaskChoice));
            OnPropertyChanged(nameof(SelectedTask));
            OnPropertyChanged(nameof(TaskNote));
            _changed();
        }
    }

    /// <summary>
    /// The tasks of the chosen quest, indented by depth, as the journal lists
    /// them. Searched and shown by name; a game task is stored by id, and
    /// nobody should have to read one.
    /// </summary>
    public IReadOnlyList<QuestTaskOption> TaskOptions
    {
        get
        {
            var options = new List<QuestTaskOption>();
            if (OffersWholeQuest) options.Add(new QuestTaskOption(WholeQuestToken, "(the quest itself)", "(the quest itself)"));

            var tasks = QuestReferences.TasksOf(Quests, IsVanilla, QuestKey);
            if (tasks != null)
                foreach (var t in tasks)
                {
                    string name = string.IsNullOrWhiteSpace(t.Name) ? "(" + t.Token + ")" : t.Name;
                    options.Add(new QuestTaskOption(t.Token, name,
                        new string(' ', t.Depth * 4) + (t.Depth > 0 ? "▸ " : "") + name
                        + (t.Counts ? "  [counts]" : "")
                        + (t.Added ? "  [yours]" : "")
                        + (t.Removed ? "  [taken out]" : "")));
                }

            // A stored task this list does not have still shows as itself, so
            // opening a row never looks like it lost its value.
            string stored = TaskKey;
            if (stored.Length > 0 && options.All(o => o.Token != stored))
                options.Add(new QuestTaskOption(stored, stored, stored + "  (not found)"));
            return options;
        }
    }

    /// <summary>
    /// What the task list's SelectedValue is bound to. The same as
    /// <see cref="TaskKey"/>, except that the empty write a search makes -
    /// typing something that is not a task clears the selection - is ignored,
    /// so searching never loses the task already chosen. Choosing "the quest
    /// itself" writes an empty string, not nothing, and still clears it.
    /// </summary>
    public string? TaskChoice
    {
        get => TaskKey;
        set { if (value != null) TaskKey = value; }
    }

    public QuestTaskInfo? SelectedTask
        => TaskKey.Length == 0 ? null : QuestReferences.Task(Quests, IsVanilla, QuestKey, TaskKey);

    /// <summary>What the game will do with the chosen task that its name does
    /// not say.</summary>
    public string TaskNote => QuestReferences.Describe(SelectedTask);

    /// <summary>Re-read everything. Called when the row's type or operation
    /// changes, and when the pack's quests may have.</summary>
    public void RaiseAll()
    {
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(IsVanilla));
        OnPropertyChanged(nameof(QuestKey));
        OnPropertyChanged(nameof(QuestOptions));
        OnPropertyChanged(nameof(QuestNote));
        OnPropertyChanged(nameof(TaskKey));
        OnPropertyChanged(nameof(TaskOptions));
        OnPropertyChanged(nameof(TaskChoice));
        OnPropertyChanged(nameof(SelectedTask));
        OnPropertyChanged(nameof(TaskNote));
        _changed();
    }
}
