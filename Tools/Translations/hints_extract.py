"""Splits the game-variable hints out of en.txt into chunks for translation (1.9.0).

    python Tools/Translations/hints_extract.py [<out folder>] [<keys per chunk>]

Writes <out>/src/h01.txt, h02.txt ... (default out: Tools/Translations/hints, 200 keys a chunk): the
[The game's variables] section's `key = text` lines, each with the variable it describes as a note above,
so a translator sees what the hint is about. A translated chunk goes to <out>/<code>/hNN.txt as plain
`key = text` lines; mt_check.py checks it against src (pass the src folder), and the refresh step
(README) brings <out>/<code>/*.txt into the shipped file.
"""
import io, os, re, sys

here = os.path.dirname(os.path.abspath(__file__))
en = os.path.join(here, "..", "..", "SMSModForge", "Languages", "en.txt")
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(here, "hints")
per = int(sys.argv[2]) if len(sys.argv) > 2 else 200

text = io.open(en, encoding="utf-8").read()
start = text.find("\n[The game's variables]\n")
if start < 0:
    sys.exit("no [The game's variables] section in en.txt: run ship_hints.py first")
section = text[start + 1:]
nxt = re.search(r"\n\[[^\]\n]+\]\n", section[1:])
if nxt:
    section = section[:nxt.start() + 1]

entries, note = [], None
for line in section.splitlines():
    if line.startswith("# ") and re.match(r"# [^\[]+\[[^\]]+\]$", line):
        note = line
    elif re.match(r"^vhint\.[A-Za-z0-9._-]+ = ", line):
        entries.append((note, line))
        note = None

os.makedirs(os.path.join(out, "src"), exist_ok=True)
for i in range(0, len(entries), per):
    path = os.path.join(out, "src", "h%02d.txt" % (i // per + 1))
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        for n, line in entries[i:i + per]:
            if n:
                f.write(n + "\n")
            f.write(line + "\n")
print("%d hints in %d chunks under %s" % (len(entries), (len(entries) + per - 1) // per, os.path.join(out, "src")))
