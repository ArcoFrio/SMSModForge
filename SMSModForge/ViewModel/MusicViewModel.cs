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

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW track takes its key from
    // whatever is typed as the display name, editing the key stops that for
    // good, and a track loaded from disk never re-derives.

    private readonly DerivedKey _derivedKey = new();

    /// <summary>Whether the runtime name still follows the display name.</summary>
    public bool KeyIsDerived => _derivedKey.IsDerived;

    /// <summary>Start deriving the key. Called for a track the author has just
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
    /// The recording, as the author picked it. An edited track plays a file
    /// made from it when the pack is saved (<see cref="MusicDef.Edit"/>); this
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
                // A different recording: the cuts were moments in the old one,
                // while the effects still say what the author wants done.
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
    /// The track's volume against the game's own level for a track: 1.0 is
    /// that level - the 0.5 of the game's Beach track, which every pack track
    /// is copied from - and what the box shows when the pack leaves it; 0 is
    /// silence and 5 five times as loud (the author, 1.7.0). The pack stores
    /// the AudioSource's own volume, half of what is shown - see
    /// <see cref="SMSModForge.Model.GameLoudness.MusicStored"/>.
    /// </summary>
    // Raw text backing so a mid-edit "0." / "0.0" isn't reformatted back to "0"
    // before you can type the fraction (see SfxViewModel.DefaultVolumeText).
    private string? _volumeText;
    public string VolumeText
    {
        get => _volumeText ??= SMSModForge.Model.GameLoudness.VolumeText(SMSModForge.Model.GameLoudness.MusicShown(Model.Volume));
        set
        {
            _volumeText = value ?? "";
            if (SMSModForge.Model.GameLoudness.TryReadVolume(_volumeText, out float shown))
            {
                Model.Volume = SMSModForge.Model.GameLoudness.MusicStored(shown);
                OnPropertyChanged(nameof(VolumeValue));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>The same volume for the slider beside the box.</summary>
    public double VolumeValue
    {
        get => SMSModForge.Model.GameLoudness.MusicShown(Model.Volume);
        set
        {
            Model.Volume = SMSModForge.Model.GameLoudness.MusicStored(System.Math.Round(value, 2));
            _volumeText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
