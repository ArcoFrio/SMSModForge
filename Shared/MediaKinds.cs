namespace SMSModForge.Shared
{
    /// <summary>
    /// What kind of art a path names, and where an animation's frames live.
    /// <para/>
    /// Compiled into both projects from one file. The editor decides a file is
    /// animated and writes frames beside it; the runtime has to reach the same
    /// conclusion and find the same folder, and two copies of "which extensions
    /// animate" would drift the first time one grew a format.
    /// <para/>
    /// The KIND comes from the extension rather than from the bytes, on
    /// purpose: it is what an author controls and what they expect. Rename a
    /// thing to .png and it should be treated as a still, not quietly animated
    /// because its header says otherwise.
    /// </summary>
    public static class MediaKinds
    {
        public const int Still = 0;
        public const int Gif = 1;
        public const int Video = 2;
        public const int Unknown = 3;

        /// <summary>Where a GIF's decoded frames sit, relative to the pack, and
        /// what the file listing their delays is called.</summary>
        public const string FramesSuffix = ".frames";
        public const string FramesManifest = "frames.json";

        // The extensions themselves, listed rather than buried in a switch,
        // because more than one thing needs them: what KindOf answers, and what
        // the editor's file picker offers. Those two disagreeing is a file the
        // tool accepts but will not let anybody choose - which is exactly what
        // happened, and it took a person noticing the picker to find it.
        //
        // Treat these as constants. They are arrays because this file compiles
        // into the runtime plugin too, on an older language version.

        /// <summary>Extensions that name a single picture.</summary>
        public static readonly string[] StillExtensions = { ".png", ".jpg", ".jpeg" };

        /// <summary>Extensions decoded to frames and played by swapping them.</summary>
        public static readonly string[] GifExtensions = { ".gif" };

        /// <summary>
        /// Extensions played by the engine's own video player.
        /// <para/>
        /// Only what it can actually play. A format that loads on the author's
        /// machine and not in the game is the worst outcome available, so .avi
        /// and .mkv are deliberately absent.
        /// </summary>
        public static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".webm" };

        /// <summary>What kind of art this path names.</summary>
        public static int KindOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return Unknown;

            int dot = path.LastIndexOf('.');
            if (dot < 0 || dot == path.Length - 1) return Unknown;

            string ext = path.Substring(dot).ToLowerInvariant();
            if (Lists(StillExtensions, ext)) return Still;
            if (Lists(GifExtensions, ext)) return Gif;
            if (Lists(VideoExtensions, ext)) return Video;
            return Unknown;
        }

        private static bool Lists(string[] extensions, string ext)
        {
            for (int i = 0; i < extensions.Length; i++)
                if (extensions[i] == ext) return true;
            return false;
        }

        public static bool IsAnimated(string path)
        {
            int kind = KindOf(path);
            return kind == Gif || kind == Video;
        }

        /// <summary>
        /// The folder holding a GIF's decoded frames: "Scenes/dance.gif"
        /// becomes "Scenes/dance.frames".
        /// <para/>
        /// Derived rather than stored, so the manifest keeps naming the file
        /// the author actually picked and nothing has to be kept in step with
        /// anything.
        /// </summary>
        public static string FramesFolderFor(string gifRel)
        {
            if (string.IsNullOrEmpty(gifRel)) return "";
            int dot = gifRel.LastIndexOf('.');
            return (dot < 0 ? gifRel : gifRel.Substring(0, dot)) + FramesSuffix;
        }

        /// <summary>One frame's file name. Zero-padded so a plain sort is the
        /// play order, whatever lists the folder.</summary>
        public static string FrameName(int index)
        {
            return index.ToString("D4") + ".png";
        }
    }
}
