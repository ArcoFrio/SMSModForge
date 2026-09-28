using System;
using System.Collections.Generic;

namespace SMSModForge.Shared
{
    /// <summary>
    /// ModForge beside XUnity.AutoTranslator, the translation mod most
    /// translations of the game are made with: what its settings say, and
    /// which of its languages is which of ModForge's.
    /// <para/>
    /// It names languages the way its translators do (<c>zh-CN</c>, <c>pt</c>)
    /// and ModForge the way its files are named (<c>zh-Hans</c>, <c>pt-BR</c>);
    /// <see cref="LanguageMatch"/> decides when two codes are one language.
    /// </summary>
    public static class XUnityLanguage
    {
        /// <summary>Its BepInEx plugin id.</summary>
        public const string PluginGuid = "gravydevsupreme.xunity.autotranslator";

        /// <summary>Its settings, in BepInEx's config folder.</summary>
        public const string ConfigFileName = "AutoTranslatorConfig.ini";

        public const string Section = "General";

        /// <summary>The language it translates into.</summary>
        public const string LanguageKey = "Language";

        /// <summary>The language it translates from: the game's.</summary>
        public const string FromLanguageKey = "FromLanguage";

        /// <summary>
        /// A value from its settings file, or null when the file does not set
        /// it. Anything after a <c>;</c> is a note, as the file's own
        /// documentation writes them.
        /// </summary>
        public static string Read(string ini, string section, string key)
        {
            if (string.IsNullOrEmpty(ini)) return null;
            bool inSection = false;
            foreach (string raw in ini.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    inSection = string.Equals(line.Substring(1, line.Length - 2).Trim(), section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int equals = line.IndexOf('=');
                if (equals <= 0 || !string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;
                string value = line.Substring(equals + 1);
                int note = value.IndexOf(';');
                if (note >= 0) value = value.Substring(0, note);
                return value.Trim();
            }
            return null;
        }

        /// <summary>Whether a ModForge language and one of XUnity's are the
        /// same language: <c>pt-BR</c> and <c>pt</c> are, <c>zh-Hans</c> and
        /// <c>zh-TW</c> are not.</summary>
        public static bool Same(string ours, string theirs)
        {
            if (string.IsNullOrEmpty(ours) || string.IsNullOrEmpty(theirs)) return false;
            return LanguageMatch.Best(ours, new[] { theirs }) != null;
        }
    }
}
