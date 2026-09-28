using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Puts a pack's photo scenes into the Starmaker site's gallery.
    /// <para/>
    /// <b>What the gallery is.</b> One row —
    /// <c>Starmaker_Core/Gallery/Scrollarea/CoreGallery</c>, a
    /// <c>HorizontalLayoutGroup</c> with a <c>ContentSizeFitter</c> inside a
    /// <c>ScrollRect</c>. The game's photos are its children, and the row grows
    /// to whatever is in it and scrolls. So there is no table of slots to find
    /// a free one in: a pack photo is another child, and the row gets longer.
    /// <para/>
    /// Each of the game's children carries a trigger that switches itself off
    /// unless its own <c>Gallery[n]</c> flag is set. A pack photo is not earned
    /// that way and has no flag of its own, so the clone's trigger is thrown
    /// away and this decides whether the row is shown — from the pack's own
    /// save data, which is where a pack's progress belongs.
    /// <para/>
    /// <b>Nothing is asked of the author.</b> They tick "Treat as a Starmaker
    /// photo" on the scene. Which photos have been taken is kept in the pack's
    /// store under an internal name, declared by the runtime, never shown as a
    /// variable and never something to keep in step by hand.
    /// <para/>
    /// Appended after the game's own, so the order of the gallery a player
    /// already knows does not move.
    /// </summary>
    internal static class StarmakerGallery
    {
        private const string Tag = "[SMSModForge.PackPlugin] Starmaker gallery: ";

        /// <summary>The row, by the path it was found at. Walked step by step
        /// rather than searched for by leaf name: more than one thing in this
        /// game is called "Gallery", and the wrong one would take the rows
        /// without complaint and show nothing.</summary>
        private static readonly string[] RowPath =
            { "9_MainCanvas", "Starmaker", "Starmaker_Core", "Gallery", "Scrollarea", "CoreGallery" };

        /// <summary>What a row this plugin added is called, so its own rows can
        /// be told from the game's and from a previous load's.</summary>
        private const string RowPrefix = "SMSModForge_Photo_";

        /// <summary>One pack's photo scenes, remembered so the rows can be made
        /// when the gallery turns up - it is part of a screen that loads after
        /// the packs do.</summary>
        private sealed class Wanted
        {
            public PackContext Context;
            public readonly List<KeyValuePair<string, Sprite>> Scenes =
                new List<KeyValuePair<string, Sprite>>();
        }

        private static readonly List<Wanted> _wanted = new List<Wanted>();
        private static bool _built;
        private static bool _saidWaiting;

        /// <summary>The row, once found. Kept because finding it means walking
        /// every loaded object - the Starmaker screen is switched off until
        /// somebody opens it - and doing that per frame is what stutters a
        /// game rather than what slows one down slightly.</summary>
        private static Transform _row;

        /// <summary>How long to leave between sweeps for a row that is not
        /// loaded yet.</summary>
        private const float BetweenSweeps = 2f;
        private static float _lastSweep = float.NegativeInfinity;

        /// <summary>
        /// Remember a scene that belongs in the gallery. Called as each pack's
        /// scenes are built, with the sprite already loaded onto the scene, so
        /// nothing is read from the archive twice.
        /// </summary>
        public static void Include(PackContext ctx, string sceneKey, Sprite art)
        {
            if (ctx == null || string.IsNullOrEmpty(sceneKey) || art == null) return;

            var forPack = _wanted.Find(w => ReferenceEquals(w.Context, ctx));
            if (forPack == null) { forPack = new Wanted { Context = ctx }; _wanted.Add(forPack); }
            forPack.Scenes.Add(new KeyValuePair<string, Sprite>(sceneKey, art));

            // A scene added after the rows were built - a pack loading late -
            // gets a fresh pass rather than being left out.
            _built = false;
        }

        /// <summary>
        /// Build the rows once the gallery exists, and keep each row's
        /// visibility in step with whether its photo has been taken.
        /// <para/>
        /// Called every frame. Costs one bool for anybody with no photo scenes
        /// installed, which is every pack written so far.
        /// </summary>
        public static void Tick(ManualLogSource log)
        {
            if (_wanted.Count == 0) return;

            if (_row == null)
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastSweep < BetweenSweeps) return;
                _lastSweep = now;

                _row = FindRow();
                if (_row == null)
                {
                    if (!_saidWaiting)
                    {
                        _saidWaiting = true;
                        log?.LogInfo(Tag + "the Starmaker gallery is not loaded yet; waiting for it.");
                    }
                    return;
                }
            }

            if (!_built) Build(_row, log);

            // Only while the gallery is up. Its rows cannot change while
            // nobody is looking at them, and this is a per-frame call.
            if (_row.gameObject.activeInHierarchy) Refresh(_row);
        }

        /// <summary>Drop everything on scene unload: the rows go with the
        /// scene, and the sprites belong to packs that are being unloaded.</summary>
        public static void Forget()
        {
            _wanted.Clear();
            _built = false;
            _saidWaiting = false;
            _row = null;
            _lastSweep = float.NegativeInfinity;
        }

        private static void Build(Transform row, ManualLogSource log)
        {
            _built = true;

            var template = Template(row);
            if (template == null)
            {
                log?.LogWarning(Tag + "the gallery has no row to copy, so pack photos cannot be "
                                + "added to it. The photos themselves still work.");
                return;
            }

            int made = 0;
            foreach (var pack in _wanted)
            {
                foreach (var scene in pack.Scenes)
                {
                    string name = RowPrefix + pack.Context.PackId + "_" + scene.Key;
                    if (row.Find(name) != null) continue;      // already there from an earlier pass

                    GameObject made1;
                    try { made1 = UnityEngine.Object.Instantiate(template.gameObject, row); }
                    catch (Exception ex)
                    {
                        log?.LogWarning(Tag + "'" + scene.Key + "' in " + pack.Context.PackId
                                        + " could not be added to the gallery: " + ex.Message);
                        continue;
                    }

                    made1.name = name;

                    // The copied trigger hides the row unless one of the GAME's
                    // gallery flags is set. A pack photo has no such flag and
                    // is not earned that way, so it goes - and this decides,
                    // from the pack's own save data.
                    foreach (var c in made1.GetComponents<Component>())
                        if (c != null && c.GetType().Name == "Trigger")
                            UnityEngine.Object.Destroy(c);

                    var image = made1.GetComponent<Image>();
                    if (image != null) image.sprite = scene.Value;

                    made1.SetActive(false);        // until Refresh says otherwise
                    made++;
                }
            }

            if (made > 0)
                log?.LogInfo(Tag + made + " pack photo(s) added to the end of the gallery.");
        }

        /// <summary>
        /// Show each pack row when its photo has been taken in this save.
        /// <para/>
        /// Read every frame rather than written when the photo is taken,
        /// because the flag can also arrive from a save being loaded, and a row
        /// that only followed the action would be missing from the gallery
        /// until the photo was taken again.
        /// </summary>
        private static void Refresh(Transform row)
        {
            foreach (var pack in _wanted)
            {
                var store = pack.Context != null ? pack.Context.Vars : null;
                if (store == null) continue;

                foreach (var scene in pack.Scenes)
                {
                    var made = row.Find(RowPrefix + pack.Context.PackId + "_" + scene.Key);
                    if (made == null) continue;

                    bool taken = store.IsPhotoTaken(scene.Key);
                    if (made.gameObject.activeSelf != taken) made.gameObject.SetActive(taken);
                }
            }
        }

        /// <summary>One of the game's own rows, to copy the size, material and
        /// mask from. Its own, not one of ours, or a second load would copy a
        /// copy.</summary>
        private static Transform Template(Transform row)
        {
            for (int i = 0; i < row.childCount; i++)
            {
                var child = row.GetChild(i);
                if (child.name.StartsWith(RowPrefix, StringComparison.Ordinal)) continue;
                if (child.GetComponent<Image>() != null) return child;
            }
            return null;
        }

        private static Transform FindRow()
        {
            var at = Find(RowPath[0]);
            for (int i = 1; at != null && i < RowPath.Length; i++)
            {
                var next = at.transform.Find(RowPath[i]);
                at = next == null ? null : next.gameObject;
            }
            return at == null ? null : at.transform;
        }

        /// <summary>The named object, switched on or not: the Starmaker screen
        /// is off until the player opens it.</summary>
        private static GameObject Find(string name)
        {
            var direct = GameObject.Find(name);
            if (direct != null) return direct;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != name) continue;
                if (!go.scene.IsValid()) continue;
                return go;
            }
            return null;
        }
    }
}
