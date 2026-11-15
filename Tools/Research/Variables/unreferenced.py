"""Every signal a collect.ps1 run holds about the variables nothing in the game references.

    python Tools/Research/Variables/unreferenced.py Tools/Research/Variables/runs/<date>

For each such variable: its value in the test save against the new-game value (a difference means
something set it at some point), references naming it on a different list (a script pointing at the
wrong list), and its mentions in text. Writes <run>/unreferenced.tsv and prints the counts.
"""
import collections, csv, os, sys

run = sys.argv[1]


def rows(part, name):
    p = os.path.join(run, part, name)
    with open(p, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


game = rows("game", "lists.tsv")
menu = {(r["list"], r["variable"]): r["value_now"] for r in rows("menu", "lists.tsv")}
refs = rows("game", "refs.tsv") + rows("menu", "refs.tsv")
referenced = {(r["list"], r["variable"]) for r in refs if not r["variable"].startswith("?")}
by_name_elsewhere = collections.defaultdict(set)
for r in refs:
    if not r["variable"].startswith("?"):
        by_name_elsewhere[r["variable"]].add(r["list"])
wrong_list = collections.defaultdict(list)
for r in refs:
    if r["variable"].startswith("?"):
        for name in r["variable"][1:].strip().split("|"):
            wrong_list[name].append(r["list"] + " @ " + r["root"])
mentions = {}
for part in ("game", "menu"):
    for r in rows(part, "mentions.tsv"):
        m = mentions.setdefault(r["variable"], [0, ""])
        m[0] += int(r["count"])
        m[1] = m[1] or r["examples"]

out = []
for r in game:
    k = (r["list"], r["variable"])
    if k in referenced:
        continue
    default = menu.get(k, "")
    differs = default != "" and r["value_now"] != default
    out.append({
        "list": r["list"], "variable": r["variable"], "type": r["type"], "saved": r["saved"],
        "new_game": default, "test_save": r["value_now"], "save_differs": "yes" if differs else "",
        "same_name_referenced_on": ",".join(sorted(by_name_elsewhere.get(r["variable"], ()))),
        "named_on_wrong_list_by": " | ".join(wrong_list.get(r["variable"], [])[:3]),
        "mentions": mentions.get(r["variable"], [0, ""])[0],
        "mention_examples": mentions.get(r["variable"], [0, ""])[1][:300],
    })

with open(os.path.join(run, "unreferenced.tsv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(out[0].keys()), delimiter="\t", lineterminator="\n")
    w.writeheader()
    w.writerows(out)

print("unreferenced:", len(out))
print("  value in the test save differs from a new game:", sum(1 for o in out if o["save_differs"]))
print("  no new-game value read (list not loaded at the menu):", sum(1 for o in out if o["new_game"] == ""))
print("  same name referenced on another list:", sum(1 for o in out if o["same_name_referenced_on"]))
print("  named by a script on a list that lacks it:", sum(1 for o in out if o["named_on_wrong_list_by"]))
print("  mentioned in text:", sum(1 for o in out if o["mentions"]))
print("  none of these signals:", sum(1 for o in out if not (o["save_differs"] or o["same_name_referenced_on"] or o["named_on_wrong_list_by"] or o["mentions"])))
for o in [o for o in out if o["save_differs"]][:8]:
    print("   differs:", o["list"], o["variable"], o["new_game"], "->", o["test_save"])
for o in [o for o in out if o["named_on_wrong_list_by"]][:6]:
    print("   wrong list:", o["list"], o["variable"], "<-", o["named_on_wrong_list_by"][:120])
for o in [o for o in out if o["mentions"]][:6]:
    print("   mention:", o["list"], o["variable"], o["mentions"], o["mention_examples"][:160])
