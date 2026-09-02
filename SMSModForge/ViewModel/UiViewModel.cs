using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;

namespace SMSModForge.ViewModel;

/// <summary>
/// One of the pack's UIs, for the UI tab — a screen of its own, or a change to
/// one the game already has. Both, because they are the same thing to author.
/// <para/>
/// Choosing a source seeds the whole screen from the game so the author can
/// see and edit what is there. That tree can be thousands of objects; what the
/// pack stores is whatever they actually changed, worked out at save time by
/// <see cref="VanillaUiDelta"/>. So this view model is deliberately generous
/// with the working copy and says nothing about the manifest — the two are
/// different sizes on purpose.
/// <para/>
/// The same shape as <see cref="VanillaPlaceExtensionViewModel"/>, which does
/// this for levels.
/// </summary>
public sealed class UiViewModel : ObservableObject
{
    public UiDef Model { get; }
    public ObservableCollection<UiNodeViewModel> Nodes { get; }

    /// <summary>Raised when anything in the tree changes, so the preview can
    /// redraw. One event for the whole extension rather than a subscription per
    /// row, since the preview redraws the whole screen anyway.</summary>
    public event Action? Changed;

    public UiViewModel(UiDef model)
    {
        Model = model;
        Nodes = new ObservableCollection<UiNodeViewModel>(
            model.Nodes.Select(n => new UiNodeViewModel(n, null, HasChanges)));
        foreach (var node in Nodes) node.Changed += Bubble;

        SeedCommand = new RelayCommand(Seed, () => CanSeed);

        // An extension read back off disk holds only its delta, which is a few
        // nodes and not a screen. Seeding here rebuilds the working copy so it
        // opens looking like the game rather than like the diff.
        EnsureSeeded();
    }

    // ── Which vanilla UI ─────────────────────────────────────────────

    public string Source
    {
        get => Model.Source;
        set
        {
            if (Model.Source == value) return;
            Model.Source = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CanSeed));
            OnPropertyChanged(nameof(IsKnown));
            OnPropertyChanged(nameof(IsVanillaBased));
            OnPropertyChanged(nameof(ShowsScreenPicker));
            OnPropertyChanged(nameof(ShowsOwnSettings));
            SeedCommand.Raise();

