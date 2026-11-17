"""Turns the game's programs, as Tools/Shaders/dxbc_to_cs.py wrote them into C# (GameBustProgram.g.cs,
GamePostProgram.g.cs), into HLSL for the graphics card - instruction for instruction, each one as the HLSL
that compiles back to that same instruction, so the card runs what the game's card ran (1.9.1: the previews
on the GPU):

    python dxbc_to_hlsl.py <Program.g.cs> <out.hlsl>

Every C# method there (one range of the program) becomes an HLSL function of the same name,
`void <name>(inout GameRegisters r, GameInputs v)`. It reads the program's textures through S0(u, v) to S3(u, v)
(float4) and its constants through `cb0[i]` and `cb1[i]` (float4 arrays), which the kernel including it declares.

The instructions are read back from the comments dxbc_to_cs.py writes above each one: the .asm files it was
made from are not kept, and the comment is the disassembler's own line (hex literals included).

As the game's program has them:
  - `mad` is mad(), dp2/dp3/dp4 are dot(): the card does what it did for the game (fused or not);
  - every other operation is its own, on `precise` registers, so the compiler fuses and reorders nothing;
  - literals are the program's 32 bits (asfloat); comparisons write all-ones bits or zero, `and`
    and `movc` work on the bits;
  - `discard` sets r.Discarded and returns (the kernel draws nothing there).
"""
import re
import sys

sys.path.insert(0, __import__('os').path.dirname(__file__))
from dxbc_to_cs import Src, Dst, split_operands, COMP  # noqa: E402

METHOD = re.compile(r'^    internal static void (\w+)<T>\(')
INSTR = re.compile(r'^\s+// (.+)$')


def read_methods(path):
    methods, name, instrs = [], None, []
    for raw in open(path, encoding='utf-8'):
        line = raw.rstrip('\n')
        m = METHOD.match(line)
        if m:
            name, instrs = m.group(1), []
            continue
        if name is None:
            continue
        if line == '    }':
            methods.append((name, instrs))
            name = None
            continue
        m = INSTR.match(line)
        if m:
            text = m.group(1).strip()
            op, _, rest = text.partition(' ')
            instrs.append((op, split_operands(rest), text))
    return methods


def lit(bits):
    return 'asfloat(0x%08Xu)' % (bits & 0xFFFFFFFF)


def comp(s, c):
    k = s.swz[c]
    if s.kind == 'lit':
        e = lit(s.lit[c])
    elif s.kind == 'cb':
        e = 'cb%d[%d].%s' % (s.cb, s.index, k)
    elif s.kind == 'r':
        e = 'r.r%d%s' % (s.index, k)
    elif s.kind == 'v':
        e = 'v.v%d%s' % (s.index, k)
    else:
        raise SystemExit('cannot read ' + s.text)
    if s.abs:
        e = 'abs(%s)' % e
    if s.neg:
        e = '(-%s)' % e
    return e


def dst(d, k):
    return ('r.r%d%s' if d.kind == 'r' else 'r.o%d%s') % (d.index, k)


