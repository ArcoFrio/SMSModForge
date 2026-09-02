"""Sample real UI rectangles into a fixture the editor's layout tests check against.

WHY
    Turning anchors into pixels is the one calculation everything else in the
    preview sits on, and it is easy to get subtly wrong - a pivot applied to the
    wrong term, a stretch axis handled as a size. Wrong subtly means the preview
    looks plausible and drifts, which is the exact failure this whole effort is
    trying to avoid.

    Unity already answered the question. The surface extractor recorded, for
    every object, both the authored anchors AND the rectangle Unity resolved
    them to. So the test does not need my opinion of the formula: it needs a
    sample of those pairs, and the formula has to reproduce them.

WHY A SAMPLE RATHER THAN ALL OF IT
    All of it is about 8000 rows and a couple of megabytes, and volume is not
    what catches a formula error - variety is. A thousand centred point-anchored
    labels prove one branch. So this buckets nodes by their anchor configuration
    and takes a spread from each, which fits in a fixture worth committing.

WHAT IS LEFT OUT
    Nodes whose own transform, or any ancestor's, carries a scale or a rotation.
    Those are correct to place with a transform at draw time rather than by
    arithmetic, so their resolved corners are not what this formula is for. 279
    nodes are scaled and 50 rotated, against 7918 plain.

USAGE
    python BuildLayoutFixture.py <extraction dir> -o ui_layout_baseline.json
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
from collections import OrderedDict, defaultdict

PER_BUCKET = 40


def bucket_of(rect):
    """What kind of anchoring this is, so the sample covers every kind."""
    amin, amax = rect["anchorMin"], rect["anchorMax"]
    stretch_x = abs(amax[0] - amin[0]) > 1e-6
    stretch_y = abs(amax[1] - amin[1]) > 1e-6
    pivot = tuple(rect["pivot"])
    centred = abs(pivot[0] - 0.5) < 1e-6 and abs(pivot[1] - 0.5) < 1e-6
    return ("stretch:%s%s" % ("x" if stretch_x else "-", "y" if stretch_y else "-"),
            "pivot:centre" if centred else "pivot:offset")


def plain(rect):
    """No scale and no rotation - see the module docstring."""
    s = rect.get("localScale") or [1, 1, 1]
    e = rect.get("localEuler") or [0, 0, 0]
    return (abs(s[0] - 1) < 1e-4 and abs(s[1] - 1) < 1e-4
            and all(abs(v) < 1e-4 for v in e))


def collect(extract_dir):
    index = json.load(io.open(os.path.join(extract_dir, "index.json"),
                              encoding="utf-8-sig"))
    buckets = defaultdict(list)

    for entry in index["surfaces"]:
        if not entry.get("usable", True):
            continue
        path = os.path.join(extract_dir, entry["file"].replace("/", os.sep))
        surface = json.load(io.open(path, encoding="utf-8-sig"))
        root = surface["root"]
        root_rect = root.get("rect", {}).get("resolved")
        if not root_rect:
            continue

        def walk(node, parent_resolved, parent_plain, trail):
            rect = node.get("rect")
            resolved = (rect or {}).get("resolved")
            here_plain = parent_plain and rect is not None and plain(rect)

            # Deliberately NOT filtered by trust. Trust says whether a value
            # is what the game will show; this test asks only whether the
            # recorded anchors and the recorded rectangle agree with each
            # other, and they were read from the same object at the same
            # moment whatever the layout system had done to it.
            if resolved and parent_resolved and here_plain:
                buckets[bucket_of(rect)].append(OrderedDict([
                    ("where", trail),
                    ("parent", OrderedDict([
                        ("min", parent_resolved["min"]),
                        ("size", parent_resolved["size"]),
                    ])),
                    ("anchorMin", rect["anchorMin"]),
                    ("anchorMax", rect["anchorMax"]),
                    ("pivot", rect["pivot"]),
                    ("anchoredPosition", rect["anchoredPosition"]),
                    ("sizeDelta", rect["sizeDelta"]),
                    ("expectedMin", resolved["min"]),
                    ("expectedSize", resolved["size"]),
                ]))

            for child in node.get("children", []):
                walk(child, resolved, here_plain, trail + "/" + child["name"])

        # The canvas root itself is the parent of the first real layer.
        walk(root, root_rect, True, entry["path"])

    return buckets


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Sample real UI rectangles into a layout test fixture.")
    parser.add_argument("extract_dir")
    parser.add_argument("-o", "--out", default="ui_layout_baseline.json")
    args = parser.parse_args(argv)

    buckets = collect(args.extract_dir)
    if not buckets:
        raise SystemExit("no usable nodes found - is that the extraction folder?")

    cases = []
    for name in sorted(buckets):
        rows = buckets[name]
        # Spread across the bucket rather than taking the first N, which would
        # all come from one surface and one authoring habit.
        step = max(1, len(rows) // PER_BUCKET)
        picked = rows[::step][:PER_BUCKET]
        for row in picked:
            row["kind"] = " ".join(name)
        cases.extend(picked)
        print("%-28s %5d nodes -> %d sampled" % (" ".join(name), len(rows), len(picked)))

    payload = OrderedDict([
        ("$schema", "smsmodforge/uilayout-baseline/v1"),
        ("note", "Anchors as authored, beside the rectangle Unity resolved them "
                 "to. The editor's own resolver has to reproduce these."),
        ("cases", cases),
    ])
    with io.open(args.out, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(payload, indent=1, ensure_ascii=False))
        handle.write("\n")
    print("\n%s: %d cases, %.0f KB"
          % (args.out, len(cases), os.path.getsize(args.out) / 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())
