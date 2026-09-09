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

        // Whether this line differs from the game's is a question about every
        // other property, so it is re-asked whenever any of them changes.
        // Otherwise the marker would be right only until the author typed, and
        // a stale "unchanged" on a line they just edited is worse than none.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsChangedFromVanilla)
                || e.PropertyName == nameof(ChangedFieldsText)
                || e.PropertyName == nameof(ResettableFields)
                || e.PropertyName == nameof(IsAddedLine)
                || e.PropertyName == nameof(ResetTooltip)) return;
            OnPropertyChanged(nameof(IsChangedFromVanilla));
            OnPropertyChanged(nameof(ChangedFieldsText));
            OnPropertyChanged(nameof(ResettableFields));
            OnPropertyChanged(nameof(IsAddedLine));
            OnPropertyChanged(nameof(ResetTooltip));
            _resetToVanilla?.Raise();
        };
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
            OnPropertyChanged(nameof(SpeakerPrefix));
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

    /// <summary>
    /// Resolves a speaker key to the character's display name. Same reasoning as
    /// <see cref="ActorColorProvider"/>: the node holds only the key, and the
    /// key is what the pack ships, but it is not what an author wants to read
    /// down a list of lines.
    /// </summary>
    public static System.Func<string, string>? ActorDisplayNameProvider;

    /// <summary>Re-read the tint. Called by the MainViewModel when an actor's
    /// colour changes — pushed rather than a static event the nodes subscribe
    /// to, because node VMs churn and would leak into it.</summary>
    // ── Against the game's own line ──────────────────────────────────

    /// <summary>
    /// The conversation this line belongs to, when it belongs to one.
    /// <para/>
    /// Set by the owner rather than looked up: a row cannot know where the
    /// list lives, and the questions below are all "how does this differ from
    /// the game's version", which only the conversation can answer.
    /// </summary>
    public DialogueViewModel? Owner { get; internal set; }

    /// <summary>Whether this line belongs to a change to one of the game's own
    /// conversations.</summary>
    public bool IsVanillaLine => Owner?.IsVanillaBased ?? false;

    /// <summary>Whether this line says anything the game does not — what a tree
    /// of 118 lines marks so the three that were touched can be found.</summary>
    public bool IsChangedFromVanilla => Owner?.HasChanges(Model) ?? false;

    /// <summary>Which fields differ, for the row's tooltip.</summary>
    public string ChangedFieldsText
    {
        get
        {
            var fields = Owner?.ChangedFields(Model);
            return fields == null || fields.Count == 0
                ? ""
                : "Changed from the game: " + string.Join(", ", fields);
        }
    }

    /// <summary>
    /// Put this line back the way the game has it — or, for a line the pack
    /// added, take it out, since the game has no version of it to go back to.
    /// </summary>
    public RelayCommand ResetToVanillaCommand => _resetToVanilla ??= new RelayCommand(
        () => Owner?.ResetNode(Model),
        () => IsChangedFromVanilla);

    private RelayCommand? _resetToVanilla;

    /// <summary>Whether this line is one the pack added rather than one of the
    /// game's — which is what makes its reset a deletion.</summary>
    public bool IsAddedLine
        => IsVanillaLine && (Owner?.ChangedFields(Model).Contains("(new line)") ?? false);

    /// <summary>What the whole-line reset does, said plainly on the button.</summary>
    public string ResetTooltip => IsAddedLine
        ? "Remove this line. The game has no version of it to go back to."
        : "Put this line back the way the game has it.";

    /// <summary>
    /// One changed field, and a way to put just that one back.
    /// <para/>
    /// Only the fields that actually differ: thirteen buttons, twelve of them
    /// greyed, is a worse answer to "what did I change here" than a short list
    /// of what did.
    /// </summary>
    public sealed record ChangedField(string Field, string Label, RelayCommand Reset);

    /// <summary>The fields of this line the pack changes, each with its own way
    /// back. Empty for an unchanged line and for a line the pack added.</summary>
    public System.Collections.Generic.IReadOnlyList<ChangedField> ResettableFields
    {
        get
        {
            var owner = Owner;
            if (owner == null || IsAddedLine) return System.Array.Empty<ChangedField>();

            var made = new System.Collections.Generic.List<ChangedField>();
            foreach (string changed in owner.ChangedFields(Model))
            {
                string one = changed;
                made.Add(new ChangedField(
                    one,
                    DialogueViewModel.FieldLabel(one),
                    new RelayCommand(() => owner.ResetField(Model, one))));
            }
            return made;
        }
    }

    /// <summary>
    /// Tell the row everything about it may have changed.
    /// <para/>
    /// Used after a reset, which replaces several of the model's fields at once
    /// from outside the row. Raising a null property name is WPF's own way of
    /// saying "all of them", and is cheaper to get right than a list that would
    /// go stale the next time a field is added.
    /// </summary>
    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
        RefreshActorTint();
    }

    public void RefreshActorTint()
    {
        OnPropertyChanged(nameof(ActorTintBrush));
        // The row label carries the speaker's NAME, so renaming a character has
        // to redraw it as well as the tint.
        OnPropertyChanged(nameof(SpeakerPrefix));
        OnPropertyChanged(nameof(Display));
    }

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
        set
        {
            Model.Text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextPreview));
            OnPropertyChanged(nameof(Display));
        }
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
            OnPropertyChanged(nameof(IsJump));
            OnPropertyChanged(nameof(JumpTargetTag));
        }
    }

    /// <summary>Whether the destination field applies. Continue and Exit have
    /// nowhere to go, so a tag typed against either is stored and then ignored
    /// — which reads as a jump that does not work.</summary>
    public bool IsJump => JumpMode == JumpMode.Jump;

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

    /// <summary>
    /// The line itself, collapsed to one row's worth. Split out from
    /// <see cref="Display"/> so the node list can put it in its own element and
    /// run the spell checker over it — the speaker prefix must stay out of that,
    /// or every character name in the pack sits under a red squiggle.
    /// <para/>
    /// Deliberately NOT length-clamped: the row trims at the actual column width
    /// and the full text is on the row's ToolTip. A fixed character cut would put
    /// the cut far short of the real edge no matter how wide the pane.
    /// </summary>
    public string TextPreview
    {
        get
        {
            string raw = string.IsNullOrEmpty(Text) ? "" : Text;
            string preview = System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", " ").Trim();
            return preview.Length == 0 ? "(no text)" : preview;
        }
    }

    /// <summary>
    /// <c>"[Name]"</c> for the speaking character, or empty. Shows the
    /// character's NAME, not the key the node stores: keys are stable
    /// identifiers ("solidsnake", "mobster"), the name is what the author
    /// recognises scanning a list of lines.
    /// <para/>
    /// No trailing space, deliberately. The row renders this next to a TextBox,
    /// and a TextBox insets its text a couple of pixels from its own left edge —
    /// a trailing space on top of that inset reads as a double gap.
    /// </summary>
    public string SpeakerPrefix
    {
        get
        {
            string speakerName = string.IsNullOrEmpty(Actor)
                ? ""
                : (ActorDisplayNameProvider?.Invoke(Actor) ?? Actor);
            return string.IsNullOrEmpty(speakerName) ? "" : "[" + speakerName + "]";
        }
    }

    /// <summary>Whole row as one string. The node list renders
    /// <see cref="SpeakerPrefix"/> and <see cref="TextPreview"/> separately now,
    /// so this is what the row's ToolTip shows — and the node id stays out of it,
    /// since jumps target a Tag and the author never types an id.</summary>
    public string Display =>
        SpeakerPrefix.Length == 0 ? TextPreview : SpeakerPrefix + " " + TextPreview;

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
