using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds pack-authored CG / story scene GameObjects. Mirrors the
    /// the host mod <c>Scenes.CreateNewPicScene</c> pattern: clone the
    /// vanilla <c>Samanthabeach</c> scene under
    /// <c>4_CG_Manager-Sexy</c>, swap the <c>Core/Art</c> SpriteRenderer
    /// (the scene image), swap the <c>Core</c> SpriteRenderer (the
    /// frame), and override the prototype's <c>Trigger</c> per the
    /// authored <see cref="SMSModForge.Model.SceneSoundMode"/>.
    /// <para/>
    /// Output goes into the per-pack <see cref="SceneRegistry"/>; the
    /// <c>ActivateScene</c> action handler reads from there.
    /// </summary>
    public static class SceneFactory
    {
        /// <summary>
        /// Build every scene declared on <paramref name="pack"/> into
        /// <paramref name="registry"/>. Missing files / unresolved frame
        /// references are logged as warnings — the scene is skipped, the
        /// rest of the pack still loads.
        /// </summary>
        public static void BuildAll(PackManifest pack, SceneRegistry registry, ManualLogSource logger)
        {
            var scenes = pack.Root["scenes"] as JArray;
            if (scenes == null || scenes.Count == 0) return;

            var cgManager = GameObject.Find("4_CG_Manager-Sexy")?.transform;
            if (cgManager == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Scenes: 4_CG_Manager-Sexy not found — skipping scene build for pack " + pack.PackId);
                return;
            }
            var prototype = cgManager.Find("Samanthabeach")?.gameObject;
            if (prototype == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Scenes: Samanthabeach prototype missing under 4_CG_Manager-Sexy — skipping scene build for pack " + pack.PackId);
                return;
            }

            string frameRoot = ResolvePluginFrameRoot();

            // SpriteManager only lays out GameObjects in its serialized
            // targetObjects list — that's what shifts concurrently-active
            // scenes apart by their slot instead of stacking them at the
            // prototype's position. Every built scene must be ADDED to that
            // list (RefreshCache alone only re-caches renderers of objects
            // already in it). Mirrors the original CreateNewPicScene:
            // `SpriteManager.targetObjects.Add(newPicScene)`.
            var spriteManager = cgManager.GetComponent("SpriteManager");
            System.Collections.IList targetObjects = null;
            if (spriteManager != null)
                targetObjects = spriteManager.GetType().GetField("targetObjects")
                    ?.GetValue(spriteManager) as System.Collections.IList;
            if (targetObjects == null)
                logger.LogWarning("[SMSModForge.PackPlugin] Scenes: SpriteManager.targetObjects not reachable — pack scenes won't auto-arrange.");

            int built = 0;
            foreach (var s in scenes)
            {
                try
                {
                    var go = BuildOne((JObject)s, pack, registry, cgManager, prototype, frameRoot, logger);
                    if (go != null)
                    {
                        targetObjects?.Add(go);
                        built++;
                    }
                }
                catch (System.Exception ex)
                {
                    logger.LogError("[SMSModForge.PackPlugin] Scene build failed for " +
                        (string)((JObject)s)["key"] + " in " + pack.PackId + ": " + ex.Message);
                }
            }

            if (built > 0)
            {
                // RefreshCache picks up the new list entries so SpriteManager's
                // renderer cache + layout include them.
                if (spriteManager != null)
                {
                    var refresh = spriteManager.GetType().GetMethod("RefreshCache");
                    refresh?.Invoke(spriteManager, null);
                }
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId + "' built " + built + " scene(s).");
            }
        }

        /// <summary>Builds one scene; returns the built GameObject (for
        /// SpriteManager targetObjects registration) or null when skipped.</summary>
        private static GameObject BuildOne(JObject s, PackManifest pack, SceneRegistry registry,
            Transform cgManager, GameObject prototype, string frameRoot, ManualLogSource logger)
        {
            string key = (string)s["key"];
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Scene in " + pack.PackId + " has no key — skipping.");
                return null;
            }

            // Scene art is an archive-relative path. The legacy
            // externalSpritePath fallback is gone — packs must ship every
            // sprite they reference, otherwise the export would have
            // omitted them and there's nothing the plugin can do anyway.
            string sceneRel = (string)s["sceneSprite"];
            if (string.IsNullOrEmpty(sceneRel) || !pack.Has(sceneRel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Scene '" + key + "' in " + pack.PackId + " missing sprite '" + sceneRel + "' in archive — skipping.");
                return null;
            }

            // Clone the prototype. The resulting GO is named "pack:<id>.<key>"
            // so multiple packs can author the same key without clashing —
            // GameObject.Find is fine on this name because there are no other
            // siblings with the same name shape.
            GameObject scene = Object.Instantiate(prototype, cgManager);
            scene.name = "pack:" + pack.PackId + "." + key;
            scene.SetActive(false);

            // Scene image: Core/Art SpriteRenderer.
            var core = scene.transform.Find("Core");
            var art = core != null ? core.Find("Art") : null;
            if (art != null)
            {
                var sr = art.GetComponent<SpriteRenderer>();
                if (sr != null) Dress(sr, scene, pack, s, sceneRel, key, logger);
            }
            else
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Scene '" + key + "' — prototype has no Core/Art child; scene image not applied.");
            }

            // Frame: Core SpriteRenderer. customFrameSprite (archive-
            // relative) wins over vanillaFrame (plugin-bundled, loose).
            string customFrame = (string)s["customFrameSprite"];
            string vanillaFrame = (string)s["vanillaFrame"];

            if (!string.IsNullOrEmpty(customFrame) && pack.Has(customFrame))
            {
                if (core != null)
                {
                    var sr = core.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.sprite = LoadSpriteFromPack(pack, customFrame);
                }
            }
            else if (!string.IsNullOrEmpty(vanillaFrame) && !string.IsNullOrEmpty(frameRoot))
            {
                // Vanilla frame PNGs ship with the plugin DLL, not the
                // pack — those are read from disk the old way.
                string framePath = Path.Combine(frameRoot, vanillaFrame);
                if (File.Exists(framePath))
                {
                    if (core != null)
                    {
                        var sr = core.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.sprite = LoadSpriteFromBytes(File.ReadAllBytes(framePath));
                    }
                }
                else
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Scene '" + key + "' vanilla frame not found at " + framePath + " — keeping prototype's frame.");
                }
            }

            // Sound override. None = leave the trigger alone. Anything else
            // strips the Trigger so the prototype's clip never plays — the
            // ActivateScene action emits the configured signal alongside the
            // SetActive(true) call.
            string soundMode = (string)s["sound"] ?? "Silent";
            string activationSignal = null;
            if (soundMode != "None")
            {
                DestroyComponentByName(scene, "Trigger");
                if (soundMode == "Kiss") activationSignal = "kiss";
                else if (soundMode == "Flash") activationSignal = "flash";
                // "Silent" → no signal, just the stripped trigger.
            }

            registry.Register(key, scene, activationSignal);
            return scene;
        }

        /// <summary>
        /// Load a PNG out of the pack archive into a fresh <see cref="Sprite"/>.
        /// Mirrors the host mod loader — 256×256 source, point filter,
        /// centre pivot — which is what the prototype expects for scene
        /// art. Frames use the same dimensions on the prototype, so we use
        /// one loader for both pack art and pack frames.
        /// </summary>
        private static Sprite LoadSpriteFromPack(PackManifest pack, string rel)
            => LoadSpriteFromBytes(pack.ReadBytes(rel));

        /// <summary>Same as <see cref="LoadSpriteFromPack"/> but for plugin-
        /// bundled vanilla frame PNGs (read with <c>File.ReadAllBytes</c> by
        /// the caller). Kept separate so the archive layer doesn't pretend
        /// to know about loose plugin-side assets.</summary>
        private static Sprite LoadSpriteFromBytes(byte[] bytes)
        {
            var tex = Decode(bytes);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Put the scene's art on its renderer — a picture, a decoded
        /// animation, or a video.
        /// <para/>
        /// Which one is decided by the extension of the file the author picked,
        /// through the same rule the editor used when it decided whether to
        /// decode anything. A still is the old path exactly.
        /// <para/>
        /// Every route ends up at the same size: the frames of an animation are
        /// fitted one by one, and a video is drawn on the quad a fitted still
        /// already established. An animation is not a different kind of scene,
        /// it is a scene that moves.
        /// </summary>
        private static void Dress(SpriteRenderer sr, GameObject scene, PackManifest pack,
                                  JObject s, string sceneRel, string key,
                                  ManualLogSource logger)
        {
            int kind = SMSModForge.Shared.MediaKinds.KindOf(sceneRel);
            bool loop = s["loop"] == null || (bool)s["loop"];

            if (kind == SMSModForge.Shared.MediaKinds.Gif)
            {
                double[] delays;
                var frames = AnimatedSprite.LoadFrames(
                    pack, sceneRel, out delays, tex => FittedSprite.CreateScene(tex));

                if (frames != null && frames.Length > 0)
                {
                    sr.sprite = frames[0];
                    scene.AddComponent<AnimatedSprite>().Play(sr, frames, delays, loop);
                    return;
                }

                // Saved before the frames were written, or shipped without
                // them. The GIF's own first frame is not readable by the engine,
                // so this is a scene that cannot draw at all - worth saying.
                logger.LogWarning("[SMSModForge.PackPlugin] Scene '" + key + "' — '" + sceneRel
                                  + "' has no decoded frames beside it ("
                                  + SMSModForge.Shared.MediaKinds.FramesFolderFor(sceneRel)
                                  + "). Re-save the pack in ModForge to write them.");
                return;
            }

            if (kind == SMSModForge.Shared.MediaKinds.Video)
            {
                // A still first, so the renderer has a quad of the right size
                // for the video to be drawn on - and so a video that will not
                // load leaves a picture rather than nothing.
                sr.sprite = FittedSprite.Create(
                    Blank(), FittedSprite.ScenePixels, FittedSprite.ScenePixels,
                    FittedSprite.ScenePpu);

                string onDisk = pack.ExtractToTemp(sceneRel);
                if (string.IsNullOrEmpty(onDisk))
                {
                    logger.LogWarning("[SMSModForge.PackPlugin] Scene '" + key + "' — video '"
                                      + sceneRel + "' is not in the pack archive.");
                    return;
                }

                float volume = s["volume"] == null ? 1f : (float)s["volume"];
                AnimatedSprite.PlayVideo(scene, sr, onDisk, loop, volume,
                                         (int)FittedSprite.ScenePixels,
                                         (int)FittedSprite.ScenePixels, logger);
                return;
            }

            sr.sprite = LoadSceneArt(pack.ReadBytes(sceneRel));
        }

        /// <summary>
        /// What a video's frame shows before its first picture arrives.
        /// <para/>
        /// Cleared to transparent, and explicitly: a fresh Texture2D holds
        /// whatever memory it was given, which came out as a solid white block
        /// sitting in the scene's frame. Nothing is the right answer for "not
        /// yet" - and if the video never loads, an empty frame reads as an
        /// empty frame rather than as a picture that failed to be one.
        /// </summary>
        private static Texture2D Blank()
        {
            int side = (int)FittedSprite.ScenePixels;
            var tex = new Texture2D(side, side, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var clear = new Color32[side * side];
            tex.SetPixels32(clear);
            tex.Apply(false);
            return tex;
        }

        /// <summary>
        /// A scene's own art, at the size a 256x256 scene occupies whatever
        /// the file's dimensions are.
        /// <para/>
        /// The frame loader above deliberately does NOT do this. A frame's
        /// size is a design decision - the two shipped with the tool are
        /// 297x310 and 388x405 - and fitting those to a 256 square would
        /// shrink both of them.
        /// </summary>
        private static Sprite LoadSceneArt(byte[] bytes)
            => FittedSprite.CreateScene(Decode(bytes));

        private static Texture2D Decode(byte[] bytes)
        {
            var tex = new Texture2D(256, 256);
            tex.filterMode = FilterMode.Point;
            if (bytes != null) ImageConversion.LoadImage(tex, bytes);
            return tex;
        }

        /// <summary>
        /// Folder next to the plugin DLL where <c>VanillaFrame</c> PNGs
        /// live. Empty when the folder is absent — callers treat that as
        /// "vanilla frame names won't resolve", which is fine because
        /// custom frames take precedence anyway.
        /// </summary>
        private static string ResolvePluginFrameRoot()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dllDir)) return null;
                string candidate = Path.Combine(dllDir, "VanillaFrames");
                if (Directory.Exists(candidate)) return candidate;
            }
            catch { /* fall through */ }
            return null;
        }

        private static void DestroyComponentByName(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName) { Object.Destroy(c); return; }
            }
        }
    }
}
