using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using SMSModForge.Model;
using SMSModForge.Shared;
using V = SMSModForge.Shared.QuestVocabulary;
using SMSModForge.Localization;

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
public sealed class QuestViewModel : ObservableObject, ISiteConditionsOwner
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
        // Polled too: asked every frame while the quest has started.
        ResetConditions = new ConditionListViewModel(model.ResetConditions, ConditionContext.Polled);
        ResetConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ConditionListViewModel.Count)) return;
            OnPropertyChanged(nameof(ResetNote));
            OnPropertyChanged(nameof(StartNote));
            OnPropertyChanged(nameof(ChangeSummary));
            OnPropertyChanged(nameof(HasChanges));
        };
        ResetDescriptionCommand = new RelayCommand(() => Description = "", () => Model.Description.Length > 0);
        ResetAllCommand = new RelayCommand(ResetAll, () => IsVanillaExtension && HasChanges);
        AddTaskCommand = new RelayCommand(() => { if (IsVanillaExtension) AddExtensionTask(); else AddTask(); },
                                          () => !ShowsVanillaPanel || IsVanillaExtension);
        AddSubtaskCommand = new RelayCommand(() => { if (IsVanillaExtension) AddExtensionSubtask(); else AddSubtask(); },
                                             () => IsVanillaExtension ? CanAddExtensionSubtask(NodeOf(_selectedExtensionRow)) : SelectedTask != null);
        RemoveTaskCommand = new RelayCommand(() => { if (IsVanillaExtension) RemoveExtensionRow(); else RemoveTask(); },
                                             () => IsVanillaExtension ? CanRemoveExtensionRow() : SelectedTask != null);
        MoveTaskUpCommand = new RelayCommand(() => { if (IsVanillaExtension) MoveExtension(-1); else MoveTask(-1); },
                                             () => IsVanillaExtension ? CanMoveExtension(-1) : CanMove(-1));
        MoveTaskDownCommand = new RelayCommand(() => { if (IsVanillaExtension) MoveExtension(+1); else MoveTask(+1); },
                                               () => IsVanillaExtension ? CanMoveExtension(+1) : CanMove(+1));
        RebuildTaskRows();
        if (Model.IsVanillaExtension)
        {
            // An entry saved about one of the game's quests stays one while it
            // is open, even if its quest is cleared to pick another.
            _wantsVanilla = true;
            RebuildVanillaRows();
        }
        StartConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ConditionListViewModel.Count)) return;
            OnPropertyChanged(nameof(ChangeSummary));
            OnPropertyChanged(nameof(HasChanges));
        };
    }

    /// <summary>A task list as an author reads it, for tests and logs: one
    /// line per row, indented, marked.</summary>
    internal string DescribeExtensionRows()
        => string.Join("\n", ExtensionRows.Select(r => r switch
        {
            VanillaTaskRowViewModel g => new string(' ', g.Depth * 2) + "game " + g.Token + (g.IsRemoved ? " removed" : ""),
            QuestTaskViewModel t => new string(' ', t.Depth * 2) + "pack " + t.Key,
            _ => "?",
        }));

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
            OnPropertyChanged(nameof(ChangeSummary));
            OnPropertyChanged(nameof(HasChanges));
            ResetDescriptionCommand?.Raise();
        }
    }

    /// <summary>Back to the game's own paragraph.</summary>
    public RelayCommand ResetDescriptionCommand { get; }

    // ── Players who had already finished it ──────────────────────────
    //
    // Three buttons rather than a list: the choice is between three things
    // that happen to a player, and each is worth a line saying what it does.

    public bool StepsAddedLeaveFinished
    {
        get => QuestGrowth.ChoiceOf(Model.WhenStepsAdded) == QuestGrowth.LeaveFinished;
        set { if (value) SetWhenStepsAdded(QuestGrowth.LeaveFinished); }
    }

    public bool StepsAddedReopen
    {
        get => QuestGrowth.ChoiceOf(Model.WhenStepsAdded) == QuestGrowth.Reopen;
        set { if (value) SetWhenStepsAdded(QuestGrowth.Reopen); }
    }

    public bool StepsAddedStartOver
    {
        get => QuestGrowth.ChoiceOf(Model.WhenStepsAdded) == QuestGrowth.StartOver;
        set { if (value) SetWhenStepsAdded(QuestGrowth.StartOver); }
    }

    private void SetWhenStepsAdded(string choice)
    {
        if (QuestGrowth.ChoiceOf(Model.WhenStepsAdded) == choice) return;
        Model.WhenStepsAdded = choice;
        OnPropertyChanged(nameof(StepsAddedLeaveFinished));
        OnPropertyChanged(nameof(StepsAddedReopen));
        OnPropertyChanged(nameof(StepsAddedStartOver));
    }

    public string Display => IsVanillaExtension
        ? Loc.F("quests.gamesQuest", "quest", TheGamesTitle.Length > 0 ? TheGamesTitle : Model.Source)
        : string.IsNullOrWhiteSpace(Title) ? Key : $"{QuestKeys.Plain(Title)} ({Key})";

    // ── One of the game's own quests, extended ───────────────────────
    //
    // An extension writes nothing into the journal of its own. The quest, its
    // tasks and what finishes them stay the game's; the pack only says what to
    // show as the player moves through it, and what to do when they do.

    private bool _wantsVanilla;

    /// <summary>
    /// Started from "+ Vanilla" and not pointed at one of the game's quests
    /// yet. Not in the manifest: a saved entry names a quest or it is one of
    /// the pack's own, and this is only the state between the two.
    /// </summary>
    public bool WantsVanilla
    {
        get => _wantsVanilla;
        set
        {
            if (_wantsVanilla == value) return;
            _wantsVanilla = value;
            RaiseKind();
        }
    }

    public bool IsVanillaExtension => Model.IsVanillaExtension;

    /// <summary>Whether the panel for one of the game's quests is the one to
    /// show - including before a quest has been chosen.</summary>
    public bool ShowsVanillaPanel => IsVanillaExtension || WantsVanilla;

    /// <summary>The panel for a quest of the pack's own.</summary>
    public bool ShowsOwnPanel => !ShowsVanillaPanel;

    private void RaiseKind()
    {
        OnPropertyChanged(nameof(IsVanillaExtension));
        OnPropertyChanged(nameof(ShowsVanillaPanel));
        OnPropertyChanged(nameof(ShowsOwnPanel));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(TheGamesTitle));
        OnPropertyChanged(nameof(TheGamesDescription));
        OnPropertyChanged(nameof(SourceNote));
        OnPropertyChanged(nameof(DescriptionNote));
        OnPropertyChanged(nameof(StartNote));
        OnPropertyChanged(nameof(HasNoVanillaTasks));
        OnPropertyChanged(nameof(ChangesTheGamesTasks));
        OnPropertyChanged(nameof(VanillaListName));
        OnPropertyChanged(nameof(ChangeSummary));
        _gameSites = null;
        OnPropertyChanged(nameof(GameSites));
        OnPropertyChanged(nameof(GameSitesNote));
        OnPropertyChanged(nameof(HasGameSitesNote));
        OnPropertyChanged(nameof(HasChanges));
        RaiseWarning();
        RaiseTaskCommands();
    }

    private IReadOnlyList<GameQuestSiteGroup>? _gameSites;

    /// <summary>What the game itself does with its quest - what starts it,
    /// what puts it back - beside what the pack does. The places that start or
    /// reset it can be changed here.</summary>
    public IReadOnlyList<GameQuestSiteGroup> GameSites
        => _gameSites ??= IsVanillaExtension
            ? GameQuestSites.ForQuest(Model.Source, this)
            : Array.Empty<GameQuestSiteGroup>();

    // -- The pack's conditions at the game's places -------------------

    SiteConditionsDef ISiteConditionsOwner.ConditionsFor(SiteConditionsDef place)
        => Model.SiteConditions.FirstOrDefault(s => s.Key == place.Key) ?? place;

    void ISiteConditionsOwner.SiteConditionsChanged(SiteConditionsDef place)
    {
        // In the entry only while it holds anything, so opening a place and
        // leaving it alone writes nothing.
        bool holds = place.Conditions.Count > 0 || place.RoomsOut.Count > 0;
        bool kept = Model.SiteConditions.Contains(place);
        if (holds && !kept) Model.SiteConditions.Add(place);
        else if (!holds && kept) Model.SiteConditions.Remove(place);
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(HasChanges));
        ResetAllCommand?.Raise();
    }

    /// <summary>The game's conditions changed - here, on another quest, or on
    /// the Dialogues tab - so every place shown says so.</summary>
    internal void RefreshGameConditions()
    {
        if (_gameSites != null)
            foreach (var group in _gameSites) group.Refresh();
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(HasChanges));
        RaiseWarning();
        ResetAllCommand?.Raise();
    }

    private void RaiseWarning()
    {
        OnPropertyChanged(nameof(TakesGameConditionsOut));
        OnPropertyChanged(nameof(WarnsOnLoad));
        OnPropertyChanged(nameof(LoadWarningText));
    }

    /// <summary>The lists of the pack's conditions at the game's places, for
    /// the places already on screen.</summary>
    internal IEnumerable<ConditionListViewModel> OpenSiteConditions
        => (_gameSites ?? Array.Empty<GameQuestSiteGroup>())
           .SelectMany(g => g.Sites)
           .Select(s => s.ExtraConditions)
           .Where(l => l != null)!;

    /// <summary>Whether a condition is taken out of any of the game's places
    /// that start or reset this quest - a change to what plays a conversation,
    /// or to a line of one.</summary>
    public bool TakesGameConditionsOut
        => IsVanillaExtension
           && GameSites.SelectMany(g => g.Sites)
                       .Any(s => s.IsEditable && s.ConditionGroups.Any(c => c.RemovedCount > 0));

    /// <summary>Whether a player loading a save already under way is warned
    /// about this entry.</summary>
    public bool WarnsOnLoad => ChangesTheGamesTasks || TakesGameConditionsOut;

    public string LoadWarningText
    {
        get
        {
            var what = new List<string>();
            if (Model.AddedTasks.Count > 0) what.Add(Loc.T("quests.loadWarning.addsTasks"));
            if (Model.VanillaTasks.Any(h => h.Removed)) what.Add(Loc.T("quests.loadWarning.takesTasksOut"));
            if (TakesGameConditionsOut) what.Add(Loc.T("quests.loadWarning.takesConditionsOut"));
            if (what.Count == 0) return "";
            bool tasks = Model.AddedTasks.Count > 0 || Model.VanillaTasks.Any(h => h.Removed);
            return Loc.F(tasks ? "quests.loadWarning.withQuest" : "quests.loadWarning", "what", Loc.JoinAnd(what));
        }
    }

    /// <summary>How many of the game's places that start or reset this quest
    /// the pack changes.</summary>
    private int ChangedPlaces
        => (_gameSites ?? (IsVanillaExtension ? GameSites : Array.Empty<GameQuestSiteGroup>()))
           .SelectMany(g => g.Sites).Count(s => s.IsEditable && s.IsChanged);

    public string GameSitesNote
        => !IsVanillaExtension
            ? ""
            : string.Join(" ", new[] { GameQuestSites.QuestNote(Model.Source), GameQuestSites.CoverageNote }
                                   .Where(s => s.Length > 0));

    public bool HasGameSitesNote => GameSitesNote.Length > 0;

    /// <summary>The quest as the pack leaves it, one node per task, in order.</summary>
    internal IReadOnlyList<ExtensionTaskNode> Nodes => _nodes;

    /// <summary>When one of the pack's tasks in the game's quest starts. Empty
    /// for a quest of the pack's own.</summary>
    internal string StartsNoteFor(QuestTaskDef task)
    {
        if (!IsVanillaExtension) return "";
        var node = _nodes.FirstOrDefault(n => ReferenceEquals(n.Added, task));
        return node == null ? "" : TaskStarts.For(node, _nodes);
    }

    /// <summary>What the vanilla list calls this entry: the journal's title
    /// for the game's quest, the name as typed when the game has none by it,
    /// or a prompt while none is chosen.</summary>
    public string VanillaListName
        => TheGamesTitle.Length > 0 ? TheGamesTitle
         : Model.Source.Length > 0 ? Model.Source
         : Loc.T("quests.chooseGamesQuest");

    /// <summary>How much of the game's quest this entry changes, for the
    /// vanilla list: what an author asks of it without opening it.</summary>
    public string ChangeSummary
    {
        get
        {
            if (!ShowsVanillaPanel || Model.Source.Length == 0) return "";
            int added = Model.AddedTasks.Sum(a => a.SelfAndDescendants().Count());
            int takenOut = Model.VanillaTasks.Count(h => h.Removed);
            int other = Model.VanillaTasks.Count(h => h.DoesAnything && !h.Removed)
                        + (Model.Description.Length > 0 ? 1 : 0)
                        + (Model.StartConditions.Count > 0 ? 1 : 0)
                        + (Model.ResetConditions.Count > 0 ? 1 : 0)
                        + ChangedPlaces;

            var parts = new List<string>();
            if (added > 0) parts.Add(Loc.P("quests.summary.added", added));
            if (takenOut > 0) parts.Add(Loc.P("quests.summary.takenOut", takenOut));
            if (other > 0) parts.Add(Loc.P("quests.summary.other", other));
            return parts.Count == 0 ? Loc.T("quests.summary.unchanged") : Loc.JoinList(parts);
        }
    }

    /// <summary>Whether the entry changes anything of the game's quest.</summary>
    public bool HasChanges => ShowsVanillaPanel && Model.Source.Length > 0 && ChangeSummary != "unchanged";

    /// <summary>
    /// Put the game's quest back the way the game has it: its description,
    /// its tasks, the pack's tasks in it, the pack's start and reset
    /// conditions, and the game's places that start or reset it - including
    /// any condition taken out of them, which the Dialogues tab shows too.
    /// </summary>
    public RelayCommand ResetAllCommand { get; }

    internal void ResetAll()
    {
        if (!IsVanillaExtension) return;
        foreach (var site in GameSites.SelectMany(g => g.Sites).Where(s => s.IsEditable && s.IsChanged).ToList())
            site.ResetCommand.Execute(null);
        Model.SiteConditions.Clear();

        while (StartConditions.Count > 0) StartConditions.Remove(StartConditions.Items[0]);
        while (ResetConditions.Count > 0) ResetConditions.Remove(ResetConditions.Items[0]);
        Description = "";
        Model.VanillaTasks.Clear();
        Model.AddedTasks.Clear();
        SelectedTask = null;
        RebuildExtensionRows();
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(HasChanges));
        ResetAllCommand.Raise();
    }

    /// <summary>The game's quests, by the name their own instructions use.</summary>
    public static IReadOnlyList<NavigatorTargetOption> VanillaQuestOptions { get; } =
        VanillaQuests.All
            .OrderBy(q => q.PlainTitle, StringComparer.OrdinalIgnoreCase)
            .Select(q => new NavigatorTargetOption(q.Name, QuestReferences.QuestLabel(null, true, q.Name)))
            .ToList();

    /// <summary>Which of the game's quests this entry is about.</summary>
    public string Source
    {
        get => Model.Source;
        set
        {
            value = (value ?? "").Trim();
            if (Model.Source == value) return;
            Model.Source = value;

            // What the pack said about the tasks of the quest it named BEFORE
            // cannot mean anything on a different one - a task id belongs to
            // one quest. Anything the new quest also has is kept.
            var tasks = QuestReferences.TasksOf(null, vanilla: true, value);
            if (tasks != null)
            {
                bool Has(string token) => tasks.Any(t => string.Equals(t.Token, token, StringComparison.Ordinal));
                Model.VanillaTasks.RemoveAll(h => !Has(h.Task));

                // The pack's added tasks are the author's work and are kept -
                // but one placed under or before a task the new quest does not
                // have is moved to the end of the list, where it can be seen.
                foreach (var added in Model.AddedTasks)
                {
                    if (!added.IsTopLevel && !Has(added.Under)) { added.Under = ""; added.Before = ""; }
                    if (added.Before.Length > 0 && !Has(added.Before)) added.Before = "";
                }
            }

            // The key is the editor's handle on this entry, not something the
            // game reads, so it follows the quest that was picked while it is
            // still deriving.
            if (_derivedKey.Next(QuestKeys.Short(TheGamesQuest?.PlainTitle ?? value, "Quest"), Model.Key) is { } derived
                && derived != Model.Key)
            {
                string old = Model.Key;
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
                KeyDerived?.Invoke(old, derived);
            }

            RebuildVanillaRows();
            OnPropertyChanged();
            RaiseKind();
        }
    }

    /// <summary>The game's quest this extends, or null while none is named.</summary>
    public VanillaQuests.VanillaQuest? TheGamesQuest => VanillaQuests.Find(Model.Source);

    /// <summary>What the journal calls it, without the tags the journal paints.</summary>
    public string TheGamesTitle => TheGamesQuest?.PlainTitle ?? "";

    /// <summary>The paragraph the game shows under that title - what an
    /// override replaces, shown so an author can see what they are replacing.</summary>
    public string TheGamesDescription => TheGamesQuest?.Description ?? "";

    public string SourceNote
    {
        get
        {
            if (!ShowsVanillaPanel) return "";
            if (Model.Source.Length == 0) return Loc.T("quests.vanillaNote.choose");
            if (TheGamesQuest == null)
                return Loc.F("quests.vanillaNote.unknown", "quest", Model.Source);
            return Loc.T("quests.vanillaNote.stays");
        }
    }

    public string DescriptionNote => IsVanillaExtension
        ? Loc.T("quests.descriptionNote")
        : "";

    /// <summary>The game's tasks, as rows a pack can hang something on - the
    /// game's rows of <see cref="ExtensionRows"/>, in the same order.</summary>
    public ObservableCollection<VanillaTaskRowViewModel> VanillaTaskRows { get; } = new();

    /// <summary>
    /// The game's quest as this pack leaves it: the game's tasks
    /// (<see cref="VanillaTaskRowViewModel"/>) and the pack's
    /// (<see cref="QuestTaskViewModel"/>) in one list, in the order the journal
    /// will list them.
    /// </summary>
    public ObservableCollection<object> ExtensionRows { get; } = new();

    private List<ExtensionTaskNode> _nodes = new();

    private object? _selectedExtensionRow;

    /// <summary>The row picked in <see cref="ExtensionRows"/>. Picking one of
    /// the game's opens its panel; picking one of the pack's opens the same
    /// task panel a quest of the pack's own uses.</summary>
    public object? SelectedExtensionRow
    {
        get => _selectedExtensionRow;
        set
        {
            if (ReferenceEquals(_selectedExtensionRow, value)) return;
            _selectedExtensionRow = value;
            SelectedVanillaTask = value as VanillaTaskRowViewModel;
            if (IsVanillaExtension || WantsVanilla) SelectedTask = value as QuestTaskViewModel;
            OnPropertyChanged();
            RaiseTaskCommands();
        }
    }

    private VanillaTaskRowViewModel? _selectedVanillaTask;
    public VanillaTaskRowViewModel? SelectedVanillaTask
    {
        get => _selectedVanillaTask;
        set
        {
            if (ReferenceEquals(_selectedVanillaTask, value)) return;
            _selectedVanillaTask = value;
            if (value != null && !ReferenceEquals(_selectedExtensionRow, value))
            {
                _selectedExtensionRow = value;
                OnPropertyChanged(nameof(SelectedExtensionRow));
                SelectedTask = null;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedVanillaTask));
        }
    }

    public bool HasSelectedVanillaTask => SelectedVanillaTask != null;

    /// <summary>
    /// A quest is named and there are no tasks to show for it, which means the
    /// catalogue does not carry that quest - a name typed by hand, or one the
    /// game added after this build. Worth saying: an empty list otherwise reads
    /// as a quest with no tasks.
    /// </summary>
    public bool HasNoVanillaTasks => ShowsVanillaPanel && Model.Source.Length > 0 && VanillaTaskRows.Count == 0;

    /// <summary>How many of the game's tasks this pack says something about -
    /// what the entry is worth, in one number.</summary>
    public int HookCount => Model.VanillaTasks.Count(h => h.DoesAnything);

    /// <summary>Whether this entry adds tasks to the game's quest or takes
    /// some out - the changes that can break a save already under way.</summary>
    public bool ChangesTheGamesTasks => Model.ChangesTheGamesTasks;

    public void RebuildVanillaRows() => RebuildExtensionRows();

    /// <summary>
    /// Re-list the game's quest from the model, keeping the view model of each
    /// of the pack's tasks that is still there (and so its key-following
    /// state), and keeping the selection on the same task.
    /// </summary>
    public void RebuildExtensionRows()
    {
        var packRows = ExtensionRows.OfType<QuestTaskViewModel>()
            .ToDictionary(r => r.Model, r => r, ReferenceComparer.Instance);
        var selected = _selectedExtensionRow;
        string? selectedGame = (selected as VanillaTaskRowViewModel)?.Token;
        var selectedPack = (selected as QuestTaskViewModel)?.Model;

        _nodes = Model.IsVanillaExtension ? ExtensionTree.Build(Model) : new List<ExtensionTaskNode>();
        ExtensionRows.Clear();
        VanillaTaskRows.Clear();
        foreach (var node in _nodes)
        {
            if (node.Game != null)
            {
                var row = new VanillaTaskRowViewModel(node, this);
                VanillaTaskRows.Add(row);
                ExtensionRows.Add(row);
            }
            else
            {
                if (!packRows.TryGetValue(node.Added!, out var row)) row = new QuestTaskViewModel(node.Added!, this);
                row.Depth = node.Depth;
                ExtensionRows.Add(row);
            }
        }
        foreach (var row in ExtensionRows.OfType<QuestTaskViewModel>()) row.RefreshStructure();

        object? again = selectedGame != null
            ? VanillaTaskRows.FirstOrDefault(r => r.Token == selectedGame)
            : selectedPack != null
                ? ExtensionRows.OfType<QuestTaskViewModel>().FirstOrDefault(r => ReferenceEquals(r.Model, selectedPack))
                : null;
        _selectedExtensionRow = null;
        SelectedExtensionRow = again ?? (selected == null ? ExtensionRows.FirstOrDefault() : null);
        if (SelectedExtensionRow == null)
        {
            SelectedVanillaTask = null;
            if (IsVanillaExtension || selectedPack != null) SelectedTask = null;
            OnPropertyChanged(nameof(SelectedExtensionRow));
        }

        OnPropertyChanged(nameof(VanillaTaskRows));
        OnPropertyChanged(nameof(ExtensionRows));
        OnPropertyChanged(nameof(HookCount));
        OnPropertyChanged(nameof(HasNoVanillaTasks));
        OnPropertyChanged(nameof(ChangesTheGamesTasks));
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(HasChanges));
        RaiseWarning();
        RaiseTaskCommands();
    }

    /// <summary>The node behind one of the list's rows.</summary>
    internal ExtensionTaskNode? NodeOf(object? row) => row switch
    {
        VanillaTaskRowViewModel g => _nodes.FirstOrDefault(n => n.Game != null && n.Token == g.Token),
        QuestTaskViewModel t => _nodes.FirstOrDefault(n => ReferenceEquals(n.Added, t.Model)),
        _ => null,
    };

    /// <summary>Whether a row of the game's list has rows under it.</summary>
    internal bool HasChildren(ExtensionTaskNode node) => _nodes.Any(n => ReferenceEquals(n.Parent, node));

    /// <summary>
    /// Keep a row's entry in the manifest only while it says something, and
    /// keep the list in the game's own task order - which is the order the
    /// runtime reads them in when it decides which description wins.
    /// </summary>
    internal void KeepHook(VanillaTaskHookDef hook, string token, bool keep)
    {
        bool has = Model.VanillaTasks.Contains(hook);
        if (keep != has)
        {
            if (!keep) Model.VanillaTasks.Remove(hook);
            else
            {
                int at = 0;
                foreach (var row in VanillaTaskRows)
                {
                    if (string.Equals(row.Token, token, StringComparison.Ordinal)) break;
                    if (Model.VanillaTasks.Contains(row.Hook)) at++;
                }
                Model.VanillaTasks.Insert(Math.Min(at, Model.VanillaTasks.Count), hook);
            }
        }
        OnPropertyChanged(nameof(HookCount));
        OnPropertyChanged(nameof(ChangesTheGamesTasks));
        OnPropertyChanged(nameof(ChangeSummary));
        OnPropertyChanged(nameof(HasChanges));
        RaiseWarning();
        ResetAllCommand?.Raise();
    }

    /// <summary>
    /// Where a task of the pack's sits, for the notes that depend on it: how
    /// the task above it completes, and where it comes among the tasks it sits
    /// with. Null for a top-level task.
    /// </summary>
    internal (string Completion, int Index)? ParentOf(QuestTaskDef task)
    {
        if (Model.IsVanillaExtension)
        {
            var node = _nodes.FirstOrDefault(n => ReferenceEquals(n.Added, task));
            if (node?.Parent == null) return null;
            return (node.Parent.Completion, ExtensionTree.ChildrenOf(_nodes, node.Parent).IndexOf(node));
        }
        var parent = Model.AllTasks().FirstOrDefault(t => t.Subtasks.Contains(task));
        return parent == null ? null : (parent.Completion, parent.Subtasks.IndexOf(task));
    }

    /// <summary>Every task row the quest shows, whichever list it is in.</summary>
    internal IEnumerable<QuestTaskViewModel> AllTaskRows
        => TaskRows.Concat(ExtensionRows.OfType<QuestTaskViewModel>());

    // ── Changing the game's quest ────────────────────────────────────
    //
    // The pack's tasks are placed among the game's by the game's ids (see
    // AddedTaskDef), so every edit here works on the list as the author sees it
    // and then writes back where each of the pack's tasks now sits.

    /// <summary>A new top-level task of the pack's, just after the selected
    /// top-level row, or at the end.</summary>
    private void AddExtensionTask()
    {
        var def = new AddedTaskDef { Key = NewTaskKey(), Name = "" };
        var anchor = NodeOf(_selectedExtensionRow);
        while (anchor?.Parent != null) anchor = anchor.Parent;
        if (anchor != null && anchor.Orphan) anchor = null;
        InsertAmong(null, anchor, def);
        Added(def);
    }

    /// <summary>
    /// A new subtask of the pack's. Under a selected top-level task it goes at
    /// the end of that task's subtasks; beside a selected subtask it goes just
    /// after it - the game's quests are two levels deep, and so are the pack's
    /// additions.
    /// </summary>
    private void AddExtensionSubtask()
    {
        var node = NodeOf(_selectedExtensionRow);
        if (!CanAddExtensionSubtask(node)) return;

        var parent = node!.IsTopLevel ? node : node.Parent!;
        var after = node.IsTopLevel ? ExtensionTree.ChildrenOf(_nodes, parent).LastOrDefault() : node;

        if (parent.Added != null)
        {
            var def = new QuestTaskDef { Key = NewTaskKey(), Name = "" };
            var list = parent.Added.Subtasks;
            int at = after?.Added != null ? list.IndexOf(after.Added) + 1 : list.Count;
            list.Insert(Math.Clamp(at, 0, list.Count), def);
            Added(def);
        }
        else
        {
            var def = new AddedTaskDef { Key = NewTaskKey(), Name = "", Under = parent.Token };
            InsertAmong(parent, after, def);
            Added(def);
        }
    }

    private bool CanAddExtensionSubtask(ExtensionTaskNode? node)
    {
        if (node == null || node.Orphan) return false;
        var parent = node.IsTopLevel ? node : node.Parent;
        return parent != null && !parent.Removed;
    }

    private string NewTaskKey()
        => CharacterDef.UniqueIdentifier("Task" + (Model.AllTasks().Count() + 1), AllTaskKeys);

    /// <summary>Put an added task among the tasks under <paramref name="parent"/>
    /// (null: the top level), just after <paramref name="after"/> (null: at the
    /// end), and write back where every added task there now sits.</summary>
    private void InsertAmong(ExtensionTaskNode? parent, ExtensionTaskNode? after, AddedTaskDef def)
    {
        var order = ExtensionTree.ChildrenOf(_nodes, parent);
        var fresh = new ExtensionTaskNode { Added = def, Parent = parent, Depth = parent == null ? 0 : parent.Depth + 1 };
        int at = after == null ? order.Count : order.IndexOf(after) + 1;
        if (at <= 0 || at > order.Count) at = order.Count;
        order.Insert(at, fresh);
        Model.AddedTasks.Add(def);
        WriteOrder(order);
    }

    /// <summary>
    /// Store the order of one parent's tasks: each added task's "before" becomes
    /// the next of the game's tasks after it, and the added tasks are listed in
    /// this order - which is exactly what <see cref="QuestTreeEdits.Arrange"/>
    /// reads back.
    /// </summary>
    private void WriteOrder(List<ExtensionTaskNode> order)
    {
        var placed = order.Select(n => new QuestTreeEdits.Placed<VanillaQuests.VanillaTask?, AddedTaskDef?>
        {
            Game = n.Game,
            Added = n.Added as AddedTaskDef,
            IsAdded = n.Game == null,
        }).ToList();

        var inOrder = new List<AddedTaskDef>();
        for (int i = 0; i < placed.Count; i++)
        {
            if (!placed[i].IsAdded || placed[i].Added == null) continue;
            var added = placed[i].Added!;
            added.Before = QuestTreeEdits.BeforeFor(placed, i, g => g == null ? "" : ExtensionTree.Token(g.Id));
            inOrder.Add(added);
        }
        if (inOrder.Count == 0) return;

        int first = inOrder.Select(a => Model.AddedTasks.IndexOf(a)).Where(i => i >= 0).DefaultIfEmpty(Model.AddedTasks.Count).Min();
        foreach (var a in inOrder) Model.AddedTasks.Remove(a);
        Model.AddedTasks.InsertRange(Math.Min(first, Model.AddedTasks.Count), inOrder);
    }

    private void Added(QuestTaskDef def)
    {
        RebuildExtensionRows();
        var row = ExtensionRows.OfType<QuestTaskViewModel>().First(r => ReferenceEquals(r.Model, def));
        row.DeriveKeyFromName(() => AllTaskKeys);
        SelectedExtensionRow = row;
        OnPropertyChanged(nameof(Model));
    }

    /// <summary>
    /// Remove the selected row: a task of the pack's is deleted; one of the
    /// game's is taken out of the quest, which its own panel can undo.
    /// </summary>
    private void RemoveExtensionRow()
    {
        var node = NodeOf(_selectedExtensionRow);
        if (node == null) return;

        if (node.Game != null)
        {
            if (_selectedExtensionRow is VanillaTaskRowViewModel game) game.IsRemoved = true;
            return;
        }

        var siblings = ExtensionTree.ChildrenOf(_nodes, node.Parent);
        if (node.Orphan) siblings = _nodes.Where(n => n.Orphan && n.Parent == null).ToList();
        int index = siblings.IndexOf(node);

        if (node.Added is AddedTaskDef top && Model.AddedTasks.Contains(top)) Model.AddedTasks.Remove(top);
        else node.Parent?.Added?.Subtasks.Remove(node.Added!);

        _selectedExtensionRow = null;
        RebuildExtensionRows();

        // Land on a neighbour rather than on nothing.
        siblings.RemoveAt(index);
        var next = siblings.Count == 0 ? node.Parent : siblings[Math.Min(index, siblings.Count - 1)];
        SelectedExtensionRow = RowFor(next) ?? ExtensionRows.FirstOrDefault();
    }

    private object? RowFor(ExtensionTaskNode? node)
    {
        if (node == null) return null;
        return node.Game != null
            ? VanillaTaskRows.FirstOrDefault(r => r.Token == node.Token)
            : ExtensionRows.OfType<QuestTaskViewModel>().FirstOrDefault(r => ReferenceEquals(r.Model, node.Added));
    }

    private bool CanRemoveExtensionRow()
    {
        var node = NodeOf(_selectedExtensionRow);
        return node != null && (node.Added != null || !node.Removed);
    }

    private bool CanMoveExtension(int step)
    {
        var node = NodeOf(_selectedExtensionRow);
        if (node?.Added == null || node.Orphan) return false;
        var siblings = SiblingsForMove(node);
        int at = siblings.IndexOf(node) + step;
        return at >= 0 && at < siblings.Count;
    }

    /// <summary>What a task of the pack's moves among: the tasks it sits with,
    /// the game's included - except under a task of the pack's, whose subtasks
    /// are all the pack's.</summary>
    private List<ExtensionTaskNode> SiblingsForMove(ExtensionTaskNode node) => ExtensionTree.ChildrenOf(_nodes, node.Parent);

    private void MoveExtension(int step)
    {
        if (!CanMoveExtension(step)) return;
        var node = NodeOf(_selectedExtensionRow)!;
        var siblings = SiblingsForMove(node);
        int from = siblings.IndexOf(node);
        siblings.RemoveAt(from);
        siblings.Insert(from + step, node);

        if (node.Parent?.Added != null)
        {
            // Under one of the pack's tasks: an ordinary list.
            var list = node.Parent.Added.Subtasks;
            list.Remove(node.Added!);
            int after = siblings.IndexOf(node);
            int at = after == 0 ? 0 : list.IndexOf(siblings[after - 1].Added!) + 1;
            list.Insert(Math.Clamp(at, 0, list.Count), node.Added!);
        }
        else WriteOrder(siblings);

        RebuildExtensionRows();
    }

    // ── Starting ─────────────────────────────────────────────────────

    /// <summary>When these all pass, the quest starts by itself.</summary>
    public ConditionListViewModel StartConditions { get; }

    /// <summary>What the start list means as it stands - an empty one does
    /// not start the quest, which is not what an empty list means elsewhere.</summary>
    public string StartNote
    {
        get
        {
            string held = ResetConditions.Count > 0
                ? " " + Loc.T("quests.startNote.held")
                : "";
            if (IsVanillaExtension)
                return StartConditions.Count == 0
                    ? Loc.T("quests.startNote.gameNone")
                    : Loc.T("quests.startNote.gameEarly") + held;

            return StartConditions.Count == 0
                ? Loc.T("quests.startNote.none")
                : Loc.T("quests.startNote.own") + held;
        }
    }

    // -- Resetting ----------------------------------------------------

    /// <summary>When these all pass, a started quest goes back to not started.</summary>
    public ConditionListViewModel ResetConditions { get; }

    public string ResetNote
    {
        get
        {
            if (ResetConditions.Count == 0)
                return IsVanillaExtension
                    ? Loc.T("quests.resetNote.gameNone")
                    : Loc.T("quests.resetNote.none");
            return Loc.T("quests.resetNote.own");
        }
    }

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

    /// <summary>The quest the task is in. A task's key is only unique within
    /// it, so renaming one follows the rows that name both.</summary>
    internal QuestViewModel Quest => _quest;

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
        // Polled too: the runtime asks every frame until they pass.
        ShowConditions = new ConditionListViewModel(model.ShowConditions, ConditionContext.Polled);
        ShowConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConditionListViewModel.Count)) OnPropertyChanged(nameof(ShowConditionsNote));
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
        => Loc.T("quests.noOwnCompletion")
           + (Model.Conditions.Count + Model.Actions.Count > 0
               ? " " + Loc.T("quests.noOwnCompletion.kept")
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

    // ── What the journal shows ───────────────────────────────────────

    /// <summary>What the quest's description becomes once this task is done.</summary>
    public string QuestDescription
    {
        get => Model.QuestDescription;
        set
        {
            if (Model.QuestDescription == (value ?? "")) return;
            Model.QuestDescription = value ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>Keep this subtask out of the journal until it starts.</summary>
    public bool HideUntilStarted
    {
        get => Model.HideUntilStarted;
        set
        {
            if (Model.HideUntilStarted == value) return;
            Model.HideUntilStarted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HideUntilStartedNote));
            OnPropertyChanged(nameof(ShowConditionsNote));
        }
    }

    /// <summary>Keep this task out of the journal until its show conditions
    /// pass.</summary>
    public bool HideUntilConditions
    {
        get => Model.HideUntilConditions;
        set
        {
            if (Model.HideUntilConditions == value) return;
            Model.HideUntilConditions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowConditionsNote));
        }
    }

    /// <summary>What brings it into the journal.</summary>
    public ConditionListViewModel ShowConditions { get; }

    /// <summary>Hide it again when the conditions stop passing.</summary>
    public bool ShowConditionsLive
    {
        get => Model.ShowConditionsLive;
        set
        {
            if (Model.ShowConditionsLive == value) return;
            Model.ShowConditionsLive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowConditionsNote));
        }
    }

    public string ShowConditionsNote => ShowNotes.For(ShowConditions.Count, ShowConditionsLive, HideUntilStarted && IsSubtask);

    /// <summary>
    /// When a hidden subtask comes into view, said for where it sits. Read off
    /// the game: a subtask starts when the one before it is done under an
    /// in-order task, together with its parent under an any-order or any-one
    /// task, and never under a task completed by an action.
    /// </summary>
    public string HideUntilStartedNote
    {
        get
        {
            if (_quest.ParentOf(Model) is not { } parent) return "";

            string when;
            if (V.Is(parent.Completion, V.ByAction))
                when = Loc.T("quests.hiddenNote.byActionTicked");
            else if (V.Is(parent.Completion, V.AnyOrder) || V.Is(parent.Completion, V.AnyOne))
                when = Loc.T("quests.hiddenNote.together");
            else if (parent.Index == 0)
                when = Loc.T("quests.hiddenNote.first");
            else
                when = Loc.T("quests.hiddenNote.afterPrevious");

            return HideUntilStarted
                ? when
                : Loc.T("quests.hiddenNote.everySubtask");
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
            OnPropertyChanged(nameof(IsSubtask));
            OnPropertyChanged(nameof(CounterNote));
            OnPropertyChanged(nameof(RowText));
        }
    }

    /// <summary>Whether this is a subtask, which is where the journal needs
    /// telling to keep a task out of sight: it already leaves out a top-level
    /// task that has not started.</summary>
    public bool IsSubtask => Depth > 0;

    public bool IsTopLevel => Depth == 0;

    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    public bool HasSubtasks => Model.Subtasks.Count > 0;

    /// <summary>Whether this is a task the pack adds to one of the game's
    /// quests, rather than one of a quest of its own.</summary>
    public bool IsAddedToGameQuest => _quest.IsVanillaExtension;

    /// <summary>What the list row says: the journal's line, or a placeholder
    /// that reads as unfinished rather than as blank.</summary>
    public string RowText
        => (IsTopLevel ? "" : "▸ ") + (string.IsNullOrWhiteSpace(Name) ? Loc.T("quests.noText") : Name)
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
        OnPropertyChanged(nameof(HideUntilStartedNote));
        OnPropertyChanged(nameof(StartsNote));
        OnPropertyChanged(nameof(HasStartsNote));
    }

    /// <summary>When the game starts this task, for one of the pack's tasks in
    /// one of the game's quests - which depends on where it was put.</summary>
    public string StartsNote => _quest.StartsNoteFor(Model);

    public bool HasStartsNote => StartsNote.Length > 0;

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
            // When its subtasks start depends on this, and so does what each of
            // them says about when a hidden one appears.
            foreach (var row in _quest.AllTaskRows) row.RefreshStructure();
        }
    }

    /// <summary>What completes this task, said the way the game does it.</summary>
    public string CompletionNote
    {
        get
        {
            if (!HasSubtasks)
                return CompletionConditions.Count > 0
                    ? Loc.T("quests.completionNote.conditions")
                    : Loc.T("quests.completionNote.none");
            if (V.Is(Completion, V.ByAction))
                return Loc.T("quests.completionNote.byAction");
            // At the top level or under an in-order parent, the game lets an
            // action complete a task outright whatever its subtasks say - the
            // game's own quests do it. Anywhere else the subtasks are checked.
            string early = ActionCanCompleteEarly
                ? " " + Loc.T("quests.completionNote.early")
                : "";
            if (V.Is(Completion, V.AnyOne))
                return Loc.T("quests.completionNote.anyOne") + early;
            if (V.Is(Completion, V.AnyOrder))
                return Loc.T("quests.completionNote.anyOrder") + early;
            return Loc.T("quests.completionNote.inOrder") + early;
        }
    }

    /// <summary>Whether it sits where the game completes a task on request
    /// regardless of its subtasks: the top level, or under an in-order task.</summary>
    private bool ActionCanCompleteEarly
    {
        get
        {
            var parent = _quest.ParentOf(Model);
            return parent == null || V.Is(parent.Value.Completion, V.InOrder);
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

    // Keys, and the choices themselves: the code compares them, so they stay
    // the same whatever language is on screen, and the list shows their text.
    public const string CountedByAction = "quests.countedBy.action";
    public const string CountedByVariable = "quests.countedBy.variable";

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
                ? Loc.T("quests.counterNote.variable")
                : Loc.T("quests.counterNote.action");
            return IsTopLevel
                ? how
                : how + " " + Loc.T("quests.counterNote.hidden");
        }
    }
}

