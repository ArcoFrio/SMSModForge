using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Everything the Starmaker photo system is, read out of the running game.
    /// <para/>
    /// <b>What this is for.</b> A pack wants a tick box that says "this photo is
    /// taken", the way the game's own photos are unlocked — and the game does
    /// that through a Game Creator Trigger, whose instructions live in a
    /// <c>[SerializeReference]</c> graph. A ripped project does not carry those
    /// graphs; the running build has deserialised them. So the three questions
    /// this has to answer can only be answered from inside the game:
    /// <list type="number">
    /// <item>Which object carries the Trigger — the photos manager itself, or
    /// an <c>EnableTrigger</c> under its <c>Core</c> — and what that Trigger
    /// does when it fires.</item>
    /// <item>How the good and bad versions of a photo are told apart, which is
    /// the trait system: whichever variable or condition picks between them.</item>
    /// <item>What else moves when a photo is unlocked — the gallery rows, and
    /// the social-media points the gallery is scored by.</item>
    /// </list>
    /// <para/>
    /// Nothing is switched on, moved or written to. The photo screens are off
    /// at load and stay off: running their OnEnable is game code that may do
    /// anything, and this is a read.
    /// <para/>
    /// Debug builds only — see the csproj, which compiles this file on the
    /// Debug configuration alone.
    /// </summary>
    internal static class PhotoSystemDump
    {
        /// <summary>The manager the photos hang off, as the game names it.</summary>
        private const string PhotosRoot = "4_CG_Manager-Photos";

        /// <summary>
        /// The gallery, by the path it was found at. Written out step by step
        /// rather than searched for by leaf name, because more than one thing
        /// in this game is called "Gallery" and the wrong one would dump
        /// cleanly and answer nothing.
        /// </summary>
        private static readonly string[] GalleryPath =
            { "9_MainCanvas", "Starmaker", "Starmaker_Core", "Gallery" };

        /// <summary>
        /// What the photos manager is expected to hold.
        /// <para/>
        /// Named rather than counted. A dump that found the root and walked one
        /// object deep looks exactly like a complete one until something says
        /// what it was looking for — and the whole question here is whether the
        /// Trigger is on the root or under Core, so both have to be asked for
        /// by name and the answer read off what was and was not found.
        /// </summary>
        private static readonly string[] Expected = { "Core", "EnableTrigger" };

        /// <summary>Game Creator's namespaces, so the walk can say which
        /// components are the game's own logic and which are Unity's.</summary>
        private const string GameCreator = "GameCreator.Runtime";

        public static void Write(ManualLogSource log)
        {
            var photos = Find(PhotosRoot);
            var gallery = FindByPath(GalleryPath);

            if (photos == null && gallery == null)
            {
                log?.LogWarning("[SMSModForge.PackPlugin] F6: neither '" + PhotosRoot + "' nor "
                                + string.Join("/", GalleryPath) + " is in this scene. "
                                + "Press F6 in CoreGameScene, with a save loaded.");
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string shapeFile = Path.Combine(
                BepInEx.Paths.BepInExRootPath, "SMSModForge-photos-shape-" + stamp + ".json");

            var run = new Walk();
            var json = new StringBuilder();
            json.Append("{\n  \"taken\": \"")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append("\",\n  \"objects\": [");
            bool first = true;
            if (photos != null) { run.Object(json, photos.transform, PhotosRoot, first); first = false; }
            if (gallery != null) run.Object(json, gallery.transform, string.Join("/", GalleryPath), first);
            json.Append("\n  ],\n  \"report\": ");
            run.Report(json);
            json.Append("\n}\n");

            File.WriteAllText(shapeFile, json.ToString(), new UTF8Encoding(false));
            log?.LogInfo("[SMSModForge.PackPlugin] F6: " + run.Objects + " object(s), "
                         + run.Components + " component(s) written to " + shapeFile);

            // Read before the big file is opened: this is what says whether the
            // Trigger is on the root or under Core, and it is two lines.
            foreach (string line in run.Controls()) log?.LogInfo("[SMSModForge.PackPlugin] F6: " + line);

            if (photos != null)
                log?.LogInfo("[SMSModForge.PackPlugin] F6: photos scripts written to "
                             + ScriptDump.WriteUnder(photos, log, "photos-scripts"));
            else
                log?.LogWarning("[SMSModForge.PackPlugin] F6: no GameObject named '" + PhotosRoot + "'.");

            if (gallery != null)
                log?.LogInfo("[SMSModForge.PackPlugin] F6: gallery scripts written to "
                             + ScriptDump.WriteUnder(gallery, log, "photos-gallery-scripts"));
            else
                log?.LogWarning("[SMSModForge.PackPlugin] F6: " + string.Join("/", GalleryPath)
                                + " is not in this scene.");

            // What every photo switches on when it is unlocked. Named by
            // instance from inside the triggers and living OUTSIDE both
            // subtrees, so the first dump found the reference and not the
            // object - this is where the social-media points are scored.
            var bonus = Find(PhotoBonusName);
            if (bonus != null)
                log?.LogInfo("[SMSModForge.PackPlugin] F6: " + PhotoBonusName + " scripts written to "
                             + ScriptDump.WriteUnder(bonus, log, "photos-bonus-scripts"));
            else
                log?.LogWarning("[SMSModForge.PackPlugin] F6: no GameObject named '" + PhotoBonusName
                                + "' - what a photo awards could not be read.");

            // The trait system, which is what picks the good photo over the bad
            // one. It is not under either subtree - it is global state - so it
            // is looked for where global state lives, and COUNTED, because
            // "there is no such object" and "the search never matched" write
            // the same empty file.
            var playerData = Find(PlayerDataName);
            int traits;
            string traitFile = ScriptDump.WriteUnderInNamespace(
                playerData, VariablesNamespace, log, "photos-variables", out traits);
            log?.LogInfo("[SMSModForge.PackPlugin] F6: " + traits + " variable script(s) on "
                         + PlayerDataName + " written to " + traitFile);
            if (playerData == null)
                log?.LogWarning("[SMSModForge.PackPlugin] F6: no GameObject named '" + PlayerDataName
                                + "' - the trait variables could not be read.");
            else if (traits == 0)
                log?.LogWarning("[SMSModForge.PackPlugin] F6: " + PlayerDataName + " carries no "
                                + VariablesNamespace + " script. The traits are kept somewhere else; "
                                + "press F11 with the Starmaker screen open to find where.");
        }

        /// <summary>Where the game keeps its global variables, read off the
        /// quest dump rather than assumed.</summary>
        private const string PlayerDataName = "11_PlayerData";

        /// <summary>The object every photo's trigger switches on, read off the
        /// first dump: each one ends with "Set Active PhotoBonus to True".</summary>
        private const string PhotoBonusName = "PhotoBonus";

        private const string VariablesNamespace = "GameCreator.Runtime.Variables";

        /// <summary>
        /// The named object, switched on or not.
        /// <para/>
        /// <c>GameObject.Find</c> skips inactive objects and every one of these
        /// is inactive at load — so without this the dump reports the things it
        /// exists to read as absent.
        /// </summary>
        private static GameObject Find(string name)
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

        /// <summary>Walk a named path from a root, so the right one of several
        /// objects sharing a leaf name is the one dumped.</summary>
        private static GameObject FindByPath(string[] path)
        {
            var at = Find(path[0]);
            for (int i = 1; at != null && i < path.Length; i++)
            {
                var next = at.transform.Find(path[i]);
                at = next == null ? null : next.gameObject;
            }
            return at;
        }

        // ── One walk ─────────────────────────────────────────────────────

        /// <summary>
        /// The tree, with every component on every object and whether the
        /// object is switched on.
        /// <para/>
        /// A script dump writes one entry per MonoBehaviour, so an object with
        /// no script on it never appears in one — and the shape is half of what
        /// is being asked for here, since the question is which object a tick
        /// box would have to switch.
        /// </summary>
        private sealed class Walk
        {
            public int Objects;
            public int Components;

            private readonly HashSet<string> _found = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<string> _triggers = new List<string>();

            public void Object(StringBuilder json, Transform t, string path, bool first)
            {
                Objects++;
                if (_found.Add(t.name)) { }

                if (!first) json.Append(',');
                json.Append("\n    { \"path\": ").Append(Quote(path))
                    .Append(", \"active\": ").Append(t.gameObject.activeSelf ? "true" : "false")
                    .Append(", \"activeInHierarchy\": ").Append(t.gameObject.activeInHierarchy ? "true" : "false")
                    .Append(", \"components\": [");

                var components = t.GetComponents<Component>();
                bool firstComponent = true;
                foreach (var c in components)
                {
                    Components++;
                    string type = c == null ? "(missing script)" : c.GetType().FullName;
                    if (!firstComponent) json.Append(", ");
                    json.Append(Quote(type));
                    firstComponent = false;

                    // The line somebody is actually looking for. Anything Game
                    // Creator calls a Trigger, wherever it turned up, listed on
                    // its own so it does not have to be found in the tree.
                    if (type.StartsWith(GameCreator, StringComparison.Ordinal)
                        && type.IndexOf("Trigger", StringComparison.Ordinal) >= 0)
                        _triggers.Add(path + "  ->  " + type
                                      + (t.gameObject.activeSelf ? "" : "   (switched off)"));
                }
                json.Append("] }");

                for (int i = 0; i < t.childCount; i++)
                    Object(json, t.GetChild(i), path + "/" + t.GetChild(i).name, false);
            }

            /// <summary>What was expected, and what of it turned up. The
            /// control: without it a dump that walked one object deep reads
            /// exactly like a complete one.</summary>
            public void Report(StringBuilder json)
            {
                json.Append("{ \"expected\": [");
                for (int i = 0; i < Expected.Length; i++)
                {
                    if (i > 0) json.Append(", ");
                    json.Append("{ \"name\": ").Append(Quote(Expected[i]))
                        .Append(", \"found\": ").Append(_found.Contains(Expected[i]) ? "true" : "false")
                        .Append(" }");
                }
                json.Append("], \"triggers\": [");
                for (int i = 0; i < _triggers.Count; i++)
                {
                    if (i > 0) json.Append(", ");
                    json.Append(Quote(_triggers[i]));
                }
                json.Append("] }");
            }

            /// <summary>The two or three lines worth reading in the log before
            /// anybody opens a file.</summary>
            public IEnumerable<string> Controls()
            {
                foreach (string name in Expected)
                    if (!_found.Contains(name))
                        yield return "expected an object named '" + name + "' and there is none.";

                if (_triggers.Count == 0)
                    yield return "no Game Creator Trigger anywhere under either subtree. "
                                 + "Whatever unlocks a photo is not a Trigger on these objects.";

                foreach (string line in _triggers) yield return "Trigger: " + line;
            }

            private static string Quote(string s)
            {
                var made = new StringBuilder("\"");
                foreach (char c in s ?? "")
                {
                    if (c == '"' || c == '\\') made.Append('\\').Append(c);
                    else if (c == '\n') made.Append("\\n");
                    else if (c < 0x20) made.Append("\\u").Append(((int)c).ToString("x4"));
                    else made.Append(c);
                }
                return made.Append('"').ToString();
            }
        }
    }
}
