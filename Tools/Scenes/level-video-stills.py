"""Adds each level's video screen to the vanilla level catalog, with a still of its video (1.9.2).
Two of the game's levels show a video behind their art - the Inside Car's sunset through its windows, the Japanese
Subway Train's tunnel - and the place preview drew them black. Neither the level extractor nor its art has them: a
VideoPlayer renders its clip into a render texture ("Videoplayer"), and a camera-space canvas's Image, whose material
samples that texture, shows it behind the level. This reads both from the game's exported project (AssetRipper's
scene YAML, its materials, its VideoClip folder), and takes one frame of each clip with ffmpeg: the frame nearest the
clip's middle brightness (sampled every half second), so a tunnel's dark stretch is not what the preview shows.
    python Tools/Scenes/level-video-stills.py SMSModForge/Resources "<extract>/Assets"
Writes, for each level whose VideoPlayer feeds such an Image:
  VanillaLevelArt/<level>/Video.PNG                        the still at the clip's own size (the export's full art)
  VanillaArtThumbs/VanillaLevelArt/<level>/Video.PNG       the same at 480 px across (what the editor ships)
  "videoScreen" in both catalogs, on the line after the level's "activeSelf": the still's file, the clip, the Image's
  centre (anchoredPosition, canvas units, y up), its size (sizeDelta) and scale, the canvas's sorting layer and order,
  and the canvas scaler's reference resolution. Run again, it rewrites the same.
Needs ffmpeg on the PATH. Keeps the catalogs' line endings.
"""
from __future__ import annotations
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image
import numpy as np

THUMB_WIDTH = 480


def docs_of(scene_text: str) -> dict[str, tuple[str, str]]:
    out = {}
    for d in re.split(r"\n(?=--- !u!)", scene_text):
        m = re.match(r"--- !u!(\d+) &(-?\d+)", d)
        if m:
            out[m.group(2)] = (m.group(1), d)
    return out


def field(doc: str, name: str) -> str | None:
    m = re.search(r"\n\s*" + re.escape(name) + r": (.*)", doc)
    return m.group(1).strip() if m else None


def vec(text: str | None) -> tuple[float, float]:
    m = re.search(r"x: ([-\d.eE]+), y: ([-\d.eE]+)", text or "")
    return (float(m.group(1)), float(m.group(2))) if m else (0.0, 0.0)


def guid_of(meta: Path) -> str:
    return re.search(r"guid: ([0-9a-f]+)", meta.read_text(encoding="utf-8")).group(1)


