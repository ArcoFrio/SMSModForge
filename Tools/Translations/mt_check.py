"""mt_check.py <code> <NN> : compare a translated chunk with its source."""
import re
import sys

BASE = r"C:\Users\gabri\AppData\Local\Temp\claude\C--Users-gabri-source-repos\ce293352-32f5-4a51-b18e-65a958229bd0\scratchpad\mt"
FORMS = {"ru": ["one", "few", "many"], "ja": ["other"], "ko": ["other"], "zh-Hans": ["other"]}
PLURAL = {"zero", "one", "two", "few", "many", "other"}


def read(path):
    d = {}
    for line in open(path, encoding="utf-8").read().splitlines():
        m = re.match(r"^([\w.\-]+) = ?(.*)$", line.strip())
        if m:
            if m.group(1) in d:
                print("DUPLICATE", m.group(1))
            d[m.group(1)] = m.group(2)
    return d


def codes(s):
    out = re.findall(r"\{[A-Za-z_][\w]*\}", s)
    out += re.findall(r"</?[A-Za-z#/][^<>]*>", s)
    out += [t for t in re.findall(r"\[[A-Za-z]+:[^\]\s]*\]", s)]
    out += re.findall(r"\\n", s)
    return sorted(out)


def main(code, nn):
    src = read(f"{BASE}\\src\\{nn}.txt")
    dst = read(f"{BASE}\\out\\{code}\\{nn}.txt")
    forms = FORMS.get(code, ["one", "other"])
    want = {}
    done_base = set()
    for k, v in src.items():
        last = k.rsplit(".", 1)[-1]
        if last in PLURAL:
            base = k.rsplit(".", 1)[0]
            if base in done_base:
                continue
            done_base.add(base)
            eng = {f: src.get(base + "." + f) for f in ("one", "other")}
            for f in forms:
                want[base + "." + f] = eng["other"] if f != "one" else (eng["one"] or eng["other"])
        else:
            want[k] = v
    missing = [k for k in want if k not in dst]
    extra = [k for k in dst if k not in want]
    for k in missing:
        print("MISSING", k)
    for k in extra:
        print("EXTRA", k)
    for k, v in want.items():
        if k not in dst:
            continue
        a, b = codes(v), codes(dst[k])
        if k.endswith(".one"):
            a = [c for c in a if c != "{count}"]
            b = [c for c in b if c != "{count}"]
        if a != b:
            print("CODES", k, a, "->", b)
        if "_" in re.sub(r"\{[^}]*\}", "", v) and re.search(r"(^|[^_])_[^\s_]", v) and "_" not in dst[k]:
            if k.startswith("menu.") or ".menu" in k:
                print("ACCESSKEY", k, dst[k])
        if dst[k].strip() == "" and v.strip() != "":
            print("EMPTY", k)
    print(code, nn, len(dst), "of", len(want))


main(sys.argv[1], sys.argv[2])
