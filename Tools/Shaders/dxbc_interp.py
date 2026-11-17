"""A reference interpreter for the game's disassembled SM4 pixel programs (the text D3DDisassemble writes).

Runs one program over N pixels at once with numpy, in float32, the way the GPU runs it: registers hold 32-bit
values (comparisons write all-ones bit patterns, `and` works on the bits), branches run under masks, discard
clears the pixel. Textures are sampled at LOD 0 with point or bilinear filtering and clamp addressing.

Dot products are FUSED, as the game's GPU computes them: the first product rounded, each further one added by a
fused multiply-add (one rounding). Measured in the game (2026-11-16, InGameSuiteTests.S1): the bust noise's hash
takes a dot product and magnifies its last bit, and with two roundings per term 154 pixels of one capture read the
wrong texel where with fused ones 1 did. `mad` is left unfused: fusing it changed nothing there.

Coordinates are LOGICAL: texture u right, v up, (0,0) the image's bottom-left; images are given as
(h, w, 4) float arrays with row 0 the TOP, as a PNG reads.

Use:
    prog = Program.load(path)
    out = prog.run(inputs={'v1': (4,N), 'v2': (4,N), 'v3': (4,N)}, cb={0: (k,4), 1: (k,4)},
                   textures={0: (img, 'point'), 1: (img, 'point'), 2: (img, 'bilinear')})
    out['o0'] -> (4, N) float32; out['alive'] -> (N,) bool
"""
import re

import numpy as np

F32 = np.float32
SWZ = 'xyzw'


class Operand:
    __slots__ = ('kind', 'index', 'swz', 'neg', 'abs', 'lit', 'mask')

    def __init__(self, text, is_dest=False):
        self.neg = self.abs = False
        self.lit = None
        self.mask = None
        self.swz = [0, 1, 2, 3]
        t = text.strip()
        if t.startswith('-'):
            self.neg, t = True, t[1:].strip()
        if t.startswith('|') and t.endswith('|'):
            self.abs, t = True, t[1:-1].strip()
        if t == 'null':
            self.kind, self.index = 'null', 0
            return
        if t.startswith('l('):
            vals = [v.strip() for v in t[2:-1].split(',')]
            bits = []
            for v in vals:
                if v.lower().startswith('0x'):
                    bits.append(np.uint32(int(v, 16)))
                else:
                    bits.append(np.array(F32(float(v))).view(np.uint32))
            if len(bits) == 1:
                bits = bits * 4
            self.kind, self.index = 'lit', 0
            self.lit = np.array(bits, dtype=np.uint32).view(F32)
            return
        m = re.fullmatch(r'(cb)(\d+)\[(\d+)\](?:\.([xyzw]+))?', t)
        if m:
            self.kind, self.index = ('cb', (int(m.group(2)), int(m.group(3))))
            sw = m.group(4) or 'xyzw'
        else:
            m = re.fullmatch(r'([rvotsx])(\d+)(?:\.([xyzw]+))?', t)
            if not m:
                raise ValueError('operand: ' + text)
            self.kind, self.index = m.group(1), int(m.group(2))
            sw = m.group(3) or 'xyzw'
        if is_dest:
            self.mask = [SWZ.index(c) for c in sw]
        else:
            if len(sw) == 1:
                sw = sw * 4
            self.swz = [SWZ.index(c) for c in sw]


def split_operands(s):
    out, depth, cur = [], 0, ''
    for ch in s:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch == ',' and depth == 0:
            out.append(cur)
            cur = ''
        else:
            cur += ch
    if cur.strip():
        out.append(cur)
    return out


