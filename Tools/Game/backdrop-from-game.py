"""Makes a bust preview's backdrop - one of the game's places as the game has it behind a bust - from what the in-game
check captured in the test copy: InGameSuiteTests.S4 for the PC's room, S7 for the level backdrops (1.9.1):

    powershell -File Tools/Game/ingame-suite.ps1 -Filter "FullyQualifiedName~.S4_"
    python Tools/Game/backdrop-from-game.py "<test copy>\\BepInEx\\plugins\\SMSModForge\\DebugBridge\\suite\\looks\\backdrop-room"
    powershell -File Tools/Game/ingame-suite.ps1 -Filter "FullyQualifiedName~.S7_"
    python Tools/Game/backdrop-from-game.py "<...looks>\\level-beach-room" Beach

The second argument is the backdrop's folder (MyRoom when left out); the captures beside the room's are found by its
folder's prefix ("backdrop-", "level-beach-"). A level with no lamp the bust's shadow is cast from has no lamp
files and no "lamp" in its backdrop.json: its lights are taken as what they add to a bust (lights.png).

(the test copy's folder is StarmakerModForgeTest in %USERPROFILE%\\.sms\\game.props). Run it after a game update when
S4 fails, then run S4 again: it holds what this wrote to the game.

Writes SMSModForge/Resources/Backdrops/<name>:
  room.png       the screen as drawn (8-bit), without the interface, the room's own buttons, a bust or the
                 post-processing. 960x540, the size the preview's bust is drawn at (two pixels a texel).
  room-hdr.png   the same frame as the camera's target holds it (B10G11R11 floats, up to about 1.6), as half floats
                 in a 16-bit RGB PNG (exact: a half holds every such value); rows from the top.
  background.png the background the bust shader's fake GI reads (_CameraSortingLayerTexture, 240x135), the same way.
  unlit-hdr.png, lamp-<k>-hdr.png  the room with its lamps off, and with each lamp alone (what a bust's shadow
                 takes from), the same way.
  room-noglow-hdr.png  the room with the lamp's glow (its light volume) off, the same way: with unlit-hdr, what pins
                 down the room's own colour, which the lamp's light multiplies when a bust's shadow dims it.
  post-lut.png   URP's internal grading table (1024x32, 8 bits), as the game builds it.
  post.json      what the game's Beautify and URP post passes run with (their materials' values).
  lights.png     what the room's lights add to a bust, in linear light, per 4x4 screen pixels over the bust's rect,
                 16-bit: value / 65535 * lightScale. The game multiplies a bust by 1 (measured, S4), so this is all.
  backdrop.json  the lamp a bust's shadow is cast from (where it is, its colour, falloff, glow and shadow, the light
                 texture scale and the global light: Light2D's and the 2D renderer's own numbers), how this GPU
                 decodes an sRGB lookup's bytes (from URP's falloff lookup read back), the screen, the lights' region,
                 the fake GI's settings (FakeGIManager's globals, live), and where a
                 single bust rests (SpriteManager: the anchor, the bust's height) and how far it breathes.
Touches nothing else.
"""
import json
import os
import re
import struct
import sys

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(REPO, "SMSModForge", "Resources", "Backdrops", "MyRoom")   # the default; main() takes a name
CELL = 4
LIGHT_SCALE = 0.25


def info_of(folder):
    lines = [l.rstrip("\n") for l in open(os.path.join(folder, "info.txt"), encoding="utf-8")]
    return dict(l.split("=", 1) for l in lines if "=" in l)


def num(text):
    return float(text.split(" ")[0].replace(",", "."))


