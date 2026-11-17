"""Writes the fixtures GameBustShaderTests holds the editor's bust shader port (Rendering/JiggleShader) to: the game's
own compiled pixel program (Sprites/JiggleSprite_FakeGI_2DLit, Universal2D pass, the variant without 2D lights),
run by dxbc_interp.py over synthetic art and masks.

    python extract.py "<StarmakerModForgeTest>\\Starmaker Story_Data" sharedassets1.assets 4479 out
    python make_bust_fixtures.py out\\Sprites_JiggleSprite_FakeGI_2DLit\\p0_ps_136.asm

(the test copy's folder is StarmakerModForgeTest in %USERPROFILE%\\.sms\\game.props). Writes
SMSModForge.Tests/Fixtures/GameBustShader/displacement.bin. Re-run after a game update; if the program changed,
the tests say so.

File layout, little-endian: b"GBS1", int32 srcW, srcH, outW, outH, cases; srcW*srcH*4 bytes art (BGRA, straight,
rows top-down), the same for the mask; then per case 7 float32 (time, speed, strength, frequency, noiseScale,
noiseSpeed, noiseStrength), outW*outH*2 float32 (the displaced uv the program reads the art at, u then v, v up), and
outW*outH*4 bytes (the colour it writes, premultiplied BGRA, rounded).
"""
import io
import os
import struct
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from dxbc_interp import Program  # noqa: E402

asm_path = sys.argv[1]
here = os.path.dirname(os.path.abspath(__file__))
out_path = os.path.join(here, '..', '..', 'SMSModForge.Tests', 'Fixtures', 'GameBustShader', 'displacement.bin')

SRC, OUT = 64, 128

# Art: a checker of 4-texel squares in two colours, a gradient across it, fully opaque in the middle, a
# transparent border two texels wide (the game's busts have empty edges; clamping then reads empty texels too).
ys, xs = np.mgrid[0:SRC, 0:SRC]
art = np.zeros((SRC, SRC, 4), np.uint8)
check = ((xs // 4 + ys // 4) % 2).astype(bool)
art[..., 2] = np.where(check, 200, 40) + (xs * 0.5).astype(np.uint8)       # R
art[..., 1] = np.where(check, 60, 180)                                      # G
art[..., 0] = (ys * 3).astype(np.uint8)                                     # B
art[..., 3] = 255
art[:2, :, 3] = art[-2:, :, 3] = 0
art[:, :2, 3] = art[:, -2:, 3] = 0

# Mask: R bounce rising left to right, G wave rising top to bottom, B noise in a disc (and below the 0.01 gate in a
# corner), A full except a soft band and an empty strip; one quarter all zero.
mask = np.zeros((SRC, SRC, 4), np.uint8)
mask[..., 2] = np.clip(xs * 4, 0, 255)
mask[..., 1] = np.clip(ys * 4, 0, 255)
disc = (xs - 32) ** 2 + (ys - 30) ** 2 < 22 ** 2
mask[..., 0] = np.where(disc, 255, 0)
mask[50:, 50:, 0] = 2                                                       # below 0.01
mask[..., 3] = 255
mask[:, 40:48, 3] = 128
mask[:, 56:60, 3] = 0
mask[:16, :16] = 0

CASES = [
    # time, speed, strength, frequency, noiseScale, noiseSpeed, noiseStrength
    (0.37, 3.0, -0.02, 4.0, 5.0, 0.5, 0.06),
    (1.9, 3.0, -0.02, 4.0, 5.0, 0.5, 0.06),
    (7.3, 4.0, 0.02, 2.0, 12.0, 0.5, 0.06),
    (123.4, 3.0, -0.015, 6.0, 20.0, 0.3, 0.03),
    (2.25, 1.0, 0.05, 10.0, 8.0, 1.0, 0.1),
]

prog = Program.load(asm_path)
# The same program, stopped just before it reads the art: o0.xy = the displaced uv.
stop = next(i for i, (op, ops) in enumerate(prog.instrs) if op.startswith('sample') and ops[2].strip().startswith('t0'))
uv_prog = Program(prog.instrs[:stop] + [('mov', ['o0.xy', 'r0.xyxx']), ('ret', [])])

oy, ox = np.mgrid[0:OUT, 0:OUT]
u = ((ox + 0.5) / OUT).ravel().astype(np.float32)
v = (1 - (oy + 0.5) / OUT).ravel().astype(np.float32)
n = u.size
inputs = {'v1': np.ones((4, n), np.float32),
          'v2': np.stack([u, v, u, v]),
          'v3': np.stack([u, v, np.zeros(n, np.float32), np.ones(n, np.float32)])}
art_f = art.astype(np.float32) / 255
mask_f = mask.astype(np.float32) / 255
# The interpreter's images are RGBA; ours are stored BGRA.
art_rgba = art_f[..., [2, 1, 0, 3]]
mask_rgba = mask_f[..., [2, 1, 0, 3]]
screen = np.zeros((4, 4, 4), np.float32)

blob = io.BytesIO()
blob.write(b'GBS1')
blob.write(struct.pack('<5i', SRC, SRC, OUT, OUT, len(CASES)))
blob.write(art.tobytes())
blob.write(mask.tobytes())
for case in CASES:
    t, speed, strength, freq, scale, nspeed, nstrength = case
    cb0 = np.zeros((152, 4), np.float32)
    cb0[15] = [t / 20, t, t * 2, t * 3]
    cb0[23] = [OUT, OUT, 1 + 1 / OUT, 1 + 1 / OUT]
    cb1 = np.zeros((5, 4), np.float32)
    cb1[0] = [1, 1, 0, 0]
    cb1[1] = [1, 1, 1, 1]
    cb1[2] = [1, 1, 1, 1]
    cb1[3] = [speed, strength, freq, scale]
    cb1[4] = [nspeed, nstrength, 0, 0]
    tex = {0: (art_rgba, 'point'), 1: (mask_rgba, 'point'), 2: (screen, 'bilinear')}
    uvs = uv_prog.run(inputs, {0: cb0, 1: cb1}, tex)['o0'][:2]
    res = prog.run(inputs, {0: cb0, 1: cb1}, tex)
    rgba = np.where(res['alive'][None, :], res['o0'], 0)
    bgra = np.clip(np.floor(rgba[[2, 1, 0, 3]].T * 255 + 0.5), 0, 255).astype(np.uint8)
    blob.write(struct.pack('<7f', *case))
    blob.write(np.stack([uvs[0], uvs[1]], axis=1).astype('<f4').tobytes())
    blob.write(bgra.tobytes())
    moved = float(np.abs(uvs - np.stack([u, v])).max())
    print(f'case t={t}: largest move {moved:.4f} uv, {int(res["alive"].sum())} pixels drawn')

os.makedirs(os.path.dirname(out_path), exist_ok=True)
open(out_path, 'wb').write(blob.getvalue())
print('wrote', os.path.normpath(out_path), len(blob.getvalue()), 'bytes')
