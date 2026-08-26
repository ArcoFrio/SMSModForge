using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

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

    /// <summary>Top-level manifest section (<c>dialogues</c>, <c>places</c>, …).
    /// The confirmation window groups on this.</summary>
    public string Section { get; init; } = "";

    /// <summary>Human-readable location, e.g.
    /// <c>GSDialogueStory05 › nodes › 12 › text</c>. Excludes
    /// <see cref="Section"/>, which the group header already shows.</summary>
    public string Path { get; init; } = "";

    public string Before { get; init; } = "";
    public string After { get; init; } = "";

    /// <summary>Glyph shown in the leading column.</summary>
    public string Glyph => Kind switch
    {
        PackChangeKind.Added => "+",
        PackChangeKind.Removed => "−",
        PackChangeKind.Reordered => "⇅",
        _ => "•",
    };
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
/// every new field.
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
                Path = "(whole manifest)",
                After = "no previous saved state to compare against",
            });
            return changes;
        }

        Diff(before, after, new List<string>(), changes);
        return changes;
    }

    private static JToken? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JToken.Parse(json); }
        catch { return null; }
    }

    // ── Walk ─────────────────────────────────────────────────────────────

    private static void Diff(JToken before, JToken after, List<string> path, List<PackChange> outp)
    {
        if (outp.Count >= MaxChanges) return;
        if (JToken.DeepEquals(before, after)) return;

        // A type flip (object → array, string → number) has no meaningful
        // sub-structure to descend into; report it whole.
        if (before.Type != after.Type)
        {
            Emit(outp, PackChangeKind.Changed, path, Summarize(before), Summarize(after));
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
                Emit(outp, PackChangeKind.Changed, path, Summarize(before), Summarize(after));
                break;
        }
    }

    private static void DiffObject(JObject before, JObject after, List<string> path, List<PackChange> outp)
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
            path.Add(name);
            try
            {
                if (a == null) Emit(outp, PackChangeKind.Removed, path, Summarize(b!), "");
                else if (b == null) Emit(outp, PackChangeKind.Added, path, "", Summarize(a));
                else Diff(b, a, path, outp);
            }
            finally { path.RemoveAt(path.Count - 1); }
        }
    }

    private static void DiffArray(JArray before, JArray after, List<string> path, List<PackChange> outp)
    {
        // A list of plain values (children ids, root ids, outfit names) reads far
        // better as one before/after line than as a change per index — and an
        // index-by-index walk of a list that had an item inserted at the front
        // reports every element as changed.
        if (IsScalarList(before) && IsScalarList(after))
        {
            Emit(outp, PackChangeKind.Changed, path, JoinScalars(before), JoinScalars(after));
            return;
        }

        var beforeIds = Identities(before);
        var afterIds = Identities(after);

        if (beforeIds != null && afterIds != null)
        {
            DiffByIdentity(before, beforeIds, after, afterIds, path, outp);
            return;
        }

        // No usable identity (conditions, actions — positional by nature).
        int n = Math.Max(before.Count, after.Count);
        for (int i = 0; i < n; i++)
        {
            if (outp.Count >= MaxChanges) return;
            path.Add("#" + (i + 1));
            try
            {
                if (i >= after.Count) Emit(outp, PackChangeKind.Removed, path, Summarize(before[i]), "");
                else if (i >= before.Count) Emit(outp, PackChangeKind.Added, path, "", Summarize(after[i]));
                else Diff(before[i], after[i], path, outp);
            }
            finally { path.RemoveAt(path.Count - 1); }
        }
    }

    private static void DiffByIdentity(JArray before, List<string> beforeIds,
                                       JArray after, List<string> afterIds,
                                       List<string> path, List<PackChange> outp)
    {
        var beforeById = new Dictionary<string, JToken>(StringComparer.Ordinal);
        for (int i = 0; i < before.Count; i++) beforeById[beforeIds[i]] = before[i];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < after.Count; i++)
        {
            if (outp.Count >= MaxChanges) return;
            string id = afterIds[i];
            seen.Add(id);
            path.Add(Label(after[i], id));
            try
            {
                if (beforeById.TryGetValue(id, out var b)) Diff(b, after[i], path, outp);
                else Emit(outp, PackChangeKind.Added, path, "", Summarize(after[i]));
            }
            finally { path.RemoveAt(path.Count - 1); }
        }

        for (int i = 0; i < before.Count; i++)
        {
            if (outp.Count >= MaxChanges) return;
            if (seen.Contains(beforeIds[i])) continue;
            path.Add(Label(before[i], beforeIds[i]));
            try { Emit(outp, PackChangeKind.Removed, path, Summarize(before[i]), ""); }
            finally { path.RemoveAt(path.Count - 1); }
        }

        // Same membership, different sequence. Worth one line: node order is
        // what the editor's list shows and what the dialogue plays in. Naming
        // the entries that actually moved beats printing both orderings —
        // for a dialogue those are opaque hash ids, and the whole point of the
        // row is "this bit moved", not "here are 40 numbers twice".
        if (seen.Count == beforeIds.Count && !beforeIds.SequenceEqual(afterIds))
        {
            var movedIds = new HashSet<string>(
                afterIds.Where((id, i) => beforeIds[i] != id), StringComparer.Ordinal);
            var movedLabels = after.Where((t, i) => movedIds.Contains(afterIds[i]))
                                   .Select(t => Label(t, ""))
                                   .Where(l => !string.IsNullOrEmpty(l))
                                   .Take(3).ToList();
            string what = movedLabels.Count == 0
                ? after.Count + " entries"
                : string.Join(", ", movedLabels) + (movedIds.Count > movedLabels.Count
                    ? " and " + (movedIds.Count - movedLabels.Count) + " more" : "");
            Emit(outp, PackChangeKind.Reordered, path, "different order", Truncate(what));
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

    private static string JoinScalars(JArray array)
        => array.Count == 0 ? "(empty)" : Truncate(string.Join(", ", array.Select(t => t.ToString())));

    // ── Formatting ───────────────────────────────────────────────────────

    private static void Emit(List<PackChange> outp, PackChangeKind kind, List<string> path,
                             string before, string after)
    {
        if (outp.Count >= MaxChanges) return;
        outp.Add(new PackChange
        {
            Kind = kind,
            Section = path.Count > 0 ? path[0] : "pack",
            Path = path.Count > 1 ? string.Join(" › ", path.Skip(1)) : "(section)",
            Before = before,
            After = after,
        });
    }

    /// <summary>One-line preview of a value. Objects show their identifying
    /// fields rather than their whole body — an added dialogue would otherwise
    /// dump its entire node tree into a table cell.</summary>
    private static string Summarize(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
                return "(none)";
            // Newtonsoft renders a JSON bool as "True"/"False"; the manifest
            // (and every other row in this table) says true/false.
            case JTokenType.Boolean:
                return (bool)token ? "true" : "false";
            case JTokenType.Object:
            {
                var obj = (JObject)token;
                var bits = new List<string>();
                foreach (var prop in obj.Properties())
                {
                    if (prop.Value.Type is JTokenType.Object or JTokenType.Array) continue;
                    bits.Add(prop.Name + ": " + Summarize(prop.Value));
                    if (bits.Count == 4) break;
                }
                int nested = obj.Properties().Count(p => p.Value.Type is JTokenType.Object or JTokenType.Array);
                if (nested > 0) bits.Add("+" + nested + " nested");
                return Truncate("{ " + string.Join(", ", bits) + " }");
            }
            case JTokenType.Array:
            {
                var arr = (JArray)token;
                return arr.Count == 1 ? "1 entry" : arr.Count + " entries";
            }
            default:
            {
                var s = token.ToString();
                if (string.IsNullOrEmpty(s)) return "(empty)";
                return Truncate(System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim());
            }
        }
    }

    private static string Truncate(string s)
        => s.Length <= MaxValueLength ? s : s.Substring(0, MaxValueLength - 1) + "…";
}