def main(room, name="MyRoom"):
    global OUT
    OUT = os.path.join(REPO, "SMSModForge", "Resources", "Backdrops", name)
    looks = os.path.dirname(room)
    base = os.path.basename(os.path.normpath(room))
    assert base.endswith("room"), "the room's capture folder ends in 'room': " + base
    prefix = base[:-len("room")]
    info = info_of(room)
    w, h = map(int, info["screen"].split("x"))
    os.makedirs(OUT, exist_ok=True)

    # The room, as drawn, and as the camera's target holds it.
    Image.open(os.path.join(room, "screen.png")).convert("RGB").save(os.path.join(OUT, "room.png"), optimize=True)
    write_half_png(os.path.join(OUT, "room-hdr.png"), read_floats(os.path.join(room, "frame.bin")))
    write_half_png(os.path.join(OUT, "background.png"), read_floats(os.path.join(room, "background.bin")))
    write_half_png(os.path.join(OUT, "unlit-hdr.png"), read_floats(os.path.join(looks, prefix + "unlit", "frame.bin")))
    has_lamp = os.path.exists(os.path.join(room, "shadow-light.txt"))
    if has_lamp:
        write_half_png(os.path.join(OUT, "room-noglow-hdr.png"), read_floats(os.path.join(looks, prefix + "noglow", "frame.bin")))
    k = 0
    while os.path.isdir(os.path.join(looks, prefix + "lamp-%d" % k)):
        write_half_png(os.path.join(OUT, "lamp-%d-hdr.png" % k), read_floats(os.path.join(looks, prefix + "lamp-%d" % k, "frame.bin")))
        k += 1
    lut = read_floats(os.path.join(room, "post-lut.bin"))
    Image.fromarray(np.round(lut[::-1, :, :3] * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "post-lut.png"))
    post = {"beautify": material_values(os.path.join(room, "post-beautify.txt")),
            "uberpost": material_values(os.path.join(room, "post-uberpost.txt"))}
    json.dump(post, open(os.path.join(OUT, "post.json"), "w", encoding="utf-8"), indent=2)

    # The lights on a bust: S4's per-pixel multiply and add, from two greys.
    xa, ya, xb, yb = map(int, open(os.path.join(room, "lights.txt")).read().split("=", 1)[1].split()[0].split(","))
    m = np.fromfile(os.path.join(room, "lights.bin"), dtype=np.float32).reshape(yb - ya, xb - xa, 6)
    mul, add = m[..., :3], m[..., 3:]
    # Where something is drawn over the busts (not a light: the multiply is far from 1), leave it out.
    good = (np.abs(mul - 1).max(axis=2) < 0.05)
    rows, cols = (yb - ya + CELL - 1) // CELL, (xb - xa + CELL - 1) // CELL
    grid = np.zeros((rows, cols, 3))
    have = np.zeros((rows, cols), bool)
    for r in range(rows):
        for c in range(cols):
            g = good[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
            if g.sum() >= CELL:
                grid[r, c] = add[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL][g].mean(axis=0)
                have[r, c] = True
    # Cells with nothing to measure take the nearest measured one.
    if not have.all():
        from scipy import ndimage
        idx = ndimage.distance_transform_edt(~have, return_distances=False, return_indices=True)
        grid = grid[idx[0], idx[1]]
    grid = np.clip(grid, 0, LIGHT_SCALE)
    # Rows from the bottom in the measurement; the PNG's rows run from the top.
    png = np.round(grid[::-1] / LIGHT_SCALE * 65535).astype(np.uint16)
    # PIL has no 16-bit RGB mode: written by hand.
    write_png16(os.path.join(OUT, "lights.png"), png)

    # Where a bust rests, and how far it breathes.
    manager = open(os.path.join(room, "bust-manager.txt"), encoding="utf-8").read()
    anchor_y = float(re.search(r"world [-\d.,]*?,(-?[\d.]+),", manager).group(1))
    amplitude = float(re.search(r"breathingAmplitude=([-\d.]+)", manager).group(1))
    speed = float(re.search(r"breathingSpeed=([-\d.]+)", manager).group(1))
    cam = re.search(r"size=([\d.]+) pos=(-?[\d.]+),(-?[\d.]+)", info["camera"])
    size, cam_y = float(cam.group(1)), float(cam.group(3))
    px_per_unit = h / (2 * size)
    grey = info_of(os.path.join(looks, prefix + "grey-dark"))
    corners = [tuple(map(float, p.split(","))) for p in grey["drawnCorners"].split(" ")[0].split(";")]
    bust_w, bust_h = corners[1][0] - corners[0][0], corners[2][1] - corners[0][1]
    centre = h / 2 + (anchor_y - cam_y) * px_per_unit

    gi = {k: num(info["_Global_" + k]) for k in ("GIEnabled", "GIIntensity", "GIBlurRadius", "GIBlurAmount", "GIEdgeWidth",
                                                 "GISaturation", "GIBrightness", "RimPower", "RimSoftness")}
    gi["GITint"] = [float(v) for v in grey["_Global_GITint"].split(",")]
    data = {
        "name": name,
        "lookupDecode": lookup_decode(os.path.join(room, "falloff.bin")),
        "screen": [w, h],
        "lights": {"left": xa, "bottom": ya, "cell": CELL, "columns": cols, "rows": rows, "scale": LIGHT_SCALE},
        "gi": gi,
        "bust": {"left": round(w / 2 - bust_w / 2), "restBottom": round(centre - bust_h / 2), "width": round(bust_w),
                 "height": round(bust_h), "breathingPixels": round(amplitude * px_per_unit, 3), "breathingSpeed": speed},
    }
    if has_lamp:
        data["lamp"] = lamp_of(room)
    json.dump(data, open(os.path.join(OUT, "backdrop.json"), "w", encoding="utf-8"), indent=2)
    print("wrote", OUT, json.dumps(data["bust"]), "lights", cols, "x", rows, "max add", grid.max().round(4))


def read_floats(path):
    """What the bridge's rt-save wrote: int32 w, h, then RGBA float32, rows from the bottom."""
    raw = open(path, "rb").read()
    w, h = struct.unpack("<ii", raw[:8])
    return np.frombuffer(raw[8:], dtype=np.float32).reshape(h, w, 4)


def lamp_of(room):
    """The lamp a bust's shadow is cast from: S4's shadow-light, lights-list, fields and the 2D renderer's data."""
    said = open(os.path.join(room, "shadow-light.txt"), encoding="utf-8").read()
    fields = open(os.path.join(room, "lamp-fields.txt"), encoding="utf-8").read()
    lights = open(os.path.join(room, "lights-list.txt"), encoding="utf-8").read()
    renderer = [l for l in open(os.path.join(room, "renderer2d.txt"), encoding="utf-8") if l.startswith("Renderer2D:")][0]

    def field(name):
        return re.search(r"\b" + name + r"=([^;]+)", fields).group(1).strip()

    def said_num(name):
        return float(re.search(name + r" (-?[\d.]+)", said).group(1))

    x, y = map(float, re.search(r"screen (-?[\d.]+),(-?[\d.]+)", said).groups())
    ppu = said_num("pixelsPerUnit")
    outer, shadow_r = said_num("outerRadius"), said_num("shadowRadius")
    assert abs(outer - shadow_r) < 1e-4, "the lamp's light reaches %s, its shadow %s: GameLamp takes one radius" % (outer, shadow_r)
    me = [l for l in lights.split(" | ") if "/Light 2D " in l and " Point " in l][0]
    colour = [float(v) for v in re.search(r"colour ([\d.,]+)", me).group(1).split(",")]
    intensity = float(re.search(r"intensity ([\d.]+)", me).group(1))
    glob = [l for l in lights.split(" | ") if " Global " in l and "Default" in l][0]
    gcol = [float(v) for v in re.search(r"colour ([\d.,]+)", glob).group(1).split(",")]
    gint = float(re.search(r"intensity ([\d.]+)", glob).group(1))
    volume = float(field("m_LightVolumeIntensity")) if field("m_LightVolumeEnabled") == "True" else 0.0
    shadow_volume = float(field("m_ShadowVolumeIntensity")) if field("m_ShadowVolumeIntensityEnabled") == "True" else 0.0
    return {
        "x": x, "y": y, "radius": round(outer * ppu, 4),
        # Light2D's colour times its intensity and alpha, as RendererLighting hands it to the shader (raw, not converted).
        "colour": [round(c * intensity * colour[3], 7) for c in colour[:3]],
        "falloff": float(field("m_FalloffIntensity")), "volumeOpacity": volume,
        "shadowIntensity": float(field("m_ShadowIntensity")), "shadowVolumeIntensity": shadow_volume,
        "shadowSoftness": float(field("m_ShadowSoftness")), "shadowSoftnessFalloff": float(field("m_ShadowSoftnessFalloffIntensity")),
        "textureScale": float(re.search(r"m_LightRenderTextureScale=([\d.]+)", renderer).group(1)),
        "global": [round(c * gint * gcol[3], 7) for c in gcol[:3]],
    }


def lookup_decode(path):
    """Each byte of an sRGB lookup as this GPU reads it: URP's falloff lookup read back, against the bytes its formula
    (Light2DLookupTexture.CreateFalloffLookupTexture) writes."""
    t = read_floats(path)[..., 0]
    table = {}
    for row in range(t.shape[0]):
        n = np.float32((row + 64) / np.float32(256))
        p = np.float32(np.log(np.float32(1) - n) / np.log(n))
        r = np.power(np.arange(t.shape[1], dtype=np.float32) / np.float32(2048), p).astype(np.float32)
        for b, v in zip(np.round(np.clip(r, 0, 1) * 255).astype(int), t[row]):
            assert table.get(b, v) == v, "byte %d read back as %s and %s" % (b, table[b], v)
            table[b] = float(v)
    assert len(table) == 256, "only %d of the 256 bytes appear" % len(table)
    return [table[b] for b in range(256)]


def write_half_png(path, rgba):
    """RGB as half floats, their bits in a 16-bit RGB PNG; rows from the top."""
    half = rgba[::-1, :, :3].astype(np.float16)
    assert np.array_equal(half.astype(np.float32), rgba[::-1, :, :3]), path + ": a value a half cannot hold"
    write_png16(path, half.view(np.uint16))


def material_values(path):
    """A material-dump's keywords and every float and vector, by name."""
    out = {"keywords": [], "values": {}}
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("material ") and " keywords: " in line and not out["keywords"]:
            out["keywords"] = line.split(" keywords: ", 1)[1].split()
        m = re.match(r"\s+(Float|Vector|Int) (\w+) = (.*)$", line)
        if m:
            out["values"].setdefault(m.group(2), [float(v) for v in m.group(3).split(",")])
    return out


def write_png16(path, rgb16):
    """A 16-bit RGB PNG, rows top-down, big-endian samples as PNG has them."""
    import struct
    import zlib
    h, w = rgb16.shape[:2]
    raw = b"".join(b"\x00" + rgb16[y].astype(">u2").tobytes() for y in range(h))

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 16, 2, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


if __name__ == "__main__":
    main(sys.argv[1], *sys.argv[2:3])
