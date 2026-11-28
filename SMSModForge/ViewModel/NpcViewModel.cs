using System.Collections.Generic;
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

    /// <summary>
    /// Masks the painter could load over this one, and what to call them.
    /// <para/>
    /// The other outfits of the same character: most of them are the same bust
    /// in different clothes, so one is usually a better starting point than an
    /// empty canvas.
    /// <para/>
    /// Empty by default, and that is the point of the default rather than an
    /// oversight. A PLACE has no character and no siblings — its two masks are
    /// a background and a foreground of one scene, and offering to paste one
    /// over the other is offering a mistake. Not answering this is how a host
    /// says the painter should not show the control at all.
    /// </summary>
    IReadOnlyList<MaskSource> OtherMasks => System.Array.Empty<MaskSource>();
}

/// <summary>One mask the painter could load over the one being edited.</summary>
/// <param name="Label">What to call it in the list — an outfit's own name.</param>
/// <param name="MaskPath">Pack-relative path to its PNG.</param>
public readonly record struct MaskSource(string Label, string MaskPath);

/// <summary>
/// INPC wrapper around an <see cref="NpcDef"/> for the NPCs tab. Exposes the
/// jiggle / blink / shadow / wet sub-objects as flat properties so the view
/// can bind sliders and boxes directly, mirroring <c>OutfitViewModel</c>.
/// </summary>
public sealed class NpcViewModel : ObservableObject, IDerivesKey, IMaskEditorHost
{
    public NpcDef Model { get; }

    public NpcViewModel(NpcDef model)
    {
        Model = model;
        ResetJiggleCommand = new RelayCommand(p =>
        {
            Model.Jiggle.ResetToDefault(p as string ?? "");
            RaiseJiggleChanged();
        });
    }

    /// <summary>Same contract as the outfit editor's — see
    /// <see cref="OutfitViewModel.ResetJiggleCommand"/>. An NPC uses the same
    /// shader and the same defaults, so a Default here means the same thing
    /// it does there.</summary>
    public RelayCommand ResetJiggleCommand { get; }

    private void RaiseJiggleChanged()
    {
        OnPropertyChanged(nameof(JiggleSpeed));
        OnPropertyChanged(nameof(JiggleStrength));
        OnPropertyChanged(nameof(JiggleFrequency));
        OnPropertyChanged(nameof(NoiseScale));
        OnPropertyChanged(nameof(NoiseSpeed));
        OnPropertyChanged(nameof(NoiseStrength));
        OnPropertyChanged(nameof(JiggleTint));
        OnPropertyChanged(nameof(PixelSnap));
    }

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW NPC takes its key from
    // whatever is typed as the display name, editing the key stops that for
    // good, and one loaded from disk follows only while its key is still
    // its display name made into a name (or a duplicate's "_copy").

    private readonly DerivedKey _derivedKey = new();

    /// <summary>Whether the runtime name still follows the display name.</summary>
    public bool KeyIsDerived => _derivedKey.IsDerived;

    /// <summary>Start deriving the key. Called for one the author has just
    /// added, duplicated or pasted.</summary>
    public void DeriveKeyFromDisplayName(System.Func<System.Collections.Generic.IEnumerable<string>> siblingKeys)
        => _derivedKey.Follow(siblingKeys);


