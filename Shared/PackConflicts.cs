using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SMSModForge.Shared
{
    /// <summary>
    /// Where two packs change the same field of the same thing of the game's -
    /// for the warning on the main menu's pack list (the author, 1.7.0).
    /// <para/>
    /// Only the game's own things can clash. A pack's scenes, places, sounds
    /// and dialogues are its own, found through it, and two packs can each have
    /// a "beach" without either noticing. What packs share is the game: its
    /// characters, the objects in its levels and screens, the lines of its
    /// conversations, its quests. Where two packs both set one field of one of
    /// those, only one of them can be seen - the one lower in the list
    /// (<see cref="PackOrder"/>) - and the other's author's work silently
    /// does not show.
    /// <para/>
    /// Read from the manifest the menu has already parsed, so finding them
    /// costs one walk over the few parts of each pack that change the game.
    /// Only what a pack explicitly changes counts: a bound object's transform
    /// when it overrides it, a line's field when the pack rewrote it. Listing
    /// an object only to hang something new off it is not a change to it.
    /// <para/>
    /// Two packs setting a field to the same value agree, and are not a
    /// clash - except a file, where the same path in two packs is two
    /// different pictures.
    /// <para/>
    /// Compiled into the plugin, and into the editor's tests, which check it.
    /// </summary>
    public static class PackConflicts
    {
        /// <summary>One field of one of the game's things, as a pack sets it.</summary>
        public sealed class Change
        {
            /// <summary>What it changes, in words a player can follow in the
            /// log: "character Anna", "Lamp in 14_Beach".</summary>
            public string Thing = "";

            /// <summary>Which of its fields; <see cref="Whole"/> for the thing
            /// as a whole (a line taken out, a task added), which clashes with
            /// any other change to it.</summary>
            public string Field = "";

            public string Value = "";
        }

        /// <summary>The field of a change to the whole thing.</summary>
        public const string Whole = "*";

        /// <summary>Two packs setting one field differently. <see cref="Later"/>
        /// loads after <see cref="Earlier"/>, so its change is the one seen.</summary>
        public sealed class Clash
        {
            public string Earlier = "";
            public string Later = "";
            public Change EarlierChange;
            public Change LaterChange;
        }

        private static readonly string[] MediaExtensions = { ".png", ".jpg", ".jpeg", ".ogg", ".wav", ".mp3" };

        // ── What one pack changes ─────────────────────────────────────────

        /// <summary>Every change <paramref name="manifest"/> makes to one of the
        /// game's things.</summary>
        public static List<Change> Of(JObject manifest)
        {
            var changes = new List<Change>();
            if (manifest == null) return changes;
            try
            {
                Characters(manifest["characters"] as JArray, changes);
                Levels(manifest["vanillaExtensions"] as JArray, changes);
                Screens(manifest["uis"] as JArray, changes);
                Conversations(manifest["dialogues"] as JArray, changes);
                Quests(manifest["quests"] as JArray, changes);
            }
            catch (Exception)
            {
                // A manifest shaped in a way this does not expect is not a
                // reason to lose the pack list: it simply reports what it got.
            }
            return changes;
        }

        private static void Add(List<Change> into, string thing, string field, JToken value)
        {
            into.Add(new Change { Thing = thing, Field = field, Value = Text(value) });
        }

        private static string Text(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return "";
            return value.Type == JTokenType.String ? (string)value : value.ToString(Formatting.None);
        }

        /// <summary>Each leaf of a value, under its path - an object is
        /// followed into, anything else is one value.</summary>
        private static void Leaves(List<Change> into, string thing, string path, JToken value)
        {
            var obj = value as JObject;
            if (obj == null)
            {
                Add(into, thing, path, value);
                return;
            }
            foreach (var p in obj.Properties())
                Leaves(into, thing, path + "." + p.Name, p.Value);
        }

        /// <summary>The game's characters: whatever a pack wrote about one is
        /// a change, since the editor keeps only what differs from the game.</summary>
        private static void Characters(JArray characters, List<Change> into)
        {
            if (characters == null) return;
            foreach (var c in characters.OfType<JObject>())
            {
                string who = (string)c["vanillaCharacter"];
                if (string.IsNullOrEmpty(who)) continue;
                string thing = "character " + who;
                foreach (var p in c.Properties())
                {
                    switch (p.Name)
                    {
                        case "key":
                        case "vanillaCharacter":
                        case "bustSource":
                            break;
                        case "outfits":
                            Keyed(into, thing, "outfit", p.Value as JArray, "gameObjectName", "key");
                            break;
                        case "expressions":
                            Keyed(into, thing, "face", p.Value as JArray, "key");
                            break;
                        default:
                            Leaves(into, thing, p.Name, p.Value);
                            break;
                    }
                }
            }
        }

        /// <summary>A list whose entries are told apart by a name of their own.</summary>
        private static void Keyed(List<Change> into, string thing, string what, JArray list, params string[] idFields)
        {
            if (list == null) return;
            foreach (var entry in list.OfType<JObject>())
            {
                string id = idFields.Select(f => (string)entry[f]).FirstOrDefault(v => !string.IsNullOrEmpty(v));
                if (id == null) continue;
                foreach (var p in entry.Properties())
                    if (!idFields.Contains(p.Name))
                        Leaves(into, thing, what + " " + id + ": " + p.Name, p.Value);
            }
        }

        /// <summary>The objects of the game's levels a pack binds to and
        /// overrides.</summary>
        private static void Levels(JArray extensions, List<Change> into)
        {
            if (extensions == null) return;
            foreach (var ext in extensions.OfType<JObject>())
            {
                string level = Short((string)ext["source"]);
                if (string.IsNullOrEmpty(level)) continue;
                Bound(into, ext["gameObjects"] as JArray, "", level, screen: false);
            }
        }

        /// <summary>The objects of the game's screens a pack binds to and
        /// overrides.</summary>
        private static void Screens(JArray uis, List<Change> into)
        {
            if (uis == null) return;
            foreach (var ui in uis.OfType<JObject>())
            {
                string screen = (string)ui["source"];
                if (string.IsNullOrEmpty(screen)) continue;
                Bound(into, ui["nodes"] as JArray, "", "screen " + screen, screen: true);
            }
        }

        /// <summary>
        /// Walk an object tree for the game's objects it binds to. Only what
        /// the pack opted into overriding is a change; an object of the pack's
        /// own, and everything under it, is the pack's.
        /// </summary>
        private static void Bound(List<Change> into, JArray nodes, string parentPath, string where, bool screen)
        {
            if (nodes == null) return;
            foreach (var n in nodes.OfType<JObject>())
            {
                if (!((bool?)n["bind"] ?? false)) continue;
                string name = (string)n["name"] ?? "";
                string path = parentPath.Length == 0 ? name : parentPath + " > " + name;
                string thing = path + " in " + where;

                if (screen)
                {
                    if ((bool?)n["overrideRect"] ?? false) Add(into, thing, "rect", n["rect"]);
                    if ((bool?)n["overrideImage"] ?? false) Add(into, thing, "image", n["image"]);
                    if ((bool?)n["overrideText"] ?? false) Add(into, thing, "text", n["text"]);
                }
                else if ((bool?)n["overrideTransform"] ?? false)
                {
                    var t = new JObject
                    {
                        ["x"] = n["x"], ["y"] = n["y"], ["rotationZ"] = n["rotationZ"],
                        ["scaleX"] = n["scaleX"], ["scaleY"] = n["scaleY"],
                    };
                    Add(into, thing, "transform", t);
                }

                // Whether it is there at all: switched once, or driven by
                // conditions - two packs driving it both ways fight over it.
                var gate = n["activeConditions"] as JArray;
                if ((bool?)n["overrideActive"] ?? false) Add(into, thing, "active", n["startActive"] ?? true);
                else if (gate != null && gate.Count > 0) Add(into, thing, "active", gate);

                Bound(into, n["children"] as JArray, path, where, screen);
            }
        }

        /// <summary>The lines of the game's conversations a pack rewrites or
        /// takes out.</summary>
        private static void Conversations(JArray dialogues, List<Change> into)
        {
            if (dialogues == null) return;
            foreach (var d in dialogues.OfType<JObject>())
            {
                string source = (string)d["source"];
                if (string.IsNullOrEmpty(source)) continue;
                foreach (var node in (d["nodes"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var fields = node["overrides"] as JArray;
                    if (fields == null || fields.Count == 0) continue;
                    string thing = "line " + (string)node["id"] + " of " + source;
                    foreach (var f in fields.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)))
                        Add(into, thing, f, node[f]);
                }
                foreach (var id in (d["removedNodes"] as JArray ?? new JArray()))
                    Add(into, "line " + (string)id + " of " + source, Whole, "removed");
            }
        }

        /// <summary>The game's quests: a task a pack adds under a key, and the
        /// description a pack gives one of the game's tasks.</summary>
        private static void Quests(JArray quests, List<Change> into)
        {
            if (quests == null) return;
            foreach (var q in quests.OfType<JObject>())
            {
                string source = (string)q["source"];
                if (string.IsNullOrEmpty(source)) continue;
                foreach (var t in (q["tasks"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string key = (string)t["key"];
                    if (!string.IsNullOrEmpty(key))
                        Add(into, "task " + key + " of quest " + source, Whole, t);
                }
                foreach (var h in (q["vanillaTasks"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string task = (string)h["task"];
                    string said = (string)h["questDescription"];
                    if (!string.IsNullOrEmpty(task) && !string.IsNullOrEmpty(said))
                        Add(into, "task " + task + " of quest " + source, "questDescription", said);
                }
            }
        }

        /// <summary>A level as people say it: <c>vanilla:14_Beach</c> is
        /// 14_Beach.</summary>
        private static string Short(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            int colon = token.IndexOf(':');
            return colon >= 0 ? token.Substring(colon + 1) : token;
        }

        // ── Where packs clash ────────────────────────────────────────────

        /// <summary>
        /// Every clash between <paramref name="packs"/>, which are given in
        /// load order with what each changes.
        /// </summary>
        public static List<Clash> Find(IList<KeyValuePair<string, List<Change>>> packs)
        {
            var clashes = new List<Clash>();
            if (packs == null || packs.Count < 2) return clashes;

            // Each thing, with every pack's changes to it.
            var byThing = new Dictionary<string, List<KeyValuePair<int, Change>>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < packs.Count; i++)
                foreach (var c in packs[i].Value ?? new List<Change>())
                {
                    List<KeyValuePair<int, Change>> list;
                    if (!byThing.TryGetValue(c.Thing, out list)) byThing[c.Thing] = list = new List<KeyValuePair<int, Change>>();
                    list.Add(new KeyValuePair<int, Change>(i, c));
                }

            foreach (var thing in byThing.Values)
            {
                if (thing.Select(t => t.Key).Distinct().Count() < 2) continue;

                // Field by field: each pack's say on one field, against every
                // other pack's say on the same field.
                foreach (var field in thing.Where(t => t.Value.Field != Whole)
                                           .GroupBy(t => t.Value.Field, StringComparer.Ordinal))
                {
                    var says = field.ToList();
                    for (int x = 0; x < says.Count; x++)
                        for (int y = 0; y < says.Count; y++)
                        {
                            var a = says[x];
                            var b = says[y];
                            if (a.Key >= b.Key || Agree(a.Value.Value, b.Value.Value)) continue;
                            clashes.Add(Make(packs, a, b));
                        }
                }

                // A change to the thing as a whole - a line taken out, a task
                // added - against every other pack that changes it at all:
                // once per pair of packs, not once per field.
                foreach (var whole in thing.Where(t => t.Value.Field == Whole).ToList())
                {
                    var others = thing.Where(t => t.Key != whole.Key)
                                      .GroupBy(t => t.Key).Select(g => g.First());
                    foreach (var other in others)
                    {
                        // Two wholes are met from both sides; keep one.
                        if (other.Value.Field == Whole && other.Key < whole.Key) continue;
                        if (other.Value.Field == Whole && Agree(whole.Value.Value, other.Value.Value)) continue;
                        clashes.Add(whole.Key < other.Key ? Make(packs, whole, other) : Make(packs, other, whole));
                    }
                }
            }
            return clashes;
        }

        private static Clash Make(IList<KeyValuePair<string, List<Change>>> packs,
                                  KeyValuePair<int, Change> earlier, KeyValuePair<int, Change> later)
        {
            return new Clash
            {
                Earlier = packs[earlier.Key].Key, Later = packs[later.Key].Key,
                EarlierChange = earlier.Value, LaterChange = later.Value,
            };
        }

        /// <summary>Whether two values say the same - which a file never does
        /// across two packs, whatever its path.</summary>
        private static bool Agree(string a, string b)
        {
            if (!string.Equals(a ?? "", b ?? "", StringComparison.Ordinal)) return false;
            string v = a ?? "";
            return !MediaExtensions.Any(e => v.EndsWith(e, StringComparison.OrdinalIgnoreCase)
                                             || v.IndexOf(e + "\"", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>The packs <paramref name="packId"/> clashes with, in load
        /// order, each once.</summary>
        public static List<string> With(IEnumerable<Clash> clashes, string packId)
        {
            var others = new List<string>();
            foreach (var c in clashes ?? Enumerable.Empty<Clash>())
            {
                string other = string.Equals(c.Earlier, packId, StringComparison.OrdinalIgnoreCase) ? c.Later
                             : string.Equals(c.Later, packId, StringComparison.OrdinalIgnoreCase) ? c.Earlier
                             : null;
                if (other != null && !others.Contains(other, StringComparer.OrdinalIgnoreCase)) others.Add(other);
            }
            return others;
        }
    }
}
