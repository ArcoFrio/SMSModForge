using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// One character: the speaker and the bust in a single editor. Absorbs what
/// <c>ActorViewModel</c> used to hold — see <see cref="CharacterDef"/> for why
/// the two were one thing all along.
/// </summary>
public sealed class CharacterViewModel : ObservableObject, IFilterableTreeNode
{
    public CharacterDef Model { get; }
    public ObservableCollection<OutfitViewModel> Outfits { get; }
    public ObservableCollection<ActorExpressionViewModel> Expressions { get; }

    /// <summary>
    /// The other characters in the pack, for deduplicating derived names.
    /// A callback rather than a snapshot because the list changes underneath.
    /// </summary>
    private readonly Func<IEnumerable<CharacterViewModel>> _siblings;

    /// <summary>
    /// Whether the derived names still track the display name.
    /// <para/>
    /// True only for a character created in this session and not yet
    /// hand-edited. Anything loaded from disk starts false, which is what keeps
    /// migration's promise: renaming an existing character's display name never
    /// moves the GameObject the runtime builds or the key dialogue matches on.
    /// </summary>
    private bool _namesFollowDisplay;

    public CharacterViewModel(CharacterDef model,
                              Func<IEnumerable<CharacterViewModel>>? siblings = null,
                              bool isNew = false)
    {
        Model = model;
        _siblings = siblings ?? System.Array.Empty<CharacterViewModel>;
        _namesFollowDisplay = isNew;

        Outfits = new ObservableCollection<OutfitViewModel>(
            model.Outfits.Select(o => new OutfitViewModel(o, this)));
        ViewSort.Alphabetical(Outfits, nameof(OutfitViewModel.Key));

        Expressions = new ObservableCollection<ActorExpressionViewModel>();
        RebuildExpressions();

        // The modified tag has to follow the character rather than be pushed
        // from each place that can change one. There are a dozen of those —
        // the colour, the voice, an outfit's sprite, a replaced texture, an
        // expression — and a list of them here would be the same fragile thing
        // IsUntouched was rewritten to stop being: correct until somebody adds
        // the thirteenth.
        ResetFieldCommand = new RelayCommand(p => ResetField(p as string ?? ""));

        _modified = IsModified;
        PropertyChanged += WhenAnythingChanged;
        ExpressionsChanged += (_, _) => RefreshModified();
    }

    // ── Has this pack changed anything about them? ───────────────────

    /// <summary>
    /// Whether the pack has changed anything at all about one of the game's
    /// characters — the one thing worth seeing while scanning a list where a
    /// hundred and nineteen of them are somebody else's work.
    /// <para/>
    /// The same question the SAVE asks, rather than a second opinion about it:
    /// an entry that counts as untouched here is exactly the entry that will be
    /// dropped from the manifest. So the tag is a promise the file keeps.
    /// </summary>
    public bool IsModified => Model.IsVanillaCharacter && !VanillaCastSeed.IsUntouched(Model);

    /// <summary>
    /// One of the game's characters this pack has not touched — the ones a
    /// sidebar row should stay out of the way for.
    /// <para/>
    /// The heading already groups them, so the row does not need a label
    /// saying which kind it is. What it needs is for a hundred and nineteen
    /// rows the author never opened to recede far enough that the two they did
    /// are findable, which is what the UI tree does with its own 1434.
    /// </summary>
    public bool IsQuietVanilla => IsVanillaBust && !IsModified;

    /// <summary>
    /// Which fields this pack has changed, named.
    /// <para/>
    /// The tag says a character differs; this says what differs, which is the
    /// question that follows it. Some of the answers are things an author
    /// cannot see anywhere else — a key their dialogue depends on, sitting in
    /// an expander that is greyed out for one of the game's characters — and
    /// those are exactly the ones a tag with no explanation is worst for.
    /// </summary>
    public string ChangedFields
    {
        get
        {
            if (!Model.IsVanillaCharacter) return "";
            var one = VanillaCharacters.Find(Model.VanillaCharacter);
            if (one == null) return "";

            var parts = new List<string>();
            if (!string.Equals(Model.Key, one.Key, StringComparison.OrdinalIgnoreCase))
                parts.Add($"dialogue key ({Model.Key})");
            if (Model.NameColor != null) parts.Add("name colour");
            if (Model.Typewriter != null) parts.Add("voice");
            if (CanResetDefaultOutfit) parts.Add("default outfit");
            if (Model.Expressions.Count > 0) parts.Add("expressions");
            if (Model.GiftLikes.Count > 0) parts.Add("gift likes");

            int busts = Model.Outfits.Count(o => o.PackArt);
            if (busts > 0) parts.Add(busts == 1 ? "1 added bust" : $"{busts} added busts");

            int textures = Model.Outfits.Sum(o => o.SpriteOverrides.Count);
            if (textures > 0)
                parts.Add(textures == 1 ? "1 replaced texture" : $"{textures} replaced textures");

            return string.Join(", ", parts);
        }
    }

    public bool HasChangedFields => ChangedFields.Length > 0;

    private bool _modified;

    // ── Putting a field back ──────────────────────────────────────

