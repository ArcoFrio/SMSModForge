"""What the game does to its own quests, and from where.

WHY
    The quest catalogue (VanillaQuests.cs) says what each of the game's quests
    IS: its tasks, their order, how each finishes. It says nothing about what
    the game DOES with them - what starts a quest, what completes, counts or
    fails each task, what resets a quest, what asks about one - and an author
    changing a quest needs exactly that, the way a conversation shows "the game
    plays this when".

    A task never starts by itself in this game: Game Creator has no
    instruction for it, and a task starts only through its quest's structure
    (the quest starting, the task before it finishing, its parent starting).
    That part is worked out from the catalogue by the editor. Everything else
    lives in scripts, and this file finds them.

WHERE IT LOOKS
    1. The shipped dialogue catalogue: every quest step inside a
       conversation's lines, and in the scripts that play a conversation.
       Each is recorded with the line it sits on, the lines leading to it and
       the conditions on those, and what the conversation itself needs to
       play.
    2. The F8 extraction's quest-references file (Debug plugin): every OTHER
       script in the game that names a quest, switched on or not, including
       the prefabs the game spawns. Each is recorded with its object, its
       trigger event, the conditions and branches guarding the step, and the
       conversations played just before and just after it in the same list.

       Given (2), the scripts that play conversations are read from it and
       not from (1): (1) records a script's steps once per conversation the
       script can play, so a step before eight conversations came out eight
       times, and "before" or "after" each of them, all at once.

    Without (2) the output says so in "complete": false, and the editor says
    so beside every list, because a list with the scene half missing reads
    exactly like a full one.

    All of the game's quest steps are in CoreGameScene: its other two scenes
    (GameStart, LoadingScreen) hold none (checked 2026-09-17 by the step type
    names Game Creator writes into each scene file). Their totals in that
    scene - 109 task completes, 44 starts, 15 counts, 9 resets, 3 fails - are
    what (1) and (2) together must come to.

USAGE
    python BuildQuestReferences.py --dialogues <catalog dir>
        [--scene SMSModForge-quest-references-*.json]
        --quests <VanillaQuests.cs> -o <out.json>
"""

from __future__ import annotations

import argparse
import glob
import io
import json
import os
import re
import sys
from collections import Counter, OrderedDict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from BuildDialogueCatalog import Dump, Starts  # noqa: E402

# What each of the game's quest instructions does to a quest or task.
DOES = OrderedDict((
    ("InstructionQuestsActivate", "starts"),
    ("InstructionQuestsDeactivate", "resets"),
    ("InstructionQuestsTaskComplete", "completes"),
    ("InstructionQuestTaskValue", "counts"),
    ("InstructionQuestsTaskFail", "fails"),
    ("InstructionQuestsTaskAbandon", "abandons"),
))

# Asking about a quest or a task, rather than changing it.
CHECK_PREFIX = "ConditionQuests"

# Any other instruction about a quest (tracking, for one) is recorded as that.
OTHER_PREFIXES = ("InstructionQuest",)


def quest_of(fields):
    """The quest a translated step names, and the task id when it names one."""
    if not isinstance(fields, dict):
        return None, None
    task = fields.get("m_Task")
    if isinstance(task, dict):
        quest = task.get("m_Quest")
        name = quest.get("name") if isinstance(quest, dict) else None
        return name, task.get("m_TaskId")
    quest = fields.get("m_Quest")
    if isinstance(quest, dict):
        return quest.get("name"), None
    return None, None


def what_of(step_type):
    if step_type in DOES:
        return DOES[step_type]
    if step_type.startswith(CHECK_PREFIX):
        return "checks"
    if step_type.startswith(OTHER_PREFIXES):
        return "other"
    return None


def brief(step):
    """A step as the editor lists it: its type, its title, and - for a counter
    or a check - the part that says how much or what state."""
    out = OrderedDict((("type", step.get("type")),))
    if step.get("title"):
        out["title"] = step["title"]
    fields = step.get("fields") or {}
    for key in ("m_Value", "m_State", "m_Operation"):
        if key in fields:
            out[key[2:].lower()] = fields[key]
    return out


# -- conversations -------------------------------------------------------

def gates_in(value):
    """A branch inside a step: an object holding a condition list and an
    instruction list side by side. The conditions guard the instructions."""
    conditions = instructions = None
    for inner in value.values():
        if isinstance(inner, dict):
            if isinstance(inner.get("conditions"), list) and len(inner) == 1:
                conditions = inner["conditions"]
            elif isinstance(inner.get("instructions"), list) and len(inner) == 1:
                instructions = inner["instructions"]
    return conditions, instructions


