"""Turn the UI surface extraction into the compact catalog the editor ships.

WHY A SEPARATE FILE FROM THE EXTRACTION
    The extraction is ~7.4 MB across 49 surface files, because it holds every
    rectangle of every object. The editor's UI tab needs that eventually - to
    draw a preview and to tell an authored change from the vanilla baseline -
    but the FIRST thing it needs is much smaller: the list of things a pack can
    extend, with enough about each to choose between them. That list is a few
    tens of kilobytes and belongs in the build.

WHAT A "BASE" IS
    A direct child of a surface.

    That rule comes from how the game is actually built rather than from
    taste. 9_MainCanvas is one canvas holding 4087 objects, but it is not one
    UI: its 21 children are Starmaker, ShopCore, Navigator, Calendar, Payout,
    Quitagme and so on, switched on one at a time. Making a pack author open
    all 4087 objects to change Payout would be the same mistake as making them
    open every level to change one room. Every surface in this scene has
    children, so the rule needs no exception.

USAGE
    python BuildUiCatalog.py <extraction dir> -o vanilla_ui_bases.json

    where <extraction dir> is the folder written by
    Tools/UnityEditor/SMSModForgeUiSurfaceExtractor.cs - the one holding
    index.json and Surfaces/.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
from collections import Counter, OrderedDict

# The canvas the game dims when it hides the gameplay UI. Anything parented
# under it inherits that CanvasGroup's alpha; anything outside it does not.
# That is the whole mechanism behind "does this disappear during a cutscene",
# and it is a parenting fact rather than a component anyone can look for - there
# is no FadeUI script in the game to search for.
GAMEPLAY_CANVAS = "9_MainCanvas"

# Components that say what a base is made of, in a way that reads usefully in a
# picker. Deliberately short: this is a summary, not the tree.
COUNTED = ["Image", "TextMeshProUGUI", "Text", "Trigger", "Button",
           "Slider", "Toggle", "ScrollRect", "InputField"]


def walk(node):
    yield node
    for child in node.get("children", []):
        for n in walk(child):
            yield n


def summarise(node):
    """What this base is built out of, counted over its whole subtree."""
    counts = Counter()
    for n in walk(node):
        for c in n.get("components", []):
            if c in COUNTED:
                counts[c] += 1
    return OrderedDict((k, counts[k]) for k in COUNTED if counts[k])


def describe_base(child, surface_path, index):
    subtree = list(walk(child))
    return OrderedDict([
        ("name", child["name"]),
        ("objects", len(subtree) - 1),
        ("onAtLoad", child["activeInHierarchy"]),
        ("siblingIndex", index),
        ("contents", summarise(child)),
    ])


def build(extract_dir):
    index_path = os.path.join(extract_dir, "index.json")
    if not os.path.exists(index_path):
        raise SystemExit("no index.json in %s - point this at the folder the "
                         "surface extractor wrote" % extract_dir)
    index = json.load(io.open(index_path, encoding="utf-8-sig"))

    # Surface paths are not unique either. One level holds two sibling
    # GameObjects both called Canvas, and each has a child called Button, so
    # base ids built from the raw path collided exactly - two different objects
    # answering to one name, with the lookup silently keeping whichever it read
    # last. Disambiguate the surface first, then build base ids on top of it.
    path_counts = Counter(e["path"] for e in index["surfaces"])
    path_used = Counter()

    surfaces = []
    for entry in index["surfaces"]:
        path_used[entry["path"]] += 1
        ambiguous_surface = path_counts[entry["path"]] > 1
        surface_id = entry["path"] + (
            "#%d" % path_used[entry["path"]] if ambiguous_surface else "")
        path = os.path.join(extract_dir, entry["file"].replace("/", os.sep))
        surface = json.load(io.open(path, encoding="utf-8-sig"))
        root = surface["root"]
        scaler = surface.get("scaler") or {}
        canvas = surface.get("canvas") or {}

        bases = []
        for i, child in enumerate(root.get("children", [])):
            bases.append(describe_base(child, entry["path"], i))

        # Two siblings can share a name - the scene has sibling GameObjects
        # called Canvas under one level - so an id built from the name alone
        # would address whichever the reader happened to find first. Only the
        # ones that actually collide get a suffix, so the common case stays
        # readable.
        seen = Counter(b["name"] for b in bases)
        used = Counter()
        for b in bases:
            used[b["name"]] += 1
            suffix = "" if seen[b["name"]] == 1 else "#%d" % used[b["name"]]
            b["id"] = surface_id + "/" + b["name"] + suffix
            b["ambiguousName"] = seen[b["name"]] > 1 or ambiguous_surface

        surfaces.append(OrderedDict([
            ("id", surface_id),
            ("path", entry["path"]),
            ("ambiguousPath", ambiguous_surface),
            ("scene", entry["scene"]),
            ("liveAtLoad", entry["liveAtLoad"]),
            ("usable", entry.get("usable", True)),
            ("dimsWithGameplayUi", entry["path"] == GAMEPLAY_CANVAS),
            ("referenceResolution", scaler.get("referenceResolution")),
            ("matchWidthOrHeight", scaler.get("matchWidthOrHeight")),
            ("uiScaleMode", scaler.get("uiScaleMode")),
            ("sortingOrder", canvas.get("sortingOrder")),
            ("bases", bases),
        ]))

    surfaces.sort(key=lambda s: (s["scene"], s["id"]))
    return OrderedDict([
        ("$schema", "smsmodforge/vanillaui/v1"),
        ("gameplayCanvas", GAMEPLAY_CANVAS),
        ("surfaces", surfaces),
    ])


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Build the compact vanilla UI base catalog the editor ships.")
    parser.add_argument("extract_dir",
                        help="folder written by SMSModForgeUiSurfaceExtractor")
    parser.add_argument("-o", "--out", default="vanilla_ui_bases.json")
    args = parser.parse_args(argv)

    catalog = build(args.extract_dir)
    with io.open(args.out, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(catalog, indent=1, ensure_ascii=False))
        handle.write("\n")

    surfaces = catalog["surfaces"]
    bases = sum(len(s["bases"]) for s in surfaces)
    ids = [b["id"] for s in surfaces for b in s["bases"]]
    dupes = [i for i, n in Counter(x.lower() for x in ids).items() if n > 1]
    if dupes:
        raise SystemExit("ids are not unique, which makes the catalog unusable "
                         "as an address book: %s" % dupes[:5])
    unusable = [s["path"] for s in surfaces if not s["usable"]]
    print("%s: %d surfaces, %d bases, %.0f KB"
          % (args.out, len(surfaces), bases, os.path.getsize(args.out) / 1024))
    if unusable:
        print("  %d surface(s) produced no usable geometry: %s"
              % (len(unusable), ", ".join(unusable)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
