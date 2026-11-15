#!/usr/bin/env python3
"""Write `Shared/VanillaCastActors.cs`: which of the game's Actor assets speaks for each of
its characters, by ASSET name.

Why by asset name: the plugin renames a game character by changing the name on the game's
own Actor (ModForge 1.9.0). Finding that Actor by the name it speaks works once and then
stops working, because after the rename it speaks the new name. The asset's own name never
changes.

Input: the F10 actor dump of a running game (`SMSModForge-dialogue-actors-<stamp>.json`, written
by a Debug plugin into the game folder; see Tools/regen_vanilla_speech.py), the cast
(`Shared/VanillaCastData.cs`) and the game's conversations
(`SMSModForge/Resources/VanillaDialogues`).

A character is linked to an Actor when:
  1. the Actor speaks the character's name (the editor's key rule on both sides), or
  2. nobody is linked by 1, the Actor's spoken name is a whole word of the character's name
     ("Benjamin" in "Judge Benjamin"), and the character's bust appears in EVERY conversation
     that Actor speaks in, and exactly one character passes. Printed, so a person sees each one.
Everything else is left out: a character with no Actor has no line of the game's to rename.

    python Tools/Names/cast_actors.py <dialogue-actors dump.json>

Deterministic: the same inputs write the same file. Re-run after a game update.
"""
import collections
import glob
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
CAST = os.path.join(REPO, "Shared", "VanillaCastData.cs")
DIALOGUES = os.path.join(REPO, "SMSModForge", "Resources", "VanillaDialogues", "Dialogues")
OUT = os.path.join(REPO, "Shared", "VanillaCastActors.cs")
MINTED = "SMSModForge_Actor_"


def key_for(name):
    return "".join(c.lower() for c in name if c.isalnum())


def resolve(data):
    byid = {}

    def index(node):
        if isinstance(node, dict):
            if "$id" in node:
                byid[node["$id"]] = node
            for v in node.values():
                index(v)
        elif isinstance(node, list):
            for v in node:
                index(v)

    index(data)

    def deref(node):
        if isinstance(node, dict) and set(node) == {"$ref"}:
            return byid.get(node["$ref"], {})
        return node if isinstance(node, dict) else {}

    return deref


def spoken_names(dump_path):
    data = json.load(io.open(dump_path, encoding="utf-8-sig"))
    deref = resolve(data)
    out = {}
    for obj in data["objects"]:
        if obj["object"].startswith(MINTED):
            continue
        name = deref(deref(deref(obj["fields"]).get("m_Actant")).get("m_Name"))
        value = deref(name.get("m_Property")).get("m_Value")
        if isinstance(value, str) and value.strip():
            out[obj["object"]] = value
    return out


def cast():
    src = io.open(CAST, encoding="utf-8-sig").read()
    busts = re.findall(r'new VanillaBust\("([^"]+)", "([^"]+)"\)', src)
    spoken = dict(re.findall(r'\{ "([^"]+)", "([^"]+)" \}', src))
    people = collections.OrderedDict()
    for go, filed in busts:
        name = spoken.get(filed, filed)
        people.setdefault(key_for(name), {"name": name, "busts": []})["busts"].append(go)
    return people


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    actors = spoken_names(sys.argv[1])
    people = cast()

    by_key = collections.defaultdict(list)
    for asset, said in actors.items():
        by_key[key_for(said)].append(asset)

    linked = collections.OrderedDict()
    for k, p in people.items():
        assets = sorted(by_key.get(k, []))
        if len(assets) > 1:
            sys.exit("%s is spoken by %d actors (%s): decide which by hand" % (p["name"], len(assets), assets))
        if assets:
            linked[k] = (assets[0], "speaks '%s'" % actors[assets[0]])

    # Rule 2. Which busts each conversation shows, and who speaks in it.
    shows = []
    for path in sorted(glob.glob(os.path.join(DIALOGUES, "*.json"))):
        raw = io.open(path, encoding="utf-8").read()
        nodes = json.loads(raw)["nodes"].values()
        shows.append(({n.get("actor") for n in nodes}, raw))

    taken = {a for a, _ in linked.values()}
    for asset in sorted(actors):
        if asset in taken:
            continue
        said = actors[asset]
        word = re.compile(r"(?<![\w])" + re.escape(said) + r"(?![\w])")
        talks = [raw for speakers, raw in shows if asset in speakers]
        if not talks:
            continue
        passing = []
        for k, p in people.items():
            if k in linked or not word.search(p["name"]):
                continue
            seen = lambda raw: any(re.search(r'"%s"' % re.escape(b), raw) for b in p["busts"])
            if all(seen(raw) for raw in talks):
                passing.append(k)
        if len(passing) == 1:
            k = passing[0]
            linked[k] = (asset, "speaks '%s', and %s's bust is in all %d of its conversations"
                         % (said, people[k]["name"], len(talks)))
            print("linked by rule 2: %s -> %s (%s)" % (people[k]["name"], asset, linked[k][1]))

    rows = "\n".join('            { "%s", "%s" },   // %s: %s' % (k, a, people[k]["name"], why)
                     for k, (a, why) in sorted(linked.items()))
    text = u"""namespace SMSModForge.Shared
{
    /// <summary>
    /// Which of the game's Actor assets speaks for each of its characters, by the
    /// asset's own name, keyed the editor's way.
    /// <para/>
    /// GENERATED by Tools/Names/cast_actors.py from an F10 actor dump and the cast. Do
    /// not edit by hand.
    /// <para/>
    /// By ASSET name because a renamed character's Actor speaks the new name: found by
    /// the name it speaks, it could be found once and never again.
    /// %d of the %d characters; the rest have no line of the game's own.
    /// </summary>
    public static class VanillaCastActors
    {
        public static readonly System.Collections.Generic.Dictionary<string, string> AssetOf =
            new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
%s
        };

        /// <summary>The Actor asset that speaks for <paramref name="castKey"/>, or null.</summary>
        public static string Of(string castKey)
        {
            string asset;
            return castKey != null && AssetOf.TryGetValue(castKey, out asset) ? asset : null;
        }
    }
}
""" % (len(linked), len(people), rows)
    io.open(OUT, "w", encoding="utf-8", newline="\r\n").write(text)
    print("%d of %d characters linked -> %s" % (len(linked), len(people), OUT))


if __name__ == "__main__":
    main()
