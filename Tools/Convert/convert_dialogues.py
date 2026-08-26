#!/usr/bin/env python3
"""
Tool C - converter.

extract (SMSAndroidsDialogues.json) + marker_logic.json (Tool A) + mapping.json
(human control point, auto-scaffolded on first run) -> dialogues_out.json
(the modpack `dialogues` array) + coverage_report.md.

Deterministic: every node is translated mechanically; every marker drives the
actions parsed from the old dispatcher; and ANYTHING uncertain (unmapped
actor/expression/scene/variable/clip, unrecognised marker op, the 2 null
targets, a dialogue with no marker logic, an unhandled GC2 type) is listed in
the coverage report rather than dropped silently.

Run again after editing mapping.json — your edits win over the auto-scaffold.
"""
import json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.abspath(os.path.join(HERE, "..", ".."))
EXTRACT = os.path.join(BASE, "SMSAndroidsDialogues.json")
MODPACK = os.path.join(BASE, "SMSAndroidsPack", "modpack.json")
MAINSTORY = os.path.join(HERE, "_input", "MainStory_pre_modforge.cs")
CHARACTERS = os.path.join(HERE, "_input", "Characters_pre_modforge.cs")
VANILLA_ROOMTALKS = os.path.join(BASE, "SMSModForge", "Model", "VanillaRoomTalks.cs")


def load_vanilla_roomtalks():
    """lowercased name -> canonical roomtalk name, from VanillaRoomTalks.cs."""
    out = {}
    if os.path.exists(VANILLA_ROOMTALKS):
        for m in re.finditer(r'new\("([^"]+)"', open(VANILLA_ROOMTALKS, encoding="utf-8").read()):
            out[m.group(1).lower()] = m.group(1)
    return out


def derive_roomtalk(level, pack_place_by_lc, vrt_canon):
    """A dialogue's roomTalk token from its LevelActive level token:
      place:<key>             -> place:<key>
      vanilla:<NN>_<Name>     -> strip the number prefix, then
                                 a pack place    -> place:<key>
                                 a vanilla roomtalk -> vanilla:<canonical>
    Place/roomtalk keys are emitted in their canonical case (the validator
    matches case-sensitively). Returns None when it can't be resolved (caller
    flags it for manual entry)."""
    if not level or ":" not in level:
        return None
    scheme, rest = level.split(":", 1)
    if scheme == "place":
        return "place:" + pack_place_by_lc.get(rest.lower(), rest)
    if scheme == "vanilla":
        name = re.sub(r"^\d+_", "", rest)
        key = name.lower()
        if key in pack_place_by_lc:
            return "place:" + pack_place_by_lc[key]
        if key in vrt_canon:
            return "vanilla:" + vrt_canon[key]
    return None
MARKER_LOGIC = os.path.join(HERE, "marker_logic.json")
MAPPING = os.path.join(HERE, "mapping.json")
OUT = os.path.join(HERE, "dialogues_out.json")
REPORT = os.path.join(HERE, "coverage_report.md")

TEMPLATES = {"DEFAULT", "EVENT", "GIFT"}

flags = []  # (category, dialogue, detail)
def flag(cat, dlg, detail):
    flags.append((cat, dlg, detail))


# ── load reference data ───────────────────────────────────────────────────
def load():
    ext = json.load(open(EXTRACT, encoding="utf-8"))
    mp = json.load(open(MODPACK, encoding="utf-8"))
    ml = json.load(open(MARKER_LOGIC, encoding="utf-8"))
    ms = open(MAINSTORY, encoding="utf-8").read()
    return ext, mp, ml, ms


def signal_field_map(ms):
    out = {}
    for m in re.finditer(r'public static SignalArgs (\w+)\s*=\s*new SignalArgs\(new PropertyName\("([^"]+)"', ms):
        out[m.group(1)] = m.group(2)
    return out


def load_bust_map():
    """Characters.<field> -> bust GameObject name, from Characters.cs.
    Two forms: `x = CreateNewBust("GO", …)` and `x = bustManager.Find("GO")`."""
    m = {}
    if not os.path.exists(CHARACTERS):
        return m
    txt = open(CHARACTERS, encoding="utf-8").read()
    for mm in re.finditer(r'(\w+)\s*=\s*CreateNewBust\("([^"]+)"', txt):
        m[mm.group(1)] = mm.group(2)
    for mm in re.finditer(r'(\w+)\s*=\s*Core\.bustManager\.Find\("([^"]+)"\)\.gameObject', txt):
        m.setdefault(mm.group(1), mm.group(2))
    return m


# ── GC2 value helpers (dig a scalar out of a Get* property) ────────────────
def prop(node, *keys):
    cur = node
    for k in keys:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(k)
    return cur


def get_text(m_text):
    """m_Text -> the authored string (preserve rich-text)."""
    return prop(m_text, "m_Text", "m_Property", "m_Text", "m_Text")


def _inner(getprop):
    """The inner m_Property dict of a GC2 Get* wrapper."""
    return prop(getprop, "m_Property")