    /// <summary>
    /// Put one field back where it started. The parameter names the field, the
    /// way the jiggle rows' Default buttons do.
    /// <para/>
    /// "Where it started" is the character's own answer rather than a constant:
    /// for one of the game's, the voice and colour the game gives THEM; for a
    /// pack's own character, nothing set at all.
    /// </summary>
    public RelayCommand ResetFieldCommand { get; }

    private void ResetField(string field)
    {
        switch (field)
        {
            case nameof(DefaultOutfit):
                // Back to the bust the game has them enter in, which is the one
                // it lists first. The field is not editable on one of the
                // game's characters, so without this a pack carrying an older
                // editor's answer had no way back to it at all.
                {
                    var one = VanillaCharacters.Find(Model.VanillaCharacter);
                    if (one != null && one.Outfits.Count > 0) DefaultOutfit = one.Outfits[0];
                }
                break;

            case nameof(NameColor):
                // Cleared rather than set to the game's hex: storing that hex
                // would have the pack asserting a colour identical to the one
                // the character already had. The box then re-reads and shows
                // that colour, which is what an author expects to see.
                Model.NameColor = null;
                _nameColorText = null;
                OnPropertyChanged(nameof(NameColor));
                OnPropertyChanged(nameof(NameColorBrush));
                OnPropertyChanged(nameof(NameColorValue));
                        break;

            case nameof(TypewriterFrequencyText):
                TypewriterFrequencyText = DefaultFrequency.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                break;

            case nameof(TypewriterPitchMinText):
                TypewriterPitchMinText = DefaultPitchMin.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                break;

            case nameof(TypewriterPitchMaxText):
                TypewriterPitchMaxText = DefaultPitchMax.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                break;

            case Voice:
                // The whole thing, and REMOVED rather than set back to matching
                // numbers. A typewriter object holding the defaults is still a
                // typewriter object: it would be written to the manifest, and
                // the character would go on counting as changed for a voice
                // identical to the one they already had.
                Model.Typewriter = null;
                _frequencyText = _pitchMinText = _pitchMaxText = null;
                OnPropertyChanged(nameof(VoiceTemplate));
                OnPropertyChanged(nameof(TypewriterEnabled));
                OnPropertyChanged(nameof(TypewriterFrequencyText));
                OnPropertyChanged(nameof(TypewriterPitchMinText));
                OnPropertyChanged(nameof(TypewriterPitchMaxText));
                break;
        }
    }

    /// <summary>The parameter that means the voice as a whole, rather than one
    /// of its numbers.</summary>
    public const string Voice = "Voice";

    /// <summary>
    /// Everything about this character back to the game's, in one go.
    /// <para/>
    /// What the per-field buttons cannot reach: the faces the pack added, the
    /// textures it replaced, the busts it drew for this character. Those have
    /// their own remove buttons and tickboxes, but finding all of them across a
    /// wardrobe of sixty-five is not a thing to ask of somebody who has decided
    /// they want the character as the game has them.
    /// <para/>
    /// Destructive in a way none of the others are — it throws away art the
    /// author may have spent an evening on — so it asks first, and says how
    /// much it is about to take.
    /// </summary>
    public bool CanResetEverything => IsModified;

