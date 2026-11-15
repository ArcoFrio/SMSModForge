using BepInEx.Logging;
using GameCreator.Runtime.Common;
using GameCreator.Runtime.Dialogue;
using GameCreator.Runtime.Dialogue.UnityUI;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace SMSModForge.PackPlugin
{
    /// <summary>
    /// The new names packs give the game's characters (1.9.0), and the game's own
    /// Actors renamed to match.
    /// <para/>
    /// One table for every pack, because the character is everybody's: a pack that
    /// renames Anna renames her over the game's lines and over every pack's lines she
    /// speaks, not only its own. Two packs renaming her: the one loaded later wins,
    /// the rule colours follow too, and the log says so.
    /// <para/>
    /// Keyed by the game's character, never by name, which is what keeps a pack's own
    /// character of the same name out of it: the Androids pack's Sakura is a character
    /// of that pack, not the game's Sakura, and nothing here can reach her.
    /// <para/>
    /// The game's lines speak through the game's own Actor assets, found by the asset's
    /// name (<see cref="SMSModForge.Shared.VanillaCastActors"/>) - by the name it speaks
    /// it could be found once, and never again after its first rename. What each said
    /// before is put back by <see cref="Restore"/> when the scene goes, so a pack removed
    /// between two loads leaves nobody renamed. A pack's lines speak through Actors the
    /// plugin mints, which take the name from here as they are made (<see
    /// cref="DialogueNodeWriter.SetActor"/>); every pack is read before any are built.
    /// </summary>
    internal static class NewNames
    {
        private const string Tag = "[SMSModForge.PackPlugin] Names: ";

        private sealed class Given
        {
            public string Name;
            public string PackId;
            public bool Found;
            public bool Complained;
        }

        private struct Was
        {
            public Actor Actor;
            public PropertyGetString Name;
        }

        private static readonly Dictionary<string, Given> _given =
            new Dictionary<string, Given>(System.StringComparer.OrdinalIgnoreCase);
        private static readonly List<Was> _wasThere = new List<Was>();
        private static Object _triedFor;

        /// <summary>The game's character an entry of a manifest stands for, by the
        /// editor's key, or "" for a character of the pack's own.</summary>
        public static string CastKeyOf(JObject character)
        {
            if (character == null || (string)character["bustSource"] != "Vanilla") return "";
            string vanilla = (string)character["vanillaCharacter"];
            if (string.IsNullOrEmpty(vanilla)) return "";
            // Through the spoken name, so a pack migrated before a correction - which
            // stored "Master" - still reaches Master Zhen.
            return SMSModForge.Shared.VanillaCastData.KeyFor(SMSModForge.Shared.VanillaCastData.SpokenName(vanilla));
        }

        /// <summary>Read one pack's new names. Called for every pack in load order,
        /// before anything is built.</summary>
        public static void Collect(PackManifest pack, ManualLogSource log)
        {
            var characters = pack != null ? pack.Characters : null;
            if (characters == null) return;

            foreach (var ch in characters)
            {
                var c = ch as JObject;
                string key = CastKeyOf(c);
                if (key.Length == 0) continue;
                string name = ((string)c["newName"] ?? "").Trim();
                if (name.Length == 0) continue;

                Given was;
                if (_given.TryGetValue(key, out was) && was.PackId != pack.PackId && was.Name != name)
                    log?.LogWarning(Tag + was.PackId + " calls " + key + " '" + was.Name + "' and " + pack.PackId
                                    + " calls them '" + name + "': " + pack.PackId + "'s is shown, being loaded later.");
                _given[key] = new Given { Name = name, PackId = pack.PackId };
            }
        }

        /// <summary>The name shown for the game's character <paramref name="castKey"/>:
        /// a pack's new name for them, or null for the game's own.</summary>
        public static string NameOf(string castKey)
        {
            Given given;
            return !string.IsNullOrEmpty(castKey) && _given.TryGetValue(castKey, out given) ? given.Name : null;
        }

        /// <summary>How many of the game's characters have a new name.</summary>
        public static int Count { get { return _given.Count; } }

        /// <summary>Rename the game's own Actors. Asked again when a conversation
        /// starts, for as long as one is still missing: an Actor asset is loaded
        /// because something in the scene points at it, which is certain by the time a
        /// conversation is on screen and only likely before.</summary>
        public static void ApplyToGame(ManualLogSource log)
        {
            if (_given.Count == 0) return;

            Dictionary<string, Actor> assets = null;
            int renamed = 0;
            foreach (var pair in _given)
            {
                var given = pair.Value;
                if (given.Found) continue;

                string asset = SMSModForge.Shared.VanillaCastActors.Of(pair.Key);
                if (asset == null)
                {
                    // Nobody in the game's own scenes speaks as them: only the packs'
                    // lines carry their name, and those take it from the table.
                    given.Found = true;
                    continue;
                }

                if (assets == null) assets = TheGamesActors(log);
                Actor actor;
                if (!assets.TryGetValue(asset, out actor) || actor == null)
                {
                    if (!given.Complained)
                    {
                        given.Complained = true;
                        log?.LogInfo(Tag + "the game's actor for " + pair.Key + " (" + asset + ") is not loaded yet;"
                                     + " tried again when a conversation starts.");
                    }
                    continue;
                }

                string said;
                try { said = actor.GetName(Args.EMPTY) ?? ""; }
                catch (System.Exception) { said = ""; }

                var old = RuntimeActorFactory.ReadNameProperty(actor);
                if (old == null || !RuntimeActorFactory.WriteNameProperty(actor, GetStringPackText.For(given.Name, given.PackId)))
                {
                    given.Found = true;
                    log?.LogWarning(Tag + "could not rename the game's actor " + asset + "; its lines show '" + said + "'.");
                    continue;
                }
                _wasThere.Add(new Was { Actor = actor, Name = old });
                given.Found = true;
                renamed++;

                // The colorizer matches the name AS DRAWN, so the game's colour for
                // "Anna" would not reach "Annie" by itself.
                if (said.Length > 0) _carried[said] = given.Name;
                try { SpeechColorApplier.CarryColour(said, given.Name, log); }
                catch (System.Exception ex) { log?.LogWarning(Tag + "'" + given.Name + "' could not take " + said + "'s colour: " + ex.Message); }
            }

            if (renamed > 0)
                log?.LogInfo(Tag + renamed + " of the game's characters renamed in its own scenes.");
        }

        /// <summary>Try again for anything still missing, once per conversation. A
        /// count check once everything is found, the ordinary case.</summary>
        public static void Tick(ManualLogSource log)
        {
            if (_given.Count == 0) return;
            var speech = SpeechUI.Current;
            if (speech == null || ReferenceEquals(_triedFor, speech)) return;
            _triedFor = speech;
            if (Unreached() > 0) ApplyToGame(log);

            // The colours again, for a colorizer that was not there at load. Asking
            // twice changes nothing: a name that already has its colour is skipped.
            foreach (var pair in _carried)
            {
                try { SpeechColorApplier.CarryColour(pair.Key, pair.Value, log, sayNone: false); }
                catch (System.Exception) { }
            }
        }

        /// <summary>The game's name, and the new one that should draw in its colour.</summary>
        private static readonly Dictionary<string, string> _carried =
            new Dictionary<string, string>(System.StringComparer.Ordinal);

        private static int Unreached()
        {
            int n = 0;
            foreach (var g in _given.Values) if (!g.Found) n++;
            return n;
        }

        /// <summary>Every name back the way the game had it, and the table emptied
        /// for the packs of the next load.</summary>
        public static void Restore()
        {
            foreach (var was in _wasThere)
                if (was.Actor != null) RuntimeActorFactory.WriteNameProperty(was.Actor, was.Name);
            _wasThere.Clear();
            _given.Clear();
            _carried.Clear();
            _triedFor = null;
        }

        /// <summary>The game's Actor assets by their own names. Ours are skipped:
        /// they carry a prefix, and a pack's actor could otherwise share a name.</summary>
        private static Dictionary<string, Actor> TheGamesActors(ManualLogSource log)
        {
            var found = new Dictionary<string, Actor>(System.StringComparer.Ordinal);
            try
            {
                foreach (var a in Resources.FindObjectsOfTypeAll<Actor>())
                {
                    if (a == null || a.name == null) continue;
                    if (a.name.StartsWith(RuntimeActorFactory.MintedPrefix)) continue;
                    if (!found.ContainsKey(a.name)) found[a.name] = a;
                }
            }
            catch (System.Exception ex)
            {
                log?.LogWarning(Tag + "actor scan failed: " + ex.Message);
            }
            return found;
        }
    }
}
