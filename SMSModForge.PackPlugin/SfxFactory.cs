using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds the per-pack SFX catalog: creates a dedicated
    /// <c>SfxPlayer</c> AudioSource under <c>12_AudioPlayer</c> and
    /// async-loads each declared clip (plus any auto-detected
    /// numbered variants — <c>Plap.ogg</c>, <c>Plap_1.ogg</c>,
    /// <c>Plap_2.ogg</c>, …) from disk. The clips are registered in
    /// <see cref="SfxRegistry"/>; both the explicit <c>PlaySFX</c>
    /// action and the dispatcher's text-pattern detector look them
    /// up by key.
    /// <para/>
    /// Unlike music, SFX doesn't need a GameObject per clip —
    /// <see cref="AudioSource.PlayOneShot(AudioClip, float)"/>
    /// handles overlap natively. So we only build one GameObject per
    /// pack, regardless of how many clips it ships.
    /// </summary>
    public static class SfxFactory
    {
        public static void BuildAll(PackManifest pack, SfxRegistry registry,
                                    MonoBehaviour pluginHost, ManualLogSource logger)
        {
            var sfx = pack.Root["sfx"] as JArray;
            if (sfx == null || sfx.Count == 0) return;

            var audioPlayer = GameObject.Find("12_AudioPlayer")?.transform;
            if (audioPlayer == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] SFX: 12_AudioPlayer not " +
                    "found — skipping SFX build for pack " + pack.PackId);
                return;
            }

            // One AudioSource per pack. Naming includes the pack id so
            // we can spot per-pack ownership in the scene hierarchy.
            var playerGo = new GameObject("SfxPlayer (pack:" + pack.PackId + ")");
            playerGo.transform.SetParent(audioPlayer, false);
            var source = playerGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f; // 2D — UI / dialogue context, never spatialised
            registry.Source = source;

            int queued = 0;
            foreach (var s in sfx)
            {
                try
                {
                    queued += QueueOne((JObject)s, pack, registry, pluginHost, logger);
                }
                catch (System.Exception ex)
                {
                    logger.LogError("[SMSModForge.PackPlugin] SFX build failed for " +
                        (string)((JObject)s)["key"] + " in " + pack.PackId + ": " + ex.Message);
                }
            }
            if (queued > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                               "' queued " + queued + " SFX clip(s) (base + variants) for load.");
        }

        /// <summary>
        /// Register the SFX entry, then queue async loads for the base
        /// audio file and every <c>&lt;basename&gt;_&lt;N&gt;.&lt;ext&gt;</c>
        /// variant that sits beside it in the pack folder. Returns the
        /// number of files queued (base + variants).
        /// </summary>
        private static int QueueOne(JObject s, PackManifest pack, SfxRegistry registry,
            MonoBehaviour pluginHost, ManualLogSource logger)
        {
            string key = (string)s["key"];
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] SFX in " + pack.PackId +
                                  " has no key — skipping.");
                return 0;
            }

            string audioRel = (string)s["audioPath"];
            if (string.IsNullOrEmpty(audioRel) || !pack.Has(audioRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] SFX '" + key + "' in " +
                                  pack.PackId + " missing audioPath '" + audioRel + "' in archive — skipping.");
                return 0;
            }

            float defaultVolume = 1f;
            if (s["defaultVolume"] != null) defaultVolume = (float)s["defaultVolume"];

            var patterns = new List<string>();
            if (s["textPatterns"] is JArray patternArray)
                foreach (var p in patternArray)
                {
                    string ps = (string)p;
                    if (!string.IsNullOrEmpty(ps)) patterns.Add(ps);
                }

            // Register an empty entry now so a PlaySFX firing during
            // the async load window finds the entry; the clip list
            // populates as the coroutines complete. Pattern detection
            // checks Clips.Count > 0 before scheduling so unloaded
            // entries are no-ops too.
            registry.GetOrCreate(key, defaultVolume, patterns);

            // Variant discovery: same folder as the base entry, same base
            // name suffixed with "_1", "_2", … up to the first gap. Same
            // shape as the host mod's CreateSFX loop pattern, just rebased
            // onto archive-relative paths. Mixed extensions tolerated
            // (Plap.ogg + Plap_1.wav is fine) because we re-derive the
            // AudioType from each entry's extension at load time.
            var rels = new System.Collections.Generic.List<string> { audioRel };
            string archiveDir = GetArchiveDir(audioRel);
            string baseName = Path.GetFileNameWithoutExtension(audioRel);
            string ext = Path.GetExtension(audioRel);
            for (int i = 1; ; i++)
            {
                string variantRel = JoinArchive(archiveDir, baseName + "_" + i + ext);
                if (!pack.Has(variantRel))
                {
                    string fallback = null;
                    foreach (var alt in new[] { ".ogg", ".OGG", ".wav", ".WAV", ".mp3", ".MP3" })
                    {
                        string candidate = JoinArchive(archiveDir, baseName + "_" + i + alt);
                        if (pack.Has(candidate)) { fallback = candidate; break; }
                    }
                    if (fallback == null) break;
                    variantRel = fallback;
                }
                rels.Add(variantRel);
            }

            foreach (var r in rels)
            {
                // Extract to a temp file the UnityWebRequest can URI-open.
                string path = pack.ExtractToTemp(r);
                if (path == null) continue;
                pluginHost.StartCoroutine(LoadAudioCoroutine(path, key, registry,
                                                             pack.PackId, logger));
            }
            return rels.Count;
        }

        /// <summary>Archive paths use forward slashes; return everything
        /// before the last slash so variant discovery can rebuild
        /// sibling paths.</summary>
        private static string GetArchiveDir(string rel)
        {
            int idx = rel.LastIndexOf('/');
            return idx < 0 ? "" : rel.Substring(0, idx);
        }

        /// <summary>Join an archive directory + leaf with a forward slash.
        /// Empty dir collapses to bare leaf.</summary>
        private static string JoinArchive(string dir, string leaf)
            => string.IsNullOrEmpty(dir) ? leaf : (dir + "/" + leaf);

        private static IEnumerator LoadAudioCoroutine(string path, string key,
            SfxRegistry registry, string packId, ManualLogSource logger)
        {
            AudioType type = AudioType.OGGVORBIS;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav") type = AudioType.WAV;
            else if (ext == ".mp3") type = AudioType.MPEG;

            string uri = new System.Uri(path).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    logger.LogError("[SMSModForge.PackPlugin] SFX load failed for '" + key +
                                    "' (" + Path.GetFileName(path) + ") in " + packId + ": " + req.error);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null) yield break;
                clip.name = Path.GetFileNameWithoutExtension(path);
                registry.AddClip(key, clip);
            }
        }
    }
}
