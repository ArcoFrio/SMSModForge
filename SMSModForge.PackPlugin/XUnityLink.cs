using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using SMSModForge.Shared;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// ModForge beside XUnity.AutoTranslator, the mod most of the game's
    /// translations are made with.
    /// <list type="bullet">
    ///   <item>ModForge set to follow the computer follows XUnity's language
    ///   instead (<see cref="PluginLanguage"/>), and that language is offered
    ///   on the main menu.</item>
    ///   <item>Choosing a language on the main menu does nothing to XUnity, as
    ///   the author decided: XUnity reads its language once, when the game
    ///   starts, and while running can only switch its translation on or off,
    ///   so the two languages are kept apart - the menu chooses the mods',
    ///   XUnity's own settings the game's.</item>
    ///   <item>XUnity is told to leave alone the texts ModForge has already put
    ///   in a language other than the one XUnity translates from - a pack's
    ///   translation, a pack written in another language, ModForge's own
    ///   translated words - so nothing is translated twice.</item>
    /// </list>
    /// All of it through reflection: XUnity is not something this plugin is
    /// built against, and most players do not have it. Anything not found is
    /// logged, and ModForge carries on as if XUnity were not there.
    /// </summary>
    internal static class XUnityLink
    {
        private const string Tag = "[SMSModForge.PackPlugin] XUnity.AutoTranslator: ";

        /// <summary>Its assembly, where its public interface is.</summary>
        private const string CoreAssembly = "XUnity.AutoTranslator.Plugin.Core";

        public static bool Loaded { get; private set; }

        /// <summary>The language it translates into: what its settings said when
        /// the game started, which is what it uses until the game is closed.</summary>
        public static string Language { get; private set; }

        /// <summary>The language it translates from - the game's.</summary>
        public static string FromLanguage { get; private set; }

        private static ManualLogSource _log;

        /// <summary>The texts ModForge has put in place that are not in the
        /// language XUnity translates from.</summary>
        private static readonly TextTemplates _leaveAlone = new TextTemplates();

        private static bool _hooked;
        private static bool _hookFailed;
        private static int _hookTries;

        /// <summary>How long to wait for it to start: about a minute of frames.</summary>
        private const int HookFrames = 3600;

        private static PropertyInfo _originalText;
        private static PropertyInfo _component;
        private static MethodInfo _ignore;

        /// <summary>
        /// Whether XUnity is loaded, and what its settings say. Called before
        /// ModForge reads its own language, which may follow XUnity's.
        /// </summary>
        public static void Detect(ManualLogSource log)
        {
            _log = log;
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(XUnityLanguage.PluginGuid, out info) || info == null) return;
            Loaded = true;

            string path = Path.Combine(Paths.ConfigPath, XUnityLanguage.ConfigFileName);
            string ini = null;
            try
            {
                if (File.Exists(path)) ini = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                log?.LogWarning(Tag + "its settings could not be read (" + path + "): " + e.Message);
            }
            // Its own defaults when its file does not say.
            Language = XUnityLanguage.Read(ini, XUnityLanguage.Section, XUnityLanguage.LanguageKey) ?? "en";
            FromLanguage = XUnityLanguage.Read(ini, XUnityLanguage.Section, XUnityLanguage.FromLanguageKey) ?? "ja";

            log?.LogInfo(Tag + "found (" + (info.Metadata == null ? "?" : info.Metadata.Version.ToString())
                         + "), translating " + FromLanguage + " into " + Language + ".");
        }

        // ── The translator it runs ───────────────────────────────────────

        /// <summary>Its public <c>AutoTranslator.Default</c>, looked up once:
        /// its assembly is loaded before this plugin, or not at all.</summary>
        private static PropertyInfo _default;
        private static bool _lookedUp;

        /// <summary>Its translator, the object behind <c>AutoTranslator.Default</c>;
        /// null until it has started.</summary>
        private static object Translator()
        {
            if (!_lookedUp)
            {
                _lookedUp = true;
                var type = FindType(CoreAssembly, "XUnity.AutoTranslator.Plugin.Core.AutoTranslator");
                _default = type == null ? null : type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
            }
            return _default == null ? null : _default.GetValue(null, null);
        }

        private static Type FindType(string assembly, string name)
        {
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(loaded.GetName().Name, assembly, StringComparison.OrdinalIgnoreCase)) continue;
                var type = loaded.GetType(name, false);
                if (type != null) return type;
            }
            return null;
        }

        // ── Not translating twice ────────────────────────────────────────

        /// <summary>The next scene reads the packs again.</summary>
        public static void ForgetTexts()
        {
            _leaveAlone.Clear();
        }

        /// <summary>A text ModForge has put in place in <paramref name="language"/>
        /// - left alone by XUnity unless that is the language it translates
        /// from. A text whose language is not known (null) is left alone: read
        /// as the game's language it would be mistranslated, where left alone
        /// it is only untranslated.</summary>
        public static void Note(string text, string language)
        {
            if (!Loaded || string.IsNullOrEmpty(text)) return;
            if (language != null && XUnityLanguage.Same(language, FromLanguage)) return;
            _leaveAlone.Add(text);
        }

        /// <summary>
        /// Every text of a pack, once its translation is laid over it: those
        /// the translation filled are in <paramref name="translatedInto"/>, those
        /// it borrowed from another translation are in a language not known,
        /// and the rest are in the pack's own language.
        /// </summary>
        public static void NotePack(PackManifest pack, string translatedInto, PackTexts.Applied applied)
        {
            if (!Loaded || pack == null || pack.Root == null) return;
            var translated = new HashSet<string>(applied == null ? new List<string>() : applied.TranslatedKeys, StringComparer.Ordinal);
            var borrowed = new HashSet<string>(applied == null ? new List<string>() : applied.Borrowed, StringComparer.Ordinal);
            string own = pack.Language ?? PackTexts.DefaultLanguage;
            int before = _leaveAlone.Count;
            foreach (var site in PackTexts.Of(pack.Root))
            {
                string language = translated.Contains(site.Key) ? translatedInto
                                : borrowed.Contains(site.Key) ? null
                                : own;
                Note(site.Text, language);
            }
            int noted = _leaveAlone.Count - before;
            if (noted > 0)
                _log?.LogInfo(Tag + pack.PackId + ": " + noted + " text(s) not in " + FromLanguage
                              + " are left for it not to translate.");
        }

        /// <summary>
        /// Ask XUnity to check with ModForge before it translates a text.
        /// Tried every frame until it has started; once it has, done once.
        /// </summary>
        public static void Hook()
        {
            if (!Loaded || _hooked || _hookFailed) return;
            try
            {
                var translator = Translator();
                if (translator == null)
                {
                    // Not started yet. It starts before this plugin's first
                    // frame, so after a minute it is not going to.
                    if (_default == null)
                    {
                        _hookFailed = true;
                        _log?.LogWarning(Tag + "this version has no AutoTranslator.Default this plugin knows, so it "
                                         + "cannot be asked to leave ModForge's translations alone.");
                    }
                    else if (++_hookTries >= HookFrames)
                    {
                        _hookFailed = true;
                        _log?.LogWarning(Tag + "is installed but never started, so it cannot be asked to leave "
                                         + "ModForge's translations alone.");
                    }
                    return;
                }
                _hooked = true;

                var context = FindType(CoreAssembly, "XUnity.AutoTranslator.Plugin.Core.ComponentTranslationContext");
                var face = FindType(CoreAssembly, "XUnity.AutoTranslator.Plugin.Core.ITranslator");
                var register = face == null ? null : face.GetMethod("RegisterOnTranslatingCallback");
                _originalText = context == null ? null : context.GetProperty("OriginalText");
                _component = context == null ? null : context.GetProperty("Component");
                _ignore = context == null ? null : context.GetMethod("IgnoreComponent", Type.EmptyTypes);
                if (register == null || _originalText == null || _component == null || _ignore == null)
                {
                    _log?.LogWarning(Tag + "this version cannot be asked to leave a text alone, so it may translate "
                                     + "texts ModForge has already translated.");
                    return;
                }

                // Its callback takes its own context type; one that takes any
                // object stands in for it, as a delegate's parameter may be a
                // base type of the one it is declared with.
                var callback = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(context),
                    typeof(XUnityLink).GetMethod(nameof(OnTranslating), BindingFlags.NonPublic | BindingFlags.Static));
                register.Invoke(translator, new object[] { callback });
                _log?.LogInfo(Tag + "asked to leave alone what ModForge has already translated.");
            }
            catch (Exception e)
            {
                _hookFailed = true;
                _log?.LogWarning(Tag + "could not be asked to leave ModForge's translations alone: " + e.Message);
            }
        }

        /// <summary>XUnity is about to translate a text. Never throws: an
        /// error here would be XUnity's.</summary>
        private static void OnTranslating(object context)
        {
            try
            {
                string text = _originalText.GetValue(context, null) as string;
                var component = _component.GetValue(context, null) as Component;
                if (LeaveAlone(text, component)) _ignore.Invoke(context, null);
            }
            catch (Exception)
            {
                // Left to XUnity, as if ModForge had not been asked.
            }
        }

        private static bool LeaveAlone(string text, Component component)
        {
            if (_leaveAlone.Contains(text)) return true;
            // ModForge's own words, in the objects it builds - when they are in
            // a language XUnity would read wrongly as the game's.
            if (component == null || XUnityLanguage.Same(GameTexts.Current.Code, FromLanguage)) return false;
            for (var t = component.transform; t != null; t = t.parent)
                if (t.name.StartsWith("SMSModForge", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
