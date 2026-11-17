"""Turns ranges of one of the game's disassembled SM4 pixel programs into C#, instruction for instruction, the way the
game's GPU runs them - the editor's exact ports, which nothing has to be hand-copied for:

    python dxbc_to_cs.py <program.asm> <out.cs> <namespace> <class> <name>:<first>:<last> [...]

Each <name>:<first>:<last> becomes a method `static void <name><T>(ref GameRegisters r, float[] cb0, float[] cb1,
ref T tex, in GameInputs v) where T : struct, IGameTextures`, for the instructions from the one starting with <first> to
the one starting with <last> (both kept; "before:<text>" ends a range just before that one; matched as text, the first match - for <last>, the first after <first>; the
names "start" and "end" mean the program's first and last). A range must open and close its own if/else blocks. The
registers a range reads before it writes them come in through `r`, and what it writes goes back out - so ranges run one
after another as the whole program would, and a caller can stand in for any range between two others.

As the GPU runs it (dxbc_interp.py does the same; GameBustShaderTests holds them to each other):
  - float32 throughout; `mad` rounds its product (unfused - fusing it changed nothing the game draws), dot products
    take the first product rounded and add the rest with fused multiply-adds (measured in the game, 2026-11-16);
  - comparisons write all-ones bits or zero, `and` and `movc` work on the bits;
  - literals are the program's 32 bits (the disassembly must be written with D3D_DISASM_PRINT_HEX_LITERALS).
"""
import re
import struct
import sys

COMP = 'xyzw'


def split_operands(text):
    out, depth, cur = [], 0, ''
    for ch in text:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch == ',' and depth == 0:
            out.append(cur.strip()); cur = ''
        else:
            cur += ch
    if cur.strip():
        out.append(cur.strip())
    return out


def parse(path):
    instrs = []
    started = False
    for raw in open(path, encoding='latin-1'):
        line = raw.split('//')[0].strip()
        if not line:
            continue
        if line.startswith('ps_'):
            started = True
            continue
        if not started or line.startswith('dcl_'):
            continue
        op, _, rest = line.partition(' ')
        instrs.append((op, split_operands(rest), line))
    return instrs


def f32(bits):
    return struct.unpack('<f', struct.pack('<I', bits & 0xFFFFFFFF))[0]


def lit_text(bits):
    v = f32(bits)
    if v != v or v in (float('inf'), float('-inf')):
        return 'BitConverter.Int32BitsToSingle(unchecked((int)0x%08X))' % (bits & 0xFFFFFFFF)
    for digits in range(1, 12):
        s = '%.*g' % (digits, v)
        if struct.unpack('<I', struct.pack('<f', float(s)))[0] == bits & 0xFFFFFFFF:
            break
    if 'e' not in s and '.' not in s:
        s += '.0'
    return s + 'f'


class Src:
    def __init__(self, text):
        self.neg = text.startswith('-')
        if self.neg:
            text = text[1:].strip()
        self.abs = text.startswith('|') and text.endswith('|')
        if self.abs:
            text = text[1:-1].strip()
        self.text = text
        if text.startswith('l('):
            vals = [x.strip() for x in text[2:-1].split(',')]
            bits = []
            for x in vals:
                if not x.lower().startswith('0x'):
                    raise SystemExit('a decimal literal (%s): disassemble with D3D_DISASM_PRINT_HEX_LITERALS' % text)
                bits.append(int(x, 16))
            if len(bits) == 1:
                bits *= 4
            self.kind, self.lit = 'lit', bits
            self.swz = COMP
            return
        m = re.fullmatch(r'(cb)(\d+)\[(\d+)\](?:\.([xyzw]+))?', text)
        if m:
            self.kind, self.cb, self.index = 'cb', int(m.group(2)), int(m.group(3))
            self.swz = (m.group(4) or COMP)
        else:
            m = re.fullmatch(r'([rvot])(\d+)(?:\.([xyzw]+))?', text)
            if not m:
                m2 = re.fullmatch(r's(\d+)', text)
                if m2:
                    self.kind, self.index, self.swz = 's', int(m2.group(1)), COMP
                    return
                raise SystemExit('operand not understood: ' + text)
            self.kind, self.index = m.group(1), int(m.group(2))
            self.swz = m.group(3) or COMP
        if len(self.swz) == 1:
            self.swz *= 4
        elif len(self.swz) < 4:
            self.swz += self.swz[-1] * (4 - len(self.swz))

    def reg(self, c):
        """The register a component is read from (for live-in analysis), or None."""
        if self.kind == 'r':
            return 'r%d%s' % (self.index, self.swz[c])
        return None

    def comp(self, c):
        k = self.swz[c]
        if self.kind == 'lit':
            e = lit_text(self.lit[c])
        elif self.kind == 'cb':
            e = 'cb%d[%d]' % (self.cb, self.index * 4 + COMP.index(k))
        elif self.kind == 'r':
            e = 'r%d%s' % (self.index, k)
        elif self.kind == 'v':
            e = 'v.v%d%s' % (self.index, k)
        else:
            raise SystemExit('cannot read ' + self.text)
        if self.abs:
            e = 'MathF.Abs(%s)' % e
        if self.neg:
            e = '(-%s)' % e
        return e


