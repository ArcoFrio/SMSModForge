namespace SMSModForge.Shared
{
    /// <summary>
    /// The version of ModForge itself — the editor and the runtime plugin,
    /// which ship together and are the same number.
    /// <para/>
    /// One constant compiled into both, because they are two assemblies making
    /// a claim about each other: a pack records the ModForge that wrote it, and
    /// the runtime decides whether it can honour that. Two copies of the number
    /// drifting apart would make every one of those judgements wrong at once.
    /// <para/>
    /// The comparison lives here too rather than in the editor's
    /// <c>PackVersion</c>, which uses language features the runtime's older
    /// compiler does not have. A test pins the two parsers against each other,
    /// so "1.2.0" cannot mean one thing in the editor and another in the game.
    /// </summary>
    public static class ForgeVersion
    {
        /// <summary>
        /// This build of ModForge.
        /// <para/>
        /// Keep in step with the two csproj <c>Version</c> elements; a test
        /// fails if they part company.
        /// </summary>
        public const string Current = "1.1.0";

        /// <summary>How a pack stands relative to the ModForge reading it.</summary>
        public enum Standing
        {
            /// <summary>No stamp, or one that is not a version. Packs written
            /// before ModForge recorded this land here, and nothing is claimed
            /// about them.</summary>
            Unknown,

            /// <summary>Written by this ModForge.</summary>
            Matches,

            /// <summary>Written by an older ModForge than the one reading it.</summary>
            PackIsOlder,

            /// <summary>Written by a NEWER ModForge. It may use things this one
            /// has never heard of.</summary>
            PackIsNewer,
        }

        /// <summary>
        /// Read a version: three whole numbers separated by dots, or null.
        /// <para/>
        /// Strict, and deliberately the same strictness the editor applies to a
        /// pack's own version. Something that quietly parsed as a version it is
        /// not would produce a confident judgement about nothing.
        /// </summary>
        public static int[] Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string[] parts = text.Trim().Split('.');
            if (parts.Length != 3) return null;

            var made = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (parts[i].Length == 0) return null;

                int value = 0;
                for (int c = 0; c < parts[i].Length; c++)
                {
                    char ch = parts[i][c];
                    if (ch < '0' || ch > '9') return null;
                    value = value * 10 + (ch - '0');
                }
                made[i] = value;
            }
            return made;
        }

        /// <summary>
        /// Compare two versions: negative when <paramref name="left"/> is
        /// older, zero when they are the same, positive when it is newer.
        /// Returns 0 when either cannot be read, so an unreadable stamp never
        /// produces a verdict.
        /// </summary>
        public static int Compare(string left, string right)
        {
            int[] a = Parse(left);
            int[] b = Parse(right);
            if (a == null || b == null) return 0;

            for (int i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;

            return 0;
        }

        /// <summary>Where a pack's stamp puts it against this ModForge.</summary>
        public static Standing Judge(string packForgeVersion)
        {
            return Judge(packForgeVersion, Current);
        }

        /// <summary>
        /// Where a pack's stamp puts it against a given ModForge.
        /// <para/>
        /// The runtime version is a parameter so this can be tested at
        /// versions nobody has shipped.
        /// </summary>
        public static Standing Judge(string packForgeVersion, string runtimeVersion)
        {
            if (Parse(packForgeVersion) == null || Parse(runtimeVersion) == null)
                return Standing.Unknown;

            int order = Compare(packForgeVersion, runtimeVersion);
            if (order == 0) return Standing.Matches;
            return order > 0 ? Standing.PackIsNewer : Standing.PackIsOlder;
        }
    }
}