def cond_var_name(getprop):
    """Variable name out of a Get*GlobalName property (nested at
    m_Property.m_Variable.m_Name.m_String)."""
    p = _inner(getprop)
    if isinstance(p, dict) and "GlobalName" in p.get("__type", ""):
        return prop(p, "m_Variable", "m_Name", "m_String")
    return None


def cond_bool(getprop):
    p = _inner(getprop)
    t = p.get("__type", "") if isinstance(p, dict) else ""
    if t.endswith("BoolTrue"):  return True
    if t.endswith("BoolFalse"): return False
    if t.endswith("BoolValue"): return bool(p.get("m_Value"))
    return None


def cond_str(getprop):
    p = _inner(getprop)
    if not isinstance(p, dict):
        return None
    if "m_Value" in p:
        return p["m_Value"]
    return prop(p, "m_Text", "m_Text")


def cond_num(getprop):
    p = _inner(getprop)
    return p.get("m_Value") if isinstance(p, dict) else None


# GC2 m_Comparison -> ModForge condition type.
CMP_TYPE = {
    "Equal": "VariableEquals", "Equals": "VariableEquals",
    "NotEqual": "VariableEquals", "NotEquals": "VariableEquals",
    "GreaterOrEqual": "VariableGreaterOrEqual", "LessOrEqual": "VariableLessOrEqual",
    "Greater": "VariableGreaterThan", "GreaterThan": "VariableGreaterThan",
    "Less": "VariableLessThan", "LessThan": "VariableLessThan",
}
CMP_NEG = {"NotEqual", "NotEquals"}


# ── mapping scaffold ──────────────────────────────────────────────────────
# Script-relative, like MAPPING/OUT — a cwd-relative path here silently
# missed the extract whenever the tool was run from anywhere but this
# folder, and the run then fell back to the stale mapping.json without
# the failure being obvious in the output.
ACTOR_EXTRACT = os.path.join(HERE, "_input", "SMSAndroidsActors.json")


def expression_map_from_actor_extract():
    """(actorKey -> {index: exprName}) straight from the GC2 Actor assets.

    GC2 stores a line's expression as an INDEX into the actor asset's expression
    list, and the dialogue extract carries only that number. The authoritative
    names live on the Actor assets, which Tools/UnityEditor/SMSModForgeActorExtractor.cs
    already dumps in list order — so index i is simply expressions[i].

    This is the correct source. The fallback below infers the mapping by joining
    against whatever the migrated pack already says, which is circular: every
    expression missing or wrong in the pack stays missing, and re-running the
    tool can only ever reproduce the gaps it was seeded from.
    """
    if not os.path.exists(ACTOR_EXTRACT):
        return {}
    try:
        data = json.load(open(ACTOR_EXTRACT, encoding="utf-8"))
    except Exception:
        return {}
    out = {}
    for a in data.get("actors", []) or []:
        ak = actor_key(a.get("assetName"))
        if not ak:
            continue
        by_index = {}
        for i, e in enumerate(a.get("expressions", []) or []):
            name = (e.get("id") or "").strip()
            if name:
                by_index[str(i)] = name
        if by_index:
            out[ak] = by_index
    return out


def derive_expression_map(ext, mp):
    """(actorKey -> {index: exprKey}). Prefers the Actor-asset extract; falls
    back to joining extract nodes to the migrated pack on node id."""
    from_extract = expression_map_from_actor_extract()
    if from_extract:
        print(f"expressions: sourced from {ACTOR_EXTRACT} "
              f"({len(from_extract)} actors)")
        return from_extract
    print(f"expressions: {ACTOR_EXTRACT} not found — falling back to the pack "
          f"join, which cannot recover expressions the pack is already missing. "
          f"Run SMSModForgeActorExtractor.cs and drop the result there.")
    mp_expr = {}  # nodeId -> exprKey
    for d in mp.get("dialogues", []):
        for n in d.get("nodes", []):
            mp_expr[n["id"]] = n.get("expression")
    out = {}
    for e in ext["dialogues"]:
        for n in e.get("dialogue", {}).get("nodes", []):
            idx = prop(n, "value", "m_Acting", "m_Expression")
            actor = prop(n, "value", "m_Acting", "m_Actor", "name")
            ak = actor_key(actor) if actor else None
            ek = mp_expr.get(n["id"])
            if ak and idx is not None and ek:
                out.setdefault(ak, {}).setdefault(str(idx), ek)
    return out


def actor_key(actor_asset):
    if not actor_asset:
        return None
    return re.sub(r'Actor$', '', actor_asset).lower()


def scene_key(field):
    return field[:1].upper() + field[1:] if field else field


def char_to_actor(char):
    """Characters.<field> (anis, anisSwim, kate, doctorFrost) -> actor key."""
    return re.sub(r'(Swim\w*|Shirtless|Slip|Wet|Naked|Topless|Coatless|Underwear|Default).*$', '', char).lower()


def schedule_var(field):
    # anisLocation -> Location_Anis
    m = re.fullmatch(r'(\w+?)Location', field)
    if m:
        return "Location_" + m.group(1)[:1].upper() + m.group(1)[1:]
    return field


