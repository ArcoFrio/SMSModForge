using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="SceneDef"/> for the Scenes tab. Frame
/// selection is split across two backing fields on the model so authors can
/// freely toggle between a vanilla frame and a custom one without losing the
/// other slot — the runtime picks <see cref="CustomFrameSprite"/> when set,
/// otherwise falls back to <see cref="VanillaFrame"/>.
/// </summary>
public sealed class SceneViewModel : ObservableObject
{
    public SceneDef Model { get; }

    public SceneViewModel(SceneDef model) { Model = model; }

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW scene takes its key from
    // whatever is typed as the display name, editing the key stops that for
    // good, and a scene loaded from disk never re-derives.

    private readonly DerivedKey _derivedKey = new();

    /// <summary>Whether the runtime name still follows the display name.</summary>
    public bool KeyIsDerived => _derivedKey.IsDerived;

    /// <summary>Start deriving the key. Called for a scene the author has just
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

    public string SceneSprite
    {
        get => Model.SceneSprite;
        set { Model.SceneSprite = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Vanilla-frame file name (e.g. "PhotoFrame.png"). Empty when the
    /// author has chosen a custom frame instead. Round-tripped through
    /// the model's nullable backing field so a blank string clears the
    /// selection rather than storing an empty value in the manifest.
    /// </summary>
    public string VanillaFrame
    {
        get => Model.VanillaFrame ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.VanillaFrame == normalised) return;
            Model.VanillaFrame = normalised;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Pack-relative path to a custom frame PNG. When non-empty this
    /// overrides <see cref="VanillaFrame"/> at runtime — both fields
    /// stay editable in the UI so the author can switch back without
    /// retyping the vanilla pick.
    /// </summary>
    public string CustomFrameSprite
    {
        get => Model.CustomFrameSprite ?? "";
        set
        {
            string? normalised = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Model.CustomFrameSprite == normalised) return;
            Model.CustomFrameSprite = normalised;
            OnPropertyChanged();
        }
    }

    public SceneSoundMode Sound
    {
        get => Model.Sound;
        set { Model.Sound = value; OnPropertyChanged(); }
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : $"{DisplayName} ({Key})";
}
