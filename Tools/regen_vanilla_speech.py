#!/usr/bin/env python3
"""Rebuild `Shared/VanillaSpeech.cs` from two runtime dumps of the game.

WHERE IT COMES FROM
    Neither of these facts survives in anything the editor can read. The
    game's Actor assets carry no SerializeField references an extractor can
    follow, and the speaker-name colours live in a private list on a component
    that only exists once a conversation has started. So both are read out of a
    running game by the plugin's own diagnostics, in a Debug build:

        F10 -> SMSModForge-dialogue-actors-<stamp>.json   every loaded Actor
        F11 -> SMSModForge-scripts-<stamp>.json           every script on screen

    Press F11 while a vanilla conversation is on screen, or the speech UI has
    not been instantiated yet and the colour list is not there to read.

WHAT IT FINDS
    Three things, one per question the editor could not answer:

    1. HOW A VANILLA CHARACTER PULLS A FACE. Not by name. Each character owns a
       number under the `Expressions` global-name variable asset -- Adrian's is
       `B-Expression`, Alice's is `goth-expression` -- and every bust on screen
       carries a trigger that copies its own character's number into
       `Currently-Chosen` and switches the matching child of `Expressions` on.
       The numbering is completely uniform: 0 neutral, 1 Happy, 2 Angry,
       3 Sad, 4 Flirty.

       That is why this matters rather than being trivia: the trigger fires on
       a global-name variable change, so activating an expression child by hand
       is undone the moment anything writes a global variable. Setting the
       number is how an expression STAYS.

    2. HOW A VANILLA CHARACTER SOUNDS. GC2's typewriter turned out to be per
       character rather than per speech skin: a frequency (25, 40 or 45) and a
       gibberish pitch range that is genuinely different per person -- 0.2-0.5
       for the bouncer, 1.6-2 for Elfina. So there are real defaults to offer
       an author, which was the open question.

    3. WHAT COLOUR THE GAME WRITES A NAME IN. 37 of them, matched against the
       rendered speaker name, case-insensitively.

WHAT IS DELIBERATELY LEFT OUT
    Characters absent from the shipped catalog, for the same reason the bust
    expressions generator leaves them out: some exist in the game with no
    content showing them, they are excluded from Shared/VanillaCastData.cs on
    purpose, and a generated dataset is a side door into the repository.

    The player. `You` has a name colour and a voice, but is not a character an
    author picks in the Characters tab.

Usage:
    python Tools/regen_vanilla_speech.py --actors <F10 json> --scripts <F11 json>
    python Tools/regen_vanilla_speech.py --actors ... --scripts ... --check
"""

import argparse
import collections
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, ".."))
CATALOG = os.path.join(REPO, "Shared", "VanillaCastData.cs")
OUT = os.path.join(REPO, "Shared", "VanillaSpeech.cs")

# The numbering every character's expressions use. Asserted rather than
# assumed: this script refuses to write if a character disagrees.
EXPECTED = {"neutral": 0, "Happy": 1, "Angry": 2, "Sad": 3, "Flirty": 4}

# GC2 writes this into a fresh Expressions list. An actor carrying only this
# has no expressions configured, which is not the same as having none.
PLACEHOLDER = "my-expression"


# ---------------------------------------------------------------- the catalog

def key_for(name):
    """The editor's key derivation, kept identical to VanillaCastData.KeyFor."""
    return "".join(c.lower() for c in name if c.isalnum())


def catalogue():
    """Character key -> spoken name, for the characters the tool offers."""
    src = io.open(CATALOG, encoding="utf-8-sig").read()
    busts = re.findall(r'new VanillaBust\("([^"]+)",\s*"([^"]+)"\)', src)
    if not busts:
        sys.exit("could not read any busts from %s -- refusing to write a "
                 "dataset with no catalog to check it against" % CATALOG)
    spoken = dict(re.findall(r'\{\s*"([^"]+)",\s*"([^"]+)"\s*\}',
                             src.split("SpokenNames =")[1]))
    out = collections.OrderedDict()
    for _go, group in busts:
        name = spoken.get(group, group)
        out.setdefault(key_for(name), name)
    return out


# ------------------------------------------------------------- reading a dump

