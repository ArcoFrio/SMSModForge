using System;

namespace SMSModForge.Model;

/// <summary>
/// How loud the game plays a pack's sounds, as a gain on their samples - so
/// the editor's Play buttons sound as the game will (the author, 1.7.0).
/// <para/>
/// Read from 1.8E's own files, not guessed:
/// <list type="bullet">
/// <item>Everything the game plays goes through one mixer group, CoreAudio's
/// Master, whose one snapshot holds it at <see cref="MixerDb"/>. The music
/// under 12_AudioPlayer routes there, and so does every channel of Game
/// Creator 2 - its GeneralSettings names that group for all five.</item>
/// <item>A pack's sound effect plays through Game Creator 2's UI channel, which
/// SQUARES the volume before Unity gets it (AudioBuffer.Rescale): a sound at 0.5
/// goes out at 0.25. Its channel volumes default to 1 and nothing in the game
/// changes them.</item>
/// <item>A pack's music track is a copy of 12_AudioPlayer/Beach, and a track
/// with no volume of its own keeps Beach's: <see cref="MusicTemplateVolume"/>.
/// The game's own tracks sit between 0.06 and 1.</item>
/// </list>
/// The player's own system volume is the one thing left out: it turns the game
/// and the editor up and down alike.
/// </summary>
public static class GameLoudness
{
    /// <summary>CoreAudio's Master group, in decibels.</summary>
    public const double MixerDb = -10.414379119873047;

    /// <summary>The volume of 12_AudioPlayer/Beach, which pack music is cloned
    /// from.</summary>
    public const float MusicTemplateVolume = 0.5f;

    /// <summary>The mixer's level as a gain: about 0.30.</summary>
    public static float Mixer => (float)Math.Pow(10, MixerDb / 20);

    /// <summary>
    /// The top of a volume box, for a sound effect and a music track alike:
    /// five times the game's own level for it (the author, 1.7.0). 1.0 is
    /// that level, and what a sound plays at when the pack leaves it alone.
    /// The plugin makes the part past what the game's own volume goes to.
    /// </summary>
    public const float MaxVolume = 5f;

    /// <summary>A pack sound effect at <paramref name="volume"/> - its default
    /// volume, or 1.0 when it has none. Squared all the way up, as the game
    /// squares it: 2 is to 1 what 1 is to 0.5.</summary>
    public static float Sfx(float? volume)
    {
        float v = Math.Clamp(volume ?? 1f, 0f, MaxVolume);
        return v * v * Mixer;
    }

    /// <summary>A pack music track at <paramref name="volume"/>, the volume
    /// the game's AudioSource is given, or at the template's when it has none.</summary>
    public static float Music(float? volume)
        => Math.Clamp(volume ?? MusicTemplateVolume, 0f, MaxVolume * MusicTemplateVolume) * Mixer;

    // ── The volume boxes ─────────────────────────────────────────────────
    //
    // Both read 1.0 for the game's own level. A sound effect's box holds the
    // value as the pack stores it - what Game Creator is given, which is 1.0
    // for the game's level already. A music track's stores the AudioSource's
    // volume, which is 0.5 for the game's level, so its box shows that as 1.0
    // and stores half of what it shows. A box at 1.0 stores nothing at all:
    // the game's own level is not a choice the pack has made.

    /// <summary>A volume as its box shows it: "1.0", "0.5", "2.75".</summary>
    public static string VolumeText(double shown)
        => shown.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What a volume box holds, as a number - 1.0 when it is empty -
    /// or false while it holds something that is not one yet.</summary>
    public static bool TryReadVolume(string? text, out float shown)
    {
        string s = (text ?? "").Trim();
        if (s.Length == 0) { shown = 1f; return true; }
        return float.TryParse(s, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out shown);
    }

    /// <summary>A sound effect's volume as stored: null for the game's level.</summary>
    public static float? SfxStored(double shown)
    {
        float v = (float)Math.Clamp(shown, 0, MaxVolume);
        return Math.Abs(v - 1f) < 0.0005f ? null : v;
    }

    /// <summary>A music track's stored volume as its box shows it.</summary>
    public static double MusicShown(float? stored) => (stored ?? MusicTemplateVolume) / MusicTemplateVolume;

    /// <summary>A music track's volume as stored: null for the game's level.</summary>
    public static float? MusicStored(double shown)
    {
        float v = (float)Math.Clamp(shown, 0, MaxVolume);
        return Math.Abs(v - 1f) < 0.0005f ? null : v * MusicTemplateVolume;
    }
}
