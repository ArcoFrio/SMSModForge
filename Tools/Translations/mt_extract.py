"""Split the English file into chunks to translate: key = text, with the
useful note above each, ~16k characters of text per chunk."""
import os
import re

EN = r"C:\Users\gabri\source\repos\SMSBustForge\SMSModForge\Languages\en.txt"
OUT = r"C:\Users\gabri\AppData\Local\Temp\claude\C--Users-gabri-source-repos\ce293352-32f5-4a51-b18e-65a958229bd0\scratchpad\mt\src"
LIMIT = 16000

GENERIC = {"Text", "Docs: a point in the topic", "Docs: a heading inside the topic",
           "Docs: the bold label in front of the next text, usually a name on screen",
           "Docs: the line under the topic's title", "Docs: a topic's title"}


def entries():
    lines = open(EN, encoding="utf-8-sig").read().replace("\r\n", "\n").split("\n")
    notes, heading, started = [], "", False
    for line in lines:
        t = line.strip()
        if not t:
            notes = []
            continue
        if t.startswith("#"):
            notes.append(t[1:].strip())
            continue
        if t.startswith("[") and t.endswith("]"):
            heading, started, notes = t[1:-1], True, []
            continue
        m = re.match(r"^([\w.\-]+) = ?(.*)$", t)
        if m:
            yield heading, [n for n in notes if n not in GENERIC and not n.startswith("en:")], m.group(1), m.group(2)
            notes = []


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        os.remove(os.path.join(OUT, f))
    chunk, size, n, heading_now = [], 0, 1, None
    total = 0
    for heading, notes, key, text in entries():
        if key.startswith("language."):
            continue
        piece = []
        if heading != heading_now:
            piece.append("")
            piece.append("[" + heading + "]")
            heading_now = heading
        for note in notes:
            piece.append("# " + note)
        piece.append(key + " = " + text)
        chunk.extend(piece)
        size += len(text)
        total += 1
        if size >= LIMIT:
            write(n, chunk)
            n, chunk, size, heading_now = n + 1, [], 0, None
    if chunk:
        write(n, chunk)
    print(n, "chunks,", total, "texts")


def write(n, chunk):
    with open(os.path.join(OUT, "%02d.txt" % n), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(chunk).strip("\n") + "\n")


main()
