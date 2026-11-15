"""One dossier per variable from a collect.ps1 run, cut into batches for the hint writers.

    python Tools/Research/Variables/make_dossiers.py Tools/Research/Variables/runs/<date>

Writes <run>/dossiers/<batch>.md (every variable of the batch: what it is, everything that sets,
reads or watches it, with the line or script it sits on and the evidence file to open) and
<run>/batches.tsv (batch, lists, variables). A list is never split unless it is too big for one
batch, so a family of variables (Gallery, Events_*) is read together.
"""
import collections, csv, os, re, sys

run = sys.argv[1]
PER_BATCH = 80
SHOW = 15


def rows(part, name):
    p = os.path.join(run, part, name)
    if not os.path.exists(p):
        return []
    with open(p, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


game = rows("game", "lists.tsv")
menu = {(r["list"], r["variable"]): r["value_now"] for r in rows("menu", "lists.tsv")}
menu_lists = {r["list"] for r in rows("menu", "lists.tsv")}
files = {(r["list"], r["variable"]): r for r in rows(".", "files.tsv")}
refs = collections.defaultdict(list)
seen_refs = set()
for part in ("game", "menu"):
    for r in rows(part, "refs.tsv"):
        # An asset (a quest, the dialogue settings) is the same object on the main menu and in the game:
        # once, not twice.
        key = (r["list"], r["variable"], r["holder"], r["root_kind"], r["root"], r["context"], r["field_path"])
        if r["root_kind"] == "asset" and key in seen_refs:
            continue
        seen_refs.add(key)
        refs[(r["list"], r["variable"])].append(dict(r, part=part))
wrong = collections.defaultdict(list)
for part in ("menu", "game"):
    for r in rows(part, "refs.tsv"):
        if r["variable"].startswith("?"):
            for n in r["variable"][1:].strip().split("|"):
                wrong[n].append(r)
dialogue_files = set(os.listdir(os.path.join(run, "game", "dialogues")))

# Instructions that run only in the Unity editor: those AFTER a "Check If is Editor" in their own script
# (a debug key). Per script and per instruction: one object holds several scripts, and an instruction
# before the check, or in a script without one, runs in a player's game. The first version tagged by
# object and flagged a real one (10_Gameplay's fix-lock key) - v14's writer caught it.
editor_after = collections.defaultdict(list)   # (path, component) -> [(instructions after a check, before one)]
block = None
for raw in open(os.path.join(run, "game", "scripts.txt"), encoding="utf-8"):
    text = raw.rstrip("\n")
    if text and not text.startswith(" ") and " : " in text:
        path, rest = text.split(" : ", 1)
        block = {"after": set(), "before": set(), "checked": False}
        editor_after[(path, rest.split(" ")[0])].append(block)
    elif block is not None and text.startswith("    "):
        title = text.strip()
        if "is Editor" in title:
            block["checked"] = True
        elif block["checked"]:
            block["after"].add(title)
        else:
            block["before"].add(title)


def runs_only_in_editor(root, context):
    blocks = editor_after.get(tuple(root.split(" : ", 1)), [])
    title = context.split(" > ")[0].replace("instruction ", "", 1) if context.startswith("instruction ") else ""
    if not title or not blocks:
        return False
    holding = [b for b in blocks if title in b["after"] or title in b["before"]]
    return bool(holding) and all(title in b["after"] and title not in b["before"] for b in holding)


def dialogue_file(root):
    path = root.split(" : ")[0]
    name = re.sub(r"[^A-Za-z0-9_\-\. ]", "_", path) + ".txt"
    return name if name in dialogue_files else ""


def kind(r):
    h = r["holder"].split(".")[-1]
    if h.startswith("FieldSet"):
        return "set"
    if h.startswith("FieldGet"):
        return "read"
    if h.startswith("Detector"):
        return "watched"
    return "other (" + h + ")"


def line(r):
    where = r["root"]
    d = dialogue_file(where)
    ctx = r["context"] or "(no instruction or condition around it)"
    bits = ["%s %s" % (r["root_kind"], where), ctx]
    if r["active"] == "no":
        bits.append("object switched off now")
    if runs_only_in_editor(where, r["context"]):
        bits.append("EDITOR-ONLY: comes after a 'Check If is Editor' in its script, a debug key that never runs in a player's game")
    if r["part"] == "menu":
        bits.append("main menu")
    if d:
        bits.append("conversation: dialogues/" + d)
    return "  - " + " | ".join(bits)


order = []
for r in game:
    if (r["list"], r["variable"]) not in {(o["list"], o["variable"]) for o in order[-1:]}:
        order.append(r)
by_list = collections.OrderedDict()
for r in game:
    by_list.setdefault(r["list"], []).append(r)

batches, current, size = [], [], 0
for lst, members in by_list.items():
    if len(members) > PER_BATCH:
        if current:
            batches.append(current)
            current, size = [], 0
        for i in range(0, len(members), PER_BATCH):
            batches.append([(lst, members[i:i + PER_BATCH])])
        continue
    if size + len(members) > PER_BATCH and current:
        batches.append(current)
        current, size = [], 0
    current.append((lst, members))
    size += len(members)
if current:
    batches.append(current)

os.makedirs(os.path.join(run, "dossiers"), exist_ok=True)
with open(os.path.join(run, "batches.tsv"), "w", encoding="utf-8", newline="") as index:
    iw = csv.writer(index, delimiter="\t", lineterminator="\n")
    iw.writerow(["batch", "lists", "variables"])
    for b, parts in enumerate(batches, 1):
        name = "v%02d" % b
        out = ["# Dossier %s" % name, "",
               "Every variable of this batch as the running game holds it (1.8E, test copy, no mod but ModForge).",
               "`set` = something writes it, `read` = a condition or value reads it, `watched` = a script runs when it changes.",
               "Context reads outward-in: the conversation line (node), then the instruction or condition holding the reference.", ""]
        count = 0
        for lst, members in parts:
            saved = members[0]["saved"]
            out += ["## List %s (%s variables; %s)" % (lst, len(members),
                    "saved with the game" if saved == "yes" else "NOT saved: back to its starting value whenever a game is loaded"), ""]
            for r in members:
                count += 1
                k = (r["list"], r["variable"])
                rs = refs.get(k, [])
                start = menu.get(k, "") if r["list"] in menu_lists else "(not loaded on the main menu)"
                f = files.get(k, {})
                out.append("### %s[%s]" % (r["list"], r["variable"]))
                out.append("type %s; value in a new game: %s; in the test save: %s" % (r["type"], start or "(empty)", r["value_now"] or "(empty)"))
                if f and f["lists_defining_name"] == "1" and len(r["variable"]) > 2:
                    out.append("name in the game's files: %s cop%s beyond its own definition" % (
                        f["extra_copies"], "y" if f["extra_copies"] == "1" else "ies"))
                elif f:
                    out.append("name in the game's files: too common to count (shared by %s lists, or very short)" % f["lists_defining_name"])
                grouped = collections.OrderedDict((g, []) for g in ("set", "read", "watched"))
                for x in rs:
                    grouped.setdefault(kind(x), []).append(x)
                if not rs:
                    out.append("NO REFERENCE anywhere in the running game (scene, prefabs, assets, main menu).")
                for g, xs in grouped.items():
                    if not xs:
                        continue
                    out.append("%s by %d:" % (g, len(xs)))
                    out += [line(x) for x in xs[:SHOW]]
                    if len(xs) > SHOW:
                        out.append("  - ... and %d more (refs.tsv)" % (len(xs) - SHOW))
                if r["variable"] in wrong:
                    out.append("named on a list that LACKS it by %d script(s), e.g. %s on list %s" % (
                        len(wrong[r["variable"]]), wrong[r["variable"]][0]["root"], wrong[r["variable"]][0]["list"]))
                out.append("")
        open(os.path.join(run, "dossiers", name + ".md"), "w", encoding="utf-8", newline="\n").write("\n".join(out))
        iw.writerow([name, ",".join(l for l, _ in parts), count])
print(len(batches), "batches:", [(("v%02d" % (i + 1)), sum(len(m) for _, m in p)) for i, p in enumerate(batches)])
sizes = [os.path.getsize(os.path.join(run, "dossiers", "v%02d.md" % (i + 1))) // 1024 for i in range(len(batches))]
print("dossier sizes KB:", sizes)
