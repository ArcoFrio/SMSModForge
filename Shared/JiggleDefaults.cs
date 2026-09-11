namespace SMSModForge.Shared
{
    /// <summary>
    /// What a bust moves like when nobody has said otherwise.
    /// <para/>
    /// Compiled into both projects from one file, for the reason every other
    /// file in here is: the editor previews a bust with these numbers and the
    /// runtime has to reproduce them, and two copies drift the first time one is
    /// corrected. They were two copies until now — the editor's in
    /// <c>JiggleParams</c>'s initialisers, the runtime's in
    /// <c>BustFactory.ApplyJiggle</c>'s null-coalescing fallbacks — agreeing
    /// only because nobody had yet changed one of them.
    /// <para/>
    /// The numbers are not arbitrary. Of the game's 318 busts, the single most
    /// common configuration is exactly this one — 78 of them — so a bust that
    /// has never been touched starts where most of the cast already is.
    /// </summary>
    public static class JiggleDefaults
    {
        /// <summary>How fast the movement cycles.</summary>
        public const float Speed = 3.0f;

        /// <summary>How far it displaces. Negative pulls inward.</summary>
        public const float Strength = -0.02f;

        /// <summary>How many waves fit across the sprite.</summary>
        public const float Frequency = 4.0f;

        /// <summary>The size of the noise cells.</summary>
        public const float NoiseScale = 5.0f;

        /// <summary>How fast the noise field drifts.</summary>
        public const float NoiseSpeed = 0.5f;

        /// <summary>How far the noise displaces.</summary>
        public const float NoiseStrength = 0.06f;

        /// <summary>Whether to snap to whole pixels.</summary>
        public const bool PixelSnap = false;
    }
}
