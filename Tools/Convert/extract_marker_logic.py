#!/usr/bin/env python3
"""
Tool A - dispatcher parser.

Reads the pre-ModForge MainStory.cs dispatcher and turns each dialogue's
marker if-blocks ( if (dialogueToActivate.Find("Scene1").activeSelf) { ... } )
into a structured marker->ops map, keyed by the dialogue's prefab asset name
(the `dialogueToActivate.name == "AssetName"` join, which matches the extract's
`assetName` exactly).

Output: marker_logic.json
  {
    "<AssetName>": {
      "gating": ["<raw guard expr>", ...],          # best-effort -> startConditions
      "markers": { "Scene1": [op, ...], "DialogueFinisher": [op, ...], ... }
    }, ...
  }

Each `op` is one of the classified shapes below, or {"op":"raw","text":...}
for anything the classifier doesn't recognise (surfaced in the coverage report
by the converter so nothing is silently dropped).

This is a DRAFT for human review - edit marker_logic.json after generating.
"""
import json, re, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC  = os.path.join(HERE, "_input", "MainStory_pre_modforge.cs")
DLG  = os.path.join(HERE, "_input", "Dialogues_pre_modforge.cs")
OUT  = os.path.join(HERE, "marker_logic.json")

MARKER_SUFFIX_RE = re.compile(r'(Scene\d+|DialogueFinisher|SpriteFocus|MouthActivator|DialogueActivator)$')


def read_source():
    with open(SRC, encoding="utf-8") as f:
        return f.read()


def load_field_map():
    """field name -> prefab asset name, from Dialogues.cs CreateNewDialogue calls.
    e.g. anisAffection03Dialogue -> AnisDialogueAffection03."""
    fm = {}
    if not os.path.exists(DLG):
        return fm
    with open(DLG, encoding="utf-8") as f:
        txt = f.read()
    for m in re.finditer(r'(\w+)\s*=\s*CreateNewDialogue\("([^"]+)"', txt):
        fm[m.group(1)] = m.group(2)
    return fm


def match_block(text, open_idx):
    """Given index of an opening '{', return (body, index-after-close)."""
    depth = 0
    i = open_idx
    while i < len(text):
        c = text[i]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return text[open_idx + 1:i], i + 1
        i += 1
    return text[open_idx + 1:], len(text)


# ── Statement classifiers ────────────────────────────────────────────────
# Each returns a dict op or None. Order matters (most specific first).
SIGNAL_FIELD_RE = re.compile(r'Signals\.Emit\((\w+)\)')

def classify(stmt):
    s = stmt.strip().rstrip(';').strip()
    if not s:
        return None

    m = re.fullmatch(r'Scenes\.(\w+)\.SetActive\((true|false)\)', s)
    if m:
        return {"op": "scene", "field": m.group(1), "active": m.group(2) == "true"}

    m = re.fullmatch(r'Characters\.(\w+)\.transform\.Find\("(?:MBase1|MBase|D1Base|Base)"\)\.Find\("Leave"\)\.gameObject\.SetActive\(true\)', s)
    if m:
        return {"op": "leaveBust", "char": m.group(1)}

    m = re.fullmatch(r'Characters\.(\w+)\.SetActive\((true|false)\)', s)
    if m:
        return {"op": "bustActive", "char": m.group(1), "active": m.group(2) == "true"}

    m = SIGNAL_FIELD_RE.fullmatch(s)
    if m:
        return {"op": "emitSignalField", "field": m.group(1)}

    m = re.fullmatch(r'Core\.EmitSignalDelayed\("([^"]+)",\s*([0-9.]+)f?\)', s)
    if m:
        return {"op": "emitSignalDelayed", "signal": m.group(1), "seconds": float(m.group(2))}

    m = re.fullmatch(r'Core\.EmitSignalGameObjectDelayed\("([^"]+)",\s*Places\.(\w+),\s*Places\.(\w+),\s*([0-9.]+)f?\)', s)
    if m:
        return {"op": "transitionLevels", "signal": m.group(1),
                "fromLevel": m.group(2), "toLevel": m.group(3), "seconds": float(m.group(4))}

    m = re.fullmatch(r'SaveManager\.SetBool\("([^"]+)",\s*(true|false)\)', s)
    if m:
        return {"op": "setVar", "name": m.group(1), "value": m.group(2), "vtype": "bool"}

    m = re.fullmatch(r'SaveManager\.SetInt\("([^"]+)",\s*(.+)\)', s)
    if m:
        return {"op": "setVar", "name": m.group(1), "value": m.group(2).strip(), "vtype": "int"}

    m = re.fullmatch(r'SaveManager\.SetString\("([^"]+)",\s*"([^"]*)"\)', s)
    if m:
        return {"op": "setVar", "name": m.group(1), "value": m.group(2), "vtype": "string"}

    m = re.fullmatch(r'Places\.(\w+)\.transform\.Find\("([^"]+)"\)\.gameObject\.SetActive\((true|false)\)', s)
    if m:
        return {"op": "goActive", "level": m.group(1), "child": m.group(2), "active": m.group(3) == "true"}

    m = re.fullmatch(r'Schedule\.(\w+)\s*=\s*"([^"]+)"', s)
    if m:
        return {"op": "setSchedule", "field": m.group(1), "value": m.group(2)}

    m = re.fullmatch(r'ChangeActiveBust\(Characters\.(\w+),\s*Characters\.(\w+)\)', s)
    if m:
        return {"op": "changeBust", "fromBust": m.group(1), "toBust": m.group(2)}

    m = re.fullmatch(r'Core\.ChangeOutfitDelayed\(Characters\.(\w+),\s*Characters\.(\w+),\s*([0-9.]+)f?\)', s)
    if m:
        return {"op": "changeBust", "fromBust": m.group(1), "toBust": m.group(2), "delay": float(m.group(3))}

    m = re.fullmatch(r'Core\.FindAndModifyProxyVariableBool\("([^"]+)",\s*(true|false)\)', s)
    if m:
        return {"op": "setVar", "name": m.group(1), "value": m.group(2), "vtype": "bool"}

    if re.match(r'Invoke\(nameof\(EndDialogue', s):
        return {"op": "endDialogue"}

    # Marker self-reset - structural, not behaviour. Drop quietly.
    if re.fullmatch(r'this\.dialogueToActivate\.transform\.Find\("[^"]+"\)\.gameObject\.SetActive\(false\)', s):
        return {"op": "_markerReset"}
    if re.fullmatch(r'Dialogues\.\w+(?:Scene\d+|DialogueFinisher|SpriteFocus|MouthActivator|DialogueActivator)\.SetActive\(false\)', s):
        return {"op": "_markerReset"}

    return {"op": "raw", "text": s}


