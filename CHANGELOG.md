# Changelog

## 1.1.0

The first update since the initial release. It is mostly about **learning the
tool** — the standing complaint was that ModForge is hard to pick up — plus the
editor fixes and format corrections that came out of actually sitting down and
following the tutorials end to end.

### Before you update

**Re-saving a pack that uses a `GameObjectActive` condition requires the 1.1.0
plugin.** That condition used to store its target in `path`; it now stores
`kind` + `target` (see *Conditions* below). The 1.1.0 plugin still reads `path`,
so packs authored in 1.0.0 keep working untouched — but a pack re-saved in the
1.1.0 editor drops `path`, and a 1.0.0 plugin reading it finds nothing and
treats the condition as failed, so gated lines silently stop playing. Ship the
editor and the plugin together.

Nothing else changes on disk. The manifest is still `smsmodforge/modpack/v1`.

### Learning and documentation

- The in-app reference now opens with a **Start here** section that builds the
  mental model before the per-control detail: what a pack is, what order to work
  in, how things point at each other, and what to check when something does not
  work.
- **Addressing** and **text substitution** are documented for the first time.
  Place tokens (`vanilla:`, `place:`, `self:`, `pack:`) and the hierarchy paths
  that Set-Active and friends accept were visible on screen and explained
  nowhere.
- The mask painter documentation was wrong in ways that mattered: a place mask
  has **one** layer in alpha, not three; and the layer names and the setting
  names genuinely disagree, which is now named rather than repeated.
- The jiggle settings use **one vocabulary** across Characters and NPCs instead
  of two different sets of labels for the same six values.
- Every control **explains itself on hover**. The reference is no longer the
  only place a field is described.
- The README describes the tool as it is. The old one named tabs and actions
  that do not exist.

### Tutorials

- Rebuilt as a **linear progression, grouped by tab**, from a pack with nothing
  in it. 14 tutorials now, covering every tab — Media, NPCs, the world map,
  Scenes, Music, SFX and Wallpapers had none at all.
- Practice art and audio **ship with the editor** and are copied into your pack,
  so no tutorial points at a file only its author has.
- A tutorial you finished tells you when it has been **rewritten since**, rather
  than staying quietly ticked.
- Tutorial steps are covered by an **automated walk-through** that proves each
  one can actually be completed, and that no step is already satisfied the
  moment it opens.

### Editor

- **Art of any size is fitted** to the frame it was authored for, evenly, rather
  than stretched — busts to 256×256 and place art to 2048×1136. The preview does
  exactly what the game does.
- **Validate** reports art that is the wrong size or the wrong shape, and any
  warning can be silenced by type or one at a time, per pack.
- Warnings for things that used to fail silently: a `[PV:name]` naming a
  variable nothing declares (it resolves to an empty string and vanishes from
  the line), a `[PV:` that never closes (it is printed to the player as typed), a
  line that spells out **Mom**, **Dad** or **Brother** instead of `{M}`, `{D}`,
  `{B}`, and a Choice node with no options.
- A **token cheatsheet** under the Dialogues sidebar: `{PC}`, and `{M}` `{D}`
  `{B}` for what this player calls Anna, Josef and Adrian.
- **Music** is a dropdown of the pack's own tracks on map buttons and navigator
  buttons, instead of a name typed from memory.
- **Node editor**: Expression and Outfit are hidden while the player is
  speaking, the jump destination only appears when the mode is Jump (and is a
  dropdown of the dialogue's tags, renamed **Jump to**), and Timeout only
  appears when Duration is Timeout.
- A dialogue's **runtime name is derived from its display name**, the way a
  character's already was.
- **Jiggle** gains a Default button on every field, for busts and NPCs.
- The **mask editor** saves beside the art it was painted over, named after it —
  `AnnaBase.png` proposes `AnnaBaseMask.png`.
- The **prefix fields** say they are paths, and trail a grey hint showing what
  the game will actually load. Picking a blink PNG fills in the folder.
- Expressions moved into the **Sprites** box; they were never a separate
  feature.
- The level preview's gizmo gains a **Reflection** part, shown when the NPC has
  one, alongside Body / Shadow / Blink / Wet.
- The scroll wheel works over the dimmed area during a tutorial.

### Conditions and actions

- **`GameObjectActive`** uses the same Category + Target row the Set-Active
  action uses — Bust, GameObjects (scoped to a level), Scene or Direct Path —
  and resolves the same way at runtime. It now finds inactive objects and scopes
  a level-overlay lookup to the level named, so a same-named object in the room
  being left can no longer answer for the one being entered. `$varName` works
  here too.
- A Choice's options no longer offer **Actions on start**. When GC2 considers an
  option "started" is not something a pack can rely on — it may be when the menu
  is drawn rather than when the option is picked — so on-finish is the only
  offered hook, and older packs carrying such actions are flagged.

### Fixes

- Adding a node with **+ Child** or **+ Sibling** copied the kind of the node it
  came from. Under a Choice that made every option a Choice, and everything
  under those read as options too — a follow-up line lost its Actions-on-start
  box and would have been built as a one-entry menu. New nodes are plain lines;
  Validate flags the packs that already carry the damage.
- Two preview dropdowns had a tooltip written as element content, so the tooltip
  text became the **first item in the list**. Mouth frame therefore ran a frame
  behind, and Expression defaulted to the tooltip string.
- Previews reload after a mask is saved.
- The Characters toolbar buttons stay on screen, and **+ Outfit** is disabled
  until a character that can have one is selected.
- The player character exists in a new pack, and dialogue lines show a speaker.

### Repository

- `Tools/` is tracked. It was ignored wholesale — 29 files, including the
  thumbnail generator that produces art the release ships and the six Unity
  extractors that refresh the vanilla catalogs. A fresh clone could not rebuild
  a release.
- `DocCoverage.py` audits the in-app reference against the editor's own
  functions, so "document everything" is checkable.

## 1.0.0

Initial release.