def speakers(mp):
    """Every speaker in the pack, in one shape, whichever format it is in.

    The pack used to carry a separate top-level `actors` array. CharacterMerge
    folded it into `characters` and now CLEARS it, so `mp["actors"]` is empty in
    any pack saved by the current editor — which silently emptied every speaker
    lookup here: the actor-key set, the default-bust table, the bust-name
    canonicaliser and (through the actor key) the expression map. That is the
    single biggest source of the drift between this tool's output and the pack.

    Returns dicts of {key, outfits[str], defaultBust}. A character's outfits are
    objects with a gameObjectName; a legacy actor's are bare strings.
    """
    out = []
    for c in mp.get("characters", []) or []:
        key = c.get("key") or ""
        if not key:
            continue
        outfits = []
        for o in c.get("outfits", []) or []:
            nm = o.get("gameObjectName") or o.get("key") if isinstance(o, dict) else o
            if nm:
                outfits.append(nm)
        out.append({"key": key,
                    "outfits": outfits,
                    "defaultBust": c.get("defaultOutfit") or (outfits[0] if outfits else "")})
    if out:
        return out
    # Pre-merge pack: fall back to the old array so an old input still converts.
    for a in mp.get("actors", []) or []:
        out.append({"key": a.get("key", ""),
                    "outfits": [n for n in (a.get("outfits") or []) if n],
                    "defaultBust": a.get("defaultBustKey", "")})
    return out


