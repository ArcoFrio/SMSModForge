using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.ViewModel;

/// <summary>
/// Renames that follow, for every kind of record the pack refers to by name.
/// <para/>
/// A record's name box writes through on every keystroke, and cascading per
/// keystroke is nonsense ("A", "An", "Ann" - and on the way through, a name
/// that is somebody else's). So each record is remembered by the name it had
/// when the author came to it - picked it, or clicked into one of its boxes -
/// and followed when they are done with it: leaving the box, pressing Enter,
/// picking something else, switching tabs, saving.
/// <para/>
/// Variables, characters and outfits had this first, each with a snapshot of
/// its own beside its selection. Kept per record rather than per selection,
/// because the one time a snapshot and a selection came apart it was a silent
/// rewrite: nodes switching into one bust repointed at another. A name
/// remembered with the object it belongs to cannot be read against a
/// different one.
/// </summary>
internal sealed class RenameTracker
{
    /// <summary>What a kind of record needs for its renames to follow.</summary>
    public sealed class Rule
    {
        /// <summary>The name the rest of the pack refers to it by.</summary>
        public Func<string> Read = null!;

        /// <summary>Whether it is still in the pack. A record deleted rather
        /// than renamed has nothing to follow.</summary>
        public Func<bool> Alive = () => true;

        /// <summary>Whether another record already has this name. Following a
        /// rename onto somebody else's name would merge the two, which is worse
        /// than leaving the clash where the author can see it.</summary>
        public Func<string, bool> Collides = _ => false;

        /// <summary>Point everything at the new name; the record itself
        /// already carries it. Returns how many references moved.</summary>
        public Func<string, string, int> Follow = null!;

        /// <summary>
        /// Put the name on the record without anybody hearing of it, or null
        /// when the pack's translations do not file anything under it. The
        /// translation files are keyed by names, so to know which of their
        /// lines a rename moves, the pack is looked at as it was and as it is.
        /// </summary>
        public Action<string>? Restate;
    }

    private readonly Func<object, Rule?> _ruleFor;
    private readonly Dictionary<object, (Rule Rule, string Name)> _watched =
        new(ReferenceEqualityComparer.Instance);

    public RenameTracker(Func<object, Rule?> ruleFor) => _ruleFor = ruleFor;

    /// <summary>A rename that was followed: the record, its rule, and the two
    /// names.</summary>
    public delegate void Followed(object record, Rule rule, string from, string to);

    /// <summary>Remember <paramref name="record"/>'s name, unless it already
    /// is: the name it had when first watched is the one a rename starts
    /// from. Anything that is not a record with a rule is ignored.</summary>
    public void Watch(object? record)
    {
        if (record == null || _watched.ContainsKey(record)) return;
        var rule = _ruleFor(record);
        if (rule == null) return;
        _watched[record] = (rule, rule.Read() ?? "");
    }

    /// <summary>The record has been renamed by something that followed the
    /// rename itself: what it is called now is where the next one starts.</summary>
    public void Rebase(object? record)
    {
        if (record == null || !_watched.TryGetValue(record, out var w)) return;
        _watched[record] = (w.Rule, w.Rule.Read() ?? "");
    }

    /// <summary>
    /// Follow every rename typed and not yet followed, through
    /// <paramref name="follow"/>.
    /// </summary>
    public void Commit(Followed follow)
    {
        if (_watched.Count == 0) return;
        foreach (var record in _watched.Keys.ToList())
        {
            // Following one rename can set off a commit of its own - a list
            // rebuilt moves a selection - which may already have dealt with
            // this record, or stopped watching it.
            if (!_watched.TryGetValue(record, out var watched)) continue;
            var (rule, from) = watched;
            if (!rule.Alive()) { _watched.Remove(record); continue; }

            string to = rule.Read() ?? "";
            if (to == from || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(from)) continue;

            // Left pending, from the name it had: once the clash is resolved by
            // typing a third name, it is the references to the FIRST name that
            // follow. Taking the clashing name as the new start would hand the
            // other record's references to this one.
            if (rule.Collides(to)) continue;

            // Remembered as the new name first, so whatever following it sets
            // off cannot find it pending a second time.
            _watched[record] = (rule, to);
            follow(record, rule, from, to);
        }
    }

    /// <summary>Stop watching everything but <paramref name="keep"/> - what is
    /// selected, which can still be renamed by something other than typing,
    /// like the button that puts a borrowed character's key back.</summary>
    public void Prune(IEnumerable<object?> keep)
    {
        var kept = new HashSet<object>(keep.Where(k => k != null)!, ReferenceEqualityComparer.Instance);
        foreach (var record in _watched.Keys.ToList())
            if (!kept.Contains(record)) _watched.Remove(record);
    }

    /// <summary>Forget everything: the pack it was watching is gone.</summary>
    public void Clear() => _watched.Clear();

    /// <summary>Whether <paramref name="record"/> is watched. For tests.</summary>
    internal bool IsWatching(object record) => _watched.ContainsKey(record);
}
