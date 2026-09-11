using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.ViewModel;

/// <summary>
/// One row of "replace this texture on the game's bust": a tick, and a path.
/// <para/>
/// The tick is not stored anywhere. It IS whether the outfit carries an entry
/// for this slot, which is what makes the promise the whole feature rests on
/// keepable — a slot nobody ticked is a slot the manifest never mentions, and
/// the runtime leaves it exactly as the game drew it. A separate boolean beside
/// the path would be a second thing to keep in step, and the first save where
/// the two disagreed would either lose an author's art or blank a texture they
/// never touched.
/// <para/>
/// Unticking therefore throws the path away, which is the honest reading of
/// "I don't want this replaced after all".
/// </summary>
public sealed class SpriteOverrideViewModel : ObservableObject, IMaskEditorHost
{
    private readonly OutfitDef _outfit;

    public SpriteOverrideViewModel(OutfitDef outfit, string slot)
    {
        _outfit = outfit;
        Slot = slot;
    }

    /// <summary>Which texture, as the manifest and the runtime spell it.</summary>
    public string Slot { get; }

    /// <summary>What to call it on screen.</summary>
    public string Label => SpriteSlotNames.Label(Slot);

    /// <summary>True for the jiggle mask, which is a data texture on the
    /// material rather than a sprite — so its row offers the mask painter the
    /// way a pack outfit's does.</summary>
    public bool IsMask => Slot == SpriteSlotNames.Mask;

    /// <summary>
    /// Whether this pack replaces this texture at all.
    /// <para/>
    /// Setting it false removes the entry, and with it the path: there is
    /// nowhere else for a path to live, and keeping one against a slot the
    /// author has said they do not want replaced would put it back into the
    /// manifest on the next save.
    /// </summary>
    public bool Replaced
    {
        get => _outfit.OverrideFor(Slot) != null;
        set
        {
            if (Replaced == value) return;
            if (value)
                _outfit.SpriteOverrides.Add(new SpriteOverrideDef { Slot = Slot });
            else
                for (int i = _outfit.SpriteOverrides.Count - 1; i >= 0; i--)
                    if (string.Equals(_outfit.SpriteOverrides[i].Slot, Slot,
                                      System.StringComparison.OrdinalIgnoreCase))
                        _outfit.SpriteOverrides.RemoveAt(i);

            OnPropertyChanged();
            OnPropertyChanged(nameof(Path));
        }
    }

    /// <summary>Pack-relative path to the replacement PNG. Blank while the
    /// author has ticked the box and not yet chosen art, which is a state worth
    /// keeping and worth an issue.</summary>
    public string Path
    {
        get => _outfit.OverrideFor(Slot) ?? "";
        set
        {
            var entry = Entry();
            if (entry == null) return;      // not ticked: the field is disabled
            if (entry.Sprite == (value ?? "")) return;
            entry.Sprite = value ?? "";
            OnPropertyChanged();
        }
    }

    // ── IMaskEditorHost ───────────────────────────────────
    //
    // So the mask row here opens the same painter a pack outfit's does. A
    // jiggle mask is not art anybody draws in a paint program: it is three
    // intensity planes, and the only sane way to author one is the tool the
    // editor already has. Leaving it off this row meant the one texture on a
    // vanilla bust that CANNOT be hand-made was the one texture with no way to
    // make it.

    string IMaskEditorHost.Key => _outfit.Key;

    /// <summary>
    /// What the painter draws over: the pack's own replacement for the base
    /// texture, when it is replacing that too.
    /// <para/>
    /// Empty otherwise, and deliberately — the bust underneath belongs to the
    /// game and its art is not in this pack, so there is nothing honest to
    /// show. The painter handles that (no underlay, and the save dialog opens
    /// at the pack root), which is a good deal better than showing somebody
    /// else's bust under a mask meant for it.
    /// </summary>
    public string PoseSpritePath => _outfit.OverrideFor(SpriteSlotNames.Base) ?? "";

    string IMaskEditorHost.MaskPath
    {
        get => Path;
        set => Path = value;
    }

    private byte[]? _liveMaskBgra;
    /// <summary>In-progress buffer published by the painter. Nothing previews a
    /// vanilla bust, so nothing reads this — it is here because the painter
    /// publishes into it and a host that cannot be written to would throw.</summary>
    public byte[]? LiveMaskBgra
    {
        get => _liveMaskBgra;
        set { _liveMaskBgra = value; OnPropertyChanged(); }
    }

    private int _liveMaskRevision;
    public int LiveMaskRevision
    {
        get => _liveMaskRevision;
        set { _liveMaskRevision = value; OnPropertyChanged(); }
    }

    private SpriteOverrideDef? Entry()
    {
        foreach (var o in _outfit.SpriteOverrides)
            if (string.Equals(o.Slot, Slot, System.StringComparison.OrdinalIgnoreCase))
                return o;
        return null;
    }
}
