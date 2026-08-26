#!/usr/bin/env python3
"""Apply only the differences that are provably safe to take from the conversion.

Two categories, both verified rather than assumed:

1. EXPRESSIONS on nodes that have a speaker. Every one of these was checked to
   equal actor.expressions[m_Expression] straight from the GC2 assets, so the
   conversion is authoritative. Nodes with no speaker are left alone — the
   pack's "neutral" and the conversion's "" both resolve to no expression, so
   rewriting 306 of them would be diff noise with no behavioural change.
   An existing real expression is never downgraded to neutral/empty: that
   direction is the only one where the pack could be ahead of vanilla.

2. SetActorBust -> explicit activation. ActorRegistry.SetBust only swaps a bust
   that is ALREADY visible, so these were silent no-ops on a character who had
   not appeared. The replacement is taken from the conversion at the same node,
   including the paired "<default> off" where it has one — several Secret Beach
   nodes need it or the default bust stays visible under the swimsuit.

Everything else (outfits, other action deltas, text) is left for manual review:
authored mod logic and migration losses are genuinely mixed there.
"""
import json
import os
import shutil
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
CONV = os.path.join(HERE, "dialogues_out.json")
PACK = os.path.abspath(os.path.join(HERE, "..", "..", "SMSAndroidsPack", "modpack.json"))
EXTRACT = os.path.abspath(os.path.join(HERE, "..", "..", "SMSAndroidsDialogues.json"))
ACTORS = os.path.join(HERE, "_input", "SMSAndroidsActors.json")

NEUTRALISH = {"", "neutral"}


def prop(o, *path):
    for p in path:
        if o is None:
            return None
        o = o.get(p) if isinstance(o, dict) else None
    return o


def main():
    conv = {d["key"]: d for d in json.load(open(CONV, encoding="utf-8"))}
    with open(PACK, encoding="utf-8") as f:
        pack_doc = json.load(f)
    ext = json.load(open(EXTRACT, encoding="utf-8"))

    # Which extract nodes actually have a speaker — the ones whose expression is
    # authoritative. Keyed (dialogueKey, nodeId).
    has_actor = set()
    for e in ext["dialogues"]:
        k = e.get("assetName")
        for n in e.get("dialogue", {}).get("nodes", []):
            if prop(n, "value", "m_Acting", "m_Actor", "name"):
                has_actor.add((k, n["id"]))

    backup = PACK + ".bak"
    shutil.copyfile(PACK, backup)
    print(f"backup -> {backup}")

    expr_changed = collections.Counter()
    expr_skipped = 0
    bust_changed = 0

    for d in pack_doc.get("dialogues", []):
        k = d.get("key")
        cd = conv.get(k)
        if not cd:
            continue
        ci = {n["id"]: n for n in cd.get("nodes", [])}

        for n in d.get("nodes", []):
            cn = ci.get(n.get("id"))
            if cn is None:
                continue

            # ── expressions ──────────────────────────────────────────────
            if (k, n["id"]) in has_actor:
                want = cn.get("expression") or ""
                have = n.get("expression") or ""
                if want != have:
                    if have not in NEUTRALISH and want in NEUTRALISH:
                        expr_skipped += 1          # pack is ahead of vanilla
                    else:
                        n["expression"] = want
                        expr_changed[f"{have or '(empty)'} -> {want or '(empty)'}"] += 1

            # ── SetActorBust ─────────────────────────────────────────────
            for slot in ("actionsOnStart", "actionsOnFinish"):
                acts = n.get(slot)
                if not acts or not any(a.get("type") == "SetActorBust" for a in acts):
                    continue
                cacts = cn.get(slot) or []
                out = []
                for a in acts:
                    if a.get("type") != "SetActorBust":
                        out.append(a)
                        continue
                    bust = (a.get("params") or {}).get("bustKey") or ""
                    # The activation for this bust in the conversion, plus the
                    # deactivation immediately before it when there is one.
                    idx = next((i for i, ca in enumerate(cacts)
                                if ca.get("type") == "SetGameObjectActive"
                                and (ca.get("params") or {}).get("target") == bust
                                and (ca.get("params") or {}).get("active") == "true"), None)
                    if idx is None:
                        out.append({"type": "SetGameObjectActive",
                                    "params": {"kind": "Bust", "target": bust, "active": "true"}})
                        bust_changed += 1
                        continue
                    repl = []
                    prev = cacts[idx - 1] if idx > 0 else None
                    if (prev and prev.get("type") == "SetGameObjectActive"
                            and (prev.get("params") or {}).get("active") == "false"
                            and not any((x.get("params") or {}).get("target")
                                        == (prev.get("params") or {}).get("target")
                                        for x in acts)):
                        repl.append(json.loads(json.dumps(prev)))
                    repl.append(json.loads(json.dumps(cacts[idx])))
                    out.extend(repl)
                    bust_changed += 1
                n[slot] = out

    with open(PACK, "w", encoding="utf-8") as f:
        json.dump(pack_doc, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"\nexpressions updated: {sum(expr_changed.values())}")
    for k, v in expr_changed.most_common():
        print(f"    {k:26} {v}")
    print(f"  left alone (pack ahead of vanilla): {expr_skipped}")
    print(f"\nSetActorBust replaced: {bust_changed}")


if __name__ == "__main__":
    main()
