using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace SMSModForge.Model;

/// <summary>
/// A sound effect as edited in the SFX tab (the author, 1.7.0): which parts of
/// the recording are kept, in what order, and how it is pitched, sped up and
/// given echo or reverb.
/// <para/>
/// Nothing here is read by the game. The editor makes the edited sound into a
/// file of its own when the pack is saved - the recording itself is never
/// changed - and the sound's <see cref="SfxDef.AudioPath"/> points at that
/// file. This is what it was made from, so the edit can be picked up again.
/// </summary>
public sealed class SfxEditDef
{
    /// <summary>The recording, relative to the pack's folder: what the sound
    /// was before it was edited, and what it is made from.</summary>
    [JsonProperty("source", Order = 1)]
    public string Source { get; set; } = "";

    /// <summary>
    /// The pieces kept of each file. A sound can have variants beside it
    /// (<c>Plap_1.ogg</c> beside <c>Plap.ogg</c>) that play in its place at
    /// random; each is cut on its own, since a moment in one is not the same
    /// moment in another. A file not listed is kept whole.
    /// </summary>
    [JsonProperty("files", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
    public List<SfxFileEditDef>? Files { get; set; }

    /// <summary>Semitones up (or down, negative), keeping the length.</summary>
    [JsonProperty("pitch", Order = 3, DefaultValueHandling = DefaultValueHandling.Ignore)]
    public double Pitch { get; set; }

    /// <summary>How much faster it plays (2 = twice as fast, half as long),
    /// keeping the pitch. 1 = as recorded.</summary>
    [JsonProperty("speed", Order = 4)]
    public double Speed { get; set; } = 1;
    public bool ShouldSerializeSpeed() => Speed != 1;

    /// <summary>A repeat of the sound after a delay, fading each time; null
    /// for none.</summary>
    [JsonProperty("echo", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
    public SfxEchoDef? Echo { get; set; }

    /// <summary>The sound in a room; null for none.</summary>
    [JsonProperty("reverb", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
    public SfxReverbDef? Reverb { get; set; }

    /// <summary>
    /// What the files on disk were last made from - the edit and the
    /// recordings, as a fingerprint - so saving again remakes only what
    /// changed. Empty until the first save.
    /// </summary>
    [JsonProperty("rendered", Order = 7, NullValueHandling = NullValueHandling.Ignore)]
    public string? Rendered { get; set; }

    /// <summary>The pieces kept of <paramref name="file"/>, or null when it
    /// is kept whole.</summary>
    public List<SfxPieceDef>? PiecesOf(string file)
        => Files?.FirstOrDefault(f => string.Equals(f.File, file, System.StringComparison.OrdinalIgnoreCase))?.Pieces;

    /// <summary>Whether anything here changes the sound at all.</summary>
    [JsonIgnore]
    public bool ChangesAnything
        => (Files?.Any(f => f.Pieces.Count > 0) ?? false)
           || Pitch != 0 || Speed != 1
           || (Echo?.Amount ?? 0) > 0
           || (Reverb?.Amount ?? 0) > 0;
}

/// <summary>The pieces kept of one file, in the order they play.</summary>
public sealed class SfxFileEditDef
{
    /// <summary>The file's name beside the recording (<c>Plap_1.ogg</c>).</summary>
    [JsonProperty("file", Order = 1)]
    public string File { get; set; } = "";

    [JsonProperty("pieces", Order = 2)]
    public List<SfxPieceDef> Pieces { get; set; } = new();
}

/// <summary>A stretch of a recording, in seconds from its start.</summary>
public sealed class SfxPieceDef
{
    [JsonProperty("from", Order = 1)]
    public double From { get; set; }

    [JsonProperty("to", Order = 2)]
    public double To { get; set; }

    [JsonIgnore]
    public double Length => To - From;
}

public sealed class SfxEchoDef
{
    /// <summary>Seconds between the sound and its repeat.</summary>
    [JsonProperty("delay", Order = 1)]
    public double Delay { get; set; } = 0.25;

    /// <summary>How loud each repeat is next to the one before, 0 to 0.9.</summary>
    [JsonProperty("amount", Order = 2)]
    public double Amount { get; set; }
}

public sealed class SfxReverbDef
{
    /// <summary>How much of the room is heard, 0 to 1.</summary>
    [JsonProperty("amount", Order = 1)]
    public double Amount { get; set; }

    /// <summary>How big the room is, 0 (a cupboard) to 1 (a hall).</summary>
    [JsonProperty("room", Order = 2)]
    public double Room { get; set; } = 0.5;
}
