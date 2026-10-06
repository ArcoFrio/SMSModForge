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

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW effect takes its key from
    // whatever is typed as the display name, editing the key stops that for
    // good, and a effect loaded from disk never re-derives.

    private readonly DerivedKey _derivedKey = new();

    /// <summary>Whether the runtime name still follows the display name.</summary>
    public bool KeyIsDerived => _derivedKey.IsDerived;

    /// <summary>Start deriving the key. Called for a effect the author has just
    /// added, never for one being loaded.</summary>
    public void DeriveKeyFromDisplayName(System.Func<System.Collections.Generic.IEnumerable<string>> siblingKeys)
        => _derivedKey.Follow(siblingKeys);

    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            // Typing a key is a decision, and it sticks.
            _derivedKey.Stop();
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
            if (_derivedKey.Next(value, Model.Key) is { } derived && derived != Model.Key)
            {
                Model.Key = derived;
                OnPropertyChanged(nameof(Key));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
        }
    }

    /// <summary>
    /// The recording, as the author picked it. An edited sound plays a file
    /// made from it when the pack is saved (<see cref="SfxDef.Edit"/>); this
    /// stays the recording, which is what there is to pick.
    /// </summary>
    public string AudioPath
    {
        get => Model.Edit?.Source is { Length: > 0 } source ? source : Model.AudioPath;
        set
        {
            value ??= "";
            if (AudioPath == value) return;
            if (Model.Edit is { } edit)
            {
                // A different recording: the cuts were moments in the old one
                // and mean nothing in this, while pitch, speed, echo and
                // reverb still say what the author wants done to it.
                bool neverMade = string.Equals(Model.AudioPath, edit.Source, System.StringComparison.OrdinalIgnoreCase);
                edit.Source = value;
                edit.Files = null;
                edit.Rendered = null;
                if (neverMade) Model.AudioPath = value;
                if (!edit.ChangesAnything)
                {
                    Model.Edit = null;
                    Model.AudioPath = value;
                }
            }
            else Model.AudioPath = value;
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
        get => _defaultVolumeText ??= SMSModForge.Model.GameLoudness.VolumeText(Model.DefaultVolume ?? 1f);
        set
        {
            _defaultVolumeText = value ?? "";
            if (SMSModForge.Model.GameLoudness.TryReadVolume(_defaultVolumeText, out float shown))
            {
                Model.DefaultVolume = SMSModForge.Model.GameLoudness.SfxStored(shown);
                OnPropertyChanged(nameof(DefaultVolumeValue));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The same volume for the slider beside the box: 0 to
    /// <see cref="SMSModForge.Model.GameLoudness.MaxVolume"/>, 1.0 - the game's own level -
    /// when the pack leaves it (the author, 1.7.0).
    /// </summary>
    public double DefaultVolumeValue
    {
        get => Model.DefaultVolume ?? 1f;
        set
        {
            Model.DefaultVolume = SMSModForge.Model.GameLoudness.SfxStored(System.Math.Round(value, 2));
            _defaultVolumeText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultVolumeText));
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
