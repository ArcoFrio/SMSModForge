"""Extracts every compiled variant of a shader from the game's assets and disassembles it with Windows' own
D3D disassembler (d3dcompiler_47.dll). Writes, under <out>/<shader name>/:
  index.tsv                one line per variant: pass, stage, variant, keywords, blob, file
  p<pass>_<stage>_<blob>.asm  each distinct program once
  p<pass>_<stage>_<blob>.params.txt  the strings + numbers of its parameter blob (constant names, offsets)
Usage: extract.py <Starmaker Story_Data> <assets file> <path id> <out dir>"""
import ctypes
import io
import os
import re
import struct
import sys
from ctypes import POINTER, WINFUNCTYPE, byref, c_char_p, c_size_t, c_uint, c_void_p

import lz4.block
import UnityPy

data_dir, fname, pid, out_root = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]

_d3d = ctypes.WinDLL('d3dcompiler_47.dll')
_dis = _d3d.D3DDisassemble
_dis.argtypes = [c_void_p, c_size_t, c_uint, c_char_p, POINTER(c_void_p)]
_dis.restype = ctypes.c_long


def disassemble(code: bytes) -> str:
    buf = ctypes.create_string_buffer(code, len(code))
    blob = c_void_p()
    # D3D_DISASM_PRINT_HEX_LITERALS: literals as their 32 bits. As decimals the
    # disassembler rounds them to six places - not the constants the GPU runs.
    hr = _dis(buf, len(code), 0x80, None, byref(blob))
    if hr != 0:
        return f'// D3DDisassemble failed: 0x{hr & 0xffffffff:08x}\n'
    vtbl = ctypes.cast(blob, POINTER(POINTER(c_void_p)))[0]
    get_ptr = WINFUNCTYPE(c_void_p, c_void_p)(vtbl[3])
    get_size = WINFUNCTYPE(c_size_t, c_void_p)(vtbl[4])
    release = WINFUNCTYPE(ctypes.c_ulong, c_void_p)(vtbl[2])
    text = ctypes.string_at(get_ptr(blob), get_size(blob)).decode('latin-1').rstrip('\x00')
    release(blob)
    return text


def strings_and_numbers(b: bytes) -> str:
    """Length-prefixed strings in a parameter blob, each with the ints that follow it."""
    lines = []
    i = 0
    while i + 4 <= len(b):
        n = struct.unpack_from('<i', b, i)[0]
        if 1 <= n <= 64 and i + 4 + n <= len(b):
            s = b[i + 4:i + 4 + n]
            if re.fullmatch(rb'[$A-Za-z_][A-Za-z0-9_]*', s):
                j = i + 4 + ((n + 3) // 4) * 4
                nums = list(struct.unpack_from('<6i', b, j)) if j + 24 <= len(b) else []
                lines.append(f'{s.decode()}\t{nums}')
                i = j
                continue
        i += 4
    return '\n'.join(lines) + '\n'


env = UnityPy.load(os.path.join(data_dir, fname))
obj = next(o for o in env.objects if o.path_id == pid)
d = obj.read()
pf = d.m_ParsedForm
name = pf.m_Name
blob = bytes(d.compressedBlob)
# The bytecode can be split into several compressed chunks (a large shader's is);
# the table, in the first, says which chunk each program is in.
segs = []
for off, cl, dl in zip(d.offsets[0], d.compressedLengths[0], d.decompressedLengths[0]):
    segs.append(lz4.block.decompress(blob[off:off + cl], uncompressed_size=dl) if cl != dl else blob[off:off + cl])
raw = segs[0]
count = struct.unpack_from('<i', raw, 0)[0]
table = [struct.unpack_from('<iii', raw, 4 + i * 12) for i in range(count)]
kw_names = list(pf.m_KeywordNames)

out = os.path.join(out_root, re.sub(r'[^A-Za-z0-9_.-]+', '_', name))
os.makedirs(out, exist_ok=True)
index = ['pass\tstage\tvariant\tkeywords\tblob\tparams\tfile']
written = set()
for si, sub in enumerate(pf.m_SubShaders):
    for pi, ps in enumerate(sub.m_Passes):
        for stage in ('progVertex', 'progFragment'):
            prog = getattr(ps, stage)
            tiers = [t for t in prog.m_PlayerSubPrograms if t]
            ptiers = [t for t in prog.m_ParameterBlobIndices if t]
            if not tiers:
                continue
            for vi, sp in enumerate(tiers[-1]):
                kws = [kw_names[k] for k in sp.m_KeywordIndices if k < len(kw_names)]
                bi = sp.m_BlobIndex
                pbi = ptiers[-1][vi] if ptiers and vi < len(ptiers[-1]) else -1
                tag = f'p{pi}_{"vs" if stage == "progVertex" else "ps"}_{bi}'
                if tag not in written:
                    o, ln, seg = table[bi]
                    entry = segs[seg][o:o + ln]
                    pos = entry.find(b'DXBC')
                    if pos >= 0:
                        size = struct.unpack_from('<I', entry, pos + 24)[0]
                        text = disassemble(entry[pos:pos + size])
                    else:
                        text = '// no DXBC in this entry\n'
                    io.open(os.path.join(out, tag + '.asm'), 'w', encoding='utf-8', newline='\n').write(
                        f'// {name} pass {pi} {stage} blob {bi}\n' + text.replace('\r\n', '\n'))
                    if pbi >= 0:
                        po, pln, pseg = table[pbi]
                        open(os.path.join(out, tag + '.params.bin'), 'wb').write(segs[pseg][po:po + pln])
                        io.open(os.path.join(out, tag + '.params.txt'), 'w', encoding='utf-8', newline='\n').write(
                            strings_and_numbers(segs[pseg][po:po + pln]))
                    written.add(tag)
                index.append(f'{pi}\t{stage}\t{vi}\t{" ".join(kws) or "-"}\t{bi}\t{pbi}\t{tag}.asm')
io.open(os.path.join(out, 'index.tsv'), 'w', encoding='utf-8', newline='\n').write('\n'.join(index) + '\n')
print(f'{name}: {len(index) - 1} variants, {len(written)} distinct programs -> {out}')
