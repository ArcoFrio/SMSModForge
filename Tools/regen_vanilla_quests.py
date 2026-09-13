#!/usr/bin/env python3
"""Rebuild `SMSModForge/Model/VanillaQuests.cs` from two runtime dumps of the game.

WHERE IT COMES FROM
    A quest's tasks live in a Game Creator tree the ripped project does not
    carry, so they are read out of a running game by the plugin's diagnostics,
    in a Debug build:

        F8  -> SMSModForge-questjournal-quests-<stamp>.json   every Quest asset
        F10 -> SMSModForge-dialogues-<stamp>.json            every dialogue

WHAT IT KEEPS
    What an author needs to point an action or a condition at one of the
    game's quests: the name the game's own instructions use (the asset name),
    the title the journal shows, and every task with its id, its place in the
    tree, how it completes, and whether it counts.

    Descriptions are left out. Nothing in the editor shows them, and they are
    the part of a quest that tells its story.

WHICH QUESTS
    The game ships Game Creator's sample quests beside its own ("Quest Simple",
    "Beast_Rat"...), and a few that nothing in the dumps starts. A quest is
    kept when one of the game's dialogues refers to it, or when it is named in
    CONFIRMED below because it has been seen in play. Everything else is left
    out rather than advertised: a quest nothing starts is either a sample or
    unreleased work, and this tool has no business listing either.

    A pack can still name a quest this file does not list - the picker is
    editable and the runtime looks quests up in the running game.

Usage:
    python Tools/regen_vanilla_quests.py --quests <F8 quests json> --dialogues <F10 json>
    python Tools/regen_vanilla_quests.py --quests ... --dialogues ... --check
"""

import argparse
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "SMSModForge", "Model", "VanillaQuests.cs")

# Seen in the journal of a real playthrough, though no dialogue in the dumps
# starts it - whatever does sits outside what F10 reaches.
CONFIRMED = {
    "A Trained Eye (Gabriel)",
}


def prop_string(prop):
    """A PropertyGetString's literal, or ''. Every title and task name in 1.8E
    is a plain string; anything else is reported rather than guessed at."""
    if not isinstance(prop, dict):
        return ""
    inner = prop.get("m_Property") or {}
    kind = inner.get("$type")
    if kind == "GetStringString":
        return inner.get("m_Value") or ""
    if kind == "GetStringTextArea":
        return (inner.get("m_Text") or {}).get("Text") or ""
    raise SystemExit("unexpected string property " + str(kind))


def prop_decimal(prop):
    inner = (prop or {}).get("m_Property") or {}
    if inner.get("$type") == "GetDecimalDecimal":
        return float(inner.get("m_Value") or 0)
    return 0.0


def counter_variable(task):
    """The global variable a Property counter follows, as List[name]."""
    inner = ((task.get("m_ValueFrom") or {}).get("m_Property")) or {}
    if inner.get("$type") != "GetDecimalGlobalName":
        return ""
    field = inner.get("m_Variable") or {}
    name = ((field.get("m_Name") or {}).get("m_String")) or ""
    asset = field.get("m_Variable")
    listed = asset.get("$unity") if isinstance(asset, dict) else ""
    return (listed + "[" + name + "]") if listed else name


def items(container):
    return (container or {}).get("$items") or []


def referenced_quests(dialogues_path):
    text = io.open(dialogues_path, encoding="utf-8-sig").read()
    return set(re.findall(r'\{"\$unity": "([^"]+)", "\$type": "Quest"\}', text))


