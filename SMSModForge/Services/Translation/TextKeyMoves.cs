using System;
using System.Collections.Generic;
using System.Linq;
using SMSModForge.Model;
using SMSModForge.Shared;

namespace SMSModForge.Services.Translation;

/// <summary>
/// Keeps a pack's translations filed where its texts are, through renames.
/// <para/>
/// A translation file names each text by a key made from what it belongs to -
/// <c>dialogue.chat.4</c> is line 4 of the dialogue <c>chat</c>, and
/// <c>character.sarah.name</c> is Sarah's name. Renaming the dialogue or the
/// character moves the text to a new key, and the translation stayed under the
/// old one: the line read as untranslated, and its words sat in the file
/// under "not used" (the author, 1.7.0: renames have to follow everywhere).
/// <para/>
/// Each rename says which keys it moved. Rather than rewrite the files on the
/// spot - which an undo could not take back, so an undone rename would strand
/// the words the other way - the moves are remembered as which keys belong
/// together, and a file is brought in line when it is next read or saved: a
/// line under a key the pack no longer makes moves to the one key of its group
/// the pack does make and the file does not have yet. However the renames went
/// - one after another, undone, done again - that is the key the text is
/// filed under now.
/// </summary>
public sealed class TextKeyMoves
{
    /// <summary>Which keys a rename has tied together, each pointing towards
    /// the one that stands for its group. Keys are compared as the files
    /// compare them, ignoring case.</summary>
    private readonly Dictionary<string, string> _parent = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether any rename has moved a key.</summary>
    public bool IsEmpty => _parent.Count == 0;

    /// <summary>Forget every move: the pack they were made in is gone.</summary>
    public void Clear() => _parent.Clear();

    /// <summary>
    /// Every text of the pack and the key it is filed under, by the object and
    /// field holding it - so the same text can be found before and after a
    /// rename that changed its key.
    /// </summary>
    public static Dictionary<(object, string), string> KeysOf(ModPack pack)
    {
        var keys = new Dictionary<(object, string), string>(new Identity());
        foreach (var slot in LanguageSession.Slots(pack, out _))
            keys[(slot.Holder, slot.Field)] = slot.Key;
        return keys;
    }

    /// <summary>Remember the keys a rename moved: each text's key before and
    /// after.</summary>
    public void Record(Dictionary<(object, string), string> before, Dictionary<(object, string), string> after)
    {
        foreach (var (holder, was) in before)
            if (after.TryGetValue(holder, out var now) && !string.Equals(was, now, StringComparison.OrdinalIgnoreCase))
                Link(was, now);
    }

    /// <summary>Tie two keys together.</summary>
    public void Link(string a, string b)
    {
        string ra = Find(a), rb = Find(b);
        if (!string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase)) _parent[ra] = rb;
    }

    private string Find(string key)
    {
        if (!_parent.ContainsKey(key)) { _parent[key] = key; return key; }
        var path = new List<string>();
        string at = key;
        while (!string.Equals(_parent[at], at, StringComparison.OrdinalIgnoreCase))
        {
            path.Add(at);
            at = _parent[at];
        }
        foreach (var p in path) _parent[p] = at;   // shorten the way for next time
        return at;
    }

    /// <summary>
    /// File every line of <paramref name="file"/> under the key its text has
    /// now. A line moves when the pack no longer makes its key and exactly one
    /// key it was renamed to or from is made by the pack and missing from the
    /// file - any other case is left as it is, under "not used" where the
    /// author can see it. Returns whether anything moved.
    /// </summary>
    public bool Apply(TextFile file, ISet<string> made)
    {
        if (file == null || IsEmpty) return false;

        bool moved = false;
        foreach (var entry in file.Entries.ToList())
        {
            if (made.Contains(entry.Key) || !_parent.ContainsKey(entry.Key)) continue;

            string group = Find(entry.Key);
            var to = _parent.Keys
                .Where(k => string.Equals(Find(k), group, StringComparison.OrdinalIgnoreCase)
                            && made.Contains(k) && !file.Has(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (to.Count != 1) continue;

            file.Remove(entry.Key);
            entry.Key = to[0];
            file.Add(entry);
            moved = true;
        }
        return moved;
    }

    /// <summary>Every key the pack's texts are filed under now.</summary>
    public static HashSet<string> Made(ModPack pack)
        => new(LanguageSession.Slots(pack, out _).Select(s => s.Key), StringComparer.OrdinalIgnoreCase);

    private sealed class Identity : IEqualityComparer<(object, string)>
    {
        public bool Equals((object, string) a, (object, string) b)
            => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;

        public int GetHashCode((object, string) x)
            => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(x.Item1), x.Item2);
    }
}
