"""Turn the runtime dialogue extraction into the catalog the editor ships.

WHY A SEPARATE FILE FROM THE EXTRACTION
    The extraction is ~50 MB across 722 dialogues, and almost none of that is
    dialogue. Game Creator stores one line of text as a Node holding a NodeText
    holding a PropertyGetString holding a GetStringTextArea holding a
    TextAreaField holding the string; a boolean condition is six objects deep
    before it names the variable. Collapsing those chains is most of the size,
    and it is also what turns the dump into something an editor can bind to.

WHAT IS DROPPED, AND WHY THAT IS SAFE
    Only state the running game owns rather than the author: m_Sequence (a live
    coroutine timer, whose Time is whatever the clock read when the extraction
    ran), m_Visits (which nodes this save has seen), m_Dirty, and the content's
    m_Time. None of it survives a restart, so none of it is a default a pack
    could sensibly extend.

WHAT IS KEPT THAT ISN'T MODELLED
    Everything. A construct this file has no shape for is kept as
    {"kind": "raw", ...} carrying its type, Game Creator's own one-line Title
    for it, and its pruned fields. That is the difference between "the editor
    cannot edit this" and "the editor cannot show this" - the first is a
    limitation, the second is data loss.

NODE IDS
    The keys are Game Creator's own node ids, and they are stable across
    restarts - two extractions taken in separate sessions produced the same ids
    for the same nodes. That is what lets a pack's change bind to a node rather
    than to a position in a list.

USAGE
    python BuildDialogueCatalog.py <dump.json> -o <catalog dir>

    writing <catalog dir>/index.json - all a picker needs - beside
    <catalog dir>/Dialogues/<flattened path>.json, one per conversation, so
    opening one does not cost all 722.
"""

from __future__ import annotations

import argparse
import io
import json
import os
import sys
from collections import Counter, OrderedDict

# Who the game's actors are, and what faces they have. Filled by main when an
# actor extraction is given; empty otherwise, and then a node keeps the bare
# number it has always had.
ACTORS = {}

# What the game CALLS each of them, where that differs from the asset's name:
# the actor filed as "DrFrost" says "Doctor Frost" above her lines. Filled from
# the same extraction, and empty without it.
ACTOR_NAMES = {}

# Live state of a running dialogue, not the dialogue.
TRANSIENT = ("m_Sequence", "m_Visits", "m_Dirty", "m_Time")

# The three fields that hold a list Game Creator shares between the nodes of
# one dialogue. See Dump.resolve for why they are named here.
SHARED_LIST_FIELDS = ("m_Instructions", "m_Values", "m_Conditions")


class Dump:
    """The extraction, with its object graph put back together.

    The dumper writes each object once with an "$id" and points at it with
    {"$ref": n} afterwards, so a shared subgraph is written once instead of
    multiplying. Reading it means indexing the ids first.
    """

    def __init__(self, root):
        self.table = {}
        self.dangling = Counter()
        self._index(root)

    def _index(self, node):
        if isinstance(node, dict):
            if "$id" in node:
                self.table[node["$id"]] = node
            for value in node.values():
                self._index(value)
        elif isinstance(node, list):
            for value in node:
                self._index(value)

    def resolve(self, value, holder=None):
        """One value, followed through a "$ref" and out of an "$items" wrapper."""
        if isinstance(value, dict) and "$ref" in value and len(value) == 1:
            target = self.table.get(value["$ref"])
            if target is None:
                # A dump taken before lists carried an id of their own. The id
                # went into the dumper's table and nothing printed it, so every
                # repeat of a shared list points at a number appearing nowhere.
                # It is recoverable, and only here: the arithmetic is exact
                # across all 722 dialogues - m_Instructions 720 inline empty +
                # 3,775 inline full + 34,811 refs = 39,306 = two per node,
                # m_Values 720 + 2 + 18,931 = 19,653 = one per node - and no
                # dialogue holds more than one inline empty list for a field.
                # So the shared instance is that dialogue's EMPTY list, always.
                self.dangling[holder] += 1
                if holder not in SHARED_LIST_FIELDS:
                    raise SystemExit(
                        "a $ref held in %r resolves to nothing, and only the "
                        "three shared list fields are known to do that. Re-run "
                        "the extraction with a build that gives lists an id."
                        % (holder,))
                return []
            value = target
        if isinstance(value, dict) and "$items" in value:
            return value["$items"]
        return value

    def field(self, node, *path):
        """A value some levels down, following refs at every step."""
        current = self.resolve(node)
        for key in path:
            if not isinstance(current, dict):
                return None
            current = self.resolve(current.get(key), key)
        return current

    def items(self, node, *path):
        """The same, when a list is wanted and absence means empty."""
        got = self.field(node, *path)
        return got if isinstance(got, list) else []

    def prune(self, value, holder=None):
        """A value with the dump's bookkeeping and the game's live state gone,
        for constructs kept whole because nothing here models them."""
        value = self.resolve(value, holder)
        if isinstance(value, dict):
            return OrderedDict(
                (key, self.prune(inner, key))
                for key, inner in value.items()
                if key != "$id" and key not in TRANSIENT)
        if isinstance(value, list):
            return [self.prune(inner, holder) for inner in value]
        return value


