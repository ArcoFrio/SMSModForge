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
            model.Nodes.Select(n => new UiNodeViewModel(n, null, HasChanges, Reset)));
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

    // ── Copying objects about ────────────────────────────────────────
    //
    // A whole object and everything under it, so a shop card built once can be
    // laid out nine times. The clipboard clones on the way in AND on the way
    // out, so a copy taken once can be pasted repeatedly and no two pastes
    // share anything.

    public RelayCommand CopyNodeCommand => _copyNode ??= new RelayCommand(() =>
    {
        if (SelectedNode != null) Services.EditorClipboard.SetItem(SelectedNode.Model);
    }, () => SelectedNode != null);

    private RelayCommand? _copyNode;

    /// <summary>
    /// Paste as a child of whatever is selected.
    /// <para/>
    /// A child rather than a sibling, because the tree's own selection is a
    /// container as often as it is a leaf, and "inside the thing I am pointing
    /// at" is the reading that needs no explaining. Pasting onto an object the
    /// GAME owns is allowed: what lands is the pack's, and adding to one of the
    /// game's objects is exactly what an extension does.
    /// </summary>
    public RelayCommand PasteNodeCommand => _pasteNode ??= new RelayCommand(() =>
    {
        var copied = Services.EditorClipboard.GetItem<UiNodeDef>();
        if (copied == null) return;

        var parent = SelectedNode ?? Nodes.FirstOrDefault();
        if (parent == null) return;

        SelectedNode = parent.AddChild(copied);
    }, () => Services.EditorClipboard.Has<UiNodeDef>() && Nodes.Count > 0);

    private RelayCommand? _pasteNode;

    /// <summary>Copy and paste in one go, landing beside the original rather
    /// than inside it - which is what "another one of these" means.</summary>
    public RelayCommand DuplicateNodeCommand => _duplicateNode ??= new RelayCommand(() =>
    {
        var chosen = SelectedNode;
        var parent = chosen?.Parent;
        if (chosen == null || parent == null) return;    // the root has no beside

        Services.EditorClipboard.SetItem(chosen.Model);
        var copied = Services.EditorClipboard.GetItem<UiNodeDef>();
        if (copied == null) return;

        var made = parent.AddChild(copied);
        parent.MoveTo(made, parent.Children.IndexOf(chosen) + 1);
        SelectedNode = made;
    }, () => SelectedNode?.Parent != null);

    private RelayCommand? _duplicateNode;

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
            OnPropertyChanged(nameof(ShowsSortingOrder));
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
            OnPropertyChanged(nameof(ShowsSortingOrder));
        }
    }

    public bool CanChooseHiding => !Model.IsVanillaBased;

    /// <summary>Whether it is up as soon as the game loads. Most are not: the
    /// vanilla scene keeps 34 of its 49 canvases switched off and turns one on
    /// when its moment arrives.</summary>
    public bool StartsOpen
    {
        get => Model.StartsOpen;
        set
        {
            if (Model.StartsOpen == value) return;
            Model.StartsOpen = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// What the checkbox binds to, which is the positive of what is stored.
    /// <para/>
    /// The manifest stores silence because that is the unusual answer and an
    /// absent field should mean the ordinary one; the box in front of an author
    /// reads the way they think about it, ticked for a screen whose buttons
    /// click.
    /// </summary>
    public bool ButtonsMakeSound
    {
        get => !Model.SilentButtons;
        set
        {
            if (Model.SilentButtons == !value) return;
            Model.SilentButtons = !value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ButtonSoundInEffect));
            OnPropertyChanged(nameof(ShowsDefaultButtonSound));
        }
    }

    /// <summary>
    /// What these buttons will actually sound like, said out loud.
    /// <para/>
    /// An empty field means the default, and a field that looks empty reads as
    /// "nothing" to everyone who did not write the code. Rather than write the
    /// default into every screen - which would put a line in every manifest and
    /// pin a value nobody chose - the editor says what the blank will do.
    /// </summary>
    public string ButtonSoundInEffect
        => !ButtonsMakeSound ? "" : $"using the game's own click: {UiDef.DefaultButtonSound}";

    /// <summary>Whether to say it: only when the field is blank AND something
    /// is going to play. A screen naming its own sound can be read off the
    /// field itself.</summary>
    public bool ShowsDefaultButtonSound
        => ButtonsMakeSound && string.IsNullOrEmpty(Model.ButtonSound);

    /// <summary>How this screen arrives when it is switched on.</summary>
    public UiOpenViewModel Open => _open ??= new UiOpenViewModel(
        () => Model.Open, v => Model.Open = v,
        () => Model.Nodes.FirstOrDefault());

    private UiOpenViewModel? _open;

    /// <summary>How this screen leaves when an action switches it off.</summary>
    public UiOpenViewModel Close => _close ??= new UiOpenViewModel(
        () => Model.Close, v => Model.Close = v,
        () => Model.Nodes.FirstOrDefault(), closing: true);

    private UiOpenViewModel? _close;

    /// <summary>What every button on this screen sounds like. See
    /// <see cref="UiDef.ButtonSound"/>.</summary>
    public string ButtonSound
    {
        get => Model.ButtonSound;
        set
        {
            if (Model.ButtonSound == value) return;
            Model.ButtonSound = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsDefaultButtonSound));
        }
    }

    /// <summary>
    /// Draw order against other independent UI - higher is nearer the front.
    /// <para/>
    /// Only means anything when this UI is NOT hidden with the game's
    /// interface, because that is the only case where it gets a canvas of its
    /// own. Inside the gameplay canvas it is one object among the game's, and
    /// order there is position among siblings - the same thing the object tree
    /// below already decides.
    /// </summary>
    public int SortingOrder
    {
        get => Model.SortingOrder;
        set
        {
            if (Model.SortingOrder == value) return;
            Model.SortingOrder = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Whether a sorting order is worth offering - see the property.
    /// Hidden rather than disabled, so nobody sets a number that does
    /// nothing.</summary>
    public bool ShowsSortingOrder => ShowsOwnSettings && !HidesWithGameplayUi;

    /// <summary>The template this was started from, for the author's own
    /// reference. Blank once it stops matching anything known.</summary>
    public string TemplateName => UiTemplate.Find(Model.Template)?.Name ?? "";

    /// <summary>
    /// Bring a copy of one of the game's screens in as a child of whatever is
    /// selected.
    /// <para/>
    /// Starting a whole UI as a copy replaces the tree, which is right when the
    /// screen IS the copy. A screen assembled from several of the game's pieces
    /// - which is how its own gift window is built, from two lists and a close
    /// button - needs them brought in one at a time instead.
    /// </summary>
    public RelayCommand AddCopyChildCommand => _addCopyChild ??= new RelayCommand(arg =>
    {
        var entry = arg as VanillaUiCatalog.Base ?? VanillaUiCatalog.Find(arg as string);
        var vanilla = entry == null ? null : VanillaUiLibrary.Node(entry);
        if (vanilla == null) return;

        var parent = SelectedNode ?? Nodes.FirstOrDefault();
        if (parent == null) return;

        var copied = VanillaUiSeed.CopyOf(vanilla, VanillaUiLibrary.Assets.NameForKey);
        if (copied != null) SelectedNode = parent.AddChild(copied);
    }, arg => Nodes.Count > 0
           && (arg is VanillaUiCatalog.Base || VanillaUiCatalog.Find(arg as string) != null));

    private RelayCommand? _addCopyChild;

    /// <summary>Add an object inside whatever is selected, built from a
    /// template rather than bare.</summary>
    public RelayCommand AddChildTemplateCommand => _addChildTemplate ??= new RelayCommand(arg =>
    {
        if (arg is not UiTemplate template) return;
        var parent = SelectedNode ?? Nodes.FirstOrDefault();
        if (parent == null) return;
        SelectedNode = parent.AddChild(template.Build());
    }, arg => arg is UiTemplate && Nodes.Count > 0);

    private RelayCommand? _addChildTemplate;

    /// <summary>
    /// Put one object back the way the game has it.
    /// <para/>
    /// Undo works on a step; this works on an object, which is what an author
    /// wants after nudging one panel about and deciding they preferred it where
    /// it was. Afterwards the node asserts nothing and drops out of the pack.
    /// </summary>
    public void Reset(UiNodeViewModel node)
    {
        if (node == null || !node.IsVanilla || !Model.IsVanillaBased) return;
        var vanilla = VanillaUiLibrary.Node(Catalog);
        var against = vanilla == null ? null : VanillaUiDelta.NodeAt(vanilla, node.Model.Bind);
        if (against == null) return;

        VanillaUiSeed.ResetTo(node.Model, against, VanillaUiLibrary.Assets.NameForKey);
        node.RefreshAll();
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke();
    }

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
        var vm = new UiNodeViewModel(merged, null, HasChanges, Reset);
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

    /// <summary>
    /// Counts edits. The preview watches this rather than the tree itself.
    /// <para/>
    /// An edit changes what is INSIDE the tree without replacing it, and a
    /// dependency property does not fire when it is set to the object it
    /// already holds - so binding the preview to the root alone leaves it
    /// showing the state the tree was in when it was last selected. A number
    /// that is different every time is the signal a binding can actually carry.
    /// </summary>
    public int Revision { get; private set; }

    private void Bubble()
    {
        Revision++;
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RootNode));
        OnPropertyChanged(nameof(Revision));
        Changed?.Invoke();
    }
}
