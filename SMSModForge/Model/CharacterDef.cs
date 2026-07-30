using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace SMSModForge.Model;

/// <summary>Where a character's bust art comes from.</summary>
public enum BustSource
{
    /// <summary>This pack draws the bust, from <see cref="CharacterDef.Outfits"/>.</summary>
    Pack,

    /// <summary>The character borrows one of the game's own busts. No art is
    /// shipped; <see cref="CharacterDef.VanillaBust"/> names it.</summary>
    Vanilla,

    /// <summary>A speaking part with no bust at all — a name and a voice, like
    /// John Dick. Perfectly valid, and the reason a character cannot simply be
    /// "a bust with extra fields".</summary>
    None,
}

/// <summary>
/// One character: who speaks, and what (if anything) is shown when they do.
/// <para/>
/// This is the merge of what used to be a <c>bust</c> and an <c>actor</c>. The
/// split never carried its weight — a bust always needed an actor to speak
/// through, the two duplicated a display name between them, and an actor's
/// "outfits" list was just the names of the bust's own outfits written out a
/// second time. What the split DID buy was the case a bust alone can't express:
/// a speaker with no art. That survives as <see cref="BustSource.None"/>.
/// <para/>
/// Two names are kept, and only one of them is yours. <see cref="DisplayName"/>
/// is what a player reads. <see cref="Key"/> and <see cref="Name"/> are plumbing
/// — the dialogue reference and the GameObject the runtime creates — and both
/// are derived from the display name for anything new. They stay in the model
/// because they are baked into existing packs and into the live scene, and
/// renaming them would break dialogue that targets a bust by name.
/// </summary>
public sealed class CharacterDef
{
    /// <summary>
    /// Pack-local key, and what a dialogue node's <c>actor</c> field matches.
    /// Derived from <see cref="DisplayName"/> when a character is created;
    /// preserved verbatim for anything that already exists.
    /// </summary>
    [JsonProperty("key", Order = 1)]
    public string Key { get; set; } = "";

    /// <summary>
    /// GameObject name the runtime builds the bust under. Derived like
    /// <see cref="Key"/>, and equally not something to rename casually: dialogue
    /// actions can target a bust by this name.
    /// </summary>
    [JsonProperty("name", Order = 2)]
    public string Name { get; set; } = "";

    /// <summary>What the player sees on a speech line. The one name an author
    /// actually writes.</summary>
    [JsonProperty("displayName", Order = 3)]
    public string DisplayName { get; set; } = "New Character";

    /// <summary>Speaker-name tint as hex RGB, or null for the default.</summary>
    [JsonProperty("nameColor", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
    public string? NameColor { get; set; }

    [JsonProperty("bustSource", Order = 5)]
    [JsonConverter(typeof(StringEnumConverter))]
    public BustSource BustSource { get; set; } = BustSource.Pack;

    /// <summary>The game's bust to borrow, when <see cref="BustSource"/> is
    /// <see cref="BustSource.Vanilla"/>.</summary>
    [JsonProperty("vanillaBust", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public string VanillaBust { get; set; } = "";

    /// <summary>
    /// Further vanilla busts this character can be switched to at runtime.
    /// <para/>
    /// Only meaningful for a vanilla-sourced character — a pack-sourced one can
    /// wear any of its own <see cref="Outfits"/>, so listing them again is the
    /// duplication this merge removes.
    /// </summary>
    [JsonProperty("vanillaOutfits", Order = 7)]
    public List<string> VanillaOutfits { get; set; } = new();

    /// <summary>Outfit shown first. Blank = the first entry.</summary>
    [JsonProperty("defaultOutfit", Order = 8, NullValueHandling = NullValueHandling.Ignore)]
    public string DefaultOutfit { get; set; } = "";

    /// <summary>
    /// Gifts this character likes. Surfaced to the host mod (which owns
    /// gift-giving) through the generic variable bridge at load time.
    /// </summary>
    [JsonProperty("giftLikes", Order = 9)]
    public List<string> GiftLikes { get; set; } = new();

    /// <summary>This pack's own outfits. Empty unless <see cref="BustSource"/>
    /// is <see cref="BustSource.Pack"/>.</summary>
    [JsonProperty("outfits", Order = 10)]
    public List<OutfitDef> Outfits { get; set; } = new();

    /// <summary>Named expressions a dialogue node can select.</summary>
    [JsonProperty("expressions", Order = 11)]
    public List<ActorExpressionDef> Expressions { get; set; } = new();

    /// <summary>Typing-blip voice, or null for the default.</summary>
    [JsonProperty("typewriter", Order = 12, NullValueHandling = NullValueHandling.Ignore)]
    public TypewriterDef? Typewriter { get; set; }

    public bool ShouldSerializeVanillaOutfits() => VanillaOutfits.Count > 0;
    public bool ShouldSerializeGiftLikes() => GiftLikes.Count > 0;
    public bool ShouldSerializeOutfits() => Outfits.Count > 0;
    public bool ShouldSerializeExpressions() => Expressions.Count > 0;

    /// <summary>Every bust name this character can be shown as, whichever source
    /// it draws from — what an outfit picker should offer.</summary>
    [JsonIgnore]
    public IEnumerable<string> WearableBusts => BustSource switch
    {
        BustSource.Pack => Outfits.Select(o => o.Key),
        BustSource.Vanilla => string.IsNullOrWhiteSpace(VanillaBust)
            ? VanillaOutfits
            : new[] { VanillaBust }.Concat(VanillaOutfits).Distinct(),
        _ => System.Array.Empty<string>(),
    };

    /// <summary>
    /// Turn a display name into an identifier: letters and digits only, first
    /// letter capitalised, everything else dropped.
    /// <para/>
    /// Deliberately lossy and deliberately not unique — see
    /// <see cref="UniqueIdentifier"/>. "Solid Snake" becomes "SolidSnake"; a
    /// name that reduces to nothing falls back rather than producing "".
    /// </summary>
    public static string Identifier(string displayName)
    {
        var sb = new StringBuilder();
        bool upper = true;
        foreach (char c in displayName ?? "")
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else upper = true;   // a break capitalises the next letter
        }
        if (sb.Length == 0) return "Character";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');   // not a legal leading char
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="Identifier"/>, with a numeric suffix if <paramref name="taken"/>
    /// already holds it.
    /// <para/>
    /// Deduplication is per-pack and case-insensitive. It cannot span packs —
    /// nothing here knows what else is installed — so the runtime keys every
    /// character by pack id as well, and two packs shipping a "Sarah" stay
    /// distinct even though both derive the same identifier.
    /// </summary>
    public static string UniqueIdentifier(string displayName, IEnumerable<string> taken)
    {
        string base_ = Identifier(displayName);
        var used = new HashSet<string>(taken ?? System.Array.Empty<string>(),
                                       System.StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(base_)) return base_;
        for (int i = 2; ; i++)
        {
            string candidate = base_ + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
