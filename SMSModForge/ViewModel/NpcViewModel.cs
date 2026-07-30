using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Shared contract between <see cref="OutfitViewModel"/> and <see cref="NpcViewModel"/>
/// so the mask editor can paint either without knowing the source type.
/// </summary>
public interface IMaskEditorHost
{
    /// <summary>Pack-local key used in the save dialog's default filename.</summary>
    string Key { get; }
    /// <summary>Path (relative to pack root) to the pose / base sprite PNG.</summary>
    string PoseSpritePath { get; }
    /// <summary>Path (relative to pack root) to the jiggle-mask PNG.</summary>
    string MaskPath { get; set; }
    /// <summary>In-progress mask buffer published by the mask editor.</summary>
    byte[]? LiveMaskBgra { get; set; }
    /// <summary>Bumped whenever the live buffer changes.</summary>
    int LiveMaskRevision { get; set; }

    /// <summary>Which shader this mask feeds. Busts and NPCs are the default;
    /// a level mask is authored in alpha instead. Defaulted so the NPC hosts
    /// need say nothing.</summary>
    Rendering.MaskKind MaskKind => Rendering.MaskKind.BustRgb;
}

/// <summary>
/// INPC wrapper around an <see cref="NpcDef"/> for the NPCs tab. Exposes the
/// jiggle / blink / shadow / wet sub-objects as flat properties so the view
/// can bind sliders and boxes directly, mirroring <c>OutfitViewModel</c>.
/// </summary>
public sealed class NpcViewModel : ObservableObject, IMaskEditorHost
{
    public NpcDef Model { get; }

    public NpcViewModel(NpcDef model) { Model = model; }

