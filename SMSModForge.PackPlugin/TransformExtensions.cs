using UnityEngine;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Generic transform-search utilities. <see cref="UnityEngine.GameObject.Find(string)"/>
    /// only finds **active** root GameObjects, and the per-frame
    /// initialisation order means some prototype GOs the pack factories
    /// look up (wallpaper panel, gift-store template, etc.) may be
    /// inactive at lookup time.
    /// <para/>
    /// <see cref="UnityEngine.Transform.Find(string)"/> itself does
    /// see inactive descendants — so as long as the caller already has
    /// a root, walking down via <c>Transform.Find</c> works. The
    /// helpers here cover the cases where the caller needs to
    /// <em>start</em> from a name lookup without a known root, or
    /// search globally.
    /// <para/>
    /// Mirrors the host mod's <c>Core.FindInActiveObjectByName</c> and
    /// <c>TransformExtensions.FindInActiveObjectByName</c>.
    /// </summary>
    internal static class TransformExtensions
    {
        /// <summary>
        /// Resolve a GameObject for the dialogue/integration GO-path actions
        /// (<c>SetGameObjectActive</c>, <c>FadeSprite</c>, …). Handles both forms
        /// authors write in the "GO path" box, and — crucially — finds
        /// <em>inactive</em> objects (so an action can activate something that
        /// starts disabled, like a hidden overlay):
        /// <list type="bullet">
        ///   <item>A full hierarchy path (<c>"5_Levels/14_Beach/Foreground/Thing"</c>):
        ///   the first segment is matched against scene roots (including inactive
        ///   ones), then <see cref="Transform.Find"/> walks the rest — which
        ///   descends through inactive children.</item>
        ///   <item>A bare name (<c>"MyOverlay"</c>): active lookup first,
        ///   then an include-inactive global search by name.</item>
        /// </list>
        /// Returns null when nothing matches.
        /// </summary>
        public static GameObject ResolveGameObject(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;

            // Fast path: active object by name or path.
            var active = GameObject.Find(pathOrName);
            if (active != null) return active;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            if (pathOrName.Contains("/"))
            {
                string trimmed = pathOrName.TrimStart('/');
                int slash = trimmed.IndexOf('/');
                string rootName = slash < 0 ? trimmed : trimmed.Substring(0, slash);
                string rest = slash < 0 ? "" : trimmed.Substring(slash + 1);
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != rootName) continue;
                    if (string.IsNullOrEmpty(rest)) return root;
                    var t = root.transform.Find(rest);   // descends inactive children
                    if (t != null) return t.gameObject;
                }
                return null;
            }

            return FindGlobalIncludingInactive(pathOrName);
        }

        /// <summary>
        /// Walk every root GameObject in the active scene (including
        /// inactive roots) and return the first descendant whose name
        /// matches. Slower than <see cref="GameObject.Find"/> for active
        /// roots — use only when the target may be inactive.
        /// </summary>
        public static GameObject FindGlobalIncludingInactive(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                // includeInactive: true → descends through inactive parents too.
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            }
            return null;
        }

        /// <summary>
        /// Find a descendant of <paramref name="root"/> by name, including
        /// inactive ones. Like <see cref="FindGlobalIncludingInactive"/> but
        /// scoped to one subtree — used to resolve a level overlay <em>within</em>
        /// a specific level so a same-named GameObject in another (e.g. the
        /// previous) level isn't picked by mistake.
        /// </summary>
        public static GameObject FindDescendantIncludingInactive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root.gameObject;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            return null;
        }

        /// <summary>
        /// Walk a slash-separated path starting from <paramref name="root"/>
        /// and return the matching descendant. Each segment is matched
        /// against direct children (including inactive ones); returns
        /// <c>null</c> if any segment is missing.
        /// <para/>
        /// Equivalent to chained <see cref="UnityEngine.Transform.Find(string)"/>
        /// calls but tolerates a <c>null</c> root and stops cleanly at
        /// the first miss.
        /// </summary>
        /// <summary>
        /// Find a direct child by name, case-insensitively (inactive children
        /// included). Bust / outfit GameObject names are looked up this way so
        /// a pack that authored e.g. <c>centiSwimShirtless</c> still resolves the
        /// actual <c>CentiSwimShirtless</c> GameObject — capitalisation in an
        /// outfit name isn't significant.
        /// </summary>
        public static Transform FindChildIgnoreCase(this Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (string.Equals(c.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        public static Transform FindPathIncludingInactive(this Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return root;
            var segments = path.Split('/');
            Transform cur = root;
            foreach (var seg in segments)
            {
                if (cur == null) return null;
                if (string.IsNullOrEmpty(seg)) continue;
                Transform found = null;
                for (int i = 0; i < cur.childCount; i++)
                {
                    var c = cur.GetChild(i);
                    if (c.name == seg) { found = c; break; }
                }
                cur = found;
            }
            return cur;
        }
    }
}
