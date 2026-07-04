using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="SfxDef"/> for the SFX tab. The
/// <see cref="SfxDef.TextPatterns"/> list is exposed as a single
/// comma-separated string for editing; multiple patterns per entry
/// (e.g. <c>*yank*,*yeet*</c> pointing at one clip) are still
/// supported, the comma is just the wire format the editor uses.
/// </summary>
public sealed class SfxViewModel : ObservableObject
{
    public SfxDef Model { get; }

    public SfxViewModel(SfxDef model) { Model = model; }

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
    /// Default playback volume as a string. Empty = inherit
    /// (PlaySFX uses 1.0). Tri-state semantics for the same
    /// reason as <see cref="MusicViewModel.VolumeText"/>.
    /// </summary>
    // Backs the editable text so a mid-edit value like "0." or "0.0" isn't
    // reformatted back to "0" the instant it parses — which made fractions
    // below 1 impossible to type. The raw text is what the box shows; the model
    // is updated whenever it parses.
    private string? _defaultVolumeText;
    public string DefaultVolumeText
    {
        get => _defaultVolumeText ??=
            (Model.DefaultVolume.HasValue ? Model.DefaultVolume.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        set
        {
            _defaultVolumeText = value ?? "";
            string s = _defaultVolumeText.Trim();
            if (string.IsNullOrEmpty(s)) Model.DefaultVolume = null;
            else if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var f))
                Model.DefaultVolume = f;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Comma-separated view of <see cref="SfxDef.TextPatterns"/>.
    /// Round-trips through the model — empty string clears the
    /// list so the SFX only fires on explicit <c>PlaySFX</c> calls.
    /// Whitespace around each pattern is trimmed; the asterisk
    /// brackets themselves are part of the pattern and stay
    /// verbatim.
    /// </summary>
    public string TextPatternsCsv
    {
        get => Model.TextPatterns == null ? "" : string.Join(", ", Model.TextPatterns);
        set
        {
            var parts = (value ?? "").Split(',')
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            if (parts.Count == 0)
            {
                if (Model.TextPatterns == null || Model.TextPatterns.Count == 0) return;
                Model.TextPatterns = null;
            }
            else
            {
                Model.TextPatterns = parts;
            }
            OnPropertyChanged();
        }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
