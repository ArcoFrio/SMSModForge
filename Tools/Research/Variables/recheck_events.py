"""Hints that rest on "nothing switches it on / nothing runs it", against the Unity events.

    python Tools/Research/Variables/recheck_events.py Tools/Research/Variables/runs/<date>

Until 2026-11-13 the evidence did not read Unity events: what each "Invoke Methods" calls and every button's
own On Click. A claim that something never happens because nothing switches an object on, runs a component
or plays a conversation was made without them, and was wrong once (the gallery tiles). For every hint
(final where settled, else as written) whose hint or evidence makes such a claim, this lists the objects
its evidence names that a "Unity event:" or "Invoke Methods:" row now targets, or that an EnableAllChildren
call reaches as a child. Writes <run>/recheck-events.tsv for the judges; prints the count.
"""
import csv, os, re, sys

run = sys.argv[1]
sys.stdout.reconfigure(encoding="utf-8")
claim = re.compile(r"(?i)\b(nothing|no script|no trigger|no instruction|no button|never)\b[^.;]{0,60}?"
                   r"\b(switch|run|open|play|show|enable|activate|turn|start|set|call|reach)")

event_targets = {}  # target path -> [owner: call]
with open(os.path.join(run, "game", "targets.tsv"), encoding="utf-8") as f:
    for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
        if r["instruction"].startswith(("Unity event:", "Invoke Methods:")):
            event_targets.setdefault(r["target"], []).append(r["owner"] + " -> " + r["instruction"])


def reached(name):
    """Event calls whose target is this object, ends with it, or is a parent whose children all get enabled."""
    hits = []
    for target, calls in event_targets.items():
        last = target.rsplit("/", 1)[-1]
        if last == name or target.endswith("/" + name):
            hits += calls
        elif any("EnableAllChildren" in c for c in calls) and ("/" + last + "/") in ("/" + name + "/"):
            hits += calls
    return hits


with open(os.path.join(run, "batches.tsv"), encoding="utf-8") as f:
    batches = [r["batch"] for r in csv.DictReader(f, delimiter="\t")]
out = []
for b in batches:
    path = os.path.join(run, "final", b + ".tsv")
    if not os.path.exists(path):
        path = os.path.join(run, "hints", b + ".tsv")
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
            text = r["hint"] + " " + r["evidence"]
            if r["hint"].startswith("Not used") or not claim.search(text):
                continue
            # Objects the row names: path segments and quoted or bare object-like words.
            names = set(re.findall(r"[A-Za-z0-9_\-]+(?: \(\d+\))?", r["evidence"]))
            names = {n for n in names if len(n) > 3 and not n.isdigit()}
            hits = []
            for n in sorted(names):
                for h in reached(n):
                    hits.append(n + ": " + h)
            if hits:
                out.append((b, r["list"], r["variable"], claim.search(text).group(0), " | ".join(sorted(set(hits))[:6])))

with open(os.path.join(run, "recheck-events.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("batch\tlist\tvariable\tclaim\tevent calls reaching an object it names\n")
    for row in out:
        f.write("\t".join(x.replace("\t", " ") for x in row) + "\n")
print("%d rows claim something never happens and name an object a Unity event reaches" % len(out))
for row in out[:15]:
    print("  %s %s[%s]: %s" % row[:4])
