"""The game's own files on disk, as a second witness beside collect.ps1's walk of the running game.

    python Tools/Research/Variables/scan_files.py Tools/Research/Variables/runs/<date> "<test copy>\\Starmaker Story_Data"

Unity writes a string as its length (4 bytes) then its bytes, so a variable's name stored anywhere in a
scene, prefab or asset - loaded at runtime or not - is found by that exact pattern. The list that
defines a variable holds its name once; any further copy is something else naming it. The game's code
is searched too, for the name as a string literal (UTF-16, as .NET stores them).

The control: variables the walk DID find references for must show further copies here. If they did
not, this method could not see references and its silence about the others would mean nothing.
Writes <run>/files.tsv: variable, lists defining the name, copies in data files, where, copies in code.
"""
import collections, csv, os, re, struct, sys

run, data = sys.argv[1], sys.argv[2]


def rows(part, name):
    with open(os.path.join(run, part, name), encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


game = rows("game", "lists.tsv")
refs = rows("game", "refs.tsv") + rows("menu", "refs.tsv")
referenced = {(r["list"], r["variable"]) for r in refs}
defs = collections.Counter(r["variable"] for r in game)
names = sorted(defs)
by_length = collections.defaultdict(set)
for n in names:
    by_length[len(n.encode("utf-8"))].add(n.encode("utf-8"))

copies = collections.Counter()
where = collections.defaultdict(collections.Counter)
# Copies are not told apart by kind: a rule that took a type id before the name for a definition (a
# list's own, or a local variable of the same name) also took every reference for one - its control
# found copies beyond "definitions" for only 143 of 1350 referenced names (2026-11-13). A copy beyond a
# list's own definition stays unexplained, and a hint resting on "nothing reads it" stays likely.
serialized = [f for f in os.listdir(data) if os.path.isfile(os.path.join(data, f))
              and not f.endswith((".resS", ".resource", ".info", ".log"))]
for f in serialized:
    blob = open(os.path.join(data, f), "rb").read()
    for length, wanted in by_length.items():
        prefix = re.escape(struct.pack("<i", length))
        for m in re.finditer(b"(?=" + prefix + b"(.{" + str(length).encode() + b"}))", blob, re.S):
            s = m.group(1)
            if s in wanted:
                n = s.decode("utf-8")
                copies[n] += 1
                where[n][f] += 1

code = collections.Counter()
code_where = collections.defaultdict(set)
managed = os.path.join(data, "Managed")
dlls = {f: open(os.path.join(managed, f), "rb").read() for f in os.listdir(managed) if f.endswith(".dll")}
unref_names = {r["variable"] for r in game if (r["list"], r["variable"]) not in referenced}
control = sorted({r["variable"] for r in game if (r["list"], r["variable"]) in referenced and defs[r["variable"]] == 1})[:60]
for n in sorted(unref_names | set(control)):
    needle = n.encode("utf-16-le")
    for f, blob in dlls.items():
        c = blob.count(needle)
        if c:
            code[n] += c
            code_where[n].add(f)

with open(os.path.join(run, "files.tsv"), "w", encoding="utf-8", newline="") as out:
    w = csv.writer(out, delimiter="\t", lineterminator="\n")
    w.writerow(["list", "variable", "referenced_in_walk", "lists_defining_name", "copies_in_data", "extra_copies", "data_files", "copies_in_code", "code_files"])
    for r in game:
        n = r["variable"]
        extra = copies[n] - defs[n]
        w.writerow([r["list"], n, "yes" if (r["list"], n) in referenced else "", defs[n], copies[n], extra,
                    ",".join("%s:%d" % kv for kv in where[n].most_common()), code.get(n, ""), ",".join(sorted(code_where.get(n, ())))])


def extra(n):
    return copies[n] - defs[n]


uniq_ref = [n for n in {r["variable"] for r in game if (r["list"], r["variable"]) in referenced} if defs[n] == 1]
uniq_unref = [n for n in unref_names if defs[n] == 1 and not any((l, n) in referenced for l in {r["list"] for r in game})]
print("files scanned:", len(serialized), "code libraries:", len(dlls))
print("CONTROL - referenced, name in one list only: %d; with further copies on disk: %d; with none: %d"
      % (len(uniq_ref), sum(1 for n in uniq_ref if extra(n) > 0), sum(1 for n in uniq_ref if extra(n) <= 0)))
print("   with none, examples:", sorted(n for n in uniq_ref if extra(n) <= 0)[:15])
print("   definitions found (each name at least once):", sum(1 for n in names if copies[n] >= 1), "of", len(names))
print("UNREFERENCED, name in one list only: %d; with further copies on disk: %d; in code: %d"
      % (len(uniq_unref), sum(1 for n in uniq_unref if extra(n) > 0), sum(1 for n in uniq_unref if code.get(n))))
print("   with further copies:", sorted((n, extra(n), dict(where[n])) for n in uniq_unref if extra(n) > 0)[:20])
print("   in code:", sorted((n, sorted(code_where[n])) for n in uniq_unref if code.get(n))[:20])
print("   control names in code:", sum(1 for n in control if code.get(n)), "of", len(control))
