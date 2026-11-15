#!/usr/bin/env python3
"""The editor's catalogue of the game's scenes (ModForge 1.9.0, extending the game's own scenes):
SMSModForge/Resources/VanillaScenes/index.json and one small JPEG per scene the game can show.

Inputs
    <dump>      what Tools/Scenes/dump-scenes.ps1 read out of the running game: scenes.tsv and one
                PNG per scene's art (at most 512 pixels a side; the game's own art is 256x256).
    <evidence>  a research run's game folder (Tools/Research/Variables collect): targets.tsv (what
                every instruction switches on, conversations and scripts alike) and scripts.txt
                (each scene's own trigger: its signal, and whether it is one of the game's photos).
    <unused>    the Gallery's list of what the game never shows (SMSGallery's
                Package/Data/UnusedContent.base.txt), made for the same game build.

What ships, and what does not
    A scene the game never shows is left out entirely - no picture, no entry - so nothing the game
    does not release ships with the editor. Unused means the Gallery lists it AND it is
    reconfirmed here, independently: nothing in the game ever switches it on (targets.tsv), or a
    person read why it cannot be reached (UNREACHABLE below, with the reason). The two disagreeing
    stops this script: a scene the Gallery calls unused that something switches on, with no reviewed
    reason, needs a person. A scene nothing switches on that the Gallery does not list is left out
    too, and listed.

Pictures
    102 pixels a side for the game's 256x256 art (1/2.5), Lanczos, JPEG quality 82: ArcoFrio chose
    it on 2026-11-15 from a side-by-side of the ways to make them (Lanczos, bicubic, area average;
    PNG and JPEG; 128, 102 and 85 px; put back to 256 px on load as the busts are, or not). The
    Scenes tab draws it pixel by pixel, as it does now, which keeps the game's pixel look. Smaller
    than the busts on purpose: enough to tell scenes apart in a picker, not a copy of the game's
    art. On black, as the game draws them (each scene's Background is a black plate). Nobody needs
    to look at them to make them: the script measures what it writes.

    python Tools/Scenes/make_scene_catalog.py <dump> <evidence> <unused>

Deterministic: the same inputs write the same files. Re-run after a game update.
"""
import collections
import csv
import io
import json
import os
import re
import shutil
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(REPO, "SMSModForge", "Resources", "VanillaScenes")

SIDE = 102
QUALITY = 82

