"""Puts the settled hints into the editor: the English texts and the table beside them.

    python Tools/Research/Variables/ship_hints.py Tools/Research/Variables/runs/<date> [--draft]

Reads <run>/final/<batch>.tsv (finalize.py writes them) for every variable in the catalog
(SMSModForge/Model/VanillaGameVariables.cs) and writes:

- the [The game's variables] section of SMSModForge/Languages/en.txt, replaced whole: one text per
  variable, keyed vhint.<list>.<name>, with the variable's address as the note above it for
  translators; every "Not used" variable shares vhint.notUsed;
- SMSModForge/Model/VanillaVariableHints.Data.cs: which variables share the "Not used" text, and
  which hints are likely (shown with the "inferred" mark) or unsure ("unconfirmed").

Stops, writing nothing, when a variable has no final hint, a final row names a variable the catalog
does not have, or two variables would get the same key. --draft takes a batch's hints as written where
it has no final rows yet, and says so in the section's note: for building the editor side before
the research is settled, never for a release.
"""
import csv, os, re, sys

run = sys.argv[1]
draft = "--draft" in sys.argv
sys.stdout.reconfigure(encoding="utf-8")
root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
catalog_path = os.path.join(root, "SMSModForge", "Model", "VanillaGameVariables.cs")
en_path = os.path.join(root, "SMSModForge", "Languages", "en.txt")
data_path = os.path.join(root, "SMSModForge", "Model", "VanillaVariableHints.Data.cs")
NOT_USED = "Not used by the game: nothing sets or reads it (1.8E)."
HEADING = "[The game's variables]"

with open(catalog_path, encoding="utf-8") as f:
    catalog = re.findall(r'new\("(.*?)", "(.*?)", "(.*?)"\)', f.read())  # (name, list, type)
with open(os.path.join(run, "batches.tsv"), encoding="utf-8") as f:
    batches = [r["batch"] for r in csv.DictReader(f, delimiter="\t")]

rows, drafted, problems = {}, [], []
for b in batches:
    path = os.path.join(run, "final", b + ".tsv")
    if not os.path.exists(path):
        if not draft:
            problems.append("%s has no final rows (finalize.py)" % b)
            continue
        path = os.path.join(run, "hints", b + ".tsv")
        drafted.append(b)
    with open(path, encoding="utf-8") as f:
        for r in csv.DictReader(f, delimiter="\t", quoting=csv.QUOTE_NONE):
            if not r["list"].startswith("#"):
                rows[(r["list"], r["variable"])] = r

known = {(l, n) for n, l, t in catalog}
for k in sorted(set(rows) - known):
    problems.append("%s[%s] is not in the catalog" % k)
# A text's key ignores case and the game's names do not (Mei-corrupted and mei-corrupted are two
# variables): the second of such a pair gets a numbered key, listed in the table so the editor finds it.
keys, key_of, renamed = {}, {}, []
for n, l, t in catalog:
    if (l, n) not in rows:
        problems.append("%s[%s] has no hint" % (l, n))
        continue
    if rows[(l, n)]["hint"] == NOT_USED:
        continue  # shares vhint.notUsed
    key = "vhint." + l.replace(" ", "_") + "." + n
    if not re.fullmatch(r"[A-Za-z0-9._-]+", key):
        problems.append("%s[%s]: key %s has a character a key cannot" % (l, n, key))
    if key.lower() in keys:
        i = 2
        while ("%s_%d" % (key, i)).lower() in keys:
            i += 1
        key = "%s_%d" % (key, i)
        renamed.append((l, n, key))
    keys[key.lower()] = "%s[%s]" % (l, n)
    key_of[(l, n)] = key
if problems:
    print("nothing written:")
    for p in problems:
        print("  " + p)
    sys.exit(1)

lines = [HEADING, "",
         "# Under each of the game's own variables in the variable dropdowns, and searched with its name.",
         "# Each says what the game does with it (Starmaker Story 1.8E), from research on the running game.",
         "# Keep the game's names as they are: characters, places, items and quoted menu words.",
         "# The note above each is the variable it describes."]
