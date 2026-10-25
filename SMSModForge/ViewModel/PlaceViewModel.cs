using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps a <see cref="PlaceDef"/> with INPC for the Places editor view. Owns
/// the observable list of navigator buttons authored on this place. Adding
/// or removing buttons mutates both the VM list and the underlying model
/// list so the manifest serialises correctly.
/// </summary>
public sealed class PlaceViewModel : ObservableObject, IDerivesKey
{
    /// <summary>
    /// Hard cap on navigator buttons per place. Matches the runtime's
    /// 12-button navigator grid (two rows of six once the strip extends).
    /// Surfaced to the UI so the author sees the limit before hitting it.
    /// </summary>
    public const int MaxNavigatorButtons = 12;

    public PlaceDef Model { get; }
    public ObservableCollection<NavigatorButtonViewModel> NavigatorButtons { get; }

    /// <summary>The place's whole GameObject tree — layered sprite objects,
    /// sprite-less containers, and the forced NPCs-root node, with NPC placements
    /// nested under them. One recursive editor renders the lot.</summary>
    public ObservableCollection<GameObjectViewModel> GameObjects { get; }

    /// <summary>Conditions-gated action groups run once on level activation / deactivation.</summary>
    public ObservableCollection<LevelHookViewModel> OnEnterHooks { get; }
    public ObservableCollection<LevelHookViewModel> OnExitHooks { get; }

    /// <summary>The forced NPCs-container node (always present). "Add NPC" hangs
    /// new placements under it by default.</summary>
    public GameObjectViewModel NpcsNode { get; }

    public RelayCommand AddEnterHookCommand { get; }
    public RelayCommand AddExitHookCommand { get; }
    public RelayCommand AddGameObjectCommand { get; }
    public RelayCommand AddNpcCommand { get; }

    public PlaceViewModel(PlaceDef model)
    {
        Model = model;
        NavigatorButtons = new ObservableCollection<NavigatorButtonViewModel>(
            model.NavigatorButtons.Select(b =>
                new NavigatorButtonViewModel(b, removeCallback: RemoveNavigatorButton,
                                                moveCallback: MoveNavigatorButton)));

        // Every place has exactly one forced NPCs-root node. Migrated/loaded
        // packs already carry it; a freshly created place gets one here so the
        // NPCs hierarchy always has a home.
        var npcsDef = model.GameObjects.FirstOrDefault(g => g.IsNpcRoot);
        if (npcsDef == null)
        {
            npcsDef = new GameObjectDef { Name = "NPCs", Role = GameObjectDef.RoleNpcRoot };
            model.GameObjects.Add(npcsDef);
        }
        GameObjects = new ObservableCollection<GameObjectViewModel>(
            model.GameObjects.Select(g => new GameObjectViewModel(g, RemoveGameObject)));
        NpcsNode = GameObjects.First(g => g.IsNpcRoot);

        OnEnterHooks = new ObservableCollection<LevelHookViewModel>(
            model.OnEnter.Select(h => new LevelHookViewModel(h, RemoveEnterHook)));
        OnExitHooks = new ObservableCollection<LevelHookViewModel>(
            model.OnExit.Select(h => new LevelHookViewModel(h, RemoveExitHook)));
        AddEnterHookCommand = new RelayCommand(() =>
        {
            var def = new LevelHookDef();
            Model.OnEnter.Add(def);
            OnEnterHooks.Add(new LevelHookViewModel(def, RemoveEnterHook));
        });
        AddExitHookCommand = new RelayCommand(() =>
        {
            var def = new LevelHookDef();
            Model.OnExit.Add(def);
            OnExitHooks.Add(new LevelHookViewModel(def, RemoveExitHook));
        });
        AddGameObjectCommand = new RelayCommand(() => AddGameObject());
        // New NPCs hang under the forced NPCs-root node (the runtime home for
        // the NPC hierarchy), next to the "Add GameObject" button.
        AddNpcCommand = new RelayCommand(() => NpcsNode.AddNpc());
    }

