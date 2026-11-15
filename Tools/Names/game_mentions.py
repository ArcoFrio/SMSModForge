#!/usr/bin/env python3
"""The game's own texts that name one of its characters, each tied to the character it means
(ModForge 1.9.0, renaming the game's characters).

A pack can give one of the game's characters a new name. Over their lines that is exact (the
Actor's name). Inside the game's words it is not: "Anna" in a line is four letters. This finds
every whole-word, same-capitals occurrence of a character's name in the game's texts and ties it:

  - a name only that character has: tied, no question (the bulk);
  - a description standing in for a name ("Cop", "Cool Guy", "Mall Waifu (Blonde)"): never tied.
    In a line they are ordinary words ("the cop"), and the character is renamed over their own
    lines only;
  - a name that is also an English word, or another thing's name (River, Clay, Jack, Phoenix,
    Ghost, Sakura, Frank): tied only where a person read the line and said so, in
    reviewed.tsv beside this script (`<source> <where> <start> <name> <hash> yes|no  reason`, the
    hash being the text's as the plugin hashes it). Anything not yet reviewed is left out and
    listed by --review, hash included.

Texts: the conversations (SMSModForge/Resources/VanillaDialogues), the quests (titles,
descriptions, task names; SMSModForge/Model/VanillaQuests.cs), and the static UI texts of the
running game (a research run's game/texts.tsv; Tools/Research/Variables collect.ps1 makes one).

    python Tools/Names/game_mentions.py --review <texts.tsv>     # counts, and what needs a person
    python Tools/Names/game_mentions.py --write <texts.tsv>      # Shared/VanillaNameMentions.cs
    python Tools/Names/game_mentions.py --stamp <texts.tsv>      # give unhashed verdicts their text's hash

Deterministic. Re-run after a game update: a verdict holds only for the text it was given on - one
whose words changed, by its hash, is waiting for a person again.
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
ACTORS = os.path.join(REPO, "Shared", "VanillaCastActors.cs")
DIALOGUES = os.path.join(REPO, "SMSModForge", "Resources", "VanillaDialogues", "Dialogues")
QUESTS = os.path.join(REPO, "SMSModForge", "Model", "VanillaQuests.cs")
REVIEWED = os.path.join(HERE, "reviewed.tsv")
OUT = os.path.join(REPO, "Shared", "VanillaNameMentions.cs")

WORDS = os.path.join(REPO, "Shared", "VanillaNameWords.cs")


def word_list(name):
    """One of the two lists in Shared/VanillaNameWords.cs, which Validate uses too: the game's texts
    and a pack's are judged by the same names."""
    src = io.open(WORDS, encoding="utf-8-sig").read()
    body = re.search(r"\b" + name + r"\s*=\s*\{(.*?)\};", src, re.S)
    if not body:
        sys.exit("no list " + name + " in " + WORDS)
    return set(re.findall(r'"((?:[^"\\]|\\.)*)"', body.group(1)))


# Descriptions, not names: never tied in the game's words (reviewed by hand, 2026-11-14).
DESCRIPTIONS = word_list("Descriptions")

# Names that are also English words or other things' names: each occurrence needs a person.
NEEDS_A_PERSON = word_list("NeedAPerson")


def key_for(name):
    return "".join(c.lower() for c in name if c.isalnum())


def cast():
    """name as written -> cast key, for every name a character goes by in the game's words."""
    src = io.open(CAST, encoding="utf-8-sig").read()
    busts = re.findall(r'new VanillaBust\("([^"]+)", "([^"]+)"\)', src)
    spoken = dict(re.findall(r'\{ "([^"]+)", "([^"]+)" \}', src))
    names = {}
    for _, filed in busts:
        name = spoken.get(filed, filed)
        names[name] = key_for(name)
    # The name the game's own Actor speaks, where it differs from the cast's ("Benjamin" for
    # Judge Benjamin): that is the name the game's lines call them by.
    actors = io.open(ACTORS, encoding="utf-8").read()
    for key, asset, said in re.findall(r'\{ "([^"]+)", "([^"]+)" \},\s*// [^:]+: speaks \'([^\']+)\'', actors):
        if said not in names:
            names[said] = key
    return names


