using System;
using System.Collections.Generic;
using System.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// What a player is told about ModForge itself, on the game's main menu
    /// (the author, 1.8.0): the first time the game starts with it, that it is
    /// there and what is worth knowing; after an update, only when the update
    /// brings something a player would notice, in a few lines. Nothing at all
    /// otherwise.
    /// <para/>
    /// Counted from 1.8.0, the first version with the notice: nobody has a
    /// record before it, so everyone - a player of 1.7 included - sees the
    /// whole notice once. A player who had ModForge before is also told what
    /// is new in this version for them.
    /// <para/>
    /// The decision and the words are here, compiled into the plugin and into
    /// the editor's tests, which check them; the plugin only shows the window
    /// and keeps the version last shown.
    /// </summary>
    public static class ForgeNotice
    {
        public enum Kind
        {
            /// <summary>Nothing to say: seen already, or the update brings
            /// nothing a player would notice.</summary>
            None,

            /// <summary>The whole notice: ModForge is here, and what to know.</summary>
            Welcome,

            /// <summary>A short list of what an update changes for players.</summary>
            Changes,
        }

        /// <summary>
        /// The versions that bring something a player would notice, oldest
        /// first. Each has its lines in the language files, under
        /// <c>game.notice.news.</c> and the version. A release adds itself here
        /// when it changes anything for players - how a pack plays, the main
        /// menu, saves - and not otherwise: a notice after every update would
        /// teach players to close it unread.
        /// </summary>
        public static readonly string[] VersionsWithNews = { "1.8.0" };

        /// <summary>What a version's news is kept under.</summary>
        public static string NewsKey(string version) => "game.notice.news." + version;

        /// <summary>
        /// What to show, given the version whose notice the player last closed
        /// (<paramref name="lastShown"/>; empty when never) and the one running.
        /// </summary>
        public static Kind Decide(string lastShown, string current)
            => Decide(lastShown, current, VersionsWithNews);

        public static Kind Decide(string lastShown, string current, IEnumerable<string> versionsWithNews)
        {
            if (ForgeVersion.Parse(lastShown) == null) return Kind.Welcome;
            if (ForgeVersion.Compare(lastShown, current) >= 0) return Kind.None;
            return NewsSince(lastShown, current, versionsWithNews).Count > 0 ? Kind.Changes : Kind.None;
        }

        /// <summary>The versions with news after <paramref name="lastShown"/>,
        /// up to and including <paramref name="current"/>, oldest first.</summary>
        public static List<string> NewsSince(string lastShown, string current, IEnumerable<string> versionsWithNews)
            => (versionsWithNews ?? Enumerable.Empty<string>())
               .Where(v => ForgeVersion.Compare(v, lastShown) > 0 && ForgeVersion.Compare(v, current) <= 0)
               .OrderBy(v => v, Comparer<string>.Create(ForgeVersion.Compare))
               .ToList();

        /// <summary>A version's news, one line each.</summary>
        public static List<string> News(string version)
            => GameTexts.T(NewsKey(version)).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        /// <summary>The button that closes it.</summary>
        public static string CloseLabel { get { return GameTexts.T("game.notice.close"); } }

        /// <summary>
        /// The whole notice. <paramref name="usedBefore"/>: ModForge has run in
        /// this game before (a player of an older version), who is also told
        /// what this version changes for them.
        /// </summary>
        public static SaveWarningText Welcome(string current, bool usedBefore)
            => Welcome(current, usedBefore, VersionsWithNews);

        /// <remarks>One sentence (the author, 1.8.0: the first version was
        /// "way too much text for the regular user"): where packs go and where
        /// they are listed. The rest a player learns from the list itself.</remarks>
        public static SaveWarningText Welcome(string current, bool usedBefore, IEnumerable<string> versionsWithNews)
        {
            var text = new SaveWarningText { Title = GameTexts.T("game.notice.welcome.title") };
            text.Paragraphs.Add(GameTexts.F("game.notice.welcome.body", "folder", ModsFolder.Name));
            if (usedBefore && (versionsWithNews ?? Enumerable.Empty<string>()).Contains(current))
            {
                var news = News(current);
                if (news.Count > 0)
                    text.Details.Add(new SaveWarningSection(GameTexts.F("game.notice.newIn", "version", current), news));
            }
            return text;
        }

        /// <summary>What the updates since <paramref name="lastShown"/> change
        /// for players, newest first.</summary>
        public static SaveWarningText Changes(string lastShown, string current)
            => Changes(lastShown, current, VersionsWithNews);

        public static SaveWarningText Changes(string lastShown, string current, IEnumerable<string> versionsWithNews)
        {
            var text = new SaveWarningText { Title = GameTexts.F("game.notice.changes.title", "version", current) };
            var versions = NewsSince(lastShown, current, versionsWithNews);
            versions.Reverse();
            foreach (var version in versions)
            {
                var news = News(version);
                if (news.Count > 0)
                    text.Details.Add(new SaveWarningSection(GameTexts.F("game.notice.newIn", "version", version), news));
            }
            return text;
        }
    }
}
