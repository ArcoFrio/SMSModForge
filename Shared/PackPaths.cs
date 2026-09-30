using System;
using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Finding a file a pack names inside the pack, when the name is a full
    /// path on the author's machine rather than one inside the pack.
    /// <para/>
    /// Every field is meant to hold a path inside the pack - "Wallpapers/Elf.png".
    /// A text box takes whatever is typed or pasted, though, and an author who
    /// pasted "Z:\Modding\Elfenlied\Wallpapers\Elf.png" shipped a pack whose
    /// image IS in it, at Wallpapers/Elf.png, under a name nothing in the pack
    /// matched: the wallpaper was skipped, and its button never appeared (an
    /// author's report, 2026-09-27). The pack's own files are the only ones a
    /// player has, so a full path is read by its end: the longest file in the
    /// pack that the path ends with, on a folder boundary.
    /// </summary>
    public static class PackPaths
    {
        /// <summary>
        /// Whether <paramref name="path"/> is a full path on somebody's machine
        /// - "Z:\...", "Z:/...", "\server\..." or "/..." - rather than one
        /// inside a pack.
        /// </summary>
        public static bool IsFullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return true;
            return path[0] == '\\' || path[0] == '/';
        }

        /// <summary>
        /// Whether a path inside the pack climbs out of it: "../Art/Elf.png",
        /// or "Art/../../Elf.png" - more steps up than down, at any point. Such
        /// a file is not in the pack, and a player never has it (1.6.3).
        /// </summary>
        public static bool LeavesThePack(string path)
        {
            if (string.IsNullOrEmpty(path) || IsFullPath(path)) return false;
            int depth = 0;
            foreach (string part in path.Split('/', '\\'))
            {
                if (part.Length == 0 || part == ".") continue;
                depth += part == ".." ? -1 : 1;
                if (depth < 0) return true;
            }
            return false;
        }

        /// <summary>
        /// The file in the pack <paramref name="fullPath"/> names: of
        /// <paramref name="inPack"/> (paths inside the pack, forward slashes), the
        /// longest the full path ends with, following a folder separator. Null
        /// when none does - or when the path is not a full one, which is looked
        /// up as it is.
        /// </summary>
        public static string FindByEnding(string fullPath, IEnumerable<string> inPack)
        {
            if (!IsFullPath(fullPath) || inPack == null) return null;
            string wanted = fullPath.Replace('\\', '/');
            string best = null;
            foreach (string candidate in inPack)
            {
                if (string.IsNullOrEmpty(candidate)) continue;
                string c = candidate.Replace('\\', '/').TrimStart('/');
                if (c.Length == 0 || c.Length >= wanted.Length) continue;
                if (!wanted.EndsWith(c, StringComparison.OrdinalIgnoreCase)) continue;
                if (wanted[wanted.Length - c.Length - 1] != '/') continue;
                if (best == null || c.Length > best.Length) best = candidate;
            }
            return best;
        }
    }
}
