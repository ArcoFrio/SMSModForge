"""Adds each level light's target sorting layers to the vanilla level catalog (1.9.2).
URP's 2D lights light only the sorting layers they name, and the level extractor does not write which: the place
preview lit the PC's room with its lamp of intensity 25, which lights the Foreground and Photos layers and not the
room's (Default), and drew the room white. This reads each Light2D's m_ApplyToSortingLayers from the game's exported
scene (AssetRipper's YAML: a hex string, eight digits a layer id, little-endian, with "*" and "+" where the export
writes "a" and "b") and names the layers from the project's TagManager.
    python Tools/Scenes/level-light-layers.py SMSModForge/Resources "<extract>/Assets"
Into both catalogs, as "m_ApplyToSortingLayers": ["Default", ...] at the start of each Light2D's params, matched by the
node's path (in order, where two siblings share a name). Run again, it rewrites the same. Keeps the line endings.
"""
from __future__ import annotations
import json
import re
import sys
from collections import defaultdict
from pathlib import Path


def main(resources: Path, assets: Path) -> int:
    scene = (assets / "Scenes" / "CoreGameScene.unity").read_text(encoding="utf-8", errors="replace")
    docs = {}
    for d in re.split(r"\n(?=--- !u!)", scene):
        m = re.match(r"--- !u!(\d+) &(-?\d+)", d)
        if m:
            docs[m.group(2)] = (m.group(1), d)
    names = {0: "Default"}
    tags = (assets.parent / "ProjectSettings" / "TagManager.asset").read_text(encoding="utf-8", errors="replace")
    for name, uid in re.findall(r"- name: (.*)\n\s+uniqueID: (\d+)", tags):
        names[int(uid)] = name.strip()

    def field(d, k):
        m = re.search(r"\n\s*" + re.escape(k) + r": (.*)", d)
        return m.group(1).strip() if m else None

    def go_of(d):
        m = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", d)
        return m.group(1) if m else None

    def parent(g):
        for c in re.findall(r"component: \{fileID: (-?\d+)\}", docs[g][1]) if g in docs else []:
            k, d = docs.get(c, ("", ""))
            if k in ("4", "224"):
                m = re.search(r"m_Father: \{fileID: (-?\d+)\}", d)
                return go_of(docs[m.group(1)][1]) if m and m.group(1) in docs else None
        return None

    def path(g):
        out = []
        while g:
            out.append(field(docs[g][1], "m_Name"))
            g = parent(g)
        return "/".join(reversed(out))

    layers_at: dict[str, list[list[str]]] = defaultdict(list)
    for k, d in docs.values():
        if k != "114" or "m_ApplyToSortingLayers" not in d or "m_LightType" not in d:
            continue
        p = path(go_of(d))
        if not p.startswith("5_Levels/"):
            continue
        raw = (field(d, "m_ApplyToSortingLayers") or "").replace("*", "a").replace("+", "b")
        ids = [int.from_bytes(bytes.fromhex(raw[i:i + 8]), "little") for i in range(0, len(raw) - 7, 8)]
        layers_at[p[len("5_Levels/"):]].append([names.get(i, str(i)) for i in ids])
    # The scene lists objects in file order, not hierarchy order; siblings of one name keep their file order.

    added = 0
    for catalog in (resources / "VanillaLevelArt" / "vanilla_levels.json",
                    resources / "VanillaArtThumbs" / "VanillaLevelArt" / "vanilla_levels.json"):
        raw = catalog.read_bytes()
        eol = "\r\n" if b"\r\n" in raw else "\n"
        lines = raw.decode("utf-8").replace("\r\n", "\n").split("\n")
        used: dict[str, int] = defaultdict(int)
        node_path = None
        added = 0
        for n, line in enumerate(lines):
            m = re.match(r'\s*"path": "((?:[^"\\]|\\.)*)",', line)
            if m:
                node_path = json.loads('"' + m.group(1) + '"')
                continue
            if '"type": "Light2D", "params": {' not in line or node_path is None:
                continue
            found = layers_at.get(node_path, [])
            k = used[node_path]
            used[node_path] += 1
            if k >= len(found):
                print(f"{catalog.name}: no light in the scene at {node_path}")
                continue
            line = re.sub(r'"m_ApplyToSortingLayers": \[[^\]]*\], ', "", line)
            lines[n] = line.replace('"type": "Light2D", "params": {', '"type": "Light2D", "params": {"m_ApplyToSortingLayers": '
                                    + json.dumps(found[k]) + ", ", 1)
            added += 1
        catalog.write_bytes("\n".join(lines).replace("\n", eol).encode("utf-8"))
    print(f"lights given their layers: {added} (each catalog)")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(Path(sys.argv[1]), Path(sys.argv[2])))
