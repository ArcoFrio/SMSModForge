using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// Per-outfit shader uniforms for <c>Sprites/JiggleSprite</c>. Property names
/// here match the shader property names with the leading underscore stripped
/// (e.g. <c>_JiggleSpeed</c> → <see cref="Speed"/>).
/// <para/>
/// Mask semantics (from the shader source):
/// <list type="bullet">
///   <item>R channel × intensity → bounce (vertical, sin(time))</item>
///   <item>G channel × intensity → wave  (horizontal, sin(uv.y·freq + time))</item>
///   <item>B channel × intensity → noise (2D hash-noise distortion)</item>
///   <item>A channel             → overall per-pixel intensity</item>
/// </list>
/// </summary>
public sealed class JiggleParams
{
    [JsonProperty("speed", Order = 1)]
    public float Speed { get; set; } = 3.0f;

    [JsonProperty("strength", Order = 2)]
    public float Strength { get; set; } = -0.02f;

    [JsonProperty("frequency", Order = 3)]
    public float Frequency { get; set; } = 4.0f;

    [JsonProperty("noiseScale", Order = 4)]
    public float NoiseScale { get; set; } = 5.0f;

    [JsonProperty("noiseSpeed", Order = 5)]
    public float NoiseSpeed { get; set; } = 0.5f;

    [JsonProperty("noiseStrength", Order = 6)]
    public float NoiseStrength { get; set; } = 0.06f;

    /// <summary>RGBA hex tint, e.g. <c>"#FFFFFFFF"</c>. Maps to shader <c>_Color</c>.</summary>
    [JsonProperty("tint", Order = 7)]
    public string Tint { get; set; } = "#FFFFFFFF";

    [JsonProperty("pixelSnap", Order = 8)]
    public bool PixelSnap { get; set; } = false;

    public JiggleParams Clone() => (JiggleParams)MemberwiseClone();

    /// <summary>
    /// A fresh instance — which is to say, every field at its default.
    /// <para/>
    /// The property initializers above stay the single source of those
    /// values: a Default button reads one out of here rather than carrying a
    /// second copy of the number that would quietly drift from this one.
    /// </summary>
    public static JiggleParams Defaults => new();

    /// <summary>
    /// Put one field back to its default, named by property so a button can
    /// pass the field it sits beside. An unrecognised name changes nothing.
    /// </summary>
    public void ResetToDefault(string field)
    {
        var d = Defaults;
        switch (field)
        {
            case nameof(Speed):         Speed         = d.Speed;         break;
            case nameof(Strength):      Strength      = d.Strength;      break;
            case nameof(Frequency):     Frequency     = d.Frequency;     break;
            case nameof(NoiseScale):    NoiseScale    = d.NoiseScale;    break;
            case nameof(NoiseSpeed):    NoiseSpeed    = d.NoiseSpeed;    break;
            case nameof(NoiseStrength): NoiseStrength = d.NoiseStrength; break;
            case nameof(Tint):          Tint          = d.Tint;          break;
            case nameof(PixelSnap):     PixelSnap     = d.PixelSnap;     break;
        }
    }
}
