using System;
using System.Collections.Generic;
using System.Text;

namespace SMSModForge.Model;

/// <summary>
/// Where two undo snapshots of a pack differ, when all of the difference is
/// inside one entry of one of its lists - one place, one extension of a level.
/// <para/>
/// So an undo can put back that one entry instead of rebuilding the whole
/// editor. Moving an object on a place and undoing it rebuilt every tab - the
/// characters, the variables, the NPCs, every place - about a second on a
/// two-megabyte pack, for a change to one object (2026-09-27).
/// <para/>
/// Read off the text rather than parsed, which on that pack would cost more
/// than the rebuild it saves. Snapshots are written indented, two spaces a
/// level (<see cref="PackRepository"/>), so a line that is exactly four spaces
/// and a brace opens or closes an entry of a list at the top of the pack, and
/// a line that is two spaces and a quote names a section. A JSON string never
/// holds a raw line break, so nothing inside a value can look like either.
/// </summary>
public static class SnapshotDiff
{
    /// <summary>The one entry that differs, as it was and as it is to be.</summary>
    public sealed class Entry
    {
        /// <summary>The list it is in, by its name in the manifest ("places").</summary>
        public string Section = "";

        /// <summary>Its place in that list.</summary>
        public int Index;

        /// <summary>The entry's text in each snapshot, without the comma after it.</summary>
        public string From = "";
        public string To = "";
    }

    /// <summary>
    /// The one entry <paramref name="from"/> and <paramref name="to"/> differ
    /// in, or null when they differ anywhere else as well - outside the lists,
    /// in two entries, or in how many entries a list has - or not at all.
    /// </summary>
    public static Entry? OneEntry(string? from, string? to)
    {
        if (from == null || to == null || string.Equals(from, to, StringComparison.Ordinal)) return null;

        var a = Split(from);
        var b = Split(to);
        if (a == null || b == null) return null;
        if (!string.Equals(a.Value.Skeleton, b.Value.Skeleton, StringComparison.Ordinal)) return null;
        if (a.Value.Entries.Count != b.Value.Entries.Count) return null;

        Entry? found = null;
        for (int i = 0; i < a.Value.Entries.Count; i++)
        {
            var x = a.Value.Entries[i];
            var y = b.Value.Entries[i];
            if (x.Section != y.Section || x.Index != y.Index) return null;
            if (string.Equals(x.Text, y.Text, StringComparison.Ordinal)) continue;
            if (found != null) return null;   // a second one
            found = new Entry { Section = x.Section, Index = x.Index, From = x.Text, To = y.Text };
        }
        return found;
    }

    private readonly struct Piece
    {
        public Piece(string section, int index, string text) { Section = section; Index = index; Text = text; }
        public string Section { get; }
        public int Index { get; }
        public string Text { get; }
    }

    /// <summary>The snapshot as everything outside the lists' entries, and the
    /// entries themselves. Null for text not laid out as a snapshot is.</summary>
    private static (string Skeleton, List<Piece> Entries)? Split(string json)
    {
        var skeleton = new StringBuilder(json.Length / 4);
        var entries = new List<Piece>();
        string section = "";
        int index = -1;
        int open = -1;

        int pos = 0;
        while (pos < json.Length)
        {
            int newline = json.IndexOf('\n', pos);
            int next = newline < 0 ? json.Length : newline + 1;
            int end = next;
            while (end > pos && (json[end - 1] == '\n' || json[end - 1] == '\r')) end--;

            if (open < 0)
            {
                if (Is(json, pos, end, "    {"))
                {
                    open = pos;
                    index++;
                }
                else
                {
                    string? name = SectionName(json, pos, end);
                    if (name != null) { section = name; index = -1; }
                    skeleton.Append(json, pos, next - pos);
                }
            }
            else if (Is(json, pos, end, "    }") || Is(json, pos, end, "    },"))
            {
                // The comma belongs to the list, not the entry: the last entry
                // has none, and an entry is read back on its own.
                int close = json.IndexOf('}', pos) + 1;
                entries.Add(new Piece(section, index, json.Substring(open, close - open)));
                skeleton.Append('\u0001').Append(json, close, next - close);
                open = -1;
            }
            pos = next;
        }
        return open < 0 ? (skeleton.ToString(), entries) : null;
    }

    private static bool Is(string json, int start, int end, string line)
        => end - start == line.Length && string.CompareOrdinal(json, start, line, 0, line.Length) == 0;

    /// <summary>The name a line gives a section of the pack - two spaces, then
    /// the quoted name - or null for any other line.</summary>
    private static string? SectionName(string json, int start, int end)
    {
        if (end - start < 4 || json[start] != ' ' || json[start + 1] != ' ' || json[start + 2] != '"') return null;
        int close = json.IndexOf('"', start + 3);
        return close < 0 || close >= end ? null : json.Substring(start + 3, close - start - 3);
    }
}