class Dst:
    def __init__(self, text):
        if text == 'null':
            self.kind, self.mask = 'null', ''
            return
        m = re.fullmatch(r'([ro])(\d+)(?:\.([xyzw]+))?', text)
        if not m:
            raise SystemExit('destination not understood: ' + text)
        self.kind, self.index, self.mask = m.group(1), int(m.group(2)), m.group(3) or COMP

    def name(self, k):
        return ('r%d%s' if self.kind == 'r' else 'o%d%s') % (self.index, k)


def emit_range(instrs, name):
    body, depth = [], 2
    reads, writes = [], set()

    def read(src, comps):
        for c in comps:
            r = src.reg(c)
            if r and r not in writes and r not in reads:
                reads.append(r)

    def line(s):
        body.append('    ' * depth + s)

    tmp = [0]

    def fresh():
        tmp[0] += 1
        return 't%d' % tmp[0]

    for op, ops, text in instrs:
        sat = op.endswith('_sat')
        base = op[:-4] if sat else op
        line('// ' + text)
        if base in ('if_nz', 'if_z'):
            s = Src(ops[0]); read(s, [0])
            line('if (%sNz(%s))' % ('' if base == 'if_nz' else '!', s.comp(0)))
            line('{'); depth += 1
            continue
        if base == 'else':
            depth -= 1; line('}'); line('else'); line('{'); depth += 1
            continue
        if base == 'endif':
            depth -= 1; line('}')
            continue
        if base == 'ret':
            line('return;')
            continue
        if base in ('discard_nz', 'discard_z'):
            s = Src(ops[0]); read(s, [0])
            line('if (%sNz(%s)) { r.Discarded = true; return; }' % ('' if base == 'discard_nz' else '!', s.comp(0)))
            continue
        if base == 'sincos':
            ds, dc, s = Dst(ops[0]), Dst(ops[1]), Src(ops[2])
            line('{'); depth += 1
            outs = []
            for d, fn in ((ds, 'MathF.Sin'), (dc, 'MathF.Cos')):
                if d.kind == 'null':
                    continue
                for k in d.mask:
                    c = COMP.index(k); read(s, [c])
                    t = fresh(); line('float %s = %s(%s);' % (t, fn, s.comp(c))); outs.append((d.name(k), t))
            for n, t in outs:
                line('%s = %s;' % (n, t)); writes.add(n)
            depth -= 1; line('}')
            continue
        if base.startswith('sample'):
            d, coord, texop = Dst(ops[0]), Src(ops[1]), Src(ops[2])
            read(coord, [0, 1])
            t = fresh()
            line('{'); depth += 1
            line('var %s = tex.S%d(%s, %s);' % (t, texop.index, coord.comp(0), coord.comp(1)))
            items = {'x': 'Item1', 'y': 'Item2', 'z': 'Item3', 'w': 'Item4'}
            for k in d.mask:
                n = d.name(k)
                line('%s = %s.%s;' % (n, t, items[texop.swz[COMP.index(k)]])); writes.add(n)
            depth -= 1; line('}')
            continue
        d = Dst(ops[0])
        srcs = [Src(o) for o in ops[1:]]
        if base in ('dp2', 'dp3', 'dp4'):
            k = int(base[2])
            for s in srcs:
                read(s, range(k))
            t = fresh()
            line('{'); depth += 1
            line('float %s = %s * %s;' % (t, srcs[0].comp(0), srcs[1].comp(0)))
            for i in range(1, k):
                line('%s = MathF.FusedMultiplyAdd(%s, %s, %s);' % (t, srcs[0].comp(i), srcs[1].comp(i), t))
            if sat:
                line('%s = Sat(%s);' % (t, t))
            for kk in d.mask:
                n = d.name(kk); line('%s = %s;' % (n, t)); writes.add(n)
            depth -= 1; line('}')
            continue
        outs = []
        for kk in d.mask:
            c = COMP.index(kk)
            for s in srcs:
                read(s, [c])
            a = [s.comp(c) for s in srcs]
            e = {
                'mov': lambda: a[0],
                'add': lambda: '%s + %s' % (a[0], a[1]),
                'mul': lambda: '%s * %s' % (a[0], a[1]),
                'mad': lambda: '%s * %s + %s' % (a[0], a[1], a[2]),
                'div': lambda: '%s / %s' % (a[0], a[1]),
                'frc': lambda: '%s - MathF.Floor(%s)' % (a[0], a[0]),
                'round_ni': lambda: 'MathF.Floor(%s)' % a[0],
                'round_pi': lambda: 'MathF.Ceiling(%s)' % a[0],
                'round_z': lambda: 'MathF.Truncate(%s)' % a[0],
                'round_ne': lambda: 'MathF.Round(%s, MidpointRounding.ToEven)' % a[0],
                'sqrt': lambda: 'MathF.Sqrt(%s)' % a[0],
                'rsq': lambda: '1f / MathF.Sqrt(%s)' % a[0],
                'exp': lambda: 'float.Exp2(%s)' % a[0],
                'log': lambda: 'MathF.Log2(%s)' % a[0],
                'min': lambda: 'MathF.Min(%s, %s)' % (a[0], a[1]),
                'max': lambda: 'MathF.Max(%s, %s)' % (a[0], a[1]),
                'lt': lambda: 'Mask(%s < %s)' % (a[0], a[1]),
                'ge': lambda: 'Mask(%s >= %s)' % (a[0], a[1]),
                'eq': lambda: 'Mask(%s == %s)' % (a[0], a[1]),
                'ne': lambda: 'Mask(%s != %s)' % (a[0], a[1]),
                'and': lambda: 'And(%s, %s)' % (a[0], a[1]),
                'movc': lambda: 'Nz(%s) ? %s : %s' % (a[0], a[1], a[2]),
            }.get(base)
            if e is None:
                raise SystemExit('instruction not handled: ' + text)
            expr = e()
            if sat:
                expr = 'Sat(%s)' % expr
            outs.append((d.name(kk), expr))
        if len(outs) == 1:
            n, e = outs[0]
            line('%s = %s;' % (n, e)); writes.add(n)
        else:
            line('{'); depth += 1
            ts = []
            for n, e in outs:
                t = fresh(); line('float %s = %s;' % (t, e)); ts.append((n, t))
            for n, t in ts:
                line('%s = %s;' % (n, t)); writes.add(n)
            depth -= 1; line('}')
    if depth != 2:
        raise SystemExit('range %s does not close its blocks' % name)
    regs = sorted(set(reads) | writes, key=lambda s: (s[0], int(s[1:-1]), s[-1]))
    head = ['    // Optimized from the first call: these run per pixel, and tiered compilation can leave them unoptimized for a whole run.',
            '    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]',
            '    internal static void %s<T>(ref GameRegisters r, float[] cb0, float[] cb1, ref T tex, in GameInputs v) where T : struct, IGameTextures' % name,
            '    {']
    # Every register the range touches starts as it was: a branch not taken
    # leaves the caller's value, as the GPU would.
    for g in regs:
        head.append('        float %s = r.%s;' % (g, g))
    written = sorted(writes, key=lambda s: (s[0], int(s[1:-1]), s[-1]))
    wb = ' '.join('r.%s = %s;' % (g, g) for g in written)
    fixed = [b.replace('return;', '{ %s return; }' % wb) if 'return;' in b else b for b in body]
    return head + fixed + ['        ' + wb] + ['    }', ''], reads, writes


