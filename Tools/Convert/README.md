# GC2 → ModForge dialogue conversion pipeline

Deterministic, reviewable conversion of the raw GC2 dialogue extract into the
modpack `dialogues` array. SMSAndroids-specific — lives here under `Tools/`, not
in the generic ModForge editor. Nothing is dropped silently: anything uncertain
lands in `coverage_report.md` for review in the editor.

## Inputs (regenerate `_input/` from git if needed)
```
# raw GC2 extract (run the Unity editor script Tools/UnityEditor/SMSModForgeDialogueExtractor.cs)
SMSBustForge/SMSAndroidsDialogues.json

# pre-ModForge dispatcher + siblings (semantic source for what each marker does)
git -C SMSAndroids show d42d17d:SMSAndroids/MainStory.cs   > _input/MainStory_pre_modforge.cs
git -C SMSAndroids show d42d17d:SMSAndroids/Dialogues.cs   > _input/Dialogues_pre_modforge.cs
# (Characters/Core/Scenes/Schedule too — see commit d42d17d)

# format truth
SMSBustForge/SMSAndroidsPack/modpack.json
```

## Run order
```
python extract_marker_logic.py     # -> marker_logic.json   (Tool A: dispatcher -> marker ops)
python convert_dialogues.py        # -> mapping.json (scaffold, first run only)
                                   #    dialogues_out.json + coverage_report.md
# 1. review/fix marker_logic.json   (raw ops the parser couldn't classify)
# 2. review/fix mapping.json        (TODO actor/expression/sfx entries)
python convert_dialogues.py        # re-run; your edits win
# 3. splice dialogues_out.json into modpack.json's "dialogues", open in the editor,
#    Validate() + visually review everything the coverage report flagged.
```

## How it works
- **Tool A** keys each dialogue by the dispatcher's `dialogueToActivate.name == "AssetName"`
  (exact match to the extract's `assetName`). It handles both dispatch shapes —
  `Find("Scene1").activeSelf` and the named-field `Dialogues.<field>Scene1.activeSelf`
  (Secret Beach / Harbor Home / voyeur) — and classifies each statement into ops
  (`scene`, `leaveBust`, `setVar`, `emitSignal*`, `changeBust`, `endDialogue`, …).
  Anything it can't classify becomes `{"op":"raw"}` (flagged later).
- **mapping.json** is the human control point: `actorAsset`→key, per-actor
  `expressionIndex`→key (auto-derived by joining extract node ids to the existing
  migrated pack), `audioClip`→SFX key, `signalField`→string. Edit the `TODO`s.
- **Tool C** translates every node mechanically (text/actor/expression/outfit,
  SFX→PlaySFX, Wait, RaiseSignal→EmitSignal, conditions) and resolves each
  `SetActive(marker)` through `marker_logic.json` → ModForge actions (the new
  unified `SetGameObjectActive{kind:Scene}`, `LeaveBust`, `SetVariable`, …).
  `startConditions` are carried over from the current pack (gating wasn't a
  failure source); Tool A's parsed gating is in the report as a cross-check.

## Coverage report categories (review these)
`expression` (index→key gaps), `raw-op` (custom dispatcher code: camera moves,
parallax, ad-hoc flags), `marker-no-logic`, `conditional` (branch-dependent flag
sets — place by hand), `outfit`/`bust` (need real bust GO names), `condition`
(integer/gameobject compares), `null-target` (the 2 unresolved SetActive
instructions), `scene`/`variable`/`sfx`/`actor` (unmapped names).
