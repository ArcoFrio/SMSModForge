"""The hints as they will ship: each batch's hints with its settled rows put in, then checked by rule.

    python Tools/Research/Variables/finalize.py Tools/Research/Variables/runs/<date>

Writes <run>/final/<batch>.tsv for every batch whose verdicts are written and, where the checker
disputed anything, whose settled rows are written too (a batch the checker found nothing in needs no
settling). Every other batch is listed as not ready. A "Not used" row is raised to sure: the coverage
proof (README, "Why no reference means none anywhere") settles it, however common its name.

Then it checks every final row against the brief's rules a script can check, and prints each breach:
a hint over 160 characters, a family word or a test-save name in a hint, a list name or "this
variable" or "probably", a "Not used" row worded otherwise, a confidence that is not sure/likely/unsure,
a settled row for a variable with no hint, or a disputed variable with no settled row.
"""
import csv, glob, os, re, sys

run = sys.argv[1]
sys.stdout.reconfigure(encoding="utf-8")
NOT_USED = "Not used by the game: nothing sets or reads it (1.8E)."
PROOF = " Coverage proof: every data file with variable data is walked (README)."


def read(path):
    with open(path, encoding="utf-8") as f:
        return [r for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE)
                if not r["list"].startswith("#")]


with open(os.path.join(run, "batches.tsv"), encoding="utf-8") as f:
    batches = [r["batch"] for r in csv.DictReader(f, delimiter="\t")]
lists = set()
with open(os.path.join(run, "game", "lists.tsv"), encoding="utf-8") as f:
    for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
        lists.add(r["list"])

# The family words a player types on the new-game screen, the new game's defaults for them, and what the
# test save filled them with: none of them belongs in a hint (brief-hints.md, "Characters by name").
family = r"\b(step-?(mom|mum|mother|dad|father|son|brother|sister)s?|moms?|mums?|mothers?|dads?|fathers?|" \
         r"brothers?|sisters?|sons?|daughters?|parents?|family|families)\b"
banned = [(re.compile(family, re.I), "family word"),
          (re.compile(r"\b(Milf|Dude|Buddy|Peter)\b"), "player-typed name"),
          (re.compile(r"\bthis variable\b", re.I), "'this variable'"),
          (re.compile(r"\bprobably\b", re.I), "'probably'"),
          (re.compile(r"\{[A-Za-z]+\}"), "raw token"),
          # The editor shows the hint alone: the values column is research data, not on screen.
          (re.compile(r"\b(see|in) (the )?values\b", re.I), "points to the values column")]
# "Friend" and "Group" are the new game's {S} and {F}, and ordinary words too: only flagged capitalised
# mid-sentence, where they read as a name.
banned.append((re.compile(r"(?<=[a-z,] )(Friend|Group)\b"), "player-typed name (capitalised)"))
# Pronouns, from ModForge's own table of how the game speaks of each character (Model/VanillaPronouns.cs:
# read out of the game's lines; River is male, by the author). A hint naming only men and saying "she/her",
# or only women and saying "he/his/him", is flagged.
_pron_src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "SMSModForge", "Model",
                              "VanillaPronouns.cs"), encoding="utf-8").read()


def _names(var):
    block = _pron_src[_pron_src.find("string[] " + var):]
    return set(re.findall(r'"([^"]+)"', block[block.find("{"):block.find("};")]))


men, women = _names("Men"), _names("Women")
she, he = re.compile(r"\b(she|her|hers|herself)\b", re.I), re.compile(r"\b(he|his|him|himself)\b", re.I)


def pronoun_fault(hint):
    named_men = {n for n in men if re.search(r"\b" + re.escape(n) + r"\b", hint)}
    named_women = {n for n in women if re.search(r"\b" + re.escape(n) + r"\b", hint)}
    if named_men and not named_women and she.search(hint):
        return "'%s' for %s" % (she.search(hint).group(0), ", ".join(sorted(named_men)))
    if named_women and not named_men and he.search(hint):
        return "'%s' for %s" % (he.search(hint).group(0), ", ".join(sorted(named_women)))
    return None


list_name = re.compile(r"\b(" + "|".join(sorted((re.escape(x) for x in lists), key=len, reverse=True)) +
                       r")\[")

