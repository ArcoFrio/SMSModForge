using System;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper for one <see cref="ComponentDef"/> attached to an overlay
/// (GameObject). Exposes the component <see cref="Type"/> plus every
/// config field; the <c>Is*</c> flags let the editor show only the fields the
/// chosen type actually uses (same conditional-visibility idea as the node
/// action rows).
/// </summary>
public sealed class ComponentRowViewModel : ObservableObject
{
    public ComponentDef Model { get; }
    private readonly Action<ComponentRowViewModel> _remove;

    public ComponentRowViewModel(ComponentDef model, Action<ComponentRowViewModel> remove)
    {
        Model = model;
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public RelayCommand RemoveCommand { get; }

    /// <summary>Enum values for the type dropdown.</summary>
    public Array ComponentTypes { get; } = Enum.GetValues(typeof(PackComponentType));

    public PackComponentType Type
    {
        get => Model.Type;
        set
        {
            if (Model.Type == value) return;
            Model.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFadeIn));
            OnPropertyChanged(nameof(IsFadeOut));
            OnPropertyChanged(nameof(IsRandom));
            OnPropertyChanged(nameof(IsBlinking));
            OnPropertyChanged(nameof(Display));
        }
    }

    public bool IsFadeIn   => Model.Type == PackComponentType.FadeInSprite;
    public bool IsFadeOut  => Model.Type == PackComponentType.FadeOutSprite;
    public bool IsRandom   => Model.Type == PackComponentType.RandomChildActivator;
    public bool IsBlinking => Model.Type == PackComponentType.BlinkingSprite;

    // ── FadeInSprite ──────────────────────────────────────────────────
    public float FadeDuration { get => Model.FadeDuration; set { Model.FadeDuration = value; OnPropertyChanged(); } }
    public float TargetAlpha  { get => Model.TargetAlpha;  set { Model.TargetAlpha = value;  OnPropertyChanged(); } }

    // ── FadeOutSprite ─────────────────────────────────────────────────
    public float Duration            { get => Model.Duration;            set { Model.Duration = value;            OnPropertyChanged(); } }
    public bool DeactivateOnComplete { get => Model.DeactivateOnComplete; set { Model.DeactivateOnComplete = value; OnPropertyChanged(); } }

    // ── Shared by both fades ──────────────────────────────────────────
    public float StartDelay { get => Model.StartDelay; set { Model.StartDelay = value; OnPropertyChanged(); } }

    // ── RandomChildActivator ──────────────────────────────────────────
    public bool ReshuffleOnEnable { get => Model.ReshuffleOnEnable; set { Model.ReshuffleOnEnable = value; OnPropertyChanged(); } }

    // ── BlinkingSprite ────────────────────────────────────────────────
    public float BlinkInterval { get => Model.BlinkInterval; set { Model.BlinkInterval = value; OnPropertyChanged(); } }
    public float MinAlpha      { get => Model.MinAlpha;      set { Model.MinAlpha = value;      OnPropertyChanged(); } }
    public float MaxAlpha      { get => Model.MaxAlpha;      set { Model.MaxAlpha = value;      OnPropertyChanged(); } }

    public string Display => Type.ToString();
}