    /// <summary>What a full reset would take away, in the author's terms, or
    /// empty when there is nothing to take.</summary>
    public string ResetEverythingSummary
    {
        get
        {
            var parts = new List<string>();

            int faces = Model.Expressions.Count;
            if (faces > 0) parts.Add(faces == 1 ? "1 expression" : $"{faces} expressions");

            int busts = Model.Outfits.Count(o => o.PackArt);
            if (busts > 0) parts.Add(busts == 1 ? "1 bust of yours" : $"{busts} busts of yours");

            int textures = Model.Outfits.Sum(o => o.SpriteOverrides.Count);
            if (textures > 0)
                parts.Add(textures == 1 ? "1 replaced texture" : $"{textures} replaced textures");

            if (Model.Typewriter != null) parts.Add("the voice");
            if (Model.NameColor != null) parts.Add("the name colour");

            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Do it. Everything the pack said about this character goes, and what is
    /// left is what seeding produces — which is to say nothing at all, since an
    /// untouched character is not written to the manifest.
    /// <para/>
    /// Rebuilt from <see cref="VanillaCastSeed.Make"/> rather than by putting
    /// each field back one at a time, for the reason that check is written that
    /// way too: a list of fields to clear is correct until somebody adds the
    /// twelfth, and here being out of date means leaving a character marked as
    /// changed with nothing on screen to show for it.
    /// </summary>
    public void ResetEverything()
    {
        var one = VanillaCharacters.Find(Model.VanillaCharacter);
        if (one == null) return;

        var fresh = VanillaCastSeed.Make(one);

        // The key is the pack's and stays: dialogue nodes name this character
        // by it, and rewriting it here would silence every line they speak.
        fresh.Key = Model.Key;

        Model.Name = fresh.Name;
        Model.DisplayName = fresh.DisplayName;
        Model.BustSource = fresh.BustSource;
        Model.DefaultOutfit = fresh.DefaultOutfit;
        Model.NameColor = null;
        Model.Typewriter = null;
        Model.GiftLikes = fresh.GiftLikes;
        Model.Expressions = fresh.Expressions;
        Model.Outfits = fresh.Outfits;

        Outfits.Clear();
        foreach (var o in Model.Outfits) Outfits.Add(new OutfitViewModel(o, this));
        ViewSort.Alphabetical(Outfits, nameof(OutfitViewModel.Key));

        _nameColorText = null;
        _frequencyText = _pitchMinText = _pitchMaxText = null;

        RebuildExpressions();
        RefreshEverything();
    }

    /// <summary>Re-read every derived thing on screen after a wholesale
    /// change.</summary>
    private void RefreshEverything()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(BustSource));
        OnPropertyChanged(nameof(DefaultOutfit));
        OnPropertyChanged(nameof(WearableBusts));
        OnPropertyChanged(nameof(NameColor));
        OnPropertyChanged(nameof(NameColorBrush));
        OnPropertyChanged(nameof(NameColorValue));
        OnPropertyChanged(nameof(VoiceTemplate));
        OnPropertyChanged(nameof(TypewriterEnabled));
        OnPropertyChanged(nameof(TypewriterFrequencyText));
        OnPropertyChanged(nameof(TypewriterPitchMinText));
        OnPropertyChanged(nameof(TypewriterPitchMaxText));
        RefreshModified();
        RefreshResets();
        BustSourceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Whether to offer resets at all.
    /// <para/>
    /// Only on one of the game's characters, because only there does "put it
    /// back" name something. On a character the pack drew, every field is the
    /// author's and there is nothing behind it to return to — a Reset button
    /// would just be a clear button wearing the wrong word.
    /// </summary>
    public bool ShowsResets => Model.IsVanillaCharacter;

    /// <summary>
    /// Whether this pack has set a colour of its own — which is exactly when
    /// there is something to put back.
    /// <para/>
    /// The box showing a colour is not enough: on one of the game's characters
    /// it shows theirs until somebody changes it. What counts is whether
    /// anything was STORED, and picking the colour the character already had
    /// stores nothing.
    /// </summary>
    public bool CanResetNameColor => Model.NameColor != null;

    /// <summary>
    /// Whether the default outfit is anything but the one the game has them
    /// enter in.
    /// <para/>
    /// The field itself is greyed on one of the game's characters — which bust
    /// they walk in wearing is the game's, in every scene it wrote — so this
    /// button is the only way an older pack's answer can be put back.
    /// </summary>
    /// <summary>
    /// Whether this character's dialogue key is anything but the one the editor
    /// derives from the game's name for them.
    /// <para/>
    /// The field is greyed for one of the game's, and for good reason — but a
    /// pack written by an older editor can be carrying a key of its own
    /// (<c>mobster</c> where the catalog says <c>mobster1</c>), and that is a
    /// difference nothing could put back. It could not simply be migrated
    /// either: every dialogue node in the pack names this character by it.
    /// <para/>
    /// So it is a reset rather than a migration, and the rename carries the
    /// references with it.
    /// </summary>
    public bool CanResetKey
    {
        get
        {
            if (!Model.IsVanillaCharacter) return false;
            var one = VanillaCharacters.Find(Model.VanillaCharacter);
            return one != null
                && !string.Equals(Model.Key, one.Key, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>The key the editor would derive for this character, for the
    /// reset to aim at.</summary>
    public string TheirOwnKey
        => VanillaCharacters.Find(Model.VanillaCharacter)?.Key ?? Model.Key;

    public bool CanResetDefaultOutfit
    {
        get
        {
            if (!Model.IsVanillaCharacter) return false;
            var one = VanillaCharacters.Find(Model.VanillaCharacter);
            if (one == null || one.Outfits.Count == 0) return false;
            return !string.Equals(Model.DefaultOutfit, one.Outfits[0], StringComparison.Ordinal);
        }
    }

    public bool CanResetFrequency
        => Model.Typewriter != null && Model.Typewriter.Frequency != DefaultFrequency;

    public bool CanResetPitchMin
        => Model.Typewriter != null && Model.Typewriter.PitchMin != DefaultPitchMin;

    public bool CanResetPitchMax
        => Model.Typewriter != null && Model.Typewriter.PitchMax != DefaultPitchMax;

    /// <summary>Whether the pack has said anything at all about this
    /// character's voice — including switching it off, which no per-field
    /// button covers.</summary>
    public bool CanResetVoice => Model.Typewriter != null;

    private void RefreshResets()
    {
        OnPropertyChanged(nameof(CanResetNameColor));
        OnPropertyChanged(nameof(CanResetKey));
        OnPropertyChanged(nameof(CanResetDefaultOutfit));
        OnPropertyChanged(nameof(CanResetEverything));
        OnPropertyChanged(nameof(ResetEverythingSummary));
        OnPropertyChanged(nameof(ChangedFields));
        OnPropertyChanged(nameof(CanResetFrequency));
        OnPropertyChanged(nameof(CanResetPitchMin));
        OnPropertyChanged(nameof(CanResetPitchMax));
        OnPropertyChanged(nameof(CanResetVoice));
    }

    /// <summary>Property changes that are about the VIEW rather than the
    /// character. Answering these would run the comparison a hundred and
    /// nineteen times per keystroke in the sidebar's search box.</summary>
    private static readonly HashSet<string> ViewOnly = new(StringComparer.Ordinal)
    {
        nameof(IsModified), nameof(IsQuietVanilla), nameof(IsFilteredIn), nameof(IsExpanded),
        nameof(CanResetNameColor), nameof(CanResetKey), nameof(CanResetDefaultOutfit),
        nameof(CanResetEverything), nameof(ResetEverythingSummary), nameof(ChangedFields),
        nameof(CanResetFrequency), nameof(CanResetPitchMin),
        nameof(CanResetPitchMax), nameof(CanResetVoice), nameof(HasChangedFields),
        // Derived from a property already handled. Answering these too would
        // run the whole comparison four times for one edit rather than once.
        nameof(NameColorBrush), nameof(NameColorValue),
    };

    /// <summary>
    /// True while the refreshes below are running, so nothing they raise can
    /// send us back in here.
    /// <para/>
    /// <see cref="ViewOnly"/> was carrying that on its own, and it is the wrong
    /// thing to trust with it: forgetting to add one name to a list turned
    /// typing a colour into a stack overflow that took the whole test RUN down
    /// rather than one test. The list stays — it saves the work — but it is an
    /// optimisation now, not the thing standing between an author and a crash.
    /// </summary>
    private bool _refreshing;

    private void WhenAnythingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_refreshing) return;
        if (e.PropertyName == null || ViewOnly.Contains(e.PropertyName)) return;

        _refreshing = true;
        try
        {
            RefreshModified();
            RefreshResets();
        }
        finally { _refreshing = false; }
    }

    /// <summary>
    /// Re-read the tag, and say so only when the answer actually changed.
    /// <para/>
    /// Called by the outfits and expressions too, which change the character
    /// without touching a property on it. Public for that reason.
    /// </summary>
    public void RefreshModified()
    {
        if (!Model.IsVanillaCharacter) return;

        bool now = IsModified;
        if (now == _modified) return;
        _modified = now;
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsQuietVanilla));
    }

