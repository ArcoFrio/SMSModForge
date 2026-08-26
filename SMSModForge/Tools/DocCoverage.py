#!/usr/bin/env python3
"""Which of the editor's functions does the documentation actually cover?

WHY THIS EXISTS
    "Document everything" is not a checkable claim. This turns it into one:
    it reads the functions out of the source -- the tabs, the fields on each
    tab, the toolbar buttons, the menu items, and every action and condition
    type the pickers offer -- and reports which of them the in-app reference
    never mentions.

KEYED TO WHAT THE UI SHOWS, NOT WHAT THE CODE CALLS IT
    This is the whole reason the tool is needed. Several action and condition
    types are FOLDED in the picker: SetVariable, IncrementVariable,
    PickRandomFromList and CountList are not offered by name at all -- they
    appear as one "Variable" entry whose Operation dropdown selects between
    them. ActivateScene is folded into SetGameObjectActive's "Scene" category.

    Documenting "SetVariable" would therefore describe something no author can
    find, which is exactly the mistake a tutorial step made: it told the reader
    to choose a type that does not appear in the list. So the inventory below
    is the picker's contents, and the folded names are reported separately as
    concepts the text still has to explain.

USAGE
    python Tools/DocCoverage.py            # summary
    python Tools/DocCoverage.py --full     # every item, covered or not
    python Tools/DocCoverage.py --missing  # only what is undocumented
"""

import argparse
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

# The editor's labels carry en-dashes and minus signs; a Windows console
# defaults to cp1252 and dies on them mid-report.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, ".."))


def read(rel):
    return io.open(os.path.join(ROOT, rel), encoding="utf-8-sig").read()


# ── the documentation, flattened to searchable text ───────────────────
def doc_text():
    """Every word of the in-app reference, lower-cased.

    Both the bullet titles and their bodies count: a function explained inside
    a paragraph is documented, even when it is not its own bullet.
    """
    s = read("Documentation/DocTopics.cs")
    return " ".join(s.split('"')[1::2]).lower()


def doc_topics():
    s = read("Documentation/DocTopics.cs")
    return re.findall(r'new DocTopic\("([^"]+)"', s)


# ── the editor's functions, read out of the source ────────────────────
def tabs():
    s = read("MainWindow.xaml")
    out = []
    for m in re.finditer(r'<TabItem[^>]*?Header="([^"]+)"', s, re.S):
        out.append(m.group(1).replace("⚒", "").strip())
    return out


def action_picker():
    """What the action Type dropdown offers, and what it folds away."""
    s = read("Model/NodeActionDef.cs")
    block = s[s.index("public static readonly string[] All"):]
    block = block[: block.index("};")]
    all_types = re.findall(r"\b([A-Z]\w+),?\s*(?://.*)?$", block, re.M)
    all_types = [t for t in re.findall(r"\b(\w+)\b", block) if t and t[0].isupper()]

    # MainViewModel.ActionTypes drops these and adds one synthetic entry.
    folded = ["ActivateScene", "SetVariable", "IncrementVariable",
              "PickRandomFromList", "CountList"]
    shown = sorted(set(t for t in all_types if t not in folded) | {"Variable"},
                   key=str.lower)
    return shown, folded


def condition_picker():
    s = read("Model/NodeConditionDef.cs")
    consts = dict(re.findall(r'public const string (\w+) = "([^"]+)"', s))
    block = s[s.index("public static readonly string[] All"):]
    block = block[: block.index("};")]
    names = [consts.get(n, n) for n in re.findall(r"\b(\w+)\b", block) if n in consts]

    # NodeConditionViewModel.BuildPicker folds every variable comparison into
    # a single "Variable" entry, exactly as the action picker does.
    folded = [n for n in names if n.startswith("Variable") or n.startswith("GameVariable")]
    shown = sorted(set(n for n in names if n not in folded) | {"Variable"},
                   key=str.lower)
    return shown, folded


