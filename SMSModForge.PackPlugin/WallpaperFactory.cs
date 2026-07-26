using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds pack-authored desktop wallpapers for the in-game PC.
    /// Mirrors the host mod's <c>Wallpaper.CreateWallpaper</c>: clone the
    /// vanilla base wallpaper (under <c>Desktop/Wallpaper/Wallpaper (0)</c>)
    /// and its selector button (under
    /// <c>Wallpaperselection/UI_Core/List/WallpaperButton (0)</c>), swap
    /// both sprites to the pack-supplied PNG, and gate the button's
    /// visibility on the authored unlock condition (typically an
    /// <c>Event_Seen*</c> flag).
    /// <para/>
    /// Each built button registers a per-frame visibility check via
    /// <see cref="WallpaperRegistry"/>, which the plugin's Update loop
    /// re-evaluates so the button appears the moment the unlock
    /// condition flips true.
    /// </summary>
    public static class WallpaperFactory
    {
        /// <summary>
        /// Build every wallpaper declared on <paramref name="pack"/> into
        /// <paramref name="registry"/>. Missing files / unresolved frame
        /// references are logged as warnings — the wallpaper is skipped,
        /// the rest of the pack still loads.
        /// </summary>
        public static void BuildAll(PackManifest pack, WallpaperRegistry registry,
                                    PackVariableStore vars, ManualLogSource logger)
        {
            var wallpapers = pack.Root["wallpapers"] as JArray;
            if (wallpapers == null || wallpapers.Count == 0) return;

            var mainCanvas = GameObject.Find("9_MainCanvas")?.transform;
            // FindPathIncludingInactive walks each segment as a direct
            // child lookup with includeInactive semantics — both the
            // wallpaper carousel and the selector panel are typically
            // inactive at scene load, so the chained Transform.Find
            // form depends on whether the intermediate parents happen
            // to be active. Using the explicit walker is robust either
            // way.
            var baseWallpaper = mainCanvas
                ?.FindPathIncludingInactive("Desktop/Wallpaper/Wallpaper (0)")?.gameObject;
            var baseButton = mainCanvas
                ?.FindPathIncludingInactive("Wallpaperselection/UI_Core/List/WallpaperButton (0)")?.gameObject;
            if (baseWallpaper == null || baseButton == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Wallpapers: vanilla " +
                    "base wallpaper or selector button missing — skipping wallpaper " +
                    "build for pack " + pack.PackId);
                return;
            }

            // The vanilla "UI_Select" click sound is on Core.otherBundle in
            // the host mod; we don't have a handle to that bundle, but the
            // base button already has an AudioSource we can clone — its
            // clip survives the Instantiate.
            int built = 0;
            foreach (var w in wallpapers)
            {
                try
                {
                    if (BuildOne((JObject)w, pack, registry, vars,
                                 baseWallpaper, baseButton, logger))
                        built++;
                }
                catch (System.Exception ex)
                {
                    logger.LogError("[SMSModForge.PackPlugin] Wallpaper build failed for " +
                        (string)((JObject)w)["key"] + " in " + pack.PackId + ": " + ex.Message);
                }
            }
            if (built > 0)
                logger.LogInfo("[SMSModForge.PackPlugin] Pack '" + pack.PackId +
                               "' built " + built + " wallpaper(s).");
        }

        private static bool BuildOne(JObject w, PackManifest pack, WallpaperRegistry registry,
            PackVariableStore vars, GameObject baseWallpaper, GameObject baseButton,
            ManualLogSource logger)
        {
            string key = (string)w["key"];
            if (string.IsNullOrEmpty(key))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Wallpaper in " + pack.PackId +
                                  " has no key — skipping.");
                return false;
            }

            // Sprite path is archive-relative. The legacy external fallback
            // is gone — packs must ship every wallpaper image they declare.
            string rel = (string)w["spritePath"];
            if (string.IsNullOrEmpty(rel) || !pack.Has(rel))
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Wallpaper '" + key + "' in " +
                                  pack.PackId + " missing sprite '" + rel + "' in archive — skipping.");
                return false;
            }

            var sprite = LoadSpriteFromPack(pack, rel);
            if (sprite == null)
            {
                logger.LogWarning("[SMSModForge.PackPlugin] Wallpaper '" + key + "' in " +
                                  pack.PackId + " sprite load failed — skipping.");
                return false;
            }

            // Clone the wallpaper display GO and apply the sprite.
            var wallpaperGo = Object.Instantiate(baseWallpaper, baseWallpaper.transform.parent);
            wallpaperGo.name = "pack:" + pack.PackId + "." + key;
            int wallpaperIndex = wallpaperGo.transform.GetSiblingIndex();
            var img = wallpaperGo.GetComponent<Image>();
            if (img != null) img.sprite = sprite;
            wallpaperGo.SetActive(false);

            // Clone the selector button and apply the same sprite.
            var buttonGo = Object.Instantiate(baseButton, baseButton.transform.parent);
            buttonGo.name = "WallpaperButton (pack:" + pack.PackId + "." + key + ")";
            var btnImg = buttonGo.GetComponent<Image>();
            if (btnImg != null) btnImg.sprite = sprite;

            // Strip the vanilla ButtonInstructions (it carries an undesired
            // GC2 binding); replace with a plain UnityUI.Button so the
            // click handler is straightforward.
            foreach (var bi in buttonGo.GetComponents<Component>())
            {
                if (bi != null && bi.GetType().Name == "ButtonInstructions")
                    Object.DestroyImmediate(bi);
            }
            var button = buttonGo.GetComponent<Button>();
            if (button == null) button = buttonGo.AddComponent<Button>();

            // Mirror the host mod's button styling: transparent normal,
            // white highlight, grey pressed.
            if (buttonGo.transform.childCount > 0)
            {
                var childImg = buttonGo.transform.GetChild(0).GetComponent<Image>();
                if (childImg != null)
                {
                    button.targetGraphic = childImg;
                    var colors = button.colors;
                    colors.normalColor = new Color(1, 1, 1, 0);
                    colors.highlightedColor = Color.white;
                    colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1);
                    colors.selectedColor = Color.white;
                    button.colors = colors;
                    button.transition = Selectable.Transition.ColorTint;
                }
            }

            // Click → set the GC2 Wallpaper variable (vanilla index-based
            // switch) and activate just this wallpaper child.
            int capturedIdx = wallpaperIndex;
            Transform wallpaperParent = wallpaperGo.transform.parent;
            button.onClick.AddListener(() =>
            {
                // Vanilla wallpaper switching reads a GC2 numeric var
                // named "Wallpaper" — same hook the host mod uses.
                GameVariableBridge.SetDouble("Wallpaper", (double)capturedIdx);
                for (int i = 0; i < wallpaperParent.childCount; i++)
                    wallpaperParent.GetChild(i).gameObject.SetActive(i == capturedIdx);

                var audio = buttonGo.GetComponent<AudioSource>();
                if (audio != null && audio.clip != null)
                    audio.PlayOneShot(audio.clip, 0.5f);
            });
            buttonGo.SetActive(false);

            // Register so the per-frame visibility check can flip it on
            // when its unlock conditions are satisfied. New packs author the
            // "unlockConditions" array; the legacy single "unlockCondition"
            // object is wrapped into a one-element array so old exports keep
            // gating identically.
            var unlocks = w["unlockConditions"] as JArray;
            if (unlocks == null && w["unlockCondition"] is JObject legacy)
                unlocks = new JArray(legacy);
            registry.Register(new WallpaperRegistry.Entry
            {
                Key = key,
                PackId = pack.PackId,
                Button = buttonGo,
                Display = wallpaperGo,
                UnlockConditions = unlocks,
                Vars = vars,
            });
            return true;
        }

        /// <summary>
        /// Load a PNG out of the pack archive into a 1920×1080 sprite —
        /// vanilla wallpaper dimensions.
        /// </summary>
        private static Sprite LoadSpriteFromPack(PackManifest pack, string rel)
        {
            try
            {
                var bytes = pack.ReadBytes(rel);
                if (bytes == null) return null;
                var tex = new Texture2D(1920, 1080);
                ImageConversion.LoadImage(tex, bytes);
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                     new Vector2(0.5f, 0.5f));
            }
            catch
            {
                return null;
            }
        }
    }
}