def unity(value):
    """The name of a scene or asset object the dump referred to rather than
    followed. None when the field was empty."""
    if isinstance(value, dict) and "$unity" in value:
        return value["$unity"]
    return None


def located(value):
    """Where that object sits in the scene, when it is in one.

    A name does not identify anything on its own - this game has 35 dialogue
    names shared by more than one dialogue - and it is the path that a
    condition like "is this object active" has to be written against.
    """
    if isinstance(value, dict):
        return value.get("$path")
    return None


class Reader:
    """Game Creator's value chains, read down to what they say.

    Every one of these is a PropertyGetX wrapping a GetXSomething wrapping the
    actual source, and the shape of the innermost object is the only thing that
    says whether a number is a literal, a variable, or a pick from a list.
    """

    # Literal values, and the field each type keeps its literal in.
    LITERALS = {
        "GetBoolValue": "m_Value",
        "GetDecimalDecimal": "m_Value",
        "GetDecimalInteger": "m_Value",
        "GetStringString": "m_Value",
    }

    # Variables, whatever they hold.
    VARIABLES = (
        "GetBoolGlobalName", "GetDecimalGlobalName", "GetStringGlobalName",
        "GetGameObjectGlobalName", "GetBoolGlobalList", "GetDecimalGlobalList",
        "GetStringGlobalList", "GetGameObjectGlobalList", "GetStringLocalList",
        "GetDecimalLocalList", "GetBoolLocalList", "GetGameObjectLocalList",
        "SetBoolGlobalName", "SetNumberGlobalName", "SetStringGlobalName",
        "SetGameObjectGlobalName", "SetBoolGlobalList", "SetNumberGlobalList",
        "SetStringGlobalList", "SetGameObjectGlobalList",
    )

    # Scene and asset references, and the field each keeps its object in.
    OBJECTS = {
        "GetGameObjectInstance": "m_GameObject",
        "GetGameObjectActions": "m_Actions",
        "GetGameObjectConditions": "m_Conditions",
        "GetGameObjectDialogue": "m_Dialogue",
        "GetGameObjectTransform": "m_Transform",
        "GetAudioClip": "m_Value",
        "GetQuestInstance": "m_Quest",
        "GetAttributeInstance": "m_Attribute",
    }

    def __init__(self, dump):
        self.d = dump
        self.unmodelled = Counter()
        self.modelled = Counter()

    # -- addresses ----------------------------------------------------

    def variable(self, field):
        """A variable, addressed the way the editor addresses one: by name,
        with its list kept for display."""
        field = self.d.resolve(field)
        if not isinstance(field, dict):
            return None
        kind = field.get("$type", "")

        if kind in ("FieldGetGlobalName", "FieldSetGlobalName"):
            return OrderedDict((
                ("scope", "global"),
                ("name", self.d.field(field, "m_Name", "m_String")),
                ("list", unity(self.d.field(field, "m_Variable"))),
                ("type", self.d.field(field, "m_TypeID", "m_String")),
            ))
        if kind in ("FieldGetGlobalList", "FieldSetGlobalList"):
            return OrderedDict((
                ("scope", "globalList"),
                ("list", unity(self.d.field(field, "m_Variable"))),
                ("pick", self.pick(self.d.field(field, "m_Select"))),
                ("type", self.d.field(field, "m_TypeID", "m_String")),
            ))
        if kind in ("FieldGetLocalList", "FieldSetLocalList"):
            return OrderedDict((
                ("scope", "localList"),
                ("on", self.value(self.d.field(field, "m_Variable"))),
                ("pick", self.pick(self.d.field(field, "m_Select"))),
                ("type", self.d.field(field, "m_TypeID", "m_String")),
            ))

        self.unmodelled["variable:" + kind] += 1
        return self.raw(field)

    def pick(self, select):
        """Which entry of a list variable: the first, the last, a random one."""
        kind = (self.d.resolve(select) or {}).get("$type", "")
        return {"GetPickFirst": "first",
                "GetPickLast": "last",
                "GetPickRandom": "random"}.get(kind, kind)

    # -- values -------------------------------------------------------

    def value(self, prop):
        """A PropertyGetX / PropertySetX, read down to a literal or an address.

        Tagged with "kind": "value" for a literal, "variable" for a variable,
        "object" for a scene or asset reference, "none" for an empty slot, and
        "raw" for anything with no shape here.
        """
        prop = self.d.resolve(prop)
        if prop is None:
            return {"kind": "none"}

        inner = self.d.field(prop, "m_Property")
        if inner is None:
            inner = prop
        if not isinstance(inner, dict):
            return {"kind": "value", "value": inner}

        kind = inner.get("$type", "")
        self.modelled[kind] += 1

        if kind in self.LITERALS:
            return {"kind": "value",
                    "value": self.d.field(inner, self.LITERALS[kind])}
        if kind == "GetBoolTrue":
            return {"kind": "value", "value": True}
        if kind == "GetBoolFalse":
            return {"kind": "value", "value": False}
        if kind == "GetStringEmpty":
            return {"kind": "value", "value": ""}
        if kind == "GetStringTextArea":
            return {"kind": "value", "value": self.d.field(inner, "m_Text", "Text")}

        if kind in self.VARIABLES:
            return {"kind": "variable",
                    "variable": self.variable(self.d.field(inner, "m_Variable"))}

        if kind in self.OBJECTS:
            target = self.d.field(inner, self.OBJECTS[kind])
            found = OrderedDict((
                ("kind", "object"),
                ("name", unity(target)),
                ("of", kind),
            ))
            where = located(target)
            if where:
                found["path"] = where
            return found

        # An empty slot says so in its own type name.
        if kind.endswith("None"):
            return {"kind": "none"}

        # A named colour carries its name; a literal one carries its channels.
        if kind == "GetColorValue":
            return {"kind": "value",
                    "value": self.d.prune(self.d.field(inner, "m_Value"))}
        if kind.startswith("GetColorColors"):
            return OrderedDict((("kind", "value"),
                                ("value", kind[len("GetColorColors"):]),
                                ("of", kind)))

        # The player, and anything else that is an address with no name.
        if "Characters" in kind:
            return OrderedDict((("kind", "object"), ("name", None), ("of", kind)))

        self.modelled[kind] -= 1
        self.unmodelled["value:" + kind] += 1
        return self.raw(inner)

    def raw(self, node):
        """A construct kept whole because nothing here models it, with Game
        Creator's own description of it when it offers one."""
        node = self.d.resolve(node)
        if not isinstance(node, dict):
            return {"kind": "raw", "value": node}

        out = OrderedDict((("kind", "raw"), ("type", node.get("$type"))))
        if node.get("$title"):
            out["title"] = node["$title"]
        out["data"] = OrderedDict(
            (key, self.d.prune(inner, key))
            for key, inner in node.items()
            if key not in ("$id", "$type", "$title") and key not in TRANSIENT)
        return out


