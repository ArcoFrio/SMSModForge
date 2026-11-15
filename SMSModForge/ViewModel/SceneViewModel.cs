using SMSModForge.Model;
using SMSModForge.Localization;

namespace SMSModForge.ViewModel;

/// <summary>
/// INPC wrapper around a <see cref="SceneDef"/> for the Scenes tab. Frame
/// selection is split across two backing fields on the model so authors can
/// freely toggle between a vanilla frame and a custom one without losing the
/// other slot — the runtime picks <see cref="CustomFrameSprite"/> when set,
/// otherwise falls back to <see cref="VanillaFrame"/>.
/// </summary>
public sealed class SceneViewModel : ObservableObject, IDerivesKey
{
    public SceneDef Model { get; }

    public SceneViewModel(SceneDef model) { Model = model; }

    // ── Runtime name, derived ──────────────────────────────────────
    //
    // See DerivedKey for the rule. In short: a NEW scene takes its key from
    // whatever is typed as the display name, editing the key stops that for
    // good, and one loaded from disk follows only while its key is still
    // its display name made into a name (or a duplicate's "_copy").

    private readonly DerivedKey _derivedKey = new();

    /// <summary>Whether the runtime name still follows the display name.</summary>
    public bool KeyIsDerived => _derivedKey.IsDerived;

    /// <summary>Start deriving the key. Called for one the author has just
    /// added, duplicated or pasted.</summary>
    public void DeriveKeyFromDisplayName(System.Func<System.Collections.Generic.IEnumerable<string>> siblingKeys)
        => _derivedKey.Follow(siblingKeys);


