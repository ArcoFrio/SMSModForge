using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Rendering;

namespace SMSModForge.ViewModel;

/// <summary>
/// One vanilla UI the pack changes, for the UI tab.
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
public sealed class VanillaUiExtensionViewModel : ObservableObject
{
    public VanillaUiExtensionDef Model { get; }
    public ObservableCollection<UiNodeViewModel> Nodes { get; }

    /// <summary>Raised when anything in the tree changes, so the preview can
    /// redraw. One event for the whole extension rather than a subscription per
    /// row, since the preview redraws the whole screen anyway.</summary>
    public event Action? Changed;

    public VanillaUiExtensionViewModel(VanillaUiExtensionDef model)
    {
        Model = model;
        Nodes = new ObservableCollection<UiNodeViewModel>(
            model.Nodes.Select(n => new UiNodeViewModel(n)));
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
            return string.IsNullOrEmpty(Model.Source) ? "(nothing chosen)" : Model.Source;
        }
    }

    /// <summary>What this extension is doing, in a line: how much of the screen
    /// it touches against how much it merely holds. An author who has moved one
    /// label should be able to see that they have moved one label.</summary>
    public string Summary
    {
        get
        {
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

        var merged = VanillaUiSeed.Merge(vanilla, Model.Nodes, out int stranded);
        Stranded = stranded;

        Model.Nodes.Clear();
        foreach (var node in Nodes) node.Changed -= Bubble;
        Nodes.Clear();

        Model.Nodes.Add(merged);
        var vm = new UiNodeViewModel(merged);
        vm.Changed += Bubble;
        Nodes.Add(vm);

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Stranded));
        OnPropertyChanged(nameof(Warning));
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

    private void Bubble()
    {
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke();
    }
}