            // A different screen entirely, so the old tree is not a delta
            // against it — keeping it would rebase every edit onto objects that
            // have nothing to do with them.
            ResetAndSeed();
            Bubble();
        }
    }

    public VanillaUiCatalog.Base? Catalog => VanillaUiCatalog.Find(Model.Source);

    public bool IsKnown => Catalog != null;

    public bool CanSeed => Catalog != null && VanillaUiLibrary.Node(Catalog) != null;

    public RelayCommand SeedCommand { get; }

    public string Display
    {
        get
        {
            var entry = Catalog;
            if (entry != null) return entry.Name + "  —  " + entry.Surface.Path;
            if (Model.IsVanillaBased) return Model.Source;        // named, but unknown here
            if (WantsVanilla) return "(no screen chosen)";
            return string.IsNullOrWhiteSpace(Model.Name) ? "(new UI)" : Model.Name;
        }
    }

    /// <summary>What the author calls it. Only meaningful for a UI of the
    /// pack's own — one built on a vanilla screen is named by that screen.</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool IsVanillaBased => Model.IsVanillaBased;

    /// <summary>The tree's root, for the preview to draw. Null for a UI with
    /// nothing in it yet.</summary>
    public UiNodeDef? RootNode => Model.Nodes.Count > 0 ? Model.Nodes[0] : null;

    /// <summary>The object whose properties are being edited, and which the
    /// preview outlines.</summary>
    public UiNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedModel));
            Changed?.Invoke();
        }
    }

    private UiNodeViewModel? _selectedNode;

    public bool HasSelection => _selectedNode != null;
    public UiNodeDef? SelectedModel => _selectedNode?.Model;

    /// <summary>
    /// The canvas this UI is drawn on, in the coordinates its anchors are
    /// fractions of.
    /// <para/>
    /// Taken from the vanilla screen when there is one, because an object
    /// anchored to the top-right of a 2500-wide canvas is somewhere else on a
    /// 1920-wide one. A UI of the pack's own gets the size almost every canvas
    /// in the game uses.
    /// </summary>
    public double CanvasWidth => Surface?.Width > 0 ? Surface.Width : 1920;
    public double CanvasHeight => Surface?.Height > 0 ? Surface.Height : 1080;

    private VanillaUiSurface? Surface => VanillaUiLibrary.SurfaceFor(Catalog);

    /// <summary>Add an object inside whatever is selected, or at the top when
    /// nothing is.</summary>
    public RelayCommand AddChildCommand => _addChild ??= new RelayCommand(() =>
    {
        var parent = SelectedNode ?? Nodes.FirstOrDefault();
        if (parent == null) return;
        SelectedNode = parent.AddChild();
    }, () => Nodes.Count > 0);

    private RelayCommand? _addChild;

    /// <summary>Remove the selected object, when it is the pack's to remove.</summary>
    public RelayCommand RemoveNodeCommand => _removeNode ??= new RelayCommand(() =>
    {
        var chosen = SelectedNode;
        if (chosen == null || !chosen.IsMine) return;
        chosen.RemoveCommand.Execute(null);
        SelectedNode = null;
    }, () => SelectedNode?.IsMine == true);

    private RelayCommand? _removeNode;

    /// <summary>
    /// Set when this row was started from "+ Vanilla" but no screen has been
    /// chosen yet.
    /// <para/>
    /// Deliberately NOT on the model. An entry with no source IS a UI of the
    /// pack's own as far as the manifest is concerned, and if the author never
    /// picks a screen that is exactly what they get. This only decides which
    /// half of the header to show while they are deciding, and being wrong
    /// about it costs a wrong label rather than a wrong pack.
    /// </summary>
    public bool WantsVanilla
    {
        get => _wantsVanilla;
        set
        {
            if (_wantsVanilla == value) return;
            _wantsVanilla = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsScreenPicker));
            OnPropertyChanged(nameof(ShowsOwnSettings));
            OnPropertyChanged(nameof(Display));
        }
    }

    private bool _wantsVanilla;

    /// <summary>Whether the header offers a vanilla screen to build on.</summary>
    public bool ShowsScreenPicker => IsVanillaBased || WantsVanilla;

    /// <summary>Whether the header offers a name and a parenting choice, which
    /// belong to a UI of the pack's own.</summary>
    public bool ShowsOwnSettings => !ShowsScreenPicker;

    /// <summary>Whether this UI hides when the game hides the rest of the
    /// interface. Only its own to decide when it is a screen of the pack's own:
    /// a change to a vanilla screen inherits whatever that screen already
    /// does.</summary>
    public bool HidesWithGameplayUi
    {
        get => Model.IsVanillaBased
            ? VanillaUiCatalog.DimsWithGameplayUi(Model.Source)
            : Model.HidesWithGameplayUi;
        set
        {
            if (Model.IsVanillaBased || Model.HidesWithGameplayUi == value) return;
            Model.HidesWithGameplayUi = value;
            OnPropertyChanged();
        }
    }

    public bool CanChooseHiding => !Model.IsVanillaBased;

    /// <summary>Whether a node asserts anything against the game. Handed to
    /// every row so a tree can mark what has actually been touched — an author
    /// looking at 1434 objects needs to see the three they changed.</summary>
    public bool HasChanges(UiNodeDef node)
    {
        if (node == null) return false;
        if (!node.IsBound) return true;                     // the pack made it
        if (!Model.IsVanillaBased) return true;             // nothing to compare to
        var vanilla = VanillaUiLibrary.Node(Catalog);
        var against = vanilla == null ? null : VanillaUiDelta.NodeAt(vanilla, node.Bind);
        return against != null && VanillaUiDelta.Asserts(node, against);
    }

    /// <summary>What this extension is doing, in a line: how much of the screen
    /// it touches against how much it merely holds. An author who has moved one
    /// label should be able to see that they have moved one label.</summary>
    public string Summary
    {
        get
        {
            if (!Model.IsVanillaBased)
            {
                int mine = Model.Nodes.Sum(CountNodes);
                return mine == 0 ? "empty" : $"{mine} object(s)";
            }
            if (Catalog == null) return "";
            var (before, after) = VanillaUiDelta.Measure(Model);
            return after == 0
                ? $"{before} objects, nothing changed yet"
                : $"{before} objects, {after} stored";
        }
    }

    // ── Seeding ──────────────────────────────────────────────────────

    /// <summary>
    /// Rebuild the working copy: the whole screen from the game, with whatever
    /// this extension already holds laid back over it.
    /// <para/>
    /// A merge rather than a fresh seed, and the difference is not academic. A
    /// pack on disk holds only its delta, so seeding alone would show an author
    /// their edit floating in an empty screen; a seed that replaced the tree
    /// would throw the edit away entirely. Both were tried, and the second is
    /// what this used to do.
    /// </summary>
    public void Seed()
    {
        var vanilla = VanillaUiLibrary.Node(Catalog);
        if (vanilla == null) return;

        var merged = VanillaUiSeed.Merge(vanilla, Model.Nodes, out int stranded,
                                         VanillaUiLibrary.Assets.NameForKey);
        Stranded = stranded;

        Model.Nodes.Clear();
        foreach (var node in Nodes) node.Changed -= Bubble;
        Nodes.Clear();

        Model.Nodes.Add(merged);
        var vm = new UiNodeViewModel(merged, null, HasChanges);
        vm.Changed += Bubble;
        Nodes.Add(vm);

        _selectedNode = null;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Stranded));
        OnPropertyChanged(nameof(Warning));
        OnPropertyChanged(nameof(RootNode));
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedModel));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        Bubble();
    }

    /// <summary>How many stored objects no longer match anything in the game —
    /// the screen has moved on since the pack was written. They are kept and
    /// still saved rather than dropped, because losing an author's work is a
    /// worse failure than showing it somewhere odd.</summary>
    public int Stranded { get; private set; }

    public string Warning
        => Stranded == 0 ? ""
           : $"{Stranded} change(s) refer to objects this version of the game " +
             "no longer has. They have been kept, at the top of the tree.";

    private void EnsureSeeded()
    {
        if (CanSeed) Seed();
    }

    private void ResetAndSeed()
    {
        // A different screen, so the stored delta is not a delta against it —
        // merging would rebase every edit onto objects that merely share a
        // path. The tree starts again from the new screen.
        Model.Nodes.Clear();
        foreach (var node in Nodes) node.Changed -= Bubble;
        Nodes.Clear();
        Stranded = 0;
        if (CanSeed) Seed();
    }

    private static int CountNodes(UiNodeDef n) => 1 + n.Children.Sum(CountNodes);

    private void Bubble()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RootNode));
        Changed?.Invoke();
    }
}
