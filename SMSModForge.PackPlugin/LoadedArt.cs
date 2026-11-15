using System;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// One texture, and one sprite of it, per picture of a pack and per way it is
    /// made, while the packs load (1.9.0, the performance pass).
    /// <para/>
    /// Every outfit, mouth, face and level layer decoded its own copy of the PNG
    /// it showed: in a large pack 1,408 bust pictures decoded for 382 files, and
    /// 94 level layers for 40, each a full readable copy in memory and on the
    /// graphics card. Within one load the same file made the same way is the
    /// same picture, so it is decoded once and shared.
    /// <para/>
    /// Only during a load (<see cref="Begin"/> to <see cref="End"/>): art loaded
    /// at any other time is made as it always was. Nothing changes a texture or
    /// a sprite once made, and nothing tells two apart by identity; anything that
    /// ever does must not take them from here.
    /// </para>
    /// </summary>
    internal static class LoadedArt
    {
        private static Dictionary<string, Texture2D> _textures;
        private static Dictionary<string, Sprite> _sprites;

        /// <summary>Share pictures until <see cref="End"/>.</summary>
        public static void Begin()
        {
            _textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        }

        /// <summary>Stop sharing: what was made stays where it was put.</summary>
        public static void End()
        {
            _textures = null;
            _sprites = null;
        }

        /// <summary>The key of one picture made one way, or null when it cannot be
        /// shared (no load under way, or a pack with no archive to name it by).</summary>
        private static string Key(PackManifest pack, string rel, string recipe)
        {
            if (_textures == null || pack?.Archive == null || string.IsNullOrEmpty(rel)) return null;
            return pack.Archive.SourcePath + "|" + rel + "|" + recipe;
        }

        /// <summary>The texture made by <paramref name="make"/> for this picture and
        /// recipe - made once per load.</summary>
        public static Texture2D Texture(PackManifest pack, string rel, string recipe, Func<Texture2D> make)
        {
            string key = Key(pack, rel, recipe);
            if (key == null) return make();
            if (_textures.TryGetValue(key, out var known) && known != null) return known;
            var made = make();
            _textures[key] = made;
            return made;
        }

        /// <summary>The sprite made by <paramref name="make"/> for this picture and
        /// recipe - made once per load.</summary>
        public static Sprite Sprite(PackManifest pack, string rel, string recipe, Func<Sprite> make)
        {
            string key = Key(pack, rel, recipe);
            if (key == null) return make();
            if (_sprites.TryGetValue(key, out var known) && known != null) return known;
            var made = make();
            _sprites[key] = made;
            return made;
        }
    }
}