    public void WatchDisplayName(System.Func<System.Collections.Generic.IEnumerable<string>> siblingKeys)
        => _derivedKey.Watch(siblingKeys, Model.DisplayName);

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            // Typing a key is a decision, and it sticks.
            _derivedKey.Stop();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            Model.DisplayName = value;
            if (_derivedKey.Next(value, Model.Key) is { } derived && derived != Model.Key)
            {
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
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

    // How its drops look (after 1.10.0, the author: "NPC sets them, placements can change"): every placement starts
    // from these (WetEffectDef.Merged). The game's own where unset; set back to it, unset.

    /// <summary>0 the wet (Anna's towel), 1 the shower. A value it does not have is ignored (warmed pages).</summary>
    public int WetKindIndex
    {
        get => WetEffectDef.ShowerKind(Model.Wet.Kind) ? 1 : 0;
        set
        {
            if (value is not (0 or 1) || value == WetKindIndex) return;
            Model.Wet.Kind = value == 1 ? WetEffectDef.KindShower : null;
            OnPropertyChanged();
        }
    }

    /// <summary>The colour the game's drop colour is multiplied by: "" for none.</summary>
    public string WetTint
    {
        get => Model.Wet.Tint ?? "";
        set
        {
            string v = (value ?? "").Trim();
            Model.Wet.Tint = v.Length == 0 || string.Equals(v, "#FFFFFF", System.StringComparison.OrdinalIgnoreCase)
                             || string.Equals(v, "#FFFFFFFF", System.StringComparison.OrdinalIgnoreCase) ? null : v;
            OnPropertyChanged();
        }
    }

    /// <summary>Times the game's drops a second.</summary>
    public float WetAmount
    {
        get => Model.Wet.Amount ?? 1f;
        set { if (!(value >= 0)) return; Model.Wet.Amount = value == 1f ? null : value; OnPropertyChanged(); }
    }

    /// <summary>Times the game's drop size.</summary>
    public float WetSize
    {
        get => Model.Wet.Size ?? 1f;
        set { if (!(value > 0)) return; Model.Wet.Size = value == 1f ? null : value; OnPropertyChanged(); }
    }

    /// <summary>Times the game's fall.</summary>
    public float WetFall
    {
        get => Model.Wet.Fall ?? 1f;
        set { if (!(value >= 0)) return; Model.Wet.Fall = value == 1f ? null : value; OnPropertyChanged(); }
    }

    public string Display => ListNames.Of(DisplayName, Key);

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
public sealed class NpcPlacementViewModel : ObservableObject, IDrifts
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

    // ── The wet effect (WetEffectDef) ────────────────────────────────────
    // What the placement sets over its NPC's look (after 1.10.0, the author: "NPC sets them, placements can change"):
    // empty, the NPC's (NpcWetDef) - and the game's where the NPC sets none. The box, the shower's top and floor are the
    // placement's alone. Nothing set at all leaves the placement's record as it was (no "wetEffect").

    /// <summary>
    /// An NPC's wet settings by its key: the open pack's (MainViewModel sets it), what a placement shows its kind
    /// by. The preview and the plugin read the NPC themselves (WetEffectDef.Merged); this is for the panel alone.
    /// </summary>
    internal static System.Func<string, NpcWetDef?>? NpcWetOf { get; set; }

    private NpcWetDef? NpcWet => string.IsNullOrEmpty(Model.Npc) ? null : NpcWetOf?.Invoke(Model.Npc);

    /// <summary>What this placed character's wet effect is: the NPC's look, the placement's over it.</summary>
    public WetEffectDef EffectiveWet(NpcWetDef? npc) => WetEffectDef.Merged(npc, Model.WetEffect);

    private WetEffectDef Effect => Model.WetEffect ??= new WetEffectDef();

    private void EffectChanged()
    {
        if (Model.WetEffect is { Empty: true }) Model.WetEffect = null;
        RaiseWet();
    }

    /// <summary>The NPC's own wet settings changed: what this shows from them, read again.</summary>
    internal void NpcWetChanged() => RaiseWet();

    private void RaiseWet()
    {
        foreach (var name in WetNames) OnPropertyChanged(name);
    }

    private static readonly string[] WetNames =
    {
        nameof(WetKindIndex), nameof(WetShower), nameof(WetAreaWidth), nameof(WetAreaHeight), nameof(WetFromTop),
        nameof(WetFloor), nameof(WetTint), nameof(WetAmount), nameof(WetSize), nameof(WetFall), nameof(WetCustomised),
    };

    /// <summary>The kind it has: the placement's, else its NPC's, else the wet.</summary>
    public string WetKind => Model.WetEffect?.Kind ?? NpcWet?.Kind ?? WetEffectDef.KindWet;

    /// <summary>0 the NPC's kind, 1 the wet, 2 the shower (the dropdown's place). A value it does not have is ignored: a
    /// page built ahead blanks its box while it has no options (warmed pages), which must not change the pack.</summary>
    public int WetKindIndex
    {
        get => Model.WetEffect?.Kind switch { null => 0, WetEffectDef.KindShower => 2, _ => 1 };
        set
        {
            if (value is not (0 or 1 or 2) || value == WetKindIndex) return;
            Effect.Kind = value switch { 1 => WetEffectDef.KindWet, 2 => WetEffectDef.KindShower, _ => null };
            EffectChanged();
        }
    }

    public bool WetShower => WetEffectDef.ShowerKind(WetKind);

    /// <summary>The box the Wet part's drops start in, its own units: the kind's own (3.21 x 8.45 the wet's, 2 x 1
    /// the shower's) until a side of it is dragged in the place preview or a number is typed.</summary>
    public float WetAreaWidth
    {
        get => Model.WetEffect?.Width ?? WetEffectDef.DefaultWidth(WetKind);
        set { if (!(value > 0)) return; Effect.Width = value; EffectChanged(); }
    }

    /// <summary>See <see cref="WetAreaWidth"/>.</summary>
    public float WetAreaHeight
    {
        get => Model.WetEffect?.Height ?? WetEffectDef.DefaultHeight(WetKind);
        set { if (!(value > 0)) return; Effect.Height = value; EffectChanged(); }
    }

    /// <summary>The box's size both at once (a side dragged: the other size kept as it reads).</summary>
    public void SetWetArea(float width, float height)
    {
        Effect.Width = width;
        Effect.Height = height;
        EffectChanged();
    }

    /// <summary>The shower's drops start along the box's top side only.</summary>
    public bool WetFromTop
    {
        get => Model.WetEffect?.FromTop == true;
        set { Effect.FromTop = value ? true : null; EffectChanged(); }
    }

    /// <summary>The shower's drops vanish at the box's floor.</summary>
    public bool WetFloor
    {
        get => Model.WetEffect?.Floor == true;
        set { Effect.Floor = value ? true : null; EffectChanged(); }
    }

    /// <summary>The colour the game's drop colour is multiplied by, here: "" for the NPC's.</summary>
    public string WetTint
    {
        get => Model.WetEffect?.Tint ?? "";
        set
        {
            string v = (value ?? "").Trim();
            Effect.Tint = v.Length == 0 ? null : v;
            EffectChanged();
        }
    }

    /// <summary>Times the game's drops a second, here: empty (null) for the NPC's.</summary>
    public float? WetAmount
    {
        get => Model.WetEffect?.Amount;
        set { if (value is < 0) return; Effect.Amount = value; EffectChanged(); }
    }

    /// <summary>Times the game's drop size, here: empty (null) for the NPC's.</summary>
    public float? WetSize
    {
        get => Model.WetEffect?.Size;
        set { if (value is <= 0) return; Effect.Size = value; EffectChanged(); }
    }

    /// <summary>Times the game's fall, here: empty (null) for the NPC's.</summary>
    public float? WetFall
    {
        get => Model.WetEffect?.Fall;
        set { if (value is < 0) return; Effect.Fall = value; EffectChanged(); }
    }

    /// <summary>Anything of the effect set here: what Reset takes back.</summary>
    public bool WetCustomised => Model.WetEffect != null;

    /// <summary>The placed character's wet effect back to its NPC's (and the game's box).</summary>
    public void ResetWetEffect()
    {
        Model.WetEffect = null;
        RaiseWet();
    }

    public RelayCommand ResetWetEffectCommand => _resetWet ??= new RelayCommand(ResetWetEffect);
    private RelayCommand? _resetWet;

    public NpcPlacementViewModel(NpcPlacementDef model, System.Action<NpcPlacementViewModel>? removeCallback = null,
                                 System.Action? changed = null)
    {
        Model = model;
        _removeCallback = removeCallback;
        // To the place's recycle bin (PlaceObjects.Delete), when the row is one of a place's.
        RemoveCommand = new RelayCommand(() => { if (Tree != null) Tree.Delete(this); else removeCallback?.Invoke(this); },
                                         () => removeCallback != null);
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
        Children = new System.Collections.ObjectModel.ObservableCollection<GameObjectViewModel>();
        foreach (var c in model.Children) Children.Add(new GameObjectViewModel(c, RemoveChild, true, Children, model.Children));
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

    public GameObjectViewModel AddChild() => AdoptAt(new GameObjectDef(), Children.Count);

    /// <summary>A child given to the character at <paramref name="index"/> among its children (the end when past it).</summary>
    internal GameObjectViewModel AdoptAt(GameObjectDef def, int index)
    {
        index = System.Math.Clamp(index, 0, Children.Count);
        int entry = index < Children.Count ? Model.Children.IndexOf(Children[index].Model) : Model.Children.Count;
        Model.Children.Insert(entry < 0 ? Model.Children.Count : entry, def);
        var vm = new GameObjectViewModel(def, RemoveChild, true, Children, Model.Children) { Tree = Tree };
        Children.Insert(index, vm);
        return vm;
    }

    private readonly System.Action<NpcPlacementViewModel>? _removeCallback;
    private PlaceObjects? _tree;

    /// <summary>The objects of the place this character stands in: what its Remove sends to the recycle bin
    /// through (see <see cref="GameObjectViewModel.Tree"/>).</summary>
    internal PlaceObjects? Tree
    {
        get => _tree;
        set
        {
            _tree = value;
            foreach (var child in Children) child.Tree = value;
        }
    }

    /// <summary>Out of the object it stands on, as its remove did before the recycle bin.</summary>
    internal void TakeOut() => _removeCallback?.Invoke(this);

    public void RemoveChild(GameObjectViewModel vm)
    {
        Model.Children.Remove(vm.Model);
        Children.Remove(vm);
    }

    /// <summary>
    /// The NPC it places. Nothing from the box is never a person's choice - a placement always names an NPC, and
    /// removing one is its own − button - but what its dropdown hands back when the NPC it shows leaves the list: an
    /// NPC removed or renamed while the place's page was built. That blanked the placement's NPC behind the author's
    /// back, and undo could not hold it (1.10.0: the page is now built ahead of time, so its dropdowns are always
    /// there - found by the walk over every control, UndoEveryControlTests, redoing an NPC's removal and rename).
    /// </summary>
    public string Npc
    {
        get => Model.Npc;
        set
        {
            if (value is null) return;
            Model.Npc = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display));
            // Its wet effect follows its NPC's where it sets nothing of its own.
            RaiseWet();
        }
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

    // ── How it drifts (after 1.10.0) ─────────────────────────────────────
    /// <summary>How the character drifts as the cursor moves, as "Moves with" offers it (<see cref="Drifting"/>).</summary>
    public string Drift => Drifting.Of(this);

    public void DriftWith(string kind, float backStrength = 0.75f) => Drifting.Apply(this, kind, backStrength);

    public bool ParallaxEnabled
    {
        get => !Model.ParallaxDisabled;
        set
        {
            if (value == ParallaxEnabled) return;
            Model.ParallaxDisabled = !value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Drift));
        }
    }

    public bool ParallaxInherits
    {
        get => Model.ParallaxStrength == null;
        set
        {
            if (value == ParallaxInherits) return;
            Model.ParallaxStrength = value ? null : 0.75f;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParallaxStrength));
            OnPropertyChanged(nameof(Drift));
        }
    }

    /// <summary>Its own strength; 0.75 shown while it drifts with the back picture. Setting it is a strength of its own.</summary>
    public float ParallaxStrength
    {
        get => Model.ParallaxStrength ?? 0.75f;
        set
        {
            if (Model.ParallaxStrength == value) return;
            Model.ParallaxStrength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParallaxInherits));
            OnPropertyChanged(nameof(Drift));
        }
    }

    /// <summary>Whether it drifts reversed, as it will in the game: its own setting, or, unset, the level's back
    /// picture's (<see cref="PlaceObjects.BackReversed"/>). Setting it is a reversal of its own, false too (see
    /// <see cref="GameObjectViewModel.ParallaxReversed"/>).</summary>
    public bool ParallaxReversed
    {
        get => Model.ParallaxReversed ?? Tree?.BackReversed ?? false;
        set
        {
            if (value == ParallaxReversed) return;
            Model.ParallaxReversed = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Its own reversal, null as the back picture's.</summary>
    public bool? ParallaxReversedOwn
    {
        get => Model.ParallaxReversed;
        set
        {
            if (value == Model.ParallaxReversed) return;
            Model.ParallaxReversed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParallaxReversed));
        }
    }

    /// <summary>The back picture's reversal changed: a character that takes it shows it.</summary>
    internal void ReversalMayHaveChanged()
    {
        if (Model.ParallaxReversed == null) OnPropertyChanged(nameof(ParallaxReversed));
    }
}