# Instruction and condition types whose fields have been read from the real
# extraction and understood. The name is what the editor keys its own model on;
# everything NOT here is still captured in full, just without a name for what
# it means. Adding one is a two-line change once its shape is confirmed.
KNOWN_INSTRUCTIONS = {
    "InstructionGameObjectSetActive": "setActive",
    "InstructionBooleanSetBool": "setBool",
    "InstructionArithmeticIncrementNumber": "incrementNumber",
    "InstructionArithmeticSetNumber": "setNumber",
    "InstructionCommonTimeWait": "wait",
    "InstructionLogicRaiseSignal": "raiseSignal",
    "InstructionLogicRunActions": "runActions",
    "InstructionLogicRunConditions": "runConditions",
    "InstructionLogicCheckConditions": "checkConditions",
    "InstructionTextSetString": "setString",
    "InstructionCommonAudioSFXPlay": "playSfx",
    "InstructionQuestsTaskComplete": "questTaskComplete",
    "InstructionDialoguePlay": "playDialogue",
}

KNOWN_CONDITIONS = {
    "ConditionMathCompareBooleans": "compareBool",
    "ConditionMathCompareIntegers": "compareNumber",
    "ConditionChance": "chance",
    "ConditionGameObjectActive": "gameObjectActive",
}