def middle_frame(clip: Path, out: Path) -> None:
    """The frame nearest the clip's middle brightness, sampled every half second, written to out."""
    duration = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(clip)],
                                    capture_output=True, text=True, check=True).stdout.strip())
    with tempfile.TemporaryDirectory() as tmp:
        frames = []
        t = 0.25
        while t < duration - 0.1:
            f = Path(tmp) / f"f{len(frames):03d}.png"
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t:.2f}", "-i", str(clip), "-frames:v", "1", str(f)], check=True)
            if f.is_file():
                frames.append((float(np.asarray(Image.open(f).convert("L")).mean()), f))
            t += 0.5
        middle = sorted(m for m, _ in frames)[len(frames) // 2]
        best = min(frames, key=lambda mf: abs(mf[0] - middle))[1]
        Image.open(best).convert("RGB").save(out)


def main(resources: Path, assets: Path) -> int:
    catalogs = [resources / "VanillaLevelArt" / "vanilla_levels.json",
                resources / "VanillaArtThumbs" / "VanillaLevelArt" / "vanilla_levels.json"]
    catalog = json.loads(catalogs[0].read_bytes().decode("utf-8-sig"))
    scene = docs_of((assets / "Scenes" / "CoreGameScene.unity").read_text(encoding="utf-8", errors="replace"))

    def go_of(doc):
        m = re.search(r"m_GameObject: \{fileID: (-?\d+)\}", doc)
        return m.group(1) if m else None

    def name_of(goid):
        return field(scene[goid][1], "m_Name") if goid in scene else None

    def components(goid):
        return re.findall(r"component: \{fileID: (-?\d+)\}", scene[goid][1]) if goid in scene else []

    def rect_of(goid):
        for cid in components(goid):
            kind, d = scene.get(cid, ("", ""))
            if kind in ("4", "224"):
                return d
        return ""

    def parent(goid):
        m = re.search(r"m_Father: \{fileID: (-?\d+)\}", rect_of(goid))
        return go_of(scene[m.group(1)][1]) if m and m.group(1) != "0" and m.group(1) in scene else None

    def path_of(goid):
        names = []
        while goid:
            names.append(name_of(goid))
            goid = parent(goid)
        return "/".join(reversed(names))

    screens = {}
    for level in catalog["levels"]:
        def walk(n):
            yield n
            for c in n.get("children", []):
                yield from walk(c)
        for node in walk(level["hierarchy"]):
            for comp in node.get("componentValues", []):
                if comp.get("type") != "VideoPlayer" or comp["params"].get("m_RenderMode") != "Render Texture":
                    continue
                clip_name, texture = comp["params"]["m_VideoClip"], comp["params"]["m_TargetTexture"]
                clip = next(iter((assets / "VideoClip").glob(clip_name + ".*")), None)
                tex_meta = next(iter(assets.glob(f"*/{texture}.renderTexture.meta")), None)
                if clip is None or clip.suffix == ".meta" or tex_meta is None:
                    continue
                tex_guid = guid_of(tex_meta)
                materials = {guid_of(m): m for m in (assets / "Material").glob("*.mat.meta")
                             if tex_guid in m.with_suffix("").read_text(encoding="utf-8", errors="replace")}
                # The level's Image whose material samples the texture.
                for oid, (kind, d) in scene.items():
                    if kind != "114" or not any(g in d for g in materials):
                        continue
                    goid = go_of(d)
                    if not path_of(goid).startswith("5_Levels/" + level["goName"] + "/"):
                        continue
                    r = rect_of(goid)
                    canvas, scaler = None, None
                    g = goid
                    while g and canvas is None:
                        for cid in components(g):
                            k, cd = scene.get(cid, ("", ""))
                            if k == "223":
                                canvas = cd
                            if k == "114" and "m_ReferenceResolution" in cd:
                                scaler = cd
                        g = parent(g) if canvas is None else g
                    layer_id = int(field(canvas, "m_SortingLayerID") or 0) if canvas else 0
                    screens[level["goName"]] = {
                        "still": "Video.PNG", "clip": clip_name,
                        "x": vec(field(r, "m_AnchoredPosition"))[0], "y": vec(field(r, "m_AnchoredPosition"))[1],
                        "width": vec(field(r, "m_SizeDelta"))[0], "height": vec(field(r, "m_SizeDelta"))[1],
                        "scale": vec(field(r, "m_LocalScale"))[0],
                        "sortingLayer": "Default" if layer_id == 0 else str(layer_id),
                        "sortingOrder": int(field(canvas, "m_SortingOrder") or 0) if canvas else 0,
                        "referenceWidth": vec(field(scaler, "m_ReferenceResolution"))[0] if scaler else 1920.0,
                        "referenceHeight": vec(field(scaler, "m_ReferenceResolution"))[1] if scaler else 1080.0,
                    }
                    full = resources / "VanillaLevelArt" / level["goName"] / "Video.PNG"
                    thumb = resources / "VanillaArtThumbs" / "VanillaLevelArt" / level["goName"] / "Video.PNG"
                    full.parent.mkdir(parents=True, exist_ok=True)
                    thumb.parent.mkdir(parents=True, exist_ok=True)
                    middle_frame(clip, full)
                    im = Image.open(full)
                    im.resize((THUMB_WIDTH, round(im.height * THUMB_WIDTH / im.width)), Image.Resampling.LANCZOS).save(thumb)
                    print(f"{level['goName']}: {clip.name} -> {full.name} {im.width}x{im.height}, {screens[level['goName']]}")
                    break

    for path in catalogs:
        raw = path.read_bytes()
        eol = "\r\n" if b"\r\n" in raw else "\n"
        text = raw.decode("utf-8").replace("\r\n", "\n")
        for name, screen in screens.items():
            # The level's entry up to its "activeSelf" line, and a videoScreen line already after it (a second run).
            block =re.compile(r'(\n      "goName": "' + re.escape(name) + r'",\n(?:      "[^"]+": [^\n]*\n)*?      "activeSelf": (?:true|false),)(\n      "videoScreen": [^\n]*)?')
            line = '\n      "videoScreen": ' + json.dumps(screen, separators=(", ", ": ")) + ","
            text, n = block.subn(lambda m: m.group(1) + line, text, count=1)
            if n != 1:
                sys.exit(f"{path}: no entry for {name}")
        path.write_bytes(text.replace("\n", eol).encode("utf-8"))
    print(f"video screens: {len(screens)}, in {len(catalogs)} catalogs")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    sys.exit(main(Path(sys.argv[1]), Path(sys.argv[2])))