def emit(name, instrs):
    out, depth = [], 1
    tmp = [0]

    def line(s):
        out.append('    ' * depth + s)

    def fresh():
        tmp[0] += 1
        return 't%d' % tmp[0]

    for op, ops, text in instrs:
        sat = op.endswith('_sat')
        base = op[:-4] if sat else op
        wrap = (lambda e: 'saturate(%s)' % e) if sat else (lambda e: e)
        line('// ' + text)
        if base in ('if_nz', 'if_z'):
            s = Src(ops[0])
            line('if (asuint(%s) %s 0u)' % (comp(s, 0), '!=' if base == 'if_nz' else '=='))
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
            s = Src(ops[0])
            line('if (asuint(%s) %s 0u) { r.Discarded = true; return; }' % (comp(s, 0), '!=' if base == 'discard_nz' else '=='))
            continue
        if base == 'sincos':
            ds, dc, s = Dst(ops[0]), Dst(ops[1]), Src(ops[2])
            line('{'); depth += 1
            outs = []
            for d, fn in ((ds, 'sin'), (dc, 'cos')):
                if d.kind == 'null':
                    continue
                for k in d.mask:
                    t = fresh(); line('precise float %s = %s(%s);' % (t, fn, comp(s, COMP.index(k)))); outs.append((dst(d, k), t))
            for n, t in outs:
                line('%s = %s;' % (n, t))
            depth -= 1; line('}')
            continue
        if base.startswith('sample'):
            d, coord, texop = Dst(ops[0]), Src(ops[1]), Src(ops[2])
            t = fresh()
            line('{'); depth += 1
            line('float4 %s = S%d(%s, %s);' % (t, texop.index, comp(coord, 0), comp(coord, 1)))
            for k in d.mask:
                line('%s = %s.%s;' % (dst(d, k), t, texop.swz[COMP.index(k)]))
            depth -= 1; line('}')
            continue
        d = Dst(ops[0])
        srcs = [Src(o) for o in ops[1:]]
        if base in ('dp2', 'dp3', 'dp4'):
            k = int(base[2])
            t = fresh()
            vec = 'float%d' % k
            a = ', '.join(comp(srcs[0], i) for i in range(k))
            b = ', '.join(comp(srcs[1], i) for i in range(k))
            line('{'); depth += 1
            line('precise float %s = %s;' % (t, wrap('dot(%s(%s), %s(%s))' % (vec, a, vec, b))))
            for kk in d.mask:
                line('%s = %s;' % (dst(d, kk), t))
            depth -= 1; line('}')
            continue
        outs = []
        for kk in d.mask:
            c = COMP.index(kk)
            a = [comp(s, c) for s in srcs]
            e = {
                'mov': lambda: a[0],
                'add': lambda: '%s + %s' % (a[0], a[1]),
                'mul': lambda: '%s * %s' % (a[0], a[1]),
                'mad': lambda: 'mad(%s, %s, %s)' % (a[0], a[1], a[2]),
                'div': lambda: '%s / %s' % (a[0], a[1]),
                'frc': lambda: 'frac(%s)' % a[0],
                'round_ni': lambda: 'floor(%s)' % a[0],
                'round_pi': lambda: 'ceil(%s)' % a[0],
                'round_z': lambda: 'trunc(%s)' % a[0],
                'round_ne': lambda: 'round(%s)' % a[0],
                'sqrt': lambda: 'sqrt(%s)' % a[0],
                'rsq': lambda: 'rsqrt(%s)' % a[0],
                'exp': lambda: 'exp2(%s)' % a[0],
                'log': lambda: 'log2(%s)' % a[0],
                'min': lambda: 'min(%s, %s)' % (a[0], a[1]),
                'max': lambda: 'max(%s, %s)' % (a[0], a[1]),
                'lt': lambda: 'asfloat((%s < %s) ? 0xFFFFFFFFu : 0u)' % (a[0], a[1]),
                'ge': lambda: 'asfloat((%s >= %s) ? 0xFFFFFFFFu : 0u)' % (a[0], a[1]),
                'eq': lambda: 'asfloat((%s == %s) ? 0xFFFFFFFFu : 0u)' % (a[0], a[1]),
                'ne': lambda: 'asfloat((%s != %s) ? 0xFFFFFFFFu : 0u)' % (a[0], a[1]),
                'and': lambda: 'asfloat(asuint(%s) & asuint(%s))' % (a[0], a[1]),
                'movc': lambda: '(asuint(%s) != 0u) ? %s : %s' % (a[0], a[1], a[2]),
            }.get(base)
            if e is None:
                raise SystemExit('instruction not handled: ' + text)
            outs.append((dst(d, kk), wrap(e())))
        if len(outs) == 1:
            n, e = outs[0]
            line('%s = %s;' % (n, e))
        else:
            # Every component from the registers as they were before the instruction.
            line('{'); depth += 1
            ts = []
            for n, e in outs:
                t = fresh(); line('precise float %s = %s;' % (t, e)); ts.append((n, t))
            for n, t in ts:
                line('%s = %s;' % (n, t))
            depth -= 1; line('}')
    if depth != 1:
        raise SystemExit('range %s does not close its blocks' % name)
    return ['void %s(inout GameRegisters r, GameInputs v)' % name, '{'] + out + ['}', '']


HEADER = '''// <auto-generated>
// Made by Tools/Shaders/dxbc_to_hlsl.py from %s (the game's own compiled program, as
// dxbc_to_cs.py wrote it); do not edit - run it again.
// </auto-generated>
'''

REGS = '''#ifndef GAME_REGISTERS
#define GAME_REGISTERS
// The program's registers, between one part of it and the next (GameBustProgram.cs: GameRegisters).
struct GameRegisters
{
%s    bool Discarded;
};
// What the vertex stage hands a pixel (GameInputs).
struct GameInputs
{
    float v1x, v1y, v1z, v1w, v2x, v2y, v2z, v2w, v3x, v3y, v3z, v3w;
};
#endif
'''


def main():
    src, out = sys.argv[1:3]
    methods = read_methods(src)
    regs = ''.join('    precise float %s;\n' % ', '.join('r%d%s' % (i, k) for k in COMP) for i in range(16))
    regs += '    precise float o0x, o0y, o0z, o0w;\n'
    lines = [HEADER % src.replace('\\', '/').split('/')[-1], REGS % regs]
    for name, instrs in methods:
        lines.append('// %s: %d instructions' % (name, len(instrs)))
        lines += emit(name, instrs)
    open(out, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines) + '\n')
    print('wrote', out, [(n, len(i)) for n, i in methods])


if __name__ == '__main__':
    main()
