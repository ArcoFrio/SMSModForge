"""The FX tab's preview pictures, from the game (1.10.0).

The Debug bridge saves each of the game's effect pictures as the game holds it (png-save, fx-icon, into the test
copy's bridge shots folder); this turns them into what the editor's preview draws and writes them to
SMSModForge/Resources/VanillaFx:

  * The Cartoon FX pictures keep their shape in ONE channel (R_BC4 / R8 - the particle shader reads red as the
    shape), which a picture read plainly shows as a red square. Those become white with that channel as alpha.
  * Pictures that are sheets of frames (smoke x4, debris 3x3, the birds' two wing beats) keep their first frame.

Run it after saving the pictures:
  bridge: png-save <texture name> vfx-<name with _ for anything not a letter or digit>.png  (each one)
          fx-icon Attractor_Canvas/Attractor_Follower vfx-icon-follower.png  (and _Subscriber)
  python -I Tools/Fx/fx-pictures-from-game.py <the bridge's shots folder>
"""
import os, sys
from PIL import Image

SHOTS = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "SMSModForge", "Resources", "VanillaFx")

# Shape in the red channel (the game's formats R_BC4_UNorm and R8_UNorm, read with png-save).
ONE_CHANNEL = {
    "cfxr magic star", "cfxr star", "cfxr card heart", "cfxr bubble 2", "cfxr ember blur", "cfxr stretch rain blur",
    "cfxr flamme plain", "cfxr flamme blur", "cfxr flamme candle", "cfxr ring spikes revert", "cfxr electric spark",
    "cfxr stretch trait",
}
# Sheets: columns, rows - the preview draws the first frame (top left, as the game counts them).
SHEETS = {"cfxr smoke cloud x4": (2, 2), "cfxr debris unlit 3x3": (3, 3), "BirdFlapAnim": (1, 2), "NoireBats": (2, 2)}

NAMES = sorted(ONE_CHANNEL | set(SHEETS) | {
    "Heart", "equip_icon_leaf", "JapaneseFlowerParticle", "MoneyBillParticlepng", "XPparticle", "Icon_Gems_Glow",
    "icon_skull", "Default-ParticleSystem", "fx_glow1", "cfxr ww smoke bright", "fx_circle_line2", "NoireBats",
    "fogcloud1",
})


def slug(name):
    return "".join(c if c.isalnum() else "_" for c in name)


def main():
    os.makedirs(OUT, exist_ok=True)
    made = 0
    for name in NAMES:
        src = os.path.join(SHOTS, "vfx-" + slug(name) + ".png")
        if not os.path.exists(src):
            print("missing:", name)
            continue
        im = Image.open(src).convert("RGBA")
        if name in ONE_CHANNEL:
            r = im.getchannel("R")
            im = Image.merge("RGBA", (Image.new("L", im.size, 255),) * 3 + (r,))
        if name in SHEETS:
            cols, rows = SHEETS[name]
            w, h = im.width // cols, im.height // rows
            im = im.crop((0, 0, w, h))
        im.save(os.path.join(OUT, slug(name) + ".png"))
        made += 1
    for icon in ("follower", "subscriber"):
        src = os.path.join(SHOTS, "vfx-icon-" + icon + ".png")
        if os.path.exists(src):
            Image.open(src).convert("RGBA").save(os.path.join(OUT, "icon_" + icon + ".png"))
            made += 1
    print(made, "pictures in", os.path.normpath(OUT))


main()
