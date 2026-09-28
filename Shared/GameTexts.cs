using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace SMSModForge.Shared
{
    /// <summary>
    /// The words ModForge's plugin shows players: the pack list on the main
    /// menu, the warning before a save meets a pack it might not suit, the
    /// player's own name on a line.
    /// <para/>
    /// They live in the same English file as the editor's, under
    /// <c>[In the game]</c> with keys starting <c>game.</c>, so a translation of
    /// ModForge is one file: the editor reads all of it, the plugin only its
    /// part. The file is built into both, under the same name.
    /// </summary>
    public static class GameTexts
    {
        /// <summary>What the English file is called inside the assembly holding this.</summary>
        public const string EnglishResource = "SMSModForge.Languages.en.txt";

        private static TextFile _english;
        private static Texts _current;

        /// <summary>The English file, which every language is measured against.</summary>
        public static TextFile English
        {
            get
            {
                if (_english == null) _english = Load();
                return _english;
            }
        }

        /// <summary>The texts in use: English until <see cref="Use"/> says otherwise.</summary>
        public static Texts Current
        {
            get
            {
                if (_current == null) _current = new Texts(English, null, "en");
                return _current;
            }
        }

        /// <summary>Show <paramref name="translation"/>, a file for
        /// <paramref name="code"/>; null goes back to English.</summary>
        public static void Use(TextFile translation, string code)
        {
            _current = translation == null || string.IsNullOrEmpty(code)
                ? new Texts(English, null, "en")
                : new Texts(English, translation, code);
        }

        /// <summary>For the tests, and the fake language they walk the screens in.</summary>
        public static void Use(Texts texts)
        {
            _current = texts;
        }

        public static string T(string key) { return Current.T(key); }

        public static string F(string key, params object[] namesAndValues) { return Current.F(key, namesAndValues); }

        public static string P(string key, long count, params object[] namesAndValues) { return Current.P(key, count, namesAndValues); }

        public static string JoinAnd(IList<string> items) { return Current.JoinAnd(items); }

        /// <summary>A name as the language quotes one: 'Alpha', «Alpha», 「Alpha」.</summary>
        public static string Quoted(string name) { return F("game.quoted", "name", name ?? ""); }

        private static TextFile Load()
        {
            var stream = typeof(GameTexts).Assembly.GetManifestResourceStream(EnglishResource);
            if (stream == null) return new TextFile();
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return TextFile.Parse(reader.ReadToEnd());
        }
    }
}