def walk_steps(value, gates, found):
    """Every quest step under a translated step list, with the conditions of
    every branch it sits inside."""
    if isinstance(value, list):
        for inner in value:
            walk_steps(inner, gates, found)
        return
    if not isinstance(value, dict):
        return

    step_type = value.get("type")
    if isinstance(step_type, str):
        what = what_of(step_type)
        if what:
            found.append((what, value, list(gates)))

    conditions, instructions = gates_in(value)
    if conditions is not None and instructions is not None:
        for c in conditions:
            walk_steps(c, gates, found)       # a check inside the gate itself
        walk_steps(instructions, gates + conditions, found)
        return

    for key, inner in value.items():
        if key in ("type", "title"):
            continue
        walk_steps(inner, gates, found)


def line_of(node, limit=160):
    text = node.get("text") or ""
    if isinstance(text, dict):
        # A line whose text is read from somewhere rather than written.
        text = text.get("value") if isinstance(text.get("value"), str) else "(text read at play time)"
    text = str(text).strip().replace("\n", " ")
    return text if len(text) <= limit else text[:limit - 1] + "…"


def trail(nodes, node_id):
    """The lines leading to a node, top first, each with its own conditions -
    what the player has to have passed through, and what had to be true for
    each of those lines to be offered."""
    chain = []
    at = nodes.get(str(node_id))
    guard = 0
    while at is not None and guard < 200:
        parent = at.get("parent")
        if parent is None or parent == -1:
            break
        at = nodes.get(str(parent))
        if at is None:
            break
        entry = OrderedDict((("node", parent), ("line", line_of(at, 80))))
        if at.get("actor"):
            entry["actor"] = at["actor"]
        if at.get("conditions"):
            entry["when"] = at["conditions"]
        chain.append(entry)
        guard += 1
    chain.reverse()
    return chain


def from_dialogues(folder, into, counts, staging=True):
    """Every quest step in the conversations, and - with `staging` - in the
    scripts that play them. Leave `staging` off when the scene extraction is
    read: it holds those same scripts whole, and says exactly which Play a
    step sits between, where a conversation's own record of its script lists
    every step of that script once per conversation the script can play."""
    for path in sorted(glob.glob(os.path.join(folder, "Dialogues", "*.json"))):
        with io.open(path, encoding="utf-8") as handle:
            dialogue = json.load(handle)
        nodes = dialogue.get("nodes") or {}
        plays = [OrderedDict((k, s.get(k)) for k in ("by", "script", "event", "when", "branches", "gates", "ahead")
                             if s.get(k))
                 for s in dialogue.get("starts") or []]

        for node_id, node in nodes.items():
            for moment in ("onStart", "onFinish"):
                found = []
                walk_steps(node.get(moment) or [], [], found)
                for what, step, gates in found:
                    entry = OrderedDict()
                    entry["via"] = "dialogue"
                    entry["dialogue"] = dialogue["id"]
                    entry["node"] = int(node_id)
                    entry["moment"] = moment
                    if node.get("actor"):
                        entry["actor"] = node["actor"]
                    entry["line"] = line_of(node)
                    when = list(node.get("conditions") or []) + gates
                    if when:
                        entry["when"] = when
                    leading = trail(nodes, node_id)
                    if leading:
                        entry["after"] = leading
                    if plays:
                        entry["plays"] = plays
                    add(into, counts, what, step, entry)

        # The scripts that play the conversation can change a quest too, in
        # the steps staged around the Play.
        if not staging:
            continue
        for start in dialogue.get("starts") or []:
            for side in ("before", "after"):
                found = []
                walk_steps(start.get(side) or [], [], found)
                for what, step, gates in found:
                    entry = OrderedDict()
                    entry["via"] = "script"
                    entry["by"] = start.get("by")
                    entry["script"] = start.get("script")
                    if start.get("event"):
                        entry["event"] = start["event"]
                    when = list(start.get("when") or []) + gates
                    if when:
                        entry["when"] = when
                    if start.get("branches"):
                        entry["branches"] = start["branches"]
                    if start.get("ahead"):
                        entry["ahead"] = start["ahead"]
                    play = OrderedDict((("dialogue", dialogue["id"]),))
                    if side == "after":
                        waits = waits_for(start.get("how"), start.get("title"))
                        if waits is not None:
                            play["waits"] = waits
                    entry["around"] = OrderedDict(((side, play),))
                    add(into, counts, what, step, entry)