def scaffold_mapping(ext, mp, ms):
    if os.path.exists(MAPPING):
        return json.load(open(MAPPING, encoding="utf-8"))

    actors = {a["key"] for a in speakers(mp)}
    scenes = {s["key"] for s in mp.get("scenes", [])}
    variables = {v["name"] for v in mp.get("variables", [])}
    sfx = mp.get("sfx", [])

    # distinct actor assets
    actor_assets = set()
    clips = set()
    for e in ext["dialogues"]:
        for n in e.get("dialogue", {}).get("nodes", []):
            a = prop(n, "value", "m_Acting", "m_Actor", "name")
            if a:
                actor_assets.add(a)
        s = json.dumps(e)
        for cm in re.finditer(r'"name":\s*"([^"]+)",\s*"type":\s*"AudioClip"', s):
            clips.add(cm.group(1))

    m = {
        "_doc": "Edit values; re-run convert. 'TODO' = needs your input. Keys auto-derived where possible.",
        "actorAsset": {a: (actor_key(a) if actor_key(a) in actors else "TODO:" + actor_key(a))
                       for a in sorted(actor_assets)},
        "expressionIndexByActor": derive_expression_map(ext, mp),
        "audioClip": {c: (next((s["key"] for s in sfx
                                if s["key"].lower() == re.sub(r'\W', '', c).lower()
                                or s.get("displayName", "").lower() == c.lower()), "TODO")) for c in sorted(clips)},
        "signalField": signal_field_map(ms),
        "sceneObjectNote": "Scene fields map to <Field>[0].upper()+rest; only TODO entries below need attention.",
        "variableNote": "Raw SaveManager keys are used as-is; undeclared ones are flagged in the report.",
    }
    json.dump(m, open(MAPPING, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    print("scaffolded mapping.json (review the TODO entries)")
    return m


# ── op -> ModForge action ─────────────────────────────────────────────────
def op_to_actions(op, dlg, mapping, refs, node_ctx=None):
    """Return a list of ModForge action dicts (may be empty); flag uncertainties.
    A bust change to the node's own speaking actor is routed to node_ctx['outfit']
    (the idiomatic ModForge per-node outfit) instead of an action."""
    o = op.get("op")
    if o == "scene":
        key = scene_key(op["field"])
        if key not in refs["scenes"]:
            flag("scene", dlg, f"scene '{key}' (from {op['field']}) not in pack scenes")
        return [{"type": "SetGameObjectActive",
                 "params": {"kind": "Scene", "target": key, "active": "true" if op["active"] else "false"}}]
    if o == "leaveBust":
        return [{"type": "LeaveBust", "params": {"actor": char_to_actor(op["char"])}}]
    if o == "bustActive":
        go = refs["bust"].get(op["char"])
        if not go:
            flag("bust", dlg, f"bustActive '{op['char']}' has no bust GO in Characters.cs")
            go = op["char"]
        actor = char_to_actor(op["char"])
        present = bool(node_ctx) and actor in node_ctx.get("dialogue_actors", set())
        if op["active"]:
            # The speaking actor's own swap is a per-node outfit — atomic with
            # the line, and what ApplyNodeVisuals is built around.
            if present and actor == (node_ctx.get("actor") if node_ctx else None):
                node_ctx["outfit"] = go
                return []
            # Everyone else gets an explicit activation, NOT SetActorBust.
            #
            # SetActorBust -> ActorRegistry.SetBust only swaps a bust that is
            # ALREADY on screen: its whole body is guarded on the outgoing bust
            # being active, so on a character who hasn't appeared yet it sets
            # CurrentBustKey and activates nothing. Using it to make someone
            # appear is silently a no-op, which is exactly the "character never
            # shows up" class of bug in the current pack.
            #
            # Tracking the change in bust_changes still propagates it forward to
            # the node outfit of any later line this actor speaks (see
            # assign_outfit), so their CurrentBustKey lands correctly anyway —
            # which is what made SetActorBust redundant rather than necessary.
            if present and node_ctx is not None:
                node_ctx["bust_changes"][actor] = go
            return [{"type": "SetGameObjectActive",
                     "params": {"kind": "Bust", "target": go, "active": "true"}}]
        # deactivation
        if present:
            return [{"type": "DeactivateBust", "params": {"actor": actor}}]
        return [{"type": "SetGameObjectActive",
                 "params": {"kind": "Bust", "target": go, "active": "false"}}]
    if o == "emitSignalField":
        sig = mapping.get("signalField", {}).get(op["field"])
        if not sig:
            flag("signal", dlg, f"unknown signal field {op['field']}")
            return []
        return [{"type": "EmitSignal", "params": {"signal": sig}}]
    if o == "emitSignalDelayed":
        return [{"type": "EmitSignalDelayed",
                 "params": {"signal": op["signal"], "seconds": str(op["seconds"])}}]
    if o == "transitionLevels":
        flag("transition", dlg, f"TransitionLevels {op['fromLevel']}->{op['toLevel']} needs level tokens")
        return [{"type": "TransitionLevels",
                 "params": {"fromLevel": "TODO:" + op["fromLevel"], "toLevel": "TODO:" + op["toLevel"],
                            "signal": op["signal"], "seconds": str(op["seconds"])}}]
    if o == "setVar":
        name = op["name"]
        if name not in refs["variables"]:
            flag("variable", dlg, f"variable '{name}' not declared in pack")
        return [{"type": "SetVariable", "params": {"name": name, "value": str(op["value"])}}]
    if o == "goActive":
        return [{"type": "SetGameObjectActive",
                 "params": {"kind": "Level Overlay", "target": op["child"],
                            "active": "true" if op["active"] else "false"}}]
    if o == "setSchedule":
        return [{"type": "SetVariable",
                 "params": {"name": schedule_var(op["field"]), "value": op["value"]}}]
    if o == "changeBust":
        go = refs["bust"].get(op["toBust"])
        if not go:
            flag("outfit", dlg, f"changeBust to '{op['toBust']}' has no bust GO in Characters.cs")
            go = op["toBust"]
        tgt_actor = char_to_actor(op["toBust"])
        # If it's the node's own speaking actor, that's a per-node outfit switch.
        if node_ctx is not None and tgt_actor == node_ctx.get("actor"):
            node_ctx["outfit"] = go
            return []
        # A non-speaking actor's outfit change. Swap the GOs explicitly — the
        # old one off, the new one on — rather than SetActorBust, which no-ops
        # unless the outgoing bust is already visible. bust_changes carries the
        # new outfit forward onto the node outfit of any later line they speak,
        # so their actor state ends up right without the action.
        prev = (node_ctx or {}).get("bust_changes", {}).get(tgt_actor) or                refs.get("default_bust", {}).get(tgt_actor, "")
        acts = []
        if prev and prev.lower() != (go or "").lower():
            acts.append({"type": "SetGameObjectActive",
                         "params": {"kind": "Bust", "target": prev, "active": "false"}})
        acts.append({"type": "SetGameObjectActive",
                     "params": {"kind": "Bust", "target": go, "active": "true"}})
        if node_ctx is not None:
            node_ctx["bust_changes"][tgt_actor] = go
        return acts
    if o == "endDialogue":
        # No-op: GC2's dialogue ends via the tree (the DialogueFinisher node is
        # always a leaf, verified across all 124), and ModForge runs the same
        # end cleanup (UI fade-back) on natural completion — so an explicit
        # EndDialogue action would only be a redundant Stop().
        return []
    if o == "conditional":
        flag("conditional", dlg, f"branch-dependent op dropped: if({op['cond'][:60]}) -> place manually")
        return []
    if o == "raw":
        flag("raw-op", dlg, op["text"][:90])
        return []
    flag("unknown-op", dlg, str(op))
    return []


# ── voyeur scene art (legacy Scenes.DialogueScenePlayer convention) ─────────
def voyeur_scene_actions(target, which, active, refs, dlg):
    """Scene markers in a voyeur dialogue drive <Target>VoyeurSecretbeachScene0N
    CG GameObjects. `which` is a scene number (1-4) to show, or "all" to hide
    every CG (finisher cleanup)."""
    base = target + "VoyeurSecretbeach"
    acts = []

    def emit(n, on, flag_missing):
        key = f"{base}Scene0{n}"
        if key not in refs["scenes"]:
            if flag_missing:
                flag("scene", dlg, f"voyeur scene '{key}' not in pack scenes")
            return
        acts.append({"type": "SetGameObjectActive",
                     "params": {"kind": "Scene", "target": key, "active": "true" if on else "false"}})

    if which == "all":
        for n in (1, 2, 3, 4):
            emit(n, False, flag_missing=False)
        return acts
    if which == 4:
        emit(1, False, flag_missing=False)   # DialogueScenePlayer hides Scene01 on Scene04
    emit(which, True, flag_missing=True)
    return acts


# ── node instruction -> actions ───────────────────────────────────────────
def instr_to_actions(instr, dlg, asset, node_actor, markers, mapping, refs, node_ctx=None):
    t = instr.get("__type")
    if t == "InstructionGameObjectSetActive":
        go = prop(instr, "m_GameObject", "m_Property", "m_GameObject")
        active = cond_bool(instr.get("m_Active", {}))
        if active is None:
            active = True   # default to activation (all observed are GetBoolValue)
        if not go:
            flag("null-target", dlg, f"SetActive with null target (node actor {node_actor})")
            return []
        marker = go.get("name")
        # SpriteFocus is a genuine toggle — both focus (true) and unfocus (false)
        # are real SetSpriteFocus calls. It focuses the whole cast (no per-actor
        # target), so only the toggle state is emitted.
        if marker == "SpriteFocus":
            return [{"type": "SetSpriteFocus",
                     "params": {"focused": "true" if active else "false"}}]
        if marker == "MouthActivator":
            flag("mouth", dlg, "MouthActivator toggle -> review (talk-anim / branch flag)")
            return []
        # Every other marker only drives its dispatcher action when ACTIVATED.
        # A node deactivating a Scene#/finisher/etc marker is a no-op: the
        # dispatcher resets the marker itself once it sees it active, so the
        # explicit deactivation does nothing.
        if not active:
            return []
        voyeur = node_ctx.get("voyeur") if node_ctx else None
        ops = markers.get(marker)

        # DialogueActivator / DialogueFinisher are generic lifecycle markers, but
        # their blocks can also carry real actions — emit any parsed ops (not just
        # ignore them). DialogueFinisher additionally always closes the dialogue.
        if marker == "DialogueActivator":
            acts = []
            for op in (ops or []):
                acts += op_to_actions(op, dlg, mapping, refs, node_ctx)
            return acts
        if marker == "DialogueFinisher":
            acts = []
            for op in (ops or []):
                acts += op_to_actions(op, dlg, mapping, refs, node_ctx)
            if voyeur:                       # voyeur cleanup: hide the CG art, mark seen
                acts += voyeur_scene_actions(voyeur, "all", False, refs, dlg)
                acts.append({"type": "SetVariable",
                             "params": {"name": "Voyeur_Seen" + voyeur, "value": "true"}})
            # No EndDialogue: the finisher node is a leaf, so the dialogue ends
            # naturally (and runs the same end cleanup) after these run.
            return acts

        # Voyeur scene art: Scene1-4 drive <Target>VoyeurSecretbeachScene0N CGs
        # (the legacy Scenes.DialogueScenePlayer), alongside any bust-change ops.
        msc = re.fullmatch(r"Scene(\d+)", marker or "")
        if voyeur and msc and 1 <= int(msc.group(1)) <= 4:
            acts = voyeur_scene_actions(voyeur, int(msc.group(1)), True, refs, dlg)
            for op in (ops or []):
                acts += op_to_actions(op, dlg, mapping, refs, node_ctx)
            return acts

        # Scene# / gift / affection markers -> dispatcher ops
        if ops is None:
            # OutfitDefault/OutfitSwim are bust-system markers with no dialogue
            # action by design (per project owner) — the only intentional no-op.
            if marker in ("OutfitDefault", "OutfitSwim"):
                return []
            # Everything else with no parsed logic is surfaced, never dropped.
            flag("marker-no-logic", dlg, f"node activates '{marker}' but no dispatcher logic was parsed")
            return []
        acts = []
        for op in ops:
            acts += op_to_actions(op, dlg, mapping, refs, node_ctx)
        return acts
    if t == "InstructionCommonAudioSFXPlay":
        clip = prop(instr, "m_AudioClip", "m_Property", "m_Value", "name")
        key = mapping.get("audioClip", {}).get(clip)
        if not key or key == "TODO":
            flag("sfx", dlg, f"audio clip '{clip}' has no SFX key")
            key = key or "TODO:" + str(clip)
        return [{"type": "PlaySFX", "params": {"clip": key}}]
    if t == "InstructionCommonTimeWait":
        secs = prop(instr, "m_Seconds", "m_Property", "m_Value")
        if secs is None:
            flag("wait", dlg, "Wait: couldn't read m_Seconds -> defaulted to 1")
            secs = 1
        return [{"type": "Wait", "params": {"seconds": str(secs)}}]
    if t == "InstructionLogicRaiseSignal":
        sig = prop(instr, "m_Signal", "m_String")
        return [{"type": "EmitSignal", "params": {"signal": sig}}]
    flag("instr-type", dlg, f"unhandled instruction {t}")
    return []


def _mk_cond(ctype, name, value, neg):
    c = {"type": ctype, "params": {"name": name or "TODO", "value": value}}
    if neg:
        c["negate"] = True
    return c


def node_conditions(value, dlg, refs):
    conds = prop(value, "m_Conditions", "m_Conditions", "m_Conditions") or []
    out = []

    def check_var(name):
        if name and name not in refs["variables"]:
            flag("variable", dlg, f"condition variable '{name}' not declared in pack")

    for c in conds:
        t = c.get("__type", "")
        if t == "ConditionMathCompareBooleans":
            var = cond_var_name(c.get("m_Value"))
            val = cond_bool(c.get("m_CompareTo"))
            comp = c.get("m_Comparison", "Equals")
            if not var:
                flag("condition", dlg, "bool condition: couldn't read variable name")
            check_var(var)
            out.append(_mk_cond("VariableEquals", var,
                                 "true" if val else "false", comp in CMP_NEG))
        elif t == "ConditionTextEquals":
            var = cond_var_name(c.get("m_Text1"))
            val = cond_str(c.get("m_Text2"))
            if not var:
                flag("condition", dlg, "text condition: couldn't read variable name")
            check_var(var)
            out.append(_mk_cond("VariableEquals", var, str(val if val is not None else ""), False))
        elif t == "ConditionMathCompareIntegers":
            var = cond_var_name(c.get("m_Value"))
            comp = prop(c, "m_CompareTo", "m_Comparison") or "Equals"
            val = cond_num(prop(c, "m_CompareTo", "m_CompareTo"))
            if not var:
                flag("condition", dlg, "int condition: couldn't read variable name")
            check_var(var)
            ctype = CMP_TYPE.get(comp)
            if ctype is None:
                flag("condition", dlg, f"int comparison '{comp}' unmapped -> VariableEquals")
                ctype = "VariableEquals"
            v = val if val is not None else 0
            v = int(v) if isinstance(v, float) and v.is_integer() else v
            out.append(_mk_cond(ctype, var, str(v), comp in CMP_NEG))
        elif t == "ConditionGameObjectActive":
            flag("condition", dlg, "ConditionGameObjectActive marker check -> review")
        else:
            flag("condition", dlg, f"unhandled condition {t}")
    return out


KIND = {"NodeTypeText": "Text", "NodeTypeChoice": "Choice", "NodeTypeRandom": "Random"}


def convert():
    ext, mp, ml, ms = load()
    mapping = scaffold_mapping(ext, mp, ms)
    refs = {"scenes": {s["key"] for s in mp.get("scenes", [])},
            "variables": {v["name"] for v in mp.get("variables", [])},
            "actors": {a["key"] for a in speakers(mp)},
            "bust": load_bust_map()}
    # Canonicalize Characters.cs bust GO names against the modpack's actual outfit
    # GameObjectNames (case-insensitive), so e.g. CreateNewBust("centiSwimShirtless")
    # emits the real "CentiSwimShirtless" the pack declares — not the raw lowercase
    # name, which the editor/runtime wouldn't find art for.
    bust_canon = {}
    for a in speakers(mp):
        for nm in [a["defaultBust"]] + list(a["outfits"]):
            if nm:
                bust_canon[nm.lower()] = nm
    refs["bust"] = {k: bust_canon.get((v or "").lower(), v) for k, v in refs["bust"].items()}
    default_bust = {a["key"]: a["defaultBust"] for a in speakers(mp)}
    refs["default_bust"] = default_bust
    # existing startConditions, reused (gating not re-derived; cross-checked in report)
    existing_sc = {d["key"]: d.get("startConditions", []) for d in mp.get("dialogues", [])}
    # The Actor-asset extract wins over whatever mapping.json froze on a previous
    # run — otherwise a stale expressionIndexByActor would keep reproducing the
    # gaps it was scaffolded from, and fixing them would need mapping.json deleted.
    expr_map = expression_map_from_actor_extract() or mapping.get("expressionIndexByActor", {})
    # roomTalk is derived from each dialogue's LevelActive level token.
    # lowercased key -> canonical pack place key (validator is case-sensitive).
    pack_place_by_lc = {p["key"].lower(): p["key"] for p in mp.get("places", [])}
    vrt_canon = load_vanilla_roomtalks()
    # Dialogues the dispatcher queued (StartDialogueSequenceQueue / ...Delayed) —
    # started without the FadeUI cinematic fade. Map dispatcher field -> asset.
    dlg_src = open(os.path.join(HERE, "_input", "Dialogues_pre_modforge.cs"), encoding="utf-8").read()
    field_to_asset = {m.group(1): m.group(2)
                      for m in re.finditer(r'(\w+)\s*=\s*CreateNewDialogue\("([^"]+)"', dlg_src)}
    queued = {field_to_asset[f]
              for f in re.findall(r'StartDialogueSequence(?:Queue|Delayed)\(Dialogues\.(\w+)', ms)
              if f in field_to_asset}

    out = []
    for e in ext["dialogues"]:
        asset = e["assetName"]
        if asset in TEMPLATES:
            continue
        dlg = asset
        entry = ml.get(asset, {})
        markers = entry.get("markers", {})
        # Voyeur dialogue? (<Char>DialogueSecretbeach01 with matching CG art) —
        # its Scene markers drive <Char>VoyeurSecretbeach CGs (DialogueScenePlayer).
        voyeur_target = None
        if asset.endswith("DialogueSecretbeach01"):
            t = asset[:-len("DialogueSecretbeach01")]
            if (t + "VoyeurSecretbeachScene01") in refs["scenes"]:
                voyeur_target = t
        if not markers and not voyeur_target and any("Scene" in json.dumps(n) for n in e["dialogue"].get("nodes", [])):
            flag("dialogue-no-logic", dlg, "references Scene markers but no dispatcher logic parsed")
        if asset not in existing_sc:
            flag("startconditions", dlg, "no existing startConditions to reuse; gating: " + str(entry.get("gating")))

        # Actors that appear (speak) anywhere in this dialogue — bust activations
        # for these become outfit swaps rather than overlaid GameObjects.
        dialogue_actors = set()
        for n in e["dialogue"].get("nodes", []):
            aa = prop(n, "value", "m_Acting", "m_Actor", "name")
            ak2 = mapping.get("actorAsset", {}).get(aa) or actor_key(aa)
            if ak2:
                dialogue_actors.add(ak2)

        nodes_out = []
        node_by_id = {}     # id -> (output dict, actor, outfit_override)
        for n in e["dialogue"].get("nodes", []):
            v = n.get("value", {})
            ntype = prop(v, "m_NodeType", "__type")
            kind = KIND.get(ntype, "Text")
            if ntype not in KIND:
                flag("nodetype", dlg, f"unknown node type {ntype}")
            actor_asset = prop(v, "m_Acting", "m_Actor", "name")
            ak = mapping.get("actorAsset", {}).get(actor_asset) or actor_key(actor_asset)
            if ak and ak.startswith("TODO"):
                flag("actor", dlg, f"actor asset {actor_asset} unmapped")
            idx = prop(v, "m_Acting", "m_Expression")
            # index 0 is the neutral expression for every actor; otherwise try the
            # mapped actor key, then the raw-derived key (the scaffold keyed
            # expressionIndexByActor by the raw key before mapping overrides).
            # Index 0 used to be hardcoded to "neutral", which threw away
            # whatever GC2 actually had there: it never consulted the map, and
            # "neutral" isn't a key the runtime resolves — RouteExpression looks
            # for an Expressions child by that name, finds none, and activates
            # nothing. That is indistinguishable from the empty string, so any
            # real expression sitting at index 0 was silently lost. Consult the
            # map for every index; an unmapped 0 falls back to "" (genuinely no
            # expression), which is what the old value meant in practice.
            ek = (expr_map.get(ak, {}).get(str(idx))
                  or expr_map.get(actor_key(actor_asset), {}).get(str(idx)))
            if ek is None:
                if idx in (0, None):
                    ek = ""
                else:
                    flag("expression", dlg, f"actor {ak} expression index {idx} unmapped")
                    ek = f"TODO:{idx}"
            text = get_text(v.get("m_Text", {})) or ""

            node_ctx = {"actor": ak, "outfit": None, "voyeur": voyeur_target,
                        "dialogue_actors": dialogue_actors, "bust_changes": {}}
            on_start, on_finish = [], []
            for instr in (prop(v, "m_OnStart", "m_Instructions", "m_Instructions") or []):
                on_start += instr_to_actions(instr, dlg, asset, ak, markers, mapping, refs, node_ctx)
            for instr in (prop(v, "m_OnFinish", "m_Instructions", "m_Instructions") or []):
                on_finish += instr_to_actions(instr, dlg, asset, ak, markers, mapping, refs, node_ctx)

            # Node-box fields, in the model's JSON order. Optional ones (tag,
            # jump, duration, timeout) are emitted only when non-default, matching
            # the editor's NullValueHandling.Ignore / ShouldSerialize* behaviour.
            nd = {
                "id": n["id"], "kind": kind, "actor": ak or "", "expression": ek or "",
                "outfit": "",   # filled by the tree pass below
                "text": text,
            }
            tagval = prop(v, "m_Tag", "m_String")
            if tagval:
                nd["tag"] = tagval
            nd["conditions"] = node_conditions(v, dlg, refs)
            nd["actionsOnStart"] = on_start
            nd["actionsOnFinish"] = on_finish
            nd["children"] = n.get("children", [])

            # Jump: Continue is the default (no `jump` block); Exit / Jump are emitted.
            jmode = prop(v, "m_Jump", "m_Jump")
            if jmode and jmode != "Continue":
                if jmode not in ("Exit", "Jump"):
                    flag("jump", dlg, f"unknown jump mode '{jmode}' on node {n['id']}")
                jd = {"mode": jmode}
                jto = prop(v, "m_Jump", "m_JumpTo", "m_String")
                if jto:
                    jd["targetTag"] = jto
                nd["jump"] = jd

            # Duration: only Timeout is non-default; carry its seconds too.
            if prop(v, "m_Duration") == "Timeout":
                nd["duration"] = "Timeout"
                secs = prop(v, "m_Timeout", "m_Property", "m_Value")
                nd["timeout"] = secs if secs is not None else 3

            node_by_id[n["id"]] = (nd, ak, node_ctx["outfit"], node_ctx["bust_changes"])
            nodes_out.append(nd)

        # Each actor's *initial* bust = the root of its changeBust chain (a
        # fromBust that's never a toBust), so a dialogue that starts mid-outfit
        # (voyeur scenes begin in the swim bust the picker set) opens correctly
        # instead of on the actor's clothed default.
        froms, tos = {}, set()
        for mk_ops in markers.values():
            for op in mk_ops:
                if op.get("op") == "changeBust":
                    froms[op["fromBust"]] = True
                    tos.add(op["toBust"])
        initial_bust = {}
        for fb in froms:
            if fb not in tos:
                go = refs["bust"].get(fb)
                if go:
                    initial_bust[char_to_actor(fb)] = go

        # Per-node outfit: walk the actual tree (roots -> children) so the
        # speaking actor's bust persists from its initial until a changeBust /
        # Outfit marker switches it. (The extract's node list isn't tree-ordered,
        # so a flat pass would assign outfits out of sequence.)
        # Outfit persists "until changed again": one shared current_bust threads
        # across the sequential roots and down sequence children, mutating in
        # place. It's copied only when descending a Choice/Random node's children
        # (real branches), so alternatives don't leak into each other.
        ordered = []   # node dicts in tree (DFS) order, for a readable node list
        def assign_outfit(nid, cb, seen):
            if nid in seen or nid not in node_by_id:
                return
            seen.add(nid)
            nd, ak, override, bust_changes = node_by_id[nid]
            ordered.append(nd)
            if ak and ak not in cb:
                cb[ak] = initial_bust.get(ak) or default_bust.get(ak, "")
            if override:
                cb[ak] = override
            nd["outfit"] = cb.get(ak, "") if ak else ""
            # Non-speaking actors' outfit changes (SetActorBust) persist forward
            # too, so a later line where they speak keeps the new outfit.
            for other_actor, other_bust in bust_changes.items():
                cb[other_actor] = other_bust
            branch = nd["kind"] in ("Choice", "Random")
            for c in nd["children"]:
                assign_outfit(c, dict(cb) if branch else cb, seen)
        seen = set()
        shared_bust = {}
        for r in e["dialogue"].get("rootIds", []):
            assign_outfit(r, shared_bust, seen)
        # any node not reachable from a root still gets a sensible default, and is
        # appended after the tree-ordered ones (in original order).
        for nd in nodes_out:
            nid = nd["id"]
            if nid not in seen:
                _, ak, override, _bc = node_by_id[nid]
                nd["outfit"] = override or (default_bust.get(ak, "") if ak else "")
                ordered.append(nd)

        # roomTalk: derive from the LevelActive start condition. Locations that
        # don't map cleanly (forest/temple/villa story spots, HH for Anis) are
        # flagged for manual entry rather than guessed.
        level = next((c.get("params", {}).get("level")
                      for c in existing_sc.get(asset, [])
                      if c.get("type") == "LevelActive"), None)
        room_talk = derive_roomtalk(level, pack_place_by_lc, vrt_canon)
        if not room_talk:
            flag("roomtalk", dlg, f"couldn't derive roomTalk from level {level!r} — set manually")
            room_talk = ""

        d_out = {"key": asset, "displayName": asset, "roomTalk": room_talk}
        # Queued-on-arrival: dialogues the dispatcher started via
        # StartDialogueSequenceQueue / ...Delayed (no FadeUI fade). Emitted
        # only when true (the editor omits the default).
        if asset in queued:
            d_out["queued"] = True
        d_out["startConditions"] = existing_sc.get(asset, [])
        d_out["nodes"] = ordered   # tree order, so the editor list reads in play order
        d_out["rootNodeIds"] = e["dialogue"].get("rootIds", [])
        out.append(d_out)

    json.dump(out, open(OUT, "w", encoding="utf-8"), indent=1, ensure_ascii=False)
    write_report(out)
    print(f"converted {len(out)} dialogues -> {OUT}")
    print(f"coverage flags: {len(flags)} -> {REPORT}")


def write_report(out):
    by_cat = {}
    for cat, dlg, detail in flags:
        by_cat.setdefault(cat, []).append((dlg, detail))
    lines = ["# Conversion coverage report", "",
             f"Dialogues converted: **{len(out)}**  |  total flags: **{len(flags)}**", "",
             "Every item below was emitted with a `TODO`/placeholder or skipped — review each in the editor.", ""]
    for cat in sorted(by_cat):
        items = by_cat[cat]
        lines.append(f"## {cat} ({len(items)})")
        for dlg, detail in items[:200]:
            lines.append(f"- **{dlg}**: {detail}")
        if len(items) > 200:
            lines.append(f"- … and {len(items) - 200} more")
        lines.append("")
    open(REPORT, "w", encoding="utf-8").write("\n".join(lines))


if __name__ == "__main__":
    convert()
