using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.VisualScripting;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// Changes the game's own scenes in place (1.9.0): an entry in a pack's
    /// <c>scenes</c> with a <c>source</c> - <c>4_CG_Manager-Sexy/AmeliaStretching</c> -
    /// dresses that object where it stands instead of building a copy.
    /// <para/>
    /// <b>What stays the game's.</b> The object keeps its name, its place and
    /// every component on it; nothing is removed. Whatever the entry leaves
    /// empty is left as the game made it: no picture keeps the art, no frame
    /// keeps the frame, a sound of <c>None</c> keeps the trigger's own signal.
    /// The game goes on showing the scene from every conversation and script
    /// that always did, and those see the change too, because it is the same
    /// object.
    /// <para/>
    /// <b>What a change does.</b> The picture and the frame go on the same two
    /// renderers a pack scene's do (<see cref="SceneFactory.Dress"/>,
    /// <see cref="SceneFactory.ApplyFrame"/>). The sound is the signal the
    /// scene's own trigger raises as it appears: edited in place to Kiss or
    /// Flash, or switched off for Silent - the instruction stays where it was,
    /// so on one of the game's photos the gallery flag and the payout around it
    /// run exactly as before. A scene whose trigger raises no signal at all (a
    /// few of the photos) is given a small component that raises the one asked
    /// for. A Starmaker photo, for a scene that is not one already, is another
    /// small component that takes the photo whenever the scene appears.
    /// <para/>
    /// <b>Its key.</b> Registered in the pack's scenes under its key, so the
    /// pack's own actions show, hide and swap it as they do one of its own -
    /// with no sound or photo of the registry's own, since the scene's trigger
    /// and the components above already do both on appearing, whoever shows it.
    /// <para/>
    /// Nothing to put back: the scene objects are made again with the level each
    /// time a save loads, and every change here goes with them.
    /// </summary>
    public static class VanillaSceneExtensions
    {
        private const string Tag = "[SMSModForge.PackPlugin] Game scene: ";

        /// <summary>Whether a scene entry changes one of the game's scenes rather
        /// than being one of the pack's own: it has a <c>source</c>, even an empty
        /// one (added in the editor, none picked yet).</summary>
        public static bool IsGameScene(JObject s)
            => s != null && s["source"] != null && s["source"].Type == JTokenType.String;

        /// <summary>Apply every change <paramref name="pack"/> makes to the game's
        /// scenes. Returns how many were applied.</summary>
        public static int ApplyAll(PackManifest pack, PackContext ctx, ManualLogSource log)
        {
            var scenes = pack.Root["scenes"] as JArray;
            if (scenes == null || scenes.Count == 0) return 0;

            string frameRoot = null;
            int done = 0;
            bool keptPrototype = false;
            foreach (var t in scenes)
            {
                var s = t as JObject;
                if (!IsGameScene(s)) continue;
                try
                {
                    // Before the first change: pack scenes are cloned from one of
                    // the game's scenes, which a pack may change.
                    if (!keptPrototype) { keptPrototype = true; SceneFactory.Prototype(log); }
                    if (frameRoot == null) frameRoot = SceneFactory.ResolvePluginFrameRoot() ?? "";
                    if (ApplyOne(s, pack, ctx, frameRoot, log)) done++;
                }
                catch (Exception ex)
                {
                    log.LogError(Tag + "'" + (string)s["key"] + "' in " + pack.PackId + " could not be applied: " + ex.Message);
                }
            }
            if (done > 0) log.LogInfo(Tag + "pack '" + pack.PackId + "' changed " + done + " of the game's scenes.");
            return done;
        }

        private static bool ApplyOne(JObject s, PackManifest pack, PackContext ctx, string frameRoot, ManualLogSource log)
        {
            string key = (string)s["key"];
            string source = (string)s["source"];
            if (string.IsNullOrEmpty(key))
            {
                log.LogWarning(Tag + "a change in " + pack.PackId + " has no key - left out.");
                return false;
            }
            if (string.IsNullOrEmpty(source))
            {
                log.LogWarning(Tag + "'" + key + "' in " + pack.PackId + " has no scene picked - left out.");
                return false;
            }

            var scene = Find(source);
            if (scene == null)
            {
                log.LogWarning(Tag + "'" + key + "' in " + pack.PackId + " changes '" + source
                               + "', which this version of the game does not have - left out.");
                return false;
            }

            var core = scene.transform.Find("Core");
            var art = core != null ? core.Find("Art") : null;
            var artSr = art != null ? art.GetComponent<SpriteRenderer>() : null;

            var trigger = scene.GetComponent<Trigger>();
            var instructions = InstructionsOf(trigger, log);
            bool gamePhoto = IsGamePhoto(instructions);

            // The picture. Fitted to the scene's square like a pack scene's; the
            // art object is put back to its natural scale, which one of the game's
            // scenes halves to draw a double-size picture at the common size.
            string picture = (string)s["sceneSprite"];
            if (!string.IsNullOrEmpty(picture))
            {
                if (!pack.Has(picture))
                    log.LogWarning(Tag + "'" + key + "' in " + pack.PackId + " - '" + picture + "' is not in the pack; the game's picture stays.");
                else if (artSr == null)
                    log.LogWarning(Tag + "'" + key + "' - '" + source + "' has no Core/Art to draw on; the picture is left out.");
                else
                {
                    var was = artSr.sprite;
                    var scale = art.localScale;
                    // An earlier pack's moving picture on this scene would go on
                    // drawing its own frames over this one. Taken off once this
                    // picture is drawn - one that cannot be leaves the earlier's.
                    var earlier = MovingPictures(scene);
                    art.localScale = new Vector3(1f, 1f, scale.z);
                    SceneFactory.Dress(artSr, scene, pack, s, picture, key, log);

                    if (ReferenceEquals(artSr.sprite, was))
                    {
                        // Nothing drawn (a GIF saved without its frames, said
                        // above): the picture there stays, at its own scale.
                        art.localScale = scale;
                    }
                    else
                    {
                        foreach (var moving in earlier) TakeOff(moving);

                        // The game draws a photo's picture in its galleries too -
                        // the same sprite. Those follow, except for a video: its
                        // sprite is only the quad the video plays on. Only for
                        // a photo, which is what the galleries show: a scene that
                        // is not one drawing a photo's very sprite would repaint
                        // that photo's tile, which is not this pack's to change.
                        // (gropepark draws 08_MPark's picture, but as a sprite of
                        // its own - measured 2026-11-15 - so it never did.)
                        if (gamePhoto && SMSModForge.Shared.MediaKinds.KindOf(picture) != SMSModForge.Shared.MediaKinds.Video)
                            Redrawn(was, artSr.sprite);
                    }
                }
            }

            SceneFactory.ApplyFrame(core, pack, s, frameRoot, key, log);

            // The sound: the trigger's own signal, changed where it stands. A
            // missing value is the game's - the editor always writes one, and an
            // older pack has no changes to the game's scenes at all.
            string sound = (string)s["sound"] ?? "None";
            if (sound != "None")
            {
                string wanted = sound == "Kiss" ? "kiss" : sound == "Flash" ? "flash" : null;
                bool placed = false;
                foreach (var ins in instructions)
                {
                    if (!(ins is InstructionLogicRaiseSignal)) continue;
                    if (wanted != null && !placed)
                    {
                        SetSignal(ins, wanted, log);
                        SetEnabled(ins, true, log);
                        placed = true;
                    }
                    else SetEnabled(ins, false, log);
                }
                // One component however many packs ask: the later pack's sound,
                // as with a signal changed in place.
                var own = scene.GetComponent<PackSceneSignal>();
                if (wanted != null && !placed)
                {
                    if (own == null) own = scene.AddComponent<PackSceneSignal>();
                    own.Signal = wanted;
                }
                else if (own != null) own.Signal = null;
            }

            // A Starmaker photo, for a scene that is not one of the game's
            // photos already: those pay out through their own trigger.
            bool photo = s["starmakerPhoto"]?.Type == JTokenType.Boolean && (bool)s["starmakerPhoto"];
            if (photo && !gamePhoto)
            {
                // One photo however many packs make it one: paid once, and
                // recorded for each of them.
                var taker = scene.GetComponent<PackScenePhoto>();
                if (taker == null) taker = scene.AddComponent<PackScenePhoto>();   // Unity's ==, not ??
                taker.Add(key, ctx);
                if (artSr != null && artSr.sprite != null) StarmakerGallery.Include(ctx, key, artSr.sprite);
            }

            ctx.Scenes?.Register(key, scene, null, false);
            return true;
        }

        /// <summary>The components an earlier pack's moving picture put on a scene
        /// (<see cref="SceneFactory.Dress"/>): a GIF's frames, a video's driver.</summary>
        private static List<MonoBehaviour> MovingPictures(GameObject scene)
        {
            var found = new List<MonoBehaviour>();
            foreach (var c in scene.GetComponents<AnimatedSprite>()) if (c != null) found.Add(c);
            foreach (var c in scene.GetComponents<VideoSprite>()) if (c != null) found.Add(c);
            return found;
        }

        /// <summary>Take an earlier pack's moving picture off, with the player and
        /// the speaker a video put on beside it. Switched off at once, so it draws
        /// no frame more before Unity destroys it.</summary>
        private static void TakeOff(MonoBehaviour moving)
        {
            if (moving == null) return;
            moving.enabled = false;
            var video = moving as VideoSprite;
            if (video != null)
            {
                if (video.Player != null) { video.Player.Stop(); UnityEngine.Object.Destroy(video.Player); }
                if (video.Speaker != null) UnityEngine.Object.Destroy(video.Speaker);
            }
            UnityEngine.Object.Destroy(moving);
        }

        // ── The same picture elsewhere in the game ────────────────────────────
        //
        // The Starmaker site's gallery draws each of the game's photos with the
        // very sprite the photo's scene draws (measured in 1.8E: the row and the
        // scene both draw "EverywhereShot_7"), and Anna's own gallery does the
        // same. A photo given a new picture is redrawn there too: every UI image
        // of the game drawing the old sprite gets the new one. Swept for a while
        // after the packs load, since those screens can arrive after them.

        private static readonly Dictionary<Sprite, Sprite> _redrawn = new Dictionary<Sprite, Sprite>();
        private static float _sweepUntil = float.NegativeInfinity;
        private static float _lastSweep = float.NegativeInfinity;
        private static int _swapped;

        /// <summary>How long after the last picture was replaced the galleries
        /// are swept for it, and how often.</summary>
        private const float SweepFor = 60f, BetweenSweeps = 2f;

        private static void Redrawn(Sprite was, Sprite now)
        {
            if (was == null || now == null || ReferenceEquals(was, now)) return;
            // A second pack replacing the first one's picture: a gallery not yet
            // swept still draws the game's, which goes straight to the latest.
            foreach (var game in new List<Sprite>(_redrawn.Keys))
                if (ReferenceEquals(_redrawn[game], was)) _redrawn[game] = now;
            _redrawn[was] = now;
            _sweepUntil = Time.realtimeSinceStartup + SweepFor;
            _lastSweep = float.NegativeInfinity;
        }

        /// <summary>Called every frame; costs one count when no pack replaced one
        /// of the game's pictures.</summary>
        public static void Tick(ManualLogSource log)
        {
            if (_redrawn.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            if (now > _sweepUntil)
            {
                if (_swapped > 0)
                    log?.LogInfo(Tag + _swapped + " of the game's gallery pictures now show a pack's picture.");
                _redrawn.Clear();
                _swapped = 0;
                return;
            }
            if (now - _lastSweep < BetweenSweeps) return;
            _lastSweep = now;

            foreach (var image in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
            {
                if (image == null || image.sprite == null || !image.gameObject.scene.IsValid()) continue;
                Sprite replacement;
                if (!_redrawn.TryGetValue(image.sprite, out replacement)) continue;
                image.sprite = replacement;
                _swapped++;
            }
        }

        /// <summary>Forget on scene unload: the pictures and the scenes go with it.</summary>
        public static void Forget()
        {
            _redrawn.Clear();
            _swapped = 0;
            _sweepUntil = float.NegativeInfinity;
        }

        /// <summary>
        /// The game's scene at <paramref name="path"/>: a child, by name, of the
        /// root object named before the slash. Switched off or not - scenes are
        /// off until shown. Names are unique among a root's children in the game
        /// (Tools/Scenes measured all 1335).
        /// </summary>
        public static GameObject Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int slash = path.IndexOf('/');
            if (slash <= 0 || slash == path.Length - 1) return null;
            string rootName = path.Substring(0, slash);
            string name = path.Substring(slash + 1);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (!loaded.isLoaded) continue;
                foreach (var root in loaded.GetRootGameObjects())
                {
                    if (root == null || root.name != rootName) continue;
                    var t = root.transform;
                    for (int c = 0; c < t.childCount; c++)
                    {
                        var child = t.GetChild(c);
                        if (child.name == name) return child.gameObject;
                    }
                }
            }
            return null;
        }

        /// <summary>Whether a scene's trigger is one of the game's photos': it ends
        /// by switching on PhotoBonus (StarmakerPhotos says why).</summary>
        private static bool IsGamePhoto(IEnumerable<Instruction> instructions)
        {
            foreach (var ins in instructions)
            {
                string title;
                try { title = ins.Title; } catch { continue; }
                if (title != null && title.IndexOf("PhotoBonus", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        // ── Game Creator's insides, by the names its own code uses ────────────

        private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo FieldIn(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Said once: a game update that renames Game Creator's insides
        /// would otherwise leave every sound change doing nothing, silently.</summary>
        private static bool _saidMissing;

        private static void Missing(string what, ManualLogSource log)
        {
            if (_saidMissing || log == null) return;
            _saidMissing = true;
            log.LogWarning(Tag + "this version of the game has no " + what + " where Game Creator 2 kept it, so the game's "
                           + "scenes keep their own sound and photo whatever a pack asks. ModForge needs an update for it.");
        }

        /// <summary>A trigger's instructions, in order; none for no trigger.</summary>
        internal static List<Instruction> InstructionsOf(Trigger trigger, ManualLogSource log = null)
        {
            var list = new List<Instruction>();
            if (trigger == null) return list;
            var outer = FieldIn(trigger.GetType(), "m_Instructions");
            var instructionList = outer?.GetValue(trigger);
            if (instructionList == null)
            {
                if (outer == null) Missing("Trigger.m_Instructions", log);
                return list;
            }
            var inner = FieldIn(instructionList.GetType(), "m_Instructions");
            if (inner == null) { Missing("InstructionList.m_Instructions", log); return list; }
            var items = inner.GetValue(instructionList) as Instruction[];
            if (items != null)
                foreach (var ins in items)
                    if (ins != null) list.Add(ins);
            return list;
        }

        /// <summary>Switch one instruction on or off, as the editor's own Disable
        /// does: it stays in place and is passed over while off.</summary>
        internal static void SetEnabled(Instruction ins, bool on, ManualLogSource log = null)
        {
            var field = FieldIn(ins.GetType(), "m_IsEnabled");
            if (field == null) { Missing("TPolymorphicItem.m_IsEnabled", log); return; }
            field.SetValue(ins, on);
        }

        /// <summary>The signal an Emit Signal instruction raises. A fresh value, so
        /// the name it read before is not kept.</summary>
        internal static void SetSignal(Instruction ins, string name, ManualLogSource log = null)
        {
            var field = FieldIn(ins.GetType(), "m_Signal");
            var text = field == null ? null : FieldIn(field.FieldType, "m_String");
            if (text == null) { Missing("InstructionLogicRaiseSignal.m_Signal.m_String", log); return; }
            object signal = Activator.CreateInstance(field.FieldType);
            text.SetValue(signal, name);
            field.SetValue(ins, signal);
        }

        /// <summary>What an Emit Signal instruction raises now, or null.</summary>
        internal static string SignalOf(Instruction ins)
        {
            var field = FieldIn(ins.GetType(), "m_Signal");
            object signal = field?.GetValue(ins);
            return signal == null ? null : FieldIn(signal.GetType(), "m_String")?.GetValue(signal) as string;
        }

        /// <summary>Whether an instruction is switched on.</summary>
        internal static bool IsOn(Instruction ins) => ins.IsEnabled;
    }

    /// <summary>
    /// Raises a signal as the scene it sits on appears, for one of the game's
    /// scenes whose trigger raises none (1.9.0, <see cref="VanillaSceneExtensions"/>).
    /// </summary>
    public sealed class PackSceneSignal : MonoBehaviour
    {
        public string Signal;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(Signal)) return;
            try { Signals.Emit(new SignalArgs(new PropertyName(Signal), gameObject)); }
            catch (Exception) { /* a sound that cannot play is not worth losing the scene over */ }
        }
    }

    /// <summary>
    /// Takes a Starmaker photo as the scene it sits on appears, for one of the
    /// game's scenes a pack makes a photo (1.9.0) - whoever shows it, as the
    /// game's own photos do through their trigger.
    /// </summary>
    public sealed class PackScenePhoto : MonoBehaviour
    {
        /// <summary>Every pack that makes this scene a photo, in load order, with
        /// its key for the scene.</summary>
        private readonly List<KeyValuePair<string, PackContext>> _takers = new List<KeyValuePair<string, PackContext>>();

        /// <summary>One more pack making this scene a photo; the same pack again
        /// (a reload) replaces its own.</summary>
        public void Add(string key, PackContext ctx)
        {
            if (ctx == null || string.IsNullOrEmpty(key)) return;
            _takers.RemoveAll(t => ReferenceEquals(t.Value, ctx) || t.Value.PackId == ctx.PackId);
            _takers.Add(new KeyValuePair<string, PackContext>(key, ctx));
        }

        /// <summary>How many packs make this scene a photo.</summary>
        public int Count => _takers.Count;

        private void OnEnable()
        {
            if (_takers.Count == 0) return;
            // Every pack remembers its photo was taken; the game pays it out once,
            // as the one photo the player took - through the pack loaded last.
            for (int i = 0; i < _takers.Count - 1; i++)
            {
                try { _takers[i].Value.Vars?.TakePhoto(_takers[i].Key); }
                catch (Exception) { /* one pack's record is not worth the others' */ }
            }
            var last = _takers[_takers.Count - 1];
            StarmakerPhotos.Taken(last.Key, last.Value, last.Value.Log);
        }
    }
}
