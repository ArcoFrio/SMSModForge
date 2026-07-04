using System;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps an <see cref="OverlayDef"/> with INPC for the Places editor's
/// "Level overlays" list. Two-way bound by the per-row editor (name, sprite
/// path, position, sorting, flags).
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private readonly Action<OverlayViewModel>? _removeCallback;

    public OverlayDef Model { get; }

    public OverlayViewModel(OverlayDef model, Action<OverlayViewModel>? removeCallback = null)
    {
        Model = model;
        _removeCallback = removeCallback;
        RemoveCommand = new RelayCommand(
            () => _removeCallback?.Invoke(this),
            () => _removeCallback != null);
        Components = new ObservableCollection<ComponentRowViewModel>(
            model.Components.Select(c => new ComponentRowViewModel(c, RemoveComponent)));
        AddComponentCommand = new RelayCommand(() => AddComponent());
        Children = new ObservableCollection<OverlayViewModel>(
            model.Children.Select(c => new OverlayViewModel(c, RemoveChild)));
        AddChildCommand = new RelayCommand(() => AddChild());
    }

    /// <summary>Removes this overlay row from its parent (place or parent overlay).</summary>
    public RelayCommand RemoveCommand { get; }

    // ── Nested children (hierarchy) ───────────────────────────────────
    /// <summary>Extra GameObjects nested under this one. Same VM type, so the
    /// editor renders them recursively with the full set of controls.</summary>
    public ObservableCollection<OverlayViewModel> Children { get; }
    public RelayCommand AddChildCommand { get; }

    public OverlayViewModel AddChild()
    {
        var def = new OverlayDef();
        Model.Children.Add(def);
        var vm = new OverlayViewModel(def, RemoveChild);
        Children.Add(vm);
        return vm;
    }

    public void RemoveChild(OverlayViewModel vm)
    {
        Model.Children.Remove(vm.Model);
        Children.Remove(vm);
    }

    // ── Utility components ────────────────────────────────────────────
    public ObservableCollection<ComponentRowViewModel> Components { get; }
    public RelayCommand AddComponentCommand { get; }

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

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string Sprite
    {
        get => Model.Sprite;
        set { Model.Sprite = value; OnPropertyChanged(); }
    }

    public float X
    {
        get => Model.X;
        set { Model.X = value; OnPropertyChanged(); }
    }

    public float Y
    {
        get => Model.Y;
        set { Model.Y = value; OnPropertyChanged(); }
    }

    public int SortingOrder
    {
        get => Model.SortingOrder;
        set { Model.SortingOrder = value; OnPropertyChanged(); }
    }

    public bool ParallaxDisabled
    {
        get => Model.ParallaxDisabled;
        set { Model.ParallaxDisabled = value; OnPropertyChanged(); }
    }

    public bool StartActive
    {
        get => Model.StartActive;
        set { Model.StartActive = value; OnPropertyChanged(); }
    }

    public float StartAlpha
    {
        get => Model.StartAlpha;
        set { Model.StartAlpha = value; OnPropertyChanged(); }
    }

    public string Mask
    {
        get => Model.Mask;
        set { Model.Mask = value; OnPropertyChanged(); }
    }

    /// <summary>Short label for headers / tooltips.</summary>
    public string Display => string.IsNullOrWhiteSpace(Name) ? "(unnamed overlay)" : Name;
}
