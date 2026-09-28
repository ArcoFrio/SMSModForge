using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Two measurements the editor needs and cannot take itself, written once
    /// each without a key being pressed.
    /// <para/>
    /// <b>The main menu</b>, when ModForge's pack list goes onto it: every object
    /// under the menu's canvases with its position, size, anchors, whether it is
    /// switched on, and the words on it - which is how the Settings panel and the
    /// Exit button are found, since nothing names them the same way twice. A
    /// language picker is going to sit inside the one and above the other.
    /// <para/>
    /// <b>The dialogue box</b>, the first time one opens: the text objects under
    /// it with their size, margins, font sizes, auto-size bounds, overflow mode
    /// and line spacing. The editor already knows the box's width and that the
    /// game shrinks a line from 38 to 28 points to fit; it does not know how much
    /// height there is, or what happens to a line that will not fit even then -
    /// and that is what a "this line will be cut off" warning is made of.
    /// <para/>
    /// Debug builds only - see the csproj, which compiles this file on the Debug
    /// configuration alone. Nothing is switched on, moved or changed: this reads.
    /// </summary>
    internal static class LayoutDump
    {
        private const string Tag = "[SMSModForge.PackPlugin] Layout dump: ";
        private static bool _menuDone;
        private static bool _speechDone;

        // ── The main menu ─────────────────────────────────────────────────

        /// <summary>Write the main menu's layout, once per game session.</summary>
        public static void Menu(ManualLogSource log)
        {
            if (_menuDone) return;
            _menuDone = true;
            WriteMenu(log, "menu");
        }

        private const int MostMenuDumps = 8;
        private const float LookEvery = 0.5f;
        private static int _menuDumps;
        private static float _nextLook;
        private static string _lastShape;
        private static string _pendingShape;

        /// <summary>
        /// Write the menu again whenever what is switched on in it changes and
        /// stays changed - which is what opening the Settings panel does. The
        /// first dump is taken before anyone has clicked anything, and a panel
        /// that is off has no laid-out size worth reading. Called every frame on
        /// the main menu; looks twice a second, and stops after a few.
        /// </summary>
        public static void WatchMenu(ManualLogSource log)
        {
            if (!_menuDone || _menuDumps >= MostMenuDumps) return;
            if (Time.unscaledTime < _nextLook) return;
            _nextLook = Time.unscaledTime + LookEvery;

            string shape;
            try { shape = Shape(); }
            catch { return; }

            if (_lastShape == null) { _lastShape = shape; return; }
            if (shape == _lastShape) { _pendingShape = null; return; }

            // Changed: wait one more look, so a flicker (a fade toggling an
            // object for a frame) is not written down as a screen.
            if (shape != _pendingShape) { _pendingShape = shape; return; }

            _lastShape = shape;
            _pendingShape = null;
            _menuDumps++;
            WriteMenu(log, "menu-" + _menuDumps);
        }

        /// <summary>The names of every switched-on object near the top of the
        /// menu's hierarchy, which is where panels are switched.</summary>
        private static string Shape()
        {
            var made = new StringBuilder();
            foreach (var scene in LoadedScenes())
                foreach (var root in scene.GetRootGameObjects())
                    ShapeOf(root.transform, 0, made);
            return made.ToString();
        }

        private static IEnumerable<UnityEngine.SceneManagement.Scene> LoadedScenes()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded) yield return scene;
            }
        }

        private static void ShapeOf(Transform t, int depth, StringBuilder made)
        {
            if (!t.gameObject.activeSelf) return;
            made.Append(depth).Append(t.name).Append('|');
            if (depth >= 4) return;
            for (int i = 0; i < t.childCount; i++) ShapeOf(t.GetChild(i), depth + 1, made);
        }

        private static void WriteMenu(ManualLogSource log, string name)
        {
            try
            {
                var made = new StringBuilder();
                made.Append("{\n  \"scene\": ").Append(Quote(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                    .Append(",\n  \"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height)
                    .Append("],\n  \"objects\": [");

                // Every loaded scene, not only the active one: a settings screen
                // can arrive as a scene of its own laid over the menu.
                int count = 0;
                bool first = true;
                foreach (var scene in LoadedScenes())
                    foreach (var root in scene.GetRootGameObjects())
                        Walk(root.transform, scene.name + ":" + root.name, made, ref first, ref count);
                made.Append("\n  ]\n}\n");

                string file = Write(name, made.ToString());
                log?.LogInfo(Tag + count + " object(s) of the main menu written to " + file);
            }
            catch (Exception ex)
            {
                log?.LogWarning(Tag + "the main menu could not be read: " + ex.Message);
            }
        }

        // ── The dialogue box ──────────────────────────────────────────────

        /// <summary>Write the text settings of the dialogue box <paramref name="speech"/>
        /// belongs to, the first time one appears.</summary>
        public static void Speech(Component speech, ManualLogSource log)
        {
            if (_speechDone || speech == null) return;
            _speechDone = true;

            try
            {
                var made = new StringBuilder();
                made.Append("{\n  \"speechUi\": ").Append(Quote(Path(speech.transform)))
                    .Append(",\n  \"screen\": [").Append(Screen.width).Append(", ").Append(Screen.height)
                    .Append("],\n  \"texts\": [");

                int count = 0;
                bool first = true;

                // The speech UI's own subtree, and the canvas it sits on: the
                // label TMP_Text a GC2 TextReference resolves to is not always a
                // child of the component that refers to it.
                var canvas = speech.GetComponentInParent<Canvas>();
                var scope = canvas != null ? canvas.transform : speech.transform;
                foreach (var c in scope.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) continue;
                    string type = c.GetType().Name;
                    if (type != "TextMeshProUGUI" && type != "TextMeshPro" && type != "Text") continue;

                    if (!first) made.Append(',');
                    first = false;
                    count++;
                    made.Append("\n    { \"path\": ").Append(Quote(Path(c.transform)))
                        .Append(", \"type\": ").Append(Quote(type))
                        .Append(", \"active\": ").Append(c.gameObject.activeInHierarchy ? "true" : "false");

                    var rect = c.transform as RectTransform;
                    if (rect != null)
                        made.Append(", \"rect\": [").Append(F(rect.rect.width)).Append(", ").Append(F(rect.rect.height)).Append(']')
                            .Append(", \"lossyScale\": [").Append(F(rect.lossyScale.x)).Append(", ").Append(F(rect.lossyScale.y)).Append(']');

                    // Read by name: the shipped plugin takes no reference to TMP
                    // for a diagnostic, and these properties are stable across
                    // the TMP versions the game could ship.
                    foreach (string name in new[]
                             {
                                 "text", "fontSize", "fontSizeMin", "fontSizeMax", "enableAutoSizing",
                                 "overflowMode", "enableWordWrapping", "textWrappingMode", "lineSpacing",
                                 "characterSpacing", "margin", "alignment", "maxVisibleLines",
                             })
                    {
                        var p = c.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                        if (p == null) continue;
                        object v;
                        try { v = p.GetValue(c, null); } catch { continue; }
                        string shown = v == null ? "null" : v.ToString();
                        if (name == "text" && shown.Length > 80) shown = shown.Substring(0, 80) + "...";
                        made.Append(", ").Append(Quote(name)).Append(": ").Append(Quote(shown));
                    }

                    var font = c.GetType().GetProperty("font", BindingFlags.Instance | BindingFlags.Public);
                    if (font != null)
                    {
                        object f = null;
                        try { f = font.GetValue(c, null); } catch { }
                        if (f is UnityEngine.Object o && o != null) made.Append(", \"font\": ").Append(Quote(o.name));
                    }
                    made.Append(" }");
                }
                made.Append("\n  ]\n}\n");

                string file = Write("dialoguebox", made.ToString());
                log?.LogInfo(Tag + count + " text object(s) of the dialogue box written to " + file);
            }
            catch (Exception ex)
            {
                log?.LogWarning(Tag + "the dialogue box could not be read: " + ex.Message);
            }
        }

        // ── Writing ───────────────────────────────────────────────────────

        private static void Walk(Transform t, string path, StringBuilder made, ref bool first, ref int count)
        {
            count++;
            if (!first) made.Append(',');
            first = false;

            made.Append("\n    { \"path\": ").Append(Quote(path))
                .Append(", \"active\": ").Append(t.gameObject.activeSelf ? "true" : "false");

            var rect = t as RectTransform;
            if (rect != null)
            {
                made.Append(", \"pos\": [").Append(F(rect.anchoredPosition.x)).Append(", ").Append(F(rect.anchoredPosition.y)).Append(']')
                    .Append(", \"size\": [").Append(F(rect.rect.width)).Append(", ").Append(F(rect.rect.height)).Append(']')
                    .Append(", \"anchorMin\": [").Append(F(rect.anchorMin.x)).Append(", ").Append(F(rect.anchorMin.y)).Append(']')
                    .Append(", \"anchorMax\": [").Append(F(rect.anchorMax.x)).Append(", ").Append(F(rect.anchorMax.y)).Append(']')
                    .Append(", \"pivot\": [").Append(F(rect.pivot.x)).Append(", ").Append(F(rect.pivot.y)).Append(']');
            }

            var components = new List<string>();
            string words = null;
            string sprite = null;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) { components.Add("(missing)"); continue; }
                string type = c.GetType().Name;
                components.Add(type);
                if (words == null && (type == "TextMeshProUGUI" || type == "TextMeshPro" || type == "Text"))
                {
                    var p = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                    try { words = p?.GetValue(c, null) as string; } catch { }
                }
                // What a button or panel looks like, so something added beside
                // it can be made to match.
                if (sprite == null && c is UnityEngine.UI.Image image && image.sprite != null)
                    sprite = image.sprite.name;
            }
            made.Append(", \"components\": [");
            for (int i = 0; i < components.Count; i++) made.Append(i == 0 ? "" : ", ").Append(Quote(components[i]));
            made.Append(']');
            if (!string.IsNullOrEmpty(words))
                made.Append(", \"text\": ").Append(Quote(words.Length > 60 ? words.Substring(0, 60) + "..." : words));
            if (sprite != null)
                made.Append(", \"sprite\": ").Append(Quote(sprite));
            made.Append(" }");

            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                Walk(child, path + "/" + child.name, made, ref first, ref count);
            }
        }

        private static string Write(string what, string content)
        {
            string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,
                "SMSModForge-" + what + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(file, content, new UTF8Encoding(false));
            return file;
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            for (var up = t.parent; up != null; up = up.parent) path = up.name + "/" + path;
            return path;
        }

        private static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static string Quote(string s)
        {
            var made = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') made.Append('\\').Append(c);
                else if (c == '\n') made.Append("\\n");
                else if (c == '\r') made.Append("\\r");
                else if (c < 0x20) made.Append("\\u").Append(((int)c).ToString("x4"));
                else made.Append(c);
            }
            return made.Append('"').ToString();
        }
    }
}