def split_statements(body):
    """Split a marker block body into top-level statements, keeping nested
    `if (...) { ... }` groups intact (returned as a single raw-ish chunk for
    sub-parsing)."""
    out = []
    i = 0
    cur = ""
    while i < len(body):
        c = body[i]
        if c == '{':
            sub, j = match_block(body, i)
            cur += "{" + sub + "}"
            i = j
            continue
        if c == ';':
            out.append(cur + ";")
            cur = ""
            i += 1
            continue
        cur += c
        i += 1
    if cur.strip():
        out.append(cur)
    return out


NESTED_IF_RE = re.compile(r'if\s*\((?P<cond>[^()]*(?:\([^()]*\)[^()]*)*)\)\s*\{(?P<body>.*)\}', re.S)


def parse_marker_body(body):
    ops = []
    for stmt in split_statements(body):
        st = stmt.strip()
        nm = NESTED_IF_RE.match(st)
        if nm:
            cond = nm.group("cond").strip()
            inner = [classify(x) for x in split_statements(nm.group("body"))]
            inner = [o for o in inner if o and o.get("op") not in ("_markerReset",)]
            ops.append({"op": "conditional", "cond": cond, "then": inner})
            continue
        o = classify(st)
        if o and o.get("op") != "_markerReset":
            ops.append(o)
    return ops


MARKER_IF_RE = re.compile(
    r'this\.dialogueToActivate\.transform\.Find\("(?P<marker>[^"]+)"\)\.gameObject\.activeSelf\)\s*')


def parse_dialogue_block(block):
    """Within a `name == "X"` block, pull each marker if-block -> ops."""
    markers = {}
    for m in MARKER_IF_RE.finditer(block):
        # find the '{' that starts this if's body (just after the match)
        brace = block.find('{', m.end() - 1)
        if brace < 0:
            continue
        body, _ = match_block(block, brace)
        marker = m.group("marker")
        markers.setdefault(marker, []).extend(parse_marker_body(body))
    return markers


# Gating: capture the guard chain around `dialogueToActivate = Dialogues.X`.
ASSIGN_RE = re.compile(r'this\.dialogueToActivate\s*=\s*Dialogues\.(?P<field>\w+)\s*;')
NAME_EQ_RE = re.compile(r'this\.dialogueToActivate\.name\s*==\s*"(?P<asset>[^"]+)"')


def gather_gating(text, assign_pos, case_name):
    """Best-effort: collect SaveManager.Get* / Places.*.activeSelf guard
    expressions from the lines just above the assignment (same case)."""
    start = text.rfind('case "', 0, assign_pos)
    window = text[start:assign_pos]
    guards = []
    for gm in re.finditer(r'(SaveManager\.Get(?:Bool|Int|String)\("[^"]+"\)[^\)&|]*|Places\.\w+\.activeSelf|Location_\w+)', window):
        g = gm.group(1).strip()
        if g not in guards:
            guards.append(g)
    if case_name and ("level:"+case_name) not in guards:
        guards.insert(0, "case:" + case_name)
    return guards


MARKER_FIELD_RE = re.compile(r'(?P<neg>!\s*)?Dialogues\.(?P<ident>[A-Za-z0-9_]+)\.activeSelf')

