using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SMSModForge.Localization;

namespace SMSModForge.Model;

/// <summary>What happened to one thing between two versions of a manifest.</summary>
public enum PackChangeKind
{
    Added,
    Removed,
    Changed,
    /// <summary>Same entries, different order. Reported once for the whole list
    /// rather than as a change per moved element.</summary>
    Reordered,
}

/// <summary>One line in the save-confirmation list.</summary>
public sealed class PackChange
{
    public PackChangeKind Kind { get; init; }

    /// <summary>Top-level manifest section (<c>dialogues</c>, <c>places</c>, …),
    /// or a translation's own heading.</summary>
    public string Section { get; init; } = "";

    /// <summary>Where it is in the file, e.g.
    /// <c>GSDialogueStory05 › nodes › "Hi" › text</c>, or a translation's key.
    /// Excludes <see cref="Section"/>. For finding it, not for reading: the
    /// prompt shows <see cref="Group"/> and <see cref="Text"/>.</summary>
    public string Path { get; init; } = "";

    /// <summary>The value on each side, in full.</summary>
    public string Before { get; init; } = "";
    public string After { get; init; } = "";

    /// <summary>What it belongs to, as the save prompt heads it:
    /// "Dialogue · Chat at the cave", "Translation: Deutsch".</summary>
    public string Group { get; init; } = "";

    /// <summary>The change in words: "“Hi there” › Expression",
    /// "New: “One more thing”", "Default volume".</summary>
    public string Text { get; init; } = "";

    /// <summary>The values worth showing under it, or empty. An id, a list
    /// of ids or a whole record are not.</summary>
    public string Was { get; init; } = "";
    public string Now { get; init; } = "";

    /// <summary>The line under <see cref="Text"/>: "neutral  →  Sad", or
    /// one side alone.</summary>
    public string Detail => Was.Length > 0 && Now.Length > 0 ? Was + "  →  " + Now : Was.Length > 0 ? Was : Now;

    public bool HasDetail => Detail.Length > 0;

    /// <summary>Everything about it, for the row's tooltip.</summary>
    public string Tip
    {
        get
        {
            string text = Text.Length > 0 ? Text : Path;
            if (Before.Length == 0 && After.Length == 0) return text;
            return string.Format(Loc.T("saveConfirm.change.tip"), text,
                                 Before.Length > 0 ? Before : Loc.T("diff.notSet"),
                                 After.Length > 0 ? After : Loc.T("diff.notSet"));
        }
    }

    /// <summary>The change's sign as text, for a copy of the list; the
    /// prompt draws its own in the icon font.</summary>
    public string Glyph => Kind switch
    {
        PackChangeKind.Added => "+",
        PackChangeKind.Removed => "−",
        PackChangeKind.Reordered => "⇅",
        _ => "•",
    };
}

/// <summary>One step down into a manifest: a property, or an element of the
/// list that property holds, with the element as it is on each side.</summary>
internal sealed class DiffStep
{
    public string Name = "";
    public bool IsElement;
    /// <summary>For an element: the property its list is in.</summary>
    public string List = "";
    /// <summary>For an element: its position in the list, from 1.</summary>
    public int Position;
    public JToken? Before, After;

    public JToken? Either => After ?? Before;
}

/// <summary>
/// Structural diff between two serialized manifests. Used by the save
/// confirmation to answer "what am I about to write?" without the author
/// having to keep a mental log of an editing session.
/// <para/>
/// It works on the JSON rather than the view-models on purpose: the JSON is
/// literally what lands on disk (both sides come from
/// <see cref="PackRepository.SerializeAsSaved"/>), so anything the diff reports
/// is a real difference in the file, and anything it stays quiet about really
/// is byte-identical. A VM-level diff would have to be extended by hand for
/// every new field. What each difference is called on screen is
/// <see cref="ChangeWords"/>'s business.
/// </summary>
public static class PackDiff
{
    /// <summary>Upper bound on reported rows. A diff this large is a
    /// bulk operation the list can't usefully itemise anyway, and the window
    /// stays responsive.</summary>
    private const int MaxChanges = 2000;

    /// <summary>Longest value text kept for a Before/After cell.</summary>
    private const int MaxValueLength = 160;

    /// <summary>
    /// Property names probed, in order, to identify an array element by
    /// something stable instead of by position — so inserting a dialogue at
    /// the top of the list doesn't report every dialogue below it as changed.
    /// </summary>
    private static readonly string[] IdentityProps =
        { "key", "name", "packId", "source", "goName", "id" };

