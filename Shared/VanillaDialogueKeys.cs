namespace SMSModForge.Shared
{
    /// <summary>
    /// The names a vanilla dialogue extension uses on disk.
    /// <para/>
    /// Compiled into both projects from one file, for the same reason the
    /// substitution parser is: the editor writes these keys and the runtime
    /// reads them, and two copies of a name drift the moment one is renamed.
    /// </summary>
    public static class VanillaDialogueKeys
    {
        /// <summary>Marks a dialogue as extending one the game already has,
        /// and names which.</summary>
        public const string Source = "source";

        /// <summary>The fields of a line that the pack actually changed — and
        /// so the only ones written back over the game's own.</summary>
        public const string Overrides = "overrides";

        /// <summary>Ids of vanilla lines the extension takes out. Its own key
        /// because a pruned manifest holds only changes, so a deleted line and
        /// an unchanged one are both simply absent.</summary>
        public const string RemovedNodes = "removedNodes";

        /// <summary>Prefix on the value of <see cref="Source"/>.</summary>
        public const string SourcePrefix = "vanilladialogue:";
    }
}