# A marker reference is `Dialogues.<field><marker>`. Two spellings occur:
#  * field ends in "Dialogue" -> the marker's leading "Dialogue" collapses, so
#    `…Dialogue` + `DialogueFinisher` is written `…DialogueFinisher`
#    (remainder = "Finisher").
#  * field does NOT end in "Dialogue" (e.g. `sBDialogueMainFirst`) -> the full
#    marker name is appended: `…First` + `DialogueFinisher` =
#    `…FirstDialogueFinisher` (remainder = "DialogueFinisher").
# So the remainder can be either the full marker name or its "Dialogue"-stripped
# form. We take the longest dialogue field that prefixes <ident>.
_FULL_MARKERS = {"DialogueFinisher", "DialogueActivator", "SpriteFocus", "MouthActivator"}
_COLLAPSED_MARKER = {"Finisher": "DialogueFinisher", "Activator": "DialogueActivator"}


def build_marker_resolver(fieldmap):
    fm_keys = sorted(fieldmap.keys(), key=len, reverse=True)

    def resolve(ident):
        for F in fm_keys:
            if ident.startswith(F):
                rem = ident[len(F):]
                if re.fullmatch(r'Scene\d+', rem):
                    return fieldmap[F], rem
                if rem in _FULL_MARKERS:
                    return fieldmap[F], rem
                if rem in _COLLAPSED_MARKER:
                    return fieldmap[F], _COLLAPSED_MARKER[rem]
        return None, None
    return resolve


def main():
    text = read_source()
    fieldmap = load_field_map()   # field -> asset name

    # field -> asset name, from the `name == "X"` checks that follow assignments
    result = {}

    # ── Pattern 1: this.dialogueToActivate.transform.Find("Marker").activeSelf
    for nm in NAME_EQ_RE.finditer(text):
        asset = nm.group("asset")
        brace = text.find('{', nm.end())
        if brace < 0:
            continue
        block, _ = match_block(text, brace)
        markers = parse_dialogue_block(block)
        entry = result.setdefault(asset, {"gating": [], "markers": {}})
        for k, v in markers.items():
            entry["markers"].setdefault(k, []).extend(v)

    # ── Pattern 2: Dialogues.<field><Marker>.activeSelf (Secret Beach / HH /
    # voyeur). Conditions can be compound (`A.activeSelf && !B.activeSelf …`);
    # negated terms are guards, not the trigger, so only the first non-negated
    # marker drives a block. De-dup by body brace position so a multi-marker
    # condition isn't processed twice.
    resolve = build_marker_resolver(fieldmap)
    processed_braces = set()
    for mm in MARKER_FIELD_RE.finditer(text):
        if mm.group("neg"):
            continue
        ident = mm.group("ident")
        asset, marker = resolve(ident)
        if not asset:
            continue
        brace = text.find('{', mm.end())
        if brace < 0 or brace in processed_braces:
            continue
        processed_braces.add(brace)
        body, _ = match_block(text, brace)
        ops = parse_marker_body(body)
        if ops:
            entry = result.setdefault(asset, {"gating": [], "markers": {}})
            entry["markers"].setdefault(marker, []).extend(ops)

    # ── Pattern-2 gating: StartDialogueSequence(Dialogues.<field>)
    for sm2 in re.finditer(r'StartDialogueSequence\(Dialogues\.(\w+)\)', text):
        field = sm2.group(1)
        asset = fieldmap.get(field)
        if asset in result:
            guards = gather_gating(text, sm2.start(), None)
            for g in guards:
                if g not in result[asset]["gating"]:
                    result[asset]["gating"].append(g)
            result[asset].setdefault("fieldName", field)

    # Attach gating by walking each assignment + the nearest following name==.
    for am in ASSIGN_RE.finditer(text):
        field = am.group("field")
        # nearest case label above
        cstart = text.rfind('case "', 0, am.start())
        case_name = None
        if cstart >= 0:
            cm = re.match(r'case "([^"]+)"', text[cstart:cstart + 80])
            if cm:
                case_name = cm.group(1)
        # nearest name== after the assignment (same dialogue)
        nm = NAME_EQ_RE.search(text, am.end())
        if not nm:
            continue
        asset = nm.group("asset")
        guards = gather_gating(text, am.start(), case_name)
        if asset in result:
            for g in guards:
                if g not in result[asset]["gating"]:
                    result[asset]["gating"].append(g)
            result[asset]["fieldName"] = field

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=1, ensure_ascii=False)

    # Console summary
    raws = sum(1 for d in result.values() for ops in d["markers"].values()
               for o in ops if o.get("op") == "raw")
    print(f"dialogues parsed: {len(result)}")
    print(f"total markers:    {sum(len(d['markers']) for d in result.values())}")
    print(f"unclassified raw ops (will be flagged): {raws}")
    print(f"-> {OUT}")


if __name__ == "__main__":
    main()