    /// <summary>Players' saves remember a Starmaker photo's scene by its key
    /// (the gallery), so one never follows its display name once saved.</summary>
    public void WatchDisplayName(System.Func<System.Collections.Generic.IEnumerable<string>> siblingKeys)
        => _derivedKey.Watch(siblingKeys, Model.DisplayName, () => Model.StarmakerPhoto);

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
            OnPropertyChanged(nameof(ChangeSummary));
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
                    return Loc.T("scenes.media.gif");
                case MediaProbe.MediaKind.Video:
                    string abs = AbsoluteSpritePath();
                    var sound = abs.Length == 0 ? null : MediaProbe.HasAudio(abs);
                    if (sound == true) return Loc.T("scenes.media.videoSound");
                    if (sound == false) return Loc.T("scenes.media.videoSilent");
                    return Loc.T("scenes.media.videoUnknown");
                case MediaProbe.MediaKind.Still:
                    return "";
                default:
                    return string.IsNullOrWhiteSpace(Model.SceneSprite)
                        ? ""
                        : Loc.T("scenes.media.unusable");
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
            OnPropertyChanged(nameof(ChangeSummary));
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
            OnPropertyChanged(nameof(ChangeSummary));
        }
    }

    public SceneSoundMode Sound
    {
        get => Model.Sound;
        set { Model.Sound = value; OnPropertyChanged(); OnPropertyChanged(nameof(GameSound)); OnPropertyChanged(nameof(ChangeSummary)); }
    }

    /// <summary>Whether showing this scene counts as taking a Starmaker photo
    /// - see <see cref="SceneDef.StarmakerPhoto"/>.</summary>
    public bool StarmakerPhoto
    {
        get => Model.StarmakerPhoto;
        set { if (Model.StarmakerPhoto == value) return; Model.StarmakerPhoto = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChangeSummary)); }
    }

    public string Display => ListNames.Of(DisplayName, Key);

    // ── One of the game's scenes (1.9.0) ────────────────────────────
    //
    // The same panel as a scene of the pack's own, where what is left empty
    // stays the game's: no picture keeps its picture, no frame its frame, and
    // a sound of None its sound. See SceneDef.Source.

    /// <summary>Whether this changes one of the game's scenes, chosen or not yet.</summary>
    public bool IsGameScene => Model.IsGameScene;

    /// <summary>The game's scene it changes, or null: none chosen yet, or one
    /// the editor's catalogue does not have.</summary>
    public VanillaScenes.Scene? Game => VanillaScenes.Find(Model.Source);

    /// <summary>
    /// The game's scene, as the picker sets it.
    /// <para/>
    /// The display name follows the scene picked while it is still the name of
    /// the last one picked (or the name a new entry starts with), and the key
    /// follows the display name as for any scene just added: what the list
    /// shows is which of the game's scenes it is until the author names it.
    /// </summary>
    public VanillaScenes.Scene? GameSource
    {
        get => Game;
        set
        {
            if (value == null || !IsGameScene || Model.Source == value.Path) return;
            string? was = Game?.Name;
            Model.Source = value.Path;
            if (string.IsNullOrWhiteSpace(Model.DisplayName) || Model.DisplayName == was || Model.DisplayName == _startName)
                DisplayName = value.Name;
            RaiseGame();
        }
    }

    /// <summary>The name a new one is added with, which a pick replaces.</summary>
    private string? _startName;

    /// <summary>Remember the name a new entry was added with, so the first pick
    /// replaces it.</summary>
    public void StartsAs(string name) => _startName = name;

    /// <summary>The game's own picture of it, on disk, for the preview while the
    /// pack's picture is empty; empty otherwise.</summary>
    public string GamePicture => IsGameScene ? VanillaScenes.PictureOf(Game) : "";

    /// <summary>The game's own frame of it (one of the editor's VanillaFrames),
    /// for the preview while the pack names none; empty otherwise.</summary>
    public string GameFrame => IsGameScene ? Game?.FrameFile ?? "" : "";

    /// <summary>Whether it is one of the game's Starmaker photos, which pays out
    /// already: the tick to make it one is not offered.</summary>
    public bool IsGamePhoto => IsGameScene && Game?.Photo == true;

    /// <summary>Whether the Starmaker photo tick applies.</summary>
    public bool OffersStarmakerPhoto => !IsGamePhoto;

    /// <summary>The game's scene's name, or a word saying none is chosen.</summary>
    public string GameName => Game?.Name
        ?? (string.IsNullOrEmpty(Model.Source) ? Loc.T("scenes.game.noneChosen") : Model.Source!);

    /// <summary>What shows it in the game, for under the picker.</summary>
    public string GameShownBy
    {
        get
        {
            var g = Game;
            if (g == null) return string.IsNullOrEmpty(Model.Source) ? "" : Loc.T("scenes.game.unknown");
            return g.ShownBy.Count == 0 ? "" : Loc.F("scenes.game.shownBy", "list", string.Join(", ", g.ShownBy));
        }
    }

    /// <summary>The game's own sound for it, in words.</summary>
    private string GameSoundWord => Game?.Signal switch
    {
        "kiss" => Loc.T("scenes.game.sound.kiss"),
        "flash" => Loc.T("scenes.game.sound.flash"),
        null => "",
        _ => Loc.T("scenes.game.sound.none"),
    };

    /// <summary>One choice in a game scene's sound box.</summary>
    public sealed record SoundChoice(SceneSoundMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The sound box for one of the game's scenes: keep the game's (saying
    /// what that is), or one of the three a scene of the pack's own has.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<SoundChoice> GameSoundChoices => new[]
    {
        new SoundChoice(SceneSoundMode.None, GameSoundWord.Length == 0
            ? Loc.T("scenes.game.soundKeepPlain")
            : Loc.F("scenes.game.soundKeep", "sound", GameSoundWord)),
        new SoundChoice(SceneSoundMode.Kiss, Loc.T("scenes.game.sound.kiss")),
        new SoundChoice(SceneSoundMode.Flash, Loc.T("scenes.game.sound.flash")),
        new SoundChoice(SceneSoundMode.Silent, Loc.T("scenes.game.sound.silent")),
    };

    /// <summary>
    /// The sound box's choice. By the choice rather than by its value: the list is
    /// made again whenever another scene is picked (the first choice names that
    /// scene's sound), and a box selecting by value let go of its selection when
    /// its list changed under it. A choice is a record, so the one in the new list
    /// is equal to the one selected.
    /// </summary>
    public SoundChoice? GameSound
    {
        get => System.Linq.Enumerable.FirstOrDefault(GameSoundChoices, c => c.Mode == Model.Sound);
        set { if (value != null && value.Mode != Model.Sound) Sound = value.Mode; }
    }

    /// <summary>
    /// What this changes of the game's scene, in a few words for its row in the
    /// list: "picture, frame", or "no change" - which still lets the pack show
    /// it by its key.
    /// </summary>
    public string ChangeSummary
    {
        get
        {
            if (!IsGameScene) return "";
            if (string.IsNullOrEmpty(Model.Source)) return Loc.T("scenes.game.change.notChosen");
            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(Model.SceneSprite)) parts.Add(Loc.T("scenes.game.change.picture"));
            if (!string.IsNullOrWhiteSpace(Model.CustomFrameSprite) || !string.IsNullOrWhiteSpace(Model.VanillaFrame))
                parts.Add(Loc.T("scenes.game.change.frame"));
            if (Model.Sound != SceneSoundMode.None) parts.Add(Loc.T("scenes.game.change.sound"));
            if (Model.StarmakerPhoto && !IsGamePhoto) parts.Add(Loc.T("scenes.game.change.photo"));
            return parts.Count == 0 ? Loc.T("scenes.game.change.none") : string.Join(", ", parts);
        }
    }

    /// <summary>Everything that depends on which scene is chosen, asked again.</summary>
    private void RaiseGame()
    {
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(GameSource));
        OnPropertyChanged(nameof(GamePicture));
        OnPropertyChanged(nameof(GameFrame));
        OnPropertyChanged(nameof(IsGamePhoto));
        OnPropertyChanged(nameof(OffersStarmakerPhoto));
        OnPropertyChanged(nameof(GameName));
        OnPropertyChanged(nameof(GameShownBy));
        OnPropertyChanged(nameof(GameSoundChoices));
        OnPropertyChanged(nameof(GameSound));
        OnPropertyChanged(nameof(ChangeSummary));
    }
}