class Dump(object):
    """One dump, with $ref resolution.

    The writer emits each object once and refers back to it afterwards, so a
    reader that does not resolve refs sees empty instruction lists and
    concludes an expression does nothing.
    """

    def __init__(self, path):
        self.data = json.load(io.open(path, encoding="utf-8-sig"))
        self.byid = {}
        self._index(self.data)

    def _index(self, node):
        if isinstance(node, dict):
            if "$id" in node:
                self.byid[node["$id"]] = node
            for v in node.values():
                self._index(v)
        elif isinstance(node, list):
            for v in node:
                self._index(v)

    def deref(self, node):
        if isinstance(node, dict) and set(node) == {"$ref"}:
            return self.byid.get(node["$ref"], {})
        return node if isinstance(node, dict) else {}

    def dig(self, node, *path):
        for step in path:
            node = self.deref(node).get(step)
        return self.deref(node)

    def value(self, node, *path):
        """Like dig, but for a leaf that is not an object."""
        for step in path[:-1]:
            node = self.deref(node).get(step)
        return self.deref(node).get(path[-1])


# ------------------------------------------------------------------- extracts

def read_actors(path):
    """Every Actor asset the game had loaded, keyed the editor's way."""
    d = Dump(path)
    found = collections.OrderedDict()
    for obj in d.data["objects"]:
        # A pack's own actors are in the scene too. They are not the subject.
        if obj["object"].startswith("SMSModForge_Actor_"):
            continue
        f = obj["fields"]
        name = d.value(f, "m_Actant", "m_Name", "m_Property", "m_Value")
        if not name:
            continue

        tw = d.dig(f, "m_Typewriter")
        pitch = d.dig(tw, "m_Pitch")
        gib = d.dig(tw, "m_Gibberish", "m_Property")
        clip = gib.get("m_Value")
        skin = f.get("m_OverrideSpeechSkin") or {}

        expressions = collections.OrderedDict()
        variables = collections.Counter()
        for entry in d.dig(f, "m_Expressions", "m_Expressions").get("$items") or []:
            entry = d.deref(entry)
            eid = d.value(entry, "m_Id", "m_String")
            if not eid or eid == PLACEHOLDER:
                continue
            steps = d.dig(entry, "m_InstructionsOnStart", "m_Instructions",
                          "m_Instructions").get("$items") or []
            for step in steps:
                step = d.deref(step)
                if step.get("$type") != "InstructionArithmeticSetNumber":
                    continue
                field = d.dig(step, "m_Set", "m_Property", "m_Variable")
                asset = (field.get("m_Variable") or {}).get("$unity")
                var = d.value(field, "m_Name", "m_String")
                val = d.value(step, "m_From", "m_Property", "m_Value")
                if asset != "Expressions" or not var or val is None:
                    continue
                expressions[eid] = int(val)
                variables[var] += 1

        found.setdefault(key_for(name), []).append(dict(
            asset=obj["object"], name=name,
            expressions=expressions, variables=variables,
            use=bool(tw.get("m_UseTypewriter")),
            frequency=tw.get("m_Frequency"),
            pitch_min=pitch.get("x"), pitch_max=pitch.get("y"),
            gibberish=(clip or {}).get("$unity") if isinstance(clip, dict) else None,
            skin=skin.get("$unity")))
    return found


def read_colors(path):
    """The speaker-name colours, as #RRGGBB keyed the editor's way."""
    d = Dump(path)
    out = {}
    seen = 0
    for obj in d.data["objects"]:
        if obj["script"] != "TMPWordColorizer":
            continue
        seen += 1
        for pair in d.dig(obj["fields"], "wordColors").get("$items") or []:
            pair = d.deref(pair)
            c = d.dig(pair, "color")
            out[key_for(pair.get("word") or "")] = "#%02X%02X%02X" % tuple(
                min(255, max(0, int(round(c.get(ch, 0) * 255)))) for ch in "rgb")
    if not seen:
        sys.exit("no TMPWordColorizer in %s -- press F11 with a vanilla "
                 "conversation on screen, or the speech UI has not been built "
                 "yet and there is nothing to read." % path)
    return out


# ------------------------------------------------------------------ emitting

def cs(s):
    if s is None:
        return "null"
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def number(v):
    """A float literal C# will not widen to double."""
    return ("%g" % float(v or 0)) + "f"


