using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using SMSModForge.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// ModForge's notice on the game's main menu (the author, 1.8.0): what
    /// <see cref="ForgeNotice"/> decides to say, in the window every ModForge
    /// message to players is drawn in (<see cref="SaveLoadWarning.Window"/>).
    /// <para/>
    /// <b>The version last shown</b> is kept in ModForge's own BepInEx settings,
    /// <c>[Notices] Last shown</c>, and written when the player closes the
    /// window: a notice that was never read - the game quit, a save loaded from
    /// the command line - comes back the next time. Emptying it shows the whole
    /// notice again. Whether ModForge ran in this game before is whether its
    /// settings file was there when the plugin started.
    /// <para/>
    /// <b>One notice at a time, whichever mod it is from.</b> Other mods may put
    /// a notice of their own on the same menu, and two at once would cover
    /// each other. The convention, which any mod can follow without knowing
    /// about this one or naming it: a notice window is a root GameObject whose
    /// name starts with <see cref="NoticePrefix"/>, and a mod shows its own
    /// only while there is no such object in any loaded scene - it waits, and
    /// looks again next frame. Unity runs every mod's Update on the same
    /// thread, one after another, so whoever looks first and finds none puts
    /// theirs up in that same call and every mod after it sees it.
    /// </summary>
    internal static class MainMenuNotice
    {
        /// <summary>The start of every notice window's name, from any mod.</summary>
        public const string NoticePrefix = "ModNotice";

        private const string WindowName = NoticePrefix + " ModForge";
        private const string Tag = "[SMSModForge.PackPlugin] Notice: ";

        /// <summary>How long the menu has been up before the notice comes:
        /// over a menu still arriving, it lands on the menu's own animation.</summary>
        private const float Settle = 1.0f;

        private static ConfigEntry<string> _lastShown;
        private static bool _usedBefore;
        private static ManualLogSource _log;

        private static bool _decided;
        private static ForgeNotice.Kind _kind = ForgeNotice.Kind.None;
        private static float _menuSince = -1f;
        private static GameObject _window;
        private static bool _answered;

        /// <summary>Whether ModForge's notice is on screen.</summary>
        public static bool IsOpen => _window != null;

        /// <param name="usedBefore">ModForge's settings file was already there
        /// when the plugin started: it has run in this game before.</param>
        public static void Configure(ConfigFile config, bool usedBefore, ManualLogSource log)
        {
            _usedBefore = usedBefore;
            _log = log;
            _lastShown = config.Bind("Notices", "Last shown", "",
                "The ModForge version whose notice on the main menu was last closed. Empty: it has never been "
                + "closed, and the whole notice shows the next time the main menu comes up. After an update, a "
                + "short notice shows only when the update changes something for players.");
        }

        /// <summary>The main menu has just come up.</summary>
        public static void MenuArrived()
        {
            _menuSince = Time.unscaledTime;
            _window = null;
            _answered = false;
        }

        /// <summary>
        /// Once a frame on the main menu, after ModForge's list of packs is up:
        /// puts the notice up when there is one to show, the menu has settled
        /// and no other notice is on screen.
        /// </summary>
        public static void Tick(TMPro.TMP_FontAsset font)
        {
            if (_window != null || _lastShown == null) return;
            if (!_decided)
            {
                _decided = true;
                _kind = ForgeNotice.Decide(_lastShown.Value, ForgeVersion.Current);
                _log?.LogInfo(Tag + "last shown '" + _lastShown.Value + "', running " + ForgeVersion.Current
                              + (_usedBefore ? " (ran here before)" : " (first run here)") + ": " + _kind + ".");
            }
            if (_kind == ForgeNotice.Kind.None) return;
            if (_menuSince < 0f || Time.unscaledTime - _menuSince < Settle) return;
            if (AnotherNoticeIsUp()) return;
            Show(_kind == ForgeNotice.Kind.Welcome
                     ? ForgeNotice.Welcome(ForgeVersion.Current, _usedBefore)
                     : ForgeNotice.Changes(_lastShown.Value, ForgeVersion.Current),
                 font, record: true);
        }

        /// <summary>The whole notice again, asked for from the menu. Does not
        /// wait: the player asked for it now.</summary>
        public static void ShowAgain(TMPro.TMP_FontAsset font)
        {
            if (_window != null || AnotherNoticeIsUp()) return;
            Show(ForgeNotice.Welcome(ForgeVersion.Current, false), font, record: false);
        }

        private static void Show(SaveWarningText text, TMPro.TMP_FontAsset font, bool record)
        {
            // Nothing on it could be clicked without an event system, and a
            // window nobody can close is worse than none: it waits for a menu
            // where it can be.
#pragma warning disable 0618
            if (EventSystem.current == null && Object.FindObjectOfType<EventSystem>() == null) return;
#pragma warning restore 0618
            _answered = false;
            try
            {
                _window = SaveLoadWarning.Window(WindowName, text, new[]
                {
                    new SaveLoadWarning.WindowButton(ForgeNotice.CloseLabel, SaveLoadWarning.GoOnColour,
                                                     () => Close(record)),
                }, font, width: 780f, centred: true);
                _log?.LogInfo(Tag + text.Title + " - " + text.Plain());
            }
            catch (System.Exception e)
            {
                // Said once and not again this run: a notice that cannot be
                // drawn must not be tried every frame.
                _kind = ForgeNotice.Kind.None;
                _log?.LogError(Tag + "could not be shown: " + e);
            }
        }

        private static void Close(bool record)
        {
            if (_answered || _window == null) return;
            _answered = true;
            SaveLoadWarning.Click();
            if (record)
            {
                // BepInEx writes the settings file as soon as a setting is set.
                _lastShown.Value = ForgeVersion.Current;
                _kind = ForgeNotice.Kind.None;
                _log?.LogInfo(Tag + "closed; " + ForgeVersion.Current + " noted as shown.");
            }
            var window = _window;
            SaveLoadWarning.Fold(window, () =>
            {
                if (ReferenceEquals(_window, window)) _window = null;
                _answered = false;
            });
        }

        /// <summary>Whether a notice - any mod's - is on screen now.</summary>
        public static bool AnotherNoticeIsUp()
        {
            foreach (var root in Roots())
                if (root != null && root.activeInHierarchy && root != _window
                    && root.name.StartsWith(NoticePrefix, System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>The root objects of every loaded scene, and of the scene
        /// kept across loads, where a mod's window may live too.</summary>
        private static IEnumerable<GameObject> Roots()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects()) yield return root;
            }
            var kept = Plugin.Instance != null ? Plugin.Instance.gameObject.scene : default(Scene);
            if (kept.IsValid() && kept.isLoaded)
                foreach (var root in kept.GetRootGameObjects()) yield return root;
        }
    }
}