    /// <summary>
    /// Compare two manifests. Either side may be null / empty / unparseable
    /// (a pack that has never been saved), in which case the result is a
    /// single informational row rather than a listing of the entire pack.
    /// </summary>
    public static List<PackChange> Compute(string? beforeJson, string? afterJson)
    {
        var changes = new List<PackChange>();

        JToken? before = TryParse(beforeJson);
        JToken? after = TryParse(afterJson);

        if (after == null) return changes;   // nothing to write — caller skips the prompt
        if (before == null)
        {
            changes.Add(new PackChange
            {
                Kind = PackChangeKind.Added,
                Section = "pack",
                Path = Loc.T("diff.wholeManifest"),
                After = Loc.T("diff.nothingToCompare"),
                Group = Loc.T("diff.kind.pack"),
                Text = Loc.T("diff.wholeManifest"),
                Now = Loc.T("diff.nothingToCompare"),
            });
            return changes;
        }

        Diff(before, after, new List<DiffStep>(), changes);
        return changes;
    }

    private static JToken? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JToken.Parse(json); }
        catch { return null; }
    }

    // ── Walk ─────────────────────────────────────────────────────────────

    private static void Diff(JToken before, JToken after, List<DiffStep> path, List<PackChange> outp)
    {
        if (outp.Count >= MaxChanges) return;
        if (JToken.DeepEquals(before, after)) return;

        // A type flip (object → array, string → number) has no meaningful
        // sub-structure to descend into; report it whole.
        if (before.Type != after.Type)
        {
            Emit(outp, PackChangeKind.Changed, path, before, after);
            return;
        }

        switch (after.Type)
        {
            case JTokenType.Object:
                DiffObject((JObject)before, (JObject)after, path, outp);
                break;
            case JTokenType.Array:
                DiffArray((JArray)before, (JArray)after, path, outp);
                break;
            default:
                Emit(outp, PackChangeKind.Changed, path, before, after);
                break;
        }
    }

    private static void DiffObject(JObject before, JObject after, List<DiffStep> path, List<PackChange> outp)
    {
        // "after" order first so the listing reads in manifest order, then any
        // property that only the old version had.
        var names = after.Properties().Select(p => p.Name)
                         .Concat(before.Properties().Select(p => p.Name).Where(n => after[n] == null))
                         .ToList();

        foreach (var name in names)
        {
            if (outp.Count >= MaxChanges) return;
            var b = before[name];
            var a = after[name];
            path.Add(new DiffStep { Name = name });
            try
            {
                if (a == null) Emit(outp, PackChangeKind.Removed, path, b, null);
                else if (b == null) Emit(outp, PackChangeKind.Added, path, null, a);
                else Diff(b, a, path, outp);
            }
            finally { path.RemoveAt(path.Count - 1); }
        }
    }

    private static void DiffArray(JArray before, JArray after, List<DiffStep> path, List<PackChange> outp)
    {
        // A list of plain values (children ids, root ids, outfit names) reads far
        // better as one change than as a change per index — and an
        // index-by-index walk of a list that had an item inserted at the front
        // reports every element as changed.
        if (IsScalarList(before) && IsScalarList(after))
        {
            Emit(outp, PackChangeKind.Changed, path, before, after);
            return;
        }

        string list = path.Count > 0 ? path[^1].Name : "";
        var beforeIds = Identities(before);
        var afterIds = Identities(after);

        if (beforeIds != null && afterIds != null)
        {
            DiffByIdentity(before, beforeIds, after, afterIds, list, path, outp);
            return;
        }

        // No usable identity (conditions, actions — positional by nature).
        int n = Math.Max(before.Count, after.Count);
        for (int i = 0; i < n; i++)
        {
            if (outp.Count >= MaxChanges) return;
            var b = i < before.Count ? before[i] : null;
            var a = i < after.Count ? after[i] : null;
            path.Add(new DiffStep { Name = "#" + (i + 1), IsElement = true, List = list, Position = i + 1, Before = b, After = a });
            try
            {
                if (a == null) Emit(outp, PackChangeKind.Removed, path, b, null);
                else if (b == null) Emit(outp, PackChangeKind.Added, path, null, a);
                else Diff(b, a, path, outp);
            }
            finally { path.RemoveAt(path.Count - 1); }
        }
    }

    private static void DiffByIdentity(JArray before, List<string> beforeIds,
                                       JArray after, List<string> afterIds, string list,
                                       List<DiffStep> path, List<PackChange> outp)
    {
        var beforeById = new Dictionary<string, JToken>(StringComparer.Ordinal);
        for (int i = 0; i < before.Count; i++) beforeById[beforeIds[i]] = before[i];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < after.Count; i++)
        {
            if (outp.Count >= MaxChanges) return;
            string id = afterIds[i];
            seen.Add(id);
            beforeById.TryGetValue(id, out var b);
            path.Add(new DiffStep { Name = Label(after[i], id), IsElement = true, List = list, Position = i + 1, Before = b, After = after[i] });
            try
            {
                if (b != null) Diff(b, after[i], path, outp);
                else Emit(outp, PackChangeKind.Added, path, null, after[i]);
            }
            finally { path.RemoveAt(path.Count - 1); }
        }

        for (int i = 0; i < before.Count; i++)
        {
            if (outp.Count >= MaxChanges) return;
            if (seen.Contains(beforeIds[i])) continue;
            path.Add(new DiffStep { Name = Label(before[i], beforeIds[i]), IsElement = true, List = list, Position = i + 1, Before = before[i] });
            try { Emit(outp, PackChangeKind.Removed, path, before[i], null); }
            finally { path.RemoveAt(path.Count - 1); }
        }

        // The ones on both sides, in a different order. Worth one line: node
        // order is what the editor's list shows and what the dialogue plays
        // in. Only those on both sides are compared - a line added at the top
        // moves nobody - and the ones that moved are named rather than both
        // orders printed: for a dialogue those are hash ids, and the point of
        // the row is "this bit moved", not "here are 40 numbers twice".
        var kept = new HashSet<string>(beforeIds.Where(seen.Contains), StringComparer.Ordinal);
        var beforeOrder = beforeIds.Where(kept.Contains).ToList();
        var afterOrder = afterIds.Where(kept.Contains).ToList();
        if (!beforeOrder.SequenceEqual(afterOrder))
        {
            var movedIds = new HashSet<string>(afterOrder.Where((id, i) => beforeOrder[i] != id), StringComparer.Ordinal);
            var movedLabels = after.Where((t, i) => movedIds.Contains(afterIds[i]))
                                   .Select(t => Label(t, ""))
                                   .Where(l => !string.IsNullOrEmpty(l))
                                   .Take(3).ToList();
            string what = movedLabels.Count == 0
                ? Loc.P("diff.entries", movedIds.Count)
                : movedIds.Count > movedLabels.Count
                    ? Loc.P("diff.andMore", movedIds.Count - movedLabels.Count, "items", Loc.JoinList(movedLabels))
                    : Loc.JoinList(movedLabels);
            Emit(outp, PackChangeKind.Reordered, path, null, null, Truncate(what));
        }
    }

    /// <summary>
    /// Display name for an array element in a path. The matching identity is
    /// often something the author never sees — a dialogue node's id is a hash —
    /// so a path built from raw identities is unnavigable. Prefer whatever the
    /// editor itself puts on screen for that thing.
    /// </summary>
    private static string Label(JToken token, string identity)
    {
        if (token is not JObject obj) return identity;

        var text = Str(obj["text"]);
        if (!string.IsNullOrEmpty(text))
            return "\"" + Clip(System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim(), 44) + "\"";

        var display = Str(obj["displayName"]);
        if (!string.IsNullOrEmpty(display) && !string.Equals(display, identity, StringComparison.Ordinal))
            return string.IsNullOrEmpty(identity) ? display : identity + " — " + display;

        return identity;
    }

    private static string Str(JToken? token)
        => token == null || token.Type == JTokenType.Null ? "" : token.ToString();

    private static string Clip(string s, int max)
        => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

    /// <summary>
    /// Per-element identity for the whole array, or null when the array can't be
    /// keyed — any non-object element, any element without one of
    /// <see cref="IdentityProps"/>, or a duplicate. All-or-nothing on purpose:
    /// a partially keyed match would silently pair up the wrong elements.
    /// </summary>
    private static List<string>? Identities(JArray array)
    {
        var ids = new List<string>(array.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var el in array)
        {
            if (el is not JObject obj) return null;
            string? id = null;
            foreach (var prop in IdentityProps)
            {
                var v = obj[prop];
                if (v == null || v.Type == JTokenType.Null) continue;
                var s = v.ToString();
                if (string.IsNullOrEmpty(s)) continue;
                id = s;
                break;
            }
            if (id == null || !seen.Add(id)) return null;
            ids.Add(id);
        }
        return ids;
    }

    private static bool IsScalarList(JArray array)
        => array.All(t => t.Type != JTokenType.Object && t.Type != JTokenType.Array);

    // ── Formatting ───────────────────────────────────────────────────────

    private static void Emit(List<PackChange> outp, PackChangeKind kind, List<DiffStep> path,
                             JToken? before, JToken? after, string? moved = null)
    {
        if (outp.Count >= MaxChanges) return;
        var words = ChangeWords.Describe(kind, path, before, after, moved);
        if (words == null) return;   // said by another row already: a line's ids as it was added
        outp.Add(new PackChange
        {
            Kind = kind,
            Section = path.Count > 0 ? path[0].Name : "pack",
            Path = path.Count > 1 ? string.Join(" › ", path.Skip(1).Select(s => s.Name)) : "(section)",
            Before = kind == PackChangeKind.Reordered ? Loc.T("diff.differentOrder") : words.Value.BeforeInFull,
            After = kind == PackChangeKind.Reordered ? moved ?? "" : words.Value.AfterInFull,
            Group = words.Value.Group,
            Text = words.Value.Text,
            Was = words.Value.Was,
            Now = words.Value.Now,
        });
    }

    internal static string Truncate(string s)
        => s.Length <= MaxValueLength ? s : s.Substring(0, MaxValueLength - 1) + "…";
}
