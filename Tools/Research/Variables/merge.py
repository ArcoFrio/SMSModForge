"""The checkers' verdicts beside the hints they dispute, and which batches are checked.

    python Tools/Research/Variables/merge.py Tools/Research/Variables/runs/<date> [--missing]

Writes <run>/disputes.tsv: every disputed or unsure hint with what the checker says it should be and why,
for settling one by one (in the evidence or the test game) before any hint changes. --missing prints the
batches whose verdicts are not written yet, one per line.
"""
import csv, glob, os, sys

run = sys.argv[1]
sys.stdout.reconfigure(encoding="utf-8")
with open(os.path.join(run, "batches.tsv"), encoding="utf-8") as f:
    batches = [r["batch"] for r in csv.DictReader(f, delimiter="\t")]
done = [b for b in batches if os.path.exists(os.path.join(run, "verdicts", b + ".tsv"))]
missing = [b for b in batches if b not in done]
if "--missing" in sys.argv:
    print("\n".join(missing))
    sys.exit(0)

hints = {}
for path in glob.glob(os.path.join(run, "hints", "*.tsv")):
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
            hints[(r["list"], r["variable"])] = (os.path.basename(path)[:-4], r)
rows, summaries = [], []
for b in done:
    with open(os.path.join(run, "verdicts", b + ".tsv"), encoding="utf-8") as f:
        for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
            if r["list"].startswith("#"):
                summaries.append((b, r["evidence"]))
                continue
            batch, h = hints.get((r["list"], r["variable"]), (b, {}))
            rows.append([batch, r["list"], r["variable"], r["kind"], r["verdict"], h.get("hint", "(no such hint)"),
                         h.get("confidence", ""), r["should_be"], r["evidence"]])
with open(os.path.join(run, "disputes.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("batch\tlist\tvariable\tkind\tverdict\thint_now\tconfidence_now\tshould_be\tevidence\n")
    for row in rows:
        f.write("\t".join(x.replace("\t", " ") for x in row) + "\n")
print("checked %d of %d batches; disputes %d, unsure %d" % (len(done), len(batches),
      sum(1 for r in rows if r[4] == "dispute"), sum(1 for r in rows if r[4] == "unsure")))
for b, s in summaries:
    print("  %s: %s" % (b, s))
if missing:
    print("  not checked: " + " ".join(missing))