    /// <summary>Adds a top-level GameObject (a sprite object or a container),
    /// inserted before the forced NPCs node so it stays last.</summary>
    public GameObjectViewModel AddGameObject()
    {
        var def = new GameObjectDef();
        int idx = Model.GameObjects.FindIndex(g => g.IsNpcRoot);
        if (idx < 0) idx = Model.GameObjects.Count;
        Model.GameObjects.Insert(idx, def);
        var vm = new GameObjectViewModel(def, RemoveGameObject);
        GameObjects.Insert(idx, vm);
        return vm;
    }

    public void RemoveGameObject(GameObjectViewModel g)
    {
        if (g.IsNpcRoot) return;   // the NPCs node is forced
        Model.GameObjects.Remove(g.Model);
        GameObjects.Remove(g);
    }

    private void RemoveEnterHook(LevelHookViewModel vm)
    {
        Model.OnEnter.Remove(vm.Model);
        OnEnterHooks.Remove(vm);
    }

    private void RemoveExitHook(LevelHookViewModel vm)
    {
        Model.OnExit.Remove(vm.Model);
        OnExitHooks.Remove(vm);
    }

    /// <summary>True while there's room for another navigator button.</summary>
    public bool CanAddNavigatorButton => NavigatorButtons.Count < MaxNavigatorButtons;

    /// <summary>"N / 12" progress label shown next to the Add button.</summary>
    public string NavigatorButtonCountLabel => $"{NavigatorButtons.Count} / {MaxNavigatorButtons}";

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW place takes its key from
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
            SetKey(value);
            // Typing a key is a decision, and it sticks.
            _derivedKey.Stop();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>
    /// The key, and the name of the place's GameObject with it while the two
    /// are one (the author, 1.8.0: "unify the Internal GO Name field into just
    /// the runtime name one"). A place whose GameObject already has a name of
    /// its own - from an older pack - keeps it: another mod may find the level
    /// by that name, so it changes only when the author says so
    /// (<see cref="UseRuntimeNameForObject"/>).
    /// </summary>
    private void SetKey(string value)
    {
        bool follows = !InternalNameIsOwn;
        Model.Key = value;
        if (follows) Model.InternalName = value;
        OnPropertyChanged(nameof(InternalName));
        OnPropertyChanged(nameof(InternalNameIsOwn));
    }

    public string InternalName
    {
        get => Model.InternalName;
        set { Model.InternalName = value; OnPropertyChanged(); OnPropertyChanged(nameof(InternalNameIsOwn)); }
    }

    /// <summary>The place's GameObject is named otherwise than its runtime
    /// name: the only time its name is shown on its own.</summary>
    public bool InternalNameIsOwn
        => !string.IsNullOrEmpty(Model.InternalName) && !string.Equals(Model.InternalName, Model.Key, System.StringComparison.Ordinal);

    /// <summary>Name the GameObject by the runtime name, as every place made now is.</summary>
    public void UseRuntimeNameForObject() => InternalName = Model.Key;

    private RelayCommand? _useRuntimeName;
    public RelayCommand UseRuntimeNameCommand => _useRuntimeName ??= new RelayCommand(UseRuntimeNameForObject);

