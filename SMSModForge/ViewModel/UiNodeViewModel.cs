using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Localization;
using SMSModForge.Rendering;

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
    private readonly Func<UiNodeDef, VanillaUiSurface.Node?>? _gameNode;
    private readonly Action? _hiddenChanged;

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
                           Action<UiNodeViewModel>? reset = null,
                           Func<UiNodeDef, VanillaUiSurface.Node?>? gameNode = null,
                           Action? hiddenChanged = null)
    {
        Model = model;
        _remove = remove;
        _hasChanges = hasChanges;
        _reset = reset;
        _gameNode = gameNode;
        _hiddenChanged = hiddenChanged;
        ToggleHiddenCommand = new RelayCommand(() => IsHiddenInPreview = !IsHiddenInPreview, () => !NotDrawnAnyway);

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
            model.Children.Select(c => new UiNodeViewModel(c, RemoveChild, hasChanges, reset, gameNode, hiddenChanged)));
        foreach (var child in Children)
        {
            child.Changed += Bubble;
            child.Parent = this;
        }

        Components = new ObservableCollection<ComponentRowViewModel>(
            model.Components.Select(c => new ComponentRowViewModel(c, RemoveComponent)));
        AddComponentCommand = new RelayCommand(() => AddComponent());

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

    /// <summary>Only as big as what it holds - see
    /// <see cref="UiLayoutDef.FitToContent"/>.</summary>
    public bool FitsToContent
    {
        get => Model.Layout?.FitToContent == true;
        set => SetLayout(l => l.FitToContent = value);
    }

    /// <summary>A grid with a set number of columns or rows - the only kind
    /// fitting to what it holds changes.</summary>
    public bool ShowsFitToContent => ArrangesAsGrid && ArrangeConstraint != "Flexible";

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

    // ── Hidden in the preview ────────────────────────────────────────
    //
    // The eye beside each row, as Unity has: the author's way of getting an
    // object out of the way while working on what is behind it. It says nothing
    // about the game, and writes nothing to the pack.

    public RelayCommand ToggleHiddenCommand { get; }

    public bool IsHiddenInPreview
    {
        get => Model.HiddenInPreview;
        set
        {
            if (Model.HiddenInPreview == value) return;
            Model.HiddenInPreview = value;
            RefreshEye();
            _hiddenChanged?.Invoke();
            Bubble();
        }
    }

    /// <summary>Not drawn because something it sits inside has been hidden.</summary>
    public bool IsHiddenByParent => Parent != null && (Parent.IsHiddenInPreview || Parent.IsHiddenByParent);

    /// <summary>Not drawn whatever its eye says, because it or something it sits
    /// inside starts switched off. The top of a tree is drawn regardless, being
    /// what is being looked at.</summary>
    public bool NotDrawnAnyway => Parent != null && (!StartActive || Parent.NotDrawnAnyway);

    public bool ShownInPreview => !IsHiddenInPreview && !IsHiddenByParent && !NotDrawnAnyway;

    public string EyeTip
        => Loc.T(NotDrawnAnyway ? "ui.eye.startsOff.tip"
                 : IsHiddenByParent ? "ui.eye.parent.tip"
                 : IsHiddenInPreview ? "ui.eye.show.tip"
                 : "ui.eye.hide.tip");

    /// <summary>This row's eye and everything's under it, which follows it.</summary>
    internal void RefreshEye()
    {
        OnPropertyChanged(nameof(IsHiddenInPreview));
        OnPropertyChanged(nameof(IsHiddenByParent));
        OnPropertyChanged(nameof(NotDrawnAnyway));
        OnPropertyChanged(nameof(ShownInPreview));
        OnPropertyChanged(nameof(EyeTip));
        ToggleHiddenCommand.Raise();
        foreach (var child in Children) child.RefreshEye();
    }

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
            _hiddenChanged?.Invoke();     // what is hidden is held by its path
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
                    ? Loc.F("ui.node.sliced", "sprite", Model.Image.Sprite) : Model.Image.Sprite);
            if (Model.Text != null && !string.IsNullOrEmpty(Model.Text.Value))
                parts.Add('“' + Trim(Model.Text.Value, 28) + '”');
            if (Model.Components.Count > 0) parts.Add(Loc.P("ui.node.components", Model.Components.Count));
            if (!Model.StartActive) parts.Add(Loc.T("ui.node.startsHidden"));
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
            RefreshEye();
            Bubble();
        }
    }

    /// <summary>Stay level while what it hangs on turns - see
    /// <see cref="UiNodeDef.Upright"/>.</summary>
    public bool Upright
    {
        get => Model.Upright;
        set
        {
            if (Model.Upright == value) return;
            Model.Upright = value;
            OnPropertyChanged();
            Bubble();
        }
    }

    // ── Pressed, and moving while shown ──────────────────────────────

    /// <summary>The size while held down, as typed - empty for none. See
    /// <see cref="UiNodeDef.PressScale"/>.</summary>
    public string PressScale
    {
        get => Model.PressScale?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set
        {
            float? parsed = float.TryParse((value ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
            if (Model.PressScale == parsed) return;
            Model.PressScale = parsed;
            OnPropertyChanged();
            Bubble();
        }
    }

    /// <summary>Whether it keeps moving while shown - see <see cref="UiLoopDef"/>.</summary>
    public bool Loops => Model.Loop is { DoesAnything: true };

    public bool PulsesFade { get => Model.Loop?.Alpha != null; set => SetPart(l => l.Alpha = value ? new[] { 0.4f, 1f } : null); }
    public bool PulsesSize { get => Model.Loop?.Scale != null; set => SetPart(l => l.Scale = value ? new[] { 1f, 1.05f } : null); }
    public bool Sweeps
    {
        get => Model.Loop?.MoveFrom != null;
        set => SetPart(l => { l.MoveFrom = value ? new[] { -100f, 0f } : null; l.MoveTo = value ? new[] { 100f, 0f } : null; });
    }

    public float FadeFrom { get => LoopAt(l => l.Alpha, 0); set => SetLoopAt(l => l.Alpha, 0, value); }
    public float FadeTo { get => LoopAt(l => l.Alpha, 1); set => SetLoopAt(l => l.Alpha, 1, value); }
    public float SizeFrom { get => LoopAt(l => l.Scale, 0); set => SetLoopAt(l => l.Scale, 0, value); }
    public float SizeTo { get => LoopAt(l => l.Scale, 1); set => SetLoopAt(l => l.Scale, 1, value); }
    public float SweepFromX { get => LoopAt(l => l.MoveFrom, 0); set => SetLoopAt(l => l.MoveFrom, 0, value); }
    public float SweepFromY { get => LoopAt(l => l.MoveFrom, 1); set => SetLoopAt(l => l.MoveFrom, 1, value); }
    public float SweepToX { get => LoopAt(l => l.MoveTo, 0); set => SetLoopAt(l => l.MoveTo, 0, value); }
    public float SweepToY { get => LoopAt(l => l.MoveTo, 1); set => SetLoopAt(l => l.MoveTo, 1, value); }

    public float LoopSeconds { get => Model.Loop?.Seconds ?? 2f; set => SetLoop(l => l.Seconds = Math.Max(0.05f, value)); }
    public float LoopPause { get => Model.Loop?.Pause ?? 0f; set => SetLoop(l => l.Pause = Math.Max(0f, value)); }
    public float LoopDelay { get => Model.Loop?.Delay ?? 0f; set => SetLoop(l => l.Delay = Math.Max(0f, value)); }
    public string LoopEasing { get => Model.Loop?.Easing ?? UiOpenDef.Linear; set => SetLoop(l => l.Easing = value ?? UiOpenDef.Linear); }

    private float LoopAt(Func<UiLoopDef, float[]?> which, int i)
        => Model.Loop is { } l && which(l) is { } a && a.Length > i ? a[i] : 0f;

    private void SetLoopAt(Func<UiLoopDef, float[]?> which, int i, float value)
        => SetLoop(l => { if (which(l) is { } a && a.Length > i) a[i] = value; });

    /// <summary>A change to a loop that is there; nothing without one.</summary>
    private void SetLoop(Action<UiLoopDef> change)
    {
        if (Model.Loop == null) return;
        change(Model.Loop);
        OnPropertyChanged(string.Empty);
        Bubble();
    }

    /// <summary>A part switched on or off: the loop is made for its first
    /// part and goes with its last, so an object that moves nothing writes
    /// nothing.</summary>
    private void SetPart(Action<UiLoopDef> change)
    {
        var loop = Model.Loop ?? new UiLoopDef();
        change(loop);
        Model.Loop = loop.DoesAnything ? loop : null;
        OnPropertyChanged(string.Empty);
        Bubble();
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

    public float ScaleX
    {
        get => At(Model.Rect.Scale, 0);
        set { Set(Model.Rect.Scale, 0, value); OnPropertyChanged(); Bubble(); }
    }

    public float ScaleY
    {
        get => At(Model.Rect.Scale, 1);
        set { Set(Model.Rect.Scale, 1, value); OnPropertyChanged(); Bubble(); }
    }

    // ── Where it sticks ──────────────────────────────────────────────
    //
    // Anchors and a pivot, put the way an author thinks about them: which part
    // of its parent this object sticks to, and whether it fills the parent's
    // width or height. Until 1.8.3 the panel said "from its anchors" with no
    // way to see or set them, so a screen with anything in a corner could only
    // be written by hand.

    /// <summary>One of the nine places in the "stick to" grid. It is lit when
    /// the object sticks there; an object filling its parent's width lights the
    /// whole row, one filling the height the whole column.</summary>
    public sealed class StickCell : ObservableObject
    {
        private readonly UiNodeViewModel _node;
        private readonly float _x, _y;

        internal StickCell(UiNodeViewModel node, float x, float y)
        {
            _node = node;
            _x = x;
            _y = y;
            Pick = new RelayCommand(() => _node.StickTo(_x, _y));
        }

        public bool IsOn => _node.IsStuckTo(_x, _y);
        public RelayCommand Pick { get; }
        internal void Refresh() => OnPropertyChanged(nameof(IsOn));
    }

    /// <summary>Top left to bottom right, as they are drawn.</summary>
    public IReadOnlyList<StickCell> StickCells
        => _stickCells ??= new[] { 1f, 0.5f, 0f }
            .SelectMany(y => new[] { 0f, 0.5f, 1f }.Select(x => new StickCell(this, x, y)))
            .ToArray();

    private StickCell[]? _stickCells;

    /// <summary>
    /// Stick to one of the nine places: flush against it, because that is what
    /// picking a corner means and it shows at once in the picture. An axis the
    /// object fills stays filled, so with the width filled this picks the top,
    /// middle or bottom of the parent to run along. An object filling both is
    /// let go first, or the click would do nothing at all.
    /// </summary>
    public void StickTo(float x, float y)
    {
        if (FillsWidth && FillsHeight)
        {
            ReleaseFill(0);
            ReleaseFill(1);
        }
        var r = Model.Rect;
        if (!FillsWidth) StickAxis(r, 0, x);
        if (!FillsHeight) StickAxis(r, 1, y);
        PlaceChanged();
    }

    private static void StickAxis(UiRectDef r, int axis, float at)
    {
        Set(r.AnchorMin, axis, at);
        Set(r.AnchorMax, axis, at);
        Set(r.Pivot, axis, at);
        Set(r.Position, axis, 0f);
    }

    private bool IsStuckTo(float x, float y) => AxisAt(0, x) && AxisAt(1, y);

    private bool AxisAt(int axis, float at)
        => Fills(axis) || (Near(At(Model.Rect.AnchorMin, axis), at) && Near(At(Model.Rect.AnchorMax, axis), at));

    private bool Fills(int axis)
        => Near(At(Model.Rect.AnchorMin, axis), 0f) && Near(At(Model.Rect.AnchorMax, axis), 1f);

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0005f;

    /// <summary>Fill the parent's width, flush with both sides until margins
    /// are given.</summary>
    public bool FillsWidth { get => Fills(0); set => SetFill(0, value); }

    /// <summary>Fill the parent's height.</summary>
    public bool FillsHeight { get => Fills(1); set => SetFill(1, value); }

    private void SetFill(int axis, bool fill)
    {
        if (Fills(axis) == fill) return;
        if (fill)
        {
            var r = Model.Rect;
            _sizeBeforeFill[axis] = At(r.Size, axis);
            Set(r.AnchorMin, axis, 0f);
            Set(r.AnchorMax, axis, 1f);
            Set(r.Position, axis, 0f);
            Set(r.Size, axis, 0f);
        }
        else ReleaseFill(axis);
        PlaceChanged();
    }

    /// <summary>Back to the middle of the parent, at the size it had. Filling
    /// leaves no size behind, and one of 0 would make it vanish.</summary>
    private void ReleaseFill(int axis)
    {
        var r = Model.Rect;
        StickAxis(r, axis, 0.5f);
        Set(r.Size, axis, _sizeBeforeFill[axis] > 0f ? _sizeBeforeFill[axis] : DefaultSizeAfterFill);
    }

    private const float DefaultSizeAfterFill = 200f;
    private readonly float[] _sizeBeforeFill = new float[2];

    // An axis that fills is set by how far in each side stays, which is how
    // Unity's own offsets read; underneath it is still a position and a size.
    public float MarginLeft { get => MarginLow(0); set => SetMargins(0, value, MarginHigh(0)); }
    public float MarginRight { get => MarginHigh(0); set => SetMargins(0, MarginLow(0), value); }
    public float MarginBottom { get => MarginLow(1); set => SetMargins(1, value, MarginHigh(1)); }
    public float MarginTop { get => MarginHigh(1); set => SetMargins(1, MarginLow(1), value); }

    private float MarginLow(int axis)
        => At(Model.Rect.Position, axis) - At(Model.Rect.Size, axis) * At(Model.Rect.Pivot, axis);

    private float MarginHigh(int axis)
        => -(At(Model.Rect.Position, axis) + At(Model.Rect.Size, axis) * (1f - At(Model.Rect.Pivot, axis)));

    private void SetMargins(int axis, float low, float high)
    {
        var r = Model.Rect;
        float size = -(low + high);
        Set(r.Size, axis, size);
        Set(r.Position, axis, low + size * At(r.Pivot, axis));
        PlaceChanged();
    }

    // The numbers themselves, for anchors between the nine places: the game's
    // own screens have them, and they must stay readable and editable.
    public float AnchorMinX { get => At(Model.Rect.AnchorMin, 0); set { Set(Model.Rect.AnchorMin, 0, value); PlaceChanged(); } }
    public float AnchorMinY { get => At(Model.Rect.AnchorMin, 1); set { Set(Model.Rect.AnchorMin, 1, value); PlaceChanged(); } }
    public float AnchorMaxX { get => At(Model.Rect.AnchorMax, 0); set { Set(Model.Rect.AnchorMax, 0, value); PlaceChanged(); } }
    public float AnchorMaxY { get => At(Model.Rect.AnchorMax, 1); set { Set(Model.Rect.AnchorMax, 1, value); PlaceChanged(); } }
    public float PivotX { get => At(Model.Rect.Pivot, 0); set { Set(Model.Rect.Pivot, 0, value); PlaceChanged(); } }
    public float PivotY { get => At(Model.Rect.Pivot, 1); set { Set(Model.Rect.Pivot, 1, value); PlaceChanged(); } }

    /// <summary>Anchors between the nine places on some axis - across part of
    /// its parent. The grid lights nothing for that, and its Width is then how
    /// much bigger than that part it is, which nobody would guess.</summary>
    public bool StretchesPartly => PartlyOn(0) || PartlyOn(1);

    private bool PartlyOn(int axis)
        => !Fills(axis) && !Near(At(Model.Rect.AnchorMin, axis), At(Model.Rect.AnchorMax, axis));

    /// <summary>Whether its parent arranges it - a row, a column or a grid -
    /// in which case the parent decides where it goes and these do nothing.</summary>
    public bool IsArranged => Parent?.Arranges == true;

    private void PlaceChanged()
    {
        OnPropertyChanged(string.Empty);
        RefreshStickCells();
        Bubble();
    }

    private void RefreshStickCells()
    {
        if (_stickCells == null) return;
        foreach (var cell in _stickCells) cell.Refresh();
    }

    // ── What the game has here ───────────────────────────────────────
    //
    // On one of the game's objects "none" does not mean the same as on the
    // pack's own. A setting left out of the pack leaves the game's value
    // standing, so taking something off has to be said in a way the save keeps
    // and the game obeys - or the preview shows it gone and the game does not.

    /// <summary>This row's object in the game, when it is one of the game's and
    /// the screen is known.</summary>
    private VanillaUiSurface.Node? GameNode => IsVanilla ? _gameNode?.Invoke(Model) : null;

    // ── Picture ──────────────────────────────────────────────────────

    public bool HasImage => !string.IsNullOrEmpty(Model.Image?.Sprite);

    /// <summary>Unity's own names for how a picture fills its rectangle.</summary>
    public static IReadOnlyList<string> ImageTypes { get; } = new[] { "Simple", "Sliced", "Tiled", "Filled" };

    public string ImageType
    {
        get => Model.Image?.Type ?? "Simple";
        set
        {
            if (Model.Image == null || Model.Image.Type == value) return;
            Model.Image.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSliced));
            OnPropertyChanged(nameof(Summary));
            Bubble();
        }
    }

    public bool IsSliced => string.Equals(ImageType, "Sliced", StringComparison.OrdinalIgnoreCase);

    public bool KeepsAspect
    {
        get => Model.Image?.PreserveAspect == true;
        set { if (Model.Image != null) { Model.Image.PreserveAspect = value; OnPropertyChanged(); Bubble(); } }
    }

    public bool FillsCenter
    {
        get => Model.Image?.FillCenter != false;
        set { if (Model.Image != null) { Model.Image.FillCenter = value; OnPropertyChanged(); Bubble(); } }
    }

    public float BorderScale
    {
        get => Model.Image?.PixelsPerUnitMultiplier ?? 1f;
        set { if (Model.Image != null && value > 0f) { Model.Image.PixelsPerUnitMultiplier = value; OnPropertyChanged(); Bubble(); } }
    }

    public bool CatchesMouse
    {
        get => Model.Image?.RaycastTarget != false;
        set { if (Model.Image != null) { Model.Image.RaycastTarget = value; OnPropertyChanged(); Bubble(); } }
    }

    /// <summary>Opacity of this object and everything under it, as typed - empty
    /// for none, which is not the same as 1: see <see cref="UiNodeDef.Alpha"/>.</summary>
    public string Opacity
    {
        get => Model.Alpha?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set
        {
            float? parsed = float.TryParse((value ?? "").Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                           System.Globalization.CultureInfo.InvariantCulture, out var f)
                ? Math.Clamp(f, 0f, 1f) : null;

            // Emptied on an object the game fades: back to the game's own value,
            // since the game keeps it whatever the pack leaves out.
            parsed ??= GameNode?.CanvasGroup?.Alpha;
            if (Model.Alpha == parsed) return;
            Model.Alpha = parsed;
            OnPropertyChanged();
            Bubble();
        }
    }

    // ── Shadow and outline ───────────────────────────────────────────

    public bool HasShadow
    {
        get => IsOn(Model.Shadow);
        set { Model.Shadow = Switched(Model.Shadow, value, ShadowDefault, GameNode?.Shadow != null); RefreshAll(); }
    }

    public string ShadowColor { get => Model.Shadow?.Color ?? ""; set => SetEffectColor(Model.Shadow, value); }
    public float ShadowX { get => At(Model.Shadow?.Distance, 0); set => SetEffectDistance(Model.Shadow, 0, value); }
    public float ShadowY { get => At(Model.Shadow?.Distance, 1); set => SetEffectDistance(Model.Shadow, 1, value); }
    public System.Windows.Media.Brush ShadowColorBrush => BrushFor(ShadowColor);

    public bool HasOutline
    {
        get => IsOn(Model.Outline);
        set { Model.Outline = Switched(Model.Outline, value, OutlineDefault, GameNode?.Outline != null); RefreshAll(); }
    }

    public string OutlineColor { get => Model.Outline?.Color ?? ""; set => SetEffectColor(Model.Outline, value); }
    public float OutlineX { get => At(Model.Outline?.Distance, 0); set => SetEffectDistance(Model.Outline, 0, value); }
    public float OutlineY { get => At(Model.Outline?.Distance, 1); set => SetEffectDistance(Model.Outline, 1, value); }
    public System.Windows.Media.Brush OutlineColorBrush => BrushFor(OutlineColor);

    private const string ShadowDefault = "#00000080", OutlineDefault = "#00000070", EffectOff = "#00000000";

    private static bool IsOn(UiEffectDef? effect) => effect != null && Rendering.UiColor.Parse(effect.Color).A != 0;

    /// <summary>
    /// Switching one on starts it at the game's usual look. Switching one off
    /// removes it - except where the game's object has one of its own, which
    /// having none would leave standing, so it is made clear instead: the one
    /// way a pack can take it off.
    /// </summary>
    private static UiEffectDef? Switched(UiEffectDef? effect, bool on, string colour, bool gameHasOne)
    {
        if (on)
        {
            var made = effect ?? new UiEffectDef { Distance = new[] { 2f, -2f } };
            if (Rendering.UiColor.Parse(made.Color).A == 0) made.Color = colour;
            return made;
        }
        if (!gameHasOne) return null;
        var clear = effect ?? new UiEffectDef { Distance = new[] { 2f, -2f } };
        clear.Color = EffectOff;
        return clear;
    }

    private void SetEffectColor(UiEffectDef? effect, string value)
    {
        if (effect == null || effect.Color == value) return;
        effect.Color = value;
        OnPropertyChanged(string.Empty);
        Bubble();
    }

    private void SetEffectDistance(UiEffectDef? effect, int i, float value)
    {
        if (effect == null) return;
        Set(effect.Distance, i, value);
        OnPropertyChanged(string.Empty);
        Bubble();
    }

    // ── What it holds ────────────────────────────────────────────────

    /// <summary>Clip what is inside to this object's rectangle - what lets a
    /// highlight run along a line and vanish at its ends.</summary>
    public bool ClipsChildren
    {
        get => Model.ClipChildren;
        set { if (Model.ClipChildren == value || !CanChangeClip) return; Model.ClipChildren = value; OnPropertyChanged(); Bubble(); }
    }

    /// <summary>A pack can add a clip to one of the game's objects but not take
    /// the game's own off, so where it has one the box stays ticked and says
    /// why. Offered, it would have shown one thing here and another in the game.</summary>
    public bool CanChangeClip => GameNode?.Clips != true;

    public ObservableCollection<ComponentRowViewModel> Components { get; }
    public RelayCommand AddComponentCommand { get; }

    private void AddComponent()
    {
        var def = new ComponentDef();
        Model.Components.Add(def);
        Components.Add(new ComponentRowViewModel(def, RemoveComponent));
        Bubble();
    }

    private void RemoveComponent(ComponentRowViewModel row)
    {
        Model.Components.Remove(row.Model);
        Components.Remove(row);
        Bubble();
    }

    // ── Read at a glance ─────────────────────────────────────────────
    //
    // A closed section says what is in it, so an object can be read without
    // opening anything, and what it does is the first thing anyone wants to
    // know about it.

    /// <summary>What this object is, in a word.</summary>
    public string KindLabel
        => Loc.T(IsClickable ? "ui.kind.button"
                 : HasImage ? "ui.kind.picture"
                 : HasText ? "ui.kind.text"
                 : Children.Count > 0 ? "ui.kind.group"
                 : "ui.kind.empty");

    public string PlaceSummary
        => IsArranged ? Loc.T("ui.summary.arranged")
           : FillsWidth && FillsHeight ? Loc.T("ui.summary.fills")
           : FillsWidth ? Loc.T("ui.fillWidth")
           : FillsHeight ? Loc.T("ui.fillHeight")
           : $"{Width:0} × {Height:0}";

    public string LooksSummary
    {
        get
        {
            var parts = new List<string>();
            if (HasImage) parts.Add(Loc.T("ui.kind.picture"));
            if (HasText) parts.Add(Loc.T("ui.kind.text"));
            return parts.Count == 0 ? Loc.T("ui.summary.nothing") : string.Join(" · ", parts);
        }
    }

    public string ShowsSummary
        => IsGated ? Loc.P("ui.summary.conditions", Model.ActiveConditions.Count)
           : Loc.T(StartActive ? "ui.summary.always" : "ui.summary.startsHidden");

    public string ClicksSummary
        => IsClickable ? Loc.P("ui.summary.actions", Model.OnClick.Count) : Loc.T("ui.summary.nothing");

    public string MovesSummary
    {
        get
        {
            var parts = new List<string>();
            if (Model.Open is { DoesAnything: true }) parts.Add(Loc.T("ui.summary.arrives"));
            if (Model.Close is { DoesAnything: true }) parts.Add(Loc.T("ui.summary.leaves"));
            if (Loops) parts.Add(Loc.T("ui.summary.keepsMoving"));
            return parts.Count == 0 ? Loc.T("ui.summary.nothing") : string.Join(" · ", parts);
        }
    }

    public string InsideSummary
    {
        get
        {
            var parts = new List<string>();
            if (Arranges) parts.Add(Arrange);
            if (ClipsChildren) parts.Add(Loc.T("ui.summary.clips"));
            return parts.Count == 0 ? Loc.T("ui.summary.nothing") : string.Join(" · ", parts);
        }
    }

    public string ComponentsSummary
        => Components.Count == 0 ? Loc.T("ui.summary.nothing") : Loc.P("ui.node.components", Components.Count);

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
                // Stored empty on an object the game draws a picture on, which
                // is how the game is told to remove it: having none would leave
                // the game's picture standing while the preview showed it gone.
                if (GameNode?.Image != null) Model.Image.Sprite = "";
                else Model.Image = null;
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

    public bool HasText => !string.IsNullOrEmpty(Model.Text?.Value);

    public string Text
    {
        get => Model.Text?.Value ?? "";
        set
        {
            // As with a picture: stored empty where the game's object has words.
            if (string.IsNullOrEmpty(value) && Model.Text != null)
            {
                if (GameNode?.Text != null) Model.Text.Value = "";
                else Model.Text = null;
            }
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

    /// <summary>TextMeshPro's own names, the ones the game's screens use. The
    /// box takes any other of them typed in.</summary>
    public static IReadOnlyList<string> TextAlignments { get; } = new[]
    {
        "TopLeft", "Top", "TopRight",
        "Left", "Center", "Right",
        "BottomLeft", "Bottom", "BottomRight",
        "MidlineLeft", "Midline", "MidlineRight",
    };

    public string TextAlignment
    {
        get => Model.Text?.Alignment ?? "Center";
        set { if (Model.Text != null && !string.IsNullOrEmpty(value) && Model.Text.Alignment != value) { Model.Text.Alignment = value; OnPropertyChanged(); Bubble(); } }
    }

    public bool WrapsText
    {
        get => Model.Text?.Wrap != false;
        set { if (Model.Text != null) { Model.Text.Wrap = value; OnPropertyChanged(); Bubble(); } }
    }

    public bool ShrinksToFit
    {
        get => Model.Text?.AutoSize == true;
        set { if (Model.Text != null && CanChangeShrink) { Model.Text.AutoSize = value; OnPropertyChanged(); Bubble(); } }
    }

    /// <summary>A pack can make text shrink but not stop the game's own from doing
    /// so, as with clipping.</summary>
    public bool CanChangeShrink => GameNode?.Text?.AutoSizes != true;

    /// <summary>A shadow or an outline on an object with only words on it: the
    /// game draws neither on text, so it is worth saying so.</summary>
    public bool EffectsDoNothing => HasText && !HasImage && (HasShadow || HasOutline);

    public float ShrinkMin
    {
        get => Model.Text?.AutoSizeMin ?? 12f;
        set { if (Model.Text != null) { Model.Text.AutoSizeMin = value; OnPropertyChanged(); Bubble(); } }
    }

    public float ShrinkMax
    {
        get => Model.Text?.AutoSizeMax ?? 72f;
        set { if (Model.Text != null) { Model.Text.AutoSizeMax = value; OnPropertyChanged(); Bubble(); } }
    }

    public float LineSpacing
    {
        get => Model.Text?.LineSpacing ?? 0f;
        set { if (Model.Text != null) { Model.Text.LineSpacing = value; OnPropertyChanged(); Bubble(); } }
    }

    public float LetterSpacing
    {
        get => Model.Text?.CharacterSpacing ?? 0f;
        set { if (Model.Text != null) { Model.Text.CharacterSpacing = value; OnPropertyChanged(); Bubble(); } }
    }

    // ── Children ─────────────────────────────────────────────────────

    /// <summary>Add an object of the pack's own, inside this one.</summary>
    public UiNodeViewModel AddChild(string? name = null)
        => AddChild(new UiNodeDef { Name = name ?? Loc.T("ui.node.newName") });

    /// <summary>Adopt a ready-made object - a template, usually - as a child of
    /// this one. Its name is made unique here rather than by the template,
    /// which has no idea what else is already in the tree.</summary>
    public UiNodeViewModel AddChild(UiNodeDef def)
    {
        def.Name = Unique(string.IsNullOrEmpty(def.Name) ? Loc.T("ui.node.newName") : def.Name);
        Model.Children.Add(def);
        var vm = new UiNodeViewModel(def, RemoveChild, _hasChanges, _reset, _gameNode, _hiddenChanged);
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
        RefreshStickCells();
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
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(EffectsDoNothing));
        OnPropertyChanged(nameof(PlaceSummary));
        OnPropertyChanged(nameof(LooksSummary));
        OnPropertyChanged(nameof(ShowsSummary));
        OnPropertyChanged(nameof(ClicksSummary));
        OnPropertyChanged(nameof(MovesSummary));
        OnPropertyChanged(nameof(InsideSummary));
        OnPropertyChanged(nameof(ComponentsSummary));
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
