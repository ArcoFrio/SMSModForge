using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Services;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps a <see cref="GameObjectDef"/> with INPC for the Places editor's unified
/// GameObjects tree. One node type covers everything: a sprite object, a
/// sprite-less container, or the forced NPCs-root node. Two-way bound by the
/// per-row editor (name, sprite path, transform, sorting, flags), and owns its
/// nested child GameObjects and NPC placements so one tree renders recursively.
/// </summary>
public sealed class GameObjectViewModel : ObservableObject
{
    private readonly Action<GameObjectViewModel>? _removeCallback;
    private readonly bool _parentInNpcSubtree;
    private readonly ObservableCollection<GameObjectViewModel>? _parentCollection;

    public GameObjectDef Model { get; }

    public GameObjectViewModel(GameObjectDef model, Action<GameObjectViewModel>? removeCallback = null,
                               bool parentInNpcSubtree = false, ObservableCollection<GameObjectViewModel>? parentCollection = null)
    {
        Model = model;
        _removeCallback = removeCallback;
        _parentInNpcSubtree = parentInNpcSubtree;
        _parentCollection = parentCollection;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => _removeCallback != null && !IsNpcRoot);
        Components = new ObservableCollection<ComponentRowViewModel>(
            model.Components.Select(c => new ComponentRowViewModel(c, RemoveComponent)));
        AddComponentCommand = new RelayCommand(() => AddComponent());
        Children = new ObservableCollection<GameObjectViewModel>(
            model.Children.Select(c => new GameObjectViewModel(c, RemoveChild, IsInNpcSubtree)));
        AddChildCommand = new RelayCommand(() => AddChild());
        Npcs = new ObservableCollection<NpcPlacementViewModel>(
            model.Npcs.Select(n => new NpcPlacementViewModel(n, RemoveNpc)));
        AddNpcCommand = new RelayCommand(() => AddNpc());
        // Polled every frame by the runtime, so a one-shot Random roll here
        // would re-roll constantly — same context the integration rules use.
        ActiveConditions = new ObservableCollection<NodeConditionViewModel>(
            model.ActiveConditions.Select(c => new NodeConditionViewModel(
                c, RemoveActiveCondition, context: ConditionContext.Rule)));
        AddActiveConditionCommand = new RelayCommand(() => AddActiveCondition());
        CopyCommand = new RelayCommand(() => EditorClipboard.SetItem(Model), () => Model != null);
        PasteCommand = new RelayCommand(() => PasteAsSibling(), () => EditorClipboard.Has<GameObjectDef>());
    }

    // ── Activation conditions ─────────────────────────────────────────────
    /// <summary>Conditions driving whether this GameObject is active. Empty =
    /// it just keeps <see cref="StartActive"/> forever.</summary>
    public ObservableCollection<NodeConditionViewModel> ActiveConditions { get; }
    public RelayCommand AddActiveConditionCommand { get; }

    public NodeConditionViewModel AddActiveCondition()
    {
        var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
        Model.ActiveConditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveActiveCondition, context: ConditionContext.Rule);
        ActiveConditions.Add(vm);
        OnPropertyChanged(nameof(HasActiveConditions));
        RefreshVanillaChange();
        return vm;
    }

    public void RemoveActiveCondition(NodeConditionViewModel vm)
    {
        Model.ActiveConditions.Remove(vm.Model);
        ActiveConditions.Remove(vm);
        OnPropertyChanged(nameof(HasActiveConditions));
        RefreshVanillaChange();
    }

    /// <summary>True once the object is condition-gated — the "switch back off"
    /// choice only means anything then.</summary>
    public bool HasActiveConditions => ActiveConditions.Count > 0;

    /// <summary>Switch the object back off when the conditions stop passing
    /// (continuous gating), vs latching it on the first time they pass.</summary>
    public bool DeactivateWhenUnmet
    {
        get => Model.DeactivateWhenUnmet;
        set { Model.DeactivateWhenUnmet = value; OnPropertyChanged(); }
    }

    // ── Bind to an existing object ────────────────────────────────────────

    /// <summary>Reach an object that already exists in the scene instead of
    /// creating one — the way a pack modifies a level it doesn't own.</summary>
    public bool Bind
    {
        get => Model.Bind;
        set
        {
            if (Model.Bind == value) return;
            Model.Bind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCreated));
            // Binding a node named "NPCs" turns it into the NPC host, which the
            // "+ Add NPC child" button keys off.
            OnPropertyChanged(nameof(IsNpcHost));
            OnPropertyChanged(nameof(CanHostNpcs));
            RefreshVanillaChange();
        }
    }

    /// <summary>Inverse of <see cref="Bind"/> — the sprite / sorting / parallax
    /// fields only mean something for an object this pack actually creates.</summary>
    public bool IsCreated => !Model.Bind;

    /// <summary>
    /// True while this node actually changes the vanilla object it's bound to.
    /// Compared live against the seeded baseline — the override flags are only
    /// computed at save, so relying on those would leave the row unmarked until
    /// you saved. False for objects the pack creates: those are additions, not
    /// changes to the game's own scene.
    /// </summary>
    public bool HasVanillaChange => VanillaDelta.IsLiveVanillaChange(Model);

    /// <summary>Re-read <see cref="HasVanillaChange"/>. Called from every edit
    /// that can flip it, so the row's highlight tracks typing.</summary>
    public void RefreshVanillaChange() => OnPropertyChanged(nameof(HasVanillaChange));

    /// <summary>True when there's a vanilla state to go back to — a bound node
    /// with a baseline attached from the extracted catalog.</summary>
    public bool CanResetToVanilla => Model.Bind && Model.Baseline != null;

    /// <summary>Discard this node's edits and restore the values the vanilla
    /// level actually has. Not a reset to zero — the baseline is the extracted
    /// object's own transform, active state and sorting.</summary>
    public RelayCommand ResetToVanillaCommand => _resetToVanilla ??= new RelayCommand(
        () => { if (VanillaDelta.ResetToBaseline(Model)) RefreshFromModel(); },
        () => CanResetToVanilla);
    private RelayCommand? _resetToVanilla;

    /// <summary>
    /// Re-read every field something rewrote on the model behind this VM's back.
    /// Needed after <see cref="VanillaDelta.Rebase"/>, which anchors a bound node
    /// to its baseline by assigning the model directly — the property setters
    /// never run, so nothing would raise <c>PropertyChanged</c> and the boxes,
    /// the preview and the gizmo would all keep showing the stale defaults.
    /// </summary>
    public void RefreshFromModel()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(RotationZ));
        OnPropertyChanged(nameof(ScaleX));
        OnPropertyChanged(nameof(ScaleY));
        OnPropertyChanged(nameof(StartActive));
        OnPropertyChanged(nameof(SortingOrder));
        OnPropertyChanged(nameof(PreviewSprite));
        RefreshVanillaChange();
    }

    public bool OverrideTransform
    {
        get => Model.OverrideTransform;
        set { Model.OverrideTransform = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public bool OverrideActive
    {
        get => Model.OverrideActive;
        set { Model.OverrideActive = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    /// <summary>
    /// The level's NPCs container. Either the forced role node a pack place
    /// carries, or — in a vanilla extension — the level's own "NPCs" object,
    /// reached by BINDING rather than by role, which is why matching on the
    /// role alone left an extension with no way to place an NPC.
    /// <para/>
    /// Deliberately not folded into <see cref="IsNpcRoot"/>: that one marks the
    /// node the pack owns and cannot delete, and a bound node stays removable.
    /// </summary>
    public bool IsNpcHost => IsNpcRoot ||
        (Model.Bind && string.Equals(Model.Name, "NPCs", System.StringComparison.OrdinalIgnoreCase));

    /// <summary>True for the NPCs container and everything nested under it —
    /// the part of the tree whose transforms compose locally and where NPC
    /// placements belong.</summary>
    public bool IsInNpcSubtree => IsNpcHost || _parentInNpcSubtree;

    /// <summary>Only nodes inside the NPCs subtree offer "+ Add NPC child".</summary>
    public bool CanHostNpcs => IsInNpcSubtree;

    /// <summary>Removes this GameObject row from its parent (place or parent node).
    /// Disabled for the forced NPCs-root node.</summary>
    public RelayCommand RemoveCommand { get; }

    /// <summary>Deep-clone this GameObjectDef into the editor clipboard for paste elsewhere.</summary>
    public RelayCommand CopyCommand { get; }

    /// <summary>Paste the clipboard's GameObjectDef as a sibling of this row.</summary>
    public RelayCommand PasteCommand { get; }

    /// <summary>True for the forced NPCs-container node — its name is locked and
    /// it can't be removed, but it still carries a transform, components, and
    /// children.</summary>
    public bool IsNpcRoot => Model.IsNpcRoot;

    /// <summary>False for the forced NPCs-root node (name locked to <c>NPCs</c>).</summary>
    public bool CanEditIdentity => !IsNpcRoot;

    // ── Nested child GameObjects (hierarchy) ──────────────────────────────
    /// <summary>GameObjects nested under this one. Same VM type, so the editor
    /// renders them recursively with the full set of controls.</summary>
    public ObservableCollection<GameObjectViewModel> Children { get; }
    public RelayCommand AddChildCommand { get; }

    public GameObjectViewModel AddChild()
    {
        var def = new GameObjectDef();
        Model.Children.Add(def);
        var vm = new GameObjectViewModel(def, RemoveChild, IsInNpcSubtree, Children);
        Children.Add(vm);
        return vm;
    }

    public void RemoveChild(GameObjectViewModel vm)
    {
        Model.Children.Remove(vm.Model);
        Children.Remove(vm);
    }

    /// <summary>Paste the clipboard's GameObjectDef as a sibling of this row.</summary>
    public void PasteAsSibling()
    {
        var cloned = EditorClipboard.GetItem<GameObjectDef>();
        if (cloned == null) return;
        // Generate a unique name to avoid conflicts.
        var baseName = cloned.Name ?? "GameObject";
        var existingNames = _parentCollection?.Select(g => g.Model.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)) ?? Array.Empty<string>();
        cloned.Name = UniqueName(baseName, existingNames);
        // Insert after this row in the parent collection.
        if (_parentCollection != null)
        {
            var idx = _parentCollection.IndexOf(this);
            _parentCollection.Insert(Math.Min(idx + 1, _parentCollection.Count), new GameObjectViewModel(cloned, null, IsInNpcSubtree, _parentCollection));
            Model.Children.Add(cloned);
        }
        else
        {
            // No parent collection — can't paste.
        }
    }

    private static string UniqueName(string baseName, IEnumerable<string> existing)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "GameObject";
        var set = new HashSet<string>(existing, System.StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(baseName)) return baseName;
        var root = System.Text.RegularExpressions.Regex.Replace(baseName, @"_copy\d*$", "");
        for (int i = 1; ; i++)
        {
            var cand = i == 1 ? root + "_copy" : root + "_copy" + i;
            if (!set.Contains(cand)) return cand;
        }
    }

    // ── NPC placements parented here ──────────────────────────────────────
    /// <summary>NPC placements hung directly under this GameObject.</summary>
    public ObservableCollection<NpcPlacementViewModel> Npcs { get; }
    public RelayCommand AddNpcCommand { get; }

    public NpcPlacementViewModel AddNpc()
    {
        var def = new NpcPlacementDef();
        Model.Npcs.Add(def);
        var vm = new NpcPlacementViewModel(def, RemoveNpc);
        Npcs.Add(vm);
        RefreshVanillaChange();
        return vm;
    }

    public void RemoveNpc(NpcPlacementViewModel vm)
    {
        Model.Npcs.Remove(vm.Model);
        Npcs.Remove(vm);
        RefreshVanillaChange();
    }

    // ── Utility components ────────────────────────────────────────────────
    public ObservableCollection<ComponentRowViewModel> Components { get; }
    public RelayCommand AddComponentCommand { get; }

    public ComponentRowViewModel AddComponent()
    {
        var def = new ComponentDef();
        Model.Components.Add(def);
        var vm = new ComponentRowViewModel(def, RemoveComponent);
        Components.Add(vm);
        RefreshVanillaChange();
        return vm;
    }

    public void RemoveComponent(ComponentRowViewModel vm)
    {
        Model.Components.Remove(vm.Model);
        Components.Remove(vm);
        RefreshVanillaChange();
    }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Sprite
    {
        get => Model.Sprite;
        set { Model.Sprite = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewSprite)); }
    }

    /// <summary>What the preview draws — the pack's sprite, else this object's
    /// extracted vanilla art (see <see cref="GameObjectDef.VanillaArtPath"/>).</summary>
    public string PreviewSprite => Model.PreviewSprite;

    public float X
    {
        get => Model.X;
        set { Model.X = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public float Y
    {
        get => Model.Y;
        set { Model.Y = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public float RotationZ
    {
        get => Model.RotationZ;
        set { Model.RotationZ = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public float ScaleX
    {
        get => Model.ScaleX;
        set { Model.ScaleX = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public float ScaleY
    {
        get => Model.ScaleY;
        set { Model.ScaleY = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public int SortingOrder
    {
        get => Model.SortingOrder;
        set { Model.SortingOrder = value; OnPropertyChanged(); }
    }

    public bool ParallaxDisabled
    {
        get => Model.ParallaxDisabled;
        set { Model.ParallaxDisabled = value; OnPropertyChanged(); }
    }

    public bool StartActive
    {
        get => Model.StartActive;
        set { Model.StartActive = value; OnPropertyChanged(); RefreshVanillaChange(); }
    }

    public float StartAlpha
    {
        get => Model.StartAlpha;
        set { Model.StartAlpha = value; OnPropertyChanged(); }
    }

    /// <summary>Renderer tint, seeded from the vanilla object for a bound node.</summary>
    public string Tint
    {
        get => Model.Tint;
        set { Model.Tint = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTint)); }
    }

    public bool HasTint => Model.HasTint;

    public string Mask
    {
        get => Model.Mask;
        set { Model.Mask = value; OnPropertyChanged(); }
    }

    /// <summary>Short label for headers / tooltips.</summary>
    public string Display => string.IsNullOrWhiteSpace(Name) ? "(unnamed GameObject)" : Name;
}
