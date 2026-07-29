using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps a <see cref="VanillaPlaceExtensionDef"/> for the Places tab.
/// Functionally the same shape as <see cref="PlaceViewModel"/> for buttons
/// — the only authored field on the parent is the vanilla source token —
/// so the navigator-buttons UI under each extension shares the same item
/// template the per-place section uses.
/// </summary>
public sealed class VanillaPlaceExtensionViewModel : ObservableObject
{
    public VanillaPlaceExtensionDef Model { get; }
    public ObservableCollection<NavigatorButtonViewModel> NavigatorButtons { get; }
    public ObservableCollection<GameObjectViewModel> GameObjects { get; }

    public VanillaPlaceExtensionViewModel(VanillaPlaceExtensionDef model)
    {
        Model = model;
        NavigatorButtons = new ObservableCollection<NavigatorButtonViewModel>(
            model.NavigatorButtons.Select(b =>
                new NavigatorButtonViewModel(b, removeCallback: RemoveNavigatorButton,
                                                moveCallback: MoveNavigatorButton)));
        GameObjects = new ObservableCollection<GameObjectViewModel>(
            model.GameObjects.Select(o => new GameObjectViewModel(o, RemoveGameObject)));
        SeedFromCatalogCommand = new RelayCommand(SeedFromCatalog, () => HasCatalogEntry);
        AddNpcCommand = new RelayCommand(() => NpcsNode().AddNpc());
        // Seed here rather than leaving it to whoever constructs us: an
        // extension loaded from disk already has its Source, so the property
        // setter below never fires for it, and the GameObjects list would come
        // up empty.
        EnsureSeeded();
    }

