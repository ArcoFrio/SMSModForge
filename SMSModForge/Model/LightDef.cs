using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A 2D light on a <see cref="GameObjectDef"/> (1.10.0): what the game's URP
/// <c>Light2D</c> is given there. Three shapes, as the engine has them:
/// <list type="bullet">
/// <item><see cref="ShapeRound"/> - a point light: full strength out to
/// <see cref="InnerRadius"/>, gone at <see cref="OuterRadius"/>, and a cone when
/// <see cref="OuterAngle"/> is under 360 (centred on the object's up, turned by
/// its rotation). Every one of the game's own 144 lamps is one.</item>
/// <item><see cref="ShapeFree"/> - a freeform light: the polygon
/// <see cref="Points"/> at full strength, fading to nothing over
/// <see cref="Edge"/> outside it.</item>
/// <item><see cref="ShapePicture"/> - a sprite light: the picture
/// <see cref="Picture"/> is the light, its colours times the light's.</item>
/// </list>
/// Units are the game's world units, points in the object's own space (its
/// scale and the level's apply to them, as to a sprite); a round light's radii
/// are world units whatever the object's scale, as URP draws them.
/// <para/>
/// On an object the pack makes, the light is made with it, lights the place's
/// layer only and casts no shadows. On a bound object (the template level's
/// light in a pack place, the game's own lamps in a vanilla extension) the
/// editor seeds this from the catalog and the manifest carries it only when it
/// differs; the game's light keeps what this does not set (its layers, its
/// shadows).
/// </summary>
public sealed class LightDef
{
    public const string ShapeRound = "round", ShapeFree = "free", ShapePicture = "picture", ShapeAmbient = "ambient";

    /// <summary><see cref="ShapeRound"/>, <see cref="ShapeFree"/> or <see cref="ShapePicture"/>.</summary>
    [JsonProperty("shape", Order = 1)]
    public string Shape { get; set; } = ShapeRound;

    /// <summary>The light's colour, hex <c>#RRGGBB</c>. The engine hands it to the
    /// light program as it is (times <see cref="Strength"/>).</summary>
    [JsonProperty("color", Order = 2)]
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>The light's intensity: what its colour is multiplied by. The
    /// place's own light is 1; the game's lamps run 0.1 (the Beach) to 0.4 (the
    /// PC's room), a few higher.</summary>
    [JsonProperty("strength", Order = 3)]
    public float Strength { get; set; } = 0.5f;

    /// <summary>URP's falloff intensity, 0..1: 0.5 fades evenly from the full
    /// part to nothing; higher keeps the light near its full part, lower spreads
    /// it out. Round and free shapes.</summary>
    [JsonProperty("falloff", Order = 4)]
    public float Falloff { get; set; } = 0.5f;

    /// <summary>The light's glow (URP's light volume) drawn over everything it
    /// reaches, 0..1: 0 none.</summary>
    [JsonProperty("glow", Order = 5)]
    public float Glow { get; set; }

    /// <summary>Round: how far the light reaches, world units.</summary>
    [JsonProperty("outerRadius", Order = 6)]
    public float OuterRadius { get; set; } = 3f;

    /// <summary>Round: how far it stays at full strength, world units (0..outer).</summary>
    [JsonProperty("innerRadius", Order = 7)]
    public float InnerRadius { get; set; }

    /// <summary>Round: the cone's whole angle, degrees; 360 a full circle.</summary>
    [JsonProperty("outerAngle", Order = 8)]
    public float OuterAngle { get; set; } = 360f;

    /// <summary>Round: the part of the cone at full strength, degrees (0..outer).</summary>
    [JsonProperty("innerAngle", Order = 9)]
    public float InnerAngle { get; set; } = 360f;

    /// <summary>Free: the polygon, [x, y] pairs in the object's own space, in order.</summary>
    [JsonProperty("points", Order = 10)]
    public List<float[]> Points { get; set; } = new();

    /// <summary>Free: how wide the soft edge outside the polygon is, world units
    /// before the object's scale (URP's shape light falloff size).</summary>
    [JsonProperty("edge", Order = 11)]
    public float Edge { get; set; } = 0.5f;

    /// <summary>Picture: the pack's picture that is the light (relative path).</summary>
    [JsonProperty("picture", Order = 12)]
    public string Picture { get; set; } = "";

    /// <summary>
    /// Light the characters too (1.10.0): the busts' sorting layer ("Bust") as well
    /// as the place's. Off, as for 141 of the game's 144 lamps: a bust then only
    /// picks the light up at its edges (its edge glow reads the place behind it)
    /// and the light's glow stays behind it. On, the light brightens and tints the
    /// bust where it reaches, and its glow is drawn over the bust. Shown to the
    /// author in the game (2026-11-19) before it was made.
    /// </summary>
    [JsonProperty("lightsCharacters", Order = 13)]
    public bool LightsCharacters { get; set; }