def texts(ui_tsv):
    """(source, where, text) for every text of the game's that a player reads."""
    for path in sorted(glob.glob(os.path.join(DIALOGUES, "*.json"))):
        d = json.load(io.open(path, encoding="utf-8"))
        for nid, n in sorted(d["nodes"].items(), key=lambda kv: int(kv[0])):
            if isinstance(n.get("text"), str) and n["text"].strip():
                yield "line", d["id"] + "#" + nid, n["text"]

    src = io.open(QUESTS, encoding="utf-8-sig").read()
    unq = lambda s: json.loads('"' + s + '"')
    for m in re.finditer(r'new\("((?:[^"\\]|\\.)*)", "((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)", (?:true|false), -?\d+, new VanillaTask\[\]\s*\{(.*?)\n\s*\}\)', src, re.S):
        quest = unq(m.group(1))
        yield "quest", quest + "#title", unq(m.group(2))
        yield "quest", quest + "#description", unq(m.group(3))
        for t in re.finditer(r'new\((-?\d+), -?\d+, "((?:[^"\\]|\\.)*)"', m.group(4)):
            yield "quest", quest + "#task " + t.group(1), unq(t.group(2))

    if ui_tsv:
        rows = io.open(ui_tsv, encoding="utf-8").read().split("\n")[1:]
        for row in rows:
            c = row.split("\t")
            if len(c) >= 4 and c[3].strip() and c[1] == "scene":
                yield "ui", c[0], c[3].replace("\\n", "\n")


def mentions(text, names):
    """(start, name) for each whole-word, same-capitals name, longest first where two overlap."""
    taken = [False] * len(text)
    found = []
    for name in sorted(names, key=len, reverse=True):
        for m in re.finditer(r"(?<![\w])" + re.escape(name) + r"(?![\w])", text):
            if any(taken[m.start():m.end()]):
                continue
            # Inside a tag or one of the game's {tokens} is not a word anybody reads.
            before = text[:m.start()]
            if before.count("<") > before.count(">") or before.count("{") > before.count("}"):
                continue
            for i in range(m.start(), m.end()):
                taken[i] = True
            found.append((m.start(), name))
    return sorted(found)


def reviewed():
    out = {}
    if not os.path.exists(REVIEWED):
        return out
    for row in io.open(REVIEWED, encoding="utf-8").read().split("\n"):
        if not row.strip() or row.startswith("#"):
            continue
        c = row.split("\t")
        # A verdict is for the text it was given on: kept by that text's hash. One with
        # none (written before 2026-11-15) counts for nothing until --stamp gives it one.
        if len(c) >= 7 and c[4].isdigit():
            out[(c[0], c[1], int(c[2]), c[3], int(c[4]))] = c[5].strip() == "yes"
    return out


def stamp(ui):
    """Give each verdict with no hash the hash of the text it is on now - once, right after the
    verdicts were made, so it is the text they were made on. A verdict whose mention is no longer
    in the texts is left without one, and so counts for nothing."""
    names = cast()
    now = {}
    for source, where, text in texts(ui):
        for start, name in mentions(text, names):
            now[(source, where, start, name)] = fnv(text)
    lines = io.open(REVIEWED, encoding="utf-8").read().split("\n")
    out, stamped, stale = [], 0, 0
    for row in lines:
        c = row.split("\t")
        if row.startswith("# Reviewed by reading"):
            row = row.replace("<start> <name> yes|no", "<start> <name> <hash of the text> yes|no")
        elif row.strip() and not row.startswith("#") and not (len(c) >= 7 and c[4].isdigit()):
            h = now.get((c[0], c[1], int(c[2]), c[3]))
            if h is None:
                stale += 1
            else:
                row = "\t".join(c[:4] + [str(h)] + c[4:])
                stamped += 1
        out.append(row)
    io.open(REVIEWED, "w", encoding="utf-8", newline="\n").write("\n".join(out))
    print("stamped %d, left without a text %d" % (stamped, stale))


