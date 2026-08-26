# Guided tutorials — plan of action

Buttons on the ModForge tab that start an ordered sequence of steps. Each step
dims the window except a cut-out around the control in question, puts a callout
beside it, and (where the step asks for an action) waits until the action has
actually been done. Exit is always available.

## Settled

**Assets ship.** They now live in `SMSModForge/Resources/TutorialAssets/`,
tracked and copied to output. `TestPack/` stays a local scratch workspace and
stays ignored. A `modpack.json` is never written into the asset folder — the
tutorial's working pack goes wherever the author points it — and `.gitignore`
carries a belt-and-braces rule for the folder anyway. Both directions verified
with `git check-ignore`.

**Do steps are gated,** and Exit is the escape. A step that has gone unsatisfied
for twelve seconds offers its hint rather than nagging immediately.

**Checks ask about the pack, not the click.** A step is satisfied by the state
actually changing, so an author who gets there another way is not told they are
wrong.

## Original note: where the tutorial assets live

`TestPack/` is in `.gitignore`, so the ten dummy sprites are untracked. They
exist on this machine and nowhere else — any tutorial that depends on them
works for us and for nobody who installs the editor.

Proposal: move them into the app as bundled content — `SMSModForge/TutorialAssets/`,
tracked, `Content` + `CopyToOutputDirectory`. A tutorial that needs a pack then
**copies** what it needs into a folder the user picks.

That also answers the packing worry properly. Rather than filtering
`modpack.json` out of the publish, no manifest is ever written into the asset
folder in the first place — the tutorial's working pack is somewhere else
entirely. Nothing to exclude, nothing to forget.

## Architecture

**Anchors.** A new `TutorialAnchor.Id` attached property tags controls
(`tab:characters`, `btn:addCharacter`, `field:baseSprite`). Deliberately not
reusing `IssueTarget.Field`: those are validation field tokens, there are only
twenty, and tutorials need to point at buttons and tabs that no issue names.
Same idea, separate vocabulary.

**Overlay.** A third child of the root `Grid` in MainWindow, sibling to the
existing `SaveToast` — no adorner needed, and it sits above everything.
It holds:
- a `Path` whose geometry is the window rect minus the spotlight rect, filled
  `EvenOdd`. A real cut-out, so the dimming is one element and the hole is
  genuinely transparent.
- the callout: instruction, step counter, Back / Next, Exit tutorial.

The `Path` is hit-test visible, so it swallows clicks outside the spotlight —
that is what keeps a step on rails. The hole is not part of its geometry, so
clicks inside it reach the real control underneath with no special handling.

**Steps.** Three kinds, which is what lets the tutorials be systematic without
being a cage:
- `Read` — explains; Next advances.
- `Do` — gated on a predicate over the view-model; advances by itself the
  moment the thing is actually done.
- `Free` — an open goal with a loose predicate ("the mask has any paint on it"),
  so the result is the author's, not ours.

**Runner.** Holds current tutorial + step index, evaluates predicates on
view-model change plus a slow timer as a backstop, and drives the overlay.

**Reuses `FlashElement`** — the amber glow from double-clicking an issue —
on arrival at each step, as asked.

## Resolution and scaling

DPI awareness is already `PerMonitorV2`, so WPF hands us device-independent
units and re-lays-out on a DPI change. On top of that:
- every rectangle comes from `TransformToVisual(overlayRoot)` + `ActualWidth/Height`,
  never a hardcoded pixel. Scale and resolution then take care of themselves.
- recompute on `LayoutUpdated` (throttled), `SizeChanged` and `DpiChanged`.
- callout placement tries below, above, right, left, takes the first that fits,
  and clamps into the window, so it can never cover its own spotlight or fall
  off a small screen.
- `BringIntoView()` before measuring, for a target scrolled out of sight.
- a step names its tab; the runner switches and defers a frame before measuring,
  the same way issue navigation already does.

## Curriculum — seven tutorials, each covering ground, none covering all

1. **First steps** — new pack, save, a character with one outfit and a base
   sprite, watch the bust preview, export. *(File, Characters, Export)*
2. **A place of your own** — a place from RoomB and Room, sorting orders,
   parallax for depth, a navigator button, a map button to reach it.
   *(Places, Map Buttons)*
3. **Your first conversation** — a dialogue in that place: two lines, an actor,
   a choice, start conditions. *(Dialogues)*
4. **Making it move** — the mask painter on a bust, jiggle settings, preview.
   Five busts of differing shape make the mask work visible.
   *(Mask painter, Characters/jiggle)*
5. **Populating the room** — two NPCs, their masks, placement in the place,
   shadow and reflection. *(NPCs, Places placement)*
6. **Remembering things** — variables, gating a dialogue on one, `[PV:]` in a
   line, a wallpaper unlocked by a condition. *(Variables, Wallpapers)*
