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

        /// <summary>A scene's art: 256x256 at 100 pixels per unit — the size
        /// the prototype's Core/Art is cut for, and what every scene in this
        /// game is drawn at.</summary>
        public const float ScenePixels = 256f;
        public const float ScenePpu = 100f;

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
        public static Sprite Create(Texture2D tex, float frameW, float frameH, float framePpu,
                                    SpriteMeshType mesh = SpriteMeshType.Tight)
        {
            if (tex == null) return null;
            float ppu = framePpu * ScaleOf(tex, frameW, frameH);
            // Sprite.Create rejects a non-positive pixelsPerUnit outright.
            if (ppu <= 0f) ppu = framePpu;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), ppu, 0, mesh);
        }

        /// <summary>
        /// A bust: drawn over its whole picture (<see cref="MovingMesh"/>).
        /// </summary>
        public static Sprite CreateBust(Texture2D tex)
            => Create(tex, BustPixels, BustPixels, BustPpu, MovingMesh(tex));

        /// <summary>
        /// The mesh of a sprite whose shader moves the picture inside it (a
        /// bust's, an NPC's jiggle): the whole picture (1.9.1).
        /// <para/>
        /// Unity draws a sprite only inside the mesh it is given, and
        /// Sprite.Create's own (Tight) is a coarse outline about two texels
        /// outside the art's opaque pixels. The jiggle moves the picture, not
        /// the mesh, so wherever it carried the art out past that outline the
        /// art was cut off - by a shape nobody could see or predict, and which
        /// the editor's previews, drawing the whole picture, never cut.
        /// Measured 2026-11-16 (in-game suite S3, the noise outfit): the art's
        /// edge moved out of the mesh vanished in the game over a band
        /// beside the bust while the preview drew it. A sprite's bounds are its
        /// rect either way (measured: the figure bust's renderer spans its full
        /// 2.56 units with a 13-point tight mesh), so nothing placed by them
        /// moves. Art whose pixels cannot be read keeps Unity's mesh: its shadow
        /// is cast from that mesh (<see cref="ShadowShapes"/>).
        /// </summary>
        public static SpriteMeshType MovingMesh(Texture2D tex)
            => tex != null && tex.isReadable ? SpriteMeshType.FullRect : SpriteMeshType.Tight;

        public static Sprite CreateLevel(Texture2D tex)
            => Create(tex, LevelWidth, LevelHeight, LevelPpu);

        /// <summary>
        /// A scene's art, at the size a 256x256 scene occupies.
        /// <para/>
        /// Scene art went through a bare Sprite.Create with no
        /// pixels-per-unit at all, so it took Unity's default of 100 whatever
        /// the file's dimensions were - and a 512x512 scene drew at twice the
        /// size of a 256x256 one. At 256 this is the identical sprite it
        /// always made, which is why no existing pack moves.
        /// </summary>
        public static Sprite CreateScene(Texture2D tex)
            => Create(tex, ScenePixels, ScenePixels, ScenePpu);
    }
}
