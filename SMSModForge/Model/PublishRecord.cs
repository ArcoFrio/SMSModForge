using System;
using System.IO;

namespace SMSModForge.Model;

/// <summary>
/// The manifest as it was the last time the pack was published.
/// <para/>
/// This is what makes the version mean something. A version identifies a
/// RELEASE, so the only honest question a bump can answer is "what is different
/// about this pack since the last one players got" — and answering it needs the
/// last one to compare against.
/// <para/>
/// Saves are not releases and neither are test exports, which is why neither of
/// them moves the number any more. An author exporting twenty times in an
/// afternoon to try something in the game has not released twenty times.
/// <para/>
/// Kept as the whole manifest rather than a summary of it, deliberately: the
/// comparison is then <see cref="VersionBump.Classify"/> unchanged, with no
/// second implementation of "what counts as a change" to drift out of step. It
/// costs a copy of one JSON file — two megabytes beside a pack of a hundred and
/// thirty — and it is excluded from export, so players never see it.
/// </summary>
public static class PublishRecord
{
    /// <summary>
    /// What the record is called, beside the manifest.
    /// <para/>
    /// Named for what it is, and dotted so it sorts away from an author's own
    /// files. <see cref="PackExporter"/> skips it by name.
    /// </summary>
    public const string FileName = ".smsforge-published.json";

    /// <summary>Whether a path names the record, so the exporter can leave it
    /// out of what ships.</summary>
    public static bool Is(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return string.Equals(Path.GetFileName(path), FileName,
                             StringComparison.OrdinalIgnoreCase);
    }

    private static string PathIn(string packRoot) => Path.Combine(packRoot, FileName);

    /// <summary>
    /// The manifest from the last publish, or null when there has not been one.
    /// <para/>
    /// Null is an ordinary answer, not an error: a pack that has never been
    /// published, and one whose folder was moved or freshly cloned, both land
    /// here. The caller proposes a version anyway and shows it to the author
    /// before anything is written.
    /// </summary>
    public static string? Read(string? packRoot)
    {
        if (string.IsNullOrWhiteSpace(packRoot)) return null;

        try
        {
            string path = PathIn(packRoot!);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Record what was just published.
    /// <para/>
    /// Written only by a publish that actually produced an archive. A failed
    /// one must leave the previous record alone, or the next publish would
    /// compare against a release that does not exist and under-report what
    /// changed.
    /// </summary>
    public static void Write(string? packRoot, string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(packRoot) || manifestJson == null) return;

        try { File.WriteAllText(PathIn(packRoot!), manifestJson); }
        catch (IOException) { /* the archive is written; this is a convenience */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The version the last published manifest carried, or null.
    /// <para/>
    /// Used to tell "the author typed a version" from "the version is where
    /// the last publish left it". A hand-typed one is a decision and is
    /// published as written.
    /// </summary>
    public static PackVersion? PublishedVersion(string? packRoot)
    {
        string? json = Read(packRoot);
        if (json == null) return null;

        try
        {
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(json);
            return PackVersion.Parse((string?)parsed["version"]);
        }
        catch (Newtonsoft.Json.JsonException) { return null; }
    }
}
