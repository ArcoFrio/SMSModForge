"""Whether copies of a name on disk line up with references the walk found, so names shared by several
lists can be settled; and the bytes around the copies of chosen names.

    python Tools/Research/Variables/shared_names.py <run> "<test copy>\\Starmaker Story_Data" [name ...]
"""
import collections, csv, os, re, struct, sys

run, data, show = sys.argv[1], sys.argv[2], sys.argv[3:]


def rows(path):
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


files = rows(os.path.join(run, "files.tsv"))
refs = rows(os.path.join(run, "game", "refs.tsv")) + rows(os.path.join(run, "menu", "refs.tsv"))
ref_count = collections.Counter(r["variable"] for r in refs)
by_name = {}
for r in files:
    by_name.setdefault(r["variable"], r)

# For names in one list: copies beyond the definition against references found.
pairs = collections.Counter()
for n, r in by_name.items():
    if r["lists_defining_name"] == "1" and r["referenced_in_walk"]:
        pairs[(int(r["extra_copies"]) == ref_count[n], int(r["extra_copies"]) >= ref_count[n])] += 1
print("one-list referenced names: copies == references %d, copies >= references %d, copies < references %d" % (
    sum(v for (eq, _), v in pairs.items() if eq), sum(v for (_, ge), v in pairs.items() if ge),
    sum(v for (_, ge), v in pairs.items() if not ge)))
ratio = [(int(r["extra_copies"]), ref_count[n]) for n, r in by_name.items() if r["lists_defining_name"] == "1" and r["referenced_in_walk"]]
over = sorted(((c - k), n) for (c, k), n in zip(ratio, [n for n, r in by_name.items() if r["lists_defining_name"] == "1" and r["referenced_in_walk"]]) if c > k)
print("  most copies beyond references:", over[-8:])

# Shared names with an unreferenced member: every copy accounted for by the references found?
shared = collections.defaultdict(list)
for r in files:
    if r["lists_defining_name"] != "1":
        shared[r["variable"]].append(r)
unsettled = []
for n, rs in shared.items():
    if all(x["referenced_in_walk"] for x in rs):
        continue
    extra = int(rs[0]["extra_copies"])
    if extra > ref_count[n]:
        unsettled.append((n, extra, ref_count[n], [x["list"] for x in rs if not x["referenced_in_walk"]]))
print("shared names with an unreferenced member: %d; with more copies on disk than references found: %d" % (
    sum(1 for rs in shared.values() if not all(x["referenced_in_walk"] for x in rs)), len(unsettled)))
for u in sorted(unsettled)[:25]:
    print("   ", u)

for name in show:
    b = name.encode("utf-8")
    needle = struct.pack("<i", len(b)) + b
    for f in sorted(os.listdir(data)):
        p = os.path.join(data, f)
        if not os.path.isfile(p) or f.endswith((".resS", ".resource")):
            continue
        blob = open(p, "rb").read()
        for m in list(re.finditer(re.escape(needle), blob))[:4]:
            around = blob[max(0, m.start() - 90): m.end() + 60]
            text = "".join(chr(c) if 32 <= c < 127 else "." for c in around)
            print("%s in %s @%d: %s" % (name, f, m.start(), text))
