using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="WallpaperDef"/> for the
/// Wallpapers tab. Both <see cref="SpritePath"/> (pack-relative) and
/// <see cref="ExternalSpritePath"/> (transitional absolute path) are
/// exposed so authors can move between bundled and external sources
/// without losing the other slot. Unlock conditions use the standard
/// condition rows (same editor as dialogues / rules / hooks).
/// </summary>
public sealed class WallpaperViewModel : ObservableObject
{
    public WallpaperDef Model { get; }

    public ObservableCollection<NodeConditionViewModel> UnlockConditions { get; }

    public WallpaperViewModel(WallpaperDef model)
    {
        Model = model;
        // Polled context: the runtime re-evaluates unlock conditions every
        // frame, so the per-evaluation Random gate is not offered here.
        UnlockConditions = new ObservableCollection<NodeConditionViewModel>(
            model.UnlockConditions.Select(c => new NodeConditionViewModel(c, RemoveUnlockCondition)));

        AddUnlockConditionCommand = new RelayCommand(() =>
        {
            var def = new NodeConditionDef { Type = NodeConditionTypes.VariableEquals };
            Model.UnlockConditions.Add(def);
            UnlockConditions.Add(new NodeConditionViewModel(def, RemoveUnlockCondition));
        });
        AddUnlockConditionGroupCommand = new RelayCommand(() =>
        {
            var def = new NodeConditionDef { Type = NodeConditionTypes.GroupAll, Conditions = new() };
            Model.UnlockConditions.Add(def);
            UnlockConditions.Add(new NodeConditionViewModel(def, RemoveUnlockCondition));
        });
        CopyUnlockConditionsCommand = new RelayCommand(
            () => Services.EditorClipboard.SetConditions(Model.UnlockConditions),
            () => Model.UnlockConditions.Count > 0);
        PasteUnlockConditionsCommand = new RelayCommand(
            () => PasteUnlockConditions(overwrite: false),
            () => Services.EditorClipboard.HasConditions);
        OverwriteUnlockConditionsCommand = new RelayCommand(
            () => PasteUnlockConditions(overwrite: true),
            () => Services.EditorClipboard.HasConditions);
    }

    public RelayCommand AddUnlockConditionCommand { get; }
    public RelayCommand AddUnlockConditionGroupCommand { get; }
    public RelayCommand CopyUnlockConditionsCommand { get; }
    public RelayCommand PasteUnlockConditionsCommand { get; }
    public RelayCommand OverwriteUnlockConditionsCommand { get; }

    public void RemoveUnlockCondition(NodeConditionViewModel c)
    {
        Model.UnlockConditions.Remove(c.Model);
        UnlockConditions.Remove(c);
    }

    private void PasteUnlockConditions(bool overwrite)
    {
        var src = Services.EditorClipboard.Conditions;
        if (src == null || src.Count == 0) return;
        if (overwrite) { Model.UnlockConditions.Clear(); UnlockConditions.Clear(); }
        foreach (var def in Services.EditorClipboard.Clone(src))
        {
            Model.UnlockConditions.Add(def);
            UnlockConditions.Add(new NodeConditionViewModel(def, RemoveUnlockCondition));
        }
    }

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

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            if (Model.DisplayName == value) return;
            Model.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>
    /// Pack-relative path to the wallpaper PNG. Wins over
    /// <see cref="ExternalSpritePath"/> at runtime when both are set.
    /// </summary>
    public string SpritePath
    {
        get => Model.SpritePath ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.SpritePath == normalised) return;
            Model.SpritePath = normalised;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Absolute path used during the host mod transition. Fallback
    /// when <see cref="SpritePath"/> is empty.
    /// </summary>
    public string ExternalSpritePath
    {
        get => Model.ExternalSpritePath ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.ExternalSpritePath == normalised) return;
            Model.ExternalSpritePath = normalised;
            OnPropertyChanged();
        }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
