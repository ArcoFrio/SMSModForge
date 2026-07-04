using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SMSModForge.Model;

/// <summary>
/// Bundles a loose pack folder into a single <c>.smspack</c> archive that
/// the runtime plugin reads.
/// <para/>
/// The output is a regular ZIP container with two structural rules:
/// <list type="number">
///   <item><c>modpack.json</c> sits at the archive root (the plugin's
///   <c>PackArchive.TryOpen</c> requires it there).</item>
///   <item>Every other file keeps its position relative to the pack root
///   on disk — <c>Sprites/MyChar/Base.PNG</c> in the folder lands at the
///   same path inside the archive, so the manifest's sprite-path strings
///   stay valid character-for-character.</item>
/// </list>
/// <para/>
/// Files filtered out at export time:
/// <list type="bullet">
///   <item>VCS + editor noise (<c>.git/</c>, <c>.gitignore</c>, <c>.DS_Store</c>, <c>Thumbs.db</c>)</item>
///   <item>Existing <c>.smspack</c> files in the folder (e.g. a previous
///   export sitting alongside the loose files)</item>
///   <item>Ad-hoc scripts the build process drops (the <c>_assemble*.py</c>
///   files left behind by content-rewrite tooling)</item>
/// </list>
/// Authors who need to ship something not covered by the filter can keep
/// it in a subfolder the manifest doesn't reference; the exporter still
/// adds it (no path-reachability check), so the file is along for the ride
/// even if the runtime never loads it.
/// </summary>
public static class PackExporter
{
    /// <summary>Canonical extension for exported pack files. Must match
    /// <c>SMSModForge.PackPlugin.PackArchive.FileExtension</c>.</summary>
    public const string FileExtension = ".smspack";

    /// <summary>Filename the runtime expects at the archive root. Must
    /// match <c>SMSModForge.PackPlugin.PackArchive.ManifestEntryName</c>.</summary>
    public const string ManifestEntryName = "modpack.json";