def line(r):
    return ("            new Speaker(%s, %s, %s, %s,\n"
            "                        %s, %d, %s, %s, %s, %s, %s),"
            % (cs(r["key"]), cs(r["name"]), cs(r["variable"]), cs(r["expressions"]),
               "true" if r["use"] else "false", int(r["frequency"] or 0),
               number(r["pitch_min"]), number(r["pitch_max"]),
               cs(r["gibberish"]), cs(r["skin"]), cs(r["color"])))


# ----------------------------------------------------------------------- main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--actors", required=True, help="the F10 dialogue-actors dump")
    ap.add_argument("--scripts", required=True, help="the F11 scripts dump")
    ap.add_argument("--check", action="store_true",
                    help="report what would be written, write nothing")
    args = ap.parse_args()

    chars = catalogue()
    actors = read_actors(args.actors)
    colors = read_colors(args.scripts)

    rows = []
    notes = []
    for k, name in chars.items():
        candidates = actors.get(k)
        if not candidates:
            continue
        if len(candidates) > 1:
            notes.append("%s has %d actor assets (%s); took the first"
                         % (name, len(candidates),
                            ", ".join(a["asset"] for a in candidates)))
        a = candidates[0]

        # The numbering is the assumption everything downstream rests on.
        for eid, val in a["expressions"].items():
            if eid in EXPECTED and val != EXPECTED[eid]:
                sys.exit("%s sets %s to %d, not the uniform %d. The runtime "
                         "maps names to numbers; decide what this means before "
                         "regenerating." % (name, eid, val, EXPECTED[eid]))

        variable = None
        if a["variables"]:
            variable = a["variables"].most_common(1)[0][0]
            if len(a["variables"]) > 1:
                # Phoenix's Happy sets GABRIEL's number. Recorded as the
                # majority rather than faithfully, because reproducing a
                # copy-paste means an author asking for one character's face
                # changes somebody else's.
                notes.append("%s drives %d different variables (%s); took %s"
                             % (name, len(a["variables"]),
                                ", ".join("%s x%d" % kv
                                          for kv in a["variables"].most_common()),
                                variable))

        rows.append(dict(
            key=k, name=name, variable=variable,
            expressions=" ".join("%s=%d" % kv for kv in a["expressions"].items()),
            use=a["use"], frequency=a["frequency"],
            pitch_min=a["pitch_min"], pitch_max=a["pitch_max"],
            gibberish=a["gibberish"], skin=a["skin"], color=colors.get(k)))

    with_var = sum(1 for r in rows if r["variable"])
    with_col = sum(1 for r in rows if r["color"])
    print("%d catalogued characters, %d matched an actor "
          "(%d can pull a face, %d have a name colour), %d actors matched nobody"
          % (len(chars), len(rows), with_var, with_col,
             sum(1 for k in actors if k not in chars)))
    for n in notes:
        print("   note: %s" % n)
    if not rows:
        sys.exit("nothing matched -- refusing to write an empty dataset")

    text = TEMPLATE % {"count": len(rows), "with_var": with_var,
                       "with_col": with_col,
                       "rows": "\n".join(line(r) for r in rows)}

    if args.check:
        print("(--check: nothing was written)")
        return
    io.open(OUT, "w", encoding="utf-8-sig", newline="\r\n").write(text)
    print("wrote %s" % OUT)