# Game Creator's Play instruction titles itself "Play <dialogue> and wait"
# exactly when it waits for the conversation to finish (its m_WaitToFinish),
# and "Play <dialogue> " when it does not.
PLAY = "InstructionDialoguePlay"


def waits_for(how, title):
    """Whether the steps after a Play run once the conversation has finished
    (True), as soon as it has started (False), or neither is known (None)."""
    if how != PLAY or not isinstance(title, str):
        return None
    return title.rstrip().endswith(" and wait")


# -- everything else -----------------------------------------------------

class QuestSites(Starts):
    """Every quest step in one script of the scene extraction, with the gates
    around it - the same reading the conversation starts get."""

    def collect(self, record, found):
        """`found` gets (what, step, gates, branches, around, located, ahead)
        for every quest step in the script - `ahead` being the branches tried
        before the step's own (Starts.ranked), `located` being where each run of
        `gates` lives, as the conversation starts record it."""
        self._found_steps = found
        self._walk_quests(record["fields"], [], [], None, set(), None, (), [], [])

    def _walk_quests(self, value, gates, branches, site, seen, holder, where, located, ahead,
                     ranked_ahead=None):
        value = self.d.resolve(value, holder)
        if isinstance(value, dict):
            key = id(value)
            if key in seen:
                return
            seen.add(key)

            kind = value.get("$type") or ""
            what = what_of(kind)
            if what:
                translated = (self.condition(value) if kind.startswith("Condition")
                              else self.instruction(value))
                self._found_steps.append((what, translated, list(gates), list(branches), self.around(site),
                                          list(located), list(ahead)))

            found = self.gate(value, where)
            inner_gates = gates + (found["when"] if found else [])
            inner_branches = branches + ([found["branch"]] if found and found["branch"] else [])
            inner_located = located + ([OrderedDict((("at", found["at"]), ("count", len(found["when"]))))]
                                       if found and found["when"] else [])
            inner_ahead = ahead + (ranked_ahead if found and ranked_ahead else [])
            for k, inner in value.items():
                if k.startswith("$"):
                    continue
                if k == "m_Conditions":
                    self._walk_quests(inner, gates, branches, site, seen, k, where + (k,), located, ahead)
                else:
                    self._walk_quests(inner, inner_gates, inner_branches, site, seen, k, where + (k,),
                                      inner_located, inner_ahead)
        elif isinstance(value, list):
            # In a list of instructions, remember the position: a step's
            # neighbours say which conversation it follows or leads into.
            staged = holder == "m_Instructions"
            ranked = self.ranked(value, holder)
            for index, inner in enumerate(value):
                self._walk_quests(inner, gates, branches, (value, index) if staged else site, seen, holder,
                                  where + (index,), located, ahead,
                                  [r for r in ranked[:index] if r is not None] if ranked else None)

    def around(self, site):
        """The conversations played just before and just after a step, in the
        same list of instructions - or None when neither is there."""
        if site is None:
            return None
        items, index = site
        out = OrderedDict()
        for side, span in (("after", reversed(items[:index])), ("before", items[index + 1:])):
            for raw in span:
                play = self.play(raw)
                if play is not None:
                    out[side] = play
                    break
        return out or None

    def play(self, raw):
        """A Play instruction, as the conversation it names and whether it
        waits - or None for any other instruction. A Play that picks its
        conversation while the game runs names none."""
        value = self.d.resolve(raw)
        if not isinstance(value, dict) or value.get("$type") != PLAY:
            return None
        out = OrderedDict((("dialogue", self.named(value.get("m_Dialogue"))),))
        waits = waits_for(PLAY, value.get("$title"))
        if waits is not None:
            out["waits"] = waits
        return out

    def named(self, value, depth=0):
        """The first scene object a value names by path."""
        value = self.d.resolve(value)
        if depth > 8:
            return None
        if isinstance(value, dict):
            if isinstance(value.get("$path"), str):
                return value["$path"]
            for k, inner in value.items():
                if not k.startswith("$"):
                    found = self.named(inner, depth + 1)
                    if found:
                        return found
        return None


