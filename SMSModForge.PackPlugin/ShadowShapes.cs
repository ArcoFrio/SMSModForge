using System;
using System.Collections.Generic;
using System.Reflection;
using SMSModForge.Shared;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Gives a pack bust's 2D shadow the shape of the art it now draws.
    /// <para/>
    /// The game's busts carry a ShadowCaster2D: the shadow the room's lamps
    /// throw on the wall behind a character. Every pack bust is a clone of a
    /// game bust whose sprite is replaced while the clone is switched off, so
    /// the caster never heard of it: until 1.9.1 every pack bust threw the game
    /// bust's shadow (Anna's, for most), and a pack's art over a game bust kept
    /// the old shadow too.
    /// <para/>
    /// The shape is now <see cref="ShadowOutline"/>'s, made from the art, given
    /// to the caster as its own path - URP's "shape editor" source, which casts
    /// a path as it is. Left to make it from the sprite, URP would use Unity's
    /// own mesh of the picture and trim it, both inside the engine where the
    /// editor's preview cannot follow; with the path, the preview casts exactly
    /// the shadow the game does.
    /// <para/>
    /// A clone also shares its shape's Mesh with the bust it was cloned from -
    /// rebuilding it in place gave the game's own bust the pack art's shadow -
    /// so the clone is given a mesh of its own first (URP makes a new one when
    /// it finds none). The caster rebuilds in its next Update, whenever the
    /// bust is next shown. Art with nothing left once its edge is pulled in
    /// casts no shadow at all, in the game as in the preview.
    /// <para/>
    /// URP keeps every member used here internal and the plugin does not
    /// reference URP, so they are reached by name; GAME_UPDATE_CHECKS.md lists
    /// them. If a game update renames one, this logs once and leaves the caster
    /// as the game made it - the old shadow, never a broken one.
    /// </summary>
    internal static class ShadowShapes
    {
        private const string CasterType = "ShadowCaster2D";
        private const string ShapeField = "m_ShadowMesh", MeshField = "m_Mesh", RebuildField = "m_ForceShadowMeshRebuild",
                             SourceField = "m_ShadowCastingSource", PathField = "m_ShapePath";
        /// <summary>ShadowCaster2D.ShadowCastingSources.ShapeEditor.</summary>
        private const int ShapeEditor = 1, ShapeProvider = 2;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static FieldInfo _shape, _mesh, _rebuild, _source, _path;
        private static bool _looked, _missing;
        /// <summary>Casters already given a mesh of their own: once is enough, and
        /// a second new mesh would orphan the first.</summary>
        private static readonly HashSet<int> _ownMesh = new HashSet<int>();
        /// <summary>Casters switched off here for art with no outline.</summary>
        private static readonly HashSet<int> _switchedOff = new HashSet<int>();
        /// <summary>One outline per sprite: busts share their art.</summary>
        private static readonly Dictionary<int, Vector3[]> _outlines = new Dictionary<int, Vector3[]>();

        /// <summary>Give the shadow caster beside <paramref name="sr"/>, if it has
        /// one, the outline of the sprite it draws now.</summary>
        internal static void Refresh(SpriteRenderer sr)
        {
            if (sr == null) return;
            foreach (var c in sr.GetComponents<Component>())
            {
                if (c == null || c.GetType().Name != CasterType) continue;
                if (!Find(c)) return;
                int id = c.GetInstanceID();
                var shape = _shape.GetValue(c);
                if (shape != null && _ownMesh.Add(id)) _mesh.SetValue(shape, null);
                var path = Outline(sr.sprite);
                var behaviour = c as Behaviour;
                if (path == null)
                {
                    // A picture that cannot be read (one of the game's own): URP
                    // makes the shape from the sprite, as for the game's busts.
                    _source.SetValue(c, Enum.ToObject(_source.FieldType, ShapeProvider));
                    _rebuild.SetValue(c, true);
                    continue;
                }
                if (path.Length < 3)
                {
                    if (behaviour != null && behaviour.enabled) { behaviour.enabled = false; _switchedOff.Add(id); }
                    continue;
                }
                if (behaviour != null && _switchedOff.Remove(id)) behaviour.enabled = true;
                _path.SetValue(c, path);
                _source.SetValue(c, Enum.ToObject(_source.FieldType, ShapeEditor));
                _rebuild.SetValue(c, true);
            }
        }

        /// <summary>The sprite's outline in its own units, as the caster takes a
        /// path (null when the picture cannot be read).</summary>
        private static Vector3[] Outline(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;
            int key = sprite.GetInstanceID();
            if (_outlines.TryGetValue(key, out var known)) return known;
            Vector3[] path = null;
            var tex = sprite.texture;
            if (tex.isReadable)
            {
                // The whole picture, which the pivot is measured in: textureRect is
                // Unity's tight crop of it (6 texels in from the suite's art).
                var rect = sprite.rect;
                int x0 = (int)rect.x, y0 = (int)rect.y, w = (int)rect.width, h = (int)rect.height;
                var px = tex.GetPixels32();
                var alpha = new byte[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        alpha[y * w + x] = px[(y0 + y) * tex.width + x0 + x].a;
                var corners = ShadowOutline.Of(alpha, w, h, sprite.pixelsPerUnit);
                float ppu = sprite.pixelsPerUnit;
                var pivot = sprite.pivot;
                path = new Vector3[corners.Length / 2];
                for (int i = 0; i < path.Length; i++)
                    path[i] = new Vector3((corners[2 * i] - pivot.x) / ppu, (corners[2 * i + 1] - pivot.y) / ppu, 0f);
            }
            _outlines[key] = path;
            return path;
        }

        private static bool Find(Component caster)
        {
            if (!_looked)
            {
                _looked = true;
                var type = caster.GetType();
                _shape = type.GetField(ShapeField, Any);
                _rebuild = type.GetField(RebuildField, Any);
                _source = type.GetField(SourceField, Any);
                _path = type.GetField(PathField, Any);
                _mesh = _shape?.FieldType.GetField(MeshField, Any);
                _missing = _shape == null || _mesh == null || _mesh.FieldType != typeof(Mesh)
                           || _rebuild == null || _rebuild.FieldType != typeof(bool)
                           || _source == null || !_source.FieldType.IsEnum
                           || _path == null || _path.FieldType != typeof(Vector3[]);
                if (_missing)
                    Plugin.Log?.LogWarning("[SMSModForge.PackPlugin] The game's " + CasterType + " is not as expected (" + ShapeField + "." + MeshField +
                                           ", " + RebuildField + ", " + SourceField + ", " + PathField + "); pack busts keep the shadow of the game bust they were made from.");
            }
            return !_missing;
        }
    }
}
