"""Changes one settled row by hand, after an in-game measurement or a review decides it.

    python Tools/Research/Variables/settle_row.py <run> <batch> <list> <variable> <decision> <hint|-> <values|-> \
        <confidence|-> <evidence|-> <reason>

A "-" keeps that column as it is. The settled file must exist (a judge has finished the batch); a row is
added when the variable has none. Run finalize.py afterwards. The reason says what settled it, so the
settled file keeps the history of every change made after the judge.
"""
import csv, os, sys

run, batch, lst, var, decision, hint, values, confidence, evidence, reason = sys.argv[1:11]
sys.stdout.reconfigure(encoding="utf-8")
path = os.path.join(run, "settled", batch + ".tsv")
cols = ["list", "variable", "decision", "hint", "values", "confidence", "evidence", "reason"]
with open(path, encoding="utf-8") as f:
    rows = list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))
row = next((r for r in rows if r["list"] == lst and r["variable"] == var), None)
if row is None:
    with open(os.path.join(run, "hints", batch + ".tsv"), encoding="utf-8") as f:
        h = next((r for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE)
                  if r["list"] == lst and r["variable"] == var), None)
    if h is None:
        sys.exit("%s[%s] is not in %s" % (lst, var, batch))
    row = {c: h.get(c, "") for c in cols}
    summary = next((i for i, r in enumerate(rows) if r["list"].startswith("#")), len(rows))
    rows.insert(summary, row)
for c, v in (("decision", decision), ("hint", hint), ("values", values), ("confidence", confidence),
             ("evidence", evidence)):
    if v != "-":
        row[c] = v
# Appended, never replaced: the judge's reason is the row's history (the consistency pass of 2026-11-14
# replaced 155 of them; runs\<date>\settled-before-consistency\ keeps the originals).
old = (row.get("reason") or "").strip()
row["reason"] = (old + " || " + reason) if old and reason not in old else reason
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write("\t".join(cols) + "\n")
    for r in rows:
        f.write("\t".join((r.get(c) or "").replace("\t", " ") for c in cols) + "\n")
print("%s %s[%s]: %s | %s" % (batch, lst, var, row["decision"], row["hint"]))