TEMPLATE = '''namespace SMSModForge.Shared
{
    /// <summary>
    /// How the game's own characters speak: the face they can pull, the voice
    /// they pull it in, and the colour their name is written in.
    /// <para/>
    /// GENERATED by Tools/regen_vanilla_speech.py from two dumps taken out of
    /// a running game -- see that file for what they are and how to take them.
    /// Do not edit by hand.
    /// <para/>
    /// None of this is readable from the game's files. The Actor assets carry
    /// no references an extractor can follow, and the name colours live in a
    /// private list on a component that does not exist until a conversation
    /// has started.
    /// <para/>
    /// Compiled into both projects, for the usual reason: the editor offers an
    /// author a character's real defaults, and the runtime has to reproduce
    /// them. Two copies would drift the first time the game changed one.
    /// <para/>
    /// %(count)d of the game's characters are here; %(with_var)d can pull a
    /// face and %(with_col)d have a name colour of their own. The rest of the
    /// cast is in <see cref="VanillaCastData"/>: a bust in a crowd is a
    /// character an author can dress, but not one the game gave a voice.
    /// </summary>
    public static class VanillaSpeech
    {
        /// <summary>One of the game's characters, as a speaker.</summary>
        public sealed class Speaker
        {
            public Speaker(string key, string name, string expressionVariable,
                           string expressions, bool useTypewriter, int frequency,
                           float pitchMin, float pitchMax, string gibberish,
                           string speechSkin, string nameColor)
            {
                Key = key;
                Name = name;
                ExpressionVariable = expressionVariable;
                UseTypewriter = useTypewriter;
                Frequency = frequency;
                PitchMin = pitchMin;
                PitchMax = pitchMax;
                Gibberish = gibberish;
                SpeechSkin = speechSkin;
                NameColor = nameColor;

                var names = new System.Collections.Generic.List<string>();
                // Case-insensitively, because an author types these names.
                var values = new System.Collections.Generic.Dictionary<string, int>(
                    System.StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(expressions))
                {
                    foreach (string pair in expressions.Split(' '))
                    {
                        int split = pair.LastIndexOf('=');
                        if (split <= 0) continue;
                        string id = pair.Substring(0, split);
                        int value;
                        if (!int.TryParse(pair.Substring(split + 1), out value)) continue;
                        names.Add(id);
                        values[id] = value;
                    }
                }
                Expressions = names.ToArray();
                _values = values;
            }

            /// <summary>The editor's key for this character.</summary>
            public string Key { get; private set; }

            /// <summary>What the game calls them out loud.</summary>
            public string Name { get; private set; }

            /// <summary>
            /// The number, under the <c>Expressions</c> global-name variable
            /// asset, that decides which face this character is wearing --
            /// or null for a character the game never gave one.
            /// <para/>
            /// Every bust on screen carries a trigger watching for a
            /// global-name variable change; on one, it copies its own
            /// character's number into <c>Currently-Chosen</c> and switches the
            /// matching child of <c>Expressions</c> on. So switching a child on
            /// by hand lasts exactly until the next global variable is written,
            /// anywhere, by anything. Setting this is how a face stays.
            /// </summary>
            public string ExpressionVariable { get; private set; }

            /// <summary>The faces this character has, as the game names them.
            /// Empty for a character with none configured.</summary>
            public string[] Expressions { get; private set; }

            private readonly System.Collections.Generic.Dictionary<string, int> _values;

            /// <summary>The number that means this face, or -1 for a name this
            /// character does not have.</summary>
            public int ValueOf(string expression)
            {
                int value;
                return expression != null && _values.TryGetValue(expression, out value)
                     ? value : -1;
            }

            /// <summary>Whether the game types this character's lines out.</summary>
            public bool UseTypewriter { get; private set; }

            /// <summary>Characters per second.</summary>
            public int Frequency { get; private set; }

            /// <summary>The pitch range the gibberish clip is played at. This is
            /// what makes one character sound unlike another.</summary>
            public float PitchMin { get; private set; }

            /// <summary>The top of that range.</summary>
            public float PitchMax { get; private set; }

            /// <summary>The audio clip the typewriter chirps, by name, or null
            /// for GC2's own default.</summary>
            public string Gibberish { get; private set; }

            /// <summary>The speech skin this character overrides to, or null for
            /// whichever one the conversation is using.</summary>
            public string SpeechSkin { get; private set; }

            /// <summary>The colour the game writes this name in, as #RRGGBB, or
            /// null for the ordinary one.</summary>
            public string NameColor { get; private set; }
        }

        /// <summary>Every character the game gave a voice, in catalog order.</summary>
        public static readonly Speaker[] All =
        {
%(rows)s
        };

        private static readonly System.Collections.Generic.Dictionary<string, Speaker> ByKey =
            BuildIndex();

        private static System.Collections.Generic.Dictionary<string, Speaker> BuildIndex()
        {
            var map = new System.Collections.Generic.Dictionary<string, Speaker>(
                System.StringComparer.OrdinalIgnoreCase);
            foreach (var s in All) map[s.Key] = s;
            return map;
        }

        /// <summary>This character as a speaker, or null for one the game never
        /// gave a voice -- a bust in a crowd, or a pack's own character.</summary>
        public static Speaker For(string characterKey)
        {
            Speaker found;
            return !string.IsNullOrEmpty(characterKey)
                && ByKey.TryGetValue(characterKey, out found) ? found : null;
        }

        /// <summary>Whether the game gave this character a voice.</summary>
        public static bool Has(string characterKey)
        {
            return For(characterKey) != null;
        }
    }
}
'''


if __name__ == "__main__":
    main()
