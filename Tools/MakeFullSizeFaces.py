#!/usr/bin/env python3
"""
Copy the game's busts' faces at their full size into
`SMSModForge/Resources/VanillaBustFaces/<BustGoName>/` (1.7.0).

The editor ships every vanilla bust's art made smaller
(`Resources/VanillaArtThumbs/VanillaBustArt`), and the preview stretches it
back to the game's 256x256. That is fine for previewing one of the game's own
outfits - every layer went through the same trip, so they agree - and wrong
for an outfit of a pack's own that borrows the game's blink, mouth or faces
("= default"): there the borrowed face sits on the pack's crisp 256x256 bust
and never matches it. These are the same faces at the game's own size, used
for borrowed fields only; the game's own outfits keep the smaller copies.

Faces only - Blink, Mouth1..4 and every Expression<Face> - for exactly the
busts that ship (the folders under VanillaArtThumbs/VanillaBustArt: busts left
out of the catalog on purpose stay out). Each file is the extraction's own
bytes, unchanged; `FullSizeFacesTests` checks that and that none is missing.

Run it again after re-extracting the art:
    python Tools/MakeFullSizeFaces.py
"""

from __future__ import annotations

import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "SMSModForge" / "Resources"
EXTRACTED = ROOT / "VanillaBustArt"
SHIPPED = ROOT / "VanillaArtThumbs" / "VanillaBustArt"
OUT = ROOT / "VanillaBustFaces"

FACE = re.compile(r"^(Blink|Mouth[1-4]|Expression[A-Za-z0-9_]+)\.PNG$")


def main() -> None:
    if OUT.exists():
        shutil.rmtree(OUT)
    busts = files = 0
    for bust in sorted(p for p in SHIPPED.iterdir() if p.is_dir()):
        copied = 0
        for shipped in sorted(bust.iterdir()):
            if not FACE.match(shipped.name):
                continue
            source = EXTRACTED / bust.name / shipped.name
            if not source.is_file():
                continue
            target = OUT / bust.name / shipped.name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            copied += 1
        if copied:
            busts += 1
            files += copied
    print(f"{files} faces of {busts} busts -> {OUT}")


if __name__ == "__main__":
    main()
