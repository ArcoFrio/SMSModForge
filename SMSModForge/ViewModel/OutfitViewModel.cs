using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Wraps an <see cref="OutfitDef"/> with INPC so the editor view updates on
/// every keystroke. Direct field exposure is fine — we don't need transforms,
/// the binding pipes value changes straight back to the POCO.
/// </summary>
public sealed class OutfitViewModel : ObservableObject, IFilterableTreeNode, IMaskEditorHost
{
    public OutfitDef Model { get; }

    /// <summary>
    /// The character wearing this, or null for an outfit built before anything
    /// needed to know.
    /// <para/>
    /// Needed because the same editor now shows three different things. On a
    /// pack character every outfit is art the pack draws. On one of the GAME's
    /// characters an outfit is usually a bust name with nothing to edit — but
    /// it can also be a bust the pack ADDED, which is a pack outfit in every
    /// respect, and the panels have to follow the outfit rather than the
    /// character to tell those two apart.
    /// </summary>
    private readonly CharacterViewModel? _owner;

    public OutfitViewModel(OutfitDef model, CharacterViewModel? owner = null)
    {
        Model = model;
        _owner = owner;
        ResetJiggleCommand = new RelayCommand(p =>
        {
            Model.Jiggle.ResetToDefault(p as string ?? "");
            RaiseJiggleChanged();
        });

        if (owner != null)
        {
            owner.ExpressionsChanged += (_, _) =>
            {
                RebuildOverrides();
                OnPropertyChanged(nameof(ExpressionFilesHint));
            };
            // The row's own tag, alongside the character's. A borrowed
            // character can have sixty-five outfits and one replaced texture,
            // and the character-level tag says only that something in there
            // changed - not which row to open.
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChangedTag)
                    || e.PropertyName == nameof(HasChangedTag)) return;
                RefreshTag();
            };
            // A sprite path, a jiggle number, a replaced texture: all of them
            // are changes to the CHARACTER as far as the manifest is concerned,
            // and the modified tag sits on the character's row.
            PropertyChanged += (_, _) => owner.RefreshModified();
        }
    }

    // ── Whose art is this? ────────────────────────────────────────

    /// <summary>
    /// One of the game's own busts: a name, and nothing of the pack's behind
    /// it. Everything that describes art is hidden for these, and what is left
    /// is what the pack CAN say about somebody else's bust — which textures to
    /// replace on it.
    /// </summary>
    public bool IsVanillaBust => _owner?.IsVanillaBust == true && !Model.PackArt;

    /// <summary>
    /// The pack draws this one, whoever wears it. True for every outfit on a
    /// pack character, and for a bust the pack added to one of the game's.
    /// </summary>
    public bool ShowsPackArt => !IsVanillaBust && _owner?.HasNoBust != true;

    /// <summary>Whether the outfit's name is the author's to choose. It is not
    /// for one of the game's busts: the name IS which bust it is.</summary>
    public bool CanEditName => !IsVanillaBust;

    /// <summary>A bust the pack added to one of the game's characters — worth
    /// saying on screen, since it sits in a list of the game's own.</summary>
    public bool IsAddedToVanilla => Model.PackArt && _owner?.IsVanillaBust == true;

    // ── Replacing textures on somebody else's bust ────────────────────

    private System.Collections.ObjectModel.ObservableCollection<SpriteOverrideViewModel>? _overrides;

    /// <summary>
    /// One row per texture this bust has: the fixed slots, then a face for each
    /// expression the character can pull.
    /// <para/>
    /// Built lazily and rebuilt when the character's expressions change, which
    /// is the sync the two panels need: an expression added in the Speech
    /// expressions box is a face the runtime will create on this bust, so it
    /// needs somewhere here to hang its art.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<SpriteOverrideViewModel> Overrides
    {
        get
        {
            if (_overrides == null)
            {
                _overrides = new System.Collections.ObjectModel.ObservableCollection<SpriteOverrideViewModel>();
                RebuildOverrides();
            }
            return _overrides;
        }
    }

    private void RebuildOverrides()
    {
        if (_overrides == null) return;
        _overrides.Clear();

        foreach (string slot in SMSModForge.Shared.SpriteSlotNames.Fixed)
            _overrides.Add(Row(slot));

        foreach (string face in FaceNames())
            _overrides.Add(Row(SMSModForge.Shared.SpriteSlotNames.Expression(face)));

        OnPropertyChanged(nameof(Overrides));
    }

    private SpriteOverrideViewModel Row(string slot)
    {
        var row = new SpriteOverrideViewModel(Model, slot);
        row.PropertyChanged += (_, e) =>
        {
            // The painter's in-progress buffer, republished under the name the
            // preview watches. Kept OUT of the reload below deliberately: a
            // brush stamp bumps this several times a second, and the reload is
            // file I/O. See JigglePreview's own note on the same trap.
            if (e.PropertyName == nameof(SpriteOverrideViewModel.LiveMaskBgra))
            {
                OnPropertyChanged(nameof(LiveMaskBgra));
                return;
            }
            if (e.PropertyName == nameof(SpriteOverrideViewModel.LiveMaskRevision))
            {
                OnPropertyChanged(nameof(LiveMaskRevision));
                return;
            }

            _owner?.RefreshModified();
            RefreshTag();
            // ...and the preview, which draws these over the game's art. A row
            // raises its own change, not the outfit's, so without this the
            // picture beside the panel went on showing the bust the game ships
            // while the author picked art for it.
            OnPropertyChanged(nameof(Overrides));
        };
        return row;
    }

    /// <summary>
    /// The faces this bust can show: the ones the game gave THIS bust, plus
    /// any the pack added to the character.
    /// <para/>
    /// Per bust rather than per character for the game's half, because the two
    /// genuinely differ — a character can have expression art on some outfits
    /// and none on others, and offering a row for art that is not there would
    /// have an author replacing a face the bust cannot show.
    /// </summary>
    private System.Collections.Generic.IEnumerable<string> FaceNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string face in InGameOrder(
                     SMSModForge.Shared.VanillaBustExpressions.For(Model.GameObjectName ?? "")))
            if (seen.Add(face)) yield return face;

        if (_owner == null) yield break;
        foreach (var added in _owner.AddedFaceNames)
            if (!string.IsNullOrWhiteSpace(added) && seen.Add(added)) yield return added;
    }

    /// <summary>
    /// The files an outfit's expression prefix will be asked for.
    /// <para/>
    /// Built from the faces the character actually has rather than fixed at the
    /// four, because that is now what the runtime loads. A face the author
    /// added shows up here the moment it is named, which is the only thing in
    /// the editor that says where to put its art.
    /// </summary>
    public string ExpressionFilesHint
        => string.Join("/", PackFaceNames().Select(f => f + ".png"));

    /// <summary>The faces a bust the PACK draws will be given art for: the four
    /// every bust is built with, plus any the character declares of its
    /// own.</summary>
    private IEnumerable<string> PackFaceNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string face in InGameOrder(SMSModForge.Shared.VanillaBustExpressions.Standard))
            if (seen.Add(face)) yield return face;

        if (_owner == null) yield break;
        foreach (var added in _owner.AddedFaceNames)
            if (!string.IsNullOrWhiteSpace(added) && seen.Add(added)) yield return added;
    }

    /// <summary>
    /// The four in the order the GAME numbers them — Happy, Angry, Sad, Flirty
    /// — rather than the order they happen to arrive in.
    /// <para/>
    /// The shipped dataset is alphabetical because its generator sorted, and
    /// the editor has always listed them the game's way. Two orders for the
    /// same four faces in one window is a small thing that reads as a bug.
    /// Anything the list does not know goes after them, alphabetically.
    /// </summary>
    private static IEnumerable<string> InGameOrder(IEnumerable<string> faces)
        => faces.OrderBy(f =>
               {
                   int at = Array.FindIndex(ExpressionSpec.Names,
                       n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase));
                   return at < 0 ? int.MaxValue : at;
               })
               .ThenBy(f => f, StringComparer.OrdinalIgnoreCase);

    /// <summary>The character's expressions changed, or the bust this outfit
    /// names did — either way the face rows are stale.</summary>
    public void RefreshOverrides()
    {
        RebuildOverrides();
        RefreshTag();
        OnPropertyChanged(nameof(ExpressionFilesHint));
        OnPropertyChanged(nameof(IsVanillaBust));
        OnPropertyChanged(nameof(ShowsPackArt));
        OnPropertyChanged(nameof(CanEditName));
        OnPropertyChanged(nameof(IsAddedToVanilla));
        OnPropertyChanged(nameof(ShowOverridesPanel));
    }

    /// <summary>
    /// What this row is marked as: nothing, <c>changed</c>, or <c>new</c>.
    /// <para/>
    /// Only on one of the game's characters, where the two are worth telling
    /// apart: <c>new</c> is a bust the pack drew for them, <c>changed</c> is
    /// one of their own with a texture painted over. On a character the pack
    /// drew, every outfit is the pack's and a tag on all of them says nothing.
    /// <para/>
    /// The same words the level tree and the UI tree use, for the same facts.
    /// </summary>
    public string ChangedTag
    {
        get
        {
            if (_owner?.IsVanillaBust != true) return "";
            if (Model.PackArt) return "new";
            return VanillaCastSeed.CarriesNothing(Model) ? "" : "changed";
        }
    }

    public bool HasChangedTag => ChangedTag.Length > 0;

    private string _taggedAs = "";

    /// <summary>Re-read the tag, and say so only when it actually moved.</summary>
    public void RefreshTag()
    {
        string now = ChangedTag;
        if (now == _taggedAs) return;
        _taggedAs = now;
        OnPropertyChanged(nameof(ChangedTag));
        OnPropertyChanged(nameof(HasChangedTag));
    }

    /// <summary>
    /// Whether the replace-textures panel is open.
    /// <para/>
    /// Null until somebody says, and until then the PACK answers: a bust
    /// carrying a spriteOverrides entry is a bust this pack replaces textures
    /// on, which is exactly what the tick claims. Nothing new is saved for this
    /// - which panels an author left expanded is still none of a manifest's
    /// business - but a tick that starts blank every time the pack is opened
    /// hides the only way in to replacements that are still in the file and
    /// still applied by the runtime. That is what was reported.
    /// <para/>
    /// It LATCHES rather than tracking the count, so unticking the last row
    /// does not slam the panel shut with the author still working in it.
    /// </summary>
    private bool? _overridesShown;
    public bool OverridesShown
    {
        get
        {
            if (_overridesShown == null && Model.SpriteOverrides.Count > 0)
                _overridesShown = true;
            return _overridesShown ?? false;
        }
        set
        {
            if (_overridesShown == value) return;
            _overridesShown = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowOverridesPanel));
        }
    }

    /// <summary>Both halves of the question the panel asks: is there anything
    /// to replace, and did the author open the box.</summary>
    public bool ShowOverridesPanel => IsVanillaBust && OverridesShown;

    /// <summary>
    /// Put one jiggle field back to its default. The parameter is the
    /// <see cref="JiggleParams"/> property name, so each Default button names
    /// the row it sits on.
    /// <para/>
    /// A command rather than a method call per button, which also makes each
    /// reset its own undo step.
    /// </summary>
    public RelayCommand ResetJiggleCommand { get; }

    /// <summary>One field changed, but which one is the command's business, not
    /// this method's — there are eight and they are all cheap to re-read.</summary>
    private void RaiseJiggleChanged()
    {
        OnPropertyChanged(nameof(JiggleSpeed));
        OnPropertyChanged(nameof(JiggleStrength));
        OnPropertyChanged(nameof(JiggleFrequency));
        OnPropertyChanged(nameof(NoiseScale));
        OnPropertyChanged(nameof(NoiseSpeed));
        OnPropertyChanged(nameof(NoiseStrength));
        OnPropertyChanged(nameof(Tint));
        OnPropertyChanged(nameof(PixelSnap));
    }

    public string Key
    {
        get => Model.Key;
        set { Model.Key = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); }
    }

    public string GameObjectName
    {
        get => Model.GameObjectName;
        set
        {
            // Key follows the name unless it was deliberately made to differ.
            // AddOutfit sets both to the same value and only this one is
            // editable, so without this a rename left Key on the original and
            // the tree label ("Key (GameObjectName)") went stale — while every
            // actual reference in the pack uses GameObjectName.
            // A BLANK name still counts as tracking. Clearing the box and
            // retyping is the normal way to rename, and comparing Key against
            // an empty GameObjectName reads as "deliberately different" — which
            // detached the two permanently after the first keystroke.
            bool keyTracked = string.IsNullOrWhiteSpace(Model.GameObjectName)
                              || string.Equals(Model.Key, Model.GameObjectName,
                                               StringComparison.OrdinalIgnoreCase);
            Model.GameObjectName = value;
            if (keyTracked && !string.IsNullOrWhiteSpace(value)) Model.Key = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Key));
            OnPropertyChanged(nameof(Display));
        }
    }

    public string BaseSprite
    {
        get => Model.BaseSprite;
        set { Model.BaseSprite = value; OnPropertyChanged(); }
    }

    public string MaskSprite
    {
        get => Model.MaskSprite;
        set { Model.MaskSprite = value; OnPropertyChanged(); }
    }

    public string BlinkSprite
    {
        get => Model.BlinkSprite;
        set { Model.BlinkSprite = value; OnPropertyChanged(); SeedPrefixesFromBlink(); }
    }

    /// <summary>
    /// Put the blink art's folder into whichever prefix field is still empty.
    /// <para/>
    /// The overlays of one outfit almost always sit together, so by the time
    /// the blink path is filled in the folder for the mouth and expression
    /// frames is already known — and typing it a third time by hand is where
    /// the typos come from. Only the folder is copied: the filename part is the
    /// author's to choose, and guessing it would be worse than leaving it.
    /// <para/>
    /// Empty fields only, so this never overwrites an answer. It also fires only
    /// once the path names a PNG, which keeps it from filling the prefix with
    /// half a folder while someone is still typing the blink path.
    /// <para/>
    /// Runs on edits, not on load: an outfit read from disk keeps exactly the
    /// prefixes it was saved with.
    /// </summary>
    private void SeedPrefixesFromBlink()
    {
        string path = Model.BlinkSprite ?? "";
        if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return;

        string folder = FolderOf(path);
        if (folder.Length == 0) return;

        if (string.IsNullOrWhiteSpace(MouthPrefix)) MouthPrefix = folder;
        if (string.IsNullOrWhiteSpace(ExpressionPrefix)) ExpressionPrefix = folder;
    }

    /// <summary>Everything up to and including the last separator, in the
    /// forward-slash form pack paths are stored in. Empty when the path names a
    /// file at the pack root, which has no folder to copy.</summary>
    private static string FolderOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string norm = path.Replace('\\', '/');
        int slash = norm.LastIndexOf('/');
        return slash < 0 ? "" : norm.Substring(0, slash + 1);
    }

    public bool BlinkEnabled
    {
        get => Model.BlinkEnabled;
        set { Model.BlinkEnabled = value; OnPropertyChanged(); }
    }

    public bool MouthEnabled
    {
        get => Model.Mouth.Enabled;
        set { Model.Mouth.Enabled = value; OnPropertyChanged(); }
    }

    public string MouthPrefix
    {
        get => Model.Mouth.Prefix;
        set { Model.Mouth.Prefix = value; OnPropertyChanged(); }
    }

    public bool ExpressionEnabled
    {
        get => Model.Expression.Enabled;
        set { Model.Expression.Enabled = value; OnPropertyChanged(); }
    }

    public string ExpressionPrefix
    {
        get => Model.Expression.Prefix;
        set { Model.Expression.Prefix = value; OnPropertyChanged(); }
    }

    // Jiggle params — bound directly to sliders in the view.
    public float JiggleSpeed
    {
        get => Model.Jiggle.Speed;
        set { Model.Jiggle.Speed = value; OnPropertyChanged(); }
    }
    public float JiggleStrength
    {
        get => Model.Jiggle.Strength;
        set { Model.Jiggle.Strength = value; OnPropertyChanged(); }
    }
    public float JiggleFrequency
    {
        get => Model.Jiggle.Frequency;
        set { Model.Jiggle.Frequency = value; OnPropertyChanged(); }
    }
    public float NoiseScale
    {
        get => Model.Jiggle.NoiseScale;
        set { Model.Jiggle.NoiseScale = value; OnPropertyChanged(); }
    }
    public float NoiseSpeed
    {
        get => Model.Jiggle.NoiseSpeed;
        set { Model.Jiggle.NoiseSpeed = value; OnPropertyChanged(); }
    }
    public float NoiseStrength
    {
        get => Model.Jiggle.NoiseStrength;
        set { Model.Jiggle.NoiseStrength = value; OnPropertyChanged(); }
    }
    public string Tint
    {
        get => Model.Jiggle.Tint;
        set { Model.Jiggle.Tint = value; OnPropertyChanged(); }
    }
    public bool PixelSnap
    {
        get => Model.Jiggle.PixelSnap;
        set { Model.Jiggle.PixelSnap = value; OnPropertyChanged(); }
    }

    public bool ParticleActive
    {
        get => Model.Particles.Count > 0 && Model.Particles[0].Active;
        set
        {
            if (Model.Particles.Count == 0)
                Model.Particles.Add(new ParticleRef());
            Model.Particles[0].Active = value;
            OnPropertyChanged();
        }
    }

    public string Display => $"{Key} ({GameObjectName})";

    // ── IMaskEditorHost ────────────────────────────────────────────────

    string IMaskEditorHost.Key => Model.Key;
    public string PoseSpritePath => BaseSprite;
    public string MaskPath
    {
        get => MaskSprite;
        set => MaskSprite = value;
    }

    // ── Live mask under edit (shared with MaskEditorWindow)

    private byte[]? _liveMaskBgra;
    /// <summary>
    /// In-progress mask buffer from the mask editor. When non-null, the live
    /// preview reads this instead of the file-loaded mask, so brush strokes
    /// show through the shader without touching disk. The mask editor sets it
    /// while open and clears (or commits to file) on close.
    /// </summary>
    public byte[]? LiveMaskBgra
    {
        get => _liveMaskBgra ?? MaskRow?.LiveMaskBgra;
        set { _liveMaskBgra = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The replace-textures row for the jiggle mask, on a bust the GAME draws.
    /// <para/>
    /// The painter publishes its in-progress buffer into whichever host opened
    /// it, and on a borrowed bust that host is the row rather than the outfit —
    /// the path lives there, so the mask does too. The preview reads one
    /// property for both kinds of bust, so this is where the two meet.
    /// <para/>
    /// Reads the backing field rather than the <see cref="Overrides"/> property
    /// on purpose: building the rows as a side effect of a getter would be a
    /// surprise, and a painter cannot have been opened on a panel that was
    /// never realised.
    /// </summary>
    private SpriteOverrideViewModel? MaskRow
    {
        get
        {
            if (_overrides == null) return null;
            foreach (var row in _overrides)
                if (row.Slot == SMSModForge.Shared.SpriteSlotNames.Mask) return row;
            return null;
        }
    }

    /// <summary>
    /// Bumped every time the mask editor finishes a stamp — gives the preview
    /// a cheap "buffer contents changed" signal even though the array
    /// reference is the same.
    /// </summary>
    private int _liveMaskRevision;
    public int LiveMaskRevision
    {
        get => _liveMaskRevision;
        set { _liveMaskRevision = value; OnPropertyChanged(); }
    }

    // ── Sidebar search (IFilterableTreeNode) ──────────────────────────────
    private bool _isFilteredIn = true;
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    // Collapsed on load. A character's busts are tall editors, and a pack with
    // a few of them opens to a wall of controls you have to scroll past to
    // reach the one you want; starting shut makes the list navigable and costs
    // one click to open. Only the initial state — expansion is remembered
    // normally from then on.
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    // Matches the initial state, so clearing a search doesn't expand busts the
    // user never opened.
    private bool _expandedBeforeFilter;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => Display;
    public System.Collections.Generic.IEnumerable<IFilterableTreeNode> FilterChildren
        => System.Linq.Enumerable.Empty<IFilterableTreeNode>();
}