def labels_and_buttons():
    """Field labels and clickable labels, grouped by the tab they sit on."""
    raw = read("MainWindow.xaml")
    root = ET.fromstring(raw)
    out = {}

    def harvest(el):
        found = set()
        for d in el.iter():
            tag = d.tag.rsplit("}", 1)[-1]
            if tag == "TextBlock":
                t = (d.attrib.get("Text") or "").strip()
                if not t or t.startswith("{") or not (2 < len(t) < 34):
                    continue
                # A trailing colon is the usual sign of a field label, but not
                # every tab writes one: the Characters jiggle grid labels its
                # rows "Speed", "Strength", "Noise scale" with no punctuation,
                # so a colon-only rule scored that whole panel as nonexistent
                # and reported 100% on fields nobody had documented. Sitting in
                # a grid's label column, or carrying a tooltip of its own, is
                # the same claim by other means.
                label = d.attrib.get("Grid.Column") == "0"
                if t.endswith(":") or label or d.attrib.get("ToolTip"):
                    found.add(t.rstrip(":"))
            elif tag in ("CheckBox", "Button", "RadioButton"):
                c = (d.attrib.get("Content") or "").strip()
                if c and not c.startswith("{") and 2 < len(c) < 40:
                    found.add(c)
        return found

    for el in root.iter():
        if el.tag.rsplit("}", 1)[-1] != "TabItem":
            continue
        header = (el.attrib.get("Header") or "?").replace("⚒", "").strip()
        out[header] = harvest(el)
    return out


def menu_items():
    raw = read("MainWindow.xaml")
    root = ET.fromstring(raw)
    out = []
    for el in root.iter():
        if el.tag.rsplit("}", 1)[-1] != "MenuItem":
            continue
        h = (el.attrib.get("Header") or "").replace("_", "").strip()
        if h and not h.startswith("{"):
            out.append(h)
    return out


# ── report ────────────────────────────────────────────────────────────
def normalise(label):
    r"""A label reduced to the words a writer would actually use.

    The UI carries detail the prose never repeats: a size hint
    ("Back sprite (2048x1136)"), an abbreviation ("Back mask (opt)"), a
    leading glyph on a toolbar button ("+ Scene", "▶ Play"). Matching the
    raw label reports those as undocumented no matter how well the text
    explains them, which buries the real gaps in noise.
    """
    t = re.sub(r"\s*\([^)]*\)\s*$", "", label)          # trailing parenthetical
    t = re.sub(r"^[^\w]+", "", t)                        # leading + - glyphs
    t = t.replace("…", "").replace("...", "")        # ellipsis on dialog buttons
    return t.strip()


def covered(term, text):
    t = normalise(term).lower()
    return bool(t) and t in text


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--full", action="store_true")
    ap.add_argument("--missing", action="store_true")
    args = ap.parse_args()

    text = doc_text()
    groups = []

    groups.append(("Tabs", sorted(tabs(), key=str.lower)))

    acts, act_folded = action_picker()
    groups.append(("Action types (as the picker shows them)", acts))
    groups.append(("Action types FOLDED into Variable / Set active", act_folded))

    conds, cond_folded = condition_picker()
    groups.append(("Condition types (as the picker shows them)", conds))
    groups.append(("Condition types FOLDED into Variable", cond_folded))

    groups.append(("Menu items", sorted(set(menu_items()), key=str.lower)))

    for tab, items in sorted(labels_and_buttons().items(), key=lambda kv: kv[0].lower()):
        if items:
            groups.append(("Fields & buttons - %s" % tab, sorted(items, key=str.lower)))

    total = miss_total = 0
    print("Documentation coverage")
    print("=" * 62)
    print("in-app topics: %d" % len(doc_topics()))
    print()

    for name, items in groups:
        missing = [i for i in items if not covered(i, text)]
        total += len(items)
        miss_total += len(missing)
        pct = 100.0 * (len(items) - len(missing)) / max(1, len(items))
        print("%-46s %3d/%-3d %5.0f%%" % (name, len(items) - len(missing), len(items), pct))
        show = items if args.full else missing
        if args.full or args.missing or missing:
            for i in show:
                mark = " " if covered(i, text) else "!"
                if args.full or mark == "!":
                    print("      %s %s" % (mark, i))

    print()
    print("=" * 62)
    print("%d of %d functions mentioned somewhere in the reference (%d not)"
          % (total - miss_total, total, miss_total))
    print()
    print("A mention is not an explanation -- this finds omissions, it cannot")
    print("judge whether what is written is correct or current.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
