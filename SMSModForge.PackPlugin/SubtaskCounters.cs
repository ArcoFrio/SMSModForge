using System;
using System.Collections.Generic;
using System.Reflection;
using SMSModForge.Shared;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Makes the journal draw a count beside a SUBTASK, which it otherwise
    /// never does.
    /// <para/>
    /// <b>Why it does not.</b> The journal builds its rows from two prefabs,
    /// <c>Journal_Task</c> and <c>Journal_SubTask</c>. Both carry the same
    /// <c>TaskUI</c>, and both have a complete <c>Text/Counter</c> under them —
    /// the brackets, the current number, the separator and the maximum — bound
    /// to the same fields on each. The counters are built and they are filled.
    /// <para/>
    /// THREE things stop the subtask's from showing, and each one hid the next.
    /// Every round of this reported success while the screen did not change,
    /// which is why the runtime now reads back the rows the journal actually
    /// drew rather than trusting that setting a field was enough:
    /// <list type="number">
    /// <item><c>TaskUI</c> switches its counter on through
    /// <c>m_ActiveElements.m_ActiveIfIsCounter</c>, which names the object to
    /// enable when the step it is drawing counts. On <c>Journal_Task</c> that
    /// names <c>Counter</c>; on <c>Journal_SubTask</c> it is null. Setting it
    /// is not enough on its own, because that settings object is a VALUE type:
    /// reflection hands out a boxed copy, so the write has to be put back.</item>
    /// <item>The pieces inside the counter can be switched off in the prefab
    /// as well. Enabling the counter alone turns on an empty box.</item>
    /// <item>And every one of those pieces is painted fully TRANSPARENT —
    /// alpha zero, where the top-level row's are opaque. So the counter ended
    /// up switched on, laid out and holding the right numbers, in invisible
    /// ink. From the outside that is indistinguishable from a counter that was
    /// never built, and it is what the previous three attempts were looking
    /// at.</item>
    /// </list>
    /// <para/>
    /// It reads as an oversight rather than a decision. A deliberate choice not
    /// to show counts on subtasks would not have built the counter, laid it
    /// out, bound both its text fields and gone on filling them in.
    /// <para/>
    /// <b>What this does.</b> Points that one field at the counter already
    /// under the prefab and switches the counter's own pieces on, so the game's
    /// <c>TaskUI</c> does the rest — it enables the counter when the step
    /// counts and fills the numbers from its own state. Nothing here draws,
    /// counts or formats anything.
    /// <para/>
    /// Only when a loaded pack actually has a counting subtask. The prefab is a
    /// shared asset, so the change reaches the game's own quests too for the
    /// rest of the session, and that is worth doing for somebody who has a pack
    /// that needs it and not worth doing to somebody who has not.
    /// </summary>
    internal static class SubtaskCounters
    {
        private const string Tag = "[SMSModForge.PackPlugin] Subtask counters: ";

        /// <summary>The object under a row holding the brackets and the
        /// numbers. Looked up rather than assumed: a rename in a future build
        /// should turn this off, not crash it.</summary>
        private const string CounterChild = "Text/Counter";

        /// <summary>The journal, so the live rows can be read without sweeping
        /// the whole game to find them.</summary>
        private const string JournalRoot = "9_QuestJournal";

        /// <summary>
        /// How long to leave between sweeps for something that is not loaded
        /// yet.
        /// <para/>
        /// Finding an object that may be inactive means walking every loaded
        /// object, which is far too expensive to do per frame — doing exactly
        /// that made the whole game stutter. This constant exists because a
        /// comment saying "be careful" did not.
        /// </summary>
        private const float BetweenSweeps = 2f;

        /// <summary>Set once the row templates have been found AND wired. Not
        /// set by a sweep that found nothing: the templates load with the
        /// journal, which is after the pack, and latching on an empty sweep
        /// would mean never looking again.</summary>
        private static bool _done;

        /// <summary>Whether a pack that needs this is installed.</summary>
        private static bool _wanted;

        private static bool _saidNothingLoaded;
        private static float _lastSweep = float.NegativeInfinity;

        /// <summary>Whether this pack needs it: see
        /// <see cref="QuestCounters.OnASubtask"/>, which lives in Shared so the
        /// rule can be tested rather than merely compiled.</summary>
        public static bool WantedBy(PackManifest manifest)
            => manifest != null && QuestCounters.OnASubtask(manifest.Root);

        /// <summary>Called once per pack at load: this pack needs it.</summary>
        public static void WantedFor(PackManifest manifest, ManualLogSource log)
        {
            if (!WantedBy(manifest)) return;
            _wanted = true;
            Enable(log);
        }

        /// <summary>
        /// Called every frame while packs are loaded.
        /// <para/>
        /// Two bools in the ordinary case, and nothing at all for somebody with
        /// no such pack. What it must never do is sweep for objects on every
        /// call — see <see cref="BetweenSweeps"/>.
        /// </summary>
        public static void Tick(ManualLogSource log)
        {
            if (!_wanted) return;
            if (!_done) { Enable(log); return; }
            Inspect(log);
        }

        /// <summary>Forget everything on scene unload: the templates went with
        /// the scene, and the next one brings its own.</summary>
        public static void Forget()
        {
            _done = false;
            _wanted = false;
            _saidNothingLoaded = false;
            _inspected = false;
            _journal = null;
            _lastSweep = float.NegativeInfinity;
        }

        private static void Enable(ManualLogSource log)
        {
            if (_done || !Due()) return;

            try
            {
                var uis = TaskUis();
                if (uis.Count == 0)
                {
                    // Not an error and not final: the journal's row templates
                    // load with the journal. Said once, then waited for.
                    if (!_saidNothingLoaded)
                    {
                        _saidNothingLoaded = true;
                        log?.LogInfo(Tag + "the journal's row templates are not loaded yet; "
                                     + "waiting for them.");
                    }
                    return;
                }
                _done = true;

                int wired = 0, already = 0, noCounter = 0;
                foreach (var ui in uis)
                {
                    if (ui == null) continue;
                    string where = Where(ui);

                    object active = Get(ui, "m_ActiveElements");
                    if (active == null)
                    {
                        log?.LogInfo(Tag + where + ": no m_ActiveElements; left alone.");
                        continue;
                    }

                    if (Get(active, "m_ActiveIfIsCounter") != null) { already++; continue; }

                    var counter = ui.transform.Find(CounterChild);
                    if (counter == null) { noCounter++; continue; }

                    Set(active, "m_ActiveIfIsCounter", counter.gameObject);

                    // Written BACK, because reflection on a value type hands
                    // out a boxed copy: setting a field on it changes the copy
                    // and throws it away, and the log says "wired" while
                    // nothing on screen has moved. Harmless when it is a class,
                    // so it is done unconditionally rather than behind a type
                    // test somebody has to keep in step with the game.
                    Set(ui, "m_ActiveElements", active);

                    // ...and the pieces inside it, which the prefab also has
                    // switched off. Without this the counter is enabled and
                    // empty: the numbers are in it and the objects holding
                    // them are not drawn.
                    int pieces = 0;
                    for (int i = 0; i < counter.childCount; i++)
                    {
                        var piece = counter.GetChild(i);
                        if (piece.gameObject.activeSelf) continue;
                        piece.gameObject.SetActive(true);
                        pieces++;
                    }

                    // ...and the last of the three, which is why this took
                    // four goes to find: every piece of the subtask's counter
                    // is painted fully TRANSPARENT in the prefab. The
                    // top-level row's are opaque. So the counter was switched
                    // on, laid out, and holding the right numbers, in invisible
                    // ink - which reads from the outside exactly like a counter
                    // that is not there.
                    int shown = 0;
                    foreach (var graphic in counter.GetComponentsInChildren<Graphic>(true))
                    {
                        if (graphic == null || graphic.color.a > 0f) continue;
                        var colour = graphic.color;
                        colour.a = 1f;
                        graphic.color = colour;
                        shown++;
                    }

                    wired++;
                    log?.LogInfo(Tag + where + ": its counter is switched on when the step counts"
                                 + (pieces > 0 ? ", " + pieces + " piece(s) inside it switched on" : "")
                                 + (shown > 0 ? ", " + shown + " painted in rather than left transparent" : "")
                                 + ".");
                }

                log?.LogInfo(Tag + uis.Count + " row template(s) seen - " + wired + " wired, "
                             + already + " already drawn by the game, " + noCounter
                             + " with no counter to switch on.");
            }
            catch (Exception ex)
            {
                _done = true;
                log?.LogWarning(Tag + "could not be switched on (" + ex.Message
                                + "). Counting still works; the number is not drawn.");
            }
        }

        // ── What the journal actually drew ───────────────────────────────

        private static bool _inspected;
        private static Transform _journal;

        /// <summary>
        /// What the rows the journal actually built look like, once.
        /// <para/>
        /// Written because the template being right is not the same as the row
        /// being right: two attempts at this reported success while the screen
        /// did not change, and it was this that found the second cause — a
        /// counter switched on with all its pieces switched off.
        /// <para/>
        /// Scoped to the journal, and the journal itself is looked for at most
        /// once every <see cref="BetweenSweeps"/> seconds until it turns up. An
        /// earlier version swept every component in the game per frame and made
        /// the whole thing stutter.
        /// </summary>
        private static void Inspect(ManualLogSource log)
        {
            if (_inspected) return;

            if (_journal == null)
            {
                if (!Due()) return;
                var root = FindInactive(JournalRoot);
                if (root == null) return;
                _journal = root.transform;
            }

            // Free, and false almost always: the journal is a screen somebody
            // opens rather than something that is up.
            if (!_journal.gameObject.activeInHierarchy) return;

            try
            {
                var rows = new List<Component>();
                foreach (var c in _journal.GetComponentsInChildren<Component>(true))
                    if (c != null && c.GetType().Name == "TaskUI") rows.Add(c);

                if (rows.Count == 0) return;
                _inspected = true;

                log?.LogInfo(Tag + "the journal has " + rows.Count + " row(s) on screen:");
                foreach (var ui in rows)
                {
                    var counter = ui.transform.Find(CounterChild);
                    string state = counter == null
                        ? "no counter at all"
                        : (counter.gameObject.activeSelf ? "counter ON" : "counter off")
                          + ", reading '" + TextUnder(ui.transform, "Text/Counter/Current")
                          + "' of '" + TextUnder(ui.transform, "Text/Counter/Maximum")
                          + "', pieces drawn " + Drawn(counter) + "/" + counter.childCount
                          + ", " + Visible(counter) + " of them not transparent";

                    log?.LogInfo(Tag + "   " + ui.gameObject.name + " '"
                                 + TextUnder(ui.transform, "Text/Title") + "' - " + state);
                }
            }
            catch (Exception ex)
            {
                _inspected = true;
                log?.LogInfo(Tag + "the rows on screen could not be read: " + ex.Message);
            }
        }

        /// <summary>How many of the counter's pieces are painted in something
        /// other than nothing. The subtask's were all transparent, which is
        /// invisible in a way no amount of switching things on would fix.</summary>
        private static int Visible(Transform counter)
        {
            int seen = 0;
            foreach (var graphic in counter.GetComponentsInChildren<Graphic>(true))
                if (graphic != null && graphic.color.a > 0f) seen++;
            return seen;
        }

        private static int Drawn(Transform counter)
        {
            int on = 0;
            for (int i = 0; i < counter.childCount; i++)
                if (counter.GetChild(i).gameObject.activeSelf) on++;
            return on;
        }

        /// <summary>The words in a Text or TextMeshPro object under a row, for
        /// the log. Either kind, because the journal uses both.</summary>
        private static string TextUnder(Transform row, string path)
        {
            var at = row.Find(path);
            if (at == null) return "";
            foreach (var c in at.GetComponents<Component>())
            {
                if (c == null) continue;
                var prop = c.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.PropertyType == typeof(string))
                    return prop.GetValue(c) as string ?? "";
            }
            return "";
        }

        // ── Looking things up, sparingly ─────────────────────────────────

        /// <summary>Whether enough time has passed to sweep again for something
        /// that was not there last time. See <see cref="BetweenSweeps"/>.</summary>
        private static bool Due()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastSweep < BetweenSweeps) return false;
            _lastSweep = now;
            return true;
        }

        /// <summary>
        /// Every <c>TaskUI</c> the game has loaded — the row templates and any
        /// row already built from one.
        /// <para/>
        /// By type rather than by the prefab's name, because the name is not
        /// unique: the first attempt searched by name, found a different object
        /// of that name and reported success while nothing changed. Expensive,
        /// so only ever called from a path that is rate limited.
        /// </summary>
        private static List<Component> TaskUis()
        {
            var found = new List<Component>();
            foreach (var c in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (c == null || c.GetType().Name != "TaskUI") continue;
                found.Add(c);
            }
            return found;
        }

        /// <summary>The named object, switched on or not. Expensive; rate
        /// limited by its caller.</summary>
        private static GameObject FindInactive(string name)
        {
            var direct = GameObject.Find(name);
            if (direct != null) return direct;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;      // an asset, not this scene
                return go;
            }
            return null;
        }

        /// <summary>Where a row template lives, for the log: its name, and
        /// whether it is an asset or something already in the scene.</summary>
        private static string Where(Component ui)
        {
            var go = ui.gameObject;
            string path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path + (go.scene.IsValid() ? " (in the scene)" : " (the template)");
        }

        // ── Reflection, kept to fields whose names and types were read off a
        //    dump of the running game ──────────────────────────────────────

        private static FieldInfo Field(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                                         | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        private static object Get(object target, string name)
        {
            var f = Field(target.GetType(), name);
            return f == null ? null : f.GetValue(target);
        }

        private static void Set(object target, string name, object value)
        {
            var f = Field(target.GetType(), name);
            if (f == null) throw new MissingFieldException(target.GetType().Name, name);
            f.SetValue(target, value);
        }
    }
}
