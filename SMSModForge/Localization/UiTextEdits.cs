using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SMSModForge.Shared;

namespace SMSModForge.Localization;

/// <summary>
/// Corrections to the editor's own words, made on screen (Language ▸ Edit texts
/// on screen): what one is saved as, and how it is laid over the language that
/// shipped.
/// <para/>
/// Saved where every translation of the author's own lives - their languages
/// folder - so a correction is a file like any other: it can be passed on, and
/// "New or update a translation" and "Check a translation" read it. But only
/// what they changed goes in it. A whole copy of the shipped language would
/// hold every text as it was that day, and the next version's better words for
/// all the rest would never be seen. So the file is laid OVER the shipped one
/// (<see cref="Layer"/>): the author's words where they wrote some for the
/// English as it is now, the shipped language everywhere else.
/// <para/>
/// No windows here - the edit window and the marks on screen call this.
/// </summary>
public static class UiTextEdits
{
    /// <summary>
    /// <paramref name="shipped"/> with <paramref name="mine"/> laid over it: a
    /// text of <paramref name="mine"/> is used where it is a translation of the
    /// English as it is now. One left as the English, one whose English has
    /// since changed, and one marked as changed from something else are not -
    /// the shipped language may well have the new words, and is the better
    /// guess until the author looks again.
    /// </summary>
    public static TextFile Layer(TextFile shipped, TextFile mine, TextFile english)
    {
        var layered = new TextFile();
        layered.TopNotes.AddRange(mine.TopNotes.Count > 0 ? mine.TopNotes : shipped.TopNotes);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var theirs in shipped.Entries)
        {
            var own = mine.Find(theirs.Key);
            layered.Add(own != null && Current(own, english) ? own : theirs);
            used.Add(theirs.Key);
        }
        // What the shipped file does not have at all: a language only partly
        // shipped, or a text newer than it.
        foreach (var own in mine.Entries)
            if (!used.Contains(own.Key) && Current(own, english)) layered.Add(own);
        return layered;
    }

    /// <summary>Whether a text of the author's is a translation of the English
    /// as it is now.</summary>
    private static bool Current(TextFile.Entry e, TextFile english)
    {
        if (e.ChangedFrom != null) return false;
        string now = english.Get(e.Key) ?? english.Get(TextFile.BaseOf(e.Key) + ".other");
        if (e.English != null && now != null && e.English != now) return false;
        if (e.Blank) return true;
        if (e.Text.Length == 0) return false;
        // Left as the English and not marked as meant to read the same: nobody
        // translated it.
        return !(e.English != null && e.Text == e.English && !e.Same);
    }

    /// <summary>
    /// The keys a text on screen is made of: the key itself, or - for a text
    /// that says a number - each form the language uses (<c>quests.changed.one</c>,
    /// <c>.few</c>, <c>.other</c>...).
    /// </summary>
    public static IReadOnlyList<string> FormsOf(string key, string code)
    {
        if (Loc.English.Has(key)) return new[] { key };
        if (!Loc.English.Has(key + ".other")) return Array.Empty<string>();
        return PluralRules.FormsFor(code).Select(f => key + "." + f).ToList();
    }

    /// <summary>The English a key (or one of its forms) stands for.</summary>
    public static string EnglishOf(string key)
        => Loc.English.Get(key)
           ?? Loc.English.Get(TextFile.BaseOf(key) + ".other")
           ?? "";

    private static readonly Regex Gap = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant);

    /// <summary>
    /// What is wrong with <paramref name="text"/> as a translation of
    /// <paramref name="english"/>: a gap the English fills that it has lost -
    /// "{pack}" - or one it has that nothing will fill. Empty when nothing is.
    /// A lost gap is a sentence with a hole where the pack's name goes.
    /// </summary>
    public static (IReadOnlyList<string> Lost, IReadOnlyList<string> Unknown) CheckGaps(string english, string text)
    {
        var want = Gap.Matches(english ?? "").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var have = Gap.Matches(text ?? "").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        return (want.Where(g => !have.Contains(g)).Select(g => "{" + g + "}").ToList(),
                have.Where(g => !want.Contains(g)).Select(g => "{" + g + "}").ToList());
    }

    /// <summary>
    /// Save <paramref name="text"/> as the author's words for
    /// <paramref name="key"/> in <paramref name="code"/>, into their file in
    /// <paramref name="folder"/>. A file of their own that is a whole
    /// translation is kept whole; otherwise the file holds only what they
    /// changed.
    /// </summary>
    public static string Save(string code, string key, string text, string folder)
    {
        string path = Path.Combine(folder, code + ".txt");
        var file = (File.Exists(path) ? Loc.Read(path) : null) ?? new TextFile();
        string english = EnglishOf(key);
        text ??= "";

        var entry = file.Find(key);
        if (entry == null)
        {
            entry = new TextFile.Entry { Key = key };
            file.Add(entry);
        }
        entry.Text = text;
        entry.Blank = text.Length == 0;
        entry.English = english;
        entry.ChangedFrom = null;
        entry.Same = text == english;

        Write(path, file, code);
        return path;
    }

    /// <summary>
    /// Take the author's words for <paramref name="key"/> back out, so the
    /// shipped language shows again. A file left with nothing in it goes.
    /// </summary>
    public static void Forget(string code, string key, string folder)
    {
        string path = Path.Combine(folder, code + ".txt");
        if (!File.Exists(path)) return;
        var file = Loc.Read(path);
        if (file == null || !file.Remove(key)) return;
        if (file.Entries.Count == 0)
        {
            File.Delete(path);
            return;
        }
        Write(path, file, code);
    }

    /// <summary>Whether the author has words of their own for <paramref name="key"/>.</summary>
    public static bool IsEdited(string code, string key, string folder)
    {
        string path = Path.Combine(folder, code + ".txt");
        return File.Exists(path) && Loc.Read(path)?.Find(key) is { } e && Current(e, Loc.English);
    }

    /// <summary>A file of the author's that translates most of the English -
    /// one "New or update a translation" wrote - rather than a few corrections.</summary>
    private static bool IsWhole(TextFile file) => file.Entries.Count * 2 >= Loc.English.Entries.Count;

    private static void Write(string path, TextFile file, string code)
    {
        if (IsWhole(file))
        {
            Loc.Write(path, TextFileWriter.Build(Loc.English, file, code, file.TopNotes));
            return;
        }

        // Only the corrections, each under the English it translates, in the
        // English file's order so it reads like the rest.
        var sb = new StringBuilder();
        var notes = file.TopNotes.Count > 0
            ? (IList<string>)file.TopNotes
            : new[] { Loc.F("translation.edits.title", "language", TranslationFiles.NativeName(code) ?? code, "code", code) };
        foreach (string n in notes) sb.Append("# ").Append(n).Append("\r\n");

        var order = Loc.English.Entries.Select((e, i) => (e.Key, i))
                       .ToDictionary(x => x.Key, x => x.i, StringComparer.OrdinalIgnoreCase);
        int Place(TextFile.Entry e)
            => order.TryGetValue(e.Key, out int i) ? i
             : order.TryGetValue(TextFile.BaseOf(e.Key) + ".other", out int j) ? j
             : int.MaxValue;

        foreach (var e in file.Entries.OrderBy(Place))
        {
            sb.Append("\r\n");
            if (e.English != null) sb.Append("# ").Append(TextFile.EnglishNote).Append(' ').Append(TextFile.Escape(e.English)).Append("\r\n");
            if (e.Same) sb.Append("# ").Append(TextFile.SameNote).Append("\r\n");
            sb.Append(e.Key).Append(" = ").Append(e.Blank ? TextFile.EmptyOnPurpose : TextFile.Escape(e.Text)).Append("\r\n");
        }
        Loc.Write(path, sb.ToString());
    }
}