    /// <summary>The key put back by the rename tracker, without it counting
    /// as one the author typed.</summary>
    internal void RestateKey(string key) => SetKey(key);

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            Model.DisplayName = value;
            if (_derivedKey.Next(value, Model.Key) is { } derived && derived != Model.Key)
            {
                SetKey(derived);
                OnPropertyChanged(nameof(Key));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string BaseSprite
    {
        get => Model.BaseSprite;
        set { Model.BaseSprite = value; OnPropertyChanged(); }
    }

    public string SecondarySprite
    {
        get => Model.SecondarySprite;
        set { Model.SecondarySprite = value; OnPropertyChanged(); }
    }

    public string MaskSprite
    {
        get => Model.MaskSprite;
        set { Model.MaskSprite = value; OnPropertyChanged(); }
    }

    public string SecondaryMaskSprite
    {
        get => Model.SecondaryMaskSprite;
        set
        {
            Model.SecondaryMaskSprite = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSecondaryMask));
        }
    }

    /// <summary>Whether the SECONDARY sprite has been given a jiggle of its own.</summary>
    public bool HasSecondaryMask => !string.IsNullOrWhiteSpace(Model.SecondaryMaskSprite);

    /// <summary>Mask-editor host for the BASE sprite's mask.</summary>
    public IMaskEditorHost BaseMaskHost => new PlaceMaskHost(this, secondary: false);

    /// <summary>Mask-editor host for the SECONDARY sprite's mask.</summary>
    public IMaskEditorHost SecondaryMaskHost => new PlaceMaskHost(this, secondary: true);

    private byte[]? _liveBaseMask, _liveSecondaryMask;

    /// <summary>
    /// The base mask as an open editor is painting it, or null.
    /// <para/>
    /// The array itself is the editor's and is mutated in place as strokes
    /// land, so the preview holds the reference and simply keeps drawing. Only
    /// gaining or losing the buffer is a change worth announcing.
    /// </summary>
    public byte[]? LiveBaseMask
    {
        get => _liveBaseMask;
        set { _liveBaseMask = value; OnPropertyChanged(); }
    }

    /// <summary>Same, for the secondary sprite's mask.</summary>
    public byte[]? LiveSecondaryMask
    {
        get => _liveSecondaryMask;
        set { _liveSecondaryMask = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Adapts one of a place's two masks to the editor.
    /// <para/>
    /// The editor only ever needed a name, the art to paint over, and somewhere
    /// to put the path — so a place plugs into it the same way an NPC does, with
    /// the sprite behind the brush being whichever of the two this mask belongs
    /// to. The kind is what makes it author alpha rather than R/G/B.
    /// </summary>
    private sealed class PlaceMaskHost : IMaskEditorHost
    {
        private readonly PlaceViewModel _place;
        private readonly bool _secondary;

        public PlaceMaskHost(PlaceViewModel place, bool secondary)
        { _place = place; _secondary = secondary; }

        // Seeds the mask editor's Save-As filename ("<Key>_mask.PNG"), so it says
        // which layer without repeating the word: GiftShop_secondary_mask.PNG.
        public string Key => _place.Key + (_secondary ? "_secondary" : "_base");
        public string PoseSpritePath => _secondary ? _place.SecondarySprite : _place.BaseSprite;

        public string MaskPath
        {
            get => _secondary ? _place.SecondaryMaskSprite : _place.MaskSprite;
            set { if (_secondary) _place.SecondaryMaskSprite = value; else _place.MaskSprite = value; }
        }

        /// <summary>Forwarded to the place so the preview can draw the mask
        /// while it is still being painted.</summary>
        public byte[]? LiveMaskBgra
        {
            get => _secondary ? _place.LiveSecondaryMask : _place.LiveBaseMask;
            set { if (_secondary) _place.LiveSecondaryMask = value; else _place.LiveBaseMask = value; }
        }

        /// <summary>Bumped per stroke. Nothing needs to watch it here: the
        /// buffer above is mutated in place and the preview's shader loop is
        /// already redrawing, so each stroke appears on the next tick.</summary>
        public int LiveMaskRevision { get; set; }

        public Rendering.MaskKind MaskKind => Rendering.MaskKind.LevelAlpha;
    }

    /// <summary>The Beach prototype's own sorting orders, which a place keeps
    /// until it says otherwise. Also what the preview draws the art at.</summary>
    public const int DefaultBaseSortingOrder = -10;
    public const int DefaultSecondarySortingOrder = -12;

    public int BaseSortingOrder
    {
        get => Model.BaseSortingOrder ?? DefaultBaseSortingOrder;
        set
        {
            Model.BaseSortingOrder = value == DefaultBaseSortingOrder ? null : value;
            OnPropertyChanged();
        }
    }

    public int SecondarySortingOrder
    {
        get => Model.SecondarySortingOrder ?? DefaultSecondarySortingOrder;
        set
        {
            Model.SecondarySortingOrder = value == DefaultSecondarySortingOrder ? null : value;
            OnPropertyChanged();
        }
    }

    public float ParallaxStrength
    {
        get => Model.ParallaxStrength;
        set
        {
            Model.ParallaxStrength = value;
            OnPropertyChanged();
            // A linked backdrop follows it.
            OnPropertyChanged(nameof(ParallaxSecondaryStrength));
        }
    }

    /// <summary>Whether the backdrop just copies the main sprite (the pre-split
    /// behaviour). Unticking seeds the slider from the main value, so the
    /// author starts at what they already had and moves away from it.</summary>
    public bool ParallaxSecondaryLinked
    {
        get => Model.ParallaxSecondaryStrength == null;
        set
        {
            Model.ParallaxSecondaryStrength = value ? null : Model.ParallaxStrength;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParallaxSecondaryStrength));
        }
    }

    /// <summary>The backdrop's effective strength — the main sprite's while
    /// linked. Setting it always means "I want my own value".</summary>
    public float ParallaxSecondaryStrength
    {
        get => Model.ParallaxSecondaryStrength ?? Model.ParallaxStrength;
        set
        {
            Model.ParallaxSecondaryStrength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParallaxSecondaryLinked));
        }
    }

    public bool ParallaxReversed
    {
        get => Model.ParallaxReversed;
        set { Model.ParallaxReversed = value; OnPropertyChanged(); }
    }

    public bool ParallaxSecondaryReversed
    {
        get => Model.ParallaxSecondaryReversed;
        set { Model.ParallaxSecondaryReversed = value; OnPropertyChanged(); }
    }

    public bool KeepAudio
    {
        get => Model.KeepAudio;
        set { Model.KeepAudio = value; OnPropertyChanged(); }
    }

    public bool KeepSeagulls
    {
        get => Model.KeepSeagulls;
        set { Model.KeepSeagulls = value; OnPropertyChanged(); }
    }

    public WeatherType WeatherType
    {
        get => Model.WeatherType;
        set { Model.WeatherType = value; OnPropertyChanged(); }
    }

    /// <summary>The list shows a place by its runtime name, spelled out as words
    /// (the author, 1.8.0: a display name like "Bedroom" says too little in a
    /// list of places) - see ListNames.Spaced.</summary>
    public string Display => ListNames.Spaced(Key);

    public NavigatorButtonViewModel? AddNavigatorButton()
    {
        if (!CanAddNavigatorButton) return null;   // 12-button cap
        // Music starts EMPTY, which tells the runtime not to touch what is
        // playing. A navigator button is a door within one area - room to room,
        // or back the way you came - and the area’s track should carry across
        // it. Defaulting these to a track would make every internal door cut the
        // music. The map buttons, which cross into somewhere new, default the
        // other way; see MainViewModel.AddMapButton.
        var def = new NavigatorButtonDef();
        Model.NavigatorButtons.Add(def);
        var vm = new NavigatorButtonViewModel(def, removeCallback: RemoveNavigatorButton,
                                                   moveCallback: MoveNavigatorButton);
        NavigatorButtons.Add(vm);
        RaiseCountChanged();
        return vm;
    }

    public void RemoveNavigatorButton(NavigatorButtonViewModel button)
    {
        Model.NavigatorButtons.Remove(button.Model);
        NavigatorButtons.Remove(button);
        RaiseCountChanged();
    }

    /// <summary>
    /// Reorder a navigator button by <paramref name="delta"/> (-1 earlier /
    /// +1 later). Swaps the entry in both the VM collection and the model
    /// list so the manifest order — the runtime instantiation / left→right
    /// order — stays in sync. No-op at the ends.
    /// </summary>
    private void MoveNavigatorButton(NavigatorButtonViewModel button, int delta)
    {
        int i = NavigatorButtons.IndexOf(button);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= NavigatorButtons.Count) return;
        NavigatorButtons.Move(i, j);
        Model.NavigatorButtons.RemoveAt(i);
        Model.NavigatorButtons.Insert(j, button.Model);
    }

    private void RaiseCountChanged()
    {
        OnPropertyChanged(nameof(CanAddNavigatorButton));
        OnPropertyChanged(nameof(NavigatorButtonCountLabel));
    }
}