def read_quest(obj):
    f = obj["fields"]
    tree = f["m_Tasks"]

    tasks = {}
    for pair in items(tree["m_Data"]):
        task = pair["Value"]["m_Value"]
        tasks[pair["Key"]] = task

    nodes = {}
    for pair in items(tree["m_Nodes"]):
        node = pair["Value"]
        nodes[pair["Key"]] = (node["m_Parent"], items(node["m_Children"]))

    ordered = []

    def visit(task_id, parent):
        # A node can name a child that has no data - a task deleted in the
        # editor leaves its id behind. The game skips those, and so does this.
        if task_id not in tasks:
            return
        task = tasks[task_id]
        ordered.append({
            "id": task_id,
            "parent": parent,
            "name": prop_string(task["m_Name"]),
            "completion": task["m_Completion"],
            "counter": task["m_UseCounter"],
            "countTo": prop_decimal(task["m_CountTo"]) if task["m_UseCounter"] != "None" else 0.0,
            "variable": counter_variable(task) if task["m_UseCounter"] == "Property" else "",
            "hidden": bool(task.get("m_IsHidden")),
        })
        for child in nodes.get(task_id, (None, []))[1]:
            visit(child, task_id)

    for root in items(tree["m_Roots"]):
        visit(root, -1)

    return {
        "name": obj["object"],
        "title": prop_string(f["m_Title"]),
        "hidden": f.get("m_Type") != "Normal",
        "sortOrder": int(f.get("m_SortOrder") or 0),
        "tasks": ordered,
    }


def cs_string(value):
    return '"' + value.replace("\\", "\\\\").replace('"', '\\"') + '"'


def cs_number(value):
    text = repr(float(value))
    return text[:-2] if text.endswith(".0") else text


def render(quests):
    out = io.StringIO()
    w = out.write
    w("﻿// <auto-generated>\n")
    w("//   Written by Tools/regen_vanilla_quests.py from a runtime dump of the game.\n")
    w("//   Regenerate it rather than editing it; see the script for what is kept and why.\n")
    w("// </auto-generated>\n")
    w("using System.Collections.Generic;\n\n")
    w("namespace SMSModForge.Model;\n\n")
    w("public static partial class VanillaQuests\n{\n")
    w("    /// <summary>Every quest of the game's own that a pack can point at.</summary>\n")
    w("    public static readonly IReadOnlyList<VanillaQuest> All = new VanillaQuest[]\n    {\n")
    for q in quests:
        w("        new(" + cs_string(q["name"]) + ", " + cs_string(q["title"]) + ", "
          + ("true" if q["hidden"] else "false") + ", " + str(q["sortOrder"]) + ", new VanillaTask[]\n        {\n")
        for t in q["tasks"]:
            w("            new(" + str(t["id"]) + ", " + str(t["parent"]) + ", " + cs_string(t["name"]) + ", "
              + "TaskCompletion." + t["completion"] + ", TaskCounter." + t["counter"] + ", "
              + cs_number(t["countTo"]) + ", " + cs_string(t["variable"]) + ", "
              + ("true" if t["hidden"] else "false") + "),\n")
        w("        }),\n")
    w("    };\n}\n")
    return out.getvalue().replace("\n", "\r\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--quests", required=True)
    parser.add_argument("--dialogues", required=True)
    parser.add_argument("--check", action="store_true",
                        help="fail if the file on disk differs from what would be written")
    args = parser.parse_args()

    dump = json.load(io.open(args.quests, encoding="utf-8-sig"))
    used = referenced_quests(args.dialogues)

    # The control: a dialogue dump with no quest references at all is a dump
    # that was taken wrong, not a game without quests.
    if not used:
        raise SystemExit("no quest references in " + args.dialogues + " - wrong file?")

    kept, skipped = [], []
    for obj in dump["objects"]:
        if obj.get("script") != "GameCreator.Runtime.Quests.Quest":
            continue
        name = obj["object"]
        if name in used or name in CONFIRMED:
            kept.append(read_quest(obj))
        else:
            skipped.append(name)

    missing = sorted((used | CONFIRMED) - {q["name"] for q in kept})
    if missing:
        raise SystemExit("referenced but not in the quest dump: " + ", ".join(missing))

    kept.sort(key=lambda q: q["name"].lower())
    text = render(kept)

    tasks = sum(len(q["tasks"]) for q in kept)
    print("kept %d quests, %d tasks; left out %d: %s" % (len(kept), tasks, len(skipped), ", ".join(sorted(skipped))))

    if args.check:
        current = io.open(OUT, encoding="utf-8-sig").read() if os.path.exists(OUT) else ""
        if current.replace("\r\n", "\n") != text.lstrip("﻿").replace("\r\n", "\n"):
            print("VanillaQuests.cs is out of date")
            sys.exit(1)
        print("up to date")
        return

    io.open(OUT, "w", encoding="utf-8", newline="").write(text)
    print("wrote " + OUT)


if __name__ == "__main__":
    main()
