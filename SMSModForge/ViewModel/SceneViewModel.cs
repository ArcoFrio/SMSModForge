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
        set
        {
            Model.SceneSprite = value;
            OnPropertyChanged();

            // Everything that depends on WHAT the file is, re-asked. This is
            // what makes the volume slider appear the moment a video is typed
            // rather than after a save or a reselect.
            OnPropertyChanged(nameof(IsAnimated));
            OnPropertyChanged(nameof(IsVideo));
            OnPropertyChanged(nameof(HasAudioTrack));
            OnPropertyChanged(nameof(MediaSummary));
        }
    }

    /// <summary>Whether the picked file moves — from its extension, which is
    /// the thing the author controls.</summary>
    public bool IsAnimated => Model.IsAnimated;

    /// <summary>Whether it is a video, the only kind that can carry sound.</summary>
    public bool IsVideo => Model.IsVideo;

    /// <summary>
    /// Whether the volume control has anything to control.
    /// <para/>
    /// Read from the file's own header rather than asked of the author. A
    /// file that cannot be read counts as HAVING audio: hiding the slider for
    /// a video that turns out to be loud leaves nothing to turn down, which is
    /// the worse of the two mistakes.
    /// </summary>
    public bool HasAudioTrack
    {
        get
        {
            // Decided from the PATH first. Asking the prober about a path that
            // could not be resolved - a pack not yet saved anywhere - got back
            // "not a video", because an empty string is not one, and the slider
            // vanished for a video that plainly is one.
            if (!Model.IsVideo) return false;

            string abs = AbsoluteSpritePath();
            if (abs.Length == 0) return true;          // cannot tell yet: offer it
            return MediaProbe.HasAudio(abs) != false;
        }
    }

    /// <summary>A line describing what was picked, for beside the path box.</summary>
    public string MediaSummary
    {
        get
        {
            switch (MediaProbe.KindOf(Model.SceneSprite))
            {
                case MediaProbe.MediaKind.Gif:
                    return "Animated GIF — decoded to frames when you save.";
                case MediaProbe.MediaKind.Video:
                    string abs = AbsoluteSpritePath();
                    var sound = abs.Length == 0 ? null : MediaProbe.HasAudio(abs);
                    if (sound == true) return "Video, with sound.";
                    if (sound == false) return "Video, silent.";
                    return "Video — could not read its tracks, so the volume is offered.";
                case MediaProbe.MediaKind.Still:
                    return "";
                default:
                    return string.IsNullOrWhiteSpace(Model.SceneSprite)
                        ? ""
                        : "Not a picture, GIF or video this tool can use.";
            }
        }
    }

    /// <summary>
    /// Whether an animated scene repeats. Meaningless for a still, which is
    /// why the control only shows for one that moves.
    /// </summary>
    public bool Loop
    {
        get => Model.Loop;
        set { Model.Loop = value; OnPropertyChanged(); }
    }

    /// <summary>A video's own sound, 0 to 1.</summary>
    public float Volume
    {
        get => Model.Volume;
        set
        {
            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            if (Model.Volume == clamped) return;
            Model.Volume = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>Where the picked file actually is, so its header can be read.
    /// Empty when the pack has not been saved anywhere yet.</summary>
    private string AbsoluteSpritePath()
    {
        string root = PackRepository.ActivePackRoot ?? "";
        string rel = Model.SceneSprite ?? "";
        if (root.Length == 0 || rel.Length == 0) return "";
        return System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
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