    /// <summary>
    /// Preview only, never written: the sorting layers a bound light of the
    /// game's lights (its catalog <c>m_ApplyToSortingLayers</c>), which the
    /// pack does not change. Null for a light the pack makes, which lights the
    /// place's layer (Default) alone.
    /// </summary>
    [JsonIgnore] public IReadOnlyList<string>? GameLayers { get; set; }

    /// <summary>The same, kept in the editor's own copies of the pack (an undo step, a record copied) and never
    /// in the file: a light read back from one of those is still the game's lamp it was (1.10.0).</summary>
    [JsonProperty("gameLayers", Order = 99)]
    public List<string>? GameLayersKept
    {
        get => GameLayers?.ToList();
        set => GameLayers = value;
    }

    public bool ShouldSerializeGameLayersKept() => GameLayers != null && !GameObjectDef.WritingTheFile;

    /// <summary>Whether it lights the place's own layer (Default), and so what
    /// the place preview draws.</summary>
    [JsonIgnore] public bool LightsThePlace => GameLayers == null || GameLayers.Contains("Default");

    /// <summary>
    /// Whether its glow is drawn over the characters: URP draws a light's glow with
    /// the layers up to the highest one it lights, so a light of the characters'
    /// layer, or of one drawn over them (the game's Foreground and Photos), glows
    /// over a bust; one of the place's alone glows behind it, in the place the
    /// bust's edge glow reads (1.9.2, LampSettings.GlowWithTheRoom).
    /// </summary>
    [JsonIgnore] public bool GlowsOverCharacters
        => LightsCharacters || (GameLayers != null && GameLayers.Any(l => l == "Foreground" || l == "Photos"));

