using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for a single <see cref="DialogueNodeDef"/>. The dialogue
/// node-list view binds to a flat collection of these and uses
/// <see cref="Display"/> for the line shown per row; the node editor in
/// the right pane binds to the selected one and exposes every authored
/// field.
/// <para/>
/// <see cref="Depth"/> + <see cref="IndentMargin"/> let the WPF node-list
/// render the flat collection as an indented tree without us having to
/// switch to a TreeView (which is awkward to drive with the existing
/// add-root / add-child / remove toolbar). The parent dialogue VM
/// recomputes both values whenever the tree structure changes via
/// <see cref="DialogueViewModel.RecomputeDepths"/>.
/// </summary>
public sealed class DialogueNodeViewModel : ObservableObject
{
    public DialogueNodeDef Model { get; }
    public ObservableCollection<NodeActionViewModel> ActionsOnStart { get; }
    public ObservableCollection<NodeActionViewModel> ActionsOnFinish { get; }
    public ObservableCollection<NodeConditionViewModel> Conditions { get; }

    public DialogueNodeViewModel(DialogueNodeDef model)
    {
        Model = model;
        // Hydrate the per-row VMs with callbacks pointing back at this VM's
        // collection-mutating methods — that's what lets the minus button in
        // each row remove itself without going through the parent dialogue
        // or the MainViewModel.
        ActionsOnStart  = new ObservableCollection<NodeActionViewModel>(
            model.ActionsOnStart .Select(a => new NodeActionViewModel(a,    removeCallback: RemoveActionOnStart)));
        ActionsOnFinish = new ObservableCollection<NodeActionViewModel>(
            model.ActionsOnFinish.Select(a => new NodeActionViewModel(a,    removeCallback: RemoveActionOnFinish)));
        // OneShot: GC2 evaluates a node's conditions when it reaches the node
        // (via PackCondition), not every frame — so a single Random roll is
        // well-defined here and the picker offers it.
        Conditions      = new ObservableCollection<NodeConditionViewModel>(
            model.Conditions    .Select(c => new NodeConditionViewModel(c, removeCallback: RemoveCondition,
                                                                        context: ConditionContext.OneShot)));

        // Per-list copy/paste/overwrite (cross-dialogue, type-safe via the
        // clipboard's separate action/condition slots).
        CopyActionsOnStartCommand      = new RelayCommand(() => Services.EditorClipboard.SetActions(Model.ActionsOnStart),
                                                          () => Model.ActionsOnStart.Count > 0);
        PasteActionsOnStartCommand     = new RelayCommand(() => PasteActions(ActionsOnStart, Model.ActionsOnStart, RemoveActionOnStart, overwrite: false),
                                                          () => Services.EditorClipboard.HasActions);
        OverwriteActionsOnStartCommand = new RelayCommand(() => PasteActions(ActionsOnStart, Model.ActionsOnStart, RemoveActionOnStart, overwrite: true),
                                                          () => Services.EditorClipboard.HasActions);
        CopyActionsOnFinishCommand      = new RelayCommand(() => Services.EditorClipboard.SetActions(Model.ActionsOnFinish),
                                                           () => Model.ActionsOnFinish.Count > 0);
        PasteActionsOnFinishCommand     = new RelayCommand(() => PasteActions(ActionsOnFinish, Model.ActionsOnFinish, RemoveActionOnFinish, overwrite: false),
                                                           () => Services.EditorClipboard.HasActions);
        OverwriteActionsOnFinishCommand = new RelayCommand(() => PasteActions(ActionsOnFinish, Model.ActionsOnFinish, RemoveActionOnFinish, overwrite: true),
                                                           () => Services.EditorClipboard.HasActions);
        CopyConditionsCommand      = new RelayCommand(() => Services.EditorClipboard.SetConditions(Model.Conditions),
                                                      () => Model.Conditions.Count > 0);
        PasteConditionsCommand     = new RelayCommand(() => PasteConditions(overwrite: false),
                                                      () => Services.EditorClipboard.HasConditions);
        OverwriteConditionsCommand = new RelayCommand(() => PasteConditions(overwrite: true),
                                                      () => Services.EditorClipboard.HasConditions);
    }

    // ── Per-list copy/paste commands ─────────────────────────────────────
    public RelayCommand CopyActionsOnStartCommand { get; }
    public RelayCommand PasteActionsOnStartCommand { get; }
    public RelayCommand OverwriteActionsOnStartCommand { get; }
    public RelayCommand CopyActionsOnFinishCommand { get; }
    public RelayCommand PasteActionsOnFinishCommand { get; }
    public RelayCommand OverwriteActionsOnFinishCommand { get; }
    public RelayCommand CopyConditionsCommand { get; }
    public RelayCommand PasteConditionsCommand { get; }
    public RelayCommand OverwriteConditionsCommand { get; }