    /// <summary>Patterns (case-insensitive) that get skipped during export
    /// — matched against the relative path's individual segments.</summary>
    private static readonly string[] SegmentBlocklist =
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "__pycache__",
    };

    /// <summary>File names (case-insensitive, leaf-only) that get skipped.</summary>
    private static readonly string[] FileBlocklist =
    {
        ".gitignore",
        ".gitattributes",
        ".gitkeep",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
    };

    /// <summary>File-name patterns to skip (case-insensitive prefix match).</summary>
    private static readonly string[] FilePrefixBlocklist =
    {
        "_assemble",   // throwaway content-migration scripts
    };

    /// <summary>
    /// Walk <paramref name="packRoot"/> and write every non-excluded file
    /// into <paramref name="outputSmspackPath"/> as a fresh ZIP archive.
    /// Overwrites the output if it exists. Throws on IO failure; the
    /// caller can surface that to the editor UI.
    /// </summary>
    /// <param name="packRoot">Absolute path to the pack folder on disk
    /// (the same folder containing <c>modpack.json</c>).</param>
    /// <param name="outputSmspackPath">Absolute destination path. The
    /// extension should be <see cref="FileExtension"/> but the writer
    /// doesn't enforce it.</param>
    /// <returns>Stats on the export — total file count + bytes written.
    /// Useful for surfacing in a success toast.</returns>
    public static ExportResult Export(string packRoot, string outputSmspackPath)
    {
        if (string.IsNullOrWhiteSpace(packRoot))
            throw new ArgumentException("packRoot is empty", nameof(packRoot));
        if (!Directory.Exists(packRoot))
            throw new DirectoryNotFoundException("Pack folder does not exist: " + packRoot);

        string manifestSourcePath = Path.Combine(packRoot, ManifestEntryName);
        if (!File.Exists(manifestSourcePath))
            throw new FileNotFoundException(
                "Pack folder is missing " + ManifestEntryName + " — can't export an empty pack.",
                manifestSourcePath);

        // Ensure the output directory exists. If the user passed e.g.
        // "C:\Packs\MyPack.smspack" but C:\Packs doesn't exist, create it.
        string? outputDir = Path.GetDirectoryName(outputSmspackPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Overwrite policy: delete first so we don't end up appending into
        // a stale archive when the OS holds a handle.
        if (File.Exists(outputSmspackPath)) File.Delete(outputSmspackPath);

        int fileCount = 0;
        long sourceBytes = 0;

        // Optimal compression keeps file size down at the cost of a
        // longer export — fine for a one-off operation. Switching to
        // Fastest later is a one-token change if export becomes the
        // hot path during iteration.
        using (var zip = ZipFile.Open(outputSmspackPath, ZipArchiveMode.Create))
        {
            foreach (var (fullPath, relPath) in EnumerateExportable(packRoot))
            {
                // ZIP entries use forward slashes regardless of platform —
                // the runtime archive reader normalises both ways but the
                // wire format is canonical so the same archive opens
                // identically on every OS.
                string entryName = relPath.Replace(Path.DirectorySeparatorChar, '/');
                zip.CreateEntryFromFile(fullPath, entryName, CompressionLevel.Optimal);
                fileCount++;
                // Read source size off the source file rather than the
                // entry: ZipArchiveEntry in Create mode throws on
                // .Length / .CompressedLength until the archive is
                // disposed ("Length properties are unavailable once an
                // entry has been opened for writing"). We get final
                // compressed size from disk after the using block below.
                sourceBytes += new FileInfo(fullPath).Length;
            }
        }

        // Now that the archive is closed, the OS exposes the file's
        // packed size. Read it for the success toast.
        long compressedBytes = new FileInfo(outputSmspackPath).Length;
        return new ExportResult(fileCount, sourceBytes, compressedBytes, outputSmspackPath);
    }

    /// <summary>
    /// Enumerate every file under <paramref name="packRoot"/> that should
    /// land in the archive, returned as <c>(absolutePath, relativePath)</c>
    /// pairs. Yields <see cref="ManifestEntryName"/> first so it always
    /// sits at the archive root, then the rest in directory-walk order.
    /// </summary>
    private static IEnumerable<(string FullPath, string RelPath)> EnumerateExportable(string packRoot)
    {
        // modpack.json first — the runtime requires this entry to exist
        // and putting it at index 0 means it lives at the start of the
        // archive's central directory, which is a tiny optimisation for
        // the plugin's "peek the packId" scan in DiscoverPacks.
        string manifestPath = Path.Combine(packRoot, ManifestEntryName);
        yield return (manifestPath, ManifestEntryName);

        foreach (var fullPath in Directory.EnumerateFiles(packRoot, "*", SearchOption.AllDirectories))
        {
            // Compute the path relative to the pack root. Path.GetRelativePath
            // gives back a forward-slash path on Unix-ish runtimes and
            // backslashes on Windows; the caller normalises before writing.
            string rel = Path.GetRelativePath(packRoot, fullPath);

            // Skip the manifest itself (already emitted above).
            if (string.Equals(rel, ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (ShouldSkip(rel)) continue;

            yield return (fullPath, rel);
        }
    }

    /// <summary>
    /// Returns true when the relative path should be excluded from the
    /// archive. Path is normalised to a list of segments so all the
    /// segment-level filters can run uniformly.
    /// </summary>
    private static bool ShouldSkip(string relPath)
    {
        if (string.IsNullOrEmpty(relPath)) return true;

        // Existing exports parked alongside the loose files.
        if (relPath.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
            return true;

        // Split on both Unix and Windows separators so the segment checks
        // catch nested matches regardless of where the relative path came
        // from.
        string[] segments = relPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var seg in segments)
        {
            // Segment-level blocklist for whole subtrees (.git, etc.).
            foreach (var blocked in SegmentBlocklist)
                if (string.Equals(seg, blocked, StringComparison.OrdinalIgnoreCase)) return true;
        }

        string leaf = segments.LastOrDefault() ?? string.Empty;

        foreach (var blocked in FileBlocklist)
            if (string.Equals(leaf, blocked, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var prefix in FilePrefixBlocklist)
            if (leaf.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>Lightweight result record returned to the editor UI.</summary>
    public sealed class ExportResult
    {
        public int FileCount { get; }
        /// <summary>Total uncompressed bytes summed across every input file
        /// the exporter wrote into the archive.</summary>
        public long SourceBytes { get; }
        /// <summary>Final on-disk size of the produced .smspack file
        /// (i.e. total compressed bytes including the ZIP central
        /// directory + per-entry overhead).</summary>
        public long CompressedBytes { get; }
        public string OutputPath { get; }
        public ExportResult(int fileCount, long sourceBytes, long compressedBytes, string outputPath)
        {
            FileCount = fileCount;
            SourceBytes = sourceBytes;
            CompressedBytes = compressedBytes;
            OutputPath = outputPath;
        }
    }
}