/// <summary>
/// One of the game's own tasks, as a pack extending that quest sees it: its
/// text and shape are the game's and cannot be edited here, and beside them sit
/// the things a pack can say about it - what the quest's description becomes
/// once it is done, what to run when it is, whether the journal shows it, and
/// whether it stays in the quest at all.
/// <para/>
/// The entry behind a row is only kept in the manifest while it says something.
/// A row an author clicked through and left alone writes nothing, so opening a
/// pack and closing it cannot grow the file.
/// </summary>
public sealed class VanillaTaskRowViewModel : ObservableObject
{
    private readonly QuestViewModel _quest;
    private readonly ExtensionTaskNode _node;

    internal VanillaTaskRowViewModel(ExtensionTaskNode node, QuestViewModel quest)
    {
        _node = node;
        _quest = quest;
        Game = node.Game!;

        Hook = node.Hook ?? new VanillaTaskHookDef { Task = node.Token };

        Actions = new ActionListViewModel(Hook.Actions);
        Actions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ActionListViewModel.Count)) return;
            Keep();
            OnPropertyChanged(nameof(RowText));
        };

        ShowConditions = new ConditionListViewModel(Hook.ShowConditions, ConditionContext.Polled);
        ShowConditions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ConditionListViewModel.Count)) return;
            Keep();
            OnPropertyChanged(nameof(VisibilityNote));
        };
    }

    /// <summary>The game's own task, as the catalogue has it.</summary>
    public VanillaQuests.VanillaTask Game { get; }

    /// <summary>The game's task as a row naming it sees it, in the quest as the
    /// pack leaves it - a task the pack gave subtasks to has subtasks.</summary>
    public QuestTaskInfo Task => new(
        Token, Game.Name, Depth, _quest.HasChildren(_node),
        QuestReferences.WordFor(Game.Completion),
        Game.Counter == TaskCounter.None ? null : Game.CountTo,
        Game.Counter == TaskCounter.Property);

    /// <summary>What the pack says about it. In the manifest only while it says
    /// something - see <see cref="Keep"/>.</summary>
    internal VanillaTaskHookDef Hook { get; }

    /// <summary>The id the game files this task under, which is what the pack
    /// stores: its text belongs to the game and a patch can rewrite it.</summary>
    public string Token => _node.Token;

    public string Name => string.IsNullOrWhiteSpace(Game.Name) ? Loc.T("quests.noText") : Game.Name;

    public int Depth => _node.Depth;

    public bool IsTopLevel => Depth == 0;

    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    /// <summary>The row in the list: the game's line, and what the pack does
    /// with it.</summary>
    public string RowText
    {
        get
        {
            var marks = new List<string>();
            if (IsRemoved) marks.Add(Loc.T("quests.mark.takenOut"));
            else if (RemovedWithParent) marks.Add(Loc.T("quests.mark.takenOutWithTask"));
            else if (Hook.IsHidden) marks.Add(Loc.T("quests.mark.hidden"));
            else if (Hook.IsHiddenUntilStarted) marks.Add(Loc.T("quests.mark.hiddenUntilStarts"));
            else if (Hook.IsHiddenUntilConditions) marks.Add(Loc.T("quests.mark.hiddenUntilConditions"));
            if (Hook.QuestDescription.Length > 0) marks.Add(Loc.T("quests.mark.description"));
            if (Actions.Count > 0) marks.Add(Loc.T("quests.mark.actions"));
            return (IsTopLevel ? "" : "▸ ") + Name
                   + (marks.Count > 0 ? "  " + Loc.F("quests.mark.list", "marks", Loc.JoinList(marks)) : "");
        }
    }

    /// <summary>Drawn faded in the list: the player will not see it.</summary>
    public bool IsOutOfSight => IsRemoved || RemovedWithParent || Hook.IsHidden;

    /// <summary>Drawn struck through in the list: not in the quest any more.</summary>
    public bool IsTakenOut => IsRemoved || RemovedWithParent;

    /// <summary>What the game will do with this task that its text does not
    /// say - a counter, or subtasks it waits for.</summary>
    public string Note => QuestReferences.Describe(Task);

    public bool HasNote => Note.Length > 0;

    // ── What the game does with it ───────────────────────────────────

    /// <summary>When the game starts it: nothing starts a task directly, so
    /// this is the quest's structure as the pack leaves it.</summary>
    public string StartsNote => TaskStarts.For(_node, _quest.Nodes);

    public bool HasStartsNote => StartsNote.Length > 0;

    private IReadOnlyList<GameQuestSiteGroup>? _gameSites;

    /// <summary>Every place the game completes, counts, fails or asks about
    /// this task, read-only.</summary>
    public IReadOnlyList<GameQuestSiteGroup> GameSites
        => _gameSites ??= GameQuestSites.ForTask(_quest.Model.Source, Token);

    public string GameSitesNote
        => string.Join(" ", new[]
           {
               GameQuestSites.TaskNote(_quest.Model.Source, Token,
                                       throughSubtasks: _quest.HasChildren(_node) && Game.Completion != TaskCompletion.Manual,
                                       counts: Game.Counter != TaskCounter.None),
               GameQuestSites.CoverageNote,
           }.Where(s => s.Length > 0));

    public bool HasGameSitesNote => GameSitesNote.Length > 0;

    /// <summary>What the quest's description becomes once the game completes
    /// this task.</summary>
    public string QuestDescription
    {
        get => Hook.QuestDescription;
        set
        {
            if (Hook.QuestDescription == (value ?? "")) return;
            Hook.QuestDescription = value ?? "";
            Keep();
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowText));
        }
    }

    /// <summary>Run once, when the game completes this task.</summary>
    public ActionListViewModel Actions { get; }

    // ── In the journal ───────────────────────────────────────────────

    // Keys, and the choices themselves - see QuestTaskViewModel.CountedByAction.
    public const string ShownAsTheGameHasIt = "quests.shown.asGame";
    public const string ShownOnceStarted = "quests.shown.onceStarted";
    public const string NeverShown = "quests.shown.never";
    public const string ShownOnceConditionsPass = "quests.shown.onceConditionsPass";

    /// <summary>
    /// The choices for this task. "Hidden until it starts" only on a subtask:
    /// the journal already leaves out a top-level task that has not started, and
    /// lists every subtask of a task it shows, started or not.
    /// </summary>
    public IReadOnlyList<string> VisibilityOptions => IsTopLevel
        ? new[] { ShownAsTheGameHasIt, ShownOnceConditionsPass, NeverShown }
        : new[] { ShownAsTheGameHasIt, ShownOnceStarted, ShownOnceConditionsPass, NeverShown };

    public string Visibility
    {
        get => Hook.IsHidden ? NeverShown
             : Hook.IsHiddenUntilStarted ? ShownOnceStarted
             : Hook.IsHiddenUntilConditions ? ShownOnceConditionsPass
             : ShownAsTheGameHasIt;
        set
        {
            if (value == null) return;
            string word = value switch
            {
                NeverShown => QuestTreeEdits.Hidden,
                ShownOnceStarted => QuestTreeEdits.HiddenUntilStarted,
                ShownOnceConditionsPass => QuestTreeEdits.HiddenUntilConditions,
                _ => "",
            };
            if (word == Hook.Visibility) return;
            Hook.Visibility = word;
            Keep();
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibilityNote));
            OnPropertyChanged(nameof(RowText));
            OnPropertyChanged(nameof(IsOutOfSight));
            OnPropertyChanged(nameof(IsHiddenUntilConditions));
        }
    }

    /// <summary>Whether the show conditions are on screen.</summary>
    public bool IsHiddenUntilConditions => Hook.IsHiddenUntilConditions;

    /// <summary>What brings the task into the journal.</summary>
    public ConditionListViewModel ShowConditions { get; }

    /// <summary>Hide it again when the conditions stop passing.</summary>
    public bool ShowConditionsLive
    {
        get => Hook.ShowConditionsLive;
        set
        {
            if (Hook.ShowConditionsLive == value) return;
            Hook.ShowConditionsLive = value;
            Keep();
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibilityNote));
        }
    }

    public string VisibilityNote
    {
        get
        {
            if (Hook.IsHidden)
                return Loc.T("quests.visibilityNote.never");
            if (Hook.IsHiddenUntilConditions)
                return ShowNotes.For(ShowConditions.Count, ShowConditionsLive, alsoUntilStarted: false);
            if (Hook.IsHiddenUntilStarted)
            {
                var parent = _node.Parent;
                if (parent?.Game?.Completion == TaskCompletion.Manual)
                    return Loc.T("quests.visibilityNote.byAction");
                if (parent?.Game != null && parent.Game.Completion != TaskCompletion.SubtasksInSequence)
                    return Loc.T("quests.hiddenNote.together");
                return Loc.T("quests.visibilityNote.onceStarts");
            }
            return IsTopLevel
                ? Loc.T("quests.visibilityNote.topLevel")
                : Loc.T("quests.hiddenNote.everySubtask");
        }
    }

    // ── Taken out of the quest ───────────────────────────────────────

    /// <summary>
    /// Take the task, and everything under it, out of the game's quest. Kept
    /// in the list, faded, so it can be put back.
    /// </summary>
    public bool IsRemoved
    {
        get => Hook.Removed;
        set
        {
            if (Hook.Removed == value) return;
            Hook.Removed = value;
            Keep();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanChooseVisibility));
            // Everything under it goes with it, and what can be added here changes.
            _quest.RebuildExtensionRows();
        }
    }

    /// <summary>Taken out because a task above it was.</summary>
    public bool RemovedWithParent => !Hook.Removed && _node.Parent?.Removed == true;

    public bool CanChooseRemoved => !RemovedWithParent;

    /// <summary>Whether the journal choice means anything: a task taken out
    /// of the quest is never listed, whatever it says.</summary>
    public bool CanChooseVisibility => !IsRemoved && !RemovedWithParent;

    public string RemovedNote
    {
        get
        {
            if (RemovedWithParent)
                return Loc.T("quests.removeNote.withParent");
            if (!Hook.Removed)
                return Loc.T(_quest.HasChildren(_node) ? "quests.removeNote.offerWithChildren" : "quests.removeNote.offer");
            return Loc.T("quests.removeNote.removed");
        }
    }

    private void Keep()
    {
        _quest.KeepHook(Hook, Token, Hook.DoesAnything);
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(ChangedFieldsText));
        OnPropertyChanged(nameof(ResettableFields));
        _reset?.Raise();
    }

    // -- Against the game's own task ----------------------------------

    /// <summary>Whether the pack says anything about this task - what the row
    /// marks, the way a changed line of a conversation is marked.</summary>
    public bool IsChanged => Hook.DoesAnything;

    private IEnumerable<(string Label, Action Reset)> Changes()
    {
        if (Hook.Removed) yield return (Loc.T("quests.mark.takenOut"), () => IsRemoved = false);
        if (Hook.Visibility.Length > 0 || Hook.ShowConditions.Count > 0 || Hook.ShowConditionsLive)
            yield return (Loc.T("quests.changed.journal"), ResetJournal);
        if (Hook.QuestDescription.Length > 0) yield return (Loc.T("quests.mark.description"), () => QuestDescription = "");
        if (Hook.Actions.Count > 0) yield return (Loc.T("quests.mark.actions"), ResetActions);
    }

    public string ChangedFieldsText
    {
        get
        {
            var labels = Changes().Select(c => c.Label).ToList();
            return labels.Count == 0 ? "" : Loc.F("quests.changedFromGame", "parts", Loc.JoinList(labels));
        }
    }

    /// <summary>Each thing the pack changes about this task, with its own way
    /// back. Only the ones it changes.</summary>
    public IReadOnlyList<DialogueNodeViewModel.ChangedField> ResettableFields
        => Changes().Select(c =>
        {
            var reset = c.Reset;
            return new DialogueNodeViewModel.ChangedField(c.Label, c.Label, new RelayCommand(reset));
        }).ToList();

    private RelayCommand? _reset;

    /// <summary>Put the task back the way the game has it.</summary>
    public RelayCommand ResetCommand => _reset ??= new RelayCommand(ResetAllOfIt, () => IsChanged);

    private void ResetJournal()
    {
        while (ShowConditions.Count > 0) ShowConditions.Remove(ShowConditions.Items[0]);
        ShowConditionsLive = false;
        Visibility = ShownAsTheGameHasIt;
    }

    private void ResetActions()
    {
        while (Actions.Count > 0) Actions.Remove(Actions.Items[0]);
    }

    private void ResetAllOfIt()
    {
        QuestDescription = "";
        ResetActions();
        ResetJournal();
        // Last: putting a task back in the quest rebuilds the list.
        IsRemoved = false;
        Keep();
    }
}

/// <summary>What a task hidden until its conditions pass does, said the same
/// way for the pack's tasks and the game's.</summary>
internal static class ShowNotes
{
    public static string For(int conditions, bool live, bool alsoUntilStarted)
    {
        string when = conditions == 0
            ? Loc.T("quests.showNote.none")
            : live
                ? Loc.T("quests.showNote.live")
                : Loc.T("quests.showNote.once");
        return alsoUntilStarted ? when + " " + Loc.T("quests.showNote.alsoUntilStarted") : when;
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
