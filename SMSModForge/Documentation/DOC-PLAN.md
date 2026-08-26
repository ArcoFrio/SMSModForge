# ModForge user documentation — plan

End-user reference for the Documentation section of the ModForge tab.
Written methodically (the personal voice belongs to the guided tutorials).

## Decisions

- **Storage:** a data catalog — `Documentation/DocTopics.cs` holds
  topic → section → bullets, rendered by a single `ItemsControl` inside the
  existing Documentation expander. `MainWindow.xaml` grows ~40 lines total.
  Chosen over inline XAML (thousands of lines of prose inside XML attributes)
  and over markdown files (would need a hand-rolled renderer to maintain).
- **Review:** in-app, plus a browsable page published per batch so prose can be
  read in bulk without launching the editor. The page is a rendering of the
  catalog, never a second source of truth.

## Conventions

- One bullet per control, in on-screen order.
- **Exact UI label** in bold, then what it does.
- A second sentence only when it earns one — when you would reach for it, or a
  gotcha. Gotchas inline; no separate warning blocks.
- No C# names, repo paths, or plugin internals. Author-facing terms only.
- Cross-reference by topic name instead of repeating an explanation. Controls
  that recur across tabs (the condition/action list editor) are documented once
  in Concepts and referred to from each tab.
- Sub-headings follow the GroupBox headings already on screen, and tab topics
  are ordered as the tabs are, so the docs and the UI stay in step.
- Exception to one-bullet-per-control: a bank of sliders that all feed one
  effect (bust and NPC jiggle) is grouped into a few bullets by what each group
  does. Eight bullets restating "how much noise" is noise itself.

## Source of truth

49 action params and 40 condition params already carry author-facing tooltips
in `ActionSchemas` / `ConditionSchemas`, in the right voice. Part 4 is seeded
from those, not written cold. Where a tooltip and the doc disagree, one of them
is a bug — fix it rather than paper over it.

## Structure — 5 parts, ~30 topics

1. **Concepts** — packs and the .smspack · keys, display names, runtime names ·
   level tokens (`place:` / `vanilla:` / `self:`) · `$variable` and `{item}`
   placeholders · folders and list behaviour · what previews do and don't show
2. **The workspace** — File · Edit · Options · Themes · the ModForge tab
   (Issues, Validate, the two debug-tracking lists) · keyboard shortcuts
3. **Tabs** — one topic each, same skeleton (purpose → toolbar → fields by
   group): Characters · NPCs · Places · Map Buttons · Dialogues · Scenes ·
   Music · SFX · Wallpapers · Variables · Integration
4. **Reference catalogs** — conditions · actions · the shared target picker
   (Category / Target / Level / Layer). Variable types/refresh modes and rule
   trigger modes were folded into the Variables and Integration tab topics
   instead, where the author is already looking at those controls.
   NOTE: the catalogs document what the PICKERS OFFER (11 conditions, 22
   actions), not the internal type counts (23/26). The editor folds the six
   Variable* and five GameVariable* conditions into one "Variable" entry with
   Source + Comparison, and the four variable-writing actions into one
   "Variable" entry with Operation + Source. Availability is also contextual:
   Random only where a condition is checked once, Timer only on rules.
5. **Validate, export, install** — issue types · what the exporter bundles ·
   where the pack goes · game-version stamping

## Batches — one prompt each, independently reviewable

- [x] 1. Scaffolding (DocTopics + renderer) + Part 2, proving it end to end
- [x] 2. Part 1 — concepts
- [x] 3. Characters · NPCs · Places · Map Buttons
- [x] 4. Dialogues · Scenes · Wallpapers
- [x] 5. Music · SFX · Variables · Integration
- [x] 6. Conditions + actions catalogs
- [x] 7. The target picker + Part 5 (validate, export, install)
- [x] 8. Consistency pass — terminology, cross-refs, ordering, gaps

## Done in batch 8

- Every "See <topic>" reference verified against real topic titles: 0 broken.
- "Key" -> "Runtime name" throughout. Eight tabs label it Runtime name; only the
  Edit menu says key. The docs now lead with the majority label and say the Edit
  menu calls the same thing the key, rather than silently picking a side.
- Removed leaked internal vocabulary: "level shader", "level resolution",
  "a level's props", "this level already has". "Level" now appears only where
  the UI itself uses it (Level tokens, Level art, Refresh on level).
- Unified needlessly-different wording for repeated labels (Display name,
  Audio path, Description, Noise sliders). Three labels still differ on purpose:
  the concept topic explains, the tab topic describes the field.
- Gap closed: added The mask painter, the Edit Mask window, which had no
  documentation at all.

## Search

`DocSearch.Filter` narrows the catalog by a case-insensitive substring across
titles, summaries, section headings, bullet labels and bullet text. A title or
heading hit keeps the whole topic or section; a content-only hit keeps just the
bullets that matched, so a common word does not answer with thirty untouched
topics. Results open expanded. It returns a fresh tree rather than flagging the
shared static one.

## Still open — decisions for the user

- Add a Validate check for SFX variant numbering with a gap (_1, _2, _4 loads
  only two, silently). Offered, not yet accepted.
- Add a Validate warning for debug tracking left on before a release. Offered,
  not yet accepted.
- The UI itself is inconsistent: eight tabs say "Runtime name", the Edit menu
  says "key". Worth settling in the app, not just the docs.

## Review page

One page, updated per batch, so the whole reference can be read in one place:
https://claude.ai/code/artifact/6a0c408e-0710-442d-ba34-1f8715507913

Generated from `DocTopics.cs` by parsing it, so the page cannot drift from what
ships. Regenerate after every batch:

    python Documentation/docparse.py     # DocTopics.cs -> json
    SP=<scratchpad> python Documentation/docpage.py   # json -> html

Then republish the same file path to keep the URL.

## Surface being covered

11 content tabs + home · 4 menus (~20 commands) · ~110 named controls ·
~35 GroupBoxes · 26 action types · 23 condition types · 3 dialogs
(Mask editor, Save confirmation, Text prompt).
