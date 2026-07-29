using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

public sealed class CharacterViewModel : ObservableObject, IFilterableTreeNode
{
    public CharacterDef Model { get; }
    public ObservableCollection<OutfitViewModel> Outfits { get; }

    public CharacterViewModel(CharacterDef model)
    {
        Model = model;
        Outfits = new ObservableCollection<OutfitViewModel>(model.Outfits.Select(o => new OutfitViewModel(o)));
        // Alphabetical display order in the Busts tree — view-layer only,
        // the model list keeps creation order.
        ViewSort.Alphabetical(Outfits, nameof(OutfitViewModel.Key));
    }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { Model.DisplayName = value; OnPropertyChanged(); }
    }

    public void AddOutfit()
    {
        var def = new OutfitDef
        {
            Key = $"{Name.ToLowerInvariant()}New",
            GameObjectName = $"{Name}New",
        };
        Model.Outfits.Add(def);
        Outfits.Add(new OutfitViewModel(def));
    }

    public void RemoveOutfit(OutfitViewModel vm)
    {
        Model.Outfits.Remove(vm.Model);
        Outfits.Remove(vm);
    }

    // ── Sidebar search (IFilterableTreeNode) ──────────────────────────────
    private bool _isFilteredIn = true;
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    // Collapsed on load, like the outfits beneath it — a pack opens to a list
    // of characters you can scan, not a wall of bust editors. Only the initial
    // state; expansion is remembered normally from then on.
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => $"{Name} {DisplayName}";
    public System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren => Outfits;
}