    public string Key
    {
        get => Model.Key;
        set { if (Model.Key == value) return; Model.Key = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { Model.DisplayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Sprite
    {
        get => Model.Sprite;
        set { Model.Sprite = value; OnPropertyChanged(); }
    }

    public string Mask
    {
        get => Model.Mask;
        set { Model.Mask = value; OnPropertyChanged(); }
    }

    public int SortingOrder
    {
        get => Model.SortingOrder;
        set { Model.SortingOrder = value; OnPropertyChanged(); }
    }

    // ── Jiggle ───────────────────────────────────────────────────────────
    public float JiggleSpeed     { get => Model.Jiggle.Speed;         set { Model.Jiggle.Speed = value; OnPropertyChanged(); } }
    public float JiggleStrength  { get => Model.Jiggle.Strength;      set { Model.Jiggle.Strength = value; OnPropertyChanged(); } }
    public float JiggleFrequency { get => Model.Jiggle.Frequency;     set { Model.Jiggle.Frequency = value; OnPropertyChanged(); } }
    public float NoiseScale      { get => Model.Jiggle.NoiseScale;    set { Model.Jiggle.NoiseScale = value; OnPropertyChanged(); } }
    public float NoiseSpeed      { get => Model.Jiggle.NoiseSpeed;    set { Model.Jiggle.NoiseSpeed = value; OnPropertyChanged(); } }
    public float NoiseStrength   { get => Model.Jiggle.NoiseStrength; set { Model.Jiggle.NoiseStrength = value; OnPropertyChanged(); } }
    public string JiggleTint     { get => Model.Jiggle.Tint;          set { Model.Jiggle.Tint = value; OnPropertyChanged(); } }
    public bool PixelSnap        { get => Model.Jiggle.PixelSnap;     set { Model.Jiggle.PixelSnap = value; OnPropertyChanged(); } }

    // ── Blink (art + timing; its transform lives on the placement) ───────
    public string BlinkSprite { get => Model.Blink.Sprite; set { Model.Blink.Sprite = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBlink)); } }
    public bool HasBlink => !string.IsNullOrWhiteSpace(Model.Blink.Sprite);
    public float BlinkMinWait { get => Model.Blink.MinWait; set { Model.Blink.MinWait = value; OnPropertyChanged(); } }
    public float BlinkMaxWait { get => Model.Blink.MaxWait; set { Model.Blink.MaxWait = value; OnPropertyChanged(); } }
    public float BlinkHold    { get => Model.Blink.Hold;    set { Model.Blink.Hold = value; OnPropertyChanged(); } }

    // ── Shadow (colour + order; its transform lives on the placement) ────
    public bool ShadowEnabled   { get => Model.Shadow.Enabled;      set { Model.Shadow.Enabled = value; OnPropertyChanged(); } }
    public string ShadowColor   { get => Model.Shadow.Color;        set { Model.Shadow.Color = value; OnPropertyChanged(); } }
    public int ShadowSortingOrder { get => Model.Shadow.SortingOrder; set { Model.Shadow.SortingOrder = value; OnPropertyChanged(); } }

    // ── Reflection (a mirrored copy of the pose, drawn downward) ─────────
    public bool ReflectionEnabled { get => Model.Reflection.Enabled; set { Model.Reflection.Enabled = value; OnPropertyChanged(); } }
    public float ReflectionAlpha  { get => Model.Reflection.Alpha;   set { Model.Reflection.Alpha = value; OnPropertyChanged(); } }
    public string ReflectionTint  { get => Model.Reflection.Tint;    set { Model.Reflection.Tint = value; OnPropertyChanged(); } }
    public float ReflectionOffsetY { get => Model.Reflection.OffsetY; set { Model.Reflection.OffsetY = value; OnPropertyChanged(); } }
    public int ReflectionSortingOrder { get => Model.Reflection.SortingOrder; set { Model.Reflection.SortingOrder = value; OnPropertyChanged(); } }

    // ── Wet (enabled/active; its emitter transform lives on the placement) ─
    public bool WetEnabled     { get => Model.Wet.Enabled;     set { Model.Wet.Enabled = value; OnPropertyChanged(); } }
    public bool WetStartActive { get => Model.Wet.StartActive; set { Model.Wet.StartActive = value; OnPropertyChanged(); } }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";

    // ── IMaskEditorHost ──────────────────────────────────────────────────

    string IMaskEditorHost.Key => Model.Key;
    public string PoseSpritePath => Sprite;
    public string MaskPath
    {
        get => Mask;
        set => Mask = value;
    }

    private byte[]? _liveMaskBgra;
    public byte[]? LiveMaskBgra
    {
        get => _liveMaskBgra;
        set { _liveMaskBgra = value; OnPropertyChanged(); }
    }

    private int _liveMaskRevision;
    public int LiveMaskRevision
    {
        get => _liveMaskRevision;
        set { _liveMaskRevision = value; OnPropertyChanged(); }
    }
}

/// <summary>
/// INPC wrapper around one <see cref="NpcPlacementDef"/> — a row in a place's
/// NPC-placements list.
/// </summary>
public sealed class NpcPlacementViewModel : ObservableObject
{
    public NpcPlacementDef Model { get; }

    public RelayCommand RemoveCommand { get; }

    /// <summary>The four part transforms (body / shadow / blink / wet), each an
    /// INPC wrapper the editor binds and the gizmo will drive. A change on any
    /// re-raises <see cref="Display"/> and the optional preview hook.</summary>
    public NpcTransformViewModel Body { get; }
    public NpcTransformViewModel Shadow { get; }
    public NpcTransformViewModel Blink { get; }
    public NpcTransformViewModel Wet { get; }

    public NpcPlacementViewModel(NpcPlacementDef model, System.Action<NpcPlacementViewModel>? removeCallback = null,
                                 System.Action? changed = null)
    {
        Model = model;
        RemoveCommand = new RelayCommand(() => removeCallback?.Invoke(this), () => removeCallback != null);
        Body = new NpcTransformViewModel(model.Body, changed);
        Shadow = new NpcTransformViewModel(model.Shadow, changed);
        Blink = new NpcTransformViewModel(model.Blink, changed);
        Wet = new NpcTransformViewModel(model.Wet, changed);
        Components = new System.Collections.ObjectModel.ObservableCollection<ComponentRowViewModel>(
            model.Components.Select(c => new ComponentRowViewModel(c, RemoveComponent)));
        AddComponentCommand = new RelayCommand(() => AddComponent());
        ActiveConditions = new System.Collections.ObjectModel.ObservableCollection<NodeConditionViewModel>(
            model.ActiveConditions.Select(c => new NodeConditionViewModel(c, RemoveCondition)));
        AddActiveConditionCommand = new RelayCommand(() => AddActiveCondition());
        // An NPC's children sit inside the NPCs subtree, so they may host NPCs too.
        Children = new System.Collections.ObjectModel.ObservableCollection<GameObjectViewModel>(
            model.Children.Select(c => new GameObjectViewModel(c, RemoveChild, parentInNpcSubtree: true)));
        AddChildCommand = new RelayCommand(() => AddChild());
    }

