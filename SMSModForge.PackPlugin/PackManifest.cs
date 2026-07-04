using BepInEx.Logging;
using Newtonsoft.Json.Linq;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Parsed view of one <c>modpack.json</c> alongside a handle to its
    /// surrounding <see cref="PackArchive"/>. Holds the original
    /// <see cref="JObject"/> so the downstream builders don't each have to
    /// re-walk the JSON.
    /// <para/>
    /// The plugin reads JSON via <c>Newtonsoft.Json.Linq</c> rather than
    /// strong-typed POCOs so unknown keys are tolerated — packs authored by
    /// future editor versions still load.
    /// <para/>
    /// As of the .smspack switch the manifest no longer holds a loose
    /// directory path; every asset reference resolves through the archive's
    /// entry table instead. See <see cref="ReadBytes"/> /
    /// <see cref="ReadText"/> / <see cref="Has"/> / <see cref="ExtractToTemp"/>
    /// which mirror the archive's API one-to-one so call sites that used
    /// to do <c>File.Exists(pack.AbsPath(rel))</c> now do
    /// <c>pack.Has(rel)</c>.
    /// </summary>
    public sealed class PackManifest
    {
        public string PackId { get; private set; }
        public PackArchive Archive { get; private set; }
        public JObject Root { get; private set; }

        public JArray Characters => Root["characters"] as JArray;
        public JArray Places => Root["places"] as JArray;
        public JArray MapButtons => Root["mapButtons"] as JArray;

        /// <summary>Loose-file fallback constant kept only for the editor /
        /// exporter and external tools that want to refer to the same string;
        /// the runtime never opens a loose modpack.json anymore.</summary>
        public const string ManifestFileName = PackArchive.ManifestEntryName;

        /// <summary>
        /// Open a packed <c>.smspack</c> file and return a parsed manifest.
        /// Returns null on any open / parse failure; diagnostics are sent to
        /// <paramref name="logger"/> so the caller can keep iterating.
        /// </summary>
        public static PackManifest TryLoad(string smspackPath, ManualLogSource logger)
        {
            var archive = PackArchive.TryOpen(smspackPath, logger);
            if (archive == null) return null;
            return TryLoadFromArchive(archive, logger);
        }

        /// <summary>
        /// Build a manifest from an already-opened <see cref="PackArchive"/>.
        /// Useful when the caller already opened the zip (e.g. to extract
        /// metadata for a banner) and wants to keep using the same handle.
        /// On failure the archive is disposed so the caller doesn't leak it.
        /// </summary>
        public static PackManifest TryLoadFromArchive(PackArchive archive, ManualLogSource logger)
        {
            string text = archive.ReadText(PackArchive.ManifestEntryName);
            if (text == null)
            {
                logger?.LogError("[SMSModForge.PackPlugin] PackArchive '" + archive.SourcePath +
                                 "' missing " + PackArchive.ManifestEntryName + ".");
                archive.Dispose();
                return null;
            }

            JObject root;
            try { root = JObject.Parse(text); }
            catch (System.Exception ex)
            {
                logger?.LogError("[SMSModForge.PackPlugin] Bad JSON in '" + archive.SourcePath +
                                 "': " + ex.Message);
                archive.Dispose();
                return null;
            }

            string packId = (string)root["packId"] ?? archive.PackId;
            return new PackManifest { PackId = packId, Archive = archive, Root = root };
        }

        /// <summary>True when the archive contains a file at the given
        /// relative path. Replaces the old <c>File.Exists(AbsPath(rel))</c>
        /// pattern.</summary>
        public bool Has(string rel) => Archive != null && Archive.Has(rel);

        /// <summary>Read an entry as UTF-8 text; returns null on miss.</summary>
        public string ReadText(string rel) => Archive?.ReadText(rel);

        /// <summary>Read an entry as bytes; returns null on miss. The
        /// standard input to <c>Texture2D.LoadImage</c>.</summary>
        public byte[] ReadBytes(string rel) => Archive?.ReadBytes(rel);

        /// <summary>Extract an entry to a deterministic temp path and return
        /// it; null on miss. Used by audio loaders that need a file URI
        /// for <c>UnityWebRequestMultimedia.GetAudioClip</c>.</summary>
        public string ExtractToTemp(string rel) => Archive?.ExtractToTemp(rel);
    }
}
