using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;

namespace SMSModForge.ViewModel;

/// <summary>
/// Keeps a unit's runtime name following its display name, until the author
/// says otherwise.
/// <para/>
/// Characters had this first and dialogues followed; every other unit still
/// asked authors to invent a second name for the same thing, which is a
/// question with no interesting answer and one more place for a typo. The rule
/// is the same everywhere it is used:
/// <list type="bullet">
///   <item>A NEWLY ADDED unit derives its key from whatever gets typed as the
///   display name, uniquified against its siblings.</item>
///   <item>Editing the key is a decision and it sticks. From then on the
///   display name no longer touches it.</item>
///   <item>A unit LOADED from disk follows too - when its key is simply its
///   display name made into a name, which is what following produced in the
///   first place (the author, 2026-10-21: a duplicated scene stayed
///   "..._copy" whatever it was renamed to). Decided the first time its
///   display name changes; the rename then follows everywhere the key is
///   used, as any rename does since 1.7.0. A key that is a duplicate's
///   "_copy" counts too: the editor wrote it, nobody chose it. A key somebody
///   typed stays, and so does one players' saves remember the unit by (a
///   Starmaker photo's scene).</item>
///   <item>A loaded unit that does not follow starts again when its display
///   name is rubbed out back into the part its key was made from - see
///   <see cref="ErasedBackInto"/> (the author, 1.8.0).</item>
/// </list>
/// Composed rather than inherited: the view models already have base classes
/// and differ in how they reach their siblings, so what is shared is this
/// little bit of state and the rule that goes with it.
/// </summary>
internal sealed class DerivedKey
{
    private bool _following;
    private bool _undecided;

    /// <summary>Loaded and allowed to follow again; never once a key has been
    /// typed.</summary>
    private bool _watched;

    /// <summary>The display name as last seen.</summary>
    private string _shown = "";
    private Func<bool>? _remembered;
    private Func<IEnumerable<string>>? _siblingKeys;

    /// <summary>Whether the key is still following the display name.</summary>
    public bool IsDerived => _following;

    /// <summary>
    /// Start following. Called for a unit the author has just added, never for
    /// one being loaded.
    /// </summary>
    /// <param name="siblingKeys">
    /// Every key in the same collection, this unit's own included — <see
    /// cref="Next"/> filters that one out. Supplied as a callback rather than a
    /// snapshot because the collection keeps changing under it.
    /// </param>
    public void Follow(Func<IEnumerable<string>> siblingKeys)
    {
        _siblingKeys = siblingKeys;
        _following = true;
    }

    /// <summary>
    /// A unit loaded from disk: it follows from its next rename on if its key
    /// is still its display name made into a name - decided then, against the
    /// display name it has now.
    /// </summary>
    /// <param name="remembered">True while players' saves remember the unit by
    /// its key, so it must keep it whatever it is called.</param>
    public void Watch(Func<IEnumerable<string>> siblingKeys, string displayName, Func<bool>? remembered = null)
    {
        _siblingKeys = siblingKeys;
        _following = false;
        _undecided = true;
        _watched = true;
        _shown = displayName ?? "";
        _remembered = remembered;
    }

    /// <summary>The author has typed a key of their own. Stop, for good.</summary>
    public void Stop()
    {
        _following = false;
        _undecided = false;
        _watched = false;
    }

    /// <summary>
    /// The key this display name should produce, or null when the key is not
    /// following any more.
    /// </summary>
    /// <param name="currentKey">
    /// What this unit's key is right now, excluded from the uniqueness check so
    /// that renaming does not collide with the name it is leaving behind and
    /// end up as Anna2.
    /// </param>
    public string? Next(string displayName, string currentKey)
    {
        string was = _shown;
        _shown = displayName ?? "";
        if (_undecided)
        {
            _undecided = false;
            _following = _remembered?.Invoke() != true
                         && (string.Equals(currentKey, Derive(was, currentKey), StringComparison.Ordinal)
                             || IsCopyLeftover(currentKey));
        }
        if (!_following && _watched && _remembered?.Invoke() != true && ErasedBackInto(was, displayName ?? "", currentKey))
            _following = true;
        if (!_following) return null;
        return Derive(displayName ?? "", currentKey);
    }

    /// <summary>A key ending the way the editor names a duplicate or a paste
    /// ("_copy", "_copy2"...): left over, not chosen.</summary>
    internal static bool IsCopyLeftover(string key)
        => System.Text.RegularExpressions.Regex.IsMatch(key ?? "", @"_copy\d*$");

    /// <summary>
    /// Whether a change of display name rubbed it out back into the part the
    /// key was made from, which puts a key that had stopped following back to
    /// following (the author, 1.8.0: a conversation renamed from Story05 to
    /// Story06 before display names carried keys kept the key
    /// GSDialogueStory05 - "detect that I erased a part of the display name
    /// that corresponded to the runtime name"). The part they share is where
    /// the key and the display name made into a name agree from the start;
    /// the change counts when it begins inside that part - the 6 rubbed out,
    /// or replaced by a 7 in one go. Never where they share less than half
    /// the key: a name of somebody's own that only starts like the display
    /// name ("Remote Area" for SecretBeach shares nothing) is not taken over.
    /// A display name brought back to exactly the key follows too.
    /// </summary>
    internal static bool ErasedBackInto(string oldDisplay, string newDisplay, string key)
    {
        string was = Spelled(oldDisplay), now = Spelled(newDisplay);
        key ??= "";
        if (now.Length == 0 || key.Length == 0 || string.Equals(was, now, StringComparison.Ordinal)) return false;
        if (string.Equals(now, key, StringComparison.Ordinal)) return true;
        int shared = Common(was, key);
        if (shared == 0 || shared * 2 < key.Length) return false;
        return Common(was, now) <= shared;
    }

    /// <summary>A display name made into a name, or "" when it has nothing to
    /// make one from.</summary>
    private static string Spelled(string displayName)
        => (displayName ?? "").Any(char.IsLetterOrDigit) ? CharacterDef.Identifier(displayName!) : "";

    private static int Common(string a, string b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
        return n;
    }

    private string Derive(string displayName, string currentKey)
    {
        var others = (_siblingKeys?.Invoke() ?? Enumerable.Empty<string>())
            .Where(k => !string.Equals(k, currentKey, StringComparison.Ordinal));
        return CharacterDef.UniqueIdentifier(displayName, others);
    }
}
