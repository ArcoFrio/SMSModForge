using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Finding the sprites and fonts a pack's UI asks for, in the running game.
    /// <para/>
    /// The editor works from an extraction — a folder of PNGs keyed by texture
    /// and crop. The game has no such folder; it has the real assets, already
    /// loaded, referenced by whatever happens to be using them. So a name is the
    /// only thing that can travel between the two, and finding what a name means
    /// here is a scan.
    /// <para/>
    /// Scanned once and cached, because the scan is expensive and the answer does
    /// not change: <c>Resources.FindObjectsOfTypeAll</c> walks everything loaded,
    /// which is thousands of objects, and a pack's UI may ask for the same
    /// "Semi Rounded" fifty times.
    /// </summary>
    internal static class UiAssets
    {
        private static Dictionary<string, Sprite> _sprites;
        private static Dictionary<string, TMP_FontAsset> _fonts;

        /// <summary>
        /// A sprite by the name the editor showed. Pack files win over the
        /// game's own, so a pack can ship art of its own and still reach for
        /// vanilla when it wants to look native.
        /// <para/>
        /// A name ending in <c>#n</c> is one of the nine the game reuses across
        /// more than one crop. The suffix is the editor's, assigned by sorting
        /// the sharers on texture and crop; nothing here can reproduce that
        /// ordering from a live scene, so the suffix is dropped and the first
        /// match taken. Wrong only for those nine, and wrong in the direction of
        /// drawing something rather than nothing.
        /// </summary>
        public static Sprite Sprite(string name, PackManifest pack)
        {
            if (string.IsNullOrEmpty(name)) return null;

            var own = FromPack(name, pack);
            if (own != null) return own;

            EnsureSprites();
            if (_sprites.TryGetValue(name, out var found)) return found;

            int hash = name.LastIndexOf('#');
            if (hash > 0 && _sprites.TryGetValue(name.Substring(0, hash), out found))
                return found;

            return null;
        }

        public static TMP_FontAsset Font(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureFonts();
            return _fonts.TryGetValue(name, out var found) ? found : null;
        }

        /// <summary>Forget the scan. For a pack being reloaded without
        /// restarting, when a scene change may have brought new assets in.</summary>
        public static void Reset()
        {
            _sprites = null;
            _fonts = null;
        }

        // ── The scans ────────────────────────────────────────────────

        private static void EnsureSprites()
        {
            if (_sprites != null) return;
            var found = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || string.IsNullOrEmpty(sprite.name)) continue;
                // First wins. Later duplicates are the same art under the same
                // name, and picking between them is not something a name can do.
                if (!found.ContainsKey(sprite.name)) found[sprite.name] = sprite;
            }
            _sprites = found;
        }

        private static void EnsureFonts()
        {
            if (_fonts != null) return;
            var found = new Dictionary<string, TMP_FontAsset>(StringComparer.OrdinalIgnoreCase);
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (font == null || string.IsNullOrEmpty(font.name)) continue;
                if (!found.ContainsKey(font.name)) found[font.name] = font;
            }
            _fonts = found;
        }

        // ── The pack's own art ───────────────────────────────────────

        private static readonly Dictionary<string, Sprite> PackSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A sprite the pack ships, named by its path inside the
        /// archive. Cached per name, including the misses, so a typo does not
        /// cost an archive read for every object that repeats it.</summary>
        private static Sprite FromPack(string name, PackManifest pack)
        {
            if (pack == null || name.IndexOf('.') < 0) return null;   // not a file name

            string key = pack.PackId + "|" + name;
            if (PackSprites.TryGetValue(key, out var cached)) return cached;

            Sprite made = null;
            try
            {
                var bytes = pack.Archive?.ReadBytes(name);
                if (bytes != null && bytes.Length > 0)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                    if (texture.LoadImage(bytes))
                        made = UnityEngine.Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f), 100f);
                }
            }
            catch { /* a broken file draws nothing, which is visible; a throw is not */ }

            PackSprites[key] = made;
            return made;
        }

        public static void ClearPackSprites() => PackSprites.Clear();
    }
}
