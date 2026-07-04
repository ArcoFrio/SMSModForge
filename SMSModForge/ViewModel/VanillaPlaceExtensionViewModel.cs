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

    public VanillaPlaceExtensionViewModel(VanillaPlaceExtensionDef model)
    {
        Model = model;
        NavigatorButtons = new ObservableCollection<NavigatorButtonViewModel>(
            model.NavigatorButtons.Select(b =>
                new NavigatorButtonViewModel(b, removeCallback: RemoveNavigatorButton,
                                                moveCallback: MoveNavigatorButton)));
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
        }
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
