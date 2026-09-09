using System;
using System.IO;
using System.IO.Compression;

namespace SMSModForge.Model;

/// <summary>
/// Packages a pack the way a player should receive it.
/// <para/>
/// An export produces a <c>.smspack</c> and leaves the author to explain where
/// it goes. That explanation is where releases go wrong: the file has to land
/// in a folder most players will never have opened, and getting it wrong
/// produces a game that starts perfectly and simply does not have the mod in
/// it — the hardest kind of failure for anybody to diagnose, and the one that
/// comes back as "your mod doesn't work".
/// <para/>
/// So a published archive is not a pack file. It is a picture of the game
/// folder with the pack already in the right place:
/// <code>
///   MyPack-1.2.0.zip
///     Mods/MyPack.smspack
/// </code>
/// Extract it into the game folder and the install is finished. There is no
/// step to get wrong, because there is no step.
/// <para/>
/// One entry, and nothing beside it. There used to be a readme at the root
/// explaining where to extract to, which was a mistake in two directions: two
/// packs both carrying a README.txt collide the moment somebody installs the
/// second one, and a player who does extract it has put a file in their game
/// folder that does nothing. Anything the archive would have said belongs on
/// the page it is downloaded from, where it can be read BEFORE the decision it
/// is about.
/// <para/>
/// The runtime plugin is deliberately NOT included. Two packs shipping
/// different builds of it would overwrite each other's runtime, and the
/// breakage would land on a player who did nothing wrong. It is a prerequisite,
/// like the mod loader itself, and the download page is where to say so.
/// </summary>
public static class PackPublisher
{
    /// <summary>The folder inside the archive, which is also the folder the
    /// runtime looks in first. One name, compiled into both, so they cannot
    /// disagree - see <see cref="Shared.ModsFolder"/>.</summary>
    public const string ModsFolderName = Shared.ModsFolder.Name;

    /// <summary>What a publish produced.</summary>
    public sealed class Result
    {
        /// <summary>The archive written.</summary>
        public string OutputPath { get; init; } = "";

        /// <summary>Where the pack sits inside it.</summary>
        public string EntryPath { get; init; } = "";

        /// <summary>Size of the archive on disk.</summary>
        public long Bytes { get; init; }

        /// <summary>How many files went into the pack itself.</summary>
        public int FileCount { get; init; }
    }

    /// <summary>
    /// The file name a published archive should have: the pack and the version
    /// it is, so a player with three downloads can tell them apart.
    /// </summary>
    public static string ArchiveNameFor(string packId, PackVersion version)
        => Safe(packId) + "-" + version + ".zip";

    /// <summary>
    /// The name the pack keeps INSIDE the archive — no version in it, on
    /// purpose.
    /// <para/>
    /// A player installing an update should replace the pack they have, not end
    /// up with two of it in the same folder. The version belongs on the
    /// download, which is what they choose between; the installed file is just
    /// the pack.
    /// </summary>
    public static string PackNameFor(string packId)
        => Safe(packId) + PackExporter.FileExtension;

    /// <summary>
    /// Build the archive.
    /// <para/>
    /// The pack is exported to a temporary file first and then stored — not
    /// re-compressed — inside the outer zip, because a <c>.smspack</c> is
    /// already a compressed archive and squeezing it again costs time to save
    /// nothing.
    /// </summary>
    public static Result Publish(string packRoot, string outputZipPath, string packId)
    {
        if (string.IsNullOrWhiteSpace(packRoot))
            throw new ArgumentException("packRoot is empty", nameof(packRoot));
        if (string.IsNullOrWhiteSpace(outputZipPath))
            throw new ArgumentException("outputZipPath is empty", nameof(outputZipPath));

        string? outputDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        string entry = ModsFolderName + "/" + PackNameFor(packId);
        string staged = Path.Combine(Path.GetTempPath(),
                                     "smsforge-publish-" + Guid.NewGuid().ToString("N")
                                     + PackExporter.FileExtension);

        try
        {
            var packed = PackExporter.Export(packRoot, staged);

            // Deleted rather than overwritten: appending into a stale archive
            // is what happens otherwise if the OS still holds a handle.
            if (File.Exists(outputZipPath)) File.Delete(outputZipPath);

            using (var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create))
                zip.CreateEntryFromFile(staged, entry, CompressionLevel.NoCompression);

            return new Result
            {
                OutputPath = outputZipPath,
                EntryPath = entry,
                Bytes = new FileInfo(outputZipPath).Length,
                FileCount = packed.FileCount,
            };
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); } catch (IOException) { }
        }
    }

    /// <summary>A pack id with anything a file name cannot hold taken out.</summary>
    private static string Safe(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return "pack";

        var made = new System.Text.StringBuilder(packId.Length);
        foreach (char c in packId)
            made.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);

        string cleaned = made.ToString().Trim();
        return cleaned.Length == 0 ? "pack" : cleaned;
    }
}
