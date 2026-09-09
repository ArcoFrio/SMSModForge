namespace SMSModForge.Shared
{
    /// <summary>
    /// The folder packs are installed into, inside the game's own folder.
    /// <para/>
    /// Compiled into both projects from one file, because two things have to
    /// agree about it and they live in different assemblies: the editor writes
    /// a published archive with the pack already inside this folder, and the
    /// runtime looks in this folder to find it. If those two names ever drifted
    /// apart the result would be an archive that installs perfectly into a
    /// folder nothing reads - a pack that is present, correct, and invisible.
    /// </summary>
    public static class ModsFolder
    {
        /// <summary>The folder's name, beside the game executable.</summary>
        public const string Name = "Mods";
    }
}
