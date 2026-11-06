"""Brings the editor's copy of the game's UI art up to what the game really draws, for the UI preview.

Run after a game update, with what the Debug bridge wrote into its shots folder, by hand through bridge.ps1:
"font-atlas *", "font-spacing", "selectable-tints", "sprite-dump" with sprites-wanted.txt beside the results:

    python Tools/Game/preview-data-from-game.py <shots folder> [--measure]

  Fonts     every font whose material draws from a coloured texture (a "Custom Atlas"): the font asset's own atlas
            holds only plain letters, and the outline and shadow are in the material's. colour-<font>.png replaces it.
            Each font's own letter spacing, plain and bold, from "font-spacing", goes in its table.
  Sprites   the shipped PNGs are reduced to keep the download small, which bands soft UI art. A sprite that costs
            little in full colour (--keep-under KB) ships in full colour at its real size; the rest stay as they are.
            Each sprite's real size and padding go in the index, because the game insets a trimmed picture by it.
  Tints     a Selectable multiplies its picture by its resting colour, which the picture's own colour does not say.
            Written on the object in its surface file as "restingTint".

Touches only Resources/VanillaOverlays. Measures and changes nothing else.
"""
import argparse
import collections
import glob
import io
import json
import os

from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OVERLAYS = os.path.join(REPO, "SMSModForge", "Resources", "VanillaOverlays")


def num(value):
    return float(value.replace(",", "."))


def load_json(path):
    return json.load(open(path, encoding="utf-8-sig"))


def save_json(path, data):
    # As the extractor writes them: one line, no spaces, no BOM.
    with open(path, "w", encoding="utf-8", newline="") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))


def fonts(shots):
    folder = os.path.join(OVERLAYS, "Fonts")
    for path in sorted(glob.glob(os.path.join(shots, "colour-*.png"))):
        name = os.path.basename(path)[len("colour-"):-4]
        table = load_json(os.path.join(folder, name + ".json"))
        want = (table["atlas"]["width"], table["atlas"]["height"])
        picture = Image.open(path).convert("RGBA")
        if picture.size != want:
            # Imported smaller than the glyph table's coordinates say. Premultiplied
            # while scaling, so its edges do not go dark.
            picture = picture.convert("RGBa").resize(want, Image.BICUBIC).convert("RGBA")
        out = os.path.join(folder, table["atlas"]["pages"][0])
        picture.save(out, optimize=True)
        print(f"font {name}: {picture.size}, {os.path.getsize(out) // 1024} KB")


def spacing(shots):
    folder = os.path.join(OVERLAYS, "Fonts")
    changed = 0
    for line in open(os.path.join(shots, "font-spacing.tsv"), encoding="utf-8"):
        name, normal, bold = line.rstrip("\n").split("\t")
        path = os.path.join(folder, name + ".json")
        # TextMeshPro's defaults are the editor's too, so most fonts need nothing.
        if not os.path.exists(path) or (num(normal) == 0 and num(bold) == 7):
            continue
        table = load_json(path)
        table["normalSpacing"] = num(normal)
        table["boldSpacing"] = num(bold)
        save_json(path, table)
        changed += 1
    print(f"font spacing: {changed} fonts")


def sprites(shots, keep_under_kb, measure):
    folder = os.path.join(OVERLAYS, "Sprites")
    index_path = os.path.join(folder, "index.json")
    rows = [l.rstrip("\n").split("\t") for l in open(os.path.join(shots, "sprites.tsv"), encoding="utf-8")
            if not l.startswith("#")]
    by_name = collections.defaultdict(list)
    for row in rows:
        by_name[row[0]].append(row)

    index = load_json(index_path)
    before = after = upgraded = unmatched = padded = 0
    for entry in index:
        rect = entry.get("textureRect", [0, 0, 0, 0])
        found = next((r for r in by_name.get(entry["sprite"], [])
                      if all(abs(num(r[3 + i]) - rect[i]) < 1 for i in range(4))), None)
        if found is None and len(by_name.get(entry["sprite"], [])) == 1:
            found = by_name[entry["sprite"]][0]

        path = os.path.join(folder, entry["file"])
        size = os.path.getsize(path)
        before += size
        if found is None:
            unmatched += 1
            after += size
            continue

        entry["rect"] = [num(found[7]), num(found[8])]
        entry["padding"] = [num(v) for v in found[9:13]]
        if any(v > 0.5 for v in entry["padding"]):
            padded += 1

        picture = Image.open(os.path.join(shots, "sprites", found[2])).convert("RGBA")
        packed = io.BytesIO()
        picture.save(packed, "PNG", optimize=True)
        if packed.tell() <= keep_under_kb * 1024:
            upgraded += 1
            after += packed.tell()
            if not measure:
                with open(path, "wb") as f:
                    f.write(packed.getvalue())
        else:
            after += size

    print(f"sprites: {len(index)}, {upgraded} in full colour, {padded} with padding, {unmatched} not found in the game")
    print(f"sprites on disk: {before // 1024} KB -> {after // 1024} KB")
    if not measure:
        save_json(index_path, index)


def tints(shots):
    folder = os.path.join(OVERLAYS, "Surfaces")
    surfaces = {}
    for path in glob.glob(os.path.join(folder, "*.json")):
        data = load_json(path)
        if isinstance(data, dict) and data.get("root") and data.get("path"):
            surfaces[data["path"]] = (path, data)

    placed = lost = 0
    touched = set()
    for line in open(os.path.join(shots, "selectable-tints.txt"), encoding="utf-8"):
        full, down, colour = line.rstrip("\n").split("\t")
        names = full.split("/")
        indices = down.split("/")
        # The longest surface path that starts this object's.
        surface = None
        for n in range(len(names), 0, -1):
            if "/".join(names[:n]) in surfaces:
                surface = "/".join(names[:n])
                start = n
                break
        if surface is None:
            lost += 1
            continue

        node = surfaces[surface][1]["root"]
        ok = True
        for name, sibling in zip(names[start:], indices[start:]):
            same = [c for c in node.get("children", []) if c["name"] == name]
            exact = [c for c in same if str(c.get("siblingIndex")) == sibling]
            if exact:
                node = exact[0]
            elif same:
                node = same[0]
            else:
                ok = False
                break
        if not ok:
            lost += 1      # something a pack made: not in the game's files
            continue
        node["restingTint"] = colour
        touched.add(surface)
        placed += 1

    for surface in touched:
        path, data = surfaces[surface]
        save_json(path, data)
    print(f"tints: {placed} placed on {len(touched)} surfaces, {lost} on objects not in the game's files")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("shots")
    parser.add_argument("--keep-under", type=int, default=24, help="ship a sprite in full colour if that costs at most this many KB")
    parser.add_argument("--measure", action="store_true", help="only say what the sprites would cost")
    args = parser.parse_args()

    if args.measure:
        sprites(args.shots, args.keep_under, True)
        return
    fonts(args.shots)
    spacing(args.shots)
    sprites(args.shots, args.keep_under, False)
    tints(args.shots)


if __name__ == "__main__":
    main()
