"""Which list each on-disk copy of a variable's name belongs to, for names several lists share.

    python Tools/Research/Variables/attribute_lists.py Tools/Research/Variables/runs/<date> "<test copy>\\Starmaker Story_Data"

A Game Creator reference stores the list asset (a Unity PPtr: file id, then an 8-byte path id) shortly
before the variable's name. Each list's path id is LEARNED from the names only that list has: the
8-byte value that recurs before their copies. The control: the learned ids must credit those same
one-list names to their own list. Then every copy of a shared name ("45" in Gallery, Gallery_2,
Gallery_3 and Comments) is credited to the list whose id precedes it, and a variable with no reference
in the running game is settled when no copy on disk is credited to its list.
Writes <run>/attributed.tsv: list, variable, copies credited to it on disk, walk references.
"""
import collections, csv, os, re, struct, sys

run, data = sys.argv[1], sys.argv[2]
sys.stdout.reconfigure(encoding="utf-8")
WINDOW = 48


def rows(part, name):
    with open(os.path.join(run, part, name), encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


game = rows("game", "lists.tsv")
lists_of = collections.defaultdict(set)
for r in game:
    lists_of[r["variable"]].add(r["list"])
refs = collections.Counter()
for part in ("game", "menu"):
    for r in rows(part, "refs.tsv"):
        refs[(r["list"], r["variable"])] += 1

blobs = {f: open(os.path.join(data, f), "rb").read() for f in os.listdir(data)
         if os.path.isfile(os.path.join(data, f)) and not f.endswith((".resS", ".resource", ".info", ".log"))}


def copies(name):
    b = name.encode("utf-8")
    needle = struct.pack("<i", len(b)) + b
    for f, blob in blobs.items():
        for m in re.finditer(re.escape(needle), blob):
            yield f, blob[max(0, m.start() - WINDOW):m.start()]


def candidates(window):
    # Every 8-byte value ending at a 4-byte boundary before the name: a path id is one of them.
    out = set()
    for end in range(len(window), 7, -4):
        v = struct.unpack("<q", window[end - 8:end])[0]
        if 0 < v < (1 << 48):
            out.add(v)
    return out


# Learn: for each list, the value that most often precedes copies of its one-list, referenced names.
learn = collections.defaultdict(collections.Counter)
seen_names = collections.Counter()
for r in game:
    n, l = r["variable"], r["list"]
    if len(lists_of[n]) != 1 or refs[(l, n)] == 0 or len(n) < 4:
        continue
    seen_names[l] += 1
    for f, w in copies(n):
        for v in candidates(w):
            learn[l][v] += 1
path_id = {}
for l, counts in learn.items():
    if not counts:
        continue
    v, c = counts.most_common(1)[0]
    path_id[l] = v
owner = collections.defaultdict(set)
for l, v in path_id.items():
    owner[v].add(l)
clash = {v: ls for v, ls in owner.items() if len(ls) > 1}
print("lists with a learned id:", len(path_id), "of", len({r["list"] for r in game}), "| ids claimed by two lists:", clash)

# Control: credit the one-list referenced names' copies by the learned ids.
by_id = {v: l for v, ls in owner.items() if len(ls) == 1 for l in ls}
right = wrong = none = 0
for r in game:
    n, l = r["variable"], r["list"]
    if len(lists_of[n]) != 1 or refs[(l, n)] == 0 or len(n) < 4:
        continue
    for f, w in copies(n):
        hit = {by_id[v] for v in candidates(w) if v in by_id}
        if l in hit:
            right += 1
        elif hit:
            wrong += 1
        else:
            none += 1
print("CONTROL - copies of one-list referenced names credited to their own list: %d, to another: %d, to none: %d" % (right, wrong, none))

# Shared names: credit each copy.
out = []
for r in game:
    n, l = r["variable"], r["list"]
    if len(lists_of[n]) == 1:
        continue
    credited = 0
    for f, w in copies(n):
        if l in {by_id[v] for v in candidates(w) if v in by_id}:
            credited += 1
    out.append((l, n, credited, refs[(l, n)]))
with open(os.path.join(run, "attributed.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("list\tvariable\tcopies_credited\twalk_references\n")
    for row in out:
        f.write("\t".join(map(str, row)) + "\n")
unref = [o for o in out if o[3] == 0]
print("shared-name variables: %d; with no walk reference: %d, of which no copy on disk credited to their list: %d"
      % (len(out), len(unref), sum(1 for o in unref if o[2] == 0)))
print("  no reference but a copy credited:", [(o[0], o[1], o[2]) for o in unref if o[2] > 0][:20])
ref_zero = [o for o in out if o[3] > 0 and o[2] == 0]
print("  referenced but no copy credited (should be few):", len(ref_zero), ref_zero[:10])
