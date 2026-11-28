using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// An object deleted from a place, or from a game's level, kept in its recycle bin (the author, after 1.10.0:
/// "deleting the GOs actually sends them there instead just in case the user needs them again later"): the
/// object as it was, everything under it with it, and where it was, so Restore puts it back there.
/// <para/>
/// Saved with the pack, so it is there when the pack is opened again - and so an undo puts it back with the
/// object (a step is the pack). Left out of an exported pack (<see cref="PackExporter"/>): the game has no use
/// for it.
/// </summary>
public sealed class BinnedObjectDef
{
    /// <summary>The object deleted, when it was an object.</summary>
    [JsonProperty("object", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
    public GameObjectDef? Object { get; set; }

    /// <summary>The character deleted, when it was one standing in the place.</summary>
    [JsonProperty("character", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public NpcPlacementDef? Character { get; set; }

    /// <summary>
    /// Where what it was in hung, from the top of the place: each step the place of an object among its
    /// parent's objects (a number) or of a character among the characters standing on it (<c>n</c> and a
    /// number), joined by <c>/</c>. Empty: the place's own top objects.
    /// </summary>
    [JsonProperty("under", Order = 3)]
    public string Under { get; set; } = "";

    /// <summary>The names along <see cref="Under"/>, joined by <c>/</c>: what the bin says it was under, and
    /// how it is found again when the objects have moved since.</summary>
    [JsonProperty("underNames", Order = 4)]
    public string UnderNames { get; set; } = "";

    /// <summary>Where it was among what it hung beside.</summary>
    [JsonProperty("at", Order = 5)]
    public int At { get; set; }

    /// <summary>
    /// One of the game's own objects, switched off where it stands rather than taken out - a pack cannot take the
    /// game's object out of the game. <see cref="Object"/> keeps its name and what switching it back on gives back:
    /// its light, whether it was on.
    /// </summary>
    [JsonProperty("switchedOff", Order = 6)]
    public bool SwitchedOff { get; set; }

    public bool ShouldSerializeSwitchedOff() => SwitchedOff;
    public bool ShouldSerializeUnder() => Under.Length > 0;
    public bool ShouldSerializeUnderNames() => UnderNames.Length > 0;
}
