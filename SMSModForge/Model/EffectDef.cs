using System.Collections.Generic;
using Newtonsoft.Json;
using FX = SMSModForge.Shared.Effects;

namespace SMSModForge.Model;

/// <summary>
/// One of the pack's effects (the FX tab, 1.10.0): made from a template - one of the game's effects or one
/// ModForge builds (<see cref="Shared.Effects"/>) - with what the author changed on it, and when it plays.
/// <para/>
/// A setting left empty is the template's own: the game's campfire as the game draws it, ModForge's burst as
/// ModForge shapes it. Only what the author set is written.
/// </summary>
public sealed class EffectDef
{
    /// <summary>
    /// Empty unless given (the FX tab's "+ Effect" gives one): what is not written must read back as it was, or a
    /// step put back is not the pack it was - an object's effect in a place, which writes none, came back from an
    /// undo as "effect1" (PlaceEffectTests, 1.10.0).
    /// </summary>
    [JsonProperty(FX.KeyField, Order = 1)]
    public string Key { get; set; } = "";
    /// <summary>An object's effect in a place has none: the object is what is named.</summary>
    public bool ShouldSerializeKey() => !string.IsNullOrEmpty(Key);

    /// <summary>Human-facing label shown in the editor's list.</summary>
    [JsonProperty("displayName", Order = 2)]
    public string DisplayName { get; set; } = "";
    public bool ShouldSerializeDisplayName() => !string.IsNullOrEmpty(DisplayName);

    /// <summary>"forge:burst", "vanilla:campfire" - <see cref="Shared.Effects.Catalog"/>.</summary>
    [JsonProperty(FX.TemplateField, Order = 3)]
    public string Template { get; set; } = FX.ForgePrefix + "burst";

    /// <summary>Changes the game's own effect where it is, rather than adding one like it (templates that
    /// allow it: the social points, the effects of the game's levels).</summary>
    [JsonProperty(FX.ChangesGameField, Order = 4)]
    public bool ChangesGame { get; set; }
    public bool ShouldSerializeChangesGame() => ChangesGame;

    /// <summary>Where on the screen, from the left (0) to the right (1); empty = the template's own place.</summary>
    [JsonProperty(FX.XField, Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public float? X { get; set; }

    /// <summary>Where on the screen, from the bottom (0) to the top (1).</summary>
    [JsonProperty(FX.YField, Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public float? Y { get; set; }

    /// <summary>Where the points fly to (the points templates).</summary>
    [JsonProperty(FX.EndXField, Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public float? EndX { get; set; }

    [JsonProperty(FX.EndYField, Order = 8, NullValueHandling = NullValueHandling.Ignore)]
    public float? EndY { get; set; }

    /// <summary><see cref="Shared.Effects.Behind"/> or <see cref="Shared.Effects.Front"/>; empty = the
    /// template's.</summary>
    [JsonProperty(FX.LayerField, Order = 9, NullValueHandling = NullValueHandling.Ignore)]
    public string? Layer { get; set; }

    [JsonProperty(FX.ScaleField, Order = 10, NullValueHandling = NullValueHandling.Ignore)]
    public float? Scale { get; set; }

    /// <summary>A picture of the pack's, pack-relative.</summary>
    [JsonProperty(FX.TextureField, Order = 11, NullValueHandling = NullValueHandling.Ignore)]
    public string? Texture { get; set; }

    /// <summary>One of the game's pictures by name (<see cref="Shared.Effects.GameTextures"/>), when no picture
    /// of the pack's is given.</summary>
    [JsonProperty(FX.GameTextureField, Order = 12, NullValueHandling = NullValueHandling.Ignore)]
    public string? GameTexture { get; set; }

    /// <summary>#RRGGBB or #RRGGBBAA; the particles' colour is multiplied by it.</summary>
    [JsonProperty(FX.ColorField, Order = 13, NullValueHandling = NullValueHandling.Ignore)]
    public string? Color { get; set; }

    /// <summary>Particles: how many, times the template's. Points: how many (new) or times the game's
    /// (changed).</summary>
    [JsonProperty(FX.AmountField, Order = 14, NullValueHandling = NullValueHandling.Ignore)]
    public float? Amount { get; set; }

    [JsonProperty(FX.SizeField, Order = 15, NullValueHandling = NullValueHandling.Ignore)]
    public float? Size { get; set; }

    [JsonProperty(FX.SpeedField, Order = 16, NullValueHandling = NullValueHandling.Ignore)]
    public float? Speed { get; set; }

    [JsonProperty(FX.LifetimeField, Order = 17, NullValueHandling = NullValueHandling.Ignore)]
    public float? Lifetime { get; set; }

    /// <summary>How strongly particles fall (0 = they float); empty = the template's.</summary>
    [JsonProperty(FX.GravityField, Order = 18, NullValueHandling = NullValueHandling.Ignore)]
    public float? Gravity { get; set; }

    /// <summary>How long it plays, in seconds: a looping effect stops after it; a flash and floating words take
    /// it. Empty = until stopped, or the template's own course.</summary>
    [JsonProperty(FX.SecondsField, Order = 19, NullValueHandling = NullValueHandling.Ignore)]
    public float? Seconds { get; set; }

    /// <summary>The points: how far they spread, as a share of the screen's height.</summary>
    [JsonProperty(FX.SpreadField, Order = 20, NullValueHandling = NullValueHandling.Ignore)]
    public float? Spread { get; set; }

    [JsonProperty(FX.FloatSecondsField, Order = 21, NullValueHandling = NullValueHandling.Ignore)]
    public float? FloatSeconds { get; set; }

    [JsonProperty(FX.TravelSecondsField, Order = 22, NullValueHandling = NullValueHandling.Ignore)]
    public float? TravelSeconds { get; set; }

    /// <summary>The floating words; [PV:name] and {name:key} filled as in a line.</summary>
    [JsonProperty(FX.TextField, Order = 23, NullValueHandling = NullValueHandling.Ignore)]
    public string? Text { get; set; }

    /// <summary>Added on rather than painted over: glowing (ModForge's own particles).</summary>
    [JsonProperty(FX.GlowField, Order = 24)]
    public bool Glow { get; set; }
    public bool ShouldSerializeGlow() => Glow;

    /// <summary><see cref="Shared.Effects.PlayWhens"/>.</summary>
    [JsonProperty(FX.PlayWhenField, Order = 25)]
    public string PlayWhen { get; set; } = FX.OnAction;
    public bool ShouldSerializePlayWhen() => PlayWhen != FX.OnAction;

    /// <summary>A new effect for an object of a place: one of the game's level effects, nothing of its own to name.</summary>
    public static EffectDef ForAPlace(string template = FX.VanillaPrefix + "fog")
        => new() { Key = "", DisplayName = "", Template = template };

    [JsonProperty(FX.ConditionsField, Order = 26)]
    public List<NodeConditionDef> Conditions { get; set; } = new();
    public bool ShouldSerializeConditions() => Conditions != null && Conditions.Count > 0;

    /// <summary>The object something under which turning on plays it (<see cref="Shared.Effects.WhenUnderTurnsOn"/>):
    /// "4_CG_Manager-Photos" for each Starmaker photo.</summary>
    [JsonProperty(FX.UnderField, Order = 27, NullValueHandling = NullValueHandling.Ignore)]
    public string? Under { get; set; }
}