    // ── One of the game's characters ──────────────────────────────────────

    /// <summary>Which of the game's characters this is, keyed the way the
    /// shared datasets are.</summary>
    private string VanillaKey => VanillaFaces.KeyOf(Model);

    /// <summary>
    /// What the game itself gave this character to speak with — voice, faces
    /// and name colour — or null for a pack's own character and for the few of
    /// the game's who never speak.
    /// <para/>
    /// See <see cref="SMSModForge.Shared.VanillaSpeech"/>: none of it is
    /// readable from the game's files, so it was read out of a running one.
    /// </summary>
    public SMSModForge.Shared.VanillaSpeech.Speaker? Speaker
        => SMSModForge.Shared.VanillaSpeech.For(VanillaKey);

    /// <summary>
    /// The faces the game can ask this character for.
    /// <para/>
    /// The character's own answer, which is not always the four: two of them
    /// have a single "talk", and many have none at all. Where the game gave
    /// them no speaking part, the bust art answers instead — the four, if any
    /// outfit has them.
    /// <para/>
    /// <c>neutral</c> is left out on purpose. It is not a face; it means no
    /// face, which the editor already expresses as an empty Expression field on
    /// a node.
    /// </summary>
    public IReadOnlyList<string> GameExpressions => VanillaFaces.Of(Model);

    /// <summary>
    /// The faces the PACK added, as opposed to the ones the game already had.
    /// What an outfit needs in order to offer somewhere to put the art for
    /// each.
    /// <para/>
    /// The CHILD name rather than the key, and the difference matters: art is
    /// named after the child the runtime activates, so a row keyed "smirk" and
    /// pointed at a child called "Smirk" wants Smirk.png. Naming the file after
    /// the key would have the editor asking for one filename and the game
    /// loading another, with nothing to say so.
    /// </summary>
    public IEnumerable<string> AddedFaceNames
        => Model.Expressions
            // An empty child name is not a face: that is how a pack spells
            // neutral, which means no expression showing.
            .Where(e => !string.IsNullOrEmpty(e.ExpressionGoName))
            .Select(e => e.ExpressionGoName);

    /// <summary>Raised when this character's expressions are added to or
    /// removed, so each outfit can re-offer a sprite row per face. The two
    /// panels are one thought split across two boxes; this is what keeps them
    /// from disagreeing.</summary>
    public event EventHandler? ExpressionsChanged;

