#!/usr/bin/env python3
"""Diff the freshly converted dialogues against what the pack currently ships.

Answers the only question that matters after a re-run: for every difference, is
it something the migration LOST (the extract is authoritative — take it) or
something authored deliberately SINCE (the pack is authoritative — keep it)?

The tool cannot know which, so it never merges. It groups differences by kind
and writes them out for review, most-likely-a-loss first.

    python compare_to_pack.py            # -> compare_report.md
"""
import json
import os
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_JSON = os.path.join(HERE, "dialogues_out.json")
PACK = os.path.abspath(os.path.join(HERE, "..", "..", "SMSAndroidsPack", "modpack.json"))
REPORT = os.path.join(HERE, "compare_report.md")


def dialogues(doc):
    return doc if isinstance(doc, list) else doc.get("dialogues", [])


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def node_index(d):
    return {n["id"]: n for n in d.get("nodes", []) if "id" in n}


def norm_actions(acts):
    """Comparable form: type + sorted params, order preserved."""
    out = []
    for a in acts or []:
        p = a.get("params") or {}
        out.append((a.get("type"), tuple(sorted((k, str(v)) for k, v in p.items()))))
    return out


def main():
    conv = {d["key"]: d for d in dialogues(load(OUT_JSON)) if "key" in d}
    pack = {d["key"]: d for d in dialogues(load(PACK)) if "key" in d}

    lines = ["# Converted extract vs. current pack", ""]
    lines.append(f"- converted: **{len(conv)}** dialogues")
    lines.append(f"- pack:      **{len(pack)}** dialogues")
    lines.append("")

    only_conv = sorted(set(conv) - set(pack))
    only_pack = sorted(set(pack) - set(conv))
    shared = sorted(set(conv) & set(pack))

    if only_conv:
        lines += ["## In the extract but NOT in the pack", "",
                  "Vanilla dialogues the migration never brought across.", ""]
        lines += [f"- `{k}` ({len(conv[k].get('nodes', []))} nodes)" for k in only_conv] + [""]
    if only_pack:
        lines += ["## In the pack but NOT in the extract", "",
                  "Authored for the mod, or renamed. Keep unless you recognise a rename.", ""]
        lines += [f"- `{k}` ({len(pack[k].get('nodes', []))} nodes)" for k in only_pack] + [""]

    diffs = collections.defaultdict(list)
    counts = collections.Counter()

    for k in shared:
        ci, pi = node_index(conv[k]), node_index(pack[k])
        for nid in sorted(set(ci) | set(pi)):
            c, p = ci.get(nid), pi.get(nid)
            if c is None:
                diffs["node-only-in-pack"].append((k, nid, "", ""))
                counts["node-only-in-pack"] += 1
                continue
            if p is None:
                diffs["node-only-in-extract"].append((k, nid, "", (c.get("text") or "")[:60]))
                counts["node-only-in-extract"] += 1
                continue
            for field in ("actor", "expression", "outfit", "text", "kind", "tag"):
                cv, pv = (c.get(field) or ""), (p.get(field) or "")
                if field == "text":
                    cv, pv = " ".join(cv.split()), " ".join(pv.split())
                if cv != pv:
                    diffs[field].append((k, nid, pv, cv))
                    counts[field] += 1
            for slot in ("actionsOnStart", "actionsOnFinish"):
                ca, pa = norm_actions(c.get(slot)), norm_actions(p.get(slot))
                if ca != pa:
                    diffs[slot].append(
                        (k, nid,
                         ", ".join(t for t, _ in pa) or "(none)",
                         ", ".join(t for t, _ in ca) or "(none)"))
                    counts[slot] += 1

    lines += ["## Summary", "", "| difference | count |", "|---|---:|"]
    for kind, n in counts.most_common():
        lines.append(f"| {kind} | {n} |")
    lines.append("")

    # Expression first — it is what prompted the re-run.
    order = ["expression", "actor", "outfit", "actionsOnStart", "actionsOnFinish",
             "kind", "tag", "text", "node-only-in-extract", "node-only-in-pack"]
    for kind in order:
        rows = diffs.get(kind)
        if not rows:
            continue
        lines += [f"## {kind} ({len(rows)})", "",
                  "| dialogue | node | pack has | extract has |", "|---|---:|---|---|"]
        for k, nid, pv, cv in rows[:400]:
            def cell(x):
                x = str(x).replace("|", "\\|")
                return (x[:70] + "…") if len(x) > 70 else (x or "*(empty)*")
            lines.append(f"| `{k}` | {nid} | {cell(pv)} | {cell(cv)} |")
        if len(rows) > 400:
            lines.append(f"\n*(+{len(rows) - 400} more)*")
        lines.append("")

    with open(REPORT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print(f"wrote {REPORT}")
    print(f"  {len(only_conv)} extract-only, {len(only_pack)} pack-only, {len(shared)} shared")
    for kind, n in counts.most_common():
        print(f"  {kind:22} {n}")


if __name__ == "__main__":
    main()
