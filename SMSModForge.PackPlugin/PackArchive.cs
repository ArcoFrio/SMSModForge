using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Read-only view of a packed <c>.smspack</c> file. Wraps a
    /// <see cref="ZipArchive"/> opened in <see cref="ZipArchiveMode.Read"/>
    /// mode so all the asset loaders share one container instead of each
    /// re-opening the zip per call.
    /// <para/>
    /// The container is just a regular ZIP under the hood — same folder
    /// structure the editor authors loose (modpack.json at the root, then
    /// <c>Audio/</c>, <c>Scenes/</c>, <c>Sprites/</c>, <c>Wallpaper/</c>,
    /// etc.) — so manifest paths stay valid character-for-character.
    /// Inspectable with any zip tool which keeps debugging cheap; the
    /// extension exists only to make the file recognisable to users and
    /// to keep file managers from auto-treating it as a generic archive.
    /// <para/>
    /// <b>Path normalisation</b>: ZIP entries store forward-slash paths.
    /// The editor authors slashes the same way, but a few the host modPack
    /// entries (and Windows-side manifest edits) carry backslashes. Every
    /// lookup runs through <see cref="Normalize"/> which flips backslashes
    /// to forward and strips a leading <c>./</c>.
    /// <para/>
    /// <b>Audio extraction</b>: Unity's <c>UnityWebRequestMultimedia</c>
    /// can't stream from a <see cref="Stream"/> — it needs a file URI. For
    /// audio loads we extract the entry to a deterministic temp path
    /// (<c>%TEMP%/SMSModForge/&lt;packId&gt;/&lt;rel&gt;</c>) on first
    /// request and cache the path for reuse. The cache survives the
    /// process; we don't proactively clean up — temp dirs are cheap and
    /// the OS reclaims them eventually.
    /// </summary>
    public sealed class PackArchive : IDisposable
    {
        /// <summary>Canonical extension for packed modpack files.</summary>
        public const string FileExtension = ".smspack";

        /// <summary>Filename of the manifest entry inside the archive.</summary>
        public const string ManifestEntryName = "modpack.json";

        /// <summary>Absolute path to the source .smspack file on disk.</summary>
        public string SourcePath { get; }

        /// <summary>The packId resolved from <c>modpack.json</c>, used as
        /// the temp-directory key.</summary>
        public string PackId { get; }

        private readonly ZipArchive _zip;
        private readonly Dictionary<string, ZipArchiveEntry> _entries;
        private readonly string _tempRoot;
        private readonly Dictionary<string, string> _extractedCache
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private PackArchive(string sourcePath, ZipArchive zip, string packId,
                            Dictionary<string, ZipArchiveEntry> entries)
        {
            SourcePath = sourcePath;
            _zip = zip;
            PackId = packId;
            _entries = entries;

            // Per-pack temp dir for audio extraction.
            _tempRoot = Path.Combine(Path.GetTempPath(), "SMSModForge", PackId);
            Directory.CreateDirectory(_tempRoot);
        }

        /// <summary>
        /// Open a <c>.smspack</c> file and return a ready-to-use archive,
        /// or null on failure (missing file / bad zip / missing manifest).
        /// Diagnostics go through <paramref name="logger"/>.
        /// </summary>
        public static PackArchive TryOpen(string smspackPath, ManualLogSource logger)
        {
            if (!File.Exists(smspackPath))
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] PackArchive: file not found '" + smspackPath + "'");
                return null;
            }

            ZipArchive zip;
            try { zip = ZipFile.OpenRead(smspackPath); }
            catch (System.Exception ex)
            {
                logger?.LogError("[SMSModForge.PackPlugin] PackArchive: bad zip '" + smspackPath +
                                 "': " + ex.Message);
                return null;
            }

            // Index every entry by its normalised path for O(1) lookups.
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in zip.Entries)
            {
                // Skip directory placeholders (FullName ending with /); we
                // don't need them for asset lookup.
                if (e.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    e.FullName.EndsWith("\\", StringComparison.Ordinal))
                    continue;
                entries[Normalize(e.FullName)] = e;
            }

            // Manifest entry must be present at the root.
            if (!entries.TryGetValue(ManifestEntryName, out var manifestEntry))
            {
                logger?.LogError("[SMSModForge.PackPlugin] PackArchive: '" + smspackPath +
                                 "' has no " + ManifestEntryName + " — not a valid pack.");
                zip.Dispose();
                return null;
            }

            // Peek the packId out of the manifest so the temp dir + log
            // lines can refer to it. We re-parse the manifest in
            // PackManifest.TryLoad below; that's cheap and keeps the
            // archive layer JSON-agnostic.
            string packId;
            try
            {
                using (var stream = manifestEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string text = reader.ReadToEnd();
                    var parsed = Newtonsoft.Json.Linq.JObject.Parse(text);
                    packId = (string)parsed["packId"] ?? Path.GetFileNameWithoutExtension(smspackPath);
                }
            }
            catch (System.Exception ex)
            {
                logger?.LogError("[SMSModForge.PackPlugin] PackArchive: failed to read packId from '" +
                                 smspackPath + "': " + ex.Message);
                zip.Dispose();
                return null;
            }

            return new PackArchive(smspackPath, zip, packId, entries);
        }

        /// <summary>
        /// True when the archive contains an entry at the (normalised)
        /// relative path. Doubles as the replacement for the loose-file
        /// <c>File.Exists</c> checks every factory used to do before load.
        /// </summary>
        public bool Has(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;
            return _entries.ContainsKey(Normalize(rel));
        }

        /// <summary>Read an entry as UTF-8 text. Returns null when the entry
        /// is missing.</summary>
        public string ReadText(string rel)
        {
            if (!TryGetEntry(rel, out var entry)) return null;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>Read an entry as a byte array. Returns null when missing.
        /// Used by every <c>Texture2D.LoadImage</c> + sprite factory.</summary>
        public byte[] ReadBytes(string rel)
        {
            if (!TryGetEntry(rel, out var entry)) return null;
            using (var stream = entry.Open())
            using (var ms = new MemoryStream(checked((int)entry.Length)))
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Extract an entry to a deterministic temp file and return the
        /// absolute path. Subsequent calls for the same <paramref name="rel"/>
        /// return the cached path immediately. Returns null when the
        /// entry is missing. Used by audio loaders that need a file URI
        /// for <c>UnityWebRequestMultimedia.GetAudioClip</c>.
        /// <para/>
        /// We re-extract on each new process (cache lives in-memory only)
        /// so a re-shipped pack picks up edits without manual cleanup; if
        /// the pack hasn't changed the second call after the in-memory
        /// cache is gone simply rewrites the same bytes.
        /// </summary>
        public string ExtractToTemp(string rel)
        {
            if (!TryGetEntry(rel, out var entry)) return null;
            string key = Normalize(rel);
            if (_extractedCache.TryGetValue(key, out var cached) && File.Exists(cached))
                return cached;

            // Mirror the archive's folder layout under the temp root so
            // multiple files with the same leaf name don't collide.
            string outPath = Path.Combine(_tempRoot, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            using (var src = entry.Open())
            using (var dst = File.Create(outPath))
                src.CopyTo(dst);

            _extractedCache[key] = outPath;
            return outPath;
        }

        public void Dispose()
        {
            try { _zip?.Dispose(); } catch { /* swallow — disposing twice is benign */ }
        }

        // ── Internal helpers ──────────────────────────────────────────────

        private bool TryGetEntry(string rel, out ZipArchiveEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(rel)) return false;
            return _entries.TryGetValue(Normalize(rel), out entry);
        }

        /// <summary>
        /// Flip backslashes to forward slashes and strip a leading "./" so
        /// every path-shape variant collapses to the same lookup key.
        /// Editor-authored paths are forward-slash, but some legacy
        /// manifest entries carry Windows-style separators and the OS
        /// happily round-trips both.
        /// </summary>
        public static string Normalize(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return rel;
            string norm = rel.Replace('\\', '/');
            if (norm.StartsWith("./", StringComparison.Ordinal)) norm = norm.Substring(2);
            return norm;
        }
    }
}
