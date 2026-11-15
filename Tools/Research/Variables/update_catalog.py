"""Brings SMSModForge/Model/VanillaGameVariables.cs into line with a collect.ps1 run of the test copy.

    python Tools/Research/Variables/update_catalog.py Tools/Research/Variables/runs/<date>

Keeps the catalog's order for every variable the game still has, drops the ones it does not, and puts
each new one after the last of its own list (a new list goes at the end). Types are the catalog's names
for the game's type ids. Prints what changed; changes nothing else in the file.
"""
import csv, os, re, sys

run = sys.argv[1]
repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
path = os.path.join(repo, "SMSModForge", "Model", "VanillaGameVariables.cs")
TYPES = {"boolean": "Boolean", "number": "Double", "string": "String", "game-object": "null", "vector3": "Vector3"}

with open(os.path.join(run, "game", "lists.tsv"), encoding="utf-8") as f:
    game = [(r["list"], r["variable"], r["type"]) for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE)]
unknown = sorted({t for _, _, t in game if t not in TYPES})
if unknown:
    raise SystemExit("type ids with no catalog name: %s" % unknown)

raw = open(path, "rb").read()
bom = raw.startswith(b"\xef\xbb\xbf")
text = raw.decode("utf-8-sig")
newline = "\r\n" if "\r\n" in text else "\n"
entry = re.compile(r'^(\s*)new\("([^"]*)", "([^"]*)", "([^"]*)"\),\s*$')
lines = text.split(newline)
spots = [(i, entry.match(l)) for i, l in enumerate(lines)]
spots = [(i, m) for i, m in spots if m]
if not spots:
    raise SystemExit("no entries found")
indent = spots[0][1].group(1)
first, last = spots[0][0], spots[-1][0]
if any(not entry.match(lines[i]) and lines[i].strip() for i in range(first, last + 1)):
    raise SystemExit("the entries are not one unbroken block")

present = {(l, n) for l, n, _ in game}
kept = [(m.group(3), m.group(2), m.group(4)) for _, m in spots if (m.group(3), m.group(2)) in present]
dropped = [(m.group(3), m.group(2)) for _, m in spots if (m.group(3), m.group(2)) not in present]
known = {(l, n) for l, n, _ in kept}
added = [(l, n, TYPES[t]) for l, n, t in game if (l, n) not in known]

order = list(kept)
for l, n, t in added:
    at = max((i for i, (ol, _, _) in enumerate(order) if ol == l), default=len(order) - 1)
    order.insert(at + 1, (l, n, t))

changed_types = [(l, n, t, TYPES[gt]) for (l, n, t) in kept for (gl, gn, gt) in game if (gl, gn) == (l, n) and TYPES[gt] != t]
body = [indent + 'new("%s", "%s", "%s"),' % (n, l, t) for l, n, t in order]
lines[first:last + 1] = body
open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + newline.join(lines).encode("utf-8"))
print("kept %d, dropped %d, added %d, retyped %d; now %d entries, %d names" % (
    len(kept), len(dropped), len(added), len(changed_types), len(order), len({n for _, n, _ in order})))
print("dropped lists:", sorted({l for l, _ in dropped}))
print("added:", sorted({l for l, _, _ in added}))
