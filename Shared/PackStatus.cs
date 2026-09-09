using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What is worth saying about an installed pack, and how loudly.
    /// <para/>
    /// The menu banner is the only place a player ever learns that something is
    /// off, so the decision behind it — which colour, which words — is worth
    /// having somewhere it can be checked. It used to live inside the runtime,
    /// which loads into a game this repository cannot start, so nothing
    /// verified it beyond the fact that it compiled.
    /// <para/>
    /// Nothing here stops a pack loading. Every one of these is attempted
    /// anyway: a pack that half-works is more use than a pack that refuses, and
    /// the row is what explains the difference.
    /// </summary>
    public static class PackStatus
    {
        /// <summary>How loudly to say it.</summary>
        public enum Level
        {
            /// <summary>Nothing to report.</summary>
            Fine,

            /// <summary>It works, but it is not quite what you think.</summary>
            Warning,

            /// <summary>It will not work as authored.</summary>
            Error,
        }

        /// <summary>Everything known about one installed pack.</summary>
        public sealed class Facts
        {
            /// <summary>False when the archive would not open, or carries no
            /// pack id.</summary>
            public bool Readable = true;

            /// <summary>The game build the pack was authored against ("" when
            /// it predates the stamp).</summary>
            public string GameVersion = "";

            /// <summary>The game build actually running ("" when it could not
            /// be read off the menu).</summary>
            public string RunningGameVersion = "";

            /// <summary>The ModForge that wrote the pack ("" when it predates
            /// the stamp).</summary>
            public string ForgeVersion = "";

            /// <summary>The ModForge doing the reading.</summary>
            public string RuntimeForgeVersion = ForgeVersion_Current();

            /// <summary>Folder the live copy sits in.</summary>
            public string Folder = "";

            /// <summary>Folder holding another copy of the same pack, or "".</summary>
            public string ShadowedIn = "";

            private static string ForgeVersion_Current()
            {
                return Shared.ForgeVersion.Current;
            }
        }

        /// <summary>The verdict: how loud, and what to append to the pack's name.</summary>
        public sealed class Report
        {
            public Level Level = Level.Fine;

            /// <summary>Short phrases shown after the pack's name, worst first.</summary>
            public List<string> Tags = new List<string>();
        }

        /// <summary>
        /// Judge one pack.
        /// <para/>
        /// Every applicable tag is added, because a pack can be several things
        /// at once — built for an older ModForge AND installed twice — and
        /// mentioning only the worst would send somebody to fix one problem
        /// while the other stayed. The level is the worst of them.
        /// </summary>
        public static Report Of(Facts facts)
        {
            var report = new Report();
            if (facts == null) return report;

            // An archive that will not open tells us nothing else about
            // itself, so nothing else is guessed at.
            if (!facts.Readable)
            {
                report.Level = Level.Error;
                report.Tags.Add("Could not be read");
                return report;
            }

            // A pack built against another build of the game. Named as the
            // GAME's version because the row can carry a ModForge complaint
            // beside it, and "Incompatible (1.7A)" next to "Needs ModForge
            // 1.3.0" leaves somebody working out which number is which.
            //
            // Only when both stamps exist: a pack from before the stamp, or a
            // game whose version could not be read, is not evidence of a
            // mismatch.
            if (!string.IsNullOrEmpty(facts.GameVersion) &&
                !string.IsNullOrEmpty(facts.RunningGameVersion) &&
                !string.Equals(facts.GameVersion, facts.RunningGameVersion,
                               System.StringComparison.OrdinalIgnoreCase))
            {
                report.Level = Level.Error;
                report.Tags.Add("Incompatible game version (" + facts.GameVersion + ")");
            }

            switch (ForgeVersion.Judge(facts.ForgeVersion, facts.RuntimeForgeVersion))
            {
                // Something the pack asks for arrived after this runtime was
                // built, so it is genuinely missing.
                case ForgeVersion.Standing.PackIsNewer:
                    report.Level = Level.Error;
                    report.Tags.Add("Needs ModForge " + facts.ForgeVersion);
                    break;

                // Should work, but is not what the tool would write today.
                case ForgeVersion.Standing.PackIsOlder:
                    Raise(report, Level.Warning);
                    report.Tags.Add("Built for ModForge " + facts.ForgeVersion);
                    break;

                // No stamp at all. Worth saying rather than passing over in
                // silence: it means nothing here can tell whether the pack and
                // the runtime agree, and "we cannot check" is a different thing
                // from "we checked and it is fine".
                case ForgeVersion.Standing.Unknown:
                    Raise(report, Level.Warning);
                    report.Tags.Add("No ModForge version recorded");
                    break;
            }

            // Installed twice, with one copy live. This is how somebody edits a
            // file all evening and sees nothing change.
            if (!string.IsNullOrEmpty(facts.ShadowedIn))
            {
                Raise(report, Level.Warning);
                report.Tags.Add("Installed twice, using from " + facts.Folder + " folder");
            }

            return report;
        }

        /// <summary>Raise the level, never lower it.</summary>
        private static void Raise(Report report, Level to)
        {
            if (report.Level < to) report.Level = to;
        }

        /// <summary>What a pack's name is prefixed with on the menu.</summary>
        public const string Bullet = "  • ";

        /// <summary>What a tag, or the continuation of a wrapped line, is
        /// indented by - enough to sit clear of the bullet above it.</summary>
        public const string Indent = "      ";

        /// <summary>
        /// The lines one pack occupies on the menu.
        /// <para/>
        /// One line: the pack, then whatever is wrong with it, reading as a
        /// sentence rather than as a list. It only becomes more than one line
        /// when it will not fit, and then it is wrapped HERE rather than left
        /// to the text engine - which is the point. Whatever lays these out has
        /// to know how many lines it is about to draw in order to leave room
        /// for them; a wrap nobody counted is a wrap that lands on top of
        /// something.
        /// </summary>
        public static List<string> Rows(string packLabel, Report report, int width)
        {
            return Wrap(Bullet + packLabel + Suffix(report), width, Indent);
        }

        /// <summary>
        /// Break text into lines of at most <paramref name="width"/>
        /// characters, at spaces where there is one.
        /// <para/>
        /// A word longer than the whole width is left over-long rather than
        /// cut: a pack id run together without spaces is still more use to
        /// somebody whole than it is chopped in half.
        /// </summary>
        public static List<string> Wrap(string text, int width, string continuation)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            if (width < 1) width = 1;
            if (continuation == null) continuation = "";

            string rest = text;
            while (rest.Length > width)
            {
                int at = rest.LastIndexOf(' ', width);

                // Never break inside the leading indent, or every wrapped line
                // would start a new one and march across the screen.
                if (at <= continuation.Length) at = -1;

                if (at < 0)
                {
                    // No space to break at. Give the whole over-long word its
                    // own line and carry on.
                    at = rest.IndexOf(' ', width);
                    if (at < 0) break;
                }

                lines.Add(rest.Substring(0, at));
                rest = continuation + rest.Substring(at + 1);
            }

            lines.Add(rest);
            return lines;
        }

        /// <summary>The tags as they are appended to a menu row.</summary>
        public static string Suffix(Report report)
        {
            if (report == null || report.Tags.Count == 0) return "";

            var made = new System.Text.StringBuilder();
            for (int i = 0; i < report.Tags.Count; i++)
                made.Append(" -  ").Append(report.Tags[i]);

            return made.ToString();
        }
    }
}