lint_only = "--lint-hints" in sys.argv  # check the hints as written, every batch, and write nothing
if not lint_only:
    os.makedirs(os.path.join(run, "final"), exist_ok=True)
ready, waiting, breaches = [], [], []
for b in batches:
    hp, vp, sp = (os.path.join(run, d, b + ".tsv") for d in ("hints", "verdicts", "settled"))
    if lint_only:
        vp = sp = os.path.join(run, "-none-")
    if not os.path.exists(hp) or (not lint_only and not os.path.exists(vp)):
        waiting.append(b + " (not checked)")
        continue
    disputed = {(r["list"], r["variable"]) for r in read(vp)} if os.path.exists(vp) else set()
    partial = not os.path.exists(sp) and os.path.exists(sp + ".partial")
    if partial:
        sp += ".partial"  # a judge still at work: checked by rule for them, never written as final
    elif disputed and not os.path.exists(sp):
        waiting.append(b + " (not settled)")
        continue
    hints = read(hp)
    settled = {(r["list"], r["variable"]): r for r in read(sp)} if os.path.exists(sp) else {}
    keys = {(r["list"], r["variable"]) for r in hints}
    for k in sorted(set(settled) - keys):
        breaches.append((b, k, "settled row for a variable with no hint"))
    for k in sorted(disputed - set(settled)):
        breaches.append((b, k, "disputed but not settled"))
    out = []
    for h in hints:
        k = (h["list"], h["variable"])
        row = dict(settled.get(k, h))
        if row["hint"].startswith("Not used") and row["confidence"] != "sure":
            row["confidence"] = "sure"
            # The writer's own doubt ("Not sure: too short to count") is what the proof settles: drop
            # that sentence, so the row does not say "sure" and "not sure" at once.
            row["evidence"] = re.sub(r"[^.;]*(not sure|too short|uncountable)[^.;]*[.;]?\s*", "", row["evidence"],
                                     flags=re.I).strip()
            if "Coverage proof" not in row["evidence"]:
                row["evidence"] = (row["evidence"].rstrip() + PROOF).strip()
        out.append(row)
        hint = row["hint"]
        if len(hint) > 160:
            breaches.append((b, k, "hint is %d characters" % len(hint)))
        if hint.startswith("Not used") and hint != NOT_USED:
            breaches.append((b, k, "'Not used' worded otherwise"))
        if hint == NOT_USED and row.get("values", ""):
            breaches.append((b, k, "'Not used' row with values"))
        if row["confidence"] not in ("sure", "likely", "unsure"):
            breaches.append((b, k, "confidence '%s'" % row["confidence"]))
        # A word rule can be wrong for one row (Names[M-Term] IS the family word; "the family" paraphrased
        # is allowed): a judge who looked and kept it says so with decision "keep", and only then is the
        # breach let through. Length and wording rules above are never let through.
        kept = "keep" in (settled.get(k, {}).get("decision") or "").split(",")  # "keep" or "amend,keep"
        for rx, what in banned:
            m = rx.search(hint)
            if m and not kept:
                breaches.append((b, k, "%s: '%s'" % (what, m.group(0))))
        m = list_name.search(hint)
        if m:
            breaches.append((b, k, "list name: '%s'" % m.group(0)))
        fault = pronoun_fault(hint)
        if fault and not kept:
            breaches.append((b, k, "pronoun: " + fault))
    if lint_only:
        ready.append(b)
        continue
    if partial:
        waiting.append(b + " (being settled)")
        continue
    with open(os.path.join(run, "final", b + ".tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("list\tvariable\thint\tvalues\tconfidence\tevidence\n")
        for r in out:
            f.write("\t".join(r.get(c, "").replace("\t", " ") for c in
                              ("list", "variable", "hint", "values", "confidence", "evidence")) + "\n")
    ready.append(b)

print("final: %d of %d batches" % (len(ready), len(batches)))
if waiting:
    print("  waiting: " + ", ".join(waiting))
print("breaches: %d" % len(breaches))
for b, k, what in breaches:
    print("  %s %s[%s]: %s" % (b, k[0], k[1], what))