# Shown only behind something no player can reach, read by a person (2026-11-15). Each is switched
# on by a conversation, so the activation scan alone would keep it.
UNREACHABLE = {
    "4_CG_Manager-Photos/B28_VentilatorNaughty (0)":
        "AnnaVentilatorEvent shows it only while Mainstory[M-Stage-3-touching] is true and "
        "Mainstory[M-Stage-2-nude] false; the first is set as Anna's 'We need to talk' scene starts and the "
        "second as it ends, so the two only disagree inside that talk (the Gallery's OnlyInsideATalk).",
    "4_CG_Manager-Photos/B29_VentilatorNaughty (1)":
        "The same conversation and the same two flags as B28.",
}


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    dump, evidence, unused_file = sys.argv[1:]

    rows = [r for r in csv.DictReader(io.open(os.path.join(dump, "scenes.tsv"), encoding="utf-8"), delimiter="\t")
            if "/pack:" not in r["path"]]

    # What switches each scene on, and from where.
    shown_by = collections.defaultdict(list)
    for line in io.open(os.path.join(evidence, "targets.tsv"), encoding="utf-8").read().split("\n")[1:]:
        c = line.split("\t")
        if len(c) >= 5 and re.match(r"Set Active .* to True$", c[2]) and re.match(r"^4_CG_Manager-[^/]+/[^/]+$", c[4]):
            if c[0] not in shown_by[c[4]]:
                shown_by[c[4]].append(c[0])

    # Each scene's own trigger: the signal it raises, and whether it is a photo (PhotoBonus).
    signal, photo = {}, set()
    lines = io.open(os.path.join(evidence, "scripts.txt"), encoding="utf-8").read().split("\n")
    for i, line in enumerate(lines):
        m = re.match(r"^(4_CG_Manager-[^/]+/[^/]+) : Trigger", line)
        if not m:
            continue
        j = i + 1
        while j < len(lines) and lines[j].startswith("  "):
            s = re.match(r"\s*Signal '([^']*)'", lines[j])
            if s:
                signal[m.group(1)] = s.group(1)
            if "Set Active PhotoBonus to True" in lines[j]:
                photo.add(m.group(1))
            j += 1

    gallery = {l[6:].strip() for l in io.open(unused_file, encoding="utf-8-sig") if l.startswith("Scene:")}

    kept, left, problems = [], [], []
    for r in rows:
        path = r["path"]
        name = path.split("/", 1)[1]
        never_on = not shown_by.get(path)
        listed = name in gallery
        if listed and not never_on and path not in UNREACHABLE:
            problems.append(path + ": the Gallery calls it unused, but " + ", ".join(shown_by[path][:3]) + " switch(es) it on")
        if never_on or (listed and path in UNREACHABLE):
            left.append((path, "listed by the Gallery" if listed else "NOT listed by the Gallery",
                         "nothing switches it on" if never_on else UNREACHABLE[path]))
            continue
        kept.append(r)
    if problems:
        for p in problems:
            print("needs a person:", p)
        sys.exit("unused scenes disagree; read them and add a reason to UNREACHABLE, or take them off")

    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    os.makedirs(os.path.join(OUT, "Thumbs"))
    entries, bytes_written, by_art = [], 0, {}
    for r in kept:
        path = r["path"]
        art = r["artSprite"]
        # Scenes sharing one picture share one file.
        if art not in by_art:
            src = os.path.join(dump, r["file"])
            with Image.open(src) as im:
                im = im.convert("RGBA")
                on_black = Image.new("RGBA", im.size, (0, 0, 0, 255))
                on_black.alpha_composite(im)
                k = SIDE / max(im.size)
                small = on_black.convert("RGB").resize((max(1, round(im.size[0] * k)), max(1, round(im.size[1] * k))),
                                                       Image.LANCZOS)
            file = "%d.jpg" % len(by_art)
            target = os.path.join(OUT, "Thumbs", file)
            small.save(target, "JPEG", quality=QUALITY, optimize=True, progressive=False)
            bytes_written += os.path.getsize(target)
            by_art[art] = file
        entries.append({
            "path": path,
            "picture": "Thumbs/" + by_art[art],
            "art": art,
            "artSize": [float(r["artW"]), float(r["artH"])],
            "frame": r["frameSprite"],
            "signal": signal.get(path, ""),
            "photo": path in photo,
            "shownBy": shown_by.get(path, [])[:6],
        })

    index = {
        "version": 1,
        "about": "The game's scenes the editor offers to extend (1.8E). Made by Tools/Scenes/make_scene_catalog.py; "
                 "scenes the game never shows are left out.",
        "scenes": entries,
    }
    io.open(os.path.join(OUT, "index.json"), "w", encoding="utf-8", newline="\n").write(
        json.dumps(index, ensure_ascii=False, indent=1) + "\n")
    # Beside the dump, never in the repository: the names of what the game does not release are
    # spoilers too (the Gallery hides them for the same reason).
    io.open(os.path.join(dump, "left-out.tsv"), "w", encoding="utf-8", newline="\n").write(
        "".join("%s\t%s\t%s\n" % row for row in sorted(left)))

    print("kept %d scenes (%d pictures, %.1f MB), left out %d (%d of them listed by the Gallery)" % (
        len(entries), len(by_art), bytes_written / 1048576.0, len(left), sum(1 for l in left if l[1].startswith("listed"))))


if __name__ == "__main__":
    main()