    public string Source
    {
        get => Model.Source;
        set
        {
            if (Model.Source == value) return;
            Model.Source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(PreviewBaseSprite));
            OnPropertyChanged(nameof(PreviewSecondarySprite));
            OnPropertyChanged(nameof(HasCatalogEntry));
            OnPropertyChanged(nameof(CatalogSummary));
            // Picking a source is what makes a hierarchy knowable, so fill it in
            // right then — the GameObjects list is the point of the tab.
            // Reset first so old catalog nodes don't leak into the preview.
            ResetAndSeed();
        }
    }

    // ── Preview + catalog ─────────────────────────────────────────────────

    private VanillaLevelCatalog.Level? CatalogLevel => VanillaLevelCatalog.FindLevelByToken(Model.Source);

    /// <summary>True once the extracted catalog knows this vanilla level — the
    /// preview art and the seeding command both depend on it.</summary>
    public bool HasCatalogEntry => CatalogLevel != null;

    /// <summary>Absolute path to the level's extracted base sprite (blank when
    /// the extraction hasn't been run). Absolute so the shared PlacePreview,
    /// which combines pack root + path, resolves it unchanged.</summary>
    public string PreviewBaseSprite => VanillaLevelCatalog.FindArt(CatalogLevel?.GoName, "Base.PNG");

    public string PreviewSecondarySprite => VanillaLevelCatalog.FindArt(CatalogLevel?.GoName, "Secondary.PNG");

    /// <summary>
    /// Sorting order of this level's own art, from the extraction.
    /// <para/>
    /// Every vanilla level picks its own — Downtown draws its base at -10 and
    /// its far layer at -15 — so the preview can't judge what sits in front of
    /// the backdrop from the single convention a PACK place is built to. Read
    /// off the level root's renderer, falling back to the pack default when the
    /// catalog has no entry.
    /// </summary>
    public int PreviewArtOrder => CatalogLevel?.Hierarchy?.SpriteRenderer?.SortingOrder ?? -4;

    /// <summary>The level root's own scale, which everything under it inherits.
    /// Almost every vanilla level ships at 0.79.</summary>
    public float PreviewLevelScale
    {
        get
        {
            var s = CatalogLevel?.Hierarchy?.ScaleY ?? 0f;
            return s > 0f ? s : 0.79f;
        }
    }

    /// <summary>This level's art import ppu, which decides how large the
    /// backdrop is in world units. Per level — they are not all the same.</summary>
    public float PreviewArtPpu
    {
        get
        {
            float ppu = CatalogLevel?.Hierarchy?.SpriteRenderer?.PixelsPerUnit ?? 0f;
            return ppu > 0f ? ppu : 70.32f;
        }
    }

    /// <summary>The lowest order any of the level's own art draws at — the far
    /// layer, when it has one.</summary>
    public int PreviewArtSecondaryOrder
    {
        get
        {
            var lv = CatalogLevel;
            if (lv?.Hierarchy == null) return -5;
            int lowest = PreviewArtOrder;
            foreach (var c in lv.Hierarchy.Children)
            {
                var sr = c.SpriteRenderer;
                if (sr != null && sr.SortingOrder < lowest) lowest = sr.SortingOrder;
            }
            return lowest;
        }
    }

    /// <summary>One-line status under the preview: what the catalog knows, or
    /// why there's nothing to show.</summary>
    public string CatalogSummary
    {
        get
        {
            if (!VanillaLevelCatalog.IsAvailable)
                return "No vanilla level catalog found. Run Tools › SMSModForge › Extract Vanilla Levels in the game's Unity project, drop the output in Resources/VanillaLevelArt, and rebuild.";
            var lv = CatalogLevel;
            if (lv == null)
                return string.IsNullOrEmpty(Model.Source)
                    ? "Pick a source place to preview it."
                    : $"'{Model.Source}' isn't in the extracted catalog.";
            int nodes = CountNodes(lv.Hierarchy);
            return $"{nodes} GameObject(s) in the vanilla hierarchy."
                 + (string.IsNullOrEmpty(PreviewBaseSprite) ? "  (no extracted art for this level)" : "");
        }
    }

    private static int CountNodes(VanillaLevelCatalog.Node? n)
        => n == null ? 0 : 1 + n.Children.Sum(CountNodes);

    /// <summary>
    /// Populate the tree with BOUND nodes mirroring the vanilla hierarchy, each
    /// seeded with the object's real transform and active state — so it reads
    /// like the level you're editing, and the save-time delta sees "unchanged"
    /// until you actually change something.
    /// <para/>
    /// Additive and idempotent: existing nodes are matched by name and left
    /// alone (their children are still filled in), so re-running after a
    /// re-extraction tops up new objects without disturbing authored work.
    /// </summary>
    public RelayCommand SeedFromCatalogCommand { get; private set; } = null!;

    public void SeedFromCatalog()
    {
        var lv = CatalogLevel;
        if (lv?.Hierarchy == null) return;
        SeedInto(Model.GameObjects, GameObjects, lv, lv.Hierarchy.Children);
    }

    private void SeedInto(System.Collections.Generic.List<GameObjectDef> defs,
                          ObservableCollection<GameObjectViewModel> vms,
                          VanillaLevelCatalog.Level level,
                          System.Collections.Generic.List<VanillaLevelCatalog.Node> baseline)
    {
        foreach (var b in baseline)
        {
            if (string.IsNullOrWhiteSpace(b.Name)) continue;
            var existingVm = vms.FirstOrDefault(
                v => string.Equals(v.Name, b.Name, System.StringComparison.OrdinalIgnoreCase));
            if (existingVm == null)
            {
                // Created bare, then anchored — same call the reloaded path uses,
                // so a freshly seeded node and one read back off disk can't drift.
                var def = new GameObjectDef { Name = b.Name, Bind = true };
                VanillaDelta.Rebase(def, b, level);
                defs.Add(def);
                existingVm = new GameObjectViewModel(def, RemoveGameObject);
                vms.Add(existingVm);
            }
            else if (existingVm.Model.Bind)
            {
                // A node loaded from the manifest arrives stripped of everything
                // the save path drops — transform, active flag, sorting, art. Put
                // it back, or it reads as "moved to the origin" against vanilla.
                VanillaDelta.Rebase(existingVm.Model, b, level);
                existingVm.RefreshFromModel();
            }
            SeedInto(existingVm.Model.Children, existingVm.Children, level, b.Children);
        }
    }

    /// <summary>
    /// Fill the tree from the catalog if it hasn't been already. Called when a
    /// pack loads and whenever the source changes, so opening an extension
    /// shows the level's real hierarchy ready to edit rather than an empty box
    /// with a button to press.
    /// <para/>
    /// Safe to call repeatedly: seeding matches existing nodes by name, and
    /// every untouched bound node is dropped again at save, so this never adds
    /// anything to the manifest.
    /// </summary>
    public void EnsureSeeded()
    {
        if (CatalogLevel?.Hierarchy == null) return;
        SeedFromCatalog();
    }

    /// <summary>
    /// Replace the entire tree with the catalog's hierarchy. Called when the
    /// source changes so old catalog nodes don't leak into the preview.
    /// </summary>
    private void ResetAndSeed()
    {
        if (CatalogLevel?.Hierarchy == null) return;
        Model.GameObjects.Clear();
        GameObjects.Clear();
        SeedFromCatalog();
    }

    public string Display
    {
        get
        {
            if (!PlaceTargetRef.TryParse(Source, out var r) || r.Kind != PlaceTargetKind.Vanilla)
                return string.IsNullOrEmpty(Source) ? "(unset)" : Source;
            var vp = VanillaPlaces.FindByGoName(r.Key);
            return vp == null ? r.Key : $"{vp.DisplayName} ({vp.GoName})";
        }
    }

    /// <summary>
    /// True when this extension actually changes the vanilla level — a modified
    /// bound object, or a GameObject of its own. False while it only mirrors the
    /// hierarchy the editor filled in, which saves as nothing. Recomputed on
    /// demand rather than cached, since any edit anywhere in the tree can flip
    /// it and the list is short.
    /// </summary>
    public bool HasVanillaChanges => VanillaDelta.HasChanges(Model);

    /// <summary>How many GameObjects this extension would write — the number on
    /// the sidebar marker.</summary>
    public int ChangedCount => VanillaDelta.ChangedCount(Model);

    private VanillaDelta.Tally Tally => VanillaDelta.Analyze(Model);

    /// <summary>GameObjects this extension ADDS to the vanilla level. The level's
    /// own objects are untouched by these.</summary>
    public int AddedCount => Tally.Added;
    public bool HasAdded => Tally.Added > 0;

    /// <summary>Objects OF THE GAME'S OWN that this extension reaches into —
    /// moved, switched, gated, or given components. The stricter reading of
    /// "changed a vanilla GameObject".</summary>
    public int ModifiedCount => Tally.Modified;
    public bool HasModified => Tally.Modified > 0;

    /// <summary>Re-read the change markers. Called when the pack is saved or the
    /// selection moves, which is when the sidebar is worth refreshing.</summary>
    public void RefreshChangeIndicator()
    {
        OnPropertyChanged(nameof(HasVanillaChanges));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(AddedCount));
        OnPropertyChanged(nameof(HasAdded));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(HasModified));
    }

    public NavigatorButtonViewModel AddNavigatorButton()
    {
        var def = new NavigatorButtonDef();
        Model.NavigatorButtons.Add(def);
        var vm = new NavigatorButtonViewModel(def, removeCallback: RemoveNavigatorButton,
                                                   moveCallback: MoveNavigatorButton);
        NavigatorButtons.Add(vm);
        return vm;
    }

    public void RemoveNavigatorButton(NavigatorButtonViewModel button)
    {
        Model.NavigatorButtons.Remove(button.Model);
        NavigatorButtons.Remove(button);
    }

    public GameObjectViewModel AddGameObject()
    {
        var def = new GameObjectDef();
        Model.GameObjects.Add(def);
        var vm = new GameObjectViewModel(def, RemoveGameObject);
        GameObjects.Add(vm);
        return vm;
    }

    // ── NPC placements ────────────────────────────────────────────────────

    /// <summary>Place an NPC on this vanilla level, under the level's own NPCs
    /// object — the same gesture as on a pack place.</summary>
    public RelayCommand AddNpcCommand { get; private set; } = null!;

    /// <summary>
    /// The node standing for the level's NPCs object, created on demand.
    /// <para/>
    /// Resolved lazily rather than in the constructor because seeding usually
    /// supplies it: every vanilla level HAS an NPCs object, so the catalog pass
    /// brings one in as a bound node. Only a level with no catalog entry needs
    /// one made here, and it's bound rather than created for the same reason —
    /// the object exists, so claiming to build it would be a lie the runtime
    /// would have to reconcile.
    /// <para/>
    /// Placements are "own additions" to the delta pass, so hanging one on an
    /// otherwise untouched bound node is what keeps that node in the manifest.
    /// </summary>
    private GameObjectViewModel NpcsNode()
    {
        var existing = GameObjects.FirstOrDefault(
            g => g.IsNpcRoot || string.Equals(g.Name, "NPCs", System.StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var def = new GameObjectDef { Name = "NPCs", Bind = true };
        Model.GameObjects.Add(def);
        var vm = new GameObjectViewModel(def, RemoveGameObject);
        GameObjects.Add(vm);
        return vm;
    }

    public void RemoveGameObject(GameObjectViewModel go)
    {
        Model.GameObjects.Remove(go.Model);
        GameObjects.Remove(go);
    }

    /// <summary>Reorder by -1/+1 in both the VM + model lists. See
    /// <see cref="PlaceViewModel"/> for the rationale (manifest order =
    /// runtime instantiation order).</summary>
    private void MoveNavigatorButton(NavigatorButtonViewModel button, int delta)
    {
        int i = NavigatorButtons.IndexOf(button);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= NavigatorButtons.Count) return;
        NavigatorButtons.Move(i, j);
        Model.NavigatorButtons.RemoveAt(i);
        Model.NavigatorButtons.Insert(j, button.Model);
    }
}
