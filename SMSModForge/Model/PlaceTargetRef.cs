using System;

namespace SMSModForge.Model;

/// <summary>
/// Pack-stable reference to a place — either a vanilla one or another pack's.
/// The wire format is a single string token so it's easy to author and round-trip:
/// <list type="bullet">
///   <item><c>vanilla:&lt;goName&gt;</c> — e.g. <c>vanilla:14_Beach</c>. The name is the literal child name under <c>5_Levels</c> in the base game; see <see cref="VanillaPlaces"/>.</item>
///   <item><c>pack:&lt;packId&gt;.&lt;placeKey&gt;</c> — e.g. <c>pack:MyMod.SecretCave</c>.</item>
///   <item><c>self:&lt;placeKey&gt;</c> — shorthand for <c>pack:&lt;thisPackId&gt;.&lt;placeKey&gt;</c>, useful while authoring.</item>
/// </list>
/// </summary>
public readonly record struct PlaceTargetRef(PlaceTargetKind Kind, string PackId, string Key)
{
    public static PlaceTargetRef Vanilla(string goName) => new(PlaceTargetKind.Vanilla, "", goName);
    public static PlaceTargetRef Pack(string packId, string key) => new(PlaceTargetKind.Pack, packId, key);
    public static PlaceTargetRef Self(string key) => new(PlaceTargetKind.Self, "", key);

    public override string ToString() => Kind switch
    {
        PlaceTargetKind.Vanilla => $"vanilla:{Key}",
        PlaceTargetKind.Pack => $"pack:{PackId}.{Key}",
        PlaceTargetKind.Self => $"self:{Key}",
        _ => "",
    };

    /// <summary>Parses the wire format. Returns false (and the empty ref) for malformed input.</summary>
    public static bool TryParse(string? token, out PlaceTargetRef result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1) return false;
        var scheme = token[..colon];
        var rest = token[(colon + 1)..];

        switch (scheme)
        {
            case "vanilla":
                result = Vanilla(rest);
                return true;
            case "self":
                result = Self(rest);
                return true;
            case "pack":
                var dot = rest.IndexOf('.');
                if (dot <= 0 || dot == rest.Length - 1) return false;
                result = Pack(rest[..dot], rest[(dot + 1)..]);
                return true;
            default:
                return false;
        }
    }
}

public enum PlaceTargetKind
{
    /// <summary>Reference to a vanilla <c>5_Levels</c> child (e.g. <c>14_Beach</c>).</summary>
    Vanilla,
    /// <summary>Reference to a place defined by another pack (or this one when written out by tooling).</summary>
    Pack,
    /// <summary>Reference to a place in the same pack — sugar that the loader rewrites to <see cref="Pack"/>.</summary>
    Self,
}
