using System;
using System.IO;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The tutorials' practice art, which the editor and the plugin both carry (1.10.0). The author: "I've seen
    /// cases of modpacks packed with the tutorial art. Maybe we can make them default assets packed within the
    /// plugin instead?" The tutorials still copy it into the practice pack's <see cref="Folder"/>, so the editor's
    /// previews and file pickers work as for any art of the pack's - but an export leaves out every file there that
    /// is the shipped file unchanged, and the plugin reads a path under <see cref="Folder"/> that the pack does not
    /// hold from its own copy. A file an author changed, or added there, is the pack's and ships as any other.
    /// </summary>
    public static class TutorialArt
    {
        /// <summary>The folder in a pack the tutorials' art is copied into, as the pack's paths name it.</summary>
        public const string Folder = "TutorialArt";

        /// <summary>Beside the plugin: BepInEx/plugins/SMSModForge/TutorialArt, the same tree as the pack's.</summary>
        public const string PluginFolder = "SMSModForge";

        /// <summary>The path inside <see cref="Folder"/> a pack path means ("TutorialArt/Busts/Bust1/Mouth1.png" is
        /// "Busts/Bust1/Mouth1.png"), or null for a path anywhere else. Either slash; a leading "./" is ignored.</summary>
        public static string Inside(string packPath)
        {
            if (string.IsNullOrEmpty(packPath)) return null;
            string p = packPath.Replace('\\', '/');
            if (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            string prefix = Folder + "/";
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || p.Length == prefix.Length) return null;
            string inside = p.Substring(prefix.Length);
            // Never out of the folder.
            foreach (string part in inside.Split('/'))
                if (part == ".." || part.Length == 0) return null;
            return inside;
        }

        /// <summary>The file a pack path means in a copy of the art rooted at <paramref name="root"/>, or null when
        /// the path is not under <see cref="Folder"/> or that copy has no such file.</summary>
        public static string FileIn(string root, string packPath)
        {
            string inside = Inside(packPath);
            if (inside == null || string.IsNullOrEmpty(root)) return null;
            string file = Path.Combine(root, inside.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(file) ? file : null;
        }
    }
}
