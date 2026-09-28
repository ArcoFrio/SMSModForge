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

        /// <summary>
        /// The language this pack is being played in, or null when it is being
        /// played as its author wrote it.
        /// <para/>
        /// Set when a translation of the pack is laid over the manifest. It is
        /// what ModForge's own words about a pack follow: a pack with no Spanish
        /// is an English pack, and calling its player "Tú" while every line
        /// around it is in English makes the editor look like it half-translated
        /// somebody's work.
        /// </summary>
        public string TranslatedInto { get; internal set; }

        /// <summary>The translation the pack is played in, when it is - kept
        /// for the game's own lines in the conversations the pack extends,
        /// which are not in the manifest (see <see cref="GameLineTranslations"/>).</summary>
        public SMSModForge.Shared.TextFile Translation { get; internal set; }

        /// <summary>The language the pack's own words are in: its manifest's
        /// <c>language</c>, English when it does not say.</summary>
        public string Language => SMSModForge.Shared.PackTexts.LanguageOf(Root);

        /// <summary>The language the pack is being played in: a translation's,
        /// or its own.</summary>
        public string PlayedIn => TranslatedInto ?? Language;

        public JArray Characters => Root["characters"] as JArray;
        public JArray Places => Root["places"] as JArray;
        public JArray MapButtons => Root["mapButtons"] as JArray;

        /// <summary>The pack's UI: screens of its own, and changes to screens
        /// the game already has, told apart by whether an entry names a
        /// source.</summary>
        public JArray Uis => Root["uis"] as JArray;

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
            var manifest = new PackManifest { PackId = packId, Archive = archive, Root = root };
            // In the player's language, where the pack has a translation into
            // it - here, so nothing that reads the manifest ever sees the
            // other words.
            PluginLanguage.Translate(manifest, logger);

            // And the letters those words are written in. After the translation,
            // so a pack played in Chinese is scanned as Chinese — and asked of
            // the manifest rather than of the player's language, because what a
            // pack is written in has nothing to do with what the player set.
            try { PluginFonts.AddForText(manifest.Root.ToString(), packId, logger); }
            catch (System.Exception ex)
            {
                logger?.LogWarning("[SMSModForge.PackPlugin] Fonts: " + packId
                                   + " could not be scanned for letters the game lacks: " + ex.Message);
            }
            return manifest;
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
