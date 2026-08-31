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
///   <item>A unit LOADED from disk never re-derives. Its key is what dialogue,
///   actions, rules and other packs already refer to it by, and renaming it
///   underneath them is not something a display-name edit should do.</item>
/// </list>
/// Composed rather than inherited: the view models already have base classes
/// and differ in how they reach their siblings, so what is shared is this
/// little bit of state and the rule that goes with it.
/// </summary>
internal sealed class DerivedKey
{
    private bool _following;
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

    /// <summary>The author has typed a key of their own. Stop, for good.</summary>
    public void Stop() => _following = false;

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
        if (!_following) return null;

        var others = (_siblingKeys?.Invoke() ?? Enumerable.Empty<string>())
            .Where(k => !string.Equals(k, currentKey, StringComparison.Ordinal));
        return CharacterDef.UniqueIdentifier(displayName, others);
    }
}
