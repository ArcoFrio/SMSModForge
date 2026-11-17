"""Writes the fixture LevelShaderTests holds the editor's level shader port (Rendering/MilkingShader) to: the game's
own compiled Universal2D pixel program for `Universal Render Pipeline/2D/Sprite-Lit-Jiggle` with _JIGGLETYPE_MILKING
and no 2D lights (the variant a pack place's material - a clone of Beach.mat - runs), run by dxbc_interp.py.

    python extract.py "<StarmakerModForgeTest>\\Starmaker Story_Data" sharedassets1.assets 4477 out
    python make_level_fixtures.py out\\Universal_Render_Pipeline_2D_Sprite-Lit-Jiggle\\p0_ps_292.asm

Constants, by the shader's packing order ($Globals): cb0[132] = _JiggleSpeed, _JiggleStrength, _JiggleType;
cb0[133] = _JiggleDirection; cb0[134] = _JiggleFrequency, _JigglePhaseOffset, _JiggleAmplitudeVariation;
cb0[137].y = _MilkingWaveCount; cb0[138] = _MilkingDirection; cb0[139].x = _MilkingCompressionRatio.

File layout, little-endian: b"GLS1", int32 artW, artH, maskW, maskH, outW, outH, cases; art (BGRA, opaque, rows
top-down), mask (BGRA, rows top-down); per case 10 float32 (time, speed, strength, phaseOffset, amplitudeVariation,
waveCount, compressionRatio, milkingDirX, milkingDirY, jiggleType), then outW*outH*4 bytes (BGRA, rows top-down).
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
out_path = os.path.join(here, '..', '..', 'SMSModForge.Tests', 'Fixtures', 'GameBustShader', 'level.bin')

AW, AH, MW, MH, OW, OH = 64, 36, 32, 32, 128, 72
ys, xs = np.mgrid[0:AH, 0:AW]
art = np.zeros((AH, AW, 4), np.uint8)
check = ((xs // 3 + ys // 3) % 2).astype(bool)
art[..., 2] = np.where(check, 220, 30)
art[..., 1] = (xs * 4).clip(0, 255)
art[..., 0] = (ys * 7).clip(0, 255)
art[..., 3] = 255
my, mx = np.mgrid[0:MH, 0:MW]
mask = np.zeros((MH, MW, 4), np.uint8)
mask[..., :3] = 255
mask[..., 3] = (mx * 8).clip(0, 255)          # alpha rising left to right
mask[:, :4, 3] = 0

CASES = [
    # time, speed, strength, phase, ampVar, waveCount, compression, dirX, dirY, jiggleType
    (0.7, 2.0, 0.004, 0.0, 0.0, 3.0, 0.1, 0.0, 1.0, 6.0),     # Beach, as shipped
    (0.7, 2.0, 0.05, 0.0, 0.0, 3.0, 0.1, 0.0, 1.0, 6.0),      # Beach, strong enough to see
    (3.1, 2.0, 0.05, 0.0, 0.0, 3.0, 0.1, 0.0, 1.0, 6.0),
    (1.3, 1.5, 0.04, 0.5, 0.3, 3.41, 1.5, 0.3, 1.0, 6.0),
    (2.2, 2.0, 0.04, 0.0, 0.0, 3.0, 0.1, 1.0, 0.0, 6.0),
]

prog = Program.load(asm_path)
oy, ox = np.mgrid[0:OH, 0:OW]
u = ((ox + 0.5) / OW).ravel().astype(np.float32)
v = (1 - (oy + 0.5) / OH).ravel().astype(np.float32)
n = u.size
inputs = {'v1': np.ones((4, n), np.float32), 'v2': np.stack([u, v, u, v]),
          'v3': np.stack([u, v, np.zeros(n, np.float32), np.ones(n, np.float32)])}
art_rgba = (art.astype(np.float32) / 255)[..., [2, 1, 0, 3]]
mask_rgba = (mask.astype(np.float32) / 255)[..., [2, 1, 0, 3]]

blob = io.BytesIO()
blob.write(b'GLS1')
blob.write(struct.pack('<7i', AW, AH, MW, MH, OW, OH, len(CASES)))
blob.write(art.tobytes())
blob.write(mask.tobytes())
for case in CASES:
    t, speed, strength, phase, var, waves, comp, dx, dy, jtype = case
    cb0 = np.zeros((140, 4), np.float32)
    cb0[15] = [t / 20, t, t * 2, t * 3]
    cb0[132] = [speed, strength, jtype, 0]
    cb0[133] = [0, 1, 0, 0]
    cb0[134] = [10.0, phase, var, 0]
    cb0[137] = [0, waves, 0, 0]
    cb0[138] = [dx, dy, 0, 0]
    cb0[139] = [comp, 0, 0, 0]
    res = prog.run(inputs, {0: cb0}, {0: (art_rgba, 'bilinear'), 1: (mask_rgba, 'point')})
    rgba = np.where(res['alive'][None, :], res['o0'], 0)
    bgra = np.clip(np.floor(rgba[[2, 1, 0, 3]].T * 255 + 0.5), 0, 255).astype(np.uint8)
    blob.write(struct.pack('<10f', *case))
    blob.write(bgra.tobytes())
    print(f'case t={t} strength={strength} dir=({dx},{dy}): {int(res["alive"].sum())} pixels')

open(out_path, 'wb').write(blob.getvalue())
print('wrote', os.path.normpath(out_path), len(blob.getvalue()), 'bytes')
