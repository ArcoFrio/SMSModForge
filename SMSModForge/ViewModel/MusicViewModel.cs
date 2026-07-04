using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="MusicDef"/> for the Music tab.
/// The loop / volume overrides are exposed as plain strings so the
/// UI can leave them empty (= use the cloned Beach template's
/// defaults) without forcing a numeric edit.
/// </summary>
public sealed class MusicViewModel : ObservableObject
{
    public MusicDef Model { get; }

    public MusicViewModel(MusicDef model) { Model = model; }

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

    public string AudioPath
    {
        get => Model.AudioPath;
        set
        {
            if (Model.AudioPath == value) return;
            Model.AudioPath = value ?? "";
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Loop override exposed as a tri-state through a string —
    /// empty = inherit from the Beach template (which loops),
    /// <c>true</c> / <c>false</c> = explicit override.
    /// </summary>
    public string LoopText
    {
        get => Model.Loop.HasValue ? Model.Loop.Value.ToString().ToLowerInvariant() : "";
        set
        {
            string s = (value ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(s)) Model.Loop = null;
            else if (s == "true" || s == "1" || s == "yes") Model.Loop = true;
            else if (s == "false" || s == "0" || s == "no") Model.Loop = false;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Volume override (0..1) exposed as a string for the same
    /// "empty = inherit" tri-state semantics.
    /// </summary>
    // Raw text backing so a mid-edit "0." / "0.0" isn't reformatted back to "0"
    // before you can type the fraction (see SfxViewModel.DefaultVolumeText).
    private string? _volumeText;
    public string VolumeText
    {
        get => _volumeText ??=
            (Model.Volume.HasValue ? Model.Volume.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        set
        {
            _volumeText = value ?? "";
            string s = _volumeText.Trim();
            if (string.IsNullOrEmpty(s)) Model.Volume = null;
            else if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var f))
                Model.Volume = f;
            OnPropertyChanged();
        }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
