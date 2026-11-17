"""Writes the fixture FakeGiTests holds the editor's bust program port to (Rendering/GameBustProgram.g.cs, from
dxbc_to_cs.py) and its fake GI (Rendering/FakeGi): the game's own compiled pixel program for the bust in the player's
room - Sprites/JiggleSprite_FakeGI_2DLit, Universal2D, one 2D light texture, the fake GI on - run by dxbc_interp.py
over synthetic art, mask and background, at the player's room's settings (Resources/Backdrops/MyRoom/backdrop.json).

    python extract.py "<StarmakerModForgeTest>\\Starmaker Story_Data" sharedassets1.assets 4479 out
    python make_gi_fixtures.py out\\Sprites_JiggleSprite_FakeGI_2DLit\\p0_ps_120.asm

(disassembled with hex literals - extract.py does). Writes SMSModForge.Tests/Fixtures/GameBustShader/gi.bin.

The bust is drawn as in the room: 256 texels at two screen pixels each, its left edge at the backdrop's bust left, its
bottom at the rest position plus each case's breathing offset, on a 960x540 screen. Every pixel of the bust's rect is
run; the fixture keeps every other one in x and y (the case's phase: 0 or 1), and of those every one the program
draws and every 16th it discards, to stay small.

File layout, little-endian: b"GGI1", int32 artSize (256), bgW, bgH, screenW, screenH, cases; art (artSize^2 x 4
float32: the linear colour and alpha a texture read gives, rows from the BOTTOM), mask (same), background (bgW x bgH x 4
float32, rows from the bottom); cb0 (138 x 4 float32, the case's time left 0), cb1 (5 x 4); then per case: float32
time, int32 left, bottom, phase, count (of pixels kept); per pixel int32 x, y (screen, from the bottom left) and float32 o0 r, g, b, a,
int32 alive.
"""
import io
import json
import os
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dxbc_interp import Program  # noqa: E402

asm = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.join(here, '..', '..')
out_path = os.path.join(repo, 'SMSModForge.Tests', 'Fixtures', 'GameBustShader', 'gi.bin')
backdrop = json.load(open(os.path.join(repo, 'SMSModForge', 'Resources', 'Backdrops', 'MyRoom', 'backdrop.json')))

S, W, H = 256, backdrop['screen'][0], backdrop['screen'][1]
BW, BH = W // 4, H // 4
rng = np.random.default_rng(1091)


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


# Art: an upright body (an ellipse) whose edge fades over a few texels on one side and is hard on the other, with a
# hole, over colours that change across it; stored as a texture read gives it (linear colour, straight alpha).
yy, xx = np.mgrid[0:S, 0:S].astype(np.float64)          # rows from the bottom
d = np.sqrt(((xx - 128) / 92) ** 2 + ((yy - 112) / 104) ** 2)
soft = np.clip((1 - d) * 104 / np.where(xx < 128, 6.0, 1.0), 0, 1)
hole = np.sqrt((xx - 150) ** 2 + (yy - 150) ** 2) < 9
alpha = np.where(hole, 0.0, soft)
alpha = np.round(alpha * 255) / 255
srgb = np.stack([0.25 + 0.6 * xx / S, 0.35 + 0.4 * np.sin(yy / 17) ** 2, 0.55 - 0.3 * yy / S], -1)
srgb = np.round(np.clip(srgb + rng.uniform(-0.03, 0.03, srgb.shape), 0, 1) * 255) / 255
art = np.concatenate([to_linear(srgb), alpha[..., None]], -1).astype(np.float32)

# Mask: the three channels over the upper half (bounce, wave, noise), full alpha; nothing below.
mask = np.zeros((S, S, 4), np.float32)
up = yy > 120
mask[up, 0] = 0.6; mask[up, 1] = 0.5; mask[up, 2] = 0.7; mask[up, 3] = 1.0