7. **Rules that run themselves** — an integration rule with a trigger mode, a
   daily variable, a scene shown by an action. *(Integration, Scenes, Actions)*

Difficulty climbs: 1–2 are pure mechanics, 3–5 add judgement, 6–7 add state and
timing — the two things that actually make a pack behave.

## Build order

- [x] A. Overlay + runner + anchors + throwaway tutorial — BUILT.
      Geometry unit-tested incl. must-fail controls. Still needs eyes on it at
      100 / 150 / 200% scaling and in a small window.
- [~] B. Anchors are tagged per tutorial as it is written, rather than in one
      speculative pass — that way there are never anchors no step points at.
      A check compares the catalog against the XAML: every anchor used must be
      tagged, and every tagged anchor must be used.
- [x] C. Tutorials 1 (First steps) and 2 (A place of your own).
- [x] D. Tutorials 3 (Your first conversation) and 4 (Making it move).
- [x] E. Tutorials 5 (Populating the room), 6 (Remembering things), 7 (Rules that run themselves).
- [x] F. Progress persistence — completion recorded per author in EditorPrefs,
      shown as a tick and an Again button. Finishing counts; exiting early does
      not, because half a tutorial teaches half a thing.

## A failure mode to remember

The catalog once rendered as an empty list with no error anywhere. `All` was
declared above one of the arrays it concatenates; static initialisers run in
textual order, so that array was still null, `Concat(null).ToArray()` threw
inside the static constructor, and the binding layer swallowed it. A build
passes, nothing logs, the list is simply empty.

The same trap is already documented in `NodeConditionViewModel.AvailableTypes`,
which builds its pickers on first use for exactly this reason.

Anything static and cross-referencing in this area is worth loading through the
built assembly before believing it — a compile proves nothing about a static
constructor.

## Seeing it in game

Each tutorial ends by sending the author into the game to look at what they
just made, and tutorial 2 teaches the mechanism that makes that possible: a
vanilla extension on the bedroom every save starts in, with a navigator button
through to their own place. Every tutorial after it goes through that door.

An earlier attempt was a Build button that generated the whole thing — places,
NPCs and a cast dialogue — into the bedroom automatically. It worked, and it
taught nothing: the author ended up with a room full of their work and no idea
how it got there. Removed. A tutorial that does the interesting part for you is
a demo.

## Verified

A check run against the built catalog and the XAML together:
33 anchors, all used, none duplicated, none dangling — and every step's declared
tab matches the tab its control actually sits on. That last one has caught real
mistakes twice: there are two "+ Add navigator button" buttons, two "+ Add NPC"
buttons and two Edit Mask buttons, and in each case one belongs to the
vanilla-extension editor rather than a pack's own place.

Curriculum shape, gentlest first:

| # | Tutorial | Steps | Read / Do / Free |
|---|----------|-------|------------------|
| 1 | First steps | 9 | 4 / 5 / 0 |
| 2 | A place of your own | 8 | 2 / 5 / 1 |
| 3 | Your first conversation | 6 | 2 / 3 / 1 |
| 4 | Making it move | 5 | 2 / 1 / 2 |
| 5 | Populating the room | 6 | 2 / 3 / 1 |
| 6 | Remembering things | 5 | 1 / 3 / 1 |
| 7 | Rules that run themselves | 7 | 4 / 2 / 1 |

Free steps rise as the tutorials get harder, which is the intent: the early ones
are mechanics with a right answer, the later ones judgement, where insisting on
one answer would teach the wrong lesson.

A first, and deliberately with throwaway content: if the spotlight or the
gating is wrong, that is far cheaper to find before seven tutorials are written
against it.

## Per-run state

`TutorialScratch` holds a step's baselines and is cleared every time a tutorial
starts. The first draft used static fields on the catalog, which survive between
runs — start a tutorial twice and the second run begins holding the first run's
answers, so a step can be satisfied before it has been read.

## Assets in a tutorial

`TutorialAssets.EnsureCopied` copies the shipped art into a `TutorialArt/`
folder inside whatever pack the author saved, on arrival at the step that needs
it. Copied rather than referenced in place, because a pack has to be
self-contained to export, and an author replacing our placeholder should be
editing a file that belongs to them. Existing files are never overwritten, so
re-running a tutorial cannot destroy a mask someone painted.

## Open decisions

1. **Assets** — bundle into the app as above, or keep tutorials working against
   whatever pack the user already has?
2. **Do steps** — hard-gated (the dimmed area eats clicks until the step is
   done) or guided (Next always available)? Hard-gating matches "have to
   complete in order"; guided is kinder to someone who already knows the tool.
3. **Progress** — remember which tutorials are finished, and offer to resume a
   half-finished one?
