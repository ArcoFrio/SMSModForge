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
public sealed class PlaceViewModel : ObservableObject
{
    /// <summary>
    /// Hard cap on navigator buttons per place. Matches the runtime's
    /// 12-button navigator grid (two rows of six once the strip extends).
    /// Surfaced to the UI so the author sees the limit before hitting it.
    /// </summary>
    public const int MaxNavigatorButtons = 12;

    public PlaceDef Model { get; }
    public ObservableCollection<NavigatorButtonViewModel> NavigatorButtons { get; }

    /// <summary>Extra sprite overlays layered onto this place's level.</summary>
    public ObservableCollection<OverlayViewModel> Overlays { get; }

    public PlaceViewModel(PlaceDef model)
    {
        Model = model;
        NavigatorButtons = new ObservableCollection<NavigatorButtonViewModel>(
            model.NavigatorButtons.Select(b =>
                new NavigatorButtonViewModel(b, removeCallback: RemoveNavigatorButton,
                                                moveCallback: MoveNavigatorButton)));
        Overlays = new ObservableCollection<OverlayViewModel>(
            model.Overlays.Select(o => new OverlayViewModel(o, RemoveOverlay)));
    }

    /// <summary>True while there's room for another navigator button.</summary>
    public bool CanAddNavigatorButton => NavigatorButtons.Count < MaxNavigatorButtons;

    /// <summary>"N / 12" progress label shown next to the Add button.</summary>
    public string NavigatorButtonCountLabel => $"{NavigatorButtons.Count} / {MaxNavigatorButtons}";

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    public string InternalName
    {
        get => Model.InternalName;
        set { Model.InternalName = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => Model.DisplayName;
        set { Model.DisplayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
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

    public float ParallaxStrength
    {
        get => Model.ParallaxStrength;
        set { Model.ParallaxStrength = value; OnPropertyChanged(); }
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

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";

    public NavigatorButtonViewModel? AddNavigatorButton()
    {
        if (!CanAddNavigatorButton) return null;   // 12-button cap
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

    public OverlayViewModel AddOverlay()
    {
        var def = new OverlayDef();
        Model.Overlays.Add(def);
        var vm = new OverlayViewModel(def, RemoveOverlay);
        Overlays.Add(vm);
        return vm;
    }

    public void RemoveOverlay(OverlayViewModel overlay)
    {
        Model.Overlays.Remove(overlay.Model);
        Overlays.Remove(overlay);
    }
}
