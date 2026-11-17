"""How the game draws a pack NPC, from what the in-game check InGameSuiteTests.S9 captured in the test copy
(1.9.1). Numbers only - nothing here shows a picture:

    powershell -File Tools/Game/ingame-suite.ps1 -Filter "FullyQualifiedName~.S9_"
    python Tools/Game/npc-look.py "<test copy>\\BepInEx\\plugins\\SMSModForge\\DebugBridge\\suite\\looks"

Reads the HDR frames (post-processing off): npc-hdr (as drawn), npc-nogi (the fake GI off), npc-neutral (the
global light alone), npc-room / npc-room-neutral (the NPC off). Reports:
  - the NPC's renderer, material and the lights on its layer (npc-look/info.txt);
  - the light on the NPC (drawn without the fake GI / drawn under the global light alone) against the light on
    the room at the same pixels (the room / the room under the global light alone): the same when the NPC is lit
    as the room is, by its layer's lights;
  - its edge glow (as drawn - without the fake GI): where, and how strong.
"""
import os
import sys
import struct

import numpy as np


def frame(folder):
    with open(os.path.join(folder, "frame.bin"), "rb") as f:
        w, h = struct.unpack("<ii", f.read(8))
        px = np.frombuffer(f.read(), dtype="<f4").reshape(h, w, 4)
    return px[..., :3].astype(np.float64)   # rows from the bottom, linear light


def main(looks):
    info = open(os.path.join(looks, "npc-look", "info.txt"), encoding="utf-8").read().splitlines()
    for l in info:
        if l.split("=", 1)[0] in ("renderer", "material", "light", "drawnCorners", "spriteRect", "background", "backgroundFlat", "time"):
            print(l[:220])
    hdr, nogi, neutral = frame(os.path.join(looks, "npc-hdr")), frame(os.path.join(looks, "npc-nogi")), frame(os.path.join(looks, "npc-neutral"))
    room, room_neutral = frame(os.path.join(looks, "npc-room")), frame(os.path.join(looks, "npc-room-neutral"))
    h, w, _ = hdr.shape
    drawn = np.abs(neutral - room_neutral).max(axis=2) > 0.002
    print("frame %dx%d; the NPC covers %d pixels (rows %d-%d, columns %d-%d from the bottom left)" % (
        w, h, drawn.sum(), *np.argwhere(drawn)[:, 0][[0, -1]], np.argwhere(drawn)[:, 1].min(), np.argwhere(drawn)[:, 1].max()))
    # The NPC's inside: drawn, and its four neighbours drawn too (two pixels in).
    inside = drawn.copy()
    for _ in range(2):
        inside[1:-1, 1:-1] &= inside[:-2, 1:-1] & inside[2:, 1:-1] & inside[1:-1, :-2] & inside[1:-1, 2:]
    ok = inside & (neutral.min(axis=2) > 0.01) & (room_neutral.min(axis=2) > 0.01)
    light_npc = nogi[ok] / neutral[ok]
    light_room = room[ok] / room_neutral[ok]
    d = np.abs(light_npc - light_room).max(axis=1)
    print("light on the NPC (drawn without the fake GI / under the global light alone), over %d inside pixels:" % ok.sum())
    print("   mean %s, the room's there %s" % (np.round(light_npc.mean(axis=0), 4), np.round(light_room.mean(axis=0), 4)))
    print("   NPC minus room: median %.4f, 95%% %.4f, max %.4f" % (np.median(d), np.percentile(d, 95), d.max()))
    print("   the NPC against 1 (the global light alone): median %.4f" % np.median(np.abs(light_npc - 1).max(axis=1)))
    # The lamp's glow is added over everything after (GameLamp), so the ratios above carry it. With the
    # room as a second surface under the same light and glow: drawn = colour x L + G for both, so
    # L = (NPC - room) / (NPC colour - room colour) where the two colours differ enough.
    dn = neutral - room_neutral
    good = inside[..., None] & (np.abs(dn) > 0.08)
    L = np.where(good, (nogi - room) / np.where(good, dn, 1), np.nan)
    G = np.where(good, room - room_neutral * L, np.nan)
    for c, n in enumerate("RGB"):
        v = L[..., c][~np.isnan(L[..., c])]
        g = G[..., c][~np.isnan(G[..., c])]
        if v.size:
            print("   light %s from NPC against room: %d pixels, median %.4f (5%%-95%% %.4f-%.4f); glow added there: median %.4f" % (
                n, v.size, np.median(v), np.percentile(v, 5), np.percentile(v, 95), np.median(g)))
    rim = (hdr - nogi).max(axis=2)
    on = drawn & (rim > 0.002)
    # The edge glow's colour, the light taken out: what the fake GI read (a flat default texture when
    # nothing behind was copied).
    Lmed = np.array([np.nanmedian(L[..., c]) for c in range(3)])
    rimc = (hdr - nogi)[on] / Lmed
    if on.any():
        print("   edge glow over the light, mean RGB %s, as shares %s" % (np.round(rimc.mean(axis=0), 4), np.round(rimc.mean(axis=0) / rimc.mean(axis=0).sum(), 3)))
    print("edge glow (as drawn minus without the fake GI): %d pixels over 0.002 (of %d drawn, %d inside), max %.4f, mean where on %.4f" % (
        on.sum(), drawn.sum(), inside.sum(), rim.max(), rim[on].mean() if on.any() else 0))
    off_npc = (~drawn) & (np.abs(hdr - nogi).max(axis=2) > 0.002)
    print("   changed off the NPC: %d pixels" % off_npc.sum())


if __name__ == "__main__":
    main(sys.argv[1])
