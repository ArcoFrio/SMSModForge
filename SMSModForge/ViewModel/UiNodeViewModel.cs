using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One object in a UI tree, for the UI tab's editor.
/// <para/>
/// Two kinds of row share this type, and the difference matters at every turn:
/// a row BOUND to something the game already has, and a row the pack added. A
/// bound row can be edited but not deleted — deleting it would mean deleting an
/// object out of the vanilla screen, which a pack cannot do and should not
/// pretend to. An added row can be deleted, because it is the pack's.
/// <para/>
/// Shaped after <see cref="GameObjectViewModel"/>, which does the same job for
/// places, so an author who has built a place already knows how this behaves.
/// </summary>
public sealed class UiNodeViewModel : ObservableObject
{
    private readonly Action<UiNodeViewModel>? _remove;
    private readonly Func<UiNodeDef, bool>? _hasChanges;
    private readonly Action<UiNodeViewModel>? _reset;

    public UiNodeDef Model { get; }
    public ObservableCollection<UiNodeViewModel> Children { get; }

    /// <summary>What happens when this object is clicked - the same action
    /// rows the dialogue and rule editors use, so a list copied from one
    /// pastes into the other.</summary>
    public ObservableCollection<NodeActionViewModel> Actions { get; }

    /// <summary>What has to be true for this object to be shown. Re-checked
    /// every frame in the game, which is what lets a list drop what it should
    /// not be offering without a rule saying so.</summary>
    public ObservableCollection<NodeConditionViewModel> ActiveConditions { get; }

