"""Which writer batches are done, and whether each finished one is whole.

    python Tools/Research/Variables/status.py Tools/Research/Variables/runs/<date> [--missing]

A batch counts as done only when hints/<batch>.tsv exists (renamed from .partial at the end) and has one
row for every variable in its dossier, each with a confidence the brief allows. --missing prints only the
batches still to run, one per line, for starting the next ones.
"""
import csv, os, re, sys

run = sys.argv[1]
only_missing = "--missing" in sys.argv
with open(os.path.join(run, "batches.tsv"), encoding="utf-8") as f:
    batches = list(csv.DictReader(f, delimiter="\t"))
done, broken, missing = [], [], []
for b in batches:
    name = b["batch"]
    dossier = open(os.path.join(run, "dossiers", name + ".md"), encoding="utf-8").read()
    want = re.findall(r"^### (.+?)\[(.+)\]$", dossier, re.M)
    path = os.path.join(run, "hints", name + ".tsv")
    if not os.path.exists(path):
        missing.append(name)
        continue
    with open(path, encoding="utf-8") as f:
        rows = list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))
    got = [(r.get("list", ""), r.get("variable", "")) for r in rows]
    bad = [r for r in rows if r.get("confidence") not in ("sure", "likely", "unsure") or not r.get("hint")]
    long = [r for r in rows if len(r.get("hint", "")) > 160]
    if sorted(got) != sorted(want) or bad:
        broken.append("%s: %d of %d rows, %d without a hint or confidence, %d over 160 characters" % (name, len(set(got) & set(want)), len(want), len(bad), len(long)))
    else:
        done.append((name, len(rows), sum(1 for r in rows if r["confidence"] == "sure"), len(long)))
if only_missing:
    print("\n".join(missing + [b.split(":")[0] for b in broken]))
    sys.exit(0)
print("done %d, incomplete %d, not started %d (of %d)" % (len(done), len(broken), len(missing), len(batches)))
for d in done:
    print("  done %s: %d rows, %d sure, %d over 160 characters" % d)
for b in broken:
    print("  INCOMPLETE " + b)
if missing:
    print("  not started: " + " ".join(missing))