    // ── Utility components on the NPC GameObject ─────────────────────────
    /// <summary>Same component vocabulary any GameObject takes. Attached before
    /// the NPC is activated, so a FadeInSprite fades this NPC in on activation.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ComponentRowViewModel> Components { get; }
    public RelayCommand AddComponentCommand { get; }

    // ── Activation conditions for the NPC GameObject itself ─────────────
    /// <summary>Conditions that gate this NPC's active state.</summary>
    public System.Collections.ObjectModel.ObservableCollection<NodeConditionViewModel> ActiveConditions { get; }
    public RelayCommand AddActiveConditionCommand { get; }

    public ComponentRowViewModel AddComponent()
    {
        var def = new ComponentDef();
        Model.Components.Add(def);
        var vm = new ComponentRowViewModel(def, RemoveComponent);
        Components.Add(vm);
        return vm;
    }

    public void RemoveComponent(ComponentRowViewModel vm)
    {
        Model.Components.Remove(vm.Model);
        Components.Remove(vm);
    }

    // ── Activation conditions for the NPC GameObject itself ─────────────
    public NodeConditionViewModel AddActiveCondition()
    {
        var def = new NodeConditionDef();
        Model.ActiveConditions.Add(def);
        var vm = new NodeConditionViewModel(def, RemoveCondition);
        ActiveConditions.Add(vm);
        return vm;
    }

    public void RemoveCondition(NodeConditionViewModel cond)
    {
        Model.ActiveConditions.Remove(cond.Model);
        ActiveConditions.Remove(cond);
    }

    // ── GameObjects parented under the NPC ───────────────────────────────
    /// <summary>Props that ride along with the pose, alongside the built-in
    /// Circle / Blink / Wet parts.</summary>
    public System.Collections.ObjectModel.ObservableCollection<GameObjectViewModel> Children { get; }
    public RelayCommand AddChildCommand { get; }

    public GameObjectViewModel AddChild()
    {
        var def = new GameObjectDef();
        Model.Children.Add(def);
        var vm = new GameObjectViewModel(def, RemoveChild, parentInNpcSubtree: true);
        Children.Add(vm);
        return vm;
    }

    public void RemoveChild(GameObjectViewModel vm)
    {
        Model.Children.Remove(vm.Model);
        Children.Remove(vm);
    }

    public string Npc
    {
        get => Model.Npc;
        set { Model.Npc = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public bool StartActive { get => Model.StartActive; set { Model.StartActive = value; OnPropertyChanged(); } }

    /// <summary>Per-placement sorting order as text, so blank can mean "use the
    /// NPC's own" rather than a numeric zero that would silently pin it.</summary>
    public string SortingOrder
    {
        get => Model.SortingOrder?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        set
        {
            Model.SortingOrder = int.TryParse(value, System.Globalization.NumberStyles.Integer,
                                              System.Globalization.CultureInfo.InvariantCulture, out var v)
                                 ? v : (int?)null;
            OnPropertyChanged();
        }
    }

    public string Display
    {
        get
        {
            string label = string.IsNullOrWhiteSpace(Name) ? Npc : Name;
            return string.IsNullOrWhiteSpace(label) ? "(unset)" : label;
        }
    }

    // ── DeactivateWhenUnmet ────────────────────────────────────────────
    public bool DeactivateWhenUnmet { get => Model.DeactivateWhenUnmet; set { Model.DeactivateWhenUnmet = value; OnPropertyChanged(); } }
}
