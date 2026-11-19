"""Adds each level sprite's pivot to the vanilla level catalog (1.9.2).

The level extractor (Tools/UnityEditor/SMSModForgeLevelExtractor.cs) writes each SpriteRenderer's sprite, sorting,
colour, ppu and pixel size, and its art as the sprite's rect alone - but not where the sprite is pivoted. 213 of the
512 sprites the game's levels use are not pivoted on their rect's centre, and some far from it: art cut out of a
whole level's canvas keeps that canvas's centre as its pivot (the Beach's water, A14Water: 1.108, 2.387), so the
place preview, drawing every sprite centred on its rect, put the Beach's sea in the middle of its sky. The
extractor writes the pivot now; this puts it into a catalog written before that, from the game's exported project
(AssetRipper's Assets/Sprite/<name>.asset, its m_Pivot), without running the extractor again.

    python Tools/Scenes/level-sprite-pivots.py SMSModForge/Resources/VanillaLevelArt/vanilla_levels.json "<extract>/Assets"

Only the renderers whose sprite is off-centre get a "pivot" (VanillaLevelCatalog reads an absent one as the centre),
written into the same line after "pixelSize", so nothing else in the file moves. Run again, it changes nothing.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

PIVOT = re.compile(r"m_Pivot: \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}")
RENDERER = re.compile(r'("spriteRenderer": \{ "sprite": "((?:[^"\\]|\\.)*)".*?"pixelSize": \[\d+, \d+\])( \})')


def number(v: float) -> str:
    text = f"{v:.4f}".rstrip("0").rstrip(".")
    return text if text not in ("", "-0") else "0"


def main(catalog: Path, assets: Path) -> int:
    sprites = assets / "Sprite"
    pivots: dict[str, tuple[float, float] | None] = {}

    def pivot_of(name: str):
        if name not in pivots:
            f = sprites / f"{name}.asset"
            m = PIVOT.search(f.read_text(encoding="utf-8", errors="replace")) if f.is_file() else None
            pivots[name] = (float(m.group(1)), float(m.group(2))) if m else None
        return pivots[name]

    raw = catalog.read_bytes()
    eol = "\r\n" if b"\r\n" in raw else "\n"           # written back with the file's own line endings
    text = raw.decode("utf-8").replace("\r\n", "\n")
    added = unknown = 0

    def put(m: re.Match) -> str:
        nonlocal added, unknown
        head, name, tail = m.group(1), m.group(2), m.group(3)
        p = pivot_of(name.encode().decode("unicode_escape") if "\\" in name else name)
        if p is None:
            unknown += 1
            return m.group(0)
        if abs(p[0] - 0.5) < 1e-4 and abs(p[1] - 0.5) < 1e-4:
            return m.group(0)
        added += 1
        return f'{head}, "pivot": [{number(p[0])}, {number(p[1])}]{tail}'

    # A line written before (with its pivot already) is left as it is: the pattern needs "pixelSize" right before " }".
    out = RENDERER.sub(put, text)
    if out != text or eol.encode() not in raw:
        catalog.write_bytes(out.replace("\n", eol).encode("utf-8"))
    print(f"pivots added: {added}; sprites not found in the export: {unknown}")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(Path(sys.argv[1]), Path(sys.argv[2])))