def fnv(text):
    """FNV-1a over UTF-16 code units: the plugin hashes the line it finds the same way."""
    h = 2166136261
    data = text.encode("utf-16-le")
    for i in range(0, len(data), 2):
        h ^= data[i] | (data[i + 1] << 8)
        h = (h * 16777619) & 0xFFFFFFFF
    return h


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "--review"
    ui = sys.argv[2] if len(sys.argv) > 2 else None
    if mode == "--stamp":
        stamp(ui)
        return
    names = cast()
    said = reviewed()
    tied, waiting, never = [], [], collections.Counter()
    for source, where, text in texts(ui):
        for start, name in mentions(text, names):
            if name in DESCRIPTIONS:
                never[name] += 1
                continue
            row = (source, where, start, name, names[name], text)
            if name in NEEDS_A_PERSON:
                verdict = said.get((source, where, start, name, fnv(text)))
                if verdict is None:
                    waiting.append(row)
                elif verdict:
                    tied.append(row)
                continue
            tied.append(row)

    sys.stdout.reconfigure(encoding="utf-8")
    by = collections.Counter((r[0], r[3]) for r in tied)
    print("tied: %d (%s)" % (len(tied), ", ".join("%s %d" % (s, n) for s, n in collections.Counter(r[0] for r in tied).items())))
    print("descriptions left alone: %d" % sum(never.values()))
    print("waiting for a person: %d" % len(waiting))
    if mode == "--review":
        for source, where, start, name, key, text in waiting:
            around = text[max(0, start - 60):start + len(name) + 60].replace("\n", " ")
            print("%s\t%s\t%d\t%s\t%d\t?\t%s" % (source, where, start, name, fnv(text), around))
        return

    if waiting:
        sys.exit("review those first (reviewed.tsv)")
    write(tied)


def write(tied):
    per = collections.OrderedDict()
    for source, where, start, name, key, text in tied:
        per.setdefault((source, where, text), []).append((start, len(name), key))
    rows = []
    for (source, where, text), spans in per.items():
        spans_s = ";".join("%d:%d:%s" % s for s in spans)
        rows.append('            "%s\\t%s\\t%d\\t%s",' % (source, where.replace("\\", "\\\\").replace('"', '\\"'), fnv(text), spans_s))
    text = u"""namespace SMSModForge.Shared
{
    /// <summary>
    /// Where the game's own texts name one of its characters, each tied to the character it means
    /// (ModForge 1.9.0, renaming the game's characters).
    /// <para/>
    /// GENERATED by Tools/Names/game_mentions.py. Do not edit by hand: a name that is also a word
    /// is tied only where a person read the line (Tools/Names/reviewed.tsv).
    /// <para/>
    /// One row per text: source (line, quest, ui), where it is (a conversation's path and node id,
    /// a quest's name and part, a UI object's path), a hash of the text as the game has it - a text
    /// that says anything else is left alone - and its names: start:length:character key.
    /// </summary>
    public static class VanillaNameMentions
    {
        public static readonly string[] Rows =
        {
%s
        };

        /// <summary>FNV-1a over UTF-16 code units, the hash the rows carry.</summary>
        public static uint Hash(string text)
        {
            uint h = 2166136261;
            if (text != null)
                foreach (char c in text) { h ^= c; h *= 16777619; }
            return h;
        }
    }
}
""" % "\n".join(rows)
    io.open(OUT, "w", encoding="utf-8", newline="\r\n").write(text)
    print("%d texts -> %s" % (len(rows), OUT))


if __name__ == "__main__":
    main()