    /// <summary>What has to be true for a click to do anything.</summary>
    public ObservableCollection<NodeConditionViewModel> ClickConditions { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand AddChildCommand { get; }

    /// <summary>Raised whenever anything under this row changes, so the preview
    /// can redraw without every property having to remember to say so.</summary>
    public event Action? Changed;

    public UiNodeViewModel(UiNodeDef model, Action<UiNodeViewModel>? remove = null,
                           Func<UiNodeDef, bool>? hasChanges = null,
                           Action<UiNodeViewModel>? reset = null)
    {
        Model = model;
        _remove = remove;
        _hasChanges = hasChanges;
        _reset = reset;

        Actions = new ObservableCollection<NodeActionViewModel>(
            model.OnClick.Select(a => new NodeActionViewModel(a, RemoveAction)));

        // Polled every frame in the game, so a Random here would re-roll
        // constantly - the same context the place gates use.
        ActiveConditions = new ObservableCollection<NodeConditionViewModel>(
            model.ActiveConditions.Select(c =>
                new NodeConditionViewModel(c, RemoveActiveCondition, context: ConditionContext.Polled)));

        // Asked once, when the button is pressed.
        ClickConditions = new ObservableCollection<NodeConditionViewModel>(
            model.ClickConditions.Select(c =>
                new NodeConditionViewModel(c, RemoveClickCondition, context: ConditionContext.OneShot)));

        Children = new ObservableCollection<UiNodeViewModel>(
            model.Children.Select(c => new UiNodeViewModel(c, RemoveChild, hasChanges, reset)));
        foreach (var child in Children)
        {
            child.Changed += Bubble;
            child.Parent = this;
        }

        RemoveCommand = new RelayCommand(() => _remove?.Invoke(this),
                                         () => _remove != null && !IsVanilla);
        AddChildCommand = new RelayCommand(() => AddChild());

        // Only offered where it means something: a vanilla object that
        // currently differs. On the pack's own there is no "default" to go back
        // to, and on an untouched one it would do nothing.
        ResetCommand = new RelayCommand(() => _reset?.Invoke(this),
                                        () => _reset != null && IsVanilla && IsChanged);

        // Later siblings draw in front, so moving a row down in the tree brings
        // the object forward. Available on vanilla rows too: rearranging what
        // the game owns is a change the pack can express, unlike deleting it.
        MoveUpCommand = new RelayCommand(() => Parent?.MoveBy(this, -1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => Parent?.MoveBy(this, +1), () => CanMove(+1));

        AddActionCommand = new RelayCommand(() => AddAction());
        AddActiveConditionCommand = new RelayCommand(() => AddCondition(
            Model.ActiveConditions, ActiveConditions, RemoveActiveCondition, ConditionContext.Polled));
        AddClickConditionCommand = new RelayCommand(() => AddCondition(
            Model.ClickConditions, ClickConditions, RemoveClickCondition, ConditionContext.OneShot));

        // The same clipboard slots the dialogue, rule and level-hook editors
        // use, so an action list moves freely between all of them.
        CopyActionsCommand = new RelayCommand(
            () => Services.EditorClipboard.SetActions(Model.OnClick),
            () => Model.OnClick.Count > 0);
        PasteActionsCommand = new RelayCommand(
            () => PasteActions(overwrite: false),
            () => Services.EditorClipboard.HasActions);
        OverwriteActionsCommand = new RelayCommand(
            () => PasteActions(overwrite: true),
            () => Services.EditorClipboard.HasActions);
    }

    // ── How it arranges its children ─────────────────────────
    //
    // "Arrange" rather than "layout group", because the question an author is
    // answering is what this object should do with the things inside it, and
    // the answer is a row, a column, a grid, or nothing.

    /// <summary>The options offered, "None" first because that is what most
    /// objects are.</summary>
    public static IReadOnlyList<string> ArrangeOptions { get; } =
        new[] { None }.Concat(UiLayoutKinds.All).ToArray();

    private const string None = "None";

    /// <summary>Unity's anchor names, which is what a layout group means by
    /// alignment.</summary>
    public static IReadOnlyList<string> AlignOptions { get; } = new[]
    {
        "UpperLeft", "UpperCenter", "UpperRight",
        "MiddleLeft", "MiddleCenter", "MiddleRight",
        "LowerLeft", "LowerCenter", "LowerRight",
    };

    public static IReadOnlyList<string> ConstraintOptions { get; } =
        new[] { "Flexible", "FixedColumnCount", "FixedRowCount" };

    public string Arrange
    {
        get => Model.Layout?.Kind ?? None;
        set
        {
            if (Arrange == value) return;

            // Kept when switching between kinds, so trying a grid and going
            // back to a row does not silently discard the spacing and padding
            // that were already set.
            if (value == None) Model.Layout = null;
            else if (Model.Layout == null) Model.Layout = new UiLayoutDef { Kind = value };
            else Model.Layout.Kind = value;

            OnPropertyChanged(string.Empty);
            Bubble();
        }
    }

    public bool Arranges => Model.Layout != null;
    public bool ArrangesAsGrid => Model.Layout?.IsGrid == true;

    /// <summary>Whether the row/column-only settings apply.</summary>
    public bool ArrangesInLine => Arranges && !ArrangesAsGrid;

    public float SpacingX
    {
        get => Pair(Model.Layout?.Spacing, 0);
        set => SetPair(l => l.Spacing, 0, value);
    }

    public float SpacingY
    {
        get => Pair(Model.Layout?.Spacing, 1);
        set => SetPair(l => l.Spacing, 1, value);
    }

    public float PadLeft { get => Pair(Model.Layout?.Padding, 0); set => SetPair(l => l.Padding, 0, value); }
    public float PadRight { get => Pair(Model.Layout?.Padding, 1); set => SetPair(l => l.Padding, 1, value); }
    public float PadTop { get => Pair(Model.Layout?.Padding, 2); set => SetPair(l => l.Padding, 2, value); }
    public float PadBottom { get => Pair(Model.Layout?.Padding, 3); set => SetPair(l => l.Padding, 3, value); }

    public float CellWidth { get => Pair(Model.Layout?.CellSize, 0); set => SetPair(l => l.CellSize, 0, value); }
    public float CellHeight { get => Pair(Model.Layout?.CellSize, 1); set => SetPair(l => l.CellSize, 1, value); }

    public string ArrangeAlign
    {
        get => Model.Layout?.Alignment ?? "MiddleCenter";
        set => SetLayout(l => l.Alignment = value);
    }

    public string ArrangeConstraint
    {
        get => Model.Layout?.Constraint ?? "Flexible";
        set => SetLayout(l => l.Constraint = value);
    }

    public int ArrangeConstraintCount
    {
        get => Model.Layout?.ConstraintCount ?? 2;
        set => SetLayout(l => l.ConstraintCount = value);
    }

    public bool SetsChildWidth { get => Model.Layout?.ControlWidth == true; set => SetLayout(l => l.ControlWidth = value); }
    public bool SetsChildHeight { get => Model.Layout?.ControlHeight == true; set => SetLayout(l => l.ControlHeight = value); }
    public bool SharesWidth { get => Model.Layout?.ExpandWidth == true; set => SetLayout(l => l.ExpandWidth = value); }
    public bool SharesHeight { get => Model.Layout?.ExpandHeight == true; set => SetLayout(l => l.ExpandHeight = value); }
    public bool ArrangeReversed { get => Model.Layout?.Reverse == true; set => SetLayout(l => l.Reverse = value); }

    /// <summary>Centre a last row that never filled up - see
    /// <see cref="UiLayoutDef.CenterLastLine"/>.</summary>
    public bool CentersLastLine
    {
        get => Model.Layout?.CenterLastLine == true;
        set => SetLayout(l => l.CenterLastLine = value);
    }

    private static float Pair(float[]? values, int i)
        => values != null && values.Length > i ? values[i] : 0f;

    private void SetPair(Func<UiLayoutDef, float[]> which, int i, float value)
        => SetLayout(l =>
        {
            var array = which(l);
            if (array.Length > i) array[i] = value;
        });

    private void SetLayout(Action<UiLayoutDef> change)
    {
        if (Model.Layout == null) return;   // nothing to arrange with
        change(Model.Layout);
        OnPropertyChanged(string.Empty);
        Bubble();
    }

    // ── What clicking it does ────────────────────────────────────────

    public RelayCommand AddActionCommand { get; }
    public RelayCommand AddActiveConditionCommand { get; }
    public RelayCommand AddClickConditionCommand { get; }

    /// <summary>Whether this object decides its own visibility.</summary>
    public bool IsGated => Model.ActiveConditions.Count > 0;

    private void AddCondition(System.Collections.Generic.List<NodeConditionDef> onModel,
                              ObservableCollection<NodeConditionViewModel> rows,
                              Action<NodeConditionViewModel> remove,
                              ConditionContext context)
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        onModel.Add(def);
        rows.Add(new NodeConditionViewModel(def, remove, context: context));
        Bubble();
    }

    private void RemoveActiveCondition(NodeConditionViewModel row)
    {
        Model.ActiveConditions.Remove(row.Model);
        ActiveConditions.Remove(row);
        Bubble();
    }

    private void RemoveClickCondition(NodeConditionViewModel row)
    {
        Model.ClickConditions.Remove(row.Model);
        ClickConditions.Remove(row);
        Bubble();
    }
    public RelayCommand CopyActionsCommand { get; }
    public RelayCommand PasteActionsCommand { get; }
    public RelayCommand OverwriteActionsCommand { get; }

    /// <summary>Whether clicking this object does anything at all. An object
    /// with no actions is decoration and the pointer goes straight through
    /// it.</summary>
    public bool IsClickable => Model.OnClick.Count > 0;

    /// <summary>Tint while the pointer is over it. Blank for no hover
    /// feedback.</summary>
    /// <summary>How this object arrives when it is switched on - the same
    /// setting a whole screen has.</summary>
    public UiOpenViewModel Open => _open ??= new UiOpenViewModel(
        () => Model.Open, v => Model.Open = v, () => Model, changed: Bubble);

    private UiOpenViewModel? _open;

    /// <summary>How this object leaves when an action switches it off.</summary>
    public UiOpenViewModel Close => _close ??= new UiOpenViewModel(
        () => Model.Close, v => Model.Close = v, () => Model, closing: true, changed: Bubble);

    private UiOpenViewModel? _close;

    /// <summary>This object's own click sound, overriding the screen's.
    /// Empty means it uses the screen's.</summary>
    public string ClickSound
    {
        get => Model.ClickSound;
        set
        {
            if (Model.ClickSound == value) return;
            Model.ClickSound = value ?? "";
            OnPropertyChanged();
        }
    }

    public string HoverTint
    {
        get => Model.HoverTint;
        set
        {
            if (Model.HoverTint == value) return;
            Model.HoverTint = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HoverTintBrush));
            Bubble();
        }
    }

    public System.Windows.Media.Brush HoverTintBrush
        => string.IsNullOrEmpty(HoverTint)
         ? System.Windows.Media.Brushes.Transparent
         : BrushFor(HoverTint);

    private NodeActionViewModel AddAction()
    {
        var def = new NodeActionDef { Type = NodeActionTypes.SetVariable };
        Model.OnClick.Add(def);
        var vm = new NodeActionViewModel(def, RemoveAction);
        Actions.Add(vm);
        Bubble();
        return vm;
    }

    private void RemoveAction(NodeActionViewModel action)
    {
        Model.OnClick.Remove(action.Model);
        Actions.Remove(action);
        Bubble();
    }

    private void PasteActions(bool overwrite)
    {
        var source = Services.EditorClipboard.Actions;
        if (source == null || source.Count == 0) return;

        if (overwrite) { Model.OnClick.Clear(); Actions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(source))
        {
            Model.OnClick.Add(def);
            Actions.Add(new NodeActionViewModel(def, RemoveAction));
        }
        Bubble();
    }

    /// <summary>The row this one sits under, or null for the root. Set by
    /// whoever builds the children, which is the only thing that knows.</summary>
    public UiNodeViewModel? Parent { get; private set; }

    // ── The tree's own state ─────────────────────────────────────────
    //
    // Held here rather than left to the TreeView, because selection can now
    // arrive from the picture as well as from a click on the row, and
    // TreeView.SelectedItem is read-only - there is no other way to push a
    // selection into it.

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
    }

    private bool _isSelected;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _isExpanded;

    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    private bool CanMove(int delta)
    {
        if (Parent == null) return false;
        int at = Parent.Children.IndexOf(this);
        int to = at + delta;
        return at >= 0 && to >= 0 && to < Parent.Children.Count;
    }

    public RelayCommand ResetCommand { get; }

    // ── What this row is ─────────────────────────────────────────────

    /// <summary>Whether this row stands for an object the game already has.
    /// The one fact that decides what may be done to it.</summary>
    public bool IsVanilla => Model.IsBound;

    /// <summary>Whether the pack added this object, so it can be removed.</summary>
    public bool IsMine => !Model.IsBound;

    /// <summary>Where this sits in the vanilla screen, or empty for an
    /// addition. Shown so an author can tell which is which without guessing
    /// from an icon.</summary>
    public string Bind => Model.Bind;

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            // A bound row's name is the vanilla object's name and renaming it
            // would break the binding, so it is read-only rather than quietly
            // corrupting the link. The editor disables the box; this is the
            // second lock.
            if (IsVanilla) return;
            Model.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            Bubble();
        }
    }

    public string Display
        => string.IsNullOrWhiteSpace(Model.Name) ? "(unnamed)" : Model.Name;

    /// <summary>
    /// What has happened to this object, in one word for the tree.
    /// <para/>
    /// The distinction the whole tab turns on, and the reason it is worth a
    /// marker: a screen can hold 1434 objects and an author needs to see the
    /// three they touched. "changed" uses exactly the rule that decides what
    /// gets saved, so a marked row and a stored row are the same rows.
    /// </summary>
    public string Status
        => IsMine ? "new"
           : _hasChanges?.Invoke(Model) == true ? "changed"
           : "";

    public bool IsChanged => Status == "changed";
    public bool IsUntouched => Status.Length == 0;

    /// <summary>A short line saying what this object is made of, for a tree row
    /// that would otherwise be a name and nothing else.</summary>
    public string Summary
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Model.Image != null && !string.IsNullOrEmpty(Model.Image.Sprite))
                parts.Add(Model.Image.Type.Equals("Sliced", StringComparison.OrdinalIgnoreCase)
                    ? Model.Image.Sprite + " (sliced)" : Model.Image.Sprite);
            if (Model.Text != null && !string.IsNullOrEmpty(Model.Text.Value))
                parts.Add('“' + Trim(Model.Text.Value, 28) + '”');
            if (Model.Components.Count > 0) parts.Add($"{Model.Components.Count} component(s)");
            if (!Model.StartActive) parts.Add("starts hidden");
            return string.Join(" · ", parts);
        }
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    public bool StartActive
    {
        get => Model.StartActive;
        set
        {
            if (Model.StartActive == value) return;
            Model.StartActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    // ── Geometry ─────────────────────────────────────────────────────

    public float PositionX
    {
        get => At(Model.Rect.Position, 0);
        set { Set(Model.Rect.Position, 0, value); OnPropertyChanged(); Bubble(); }
    }

    public float PositionY
    {
        get => At(Model.Rect.Position, 1);
        set { Set(Model.Rect.Position, 1, value); OnPropertyChanged(); Bubble(); }
    }

    public float Width
    {
        get => At(Model.Rect.Size, 0);
        set { Set(Model.Rect.Size, 0, value); OnPropertyChanged(); Bubble(); }
    }

    public float Height
    {
        get => At(Model.Rect.Size, 1);
        set { Set(Model.Rect.Size, 1, value); OnPropertyChanged(); Bubble(); }
    }

    public float RotationZ
    {
        get => Model.Rect.RotationZ;
        set { Model.Rect.RotationZ = value; OnPropertyChanged(); Bubble(); }
    }

    // ── Picture ──────────────────────────────────────────────────────

    public bool HasImage => Model.Image != null;

    public string Sprite
    {
        get => Model.Image?.Sprite ?? "";
        set
        {
            // Setting a sprite on an object that had none gives it a picture,
            // which is a thing an author does on purpose - a container becomes
            // a panel. Clearing it back to nothing takes the picture away
            // rather than leaving an empty one behind to be saved.
            if (string.IsNullOrEmpty(value) && Model.Image != null)
            {
                Model.Image = null;
            }
            else if (!string.IsNullOrEmpty(value))
            {
                Model.Image ??= new UiImageDef();
                if (Model.Image.Sprite == value) return;
                Model.Image.Sprite = value;
            }
            else return;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    public string Tint
    {
        get => Model.Image?.Tint ?? "#FFFFFFFF";
        set
        {
            if (Model.Image == null || Model.Image.Tint == value) return;
            Model.Image.Tint = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TintBrush));
            Bubble();
        }
    }

    // ── Words ────────────────────────────────────────────────────────

    public bool HasText => Model.Text != null;

    public string Text
    {
        get => Model.Text?.Value ?? "";
        set
        {
            if (string.IsNullOrEmpty(value) && Model.Text != null) Model.Text = null;
            else if (!string.IsNullOrEmpty(value))
            {
                Model.Text ??= new UiTextDef();
                if (Model.Text.Value == value) return;
                Model.Text.Value = value;
            }
            else return;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasText));
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    public string Font
    {
        get => Model.Text?.Font ?? "";
        set
        {
            if (Model.Text == null || Model.Text.Font == value) return;
            Model.Text.Font = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    /// <summary>Text colour as "#RRGGBBAA".</summary>
    public string TextColor
    {
        get => Model.Text?.Color ?? "#FFFFFFFF";
        set
        {
            if (Model.Text == null || Model.Text.Color == value) return;
            Model.Text.Color = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextColorBrush));
            Bubble();
        }
    }

    /// <summary>The swatch beside the box, so a hex string is something a
    /// person can actually judge.</summary>
    public System.Windows.Media.Brush TintBrush => BrushFor(Tint);
    public System.Windows.Media.Brush TextColorBrush => BrushFor(TextColor);

    private static System.Windows.Media.Brush BrushFor(string hex)
    {
        var c = Rendering.UiColor.Parse(hex);
        return new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
    }

    public float FontSize
    {
        get => Model.Text?.Size ?? 36f;
        set
        {
            if (Model.Text == null || Math.Abs(Model.Text.Size - value) < 0.001f) return;
            Model.Text.Size = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    // ── Children ─────────────────────────────────────────────────────

    /// <summary>Add an object of the pack's own, inside this one.</summary>
    public UiNodeViewModel AddChild(string name = "New object")
        => AddChild(new UiNodeDef { Name = name });

    /// <summary>Adopt a ready-made object - a template, usually - as a child of
    /// this one. Its name is made unique here rather than by the template,
    /// which has no idea what else is already in the tree.</summary>
    public UiNodeViewModel AddChild(UiNodeDef def)
    {
        def.Name = Unique(string.IsNullOrEmpty(def.Name) ? "New object" : def.Name);
        Model.Children.Add(def);
        var vm = new UiNodeViewModel(def, RemoveChild, _hasChanges, _reset);
        vm.Changed += Bubble;
        vm.Parent = this;
        Children.Add(vm);
        RaiseMoves();
        Bubble();
        return vm;
    }

    /// <summary>A name not already taken by a sibling. Duplicate names are how
    /// bind paths become ambiguous, and this is the cheapest place to stop
    /// that.</summary>
    private string Unique(string wanted)
    {
        var taken = Children.Select(c => c.Model.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(wanted)) return wanted;
        for (int n = 2; ; n++)
        {
            string candidate = $"{wanted} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private void RemoveChild(UiNodeViewModel child)
    {
        // Only the pack's own objects. A vanilla one has no delete command, and
        // this is the second lock in case something else calls in.
        if (child.IsVanilla) return;
        child.Changed -= Bubble;
        child.Parent = null;
        Model.Children.Remove(child.Model);
        Children.Remove(child);
        RaiseMoves();
        Bubble();
    }

    // ── Order ─────────────────────────────────────────
    //
    // A canvas has no depth to sort by: an object is in front of another
    // because it comes after it. So the order of these rows is not a
    // presentation detail, it is what the pack will look like, and both lists
    // have to be moved together - the rows an author sees and the model that
    // gets saved.

    /// <summary>Move a child one place up or down among its siblings.</summary>
    public void MoveBy(UiNodeViewModel child, int delta)
        => MoveTo(child, Children.IndexOf(child) + delta);

    /// <summary>Move a child to a given place among its siblings.</summary>
    public void MoveTo(UiNodeViewModel child, int index)
    {
        int at = Children.IndexOf(child);
        if (at < 0 || index < 0 || index >= Children.Count || index == at) return;

        Children.Move(at, index);
        Model.Children.RemoveAt(at);
        Model.Children.Insert(index, child.Model);

        RaiseMoves();
        Bubble();
    }

    /// <summary>Every sibling's ability to move has just changed - the ones at
    /// the ends most of all.</summary>
    private void RaiseMoves()
    {
        foreach (var child in Children)
        {
            child.MoveUpCommand.Raise();
            child.MoveDownCommand.Raise();
        }
    }

    /// <summary>Re-read everything from the model. For after a reset, which
    /// replaces the values wholesale rather than through the setters.</summary>
    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
        Bubble();
    }

    private void Bubble()
    {
        // The marker is derived from the model, so it has to be re-asked after
        // anything that could change the answer.
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(IsUntouched));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsClickable));
        OnPropertyChanged(nameof(IsGated));
        ResetCommand?.Raise();
        Changed?.Invoke();
    }

    private static float At(float[]? pair, int i)
        => pair != null && pair.Length > i ? pair[i] : 0f;

    private static void Set(float[] pair, int i, float value)
    {
        if (pair.Length > i) pair[i] = value;
    }
}
