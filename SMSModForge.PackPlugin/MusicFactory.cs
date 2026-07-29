using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds pack-authored music tracks under <c>12_AudioPlayer</c>.
    /// Mirrors a host music-player pattern: clone
    /// the vanilla <c>Beach</c> child, rename it to the pack's key, and
    /// load an <see cref="AudioClip"/> from disk into its
    /// <see cref="AudioSource"/>. The track stays inactive until a
    /// <c>SwitchMusic</c> dialogue action turns it on — the existing
    /// action does <c>audioPlayer.Find(name)</c>, which finds pack
    /// tracks the same way it finds vanilla / the host mod tracks.
    /// <para/>
    /// Audio loading is async: <see cref="UnityWebRequestMultimedia"/>
    /// returns the clip on a coroutine. The clip is assigned to the
    /// source when the request completes; if the player triggers a
    /// <c>SwitchMusic</c> before the load finishes (unlikely at normal
    /// play tempo), the track plays silent until the assignment lands
    /// and then becomes audible — no crash, no spurious GameObject.
    /// </summary>
    public static class MusicFactory
    {
        public static void BuildAll(PackManifest pack, MonoBehaviour pluginHost,
                                    ManualLogSource logger)
        {
            var tracks = pack.Root["music"] as JArray;
            if (tracks == null || tracks.Count == 0) return;

            var audioPlayer = GameObject.Find("12_AudioPlayer")?.transform;
            var template = audioPlayer?.Find("Beach")?.gameObject;
            if (audioPlayer == null || template == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music: " +
                    "12_AudioPlayer/Beach template missing — skipping music build for pack " +
                    pack.PackId);
                return;
            }

            int built = 0;
            foreach (var t in tracks)
            {
                try
                {
                    if (BuildOne((JObject)t, pack, pluginHost, audioPlayer, template, logger))
                        built++;
                }
                catch (System.Exception ex)
                {
                    logger.LogError("[SMSModForge.PackPlugin] Music build failed for " +
                        (string)((JObject)t)["key"] + " in " + pack.PackId + ": " + ex.Message);
                }
            }
            if (built > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                               "' queued " + built + " music track(s) for load.");
        }

        private static bool BuildOne(JObject t, PackManifest pack, MonoBehaviour pluginHost,
            Transform audioPlayer, GameObject template, ManualLogSource logger)
        {
            string key = (string)t["key"];
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music in " + pack.PackId +
                                  " has no key — skipping.");
                return false;
            }

            string audioRel = (string)t["audioPath"];
            if (string.IsNullOrEmpty(audioRel) || !pack.Has(audioRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music '" + key + "' in " +
                                  pack.PackId + " missing audioPath '" + audioRel + "' in archive — skipping.");
                return false;
            }

            // UnityWebRequestMultimedia.GetAudioClip needs a file URI; the
            // archive helper extracts the entry to a deterministic temp
            // path on first use and caches the result for reuse, so a
            // pack that lists the same clip twice doesn't pay the IO cost
            // twice.
            string path = pack.ExtractToTemp(audioRel);
            if (string.IsNullOrEmpty(path))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music '" + key + "' in " +
                                  pack.PackId + " — failed to extract '" + audioRel + "' to temp.");
                return false;
            }

            // Refuse a name collision with an already-present GO under
            // 12_AudioPlayer — the SwitchMusic action would become
            // ambiguous and we'd risk stomping a vanilla / the host mod
            // track.
            if (audioPlayer.Find(key) != null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music '" + key + "' in " +
                                  pack.PackId + " collides with an existing audio child — skipping.");
                return false;
            }

            // Clone the template; rename to the pack's key so
            // SwitchMusic can find it by name.
            var go = Object.Instantiate(template, audioPlayer);
            go.name = key;

            var source = go.GetComponent<AudioSource>();
            if (source == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Music '" + key + "' in " +
                                  pack.PackId + " — cloned template has no AudioSource.");
            }
            else
            {
                // The clone inherits the template's clip, and if the template
                // was active it has already begun playing it. Silence it before
                // anything is audible; the pack's own clip lands on the
                // coroutine below.
                source.Stop();
                source.clip = null;

                // Apply overrides BEFORE the clip lands — Unity is fine
                // with loop / volume changes on a clipless source.
                if (t["loop"] != null) source.loop = (bool)t["loop"];
                if (t["volume"] != null) source.volume = (float)t["volume"];

                // playOnAwake stays ON, matching every other track under
                // 12_AudioPlayer. A track there sounds BECAUSE it is enabled:
                // SwitchMusic only deactivates the siblings and activates the
                // one it wants, and never calls Play(). Turning playOnAwake off
                // therefore made pack music silent by every route — switched to
                // by a dialogue action, or enabled by hand.
                source.playOnAwake = true;
            }

            go.SetActive(false);

            pluginHost.StartCoroutine(LoadAudioCoroutine(path, source, key, pack.PackId, logger));
            return true;
        }

        /// <summary>
        /// Stream the audio file into an <see cref="AudioClip"/> and
        /// assign it to <paramref name="source"/>. Format is inferred
        /// from the file extension — OGG is the default since that's
        /// what the host mod's bundled tracks are.
        /// </summary>
        private static IEnumerator LoadAudioCoroutine(string path, AudioSource source,
            string key, string packId, ManualLogSource logger)
        {
            AudioType type = AudioType.OGGVORBIS;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav") type = AudioType.WAV;
            else if (ext == ".mp3") type = AudioType.MPEG;

            // file:// URI form keeps UnityWebRequest happy on Windows
            // paths; the underlying loader does the right thing on
            // every Unity-supported platform.
            string uri = new System.Uri(path).AbsoluteUri;
            using (var req = UnityWebRequestMultimedia.GetAudioClip(uri, type))
            {
                yield return req.SendWebRequest();
                // Starmaker Story 1.8E ships Unity 2020+ where the
                // Result enum is the canonical success check; the
                // legacy isNetworkError / isHttpError properties are
                // deprecated and produce build warnings.
                if (req.result != UnityWebRequest.Result.Success)
                {
                    logger.LogError("[SMSModForge.PackPlugin] Music load failed for '" + key +
                                    "' in " + packId + ": " + req.error);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(req);
                if (clip == null)
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Music load returned null clip for '" +
                                      key + "' in " + packId + ".");
                    yield break;
                }
                clip.name = key;
                if (source != null)
                {
                    source.clip = clip;

                    // If the track was switched to while the clip was still
                    // loading, playOnAwake has already come and gone against an
                    // empty source. Assigning a clip does NOT start playback, so
                    // without this the track stays silent for as long as it
                    // remains enabled — the load simply never becomes audible.
                    if (source.isActiveAndEnabled && !source.isPlaying)
                        source.Play();
                }
                logger.LogInfo("[SMSModForge.PackPlugin] Music '" + key + "' loaded for pack " + packId + ".");
            }
        }
    }
}
