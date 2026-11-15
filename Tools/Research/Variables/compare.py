"""What a collect.ps1 run says against ModForge's vanilla variable catalog.

    python Tools/Research/Variables/compare.py Tools/Research/Variables/runs/<date>

Prints: lists and names the catalog has that the game does not (and the other way), how many
variables something in the game reads or sets structurally, and which have no such reference
anywhere (the ones to keep looking for). Writes <run>/variables.tsv: one row per variable with
its list, type, saved, value at the main menu, and its reference and mention counts.
"""
import collections, csv, os, re, sys

run = sys.argv[1]
repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
catalog = re.findall(r'new\("([^"]*)", "([^"]*)", "([^"]*)"\)',
                     open(os.path.join(repo, "SMSModForge", "Model", "VanillaGameVariables.cs"), encoding="utf-8-sig").read())


def rows(name, part):
    path = os.path.join(run, part, name)
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8") as f:
        return list(csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE))


lists = {p: rows("lists.tsv", p) for p in ("menu", "game")}
game_pairs = {(r["list"], r["variable"]) for r in lists["game"]}
game_lists = collections.Counter(r["list"] for r in lists["game"])
cat_pairs = {(l, n) for n, l, _ in catalog}
cat_lists = collections.Counter(l for l, _ in cat_pairs)

print("game: %d lists, %d list/variable pairs; catalog: %d lists, %d pairs" % (len(game_lists), len(game_pairs), len(cat_lists), len(cat_pairs)))
print("lists only in the catalog:", sorted(set(cat_lists) - set(game_lists)))
print("lists only in the game:", sorted(set(game_lists) - set(cat_lists)))
only_cat = sorted(cat_pairs - game_pairs)
only_game = sorted(game_pairs - cat_pairs)
print("pairs only in the catalog (%d):" % len(only_cat), only_cat[:12])
print("pairs only in the game (%d):" % len(only_game), only_game[:12])
unnamed = sorted({r["list"] for r in lists["game"] if r["unnamed_asset"] == "yes"})
print("unnamed list assets in the game:", unnamed)
saved = collections.Counter((r["list"], r["saved"]) for r in lists["game"])

refs = collections.defaultdict(list)
for part in ("menu", "game"):
    for r in rows("refs.tsv", part):
        refs[(r["list"], r["variable"])].append((part, r))
mentions = collections.Counter()
for part in ("menu", "game"):
    for r in rows("mentions.tsv", part):
        mentions[r["variable"]] += int(r["count"])

menu_value = {(r["list"], r["variable"]): r["value_now"] for r in lists["menu"]}
menu_lists = {r["list"] for r in lists["menu"]}
unmatched = sum(len(v) for k, v in refs.items() if k[1].startswith("?"))
print("references whose name did not match its list:", unmatched)

named = [p for p in game_pairs if refs.get(p)]
print("variables with a structural reference: %d of %d" % (len(named), len(game_pairs)))
none = sorted(p for p in game_pairs if not refs.get(p))
print("with none (%d), of which named in some text: %d" % (len(none), sum(1 for p in none if mentions[p[1]])))
by_list = collections.Counter(l for l, _ in none)
print("with none, by list:", by_list.most_common(20))

with open(os.path.join(run, "variables.tsv"), "w", encoding="utf-8", newline="") as f:
    w = csv.writer(f, delimiter="\t", lineterminator="\n")
    w.writerow(["list", "variable", "type", "saved", "value_at_menu", "refs", "set_by", "read_by", "mentions"])
    types = {(r["list"], r["variable"]): (r["type"], r["saved"]) for r in lists["game"]}
    for p in sorted(game_pairs):
        rs = [r for _, r in refs.get(p, [])]
        sets = sum(1 for r in rs if r["holder"].split(".")[-1].startswith("Set"))
        reads = sum(1 for r in rs if r["holder"].split(".")[-1].startswith("Get"))
        value = menu_value.get(p, "") if p[0] in menu_lists else ""
        w.writerow([p[0], p[1], types[p][0], types[p][1], value, len(rs), sets, reads, mentions[p[1]]])
holders = collections.Counter(r["holder"].split(".")[-1] for v in refs.values() for _, r in v)
print("holder kinds:", holders.most_common(25))
print("unmatched references:", [(k[1][:60], r["root"][:70]) for k, v in refs.items() if k[1].startswith("?") for _, r in v][:13])

# The control: the Gallery's evidence reads the same game by instruction titles. A name it sees in a
# script or a conversation that this run has no structural reference for is a gap in this walk.
if len(sys.argv) > 2:
    ev = sys.argv[2]
    pattern = re.compile(r"\[([^\]\[]+)\]")
    gallery = set()
    for path in [os.path.join(ev, "scripts.txt")] + [os.path.join(ev, "dialogues", f) for f in os.listdir(os.path.join(ev, "dialogues"))]:
        for m in pattern.finditer(open(path, encoding="utf-8").read()):
            gallery.add(m.group(1))
    names_with_refs = {n for (_, n), v in refs.items() if v}
    game_names = {n for _, n in game_pairs}
    seen_there = gallery & game_names
    missed = sorted(seen_there - names_with_refs)
    print("control: names the Gallery's evidence sees %d, of them without a reference here %d:" % (len(seen_there), len(missed)), missed[:30])
    by_name_none = sorted(game_names - names_with_refs)
    print("by name (any list): %d names, %d with no reference here" % (len(game_names), len(by_name_none)))
