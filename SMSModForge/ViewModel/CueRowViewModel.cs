using System;
using System.Globalization;
using System.Linq;
using SMSModForge.Localization;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.ViewModel;

/// <summary>
/// One sound in the selected line, as the node editor's "Sounds in this
/// line" lists it (1.8.0): how loud, when it starts, whether its words show,
/// whether it waits for the typewriter. Writes the line's
/// <see cref="DialogueNodeDef.Cues"/>, and only what differs from the
/// defaults - a row put back to them leaves no entry.
/// </summary>
public sealed class CueRowViewModel : ObservableObject
{
    private readonly DialogueNodeDef _node;
    private readonly Action _changed;

    public SoundCues.Match Match { get; }

    /// <summary>The sound before it in the line, or null for the first.</summary>
    public SoundCues.Match? Previous { get; }

    public CueRowViewModel(DialogueNodeDef node, SoundCues.Match match, SoundCues.Match? previous, Action changed)
    {
        _node = node;
        Match = match;
        Previous = previous;
        _changed = changed;
        // A value from beyond the sliders' usual reach - an older pack's
        // migration can give one - widens them, so they show it rather than
        // their end.
        _volumeTop = Math.Max(SliderVolume, VolumePercent);
        _delayLowest = Math.Min(-3, Math.Floor(Delay));
        _delayHighest = Math.Max(5, Math.Ceiling(Delay));
    }

    public bool IsFirst => Previous == null;

    /// <summary>"2 · *clink*": its place in the line, and its pattern.</summary>
    public string Title => Loc.F("cues.row", "n", Match.Order, "pattern", Match.Pattern);

    private SoundCueDef? Entry => _node.Cues?.FirstOrDefault(c => c.At == Match.Order
        && string.Equals(c.Pattern, Match.Pattern, StringComparison.OrdinalIgnoreCase));

    private SoundCueDef Ensure()
    {
        var entry = Entry;
        if (entry != null) return entry;
        _node.Cues ??= new();
        entry = new SoundCueDef { At = Match.Order, Pattern = Match.Pattern };
        _node.Cues.Add(entry);
        _node.Cues.Sort((a, b) => a.At.CompareTo(b.At));
        return entry;
    }

    /// <summary>A row back at every default leaves nothing in the file.</summary>
    private void Wrote(SoundCueDef entry)
    {
        if (entry.ToSetting().IsDefault) _node.Cues.Remove(entry);
        _changed();
    }

    // ── Volume ───────────────────────────────────────────────────────────

    public const double MostVolume = 500;

    /// <summary>Where the volume slider ends unless a louder value is set:
    /// the box beside it goes to <see cref="MostVolume"/>.</summary>
    public const double SliderVolume = 200;

    private double _volumeTop;

    /// <summary>The volume slider's end: <see cref="SliderVolume"/>, or the
    /// loudest this row has been set to. It never shrinks under the mouse.</summary>
    public double VolumeMax => _volumeTop;

    /// <summary>Of the sound's own volume, in percent; above 100 is louder
    /// than the sound is set (the author, 1.8.0).</summary>
    public double VolumePercent
    {
        get => Math.Round((Entry?.Volume ?? 1f) * 100);
        set
        {
            double percent = Math.Clamp(Math.Round(value), 0, MostVolume);
            if (Math.Abs(percent - VolumePercent) < 0.5) return;
            var entry = Ensure();
            entry.Volume = Math.Abs(percent - 100) < 0.5 ? null : (float)(percent / 100);
            Wrote(entry);
            if (percent > _volumeTop) { _volumeTop = percent; OnPropertyChanged(nameof(VolumeMax)); }
            OnPropertyChanged();
        }
    }

    // ── When it starts ───────────────────────────────────────────────────

    /// <summary>The earliest a start can be set: before the sound before it
    /// ends - overlapping it - for a sound that follows another; never
    /// before the line, or before the typewriter reaches it.</summary>
    public double DelayMin => IsFirst || WhenTyped ? 0 : _delayLowest;
    public double DelayMax => _delayHighest;

    private readonly double _delayLowest, _delayHighest;

    /// <summary>Seconds; what from, <see cref="DelayMeaning"/> says.</summary>
    public double Delay
    {
        // To the millisecond: the file keeps a float, and 0.057 read back as
        // a double is 0.05700000002980232 - which the box showed, cut off.
        get => Math.Round((double)(Entry?.Delay ?? 0f), 3);
        set
        {
            double seconds = Math.Round(Math.Clamp(value, DelayMin, DelayMax), 3);   // the millisecond, as the box shows it
            if (Math.Abs(seconds - Delay) < 0.0005) return;
            var entry = Ensure();
            entry.Delay = Math.Abs(seconds) < 0.0005 ? null : (float)seconds;
            Wrote(entry);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DelayMeaning));
        }
    }

    /// <summary>
    /// The start in a sentence, so nobody has to guess what zero or a
    /// negative number means (the author: "make sure the user understands
    /// this without space for assumptions"): "Starts 0.3 s before *clink*
    /// ends, over it."
    /// </summary>
    public string DelayMeaning
    {
        get
        {
            // As the box beside it writes it, so the two never disagree: the
            // editor's number boxes all take a point (0.057, not 0,06).
            string seconds = Math.Abs(Delay).ToString("0.###", CultureInfo.InvariantCulture);
            bool none = Math.Abs(Delay) < 0.0005;
            if (WhenTyped) return none ? Loc.T("cues.startsWhenTyped") : Loc.F("cues.startsAfterTyped", "seconds", seconds);
            if (IsFirst) return none ? Loc.T("cues.startsWithLine") : Loc.F("cues.startsAfterLine", "seconds", seconds);
            string previous = Previous!.Pattern;
            if (none) return Loc.F("cues.startsWhenPreviousEnds", "pattern", previous);
            return Delay > 0
                ? Loc.F("cues.startsAfterPrevious", "seconds", seconds, "pattern", previous)
                : Loc.F("cues.startsBeforePreviousEnds", "seconds", seconds, "pattern", previous);
        }
    }

    // ── Words and typewriter ─────────────────────────────────────────────

    /// <summary>The pattern's words show in the line. Off, the player hears
    /// the sound and never reads "*clink*".</summary>
    public bool ShowText
    {
        get => !(Entry?.Hidden ?? false);
        set
        {
            if (value == ShowText) return;
            var entry = Ensure();
            entry.Hidden = !value;
            Wrote(entry);
            OnPropertyChanged();
        }
    }

    /// <summary>Starts when the typewriter reaches the pattern, shown or not,
    /// rather than after the sound before it.</summary>
    public bool WhenTyped
    {
        get => Entry?.Typed ?? false;
        set
        {
            if (value == WhenTyped) return;
            var entry = Ensure();
            entry.Typed = value;
            if (value && entry.Delay < 0) entry.Delay = null;   // the typewriter cannot be got ahead of
            Wrote(entry);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DelayMin));
            OnPropertyChanged(nameof(Delay));
            OnPropertyChanged(nameof(DelayMeaning));
        }
    }
}