class Steps(Reader):
    """The conditions and instructions hanging off a node.

    Every one is captured whole, with its value chains read down to literals
    and addresses. Only the naming is selective: a type in KNOWN_* gets a kind
    the editor can model, and one that isn't gets the same fields plus Game
    Creator's own Title, which is what the game itself draws on the block.
    That way an unmodelled construct is something the editor cannot EDIT
    rather than something it cannot SHOW.
    """

    def slot(self, value, holder=None):
        """One field of a step, with anything addressable resolved."""
        value = self.d.resolve(value, holder)

        if isinstance(value, dict):
            kind = value.get("$type", "")
            if kind.startswith("PropertyGet") or kind.startswith("PropertySet"):
                return self.value(value)
            if kind == "ConditionList":
                return {"conditions": [self.condition(c)
                                       for c in self.d.items(value, "m_Conditions")]}
            if kind == "InstructionList":
                return {"instructions": [self.instruction(i)
                                         for i in self.d.items(value, "m_Instructions")]}
            if "$unity" in value:
                found = OrderedDict((("kind", "object"),
                                     ("name", value["$unity"]),
                                     ("of", kind)))
                if value.get("$path"):
                    found["path"] = value["$path"]
                return found
            return OrderedDict(
                (key, self.slot(inner, key))
                for key, inner in value.items()
                if key not in ("$id", "$type", "$title") and key not in TRANSIENT)

        if isinstance(value, list):
            return [self.slot(inner, holder) for inner in value]
        return value

    def step(self, node, known):
        """A condition or an instruction: its type, what it says it does, and
        every field it carries."""
        node = self.d.resolve(node)
        if not isinstance(node, dict):
            return {"kind": "raw", "value": node}

        kind = node.get("$type", "")
        out = OrderedDict()
        if kind in known:
            out["kind"] = known[kind]
            self.modelled[kind] += 1
        else:
            self.unmodelled[kind] += 1
        out["type"] = kind
        if node.get("$title"):
            out["title"] = node["$title"]

        fields = OrderedDict(
            (key, self.slot(inner, key))
            for key, inner in node.items()
            if key not in ("$id", "$type", "$title") and key not in TRANSIENT)
        if fields:
            out["fields"] = fields
        return out

    def condition(self, node):
        return self.step(node, KNOWN_CONDITIONS)

    def instruction(self, node):
        return self.step(node, KNOWN_INSTRUCTIONS)


def build_node(steps, dump, node, tree):
    """One node of a dialogue: what it says, who says it, and what it does."""
    out = OrderedDict()

    node_type = dump.field(node, "m_NodeType") or {}
    kind = node_type.get("$type", "")
    out["kind"] = {"NodeTypeText": "text",
                   "NodeTypeChoice": "choice",
                   "NodeTypeRandom": "random"}.get(kind, kind)

    out["parent"] = tree.get("m_Parent", -1)
    # Read through the dump rather than off the dict: a list arrives wrapped as
    # {"$id": n, "$items": [...]}, and list() over that wrapper yields its two
    # KEYS, which then read as two children no dialogue has.
    children = dump.items(tree, "m_Children")
    if children:
        out["children"] = list(children)

    acting = dump.field(node, "m_Acting") or {}
    out["actor"] = unity(dump.resolve(acting.get("m_Actor")))
    portrait = acting.get("m_Portrait")
    if portrait and portrait != "ActorDefault":
        out["portrait"] = portrait
    # Every node picks an expression, and nought is a choice like any other -
    # it is the actor's FIRST face, called "neutral" for most of the cast, and
    # choosing it runs that expression's own instructions. Treated as "no
    # expression" because nought is falsy, 16,510 lines showed an empty box
    # where the game plainly has a face.
    index = acting.get("m_Expression") or 0
    if index:
        out["expression"] = index          # the raw fact; nought is the default

    faces = ACTORS.get(unity(dump.resolve(acting.get("m_Actor"))) or "")
    if faces and 0 <= index < len(faces) and faces[index]:
        out["expressionName"] = faces[index]

    text = steps.value(dump.field(node, "m_Text", "m_Text"))
    out["text"] = text.get("value") if text.get("kind") == "value" else text

    values = dump.items(node, "m_Text", "m_Values")
    if values:
        out["values"] = [steps.slot(v) for v in values]

    audio = steps.value(dump.field(node, "m_Audio"))
    if audio.get("kind") != "none":
        out["audio"] = audio

    animation = dump.field(node, "m_Animation")
    if animation is not None:
        out["animation"] = steps.slot(animation)

    duration = dump.field(node, "m_Duration")
    if duration != "UntilInteraction":
        out["duration"] = duration
        timeout = steps.value(dump.field(node, "m_Timeout"))
        out["timeout"] = timeout.get("value") if timeout.get("kind") == "value" else timeout

    tag = dump.field(node, "m_Tag", "m_String")
    if tag:
        out["tag"] = tag

    jump = dump.field(node, "m_Jump") or {}
    mode = jump.get("m_Jump")
    if mode and mode != "Continue":
        target = dump.field(jump, "m_JumpTo", "m_String")
        out["jump"] = OrderedDict((("mode", mode), ("to", target))) if target \
            else OrderedDict((("mode", mode),))

    # Whatever the node type itself carries - a choice's timer, a random
    # node's repeat rule - minus the type name already recorded as "kind".
    settings = OrderedDict(
        (key, steps.slot(value, key))
        for key, value in node_type.items()
        if key not in ("$id", "$type", "$title") and key not in TRANSIENT)
    if settings:
        out["settings"] = settings

    conditions = [steps.condition(c) for c in
                  dump.items(node, "m_Conditions", "m_Conditions", "m_Conditions")]
    if conditions:
        out["conditions"] = conditions

    for where, name in (("m_OnStart", "onStart"), ("m_OnFinish", "onFinish")):
        run = [steps.instruction(i) for i in
               dump.items(node, where, "m_Instructions", "m_Instructions")]
        if run:
            out[name] = run

    return out


