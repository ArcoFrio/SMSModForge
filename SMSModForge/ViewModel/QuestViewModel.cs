using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using SMSModForge.Model;
using V = SMSModForge.Shared.QuestVocabulary;

namespace SMSModForge.ViewModel;

/// <summary>
/// A quest on the Quests tab: its title and description, and its tasks as the
/// journal will list them.
/// <para/>
/// The tasks are shown as ONE flat, indented list rather than a tree control,
/// because that is how the journal shows them and how an author reads them:
/// top to bottom, subtasks under their task. The list is rebuilt from the model
/// after every structural change, so the model's nesting is the only truth.
/// </summary>
public sealed class QuestViewModel : ObservableObject
{
    public QuestDef Model { get; }

    public QuestViewModel(QuestDef model)
    {
        Model = model;
        // Polled: the runtime asks every frame until the quest starts.
        StartConditions = new ConditionListViewModel(model.StartConditions, ConditionContext.Polled);
        StartConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConditionListViewModel.Count)) OnPropertyChanged(nameof(StartNote));
        };
        AddTaskCommand = new RelayCommand(AddTask);
        AddSubtaskCommand = new RelayCommand(AddSubtask, () => SelectedTask != null);
        RemoveTaskCommand = new RelayCommand(RemoveTask, () => SelectedTask != null);
        MoveTaskUpCommand = new RelayCommand(() => MoveTask(-1), () => CanMove(-1));
        MoveTaskDownCommand = new RelayCommand(() => MoveTask(+1), () => CanMove(+1));
        RebuildTaskRows();
    }

    // ── Runtime name, derived ────────────────────────────────────────
    //
    // See DerivedKey. A quest's key matters more than most: the player's saved
    // progress is filed under an id made from it, so a quest loaded from disk
    // never re-derives, and renaming one is a deliberate act with a warning.

    private readonly DerivedKey _derivedKey = new();

    public bool KeyIsDerived => _derivedKey.IsDerived;

    public void DeriveKeyFromTitle(Func<IEnumerable<string>> siblingKeys) => _derivedKey.Follow(siblingKeys);

    /// <summary>
    /// The key moved because the title did (old, new). Rows that already name
    /// the quest follow it: a quest whose key is still following its title has
    /// never been released under that key, so nobody's progress is filed under it.
    /// </summary>
    public event Action<string, string>? KeyDerived;

    /// <summary>A task's key moved, by following its name or by a rename
    /// (old, new).</summary>
    public event Action<string, string>? TaskKeyChanged;

    internal void OnTaskKeyChanged(string oldKey, string newKey) => TaskKeyChanged?.Invoke(oldKey, newKey);

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            _derivedKey.Stop();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value) return;
            Model.Title = value ?? "";
            if (_derivedKey.Next(QuestKeys.Short(value, "Quest"), Model.Key) is { } derived && derived != Model.Key)
            {
                string old = Model.Key;
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
                KeyDerived?.Invoke(old, derived);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description == value) return;
            Model.Description = value ?? "";
            OnPropertyChanged();
        }
    }

    public string Display => string.IsNullOrWhiteSpace(Title) ? Key : $"{QuestKeys.Plain(Title)} ({Key})";

    // ── Starting ─────────────────────────────────────────────────────

    /// <summary>When these all pass, the quest starts by itself.</summary>
    public ConditionListViewModel StartConditions { get; }

    /// <summary>What the start list means as it stands - an empty one does
    /// not start the quest, which is not what an empty list means elsewhere.</summary>
    public string StartNote => StartConditions.Count == 0
        ? "No start conditions: the quest only starts when a Quest action starts it."
        : "The quest starts by itself the moment all of these pass. A Quest action can still start it sooner.";

    // ── Tasks ────────────────────────────────────────────────────────

    /// <summary>Every task, depth first, indented - the journal's order.</summary>
    public ObservableCollection<QuestTaskViewModel> TaskRows { get; } = new();

    private QuestTaskViewModel? _selectedTask;
    public QuestTaskViewModel? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (ReferenceEquals(_selectedTask, value)) return;
            _selectedTask = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTask));
            RaiseTaskCommands();
        }
    }

    public bool HasSelectedTask => SelectedTask != null;

    public RelayCommand AddTaskCommand { get; }
    public RelayCommand AddSubtaskCommand { get; }
    public RelayCommand RemoveTaskCommand { get; }
    public RelayCommand MoveTaskUpCommand { get; }
    public RelayCommand MoveTaskDownCommand { get; }

    /// <summary>Every task key in this quest, for keeping a new one unique -
    /// unique across the QUEST, since that is what its id is scoped to.</summary>
    internal IEnumerable<string> AllTaskKeys => Model.AllTasks().Select(t => t.Key);

    /// <summary>A new top-level task, at the end: the journal's next step.</summary>
    private void AddTask() => Add(Model.Tasks);

    /// <summary>A new subtask, as the last one under the selected task.</summary>
    private void AddSubtask()
    {
        if (SelectedTask == null) return;
        Add(SelectedTask.Model.Subtasks);
    }

    private void Add(List<QuestTaskDef> into)
    {
        var def = new QuestTaskDef
        {
            Key = CharacterDef.UniqueIdentifier("Task" + (Model.AllTasks().Count() + 1), AllTaskKeys),
            Name = "",
        };
        into.Add(def);
        RebuildTaskRows();
        var row = TaskRows.First(r => ReferenceEquals(r.Model, def));
        row.DeriveKeyFromName(() => AllTaskKeys);
        SelectedTask = row;
        OnPropertyChanged(nameof(Model));
    }

    private void RemoveTask()
    {
        var row = SelectedTask;
        if (row == null) return;
        var siblings = ParentListOf(row.Model);
        if (siblings == null) return;

        int index = siblings.IndexOf(row.Model);
        siblings.Remove(row.Model);
        RebuildTaskRows();

        // Land on a neighbour rather than on nothing, so removing several in a
        // row is several clicks rather than a click and a hunt.
        SelectedTask = siblings.Count == 0
            ? null
            : TaskRows.FirstOrDefault(r => ReferenceEquals(r.Model, siblings[Math.Min(index, siblings.Count - 1)]));
    }

    private bool CanMove(int step)
    {
        if (SelectedTask == null) return false;
        var siblings = ParentListOf(SelectedTask.Model);
        if (siblings == null) return false;
        int at = siblings.IndexOf(SelectedTask.Model) + step;
        return at >= 0 && at < siblings.Count;
    }

    /// <summary>Among its siblings only: moving a task under a different
    /// parent is a different edit, and one click should not do it.</summary>
    private void MoveTask(int step)
    {
        if (!CanMove(step)) return;
        var task = SelectedTask!.Model;
        var siblings = ParentListOf(task)!;
        int from = siblings.IndexOf(task);
        siblings.RemoveAt(from);
        siblings.Insert(from + step, task);
        RebuildTaskRows();
        SelectedTask = TaskRows.FirstOrDefault(r => ReferenceEquals(r.Model, task));
    }

    /// <summary>The list a task sits in: the quest's own, or its parent's.</summary>
    internal List<QuestTaskDef>? ParentListOf(QuestTaskDef task)
    {
        if (Model.Tasks.Contains(task)) return Model.Tasks;
        foreach (var t in Model.AllTasks())
            if (t.Subtasks.Contains(task)) return t.Subtasks;
        return null;
    }

    /// <summary>Re-flatten the tasks from the model, keeping each row's view
    /// model (and so its key-following state) for tasks that are still there.</summary>
    public void RebuildTaskRows()
    {
        var existing = TaskRows.ToDictionary(r => r.Model, r => r, ReferenceComparer.Instance);
        TaskRows.Clear();

        void Walk(List<QuestTaskDef> tasks, int depth)
        {
            foreach (var t in tasks)
            {
                if (!existing.TryGetValue(t, out var row)) row = new QuestTaskViewModel(t, this);
                row.Depth = depth;
                TaskRows.Add(row);
                Walk(t.Subtasks, depth + 1);
            }
        }
        Walk(Model.Tasks, 0);

        foreach (var row in TaskRows) row.RefreshStructure();
        if (_selectedTask != null && !TaskRows.Contains(_selectedTask)) SelectedTask = null;
        OnPropertyChanged(nameof(TaskCount));
        RaiseTaskCommands();
    }

    public int TaskCount => TaskRows.Count;

    private void RaiseTaskCommands()
    {
        AddSubtaskCommand.Raise();
        RemoveTaskCommand.Raise();
        MoveTaskUpCommand.Raise();
        MoveTaskDownCommand.Raise();
    }

    private sealed class ReferenceComparer : IEqualityComparer<QuestTaskDef>
    {
        public static readonly ReferenceComparer Instance = new();
        public bool Equals(QuestTaskDef? x, QuestTaskDef? y) => ReferenceEquals(x, y);
        public int GetHashCode(QuestTaskDef obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>One row of a quest's task list.</summary>
public sealed class QuestTaskViewModel : ObservableObject
{
    public QuestTaskDef Model { get; }
    private readonly QuestViewModel _quest;

    public QuestTaskViewModel(QuestTaskDef model, QuestViewModel quest)
    {
        Model = model;
        _quest = quest;
        // Polled: the runtime asks every frame while the task is in progress.
        CompletionConditions = new ConditionListViewModel(model.Conditions, ConditionContext.Polled);
        CompletionActions = new ActionListViewModel(model.Actions);
        CompletionConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConditionListViewModel.Count)) OnPropertyChanged(nameof(CompletionNote));
        };
    }

    // ── Completing on its own ────────────────────────────────────────

    /// <summary>When these all pass while the task is in progress, it
    /// completes. Only offered while the task has no subtasks.</summary>
    public ConditionListViewModel CompletionConditions { get; }

    /// <summary>Run when the task completes, however it was completed.</summary>
    public ActionListViewModel CompletionActions { get; }

    /// <summary>
    /// Whether this task can carry conditions and actions of its own. A task
    /// with subtasks finishes through them, so the runtime does not check
    /// its own list and the editor does not offer one.
    /// </summary>
    public bool HasOwnCompletion => !HasSubtasks;

    /// <summary>Said where the lists would be, on a task with subtasks.</summary>
    public string NoOwnCompletionNote
        => "This task has subtasks, so it finishes through them and has no completion conditions or actions "
           + "of its own - give those to its subtasks instead."
           + (Model.Conditions.Count + Model.Actions.Count > 0
               ? " The ones it had before it gained subtasks are kept, but not used while it has them."
               : "");

    private readonly DerivedKey _derivedKey = new();

    public void DeriveKeyFromName(Func<IEnumerable<string>> siblingKeys) => _derivedKey.Follow(siblingKeys);

    public bool KeyIsDerived => _derivedKey.IsDerived;

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            string old = Model.Key;
            Model.Key = value;
            _derivedKey.Stop();
            OnPropertyChanged();
            _quest.OnTaskKeyChanged(old, value);
        }
    }

    /// <summary>The line the journal shows.</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value ?? "";
            if (_derivedKey.Next(QuestKeys.Short(value, "Task"), Model.Key) is { } derived && derived != Model.Key)
            {
                string old = Model.Key;
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
                _quest.OnTaskKeyChanged(old, derived);
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowText));
        }
    }

    // ── Where it sits ────────────────────────────────────────────────

    private int _depth;
    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Indent));
            OnPropertyChanged(nameof(IsTopLevel));
            OnPropertyChanged(nameof(CounterNote));
            OnPropertyChanged(nameof(RowText));
        }
    }

    public bool IsTopLevel => Depth == 0;

    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    public bool HasSubtasks => Model.Subtasks.Count > 0;

    /// <summary>What the list row says: the journal's line, or a placeholder
    /// that reads as unfinished rather than as blank.</summary>
    public string RowText
        => (IsTopLevel ? "" : "▸ ") + (string.IsNullOrWhiteSpace(Name) ? "(no text)" : Name)
           + (Counts ? "  (0/" + CountToText + ")" : "");

    /// <summary>Called by the quest after the list is rebuilt: subtasks may
    /// have come or gone under this task.</summary>
    internal void RefreshStructure()
    {
        OnPropertyChanged(nameof(HasSubtasks));
        OnPropertyChanged(nameof(HasOwnCompletion));
        OnPropertyChanged(nameof(NoOwnCompletionNote));
        OnPropertyChanged(nameof(CompletionNote));
        OnPropertyChanged(nameof(RowText));
    }

    // ── How it completes ─────────────────────────────────────────────

    public static IReadOnlyList<string> Completions => V.Completions;

    public string Completion
    {
        get => string.IsNullOrEmpty(Model.Completion) ? V.InOrder : Model.Completion;
        set
        {
            if (Completion == value) return;
            Model.Completion = value ?? V.InOrder;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CompletionNote));
        }
    }

    /// <summary>What completes this task, said the way the game does it.</summary>
    public string CompletionNote
    {
        get
        {
            if (!HasSubtasks)
                return CompletionConditions.Count > 0
                    ? "Completes by itself when its conditions below pass. A Quest action, or its counter reaching its target, can still complete it."
                    : "No completion conditions yet: only a Quest action, or its counter reaching its target, completes it.";
            if (V.Is(Completion, V.ByAction))
                return "Completed only by a Quest action. Its subtasks never start, so they read as notes under it.";
            // At the top level or under an in-order parent, the game lets an
            // action complete a task outright whatever its subtasks say - the
            // game's own quests do it. Anywhere else the subtasks are checked.
            string early = ActionCanCompleteEarly
                ? " A Quest action can also complete it before then."
                : "";
            if (V.Is(Completion, V.AnyOne))
                return "Starts all its subtasks at once, and completes itself when any one of them is done." + early;
            if (V.Is(Completion, V.AnyOrder))
                return "Starts all its subtasks at once, and completes itself when all of them are done." + early;
            return "Starts its subtasks one at a time, and completes itself when the last one is done." + early;
        }
    }

    /// <summary>Whether it sits where the game completes a task on request
    /// regardless of its subtasks: the top level, or under an in-order task.</summary>
    private bool ActionCanCompleteEarly
    {
        get
        {
            var parent = _quest.Model.AllTasks().FirstOrDefault(t => t.Subtasks.Contains(Model));
            return parent == null || V.Is(parent.Completion, V.InOrder);
        }
    }

    // ── Counter ──────────────────────────────────────────────────────

    public bool Counts
    {
        get => Model.CountTo is > 0;
        set
        {
            if (value == Counts) return;
            Model.CountTo = value ? 3 : null;
            _countToText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountToText));
            OnPropertyChanged(nameof(CounterNote));
            OnPropertyChanged(nameof(RowText));
            OnPropertyChanged(nameof(CountsFromVariable));
        }
    }

    private string? _countToText;

    /// <summary>The target as typed. Kept as text so a half-typed number is not
    /// reformatted under the cursor; the model follows whenever it parses.</summary>
    public string CountToText
    {
        get => _countToText ??= Model.CountTo is > 0
            ? Model.CountTo.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : "";
        set
        {
            _countToText = value ?? "";
            if (double.TryParse(_countToText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0)
                Model.CountTo = n;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowText));
        }
    }

    // ── Where the count comes from ───────────────────────────────────

    public const string CountedByAction = "Quest action";
    public const string CountedByVariable = "Variable";

    public static IReadOnlyList<string> CounterSources { get; } = new[] { CountedByAction, CountedByVariable };

    /// <summary>What sets the count: Quest actions, or a number variable it
    /// follows. Kept when the counter is switched off and on again.</summary>
    public string CounterSource
    {
        get => V.Is(Model.CountFrom, V.CountFromVariable) ? CountedByVariable : CountedByAction;
        set
        {
            if (value == CounterSource) return;
            Model.CountFrom = value == CountedByVariable ? V.CountFromVariable : "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountsFromVariable));
            OnPropertyChanged(nameof(CounterNote));
        }
    }

    /// <summary>Whether the variable row is on screen.</summary>
    public bool CountsFromVariable => Model.CountsFromVariable;

    /// <summary>The same two words a variable condition uses.</summary>
    public static IReadOnlyList<string> CountVariableSources => NodeConditionViewModel.VariableSources;

    public string CountVariableSource
    {
        get => Model.CountVariableIsVanilla ? "Vanilla" : "Pack";
        set
        {
            bool vanilla = string.Equals(value, "Vanilla", StringComparison.OrdinalIgnoreCase);
            if (vanilla == Model.CountVariableIsVanilla) return;
            Model.CountSource = vanilla ? V.Vanilla : "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCountVariableVanilla));
            OnPropertyChanged(nameof(CountVariableKindNote));
        }
    }

    public bool IsCountVariableVanilla => Model.CountVariableIsVanilla;

    public string CountVariable
    {
        get => Model.CountVariable;
        set
        {
            value = (value ?? "").Trim();
            if (value == Model.CountVariable) return;
            Model.CountVariable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CountVariableKindNote));
        }
    }

    /// <summary>What the named variable holds, in the words the variable
    /// rows use - so a counter pointed at a yes/no says so beside the name.</summary>
    public string CountVariableKindNote => VariableTypes.Label(
        NodeConditionViewModel.VariableKindLookup?.Invoke(Model.CountVariable ?? "", Model.CountVariableIsVanilla)
        ?? VariableKind.Unknown);

    public string CounterNote
    {
        get
        {
            if (!Counts) return "";
            string how = CountsFromVariable
                ? "Follows the variable while the task is in progress, and completes the task when the variable reaches the target. A Quest action setting the count is overwritten."
                : "Set or add to it with a Quest action. The task completes itself when the count reaches the target.";
            return IsTopLevel
                ? how
                : how + " The journal only draws a count beside a top-level task, so the player will not see this one.";
        }
    }
}

/// <summary>How a quest's or task's runtime name is made from what the author
/// typed.</summary>
internal static class QuestKeys
{
    /// <summary>
    /// The first few words, not the whole line. A task's name is a sentence,
    /// and a key made of all of it is a key nobody wants to read in a log.
    /// </summary>
    public static string Short(string? text, string whenEmpty)
    {
        var words = Plain(text).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        // A name with nothing in it would otherwise fall through to the
        // character fallback, and a task called "Character" is a puzzle.
        return words.Length == 0 ? whenEmpty : string.Join(" ", words.Take(4));
    }

    /// <summary>Rich-text tags removed.</summary>
    public static string Plain(string? text) => VanillaQuests.StripTags(text ?? "");
}
