using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Which installed pack files actually load, when more than one claims the
    /// same pack.
    /// <para/>
    /// Packs are read from two folders — <see cref="ModsFolder.Name"/> beside
    /// the game, and the older location beside the plugin — so the same pack
    /// sitting in both is the normal shape of a half-finished move. Loading it
    /// twice would register everything in it twice; loading one and saying
    /// nothing means an author who edited the copy that lost spends an evening
    /// wondering why their changes do nothing.
    /// <para/>
    /// Two files in the SAME folder can collide too, since the pack id lives
    /// inside the archive and has nothing to do with the file name. A renamed
    /// spare copy is exactly how that happens.
    /// <para/>
    /// Compiled into both projects: the runtime resolves this to decide what to
    /// load and what to say on the menu, and the editor's test project compiles
    /// it so the rule can be checked without starting a game.
    /// </summary>
    public static class PackInstallScan
    {
        /// <summary>One pack file found on disk.</summary>
        public sealed class Candidate
        {
            /// <summary>Full path to the file.</summary>
            public string Path;

            /// <summary>The id inside it, or empty when it could not be read.</summary>
            public string PackId;

            public Candidate() { }

            public Candidate(string path, string packId)
            {
                Path = path;
                PackId = packId;
            }
        }

        /// <summary>A pack that will load, and anything it is shadowing.</summary>
        public sealed class Resolved
        {
            /// <summary>The file that wins.</summary>
            public string Path;

            /// <summary>Its pack id.</summary>
            public string PackId;

            /// <summary>Other files claiming the same id, which are ignored.
            /// Empty for the ordinary case.</summary>
            public List<string> Shadowed = new List<string>();

            /// <summary>Whether anything is being ignored because of this one.</summary>
            public bool IsDuplicated { get { return Shadowed.Count > 0; } }
        }

        /// <summary>
        /// Decide what loads.
        /// <para/>
        /// <paramref name="candidates"/> must already be in priority order —
        /// the caller knows which folder wins, this only knows that earlier
        /// beats later. The first file claiming an id keeps it; every later one
        /// is recorded against it rather than dropped, so the reason can be
        /// shown to somebody.
        /// <para/>
        /// A candidate with no readable id is passed through untouched: it is
        /// not a duplicate of anything, it is a broken file, and the caller
        /// already has a way to say so.
        /// </summary>
        public static List<Resolved> Resolve(IList<Candidate> candidates)
        {
            var resolved = new List<Resolved>();
            if (candidates == null) return resolved;

            var byId = new Dictionary<string, Resolved>(
                System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < candidates.Count; i++)
            {
                var one = candidates[i];
                if (one == null || string.IsNullOrEmpty(one.Path)) continue;

                if (string.IsNullOrEmpty(one.PackId))
                {
                    resolved.Add(new Resolved { Path = one.Path, PackId = "" });
                    continue;
                }

                Resolved winner;
                if (byId.TryGetValue(one.PackId, out winner))
                {
                    winner.Shadowed.Add(one.Path);
                    continue;
                }

                winner = new Resolved { Path = one.Path, PackId = one.PackId };
                byId[one.PackId] = winner;
                resolved.Add(winner);
            }

            return resolved;
        }

        /// <summary>
        /// The name of the folder a file sits in — "Mods" or "ModPacks" — for a
        /// message that has to tell somebody WHICH copy is live without pasting
        /// a full path into a menu.
        /// </summary>
        public static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                return string.IsNullOrEmpty(dir) ? "" : System.IO.Path.GetFileName(dir);
            }
            catch (System.ArgumentException) { return ""; }
        }
    }
}
