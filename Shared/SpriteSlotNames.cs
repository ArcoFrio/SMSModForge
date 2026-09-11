namespace SMSModForge.Shared
{
    /// <summary>
    /// The textures a bust has, as a pack spells them when it replaces one.
    /// <para/>
    /// Compiled into both projects for the usual reason, and here the reason
    /// bites harder than most: the editor writes these strings, the validator
    /// reads them, and the runtime matches on them. A typo in any one of the
    /// three is not an error anybody sees — it is a texture that silently never
    /// gets replaced, on somebody else's machine.
    /// <para/>
    /// A face is namespaced (<c>expression:Happy</c>) so it can never collide
    /// with a fixed slot, and so a face the PACK added to one of the game's
    /// characters is named the same way as one the game already had.
    /// </summary>
    public static class SpriteSlotNames
    {
        /// <summary>The body sprite, on the bust's own sprite root.</summary>
        public const string Base = "base";

        /// <summary>The jiggle mask. Not a sprite: a data texture on the
        /// material, which is why the runtime handles it apart from the
        /// rest.</summary>
        public const string Mask = "mask";

        /// <summary>The eyes-closed overlay.</summary>
        public const string Blink = "blink";

        /// <summary>The four mouth frames, in the order the animation plays
        /// them.</summary>
        public static readonly string[] Mouth = { "mouth1", "mouth2", "mouth3", "mouth4" };

        /// <summary>What a face's slot name starts with.</summary>
        public const string ExpressionPrefix = "expression:";

        /// <summary>Every slot that is not a face, in the order the editor
        /// shows them.</summary>
        public static readonly string[] Fixed =
        {
            Base, Mask, Blink, "mouth1", "mouth2", "mouth3", "mouth4",
        };

        /// <summary>The slot name for one face.</summary>
        public static string Expression(string face)
        {
            return ExpressionPrefix + (face ?? "");
        }

        /// <summary>The face this slot names, or null when it names something
        /// else. A fixed slot is never a face, however much "blink" reads like
        /// one.</summary>
        public static string ExpressionOf(string slot)
        {
            if (slot == null) return null;
            if (!slot.StartsWith(ExpressionPrefix, System.StringComparison.Ordinal)) return null;
            return slot.Substring(ExpressionPrefix.Length);
        }

        /// <summary>The mouth frame this slot names (1-4), or 0 when it names
        /// something else.</summary>
        public static int MouthFrame(string slot)
        {
            for (int i = 0; i < Mouth.Length; i++)
                if (string.Equals(slot, Mouth[i], System.StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            return 0;
        }

        /// <summary>What to call a slot on screen.</summary>
        public static string Label(string slot)
        {
            string face = ExpressionOf(slot);
            if (face != null) return face;

            int frame = MouthFrame(slot);
            if (frame > 0) return "Mouth " + frame;

            if (slot == Base) return "Base";
            if (slot == Mask) return "Jiggle mask";
            if (slot == Blink) return "Blink";
            return slot ?? "";
        }
    }
}