def from_scene(path, into, counts):
    with io.open(path, encoding="utf-8-sig") as handle:
        raw = json.load(handle)
    scripts = 0
    for record in raw.get("objects", []):
        scripts += 1
        dump = Dump(record["fields"])
        sites = QuestSites(dump)
        found = []
        sites.collect(record, found)
        event = dump.field(record["fields"], "m_TriggerEvent")
        for what, step, gates, branches, around, located, ahead in found:
            entry = OrderedDict()
            entry["via"] = "script"
            entry["by"] = record.get("object")
            entry["script"] = (record.get("script") or "").rsplit(".", 1)[-1]
            if isinstance(event, dict) and event.get("$type"):
                entry["event"] = event["$type"]
            if "active" in record:
                entry["active"] = record["active"]
            if record.get("prefab"):
                entry["prefab"] = True
            if gates:
                entry["when"] = gates
            if located:
                entry["gates"] = located
            if ahead:
                entry["ahead"] = ahead
            if branches:
                entry["branches"] = branches
            if around:
                entry["around"] = around
            add(into, counts, what, step, entry)
    return raw.get("taken"), scripts, raw.get("note")


# -- the file ------------------------------------------------------------

def add(into, counts, what, step, entry):
    quest, task = quest_of(step.get("fields"))
    if not quest:
        counts["no quest named"] += 1
        return
    entry["step"] = brief(step)
    q = into.setdefault(quest, OrderedDict())
    if task is None:
        q.setdefault(what, []).append(entry)
    else:
        t = q.setdefault("tasks", OrderedDict()).setdefault(str(task), OrderedDict())
        t.setdefault(what, []).append(entry)
    counts[what] += 1


def read_catalogue(path):
    """Quest name -> set of task ids, from VanillaQuests.cs."""
    text = io.open(path, encoding="utf-8-sig").read()
    heads = [(m.start(), m.group(1)) for m in re.finditer(r'\n        new\("((?:[^"\\]|\\.)*)"', text)]
    quests = OrderedDict()
    for i, (at, name) in enumerate(heads):
        end = heads[i + 1][0] if i + 1 < len(heads) else len(text)
        ids = [int(x) for x in re.findall(r'\n            new\((-?\d+), -?\d+, ', text[at:end])]
        quests[name.replace('\\"', '"')] = ids
    return quests


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dialogues", required=True, help="the shipped VanillaDialogues folder")
    parser.add_argument("--scene", help="the SMSModForge-quest-references-*.json from F8")
    parser.add_argument("--quests", required=True, help="VanillaQuests.cs")
    parser.add_argument("-o", "--out", required=True)
    args = parser.parse_args(argv)

    catalogue = read_catalogue(args.quests)
    into = OrderedDict()
    counts = Counter()

    from_dialogues(args.dialogues, into, counts, staging=not args.scene)
    scene = None
    if args.scene:
        taken, scripts, note = from_scene(args.scene, into, counts)
        scene = OrderedDict((("file", os.path.basename(args.scene)), ("taken", taken), ("scripts", scripts)))
        if note:
            scene["note"] = note

    with io.open(os.path.join(args.dialogues, "index.json"), encoding="utf-8") as handle:
        dialogue_source = json.load(handle).get("source")

    # The catalogue's quests in its order, then any other quest something
    # refers to - which is how the catalogue tool learns a quest is in use.
    quests = OrderedDict()
    for name in catalogue:
        if name in into:
            quests[name] = into[name]
    unknown = sorted(set(into) - set(catalogue))
    for name in unknown:
        quests[name] = into[name]

    out = OrderedDict((
        ("version", 1),
        ("complete", bool(scene) and not (scene or {}).get("note")),
        ("sources", OrderedDict((("dialogues", dialogue_source), ("scene", scene)))),
        ("quests", quests),
    ))
    with io.open(args.out, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(out, indent=1, ensure_ascii=False))
        handle.write("\n")

    # What is known and what is not, per quest, so a gap is seen here first.
    print("%s: %s" % (args.out, ", ".join("%d %s" % (v, k) for k, v in counts.most_common())))
    if not scene:
        print("  scene scripts NOT included - run F8 with a Debug plugin and pass --scene")
    else:
        print("  scene: %d script(s) from %s" % (scene["scripts"], scene["file"]))
    unstarted = [q for q in catalogue if not (quests.get(q) or {}).get("starts")]
    print("  %d of %d quests with nothing found that starts them%s"
          % (len(unstarted), len(catalogue), (": " + ", ".join(unstarted)) if unstarted else ""))
    stray = 0
    for name, ids in catalogue.items():
        known = set(str(i) for i in ids)
        for task in ((quests.get(name) or {}).get("tasks") or {}):
            if task not in known:
                stray += 1
    if stray:
        print("  %d reference(s) to task ids the catalogue does not have" % stray)
    if unknown:
        print("  %d quest(s) named that the catalogue leaves out (regenerate it with --references): %s"
              % (len(unknown), ", ".join(unknown)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