def build_dialogue(steps, dump, record):
    """One dialogue, keyed on Game Creator's own node ids."""
    fields = record["fields"]
    content = dump.field(fields, "m_Story", "m_Content")
    if content is None:
        return None

    path = record["object"]
    out = OrderedDict()
    out["id"] = path
    out["name"] = path.rsplit("/", 1)[-1]
    if record.get("index") is not None:
        out["index"] = record["index"]
    out["skin"] = unity(dump.field(content, "m_DialogueSkin"))
    out["roles"] = [unity(dump.field(r, "m_Actor"))
                    for r in dump.items(content, "m_Roles")]
    out["roots"] = list(dump.field(content, "m_Roots") or [])

    trees = {}
    for entry in dump.items(content, "m_Nodes"):
        value = dump.field(entry, "Value") or {}
        trees[entry.get("Key")] = value

    nodes = OrderedDict()
    for entry in dump.items(content, "m_Data"):
        key = entry.get("Key")
        node = dump.field(entry, "Value", "m_Value")
        if node is None:
            continue
        nodes[str(key)] = build_node(steps, dump, node, trees.get(key, {}))
    out["nodes"] = nodes
    return out


def build(dump_path):
    with io.open(dump_path, "r", encoding="utf-8-sig") as handle:
        raw = json.load(handle)

    catalog = OrderedDict()
    catalog["version"] = 1
    catalog["source"] = os.path.basename(dump_path)
    dialogues = []

    every = Counter()
    for record in raw.get("objects", []):
        dump = Dump(record["fields"])
        steps = Steps(dump)
        built = build_dialogue(steps, dump, record)
        if built is None:
            continue
        dialogues.append(built)
        every.update(steps.unmodelled)
        every.update({"@modelled:" + k: v for k, v in steps.modelled.items()})
        every.update({"@dangling:" + str(k): v for k, v in dump.dangling.items()})

    dialogues.sort(key=lambda d: d["id"])
    catalog["dialogues"] = dialogues
    return catalog, every