class Program:
    def __init__(self, instrs):
        self.instrs = instrs

    @staticmethod
    def load(path):
        instrs = []
        started = False
        for line in open(path, encoding='utf-8'):
            line = line.split('//')[0].strip()
            if not line:
                continue
            if re.match(r'(ps|vs)_\d_\d', line):
                started = True
                continue
            if not started or line.startswith('dcl_'):
                continue
            op, _, rest = line.partition(' ')
            ops = split_operands(rest) if rest else []
            instrs.append((op, ops))
        return Program(instrs)

    # ---------------------------------------------------------------- run
    def run(self, inputs, cb, textures, n=None):
        n = n or next(iter(inputs.values())).shape[1]
        regs = {}
        outs = {}
        alive = np.ones(n, dtype=bool)
        stack = []
        active = alive.copy()

        def reg(kind, idx):
            store = outs if kind == 'o' else regs
            key = (kind, idx)
            if key not in store:
                store[key] = np.zeros((4, n), dtype=F32)
            return store[key]

        def read(o):
            if o.kind == 'lit':
                v = np.repeat(o.lit[o.swz][:, None], n, axis=1)
            elif o.kind == 'cb':
                b, i = o.index
                row = cb[b][i].astype(F32)
                v = np.repeat(row[o.swz][:, None], n, axis=1)
            elif o.kind == 'v':
                v = inputs['v%d' % o.index][o.swz]
            else:
                v = reg(o.kind, o.index)[o.swz]
            v = v.astype(F32, copy=True)
            if o.abs:
                v = np.abs(v)
            if o.neg:
                v = -v
            return v

        def write(d, value, sat=False):
            if d.kind == 'null':
                return
            if sat:
                value = np.clip(value, F32(0), F32(1))
            r = reg(d.kind, d.index)
            for c in d.mask:
                r[c] = np.where(active, value[c], r[c])

        def bits(v):
            return v.view(np.uint32)

        def from_bits(b):
            return b.view(F32)

        TRUE = np.uint32(0xFFFFFFFF)
        for op, ops in self.instrs:
            sat = op.endswith('_sat')
            base = op[:-4] if sat else op
            if base == 'ret':
                break
            if base == 'if_nz':
                c = bits(read(Operand(ops[0])))[0] != 0
                stack.append((active.copy(), c))
                active = active & c
                continue
            if base == 'if_z':
                c = bits(read(Operand(ops[0])))[0] == 0
                stack.append((active.copy(), c))
                active = active & c
                continue
            if base == 'else':
                prev, c = stack[-1]
                active = prev & ~c
                continue
            if base == 'endif':
                prev, _ = stack.pop()
                active = prev
                continue
            if base in ('discard_nz', 'discard_z'):
                c = bits(read(Operand(ops[0])))[0] != 0
                if base == 'discard_z':
                    c = ~c
                alive &= ~(c & active)
                continue
            d = Operand(ops[0], is_dest=True)
            if base == 'sincos':
                d2 = Operand(ops[1], is_dest=True)
                a = read(Operand(ops[2]))
                write(d, np.sin(a).astype(F32), sat)
                write(d2, np.cos(a).astype(F32), sat)
                continue
            if base.startswith('sample'):
                coord = read(Operand(ops[1]))
                tex = Operand(ops[2])
                img, mode = textures[tex.index]
                rgba = sample(img, mode, coord[0], coord[1])
                write(d, rgba[tex.swz], sat)
                continue
            srcs = [read(Operand(o)) for o in ops[1:]]
            if base == 'mov':
                v = srcs[0]
            elif base == 'add':
                v = srcs[0] + srcs[1]
            elif base == 'mul':
                v = srcs[0] * srcs[1]
            elif base == 'mad':
                v = srcs[0] * srcs[1] + srcs[2]
            elif base == 'div':
                with np.errstate(divide='ignore', invalid='ignore'):
                    v = srcs[0] / srcs[1]
            elif base in ('dp2', 'dp3', 'dp4'):
                k = int(base[2])
                s = srcs[0][0] * srcs[1][0]
                for i in range(1, k):
                    s = (srcs[0][i].astype(np.float64) * srcs[1][i].astype(np.float64) + s.astype(np.float64)).astype(np.float32)
                v = np.repeat(s[None], 4, axis=0)
            elif base == 'frc':
                v = srcs[0] - np.floor(srcs[0])
            elif base == 'round_ni':
                v = np.floor(srcs[0])
            elif base == 'round_pi':
                v = np.ceil(srcs[0])
            elif base == 'round_z':
                v = np.trunc(srcs[0])
            elif base == 'sqrt':
                with np.errstate(invalid='ignore'):
                    v = np.sqrt(srcs[0])
            elif base == 'rsq':
                with np.errstate(divide='ignore', invalid='ignore'):
                    v = F32(1) / np.sqrt(srcs[0])
            elif base == 'exp':
                v = np.exp2(srcs[0])
            elif base == 'log':
                with np.errstate(divide='ignore', invalid='ignore'):
                    v = np.log2(srcs[0])
            elif base == 'min':
                v = np.minimum(srcs[0], srcs[1])
            elif base == 'max':
                v = np.maximum(srcs[0], srcs[1])
            elif base in ('lt', 'ge', 'eq', 'ne'):
                a, b = srcs
                c = {'lt': a < b, 'ge': a >= b, 'eq': a == b, 'ne': a != b}[base]
                v = from_bits(np.where(c, TRUE, np.uint32(0)).astype(np.uint32))
            elif base == 'and':
                v = from_bits(bits(srcs[0]) & bits(srcs[1]))
            elif base == 'or':
                v = from_bits(bits(srcs[0]) | bits(srcs[1]))
            elif base == 'movc':
                c = bits(srcs[0]) != 0
                v = np.where(c, srcs[1], srcs[2])
            else:
                raise NotImplementedError(op)
            write(d, v.astype(F32), sat)
        return {'o0': outs.get(('o', 0), np.zeros((4, n), dtype=F32)), 'alive': alive}


def sample(img, mode, u, v):
    """img (h, w, 4) row 0 = top; u right, v up; LOD 0. Clamped, or for a mode ending '_wrap' repeated."""
    h, w = img.shape[:2]
    wrap = mode.endswith('_wrap')
    mode = mode[:-5] if wrap else mode

    def fit(i, n):
        return (np.mod(i, n) if wrap else np.clip(i, 0, n - 1)).astype(np.int64)
    if mode == 'point':
        x = fit(np.floor(u * w), w)
        y = fit(np.floor(v * h), h)
        return img[h - 1 - y, x].T.astype(F32)
    # bilinear: texel centres at (i + 0.5) / w
    fx = u * w - 0.5
    fy = v * h - 0.5
    x0 = np.floor(fx)
    y0 = np.floor(fy)
    ax = (fx - x0).astype(F32)
    ay = (fy - y0).astype(F32)
    x0i, x1i = fit(x0, w), fit(x0 + 1, w)
    y0i, y1i = fit(y0, h), fit(y0 + 1, h)

    def at(xi, yi):
        return img[h - 1 - yi, xi]

    c00, c10, c01, c11 = at(x0i, y0i), at(x1i, y0i), at(x0i, y1i), at(x1i, y1i)
    top = c00 * (1 - ax)[:, None] + c10 * ax[:, None]
    bot = c01 * (1 - ax)[:, None] + c11 * ax[:, None]
    return (top * (1 - ay)[:, None] + bot * ay[:, None]).T.astype(F32)