# Background: what a room might be - smooth light, a bright window, stripes - linear, rows from the bottom.
by, bx = np.mgrid[0:BH, 0:BW].astype(np.float64)
bg = np.zeros((BH, BW, 4), np.float32)
bg[..., 0] = 0.15 + 0.6 * np.exp(-((bx - 170) ** 2 + (by - 85) ** 2) / 900)
bg[..., 1] = 0.08 + 0.05 * (np.sin(bx / 5) > 0) + 0.3 * by / BH
bg[..., 2] = 0.25 + 0.2 * np.cos(by / 9) * np.cos(bx / 13)
bg[..., 3] = 1

gi = backdrop['gi']
cb0 = np.zeros((138, 4), np.float32)
cb0[23] = [W, H, 1 + 1 / W, 1 + 1 / H]
cb0[129] = [1, 0, 0, 0]
cb0[134] = [gi['GIEnabled'], gi['GIIntensity'], gi['GIBlurRadius'], gi['GIBlurAmount']]
cb0[135] = [gi['GIEdgeWidth'], gi['GISaturation'], gi['GIBrightness'], 0]
cb0[136] = gi['GITint']
cb0[137] = [gi['RimPower'], gi['RimSoftness'], 1, 0]
cb1 = np.zeros((5, 4), np.float32)
cb1[0] = [1, 1, 0, 0]; cb1[1] = cb1[2] = [1, 1, 1, 1]
cb1[3] = [1.3, 0.012, 9.0, 3.0]
cb1[4] = [0.7, 0.01, 0, 0]

left, rest = backdrop['bust']['left'], backdrop['bust']['restBottom']
prog = Program.load(asm)
# The interpreter's images: rows from the TOP.
tex = {0: (np.ones((4, 4, 4), np.float32), 'bilinear'), 1: (art[::-1].copy(), 'point'), 2: (mask[::-1].copy(), 'point'),
       3: (bg[::-1].copy(), 'bilinear')}

buf = io.BytesIO()
buf.write(b'GGI1')
cases = [(4.25, 0, 0), (19.5, 3, 1)]
buf.write(struct.pack('<6i', S, BW, BH, W, H, len(cases)))
buf.write(art.tobytes()); buf.write(mask.tobytes()); buf.write(bg.tobytes())
buf.write(cb0.tobytes()); buf.write(cb1.tobytes())
for t, bob, phase in cases:
    bottom = rest + bob
    xs, ys = np.meshgrid(np.arange(left + phase, left + 2 * S, 2), np.arange(bottom + phase, bottom + 2 * S, 2))
    xs, ys = xs.ravel(), ys.ravel()
    u = ((xs + 0.5 - left) / (2 * S)).astype(np.float32)
    v = ((ys + 0.5 - bottom) / (2 * S)).astype(np.float32)
    n = u.size
    c0 = cb0.copy(); c0[15] = [t / 20, t, 2 * t, 3 * t]
    inputs = {'v1': np.ones((4, n), np.float32), 'v2': np.stack([u, v, u, v]),
              'v3': np.stack([((xs + 0.5) / W).astype(np.float32), ((ys + 0.5) / H).astype(np.float32),
                              np.zeros(n, np.float32), np.ones(n, np.float32)])}
    res = prog.run(inputs, {0: c0, 1: cb1}, tex)
    o, alive = res['o0'], res['alive']
    keep = alive | (np.arange(n) % 16 == 0)       # every pixel drawn, and some the program discards
    buf.write(struct.pack('<f4i', t, left, bottom, phase, int(keep.sum())))
    rec = np.zeros(int(keep.sum()), dtype=[('x', '<i4'), ('y', '<i4'), ('o', '<f4', 4), ('alive', '<i4')])
    rec['x'], rec['y'], rec['o'], rec['alive'] = xs[keep], ys[keep], o.T[keep], alive[keep].astype(np.int32)
    buf.write(rec.tobytes())
    rim = alive & (np.abs(o[:3] - art[np.clip((v * S).astype(int), 0, S - 1), np.clip((u * S).astype(int), 0, S - 1), :3].T * o[3]).max(0) > 1e-3)
    print('case t=%.2f bob=%d: %d pixels, %d drawn, about %d changed by the GI' % (t, bob, n, alive.sum(), rim.sum()))
open(out_path, 'wb').write(buf.getvalue())
print('wrote', out_path, len(buf.getvalue()), 'bytes')