    /// <summary>
    /// The rows the Speech expressions box shows: the game's, then the pack's.
    /// <para/>
    /// The game's are built from the shared dataset and are NOT in
    /// <c>Model.Expressions</c>, which matters more than it sounds: putting
    /// them there would write four expressions into every manifest that so much
    /// as looked at Anna, and untouched has to keep meaning untouched.
    /// </summary>
    private void RebuildExpressions()
    {
        Expressions.Clear();
        foreach (string face in GameExpressions)
        {
            // neutral names no child, here as everywhere else: it means no
            // expression showing, which is the bust's own face.
            bool none = string.Equals(face, VanillaFaces.Neutral, StringComparison.OrdinalIgnoreCase);
            Expressions.Add(new ActorExpressionViewModel(
                new ActorExpressionDef { Key = face, ExpressionGoName = none ? "" : face },
                remove: null, fromTheGame: true));
        }

        foreach (var e in Model.Expressions)
        {
            Expressions.Add(Hooked(new ActorExpressionViewModel(e, RemoveExpression)));
        }

        ExpressionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Identity ──────────────────────────────────────────────────────────

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            Model.DisplayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            if (_namesFollowDisplay) DeriveNames();
        }
    }

    /// <summary>Dialogue reference. Derived until hand-edited; editing it pins
    /// both names, since a half-derived pair is more surprising than neither.</summary>
    public string Key
    {
        get => Model.Key;
        set
        {
            if (Model.Key == value) return;
            Model.Key = value;
            _namesFollowDisplay = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Display));
            OnPropertyChanged(nameof(NamesAreDerived));
        }
    }

    /// <summary>GameObject the runtime builds the bust under.</summary>
    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value) return;
            Model.Name = value;
            _namesFollowDisplay = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NamesAreDerived));
        }
    }

    /// <summary>
    /// The reserved player character, which every pack shares.
    /// <para/>
    /// Everything that decides WHO it is — key, name, colour, bust source — is
    /// fixed, because a pack that changed any of them would stop showing the
    /// player the same person as every other pack does. Only the typing voice
    /// is per-pack.
    /// </summary>
    public bool IsPlayer => Model.IsPlayer;

    /// <summary>
    /// Whether the name, the internal names and the bust source are the
    /// author's to change.
    /// <para/>
    /// They are not for one of the game's characters, and the reason is the
    /// same one that makes the player's fixed: they are not this pack's to
    /// decide. Anna is called Anna in every mod, her key is how the runtime
    /// finds her in the compiled cast, and her busts come from the game
    /// whatever this manifest says. A pack that renamed her would have written
    /// a second Anna rather than edited the one everybody shares.
    /// </summary>
    public bool CanEditIdentity => !Model.IsPlayer && !Model.IsVanillaCharacter;

    /// <summary>
    /// Whether this pack may set the speaker-name colour.
    /// <para/>
    /// It may, now, for one of the game's characters. The reasoning that said
    /// otherwise — that a pack tinting Anna differently from every other
    /// appearance of Anna is a bug rather than a style — turned out to be a
    /// decision for the author rather than for the editor, and the runtime
    /// already replaces the game's entry rather than sitting beside it, so the
    /// result is one colour for Anna and not two.
    /// <para/>
    /// Not for the player, whose whole identity is shared across every pack
    /// that addresses "you".
    /// </summary>
    public bool CanEditNameColor => !Model.IsPlayer;

    /// <summary>
    /// The colour the GAME writes this name in, as #RRGGBB, or null when it
    /// writes it plain.
    /// <para/>
    /// Shown beside the author's so it is clear what a colour here replaces.
    /// 36 of the game's characters have one.
    /// </summary>
    public string? GameNameColor => Speaker?.NameColor;

    public bool HasGameNameColor => !string.IsNullOrEmpty(GameNameColor);

    /// <summary>
    /// Whether the default outfit is the author's to choose.
    /// <para/>
    /// It is not for one of the game's characters: which bust they appear in
    /// first is the game's, and it is the one the game lists first. A pack
    /// changing it would change how that character enters every scene in the
    /// game, including the ones it did not write.
    /// </summary>
    public bool CanEditDefaultOutfit => !Model.IsVanillaCharacter;

    /// <summary>Whether the names are still tracking the display name — shown
    /// in the advanced panel so it is clear why they move on their own.</summary>
    public bool NamesAreDerived => _namesFollowDisplay;

    private void DeriveNames()
    {
        var others = _siblings().Where(c => !ReferenceEquals(c, this)).ToList();
        Model.Key = CharacterDef.UniqueIdentifier(Model.DisplayName, others.Select(c => c.Model.Key));
        Model.Name = CharacterDef.UniqueIdentifier(Model.DisplayName, others.Select(c => c.Model.Name));
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Display));
    }

    public string Display => string.IsNullOrWhiteSpace(DisplayName) ? Key : DisplayName;

    // ── Bust source ───────────────────────────────────────────────────────

    public BustSource BustSource
    {
        get => Model.BustSource;
        set
        {
            if (Model.BustSource == value) return;
            Model.BustSource = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPackBust));
            OnPropertyChanged(nameof(IsVanillaBust));
            OnPropertyChanged(nameof(HasNoBust));
            OnPropertyChanged(nameof(CanEditNameColor));
            OnPropertyChanged(nameof(WearableBusts));
            BustSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the bust source may be changed. Fixed for one of the
    /// game's characters, who is a vanilla bust by definition, and for the
    /// player.</summary>
    public bool CanEditBustSource => CanEditIdentity;

    public bool IsPackBust => Model.BustSource == BustSource.Pack;
    public bool IsVanillaBust => Model.BustSource == BustSource.Vanilla;
    public bool HasNoBust => Model.BustSource == BustSource.None;

    /// <summary>
    /// The game's busts, for the picker. Whole records rather than names so the
    /// dropdown can group by character — the catalog runs to hundreds of
    /// entries, and "Anna_Bust" is only findable if Anna is what you scan for.
    /// </summary>
    public IReadOnlyList<SMSModForge.Model.VanillaBusts.VanillaBust> VanillaBustCatalog
        => SMSModForge.Model.VanillaBusts.All;


    public string DefaultOutfit
    {
        get => Model.DefaultOutfit;
        set
        {
            if (Model.DefaultOutfit == value) return;
            Model.DefaultOutfit = value ?? "";
            OnPropertyChanged();
            BustSourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Bust names a dialogue node can switch this character to.</summary>
    public IEnumerable<string> WearableBusts => Model.WearableBusts;

    /// <summary>Raised when the bust source, default outfit or vanilla bust
    /// changes, so dialogue-side derived properties can refresh without every
    /// consumer walking the character list.</summary>
    public event EventHandler? BustSourceChanged;

    // ── Name colour ───────────────────────────────────────────────────────

    // Raw-text backing, for the same reason the typewriter fields have one: the
    // box shows an INHERITED value when the pack has set nothing, so clearing
    // it to retype would otherwise snap the game's hex straight back in under
    // the caret and leave the author typing onto the end of it.
    private string? _nameColorText;

    /// <summary>
    /// What the box shows: this pack's colour, or — when it has set none — the
    /// one the character already has.
    /// <para/>
    /// Showing the inherited colour rather than an empty box is the whole
    /// point. An author opening Adrian should SEE the blue the game writes him
    /// in, and be able to nudge it, rather than face a blank field and a note
    /// off to one side telling them what it would have been.
    /// <para/>
    /// What is STORED is still only a difference. Setting the box to the colour
    /// the character already had stores nothing at all — see
    /// <see cref="IsTheirOwnColor"/> — so a pack never ends up asserting a
    /// colour identical to the game's, and a character never carries the
    /// changed tag for agreeing with it.
    /// </summary>
    public string NameColor
    {
        get => _nameColorText ??= Model.NameColor ?? GameNameColor ?? "";
        set
        {
            _nameColorText = value ?? "";

            string? stored = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (stored != null && IsTheirOwnColor(stored)) stored = null;

            if (Model.NameColor != stored)
            {
                Model.NameColor = stored;
                OnPropertyChanged(nameof(NameColorBrush));
                OnPropertyChanged(nameof(NameColorValue));
                    }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether this hex is the colour the character already has, however it was
    /// spelled.
    /// <para/>
    /// Compared as COLOURS rather than as strings where both parse, so
    /// <c>#99c5ff</c> picked off the wheel is recognised as the same answer as
    /// the <c>#99C5FF</c> the game has. A string compare would have the pack
    /// storing a colour identical to the game's and calling the character
    /// changed for it.
    /// </summary>
    private bool IsTheirOwnColor(string hex)
    {
        string? theirs = GameNameColor;
        if (string.IsNullOrEmpty(theirs)) return false;
        if (string.Equals(hex, theirs, StringComparison.OrdinalIgnoreCase)) return true;

        return TryParseColor(hex, out var a) && TryParseColor(theirs!, out var b) && a == b;
    }

    /// <summary>The colour actually in force: the pack's, else the
    /// character's own. What the swatch paints, and what a colour picker opens
    /// on — neither should follow a half-typed hex.</summary>
    private string EffectiveNameColor => Model.NameColor ?? GameNameColor ?? "";

    public System.Windows.Media.Brush NameColorBrush
        => TryParseColor(EffectiveNameColor, out var c)
           ? new System.Windows.Media.SolidColorBrush(c)
           : System.Windows.Media.Brushes.White;

    public System.Windows.Media.Color NameColorValue
    {
        get => TryParseColor(EffectiveNameColor, out var c) ? c : System.Windows.Media.Colors.White;
        set => NameColor = value.A == 255
            ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
            : $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}";
    }

    private static bool TryParseColor(string hex, out System.Windows.Media.Color c)
    {
        c = System.Windows.Media.Colors.White;
        if (string.IsNullOrEmpty(hex)) return false;
        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color p)
            { c = p; return true; }
        }
        catch { /* malformed hex — the swatch falls back to white */ }
        return false;
    }

    // ── Typewriter voice ──────────────────────────────────────────────────
    // Getters read the nullable model with fallbacks so merely viewing a
    // character never materialises a TypewriterDef and dirties the pack;
    // setters create on write.

    public string[] VoiceTemplates { get; } = { "Male", "Female", "Custom" };

    /// <summary>
    /// What this character sounds like before the pack says anything.
    /// <para/>
    /// For one of the game's, its OWN voice rather than a generic default:
    /// GC2's typewriter turned out to be per character, not per speech skin, so
    /// there is a real answer — 0.2-0.5 for the bouncer, 1.6-2 for Elfina. An
    /// author who opens Anna and changes nothing should see Anna's numbers, and
    /// one who changes the pitch should be changing something rather than
    /// discovering the field was never hers.
    /// </summary>
    private int DefaultFrequency => Speaker?.Frequency ?? 45;
    private float DefaultPitchMin => Speaker?.PitchMin ?? 1.0f;
    private float DefaultPitchMax => Speaker?.PitchMax ?? 1.5f;

    /// <summary>Whether these numbers are the game's own rather than the
    /// editor's generic ones — worth saying next to them.</summary>
    public bool HasGameVoice => Speaker != null;

    public string GameVoiceSummary
        => Speaker == null
           ? ""
           : $"The game gives {Speaker.Name} {Speaker.Frequency} characters a second at pitch "
             + $"{Speaker.PitchMin:0.##}-{Speaker.PitchMax:0.##}.";

    /// <summary>
    /// The typewriter to write into, created on the first edit.
    /// <para/>
    /// Seeded with what the panel was ALREADY SHOWING rather than with
    /// TypewriterDef's own defaults, and that is not a nicety. The panel shows
    /// one of the game's characters their own voice while nothing is set —
    /// Adrian at 40 and 0.8-1.2 — and a fresh TypewriterDef holds 45 and
    /// 1.0-1.5. So editing his pitch used to bring a whole object into
    /// existence around it, quietly moving his frequency to 45 as well: one
    /// field touched, two fields changed, and nothing on screen to say so.
    /// </summary>
    private TypewriterDef TwEdit => Model.Typewriter ??= new TypewriterDef
    {
        Frequency = DefaultFrequency,
        PitchMin = DefaultPitchMin,
        PitchMax = DefaultPitchMax,
    };

    public string VoiceTemplate
    {
        get => Model.Typewriter?.Template switch { "M" => "Male", "F" => "Female", _ => "Custom" };
        set
        {
            switch (value)
            {
                case "Male":
                    TwEdit.Template = "M"; TwEdit.Frequency = 45; TwEdit.PitchMin = 0.6f; TwEdit.PitchMax = 0.9f;
                    break;
                case "Female":
                    TwEdit.Template = "F"; TwEdit.Frequency = 45; TwEdit.PitchMin = 1.0f; TwEdit.PitchMax = 1.5f;
                    break;
                default:
                    TwEdit.Template = "Custom";
                    break;
            }
            DropVoiceIfItSaysNothing();
            _frequencyText = _pitchMinText = _pitchMaxText = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TypewriterFrequencyText));
            OnPropertyChanged(nameof(TypewriterPitchMinText));
            OnPropertyChanged(nameof(TypewriterPitchMaxText));
        }
    }

    public bool TypewriterEnabled
    {
        get => Model.Typewriter?.Enabled ?? true;
        set
        {
            if ((Model.Typewriter?.Enabled ?? true) == value) return;
            TwEdit.Enabled = value;
            DropVoiceIfItSaysNothing();
            OnPropertyChanged();
        }
    }

    // Raw-text backing so a mid-edit "0." isn't snapped back before the
    // fraction lands.
    private string? _frequencyText;
    public string TypewriterFrequencyText
    {
        get => _frequencyText ??= (Model.Typewriter?.Frequency ?? DefaultFrequency)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _frequencyText = value ?? "";
            if (int.TryParse(_frequencyText.Trim(), System.Globalization.NumberStyles.Integer,
                             System.Globalization.CultureInfo.InvariantCulture, out var n))
            { TwEdit.Frequency = n; MarkCustom(); DropVoiceIfItSaysNothing(); }
            OnPropertyChanged();
        }
    }

    private string? _pitchMinText;
    public string TypewriterPitchMinText
    {
        get => _pitchMinText ??= (Model.Typewriter?.PitchMin ?? DefaultPitchMin)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _pitchMinText = value ?? "";
            if (float.TryParse(_pitchMinText.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var f))
            { TwEdit.PitchMin = f; MarkCustom(); DropVoiceIfItSaysNothing(); }
            OnPropertyChanged();
        }
    }

    private string? _pitchMaxText;
    public string TypewriterPitchMaxText
    {
        get => _pitchMaxText ??= (Model.Typewriter?.PitchMax ?? DefaultPitchMax)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            _pitchMaxText = value ?? "";
            if (float.TryParse(_pitchMaxText.Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out var f))
            { TwEdit.PitchMax = f; MarkCustom(); DropVoiceIfItSaysNothing(); }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Throw the typewriter away when every number in it is the one the
    /// character already had.
    /// <para/>
    /// The object outlives its contents otherwise, and that was visible: put
    /// the pitch back with its own Reset button and the character stayed marked
    /// as changed, on the strength of a record holding nothing but the values
    /// the panel shows anyway. Reset all was the only way out, for a reason
    /// nobody could see — there was nothing left on screen for it to clear.
    /// <para/>
    /// So the last field going back takes the record with it, and Reset all
    /// becomes a shortcut rather than the only door. Same rule the migration
    /// applies to packs already written: if it matches what the panel would
    /// show with nothing stored, store nothing.
    /// <para/>
    /// <c>Template</c> is not compared. It is an editor-only hint the runtime
    /// ignores, and a record kept alive by "Custom" alone would be the same
    /// invisible tag by another route.
    /// </summary>
    private void DropVoiceIfItSaysNothing()
    {
        var voice = Model.Typewriter;
        if (voice == null) return;

        bool speaks = Speaker?.UseTypewriter ?? true;
        if (voice.Enabled != speaks) return;
        if (voice.Frequency != DefaultFrequency) return;
        if (voice.PitchMin != DefaultPitchMin) return;
        if (voice.PitchMax != DefaultPitchMax) return;

        Model.Typewriter = null;
        OnPropertyChanged(nameof(VoiceTemplate));
    }

    private void MarkCustom()
    {
        if (Model.Typewriter != null && Model.Typewriter.Template != "Custom")
        {
            Model.Typewriter.Template = "Custom";
            OnPropertyChanged(nameof(VoiceTemplate));
        }
    }

    // ── Collections ───────────────────────────────────────────────────────

    /// <summary>
    /// Add an outfit. For a vanilla character this is a bust name and nothing
    /// else — pick which from the catalog in the editor; for a pack character it
    /// is a fresh outfit to hang sprites on.
    /// </summary>
    public OutfitViewModel AddOutfit(string bustName = "")
    {
        string name = string.IsNullOrWhiteSpace(bustName)
            ? CharacterDef.UniqueIdentifier(
                (string.IsNullOrWhiteSpace(Name) ? "Outfit" : Name) + " New",
                Outfits.Select(o => o.GameObjectName))
            : bustName;
        // On one of the game's characters, anything ADDED is a bust the pack
        // draws - the wardrobe it came with is bust names with no art. Said
        // outright rather than inferred from "has sprites", since it has none
        // yet and would otherwise be taken for one of the game's and open with
        // every field greyed out.
        var def = new OutfitDef { Key = name, GameObjectName = name, PackArt = IsVanillaBust };
        Model.Outfits.Add(def);
        var vm = new OutfitViewModel(def, this);
        Outfits.Add(vm);
        // A character with outfits but no default has nothing to wear: the
        // runtime falls back to the first outfit anyway (ActorRegistry.Declare),
        // so leaving it blank only hides which one that is. Adopt the first one
        // added rather than making the author notice a field they never set.
        if (string.IsNullOrWhiteSpace(DefaultOutfit)) DefaultOutfit = def.GameObjectName;
        OnPropertyChanged(nameof(WearableBusts));
        return vm;
    }

    public void RemoveOutfit(OutfitViewModel vm)
    {
        Model.Outfits.Remove(vm.Model);
        Outfits.Remove(vm);
        // Don't leave the default pointing at an outfit that no longer exists.
        if (string.Equals(DefaultOutfit, vm.GameObjectName, StringComparison.OrdinalIgnoreCase))
            DefaultOutfit = Outfits.Count > 0 ? Outfits[0].GameObjectName : "";
        OnPropertyChanged(nameof(WearableBusts));
    }

    public ActorExpressionViewModel AddExpression()
    {
        // Not "Happy" on one of the game's characters: they already have it,
        // and a second row of the same name is two answers to one question.
        var taken = new HashSet<string>(
            Expressions.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);
        string key = taken.Contains("Happy") ? UniqueFace(taken) : "Happy";

        var def = new ActorExpressionDef { Key = key, ExpressionGoName = key };
        Model.Expressions.Add(def);
        // Hooked like the rest. A face added in this session is exactly the one
        // an author renames — it arrives called "Expression1" — so a row built
        // here and not wired up is the case that matters most.
        var vm = Hooked(new ActorExpressionViewModel(def, RemoveExpression));
        Expressions.Add(vm);
        ExpressionsChanged?.Invoke(this, EventArgs.Empty);
        return vm;
    }

    /// <summary>
    /// Wire a face row up so editing it reaches everything that depends on it.
    /// <para/>
    /// Typing in a face's name changes the character without touching a
    /// property on it, and RENAMES the row where its art goes: a face is added
    /// with a placeholder name and typed over, which is the only way to make
    /// one. A row whose art slot kept the name it was born with would have an
    /// author filling in a path for "Expression1" and wondering why "Smirk"
    /// showed nothing.
    /// </summary>
    private ActorExpressionViewModel Hooked(ActorExpressionViewModel row)
    {
        row.PropertyChanged += (_, _) => ExpressionsChanged?.Invoke(this, EventArgs.Empty);
        return row;
    }

    private static string UniqueFace(HashSet<string> taken)
    {
        for (int i = 1; ; i++)
        {
            string candidate = "Expression" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    public void RemoveExpression(ActorExpressionViewModel vm)
    {
        Model.Expressions.Remove(vm.Model);
        Expressions.Remove(vm);
        ExpressionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Sidebar search (IFilterableTreeNode) ──────────────────────────────

    private bool _isFilteredIn = true;
    public bool IsFilteredIn
    {
        get => _isFilteredIn;
        set { if (_isFilteredIn == value) return; _isFilteredIn = value; OnPropertyChanged(); }
    }

    // Collapsed on load: a pack opens to a list of characters you can scan,
    // not a wall of bust editors.
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChanged(); }
    }

    private bool _expandedBeforeFilter;
    public void StashExpansion() => _expandedBeforeFilter = IsExpanded;
    public void RestoreExpansion() => IsExpanded = _expandedBeforeFilter;

    public string FilterKey => $"{Name} {DisplayName} {Key}";
    public IEnumerable<IFilterableTreeNode> FilterChildren => Outfits;
}
