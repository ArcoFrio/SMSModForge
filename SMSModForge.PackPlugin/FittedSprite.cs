using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Builds a sprite from art of any size so it occupies the space art of the
    /// EXPECTED size would have occupied.
    /// <para/>
    /// Both bust and level art used to be cut with a hardcoded rect — 256x256
    /// for a bust, 2048x1136 for a level — while the texture underneath took
    /// whatever dimensions the PNG actually had, because
    /// <c>Texture2D.LoadImage</c> resizes to the file. Art larger than expected
    /// therefore showed only the corner the rect happened to cover, and art
    /// smaller than the rect could not be cut at all. Neither said anything: the
    /// editor never checked a PNG's dimensions, so "my bust does not show up"
    /// was the only symptom an author ever got.
    /// <para/>
    /// The fix is to cut the whole texture and change the SCALE instead. A
    /// sprite's world size is pixels / pixelsPerUnit, so multiplying the frame's
    /// pixels-per-unit by how many times too big the art is leaves the world
    /// size where it was. Art at exactly the expected size comes out with the
    /// frame's own pixels-per-unit, i.e. byte-for-byte what it did before.
    /// <para/>
    /// The larger of the two ratios wins, so art of the wrong aspect fits INSIDE
    /// the frame rather than overflowing it. Overflow is the worse failure:
    /// a bust that spills past its frame draws over the interface.
    /// </summary>
    internal static class FittedSprite
    {
        /// <summary>A bust frame: 256x256 at 100 pixels per unit.</summary>
        public const float BustPixels = 256f;
        public const float BustPpu = 100f;

        /// <summary>A level layer: 2048x1136 at the game's own 70.32.</summary>
        public const float LevelWidth = 2048f;
        public const float LevelHeight = 1136f;
        public const float LevelPpu = 70.32f;

        /// <summary>
        /// How far off the expected frame this art is: 1 when it matches, 2 when
        /// it is twice as big, 0.5 when it is half. Exposed so callers can log
        /// the fact — a silently rescaled sprite is still worth mentioning once.
        /// </summary>
        public static float ScaleOf(Texture2D tex, float frameW, float frameH)
        {
            if (tex == null || frameW <= 0f || frameH <= 0f) return 1f;
            float sx = tex.width / frameW;
            float sy = tex.height / frameH;
            float scale = sx > sy ? sx : sy;
            return scale > 0f ? scale : 1f;
        }

        /// <summary>
        /// Cut the whole texture, at the pixels-per-unit that keeps it the size
        /// the frame expects.
        /// </summary>
        public static Sprite Create(Texture2D tex, float frameW, float frameH, float framePpu)
        {
            if (tex == null) return null;
            float ppu = framePpu * ScaleOf(tex, frameW, frameH);
            // Sprite.Create rejects a non-positive pixelsPerUnit outright.
            if (ppu <= 0f) ppu = framePpu;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), ppu);
        }

        public static Sprite CreateBust(Texture2D tex)
            => Create(tex, BustPixels, BustPixels, BustPpu);

        public static Sprite CreateLevel(Texture2D tex)
            => Create(tex, LevelWidth, LevelHeight, LevelPpu);
    }
}