    [JsonIgnore] public bool IsRound => !IsFree && !IsPicture && !IsAmbient;
    [JsonIgnore] public bool IsFree => string.Equals(Shape, ShapeFree, StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool IsPicture => string.Equals(Shape, ShapePicture, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The whole place (1.10.0): not a lamp but the place's base light - what every
    /// picture on its layer starts from instead of the scene's white 1, so below 1
    /// it darkens the place (and its NPCs) and the place's lamps light it back up.
    /// Its colour and strength only, and whether it takes the characters' base
    /// light too. On only while its object is (and its place shown): a dialogue or
    /// a condition switching the object switches day and night.
    /// </summary>
    [JsonIgnore] public bool IsAmbient => string.Equals(Shape, ShapeAmbient, StringComparison.OrdinalIgnoreCase);

    // Only the chosen shape's own settings are written.
    public bool ShouldSerializeOuterRadius() => IsRound;
    public bool ShouldSerializeInnerRadius() => IsRound && InnerRadius != 0f;
    public bool ShouldSerializeOuterAngle() => IsRound && OuterAngle < 360f;
    public bool ShouldSerializeInnerAngle() => IsRound && OuterAngle < 360f;
    public bool ShouldSerializePoints() => IsFree;
    public bool ShouldSerializeEdge() => IsFree;
    public bool ShouldSerializePicture() => IsPicture;
    public bool ShouldSerializeFalloff() => !IsPicture && !IsAmbient;
    public bool ShouldSerializeGlow() => Glow != 0f && !IsAmbient;
    // Written when on - and always for a light of the game's, whose own layers it otherwise keeps: the plugin
    // then knows to take the characters' layer off as well as to put it on.
    public bool ShouldSerializeLightsCharacters() => LightsCharacters || GameLayers != null;

    /// <summary>A square of <paramref name="half"/> units each way: what a light
    /// turned free starts as.</summary>
    public static List<float[]> Square(float half)
        => new() { new[] { -half, -half }, new[] { half, -half }, new[] { half, half }, new[] { -half, half } };

    public LightDef Clone() => new()
    {
        Shape = Shape, Color = Color, Strength = Strength, Falloff = Falloff, Glow = Glow,
        OuterRadius = OuterRadius, InnerRadius = InnerRadius, OuterAngle = OuterAngle, InnerAngle = InnerAngle,
        Points = Points.Select(p => (float[])p.Clone()).ToList(), Edge = Edge, Picture = Picture,
        LightsCharacters = LightsCharacters, GameLayers = GameLayers,
    };

    private const float Epsilon = 1e-4f;
    private static bool Near(float a, float b) => Math.Abs(a - b) <= Epsilon;

    /// <summary>Whether the two would give the game the same light: the settings
    /// of the shape they share, compared as the manifest writes them.</summary>
    public bool SameLightAs(LightDef? o)
    {
        if (o == null) return false;
        if (!string.Equals(Shape, o.Shape, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(NormalColor(Color), NormalColor(o.Color), StringComparison.Ordinal)) return false;
        if (!Near(Strength, o.Strength) || LightsCharacters != o.LightsCharacters) return false;
        if (IsAmbient) return true;
        if (!Near(Glow, o.Glow)) return false;
        if (!IsPicture && !Near(Falloff, o.Falloff)) return false;
        if (IsRound)
            return Near(OuterRadius, o.OuterRadius) && Near(InnerRadius, o.InnerRadius)
                && Near(Math.Min(OuterAngle, 360f), Math.Min(o.OuterAngle, 360f))
                && (OuterAngle >= 360f || Near(InnerAngle, o.InnerAngle));
        if (IsFree)
            return Near(Edge, o.Edge) && Points.Count == o.Points.Count
                && Points.Zip(o.Points).All(p => p.First.Length >= 2 && p.Second.Length >= 2
                                                && Near(p.First[0], p.Second[0]) && Near(p.First[1], p.Second[1]));
        return string.Equals(Picture, o.Picture, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"#RRGGBB" in capitals; white for anything unreadable.</summary>
    public static string NormalColor(string? hex)
    {
        var h = (hex ?? "").Trim().TrimStart('#');
        if (h.Length < 6 || !int.TryParse(h.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return "#FFFFFF";
        return "#" + h.Substring(0, 6).ToUpperInvariant();
    }

    /// <summary>The colour as 0..1 numbers, as the engine reads them.</summary>
    public (float r, float g, float b) Rgb()
    {
        var h = NormalColor(Color);
        float Byte(int at) => Convert.ToInt32(h.Substring(at, 2), 16) / 255f;
        return (Byte(1), Byte(3), Byte(5));
    }

    /// <summary>
    /// The light a <c>Light2D</c> from the catalog is (its serialized values, as
    /// the extractor read them), or null for one this cannot describe (a global
    /// light, an old parametric one). Its alpha folds into the strength, as the
    /// engine folds it into the colour it sends.
    /// </summary>
    public static LightDef? FromCatalog(IReadOnlyDictionary<string, object?> p)
    {
        string type = p.TryGetValue("m_LightType", out var t) ? Convert.ToString(t, CultureInfo.InvariantCulture) ?? "" : "";
        var def = new LightDef
        {
            Shape = type switch { "Point" => ShapeRound, "Freeform" => ShapeFree, "Sprite" => ShapePicture, "Global" => ShapeAmbient, _ => "" },
        };
        if (def.Shape.Length == 0) return null;
        string hex = p.TryGetValue("m_Color", out var c) ? Convert.ToString(c, CultureInfo.InvariantCulture) ?? "" : "";
        float alpha = hex.Length >= 8 && int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            ? a / 255f : 1f;
        def.Color = NormalColor(hex);
        def.Strength = F(p, "m_Intensity", 1f) * alpha;
        def.Falloff = F(p, "m_FalloffIntensity", 0.5f);
        bool volume = !p.TryGetValue("m_LightVolumeEnabled", out var ve) || ve is not bool on || on;
        def.Glow = volume ? F(p, "m_LightVolumeIntensity", 0f) : 0f;
        def.OuterRadius = F(p, "m_PointLightOuterRadius", 1f);
        def.InnerRadius = F(p, "m_PointLightInnerRadius", 0f);
        def.OuterAngle = F(p, "m_PointLightOuterAngle", 360f);
        def.InnerAngle = F(p, "m_PointLightInnerAngle", 360f);
        def.Edge = F(p, "m_ShapeLightFalloffSize", 0.5f);
        def.GameLayers = CatalogLayers(p);
        def.LightsCharacters = def.GameLayers?.Contains("Bust") ?? false;
        return def;
    }

    /// <summary>The sorting layers a catalog light lights (the level catalog's
    /// <c>m_ApplyToSortingLayers</c>), or null when the catalog does not say.</summary>
    public static IReadOnlyList<string>? CatalogLayers(IReadOnlyDictionary<string, object?> p)
    {
        if (!p.TryGetValue("m_ApplyToSortingLayers", out var layers) || layers is string || layers is not System.Collections.IEnumerable list)
            return null;
        return list.Cast<object?>().Select(l => Convert.ToString(l, CultureInfo.InvariantCulture) ?? "").ToList();
    }

    private static float F(IReadOnlyDictionary<string, object?> d, string k, float fallback)
        => d.TryGetValue(k, out var v) && v != null
           && float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
           ? f : fallback;
}
