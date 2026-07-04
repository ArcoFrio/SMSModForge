using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

public sealed class CharacterViewModel : ObservableObject
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
}
