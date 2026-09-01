# Changelog

## Unreleased

### Added

- **An `InputKey` condition** — gate anything on a keyboard key or a mouse
  button. Four phases: **Pressed** and **Released** are moments, true once per
  press; **Down** and **Up** are states, true for as long as they hold.
- The key is chosen from grouped dropdowns rather than typed. The game reads
  keys by position rather than by the letter on the cap, so the groups that
  move between keyboard layouts say so in their headings, and the ones that do
  not — mouse, arrows, modifiers, function keys, numpad — come first.
- Validate flags a missing key, a key name the picker does not carry, and an
  edge phase on a dialogue node's own conditions, which are checked once when
  the conversation reaches the node and so will almost never catch one.
- **Set-Active gains a Toggle**, alongside Activate and Deactivate: flip the
  target to the opposite of whatever it currently is. It reads the object's own
  state, so a parent hidden around it does not count as off.
- **Runtime names derive from display names** for NPCs, places, scenes, music,
  SFX, wallpapers and integration rules, the way characters and dialogues
  already did. A unit loaded from disk never re-derives, since its key is what
  everything else refers to it by.
- The runtime name is now a **read-only display** rather than a field to fill
  in. It stays selectable, because the runtime names units by key in its log,
  validation messages are keyed on it, and a cross-pack reference is written as
  pack.key — so it is worth reading even though there is nothing to type.
- **Every music dropdown lists the game's own tracks** as well as the pack's —
  all 38 of them, under **This pack** / **The game’s own** headings. Buttons,
  navigator buttons and the SwitchMusic action all name a child of the same
  object, so pointing one at the game’s own music was always legal; until now
  you had to know how it was spelled. The fields stay typable: the list comes
  from a dump of an older build, so anything added since can still be entered
  by hand.
- **A new map button starts on `Music`**, the game’s ordinary track: it crosses
  the world map into somewhere new, so it should say what plays there. **A new
  navigator button still starts empty** — it is a door within one area, and the
  area’s music should carry across it. An empty box means the game does not
  touch the music at all, which is also what clearing either box does. Buttons
  already saved with an empty Music are left exactly as they are.
- Renaming a track refreshes the music dropdowns. SFX already did this; music
  did not, so a renamed track went on being offered under its old key until the
  app restarted.
- Validate reports a reference that names **an effect or scene this pack does
  not have** — from PlaySFX, or a Set-Active aimed at a scene. Music references
  are deliberately not checked: they name a child of 12_AudioPlayer, and the
  game's own tracks live there beside the pack's.

### Fixed

- **Resizable panes are proportional.** Every tab's splitter columns were fixed
  pixel widths, and a drag wrote another fixed width, so a layout arranged on a
  maximised window kept those exact widths on a small one — the content column
  was squeezed to nothing and the right of the tab sat off screen. They are
  shares now, so panes scale with the window. Saved layouts store the ratio
  rather than the pixels; a layout saved by 1.1.0 is ignored once, and the
  window will not size below what its widest tab needs.
- The app icon carried a single 32x32 frame, so everywhere Windows wanted
  something bigger it was upscaling 32 pixels. It now holds 16 through 256,
  each resampled from the 512x512 source.
- Context menus painted a light band down their left edge, over the start of
  every label — the stock popup template drew its own icon gutter behind the
  themed one.
- The issues list marks the silenced issues when it shows them, and its context
  menu enables each entry by what the selected row actually is. "Stop ignoring
  this" was previously offered on every row, including ones that were not
  ignored, which read as an action with no way to reach it.
- Double-clicking an issue about an action or condition jumps to **that** one.
  The issue's location named the list but not the position, and the row was
  found by matching its type, so with two Variable actions on a node both
  issues flashed the first. Ignore entries deliberately stay position-free, so
  inserting an action above a silenced one does not un-silence it.
- The same jump works for an action inside a DiceRoll branch. Those rows live
  inside the DiceRoll's own row rather than in the node's action list, so the
  search that finds every other action could not see them and the jump fell
  back to flashing the whole node.
- **"Ignore every issue like it" now works on every check.** It needs a code to
  key a pack-wide rule on, and only 7 of 115 checks had one, so the option was
  greyed out almost everywhere. All 115 are coded now.

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