    private void PasteActions(ObservableCollection<NodeActionViewModel> vmList, System.Collections.Generic.List<NodeActionDef> modelList,
                              System.Action<NodeActionViewModel> removeCb, bool overwrite)
    {
        var src = Services.EditorClipboard.Actions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { modelList.Clear(); vmList.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            modelList.Add(def);
            vmList.Add(new NodeActionViewModel(def, removeCb));
        }
    }

    private void PasteConditions(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { Model.Conditions.Clear(); Conditions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.Conditions.Add(def);
            Conditions.Add(new NodeConditionViewModel(def, removeCallback: RemoveCondition,
                                                      context: ConditionContext.OneShot));
        }
    }

    public int Id => Model.Id;

    public DialogueNodeKind Kind
    {
        get => Model.Kind;
        set { Model.Kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(KindGlyph)); }
    }

    public string Actor
    {
        get => Model.Actor;
        set
        {
            Model.Actor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(ActorTintBrush));
        }
    }

    // ── Speaker tint ──────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an actor key to the colour authored on the Actors tab, or null
    /// when the actor has none / isn't found. Set once by the MainViewModel —
    /// the node VM has only the actor KEY, and reaching the actor list from
    /// here would couple every node to the whole pack.
    /// </summary>
    public static System.Func<string, System.Windows.Media.Color?>? ActorColorProvider;

    /// <summary>Re-read the tint. Called by the MainViewModel when an actor's
    /// colour changes — pushed rather than a static event the nodes subscribe
    /// to, because node VMs churn and would leak into it.</summary>
    public void RefreshActorTint() => OnPropertyChanged(nameof(ActorTintBrush));

    /// <summary>
    /// Faint wash of the speaking actor's colour, so a change of speaker reads
    /// at a glance down the node list. Transparent when the node has no actor
    /// or the actor has no colour, which leaves the row's normal background.
    /// <para/>
    /// The alpha is deliberately low: these sit behind selection highlighting
    /// and the tag chip, and a saturated fill would fight both.
    /// </summary>
    public System.Windows.Media.Brush ActorTintBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Actor) || ActorColorProvider == null)
                return System.Windows.Media.Brushes.Transparent;
            var c = ActorColorProvider(Actor);
            if (c == null) return System.Windows.Media.Brushes.Transparent;
            var tint = System.Windows.Media.Color.FromArgb(
                ActorTintAlpha, c.Value.R, c.Value.G, c.Value.B);
            var brush = new System.Windows.Media.SolidColorBrush(tint);
            brush.Freeze();   // shared per row, never mutated
            return brush;
        }
    }

    /// <summary>Alpha applied to an actor's colour for the node-row wash.</summary>
    private const byte ActorTintAlpha = 56;

    public string Expression
    {
        get => Model.Expression;
        set { Model.Expression = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Optional outfit switch — a bust GO name from the speaking actor's
    /// <see cref="ActorViewModel.Outfits"/>. Empty = keep the actor's
    /// current bust.
    /// </summary>
    public string Outfit
    {
        get => Model.Outfit;
        set { Model.Outfit = value; OnPropertyChanged(); }
    }

    public string Text
    {
        get => Model.Text;
        set { Model.Text = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Tag
    {
        get => Model.Tag ?? "";
        set
        {
            Model.Tag = string.IsNullOrEmpty(value) ? null : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTag));
        }
    }

    /// <summary>How this line advances (Until Interaction / Timeout).</summary>
    public NodeDurationMode Duration
    {
        get => Model.Duration;
        set { Model.Duration = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsTimeout)); }
    }

    /// <summary>Drives the timeout-seconds box's enabled state.</summary>
    public bool IsTimeout => Model.Duration == NodeDurationMode.Timeout;

    /// <summary>Seconds the line lingers after typing, in Timeout mode.</summary>
    public float Timeout
    {
        get => Model.Timeout;
        set { Model.Timeout = value; OnPropertyChanged(); }
    }

    public JumpMode JumpMode
    {
        get => Model.Jump?.Mode ?? JumpMode.Continue;
        set
        {
            if (value == JumpMode.Continue)
            {
                Model.Jump = null;
            }
            else
            {
                Model.Jump ??= new JumpDef();
                Model.Jump.Mode = value;
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(JumpTargetTag));
        }
    }

    public string JumpTargetTag
    {
        get => Model.Jump?.TargetTag ?? "";
        set
        {
            Model.Jump ??= new JumpDef { Mode = JumpMode.Jump };
            Model.Jump.TargetTag = string.IsNullOrEmpty(value) ? null : value;
            OnPropertyChanged();
        }
    }

    /// <summary>Comma-separated child ids, suitable for showing in the list.</summary>
    public string ChildrenSummary => Model.Children.Count == 0 ? "(none)" : string.Join(", ", Model.Children);

    // ── Tree depth (set by the parent DialogueViewModel) ─────────────

    private int _depth;
    /// <summary>
    /// Distance from the nearest root in the dialogue tree, 0 for roots.
    /// Driven by <see cref="DialogueViewModel.RecomputeDepths"/>; never
    /// set directly from the UI.
    /// </summary>
    public int Depth
    {
        get => _depth;
        internal set
        {
            if (_depth == value) return;
            _depth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IndentMargin));
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>
    /// Left-margin thickness derived from <see cref="Depth"/>. Bound to
    /// the node list's row-template margin so the flat ListBox renders
    /// as an indented tree.
    /// </summary>
    public Thickness IndentMargin => new Thickness(Depth * 16, 0, 0, 0);

    /// <summary>One-line preview the node list uses as its item template. The
    /// Kind is conveyed by <see cref="KindGlyph"/> in the row, so it's not
    /// repeated here.</summary>
    public string Display
    {
        get
        {
            // Collapse newline/whitespace runs so the row stays a single line, but
            // DON'T clamp the length here — the node card trims with an ellipsis at
            // the actual column width (TextTrimming in the item template, with the
            // full text on the row's ToolTip). A fixed character cut here would make
            // the "…" appear far short of the real edge no matter how wide the pane.
            string raw = string.IsNullOrEmpty(Text) ? "" : Text;
            string preview = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
            if (preview.Length == 0) preview = "(no text)";
            string speaker = string.IsNullOrEmpty(Actor) ? "" : "[" + Actor + "] ";
            // The node id is bookkeeping the author never types — jumps target a
            // Tag, not an id — so the row shows the line, not the number.
            return speaker + preview;
        }
    }

    /// <summary>Whether this node carries a jump Tag, i.e. something else can
    /// jump to it. Drives the tag chip on the node row.</summary>
    public bool HasTag => !string.IsNullOrWhiteSpace(Tag);

    private bool _isChoiceChild;
    /// <summary>True for a direct child of a Choice node — i.e. an answer button
    /// rather than a normal line. Set by <see cref="DialogueViewModel.RecomputeDepths"/>.</summary>
    public bool IsChoiceChild
    {
        get => _isChoiceChild;
        set { if (_isChoiceChild == value) return; _isChoiceChild = value; OnPropertyChanged(); OnPropertyChanged(nameof(KindGlyph)); }
    }

    /// <summary>Leading symbol shown per node row: a choice answer takes
    /// precedence with the <c>◆</c> badge (it reads like a button), otherwise
    /// it's by <see cref="Kind"/> — Text <c>▸</c>, Choice <c>⇄</c>, Random <c>?</c>.</summary>
    public string KindGlyph => IsChoiceChild ? "◆" : Kind switch
    {
        DialogueNodeKind.Choice => "⇄",
        DialogueNodeKind.Random => "?",
        _ => "▸",
    };

    // ── Action collection ops ─────────────────────────────────────────

    public NodeActionViewModel AddActionOnStart()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        Model.ActionsOnStart.Add(def);
        var vm = new NodeActionViewModel(def, removeCallback: RemoveActionOnStart);
        ActionsOnStart.Add(vm);
        return vm;
    }

    public NodeActionViewModel AddActionOnFinish()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        Model.ActionsOnFinish.Add(def);
        var vm = new NodeActionViewModel(def, removeCallback: RemoveActionOnFinish);
        ActionsOnFinish.Add(vm);
        return vm;
    }

    public void RemoveActionOnStart(NodeActionViewModel a)
    {
        Model.ActionsOnStart.Remove(a.Model);
        ActionsOnStart.Remove(a);
    }

    public void RemoveActionOnFinish(NodeActionViewModel a)
    {
        Model.ActionsOnFinish.Remove(a.Model);
        ActionsOnFinish.Remove(a);
    }

    // ── Condition collection ops ──────────────────────────────────────

    public NodeConditionViewModel AddCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, removeCallback: RemoveCondition,
                                            context: ConditionContext.OneShot);
        Conditions.Add(vm);
        return vm;
    }

    /// <summary>Add an empty AND group (switchable to OR) to this node's conditions.</summary>
    public NodeConditionViewModel AddConditionGroup()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
        Model.Conditions.Add(def);
        var vm = new NodeConditionViewModel(def, removeCallback: RemoveCondition,
                                            context: ConditionContext.OneShot);
        Conditions.Add(vm);
        return vm;
    }

    public void RemoveCondition(NodeConditionViewModel c)
    {
        Model.Conditions.Remove(c.Model);
        Conditions.Remove(c);
    }
}