class Starts(Steps):
    """
    Where a conversation is played from, and what has to be true first.

    A dialogue holds only what it says. Whether it plays at all is decided
    somewhere else: a room carries a Conditions component whose branches each
    gate one conversation, and the branch that names this dialogue is, in the
    only sense that matters to an author, its start condition.

    Gates are recognised by TYPE rather than by field name - anything holding
    both a ConditionList and an InstructionList is one - so a Branch, a
    Trigger, a button and an Actions asset all read the same way without a
    table of shapes per component.
    """

    def gate(self, value):
        """The conditions guarding everything inside this object, or None if it
        guards nothing."""
        conditions = instructions = None
        for key, inner in value.items():
            if key.startswith("$"):
                continue
            resolved = self.d.resolve(inner, key)
            if not isinstance(resolved, dict):
                continue
            kind = resolved.get("$type")
            if kind == "ConditionList":
                conditions = resolved
            elif kind == "InstructionList":
                instructions = resolved
        if conditions is None or instructions is None:
            return None

        return OrderedDict((
            ("branch", value.get("m_Description") or None),
            ("when", [self.condition(c)
                      for c in self.d.items(conditions, "m_Conditions")]),
        ))

    def scan(self, record, wanted, into, only_by_index=False):
        """Every place in this script that reaches one of the wanted dialogues.

        `only_by_index` is for the second extraction pass, which dumps what
        SITS with each conversation rather than what names one. The two overlap
        - a room that names one conversation may pick among others by index -
        so that pass contributes only the sites the first cannot see.
        """
        self._only_by_index = only_by_index
        self._walk(record["fields"], [], None, None, record, wanted, into, set())

    def _walk(self, value, gates, instruction, site, record, wanted, into, seen,
              holder=None):
        value = self.d.resolve(value, holder)

        if isinstance(value, dict):
            path = value.get("$path")
            if path in wanted and not getattr(self, "_only_by_index", False):
                self._found(path, gates, instruction, site, record, value, into, seen)

            picked = self.by_index(value)
            if picked is not None:
                self._among(picked, gates, instruction, site, record, wanted, into, seen)

            kind = value.get("$type") or ""
            if kind.startswith("Instruction"):
                instruction = value

            found = self.gate(value)
            if found is not None:
                gates = gates + [found]

            for key, inner in value.items():
                if key.startswith("$"):
                    continue
                self._walk(inner, gates, instruction, site, record, wanted, into,
                           seen, key)

        elif isinstance(value, list):
            # Descending into a list of instructions, remember WHERE: the
            # staging around a Play - the busts switched on, the fade, the
            # cooldown set afterwards - is as much a part of how a
            # conversation happens as the conditions that let it.
            staged = holder == "m_Instructions"
            for index, inner in enumerate(value):
                self._walk(inner, gates, instruction,
                           (value, index) if staged else site,
                           record, wanted, into, seen, holder)

    def by_index(self, value):
        """
        The variable a "play my child at index N" reads, or None.

        Some conversations are never named by anything. The room plays whichever
        child of itself a variable points at - "Play Self/Events_Only_Done_2
        [fan-encounter]" - and 55 of the game's conversations are only ever
        reached that way. Nothing references them, so nothing finds them by
        looking for references; they are found by recognising the branch that
        picks among them.
        """
        if value.get("$type") != "GetGameObjectChildByIndex":
            return None

        where = self.d.field(value, "m_Transform", "m_Property")
        if not isinstance(where, dict) or where.get("$type") != "GetGameObjectSelf":
            return None            # picking among someone else's children

        index = self.value(self.d.field(value, "m_Index"))
        return index if index.get("kind") == "variable" else None

    def _among(self, picked, gates, instruction, site, record, wanted, into, seen):
        """Attribute a start to every conversation the branch could reach.

        Each child is reached when the branch passes AND the variable happens to
        hold that child's own position, so the position becomes a condition of
        its own - which is what makes this a gate rather than a shrug.
        """
        owner = record["object"]
        for path, index in wanted.items():
            if path.rsplit("/", 1)[0] != owner:
                continue

            entry = self._entry(path, gates, instruction, site, record, {}, into, seen)
            if entry is None:
                continue

            entry["chosenBy"] = picked
            variable = picked.get("variable") or {}
            if index is not None:
                entry.setdefault("when", []).append(OrderedDict((
                    ("kind", "compareNumber"),
                    ("type", "ConditionMathCompareIntegers"),
                    # What is actually compared, with the position that makes
                    # the number mean something. Titled the other way round -
                    # "if it is the 3rd child" - it described the CONVERSATION
                    # rather than the test, which is not what gets evaluated.
                    ("title", "If %s[%s] = %d (this is the %d%s child of %s)"
                              % (variable.get("list") or "", variable.get("name") or "",
                                 index, index + 1, Ordinal(index + 1),
                                 owner.rsplit("/", 1)[-1])),
                    ("fields", OrderedDict((
                        ("m_Value", picked),
                        ("m_CompareTo", OrderedDict((
                            ("m_Comparison", "Equals"),
                            ("m_CompareTo", {"kind": "value", "value": index}),
                        ))),
                    ))),
                )))

    def _found(self, path, gates, instruction, site, record, reference, into, seen):
        self._entry(path, gates, instruction, site, record, reference, into, seen)

    def _entry(self, path, gates, instruction, site, record, reference, into, seen):
        """One start site, recorded once however many ways it names the
        conversation. Returns the entry, or None if it was already there."""
        key = (path, id(instruction) if instruction is not None else id(reference))
        if key in seen:
            return None
        seen.add(key)

        script = record["script"].rsplit(".", 1)[-1]
        entry = OrderedDict()
        entry["by"] = record["object"]
        entry["script"] = script

        event = self.d.field(record["fields"], "m_TriggerEvent")
        if isinstance(event, dict) and event.get("$type"):
            entry["event"] = event["$type"]

        if instruction is not None:
            entry["how"] = instruction.get("$type")
            if instruction.get("$title"):
                entry["title"] = instruction["$title"]

        # A reference to the GameObject rather than to the Dialogue means the
        # conversation is being stored for something else to play later.
        if reference:
            entry["as"] = reference.get("$type")

        conditions = []
        branches = []
        for gate in gates:
            conditions.extend(gate["when"])
            if gate["branch"]:
                branches.append(gate["branch"])
        if conditions:
            entry["when"] = conditions
        if branches:
            entry["branches"] = branches

        if site is not None:
            items, index = site
            before = [self.instruction(x) for x in items[:index]]
            after = [self.instruction(x) for x in items[index + 1:]]
            if before:
                entry["before"] = before
            if after:
                entry["after"] = after

        into.setdefault(path, []).append(entry)
        return entry