if drafted:
    lines.append("# DRAFT: not yet settled for " + ", ".join(drafted) + ". Not for a release.")
lines += ["",
          "# Shared by every variable the game never sets or reads.",
          "vhint.notUsed = " + NOT_USED, "",
          "# After a hint worked out with one step the game's files do not show outright.",
          "vhint.mark.inferred = (inferred)", "",
          "# After a hint the research could not follow to the end.",
          "vhint.mark.unconfirmed = (unconfirmed)", ""]
not_used, uncertain = [], []
for n, l, t in catalog:
    r = rows[(l, n)]
    if r["hint"] == NOT_USED:
        not_used.append((l, n))
        continue
    if r["confidence"] == "likely":
        uncertain.append((l, n, "l"))
    elif r["confidence"] == "unsure":
        uncertain.append((l, n, "u"))
    lines += ["# %s[%s]" % (l, n), "%s = %s" % (key_of[(l, n)], r["hint"].strip()), ""]

with open(en_path, encoding="utf-8") as f:
    text = f.read()
start = text.find("\n" + HEADING + "\n")
if start >= 0:
    nxt = re.search(r"\n\[[^\]\n]+\]\n", text[start + len(HEADING) + 2:])
    end = start + len(HEADING) + 2 + nxt.start() if nxt else len(text)
    text = text[:start] + "\n" + "\n".join(lines).rstrip("\n") + "\n" + text[end:]
else:
    text = text.rstrip("\n") + "\n\n" + "\n".join(lines).rstrip("\n") + "\n"
with open(en_path, "w", encoding="utf-8", newline="\n") as f:
    f.write(text)


def cs(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


out = ["// Written by Tools/Research/Variables/ship_hints.py from the settled research. Do not edit by hand:",
       "// change the research and run the script again.",
       "using System.Collections.Generic;", "",
       "namespace SMSModForge.Model;", "",
       "public static partial class VanillaVariableHints", "{",
       "    // Exact names, capitals and all: the game tells them apart (VanillaGameVariables.AllNames).", "",
       "    // English on purpose: every string below is one of the game's own variable names, never words shown to anybody.",
       "    /// <summary>The variables the game never sets or reads (1.8E), as List[name].</summary>",
       "    private static readonly HashSet<string> NotUsed = new(System.StringComparer.Ordinal)",
       "    {"]
out += ["        %s," % cs("%s[%s]" % k) for k in not_used]
out += ["    };", "",
        "    // English on purpose: the game's own variable names, never words shown to anybody.",
        "    /// <summary>The hints that are not certain: 'l' worked out with a step inferred, 'u' not followed",
        "    /// to the end. Every other hint is what the game's files say outright.</summary>",
        "    private static readonly Dictionary<string, char> Uncertain = new(System.StringComparer.Ordinal)",
        "    {"]
out += ["        [%s] = '%s'," % (cs("%s[%s]" % (l, n)), c) for l, n, c in uncertain]
out += ["    };", "",
        "    // English on purpose: the game's own variable names, never words shown to anybody.",
        "    /// <summary>A text's key ignores case and the game's names do not: the second variable of a",
        "    /// pair that differs only in capitals has a numbered key of its own.</summary>",
        "    private static readonly Dictionary<string, string> Renamed = new(System.StringComparer.Ordinal)",
        "    {"]
out += ["        [%s] = %s," % (cs("%s[%s]" % (l, n)), cs(k)) for l, n, k in renamed]
out += ["    };", "}", ""]
with open(data_path, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out))

print("written: %d hints (%d own texts, %d not used; %d inferred, %d unconfirmed)%s" % (
    len(catalog), len(catalog) - len(not_used), len(not_used),
    sum(1 for u in uncertain if u[2] == "l"), sum(1 for u in uncertain if u[2] == "u"),
    "; DRAFT for " + ", ".join(drafted) if drafted else ""))
