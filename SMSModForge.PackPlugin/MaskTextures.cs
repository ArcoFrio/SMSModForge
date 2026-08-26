using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The mask a sprite gets when the pack authored none.
    /// <para/>
    /// An unset mask field means "this sprite has no jiggle", which is a
    /// perfectly ordinary thing for a pack to want and used to be impossible to
    /// express: both factories required the mask to exist in the archive and
    /// skipped the whole bust or place when it did not, so a character or room
    /// with no mask art simply never appeared in the game.
    /// <para/>
    /// Leaving <c>_MaskTex</c> unassigned is not the fix. Unity hands an unset
    /// texture sampler its default, which is white — alpha 1 across the whole
    /// sprite, i.e. the MAXIMUM effect everywhere. The absence of a mask would
    /// read as "displace all of it", which is the opposite of what an empty
    /// field means.
    /// <para/>
    /// So an explicit transparent black is bound instead. The shader's gate is
    /// <c>mask.a * (r + g + b) &gt; 0.001</c> (see <c>OverlayJiggle</c>), and
    /// this fails it on both halves — it reads as "none" whether a given effect
    /// samples alpha or the colour channels.
    /// </summary>
    internal static class MaskTextures
    {
        private static Texture2D _none;

        /// <summary>
        /// A 1x1 fully transparent mask, shared by every sprite that has none.
        /// <para/>
        /// One pixel is enough: the sampler clamps, so every UV in the sprite
        /// lands on it. Cached because this is bound per outfit and per level,
        /// and marked <see cref="HideFlags.HideAndDontSave"/> so a scene change
        /// cannot destroy the copy the next pack is still pointing at.
        /// </summary>
        internal static Texture2D None()
        {
            if (_none != null) return _none;

            // Linear, like every other mask here: it carries amounts, not colour.
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            tex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;

            _none = tex;
            return _none;
        }

        /// <summary>
        /// Whether <paramref name="rel"/> names a mask the pack actually ships.
        /// An empty path is a deliberate "no mask"; a path that is set but
        /// absent from the archive is a typo, and callers still log that.
        /// </summary>
        internal static bool IsAuthored(PackManifest pack, string rel)
            => !string.IsNullOrEmpty(rel) && pack != null && pack.Has(rel);
    }
}