def read_actors(path):
    """Who the game's conversations are spoken by, and what faces they have.

    A node names its actor and then picks an expression BY NUMBER - "Anna,
    expression 4". The number indexes this list, so without it the editor shows
    a bare 4 in a field that means a name. Actors are ScriptableObjects rather
    than anything in a scene, which is why they need their own extraction.
    """
    with io.open(path, "r", encoding="utf-8-sig") as handle:
        raw = json.load(handle)

    found = OrderedDict()
    said = OrderedDict()
    for record in raw.get("objects", []):
        dump = Dump(record["fields"])
        names = []
        for expression in dump.items(record["fields"], "m_Expressions", "m_Expressions"):
            names.append(dump.field(expression, "m_Id", "m_String"))
        found[record["object"]] = names

        # The Actant name: what the game puts above the line, as opposed to
        # what the asset is filed under. Only the ones that differ are kept -
        # 69 of 115 - because the rest are answered by the key itself.
        spoken = dump.field(record["fields"], "m_Actant", "m_Name", "m_Property", "m_Value")
        if spoken and spoken != record["object"]:
            said[record["object"]] = spoken
    return found, said


def Ordinal(n):
    """"st", "nd", "rd" or "th", for a sentence a person reads."""
    if 10 <= n % 100 <= 20:
        return "th"
    return {1: "st", 2: "nd", 3: "rd"}.get(n % 10, "th")


def read_starts(path, wanted, only_by_index=False):
    """Every start site in the extraction, keyed by the dialogue it starts.

    `wanted` maps each conversation's path to its position among its siblings,
    or None when the extraction predates that being recorded.
    """
    with io.open(path, "r", encoding="utf-8-sig") as handle:
        raw = json.load(handle)

    into = {}
    scripts = 0
    for record in raw.get("objects", []):
        dump = Dump(record["fields"])
        Starts(dump).scan(record, wanted, into, only_by_index)
        scripts += 1
    return into, scripts


# Characters a path may hold that a file name may not.
UNSAFE = '<>:"/\\|?*'


def file_name(dialogue_id):
    """The file one dialogue is written to.

    The same rule the UI extraction uses for its surfaces - the path with its
    separators flattened - so the two catalogs read alike on disk.

    Trailing spaces and dots come off as well. Two objects in this game are
    named with one ("X_Movies/Adult_Dialogue "), and Windows will accept such a
    file while quietly resolving the name without it, so the id keeps the space
    and the file does not. check() below is what catches it if that ever makes
    two dialogues want one file.
    """
    flat = "".join("_" if c in UNSAFE else c for c in dialogue_id)
    return flat.rstrip(" .") + ".json"


def summarise(dialogue):
    """What a picker needs to tell one dialogue from another without opening
    it: who speaks, how big it is, and whether it branches or gates."""
    nodes = dialogue["nodes"].values()
    actors = []
    for node in nodes:
        actor = node.get("actor")
        if actor and actor not in actors:
            actors.append(actor)
    return OrderedDict((
        ("id", dialogue["id"]),
        ("name", dialogue["name"]),
        ("file", "Dialogues/" + file_name(dialogue["id"])),
        ("nodes", len(dialogue["nodes"])),
        ("roots", len(dialogue["roots"])),
        ("actors", actors),
        ("choices", sum(1 for n in nodes if n.get("kind") == "choice")),
        ("conditions", sum(len(n.get("conditions", [])) for n in nodes)),
        ("instructions", sum(len(n.get("onStart", [])) + len(n.get("onFinish", []))
                             for n in nodes)),
        ("starts", len(dialogue.get("starts", []))),
    ))


