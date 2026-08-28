# Rebuilding the tutorials

The standing complaint is that ModForge is hard to learn. The reference and the
tooltips have been dealt with; the tutorials are what is left, and they have a
different problem from the docs. They assume the reader already knows how this
game is put together.

They also cannot currently be trusted: every regression in them so far was
found by a human clicking through and getting stuck. That is the first thing to
fix, because it decides how everything after it gets built.

---

## What is wrong now

**They teach the editor, not the craft.** A step says "point the base sprite at
one of the fuller busts" and assumes the reader knows what a bust is, what
size it has to be, and why. An author arriving with their own art has nowhere
to learn what art to make.

**They lean on the shipped practice art.** The assets exist so nobody is
blocked, and that is right — but a tutorial that only works with them teaches
nothing about starting from nothing. Every step that names a practice file
should also say what the file IS, so the same step works with the author's own.

**Seven tutorials, no depth.** One per broad area, each stopping at the first
useful result. There is no second tutorial anywhere that goes further into a
tab an author has decided they care about.

**Three tabs were never opened by any tutorial** — Map Buttons, Music, SFX —
which the last batch covered with Read steps as a stopgap, not a fix.

---

## Principles

1. **Assume nothing about the game.** A reader who has played it and never
   modded it should get through. Where a step relies on a fact about the game,
   the step states the fact.
2. **Every step works without the practice art.** Name the practice file AND
   say what it is, so the same instruction reads correctly for an author who
   brought their own.
3. **State sizes and formats where they matter.** Bust art is authored at
   256x256; level layers at 2048x1136; a bust has exactly four mouth frames and
   exactly four expressions, named Happy, Angry, Sad and Flirty. Art of another
   size is now fitted rather than broken, but "what should I make" still needs
   an answer.
4. **The framework does not grow per tutorial.** A tutorial is data. If a step
   cannot be expressed with what `TutorialStep` already has, the framework gains
   one general capability — never a special case for one step.
5. **A step's check asks about the pack, not the click**, and has to be
   specific enough to fail. Already the house rule; the tests enforce it.

---

## Batches

Ordered so that the machinery which proves a tutorial works exists before any
tutorial is written against it.

### Batch 1 — the test harness

A new `SMSModForge.Tests` project. Feasibility is already confirmed:
`MainViewModel` constructs with no `Application` and no window, all 70 steps
enumerate, all 36 `IsDone` predicates run without throwing, and commands
execute headlessly.

**Static checks** (no UI, fast, catch the failures seen so far):
- every `Anchor` and `AlsoAllow` id resolves to a real `TutorialAnchor.Id` in
  the XAML — the "step highlights the wrong thing" class
- `Do` and `Free` steps have an `IsDone`; `Read` steps do not
- `Do` steps have a `Hint`
- `Tab` is a valid index, and matches the tab the anchor actually lives on
- ids are unique; `Level` values are contiguous and ordered
- no step body names a control that does not exist in the XAML

**Progression checks** (the real ask): a scripted solution per step, driven
through the view model. For each step, assert it is NOT satisfied on arrival,
apply the solution, then assert it IS. That catches both "already passed before
you did anything" and "cannot be passed at all" — the two failures that
actually stopped people.

### Batch 2 — framework and validation

Only what the tutorials need, and nothing tutorial-specific:
- surface `Level` in the UI as real grouping (it is set 1..7 today and shown
  nowhere)
- whatever general step capability Batch 1 shows to be missing
- `PackValidator` learns to open images: warn when bust art is not 256x256 and
  when a level layer is not 2048x1136, now that both are fitted rather than
  broken. The editor should say it, not only the tutorial.
- **open question**: the WPF preview renders busts through a 256-based path.
  Confirm it agrees with the runtime's new fitting, or the preview and the game
  will disagree for odd-sized art.

### Batches 3+ — the tutorials, one group per tab

Each group starts where the current single tutorial does and then goes further.
Grouped by tab, ordered by depth, using `Level` for the progression.

- **Characters** — a bust from nothing; then blink, mouth and expressions
  (now that the practice busts have them); then the jiggle mask.
- **Places** — a room; then layering, parallax and depth; then vanilla
  extensions.
- **Dialogues** — a conversation; then branching and conditions; then actions.
- **NPCs** — placing one; then reflections, shadows and per-room state.
- **Media** — Scenes, Music, SFX and Wallpapers: the tabs with the thinnest
  coverage, and the ones with genuinely undiscoverable behaviour (SFX
  auto-trigger patterns, numbered variants).
- **Logic** — Variables, then Integration rules.
- **Map Buttons** — never covered by any tutorial.

Exact tutorial count per group falls out of writing them; the grouping is the
commitment.

### Final batch — the human pass

The tests prove a tutorial is completable. They cannot say whether it is
followable, clear, or worth the reader's time. That is the manual run-through,
after every batch is in.

---

## Facts a step may state, verified

Kept here so a step never has to guess, and so a wrong one is correctable in
one place.

| | |
|---|---|
| Bust art | authored at 256x256; other sizes are fitted to that frame |
| Mouth frames | exactly 4, looked up as `prefix` + `1..4` + extension |
| Expressions | exactly 4: Happy, Angry, Sad, Flirty — any other name resolves to nothing |
| Blink | one frame; the outfit's "Has Blink frame" box says whether it exists |
| Level layers | 2048x1136, back at sorting order -12, front at -10 |
| Level masks | 256x143, optional |
| Bust mask | 256x256; R/G/B are Bounce, Wave and Noise, alpha scales all three |
| Place mask | one plane, in alpha |
| Navigator buttons | 12 per place |
| Map districts | Seaside, The Line, Neon Row, Shopside, Foundry |
| Audio | OGG, WAV and MP3; `_1`, `_2` variants picked at random |
