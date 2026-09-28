using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using SMSModForge.Shared;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The language a player plays in: ModForge's own words, and every pack
    /// that has a translation for it.
    /// <para/>
    /// The game itself has no language setting and is English whatever this
    /// says. What this changes is what ModForge adds: the pack list on the main
    /// menu, the save warning, and the packs' own lines, names, quests, buttons
    /// and screens, wherever a pack ships a translation.
    /// </summary>
    internal static class PluginLanguage
    {
        private const string Tag = "[SMSModForge.PackPlugin] Language: ";

        /// <summary>The setting's value that follows the computer's language.</summary>
        public const string Auto = "auto";

        /// <summary>The language asked for, as a code: <c>es</c>, <c>pt-BR</c>...</summary>
        public static string Code { get; private set; } = "en";

        /// <summary>
        /// Translations of ModForge's own words: <c>BepInEx/plugins/SMSModForge/Languages</c>,
        /// beside the plugin. The same files as the editor's; the game reads
        /// only their <c>[In the game]</c> part.
        /// </summary>
        public static string Folder
        {
            get
            {
                string plugins = Path.GetDirectoryName(typeof(PluginLanguage).Assembly.Location) ?? "";
                return Path.Combine(Path.Combine(plugins, "SMSModForge"), "Languages");
            }
        }

        private static ConfigEntry<string> _setting;

        /// <summary>Read the setting, and switch ModForge's own words to it.
        /// Called first thing, before a pack is read or a menu drawn.</summary>
        public static void Configure(ConfigFile config, ManualLogSource log)
        {
            _setting = config.Bind("Language", "Language", Auto,
                "The language ModForge shows its own words in (the pack list on the main menu, the save "
                + "warning), and the language packs are played in when they come with a translation into it. "
                + "The game itself stays in English. " + Auto + " follows the language the computer is set to; "
                + "otherwise a code such as en, es, pt-BR, zh-Hans, ja, ko, ru, de or fr. "
                + "Also chosen on the main menu, with the flags above Exit.");
            _gameLinesNotice = config.Bind("Language", "GameLinesNoticeShown", false,
                "Whether you have been told that packs show the game's own lines, in the conversations they "
                + "extend, in their translation of them. They always do; set this to false to be told again.");
            Apply(_setting.Value, log);
        }

        private static ConfigEntry<bool> _gameLinesNotice;

        /// <summary>Whether the player has been told about packs showing the
        /// game's own lines in their translations (<see cref="GameLineNotice"/>).</summary>
        public static bool GameLinesNoticeShown
        {
            get { return _gameLinesNotice != null && _gameLinesNotice.Value; }
        }

        /// <summary>The notice has been on screen. Written straight away: it
        /// is the player's, not a save's.</summary>
        public static void NoteGameLinesNoticeShown(ManualLogSource log)
        {
            if (_gameLinesNotice == null || _gameLinesNotice.Value) return;
            _gameLinesNotice.Value = true;
            log?.LogInfo(Tag + "told the player that packs show the game's own lines in their translations.");
        }

        /// <summary>What the setting says: <see cref="Auto"/> or a code.</summary>
        public static string Setting
        {
            get
            {
                string value = _setting == null ? "" : (_setting.Value ?? "").Trim();
                return value.Length == 0 ? Auto : value;
            }
        }

        /// <summary>Whether the setting follows the computer's language.</summary>
        public static bool FollowsComputer
        {
            get { return string.Equals(Setting, Auto, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>What <see cref="Auto"/> follows: XUnity.AutoTranslator's
        /// language when it is installed - a player who has it plays the game
        /// in that language - and the computer's otherwise.</summary>
        public static string AutoLanguage
        {
            get { return XUnityLink.Loaded && !string.IsNullOrEmpty(XUnityLink.Language) ? XUnityLink.Language : ComputerLanguage; }
        }

        /// <summary>The language the computer is set to, as a code; English
        /// when the game cannot tell.</summary>
        public static string ComputerLanguage
        {
            get { return LanguageMatch.CodeOfSystemLanguage(Application.systemLanguage.ToString()) ?? "en"; }
        }

        /// <summary>
        /// Change the setting - from the main menu - and everything that
        /// follows it at once: ModForge's own words and the fonts they need.
        /// Packs follow the next time they are read, which is when a game is
        /// started or loaded: the language is only chosen on the main menu, so
        /// no pack is ever read in one language and then played in another.
        /// </summary>
        public static void Switch(string setting, ManualLogSource log)
        {
            string value = string.IsNullOrEmpty(setting) ? Auto : setting.Trim();
            // Written straight away: BepInEx saves the file when a setting is set.
            if (_setting != null) _setting.Value = value;
            log?.LogInfo(Tag + "chosen on the main menu: " + value);
            Apply(value, log);
            PluginFonts.AddFallbacks(Code, log);
        }

        private static void Apply(string setting, ManualLogSource log)
        {
            string wanted = (setting ?? "").Trim();
            if (wanted.Length == 0 || string.Equals(wanted, Auto, StringComparison.OrdinalIgnoreCase))
            {
                wanted = AutoLanguage;
                if (XUnityLink.Loaded)
                    log?.LogInfo(Tag + "following XUnity.AutoTranslator's language: " + wanted);
                else
                    log?.LogInfo(Tag + "following the computer's language (" + Application.systemLanguage + "): " + wanted);
            }
            Code = wanted;

            var files = Files(Folder);
            string best = LanguageMatch.Best(wanted, files.Keys);
            if (best == null || best.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                // Back to English, which matters now that this can run twice.
                GameTexts.Use(null, null);
                if (PluralRules.LanguageOf(wanted) != "en")
                    log?.LogInfo(Tag + "no translation of ModForge's own words into " + wanted + " in " + Folder
                                 + " - they stay in English. Packs are still played in " + wanted
                                 + " where they come with a translation into it.");
                return;
            }

            var file = Read(files[best], log);
            GameTexts.Use(file, file == null ? null : best);
            if (file != null) log?.LogInfo(Tag + "ModForge's own words in " + best + ", from " + files[best]);
        }

        /// <summary>The languages ModForge has its own words in: English, and
        /// each file in <see cref="Folder"/>.</summary>
        public static List<string> OwnLanguages()
        {
            var codes = new List<string> { "en" };
            codes.AddRange(Files(Folder).Keys);
            return codes;
        }

        /// <summary>
        /// A language's name in itself: ModForge's own list first, then the
        /// runtime's, then the code. Never empty.
        /// </summary>
        public static string NameOf(string code)
        {
            string name = LanguageChoice.NativeName(code);
            if (name != null) return name;
            try
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(code);
                string native = culture.NativeName;
                if (!string.IsNullOrEmpty(native) && !string.Equals(native, code, StringComparison.OrdinalIgnoreCase))
                    return char.ToUpper(native[0], culture) + native.Substring(1);
            }
            catch (Exception) { }
            return code ?? "";
        }

        /// <summary>
        /// Lay the pack's translation into the player's language over its
        /// manifest, before anything reads it. A pack with none is played in its
        /// own words.
        /// <para/>
        /// The pack's own language counts as one of the ones it has: a pack
        /// written in Brazilian Portuguese is read in its own words by a
        /// Brazilian, not in a Portuguese translation it happens to carry.
        /// <para/>
        /// A text the pack leaves empty was typed only in a translation. It
        /// takes the player's language if that has it, and otherwise the first
        /// translation that does, by code - so it is never blank, which would be
        /// worse than reading it in the wrong language.
        /// </summary>
        public static void Translate(PackManifest pack, ManualLogSource log)
        {
            if (pack == null || pack.Root == null || pack.Archive == null) return;
            var files = PackTexts.Files(pack.Archive.Paths);
            if (files.Count == 0)
            {
                XUnityLink.NotePack(pack, null, null);
                return;
            }

            string best = PackTexts.Choose(Code, pack.Language, files.Keys);
            TextFile chosen = best != null ? ReadPackFile(pack, files[best], log) : null;

            Func<IList<TextFile>> others = () =>
            {
                var codes = new List<string>(files.Keys);
                codes.Sort(StringComparer.OrdinalIgnoreCase);
                var list = new List<TextFile>();
                foreach (string code in codes)
                {
                    if (chosen != null && string.Equals(code, best, StringComparison.OrdinalIgnoreCase)) continue;
                    var file = ReadPackFile(pack, files[code], log);
                    if (file != null) list.Add(file);
                }
                return list;
            };

            var applied = PackTexts.Apply(pack.Root, chosen, others);
            XUnityLink.NotePack(pack, chosen != null ? best : null, applied);
            if (chosen != null)
            {
                pack.TranslatedInto = best;
                pack.Translation = chosen;
                log?.LogInfo(Tag + pack.PackId + " in " + best + ": " + applied.Translated + " of " + applied.Total
                             + " texts translated"
                             + (applied.Outdated.Count > 0
                                    ? ", " + applied.Outdated.Count + " left as the pack says them because the pack "
                                      + "changed them after they were translated (" + string.Join(", ", applied.Outdated.ToArray()) + ")"
                                    : "")
                             + ".");
            }
            if (applied.Borrowed.Count > 0)
                log?.LogInfo(Tag + pack.PackId + ": " + applied.Borrowed.Count + " text(s) the pack has no words of "
                             + "its own for were given another translation's words so they are not blank ("
                             + string.Join(", ", applied.Borrowed.ToArray()) + ").");
        }

        private static TextFile ReadPackFile(PackManifest pack, string path, ManualLogSource log)
        {
            string text = pack.ReadText(path);
            if (text == null) return null;
            var file = TextFile.Parse(text);
            foreach (var bad in file.BadLines)
                log?.LogWarning(Tag + pack.PackId + ": " + path + " line " + bad.Line
                                + " could not be read and is ignored: " + bad.Content);
            return file;
        }

        private static readonly Dictionary<string, TextFile> _otherLanguages =
            new Dictionary<string, TextFile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// ModForge's word for <paramref name="key"/> in <paramref name="code"/>,
        /// which need not be the player's: a pack written in Portuguese and
        /// played in its own words calls the player by the Portuguese word,
        /// whatever ModForge itself is showing. English when ModForge has no
        /// file for that language.
        /// </summary>
        public static string WordIn(string code, string key)
        {
            string english = GameTexts.English.Get(key) ?? key;
            if (string.IsNullOrEmpty(code) || PluralRules.LanguageOf(code) == "en") return english;
            if (LanguageMatch.Best(code, new[] { GameTexts.Current.Code }) != null) return GameTexts.T(key);

            TextFile file;
            if (!_otherLanguages.TryGetValue(code, out file))
            {
                var files = Files(Folder);
                string best = LanguageMatch.Best(code, files.Keys);
                file = best == null ? null : Read(files[best], null);
                _otherLanguages[code] = file;
            }
            return (file == null ? null : file.Translated(key)) ?? english;
        }

        /// <summary>The translations in a folder, by code.</summary>
        private static Dictionary<string, string> Files(string folder)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!Directory.Exists(folder)) return found;
                foreach (string path in Directory.GetFiles(folder, "*" + PackTexts.Extension))
                {
                    string code = Path.GetFileNameWithoutExtension(path);
                    if (TextFile.IsKey(code)) found[code] = path;
                }
            }
            catch (Exception) { }
            return found;
        }

        private static TextFile Read(string path, ManualLogSource log)
        {
            try { return TextFile.Parse(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception ex)
            {
                log?.LogWarning(Tag + "could not read " + path + ": " + ex.Message);
                return null;
            }
        }
    }
}