def check(dialogues):
    """The two ways this catalog could be quietly wrong.

    Both would surface much later as a pack binding to the wrong line, which is
    the one failure worth failing the build over.
    """
    dupes = [i for i, n in Counter(d["id"] for d in dialogues).items() if n > 1]
    if dupes:
        raise SystemExit("dialogue ids repeat, so they cannot address "
                         "anything: %s" % dupes[:5])

    clashes = [f for f, n in Counter(file_name(d["id"]) for d in dialogues).items()
               if n > 1]
    if clashes:
        raise SystemExit("two dialogues want the same file, so one would "
                         "overwrite the other: %s" % clashes[:5])

    orphans = []
    for dialogue in dialogues:
        known = set(dialogue["nodes"])
        for node_id, node in dialogue["nodes"].items():
            for child in node.get("children", []):
                if str(child) not in known:
                    orphans.append((dialogue["id"], node_id, child))
        for root in dialogue["roots"]:
            if str(root) not in known:
                orphans.append((dialogue["id"], "root", root))
    if orphans:
        raise SystemExit("%d node reference(s) point at nodes the catalog does "
                         "not hold, e.g. %s" % (len(orphans), orphans[:3]))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dump", help="the SMSModForge-dialogues-*.json extraction")
    parser.add_argument("-o", "--out", default="VanillaDialogues",
                        help="the folder to write index.json and Dialogues/ into")
    parser.add_argument("--starts",
                        help="the SMSModForge-dialogue-starts-*.json from the "
                             "same key press: what plays each conversation")
    parser.add_argument("--around",
                        help="the SMSModForge-dialogue-around-*.json from the "
                             "same key press: the scripts sitting with each "
                             "conversation, which is where a branch that picks "
                             "among children by index lives")
    parser.add_argument("--actors",
                        help="the SMSModForge-dialogue-actors-*.json from the "
                             "same key press: what an expression number means")
    parser.add_argument("--report", action="store_true",
                        help="list every construct, modelled or not")
    args = parser.parse_args(argv)

    global ACTORS, ACTOR_NAMES
    if args.actors:
        ACTORS, ACTOR_NAMES = read_actors(args.actors)

    catalog, seen = build(args.dump)
    dialogues = catalog["dialogues"]
    check(dialogues)

    started = sites = 0
    if args.starts:
        # Path -> position among its siblings, which is what a "play my child
        # at index N" branch needs in order to name one conversation rather
        # than a handful. None for an extraction taken before that existed.
        wanted = OrderedDict((d["id"], d.get("index")) for d in dialogues)
        found, sites = read_starts(args.starts, wanted)

        # And the branches that pick among a container's children rather than
        # naming one. Nothing references those conversations, so the pass above
        # cannot see them however hard it looks.
        if args.around:
            picked, around = read_starts(args.around, wanted, only_by_index=True)
            for path, entries in picked.items():
                found.setdefault(path, []).extend(entries)
            sites += around

        # And conversations started from inside another conversation - stored
        # into a variable by one line and played by a later one. The reference
        # search skips dialogues on purpose (they are dumped whole elsewhere),
        # so these are only visible by reading the conversations themselves.
        from_dialogues, read = read_starts(args.dump, wanted)
        for path, entries in from_dialogues.items():
            for entry in entries:
                if entry["by"] == path:
                    continue                 # a conversation naming itself
                found.setdefault(path, []).append(entry)
        sites += read

        for dialogue in dialogues:
            where = found.get(dialogue["id"])
            if where:
                dialogue["starts"] = where
                started += 1

    # One file each, rather than one file.
    #
    # The whole catalog is ~9 MB and the median dialogue is 7 KB, so a tab that
    # opens one conversation would otherwise pay for all 722. The index alone
    # is enough to fill a picker, and it is under a tenth of a megabyte.
    folder = os.path.join(args.out, "Dialogues")
    if not os.path.isdir(folder):
        os.makedirs(folder)

    written = 0
    for dialogue in dialogues:
        path = os.path.join(folder, file_name(dialogue["id"]))
        with io.open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(json.dumps(dialogue, indent=1, ensure_ascii=False))
            handle.write("\n")
        written += os.path.getsize(path)

    index = OrderedDict((
        ("version", catalog["version"]),
        ("source", catalog["source"]),
        ("actors", ACTORS),
        ("actorNames", ACTOR_NAMES),
        ("dialogues", [summarise(d) for d in dialogues]),
    ))
    index_path = os.path.join(args.out, "index.json")
    with io.open(index_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(index, indent=1, ensure_ascii=False))
        handle.write("\n")

    # Files left over from a previous run would be shipped and never read.
    kept = set(file_name(d["id"]) for d in dialogues)
    stale = [f for f in os.listdir(folder) if f not in kept]
    for name in stale:
        os.remove(os.path.join(folder, name))

    nodes = sum(len(d["nodes"]) for d in dialogues)
    unmodelled = {k: v for k, v in seen.items() if not k.startswith("@")}
    dangling = sum(v for k, v in seen.items() if k.startswith("@dangling:"))
    print("%s: %d dialogues, %d nodes, %.1f MB + %.0f KB index"
          % (args.out, len(dialogues), nodes, written / 1e6,
             os.path.getsize(index_path) / 1024))
    if args.starts:
        print("  %d of %d have a start site, from %d script(s)"
              % (started, len(dialogues), sites))
        blind = [d["id"] for d in dialogues if not d.get("starts")]
        if blind:
            print("  %d are chosen at runtime rather than named, e.g. %s"
                  % (len(blind), blind[0]))
    if stale:
        print("  removed %d file(s) no longer in the extraction" % len(stale))
    if dangling:
        print("  %d shared empty lists recovered from a pre-fix extraction"
              % dangling)
    if unmodelled:
        total = sum(unmodelled.values())
        print("  %d occurrences of %d construct(s) kept whole but unnamed:"
              % (total, len(unmodelled)))
        for name, count in sorted(unmodelled.items(), key=lambda kv: -kv[1]):
            print("    %-46s %d" % (name, count))
    if args.report:
        print("  modelled:")
        for name, count in sorted(seen.items()):
            if name.startswith("@modelled:"):
                print("    %-46s %d" % (name[len("@modelled:"):], count))
    return 0


if __name__ == "__main__":
    sys.exit(main())