def main():
    asm, out, ns, cls = sys.argv[1:5]
    instrs = parse(asm)
    ranges = []
    at = 0
    for spec in sys.argv[5:]:
        name, first, last = spec.split(':', 2)
        def find(prefix, start):
            if prefix == 'start':
                return 0
            if prefix == 'end':
                return len(instrs) - 1
            for i in range(start, len(instrs)):
                if instrs[i][2].startswith(prefix):
                    return i
            raise SystemExit('no instruction starting %r after %d' % (prefix, start))
        i0 = find(first, at)
        i1 = find(last[len('before:'):], i0) - 1 if last.startswith('before:') else find(last, i0)
        ranges.append((name, i0, i1))
    regs = ['r%d%s' % (i, k) for i in range(16) for k in COMP] + ['o0' + k for k in COMP]
    lines = ['// <auto-generated>',
             '// Made by Tools/Shaders/dxbc_to_cs.py from the game\'s own compiled program',
             '// (%s); do not edit - run it again.' % asm.replace('\\', '/').split('/')[-1],
             '// </auto-generated>',
             'using System;',
             '',
             'namespace %s;' % ns,
             '',
             'internal static partial class %s' % cls,
             '{']
    for name, i0, i1 in ranges:
        code, reads, writes = emit_range(instrs[i0:i1 + 1], name)
        lines.append('    // %s: instructions %d-%d; reads %s' % (name, i0, i1, ', '.join(reads) or 'nothing'))
        lines += code
    lines.append('}')
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
    print('wrote', out, [(n, i1 - i0 + 1) for n, i0, i1 in ranges])


if __name__ == '__main__':
    main()
