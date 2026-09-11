# Changelog

## 1.3.2

The preview tells the truth about one of the game's busts. Three things it was
not showing, and one place where it and the game disagreed on purpose.

### The preview shows what your pack replaces

**Tick a texture, choose your PNG, and the bust beside the panel changes.** It
did not before. The preview of a borrowed bust loaded the game's art and stopped
there, with no notion that a pack could paint over any of it — so the replacement
you had just chosen was applied by the runtime, saved in your manifest, and
invisible in the editor. Base, mask, blink, the four mouth frames and every face,
each landing where it belongs.

The same rule the runtime has always followed holds here: a slot you have not
ticked is left exactly as the game drew it, and a slot ticked with no art chosen
yet keeps the game's texture rather than going blank.

It also no longer needs the vanilla art extraction to be present. Your own
replacement is your own file, and it should appear whether or not the editor has
the game's art beside it to draw underneath.

**And it updates as you type.** Pointing a row at a different file repaints the
bust. The row and the outfit are different things as far as change notifications
go, and the preview was listening to the outfit — so the picture stayed on
whatever it had loaded when you selected the bust.

### The mask painter works on a borrowed bust

**Strokes appear as you paint**, the way they always have on a bust the pack
draws. Edit Mask on one of the game's busts opened the painter perfectly well
and published every stroke into the override row it was opened from — which
nothing was reading. The preview reads the outfit.

Painting does not reload the bust's textures from disk on every stroke, which is
the trap next door to this one: the brush publishes several times a second, and
choosing new art is what should reread files, not moving a brush.

### One set of jiggle numbers, everywhere

**Every bust in the preview now moves by the pack's jiggle settings, the game's
own included** — and the runtime does the same to the busts your pack changes, so
what you author is what plays.

The preview used to run a borrowed bust on the uniforms the extractor read off
the game's own material. That is right only if this shader IS the game's, and it
is not: it is an approximation of it. The game's numbers through a different
shader do not reproduce the game, they produce a third thing — and one that
disagreed with every pack bust beside it, for reasons an author could not see and
could not change, since the jiggle sliders are hidden on a borrowed bust. It read
as the game's characters being mysteriously sluggish, which some of them are:
Adrian's own frequency is 1 where a pack bust starts at 4.

**This is a deliberate, visible divergence from the game.** A bust your pack
changes will move differently from the same bust untouched beside it. That is the
cost of the preview being honest about what it can reproduce, and it is the trade
this release makes.

Only busts your pack actually changes. Every other bust in the game keeps its own
motion, and an outfit whose art all failed to load is not re-jiggled either.

The defaults themselves lived in **three** copies — the editor's, and two
separate fallbacks in the runtime, one of them driving the blink, mouth and
expression overlays. They agreed only because nobody had yet changed one. There
is one copy now, compiled into both projects, so they cannot drift.

### Not changed

The jiggle sliders are still hidden on one of the game's busts, so a pack sets
those numbers without being able to tune them. Breathing is unchanged and has
never differed between a pack bust and a borrowed one: it is one speed and one
depth for whatever is on screen, because in game the offset is applied to the
busts' shared parent rather than to any bust.

## 1.3.1

**Updating itself works.** It never had.

The editor downloaded an update, unpacked it, and handed over to the new build
to copy itself into place — and that build died on its first line, every time,
before copying anything. What an author saw was the progress prompt, the editor
closing itself, and the same version still there when they opened it again.

The line was one that reads as obviously correct:

```csharp
StartupUri = null;   // so WPF does not open a window
```

WPF rejects null outright, so it threw. The process that threw was the one
started to do the replacing, after the editor had already been told to close —
so there was no window left anywhere to report it, and nothing to distinguish it
from an update that had simply decided to do nothing.

Every release that has shipped the updater is affected: 1.2.0, which introduced
it, and 1.3.0. **Installing this one fixes updating from any of them**, because
the build doing the copying is the NEW one — so a 1.2.0 or 1.3.0 editor pointed
at this release hands over to a version that can finish the job.

The window is now opened by the editor itself rather than by `StartupUri`, so
there is nothing for the applier to suppress. And the hand-over is tested by
starting a real editor with the real switch and looking at what lands on disk,
which is the only way to see it: the fault was in code that only runs in a second
process, and every test around it passed while the step they exist to reach had
never once run.

Nothing else changed. The plugin is rebuilt only so the pair stay in step — a
pack saved by a 1.3.1 editor names 1.3.1 as the runtime it wants, and a 1.3.0
plugin would report that as an error on a pack that is perfectly fine.

## 1.3.0

One release, one subject: **the game's own cast**. A pack could always put words
in their mouths. It can now change how they look, how they sound, what faces
they can pull and what colour their name is written in — and all of it holds in
the game's own scenes, not only in the pack's conversations.

### Before you update

**Every pack written before 1.3.0 has the game's characters put back the way the
game has them.** Those characters were being *stored* — names, colours, voices,
whole wardrobes — and none of it was wired to anything, so what a pack carried
was a record of edits that never happened. Rather than carry that forward into a
release where those fields finally mean something, they are reset.

**Busts your pack drew are kept.** An outfit with art of its own is your work,
not a copy of the game's, and it survives with everything on it.

Four smaller passes run alongside it, each reported by name when the pack loads:
machine-written names on the game's characters are put back, fields a pack no
longer sets are dropped, settings that only repeated the game's own defaults are
dropped, and expressions that only restated the game's are dropped.

As with every change to a saved pack: it happens on load, in memory, nothing is
written until you save, you are told what changed and how many, and the original
is kept beside the manifest the first time you save afterwards.

### What a pack can do to one of the game's characters

**Replace textures on a bust the game already has.** One row per texture — base,
mask, blink, the four mouth frames, and a row for each face. Tick only what you
are replacing. **A slot you do not tick is a slot your pack never mentions**, and
the game draws it exactly as it always did. That is the promise the whole feature
rests on, which is why the tick is not a stored setting: it *is* whether your
pack carries an entry for that texture.

- **Edit Mask** opens the painter on the jiggle mask, the same one a bust of your
  own uses. It is the one texture here nobody can author by hand — three
  intensity planes packed into R/G/B — so it was the one texture with no way to
  make it.
- **Add a bust the game never gave them.** Fully your art, sitting in their
  wardrobe beside the game's, and treated as yours everywhere: it is validated
  like your own bust, tagged **new** rather than **changed**, and a name that
  collides with one of the game's is refused rather than quietly shadowing it.
- **Give them a face they never had.** Named in the character's expression list
  and drawn from the outfit's expression prefix, the same as any other face.

**A name colour**, which replaces the one the game writes them in rather than
sitting beside it. **A typing voice** — cadence and pitch range — starting from
the character's real numbers rather than a generic default, so opening the panel
describes the character instead of overwriting them.

**Everything above applies in the game's own scenes.** A character you re-voiced
sounds re-voiced when the game plays its own conversation, and their name is
written in your colour there too.

### Knowing which of it is yours

- A **changed** tag on a character the pack has altered, and on each bust of
  theirs individually — a character can have sixty-five outfits and one replaced
  texture, and "something in here changed" does not say which row to open.
  **new** marks a bust your pack drew for them.
- A **reset** beside every field that has moved, and one that puts the whole
  character back. A field showing the game's own value has nothing to reset, and
  says so by having no button rather than by offering one that does nothing.
- None of this appears on a character your pack invented, where every field is
  yours and a tag on all of them says nothing.

### The game's own speech, written down

`Shared/VanillaSpeech.cs` — **84 of the game's characters**, of whom 54 can pull
a face and 36 have a name colour of their own. The editor offers a character's
real defaults and the runtime reproduces them, from one file compiled into both.

None of it is readable from the game's files: the Actor assets carry no
references an extractor can follow, and the name colours live in a private list
on a component that does not exist until a conversation has started. It is
extracted from a running game by `Tools/regen_vanilla_speech.py`, which refuses
to write if what it reads stops being uniform.

The faces turned out not to work by name at all. Each character owns **a number**
under the game's `Expressions` variable, and every bust on screen watches for any
global variable to change before switching to it — so a face switched on by hand
lasted until the next variable was written, by anything, and then reverted.
Writing the number is what makes a face stay.

`Shared/VanillaBustExpressions.cs` records which busts have faces: 209 have all
four, 76 have none, and not one has some other combination.

### Renaming something renames it everywhere

Change a character's key, a bust's name, a dialogue's key — anything the rest of
the pack refers to — and **every reference to it is rewritten with it**. Actions,
conditions, and the text of lines, across the whole pack. Before this, renaming
meant hunting through the UI for everything that named the old spelling, and
missing one produced a reference to something that no longer existed.

The **reset** on a borrowed character's key uses the same machinery, so putting a
key back is as safe as changing it.

### Searching inside a dropdown

**Type in any dropdown and it narrows to the names that contain what you typed** —
not only the ones that start with it, so `na` finds both **Anna** and **Nadia**.
It is not a separate mode or a separate box: it is how the dropdowns work now.

It filters on **what you actually typed**, never on what autocompletion put in
the box for you. Otherwise typing `An`, having `Anna` completed over it, and then
seeing the list collapse to that one entry would take **Adrian** away at exactly
the moment you were reaching for it.

### A variable check stops looking like a comparison

The store picker under a variable's Value was labelled **Compare to** and shown
on every check, which had people believing a variable check always compares one
variable against another. It never was an operand — it picks which store a
`$name` in the value is read from, and a plain value ignores it entirely.

It is called **Read from** now, the same thing the Set-variable editor already
called it, and it appears only once there is a `$name` to look up. A grey line
under the Value box says what a value accepts, so the ability to compare against
another variable still announces itself.

### Fixed

- **A re-voiced character sounded exactly as before in the game's own scenes**,
  and their name was still written in the game's colour. Both settings reached
  only the Actor the plugin synthesises for its own lines, and the game speaks
  through its own Actor assets and its own speech UI — so both did nothing
  anywhere except inside a pack-built conversation, which from the author's
  chair is indistinguishable from the setting being ignored. The pack's voice is
  now written onto the game's own Actor, and speaker colours are painted wherever
  a conversation appears from. Both put back what they found when the scene
  unloads: these are the game's assets, and a pack that has been unloaded should
  not still be speaking through them.
- **Every `neutral` expression reported a warning.** Reading a character's faces
  treated an expression mapped to an empty name as a face whose art was missing —
  and an empty name is how every pack written so far spells `neutral`, which
  means no face at all.
- **Clicking between characters and outfits could raise a rename prompt** and
  quietly repoint references. Selecting a character wrote the outfit selection
  without taking the snapshot the rename check compares against, so the next
  commit read the difference as a rename and rewrote every reference to the old
  name.
- **Double-clicking a validation issue about one of the game's characters did
  nothing.** The jump asked the character tree for the row, and the game's cast
  is filed under a heading that starts closed — a row inside a closed heading has
  never been built, so the lookup found nothing and the jump gave up before
  selecting, scrolling or flashing anything. It worked throughout for your own
  characters, whose heading opens by default, which is why it went unseen.
- **"Replace textures on this bust" came back unticked** every time a pack was
  reopened, hiding replacements that were still in the manifest and still being
  applied. The tick reads the pack now rather than starting blank.
- **Clicking a character landed on whichever bust was first** rather than the one
  they enter in. The same as each other until a pack says otherwise — at which
  point it showed a bust the author had never edited.
- **The plugin shipped its scene dumps.** F10, F11 and F12 wrote megabytes of
  reflected scene state into the player's game folder, and every release up to
  and including 1.2.0 carried them. They are compiled only into a Debug build
  now, and a check reads the bytes of the packaged DLL before a release goes out,
  because nothing in the ordinary test run can see it.

## 1.2.0

Two things the roadmap named for this release, and a third that grew out of
watching people install mods: **a UI tab**, **scenes that move**, and **a way to
hand a pack to somebody that they cannot get wrong**.

### Before you update

**Packs now live in a `Mods` folder in the game folder**, beside the game's
`.exe`. The old location — `BepInEx/plugins/SMSModForge/ModPacks/` — is still
read and always will be, so nothing you have installed stops working. It is
simply no longer the place anybody is sent: four levels inside BepInEx is a lot
to ask of somebody who just wants to play, and getting it wrong produces a game
that starts perfectly and does not have the mod in it.

If a pack ends up in **both** folders, the `Mods` copy is the one loaded, and
the menu says so in amber. That is worth reading rather than dismissing: editing
the copy that lost is an evening spent wondering why nothing changes.

**A pack's version now moves when you publish, and only when you publish.**
Saving does not move it, and neither does exporting. A version says what players
were given, and an afternoon of saving gives them nothing.

**Opening a pack stamps it** with the ModForge version that will write it, so
the game can tell you when a pack needs a newer runtime than you have. As with
every change to a saved pack, it is reported when the pack loads and the
original is kept beside the manifest the first time you save.

### A UI tab

**Screens the pack owns, and changes to the ones the game already has**, in one
tab because from an author's side they are the same job.

- **Start one from a shape** — a blank panel, a window with a title bar and a
  close button, a dialog, a list, a tooltip. Or **+ Vanilla** to build on one of
  the game's own screens, which fills the tree with what is actually in it.
- **A tree of objects** with the properties of whichever is selected underneath:
  name, position, size, art, text and font. Objects nest, and an object is
  positioned against its parent, so moving a panel takes its contents along.
- **A preview against the game's own canvas**, at the size the game uses, with
  the selected object picked out. An object off the edge here is off the edge in
  the game.
- **Only your differences are stored** on a screen built from a vanilla one. An
  object you never touch is not in your pack at all, which is why **Reset** on
  an object makes it stop being a change — and why a pack that alters one label
  in the shop stays a pack that alters one label, rather than a copy of the shop
  that will not survive the game being patched.
- **Switch one on** with the Set-Active action, category **UI**, the same way
  you switch on a scene.

### Scenes that move

**A scene's art can now be a GIF or a video** — `.gif`, `.mp4`, `.m4v`, `.mov`
or `.webm` — decided by the file's extension, because that is the thing an
author controls. Rename something to `.png` and it stays a still.

- **GIFs are decoded to frames when the pack is saved**, not in the game while a
  scene is opening. Frames carry their own delays, so a title card held for a
  second in front of a fast loop plays the way it was authored.
- **Videos are played by the engine's own player**, and if one carries an audio
  track the editor notices and offers a volume slider. No track, no slider.
- **Animation is a Scenes feature**, and says so: point an animated file at a
  bust or a level layer and validation explains why rather than letting it fail
  quietly in the game.
- **Art of any resolution is fitted to the scene square**, the same as the
  runtime does. A 512×512 scene used to preview at twice the size of its
  neighbours and then play at the same size as them.

In the editor, **a GIF scene animates in the preview** — an animation you can
only see by exporting and starting the game is one you cannot judge — while a
video is held on its first frame, because that costs a decoder rather than a
blit. **When Windows has no decoder for a format the game plays perfectly
well**, a still is lifted out of the file directly so there is something to
look at.

The file picker on the Scenes tab now offers all of that, rather than PNGs only.

### Publishing

**File ▸ Publish for players** packages a release. It checks the pack over
first, says so if anything is broken and lets you overrule it, moves the
version, writes the archive, and opens the folder it wrote to.

What it writes is not a pack file. It is a picture of the game folder with the
pack already in the right place:

    MyPack-1.2.0.zip
      Mods/MyPack.smspack

Extract that into the game folder and the install is finished. There is no step
left to get wrong, because there is no step — and nothing else is in the
archive, so installing a second pack never asks anybody to overwrite anything.
The download carries the version so three of them can be told apart; the
installed file does not, so an update replaces the pack rather than leaving two
for the game to load.

The runtime plugin is deliberately not included. Two packs shipping different
builds of it would overwrite each other's runtime, and the breakage would land
on a player who did nothing wrong.

**Export is still there and unchanged** — it is the quick loop for trying your
own work in the game, and it moves nothing.

### The number on your pack

**A version, shown beside the pack's name in the game's menu.** It moves by
itself, by what actually changed since your last release: something new moves
the middle number, a change to something that was already there moves the last
one. The first is yours alone, typed in when you decide a release is a new thing
rather than more of the old one — and a version you type is published exactly as
typed.

Packs written before this get **0.1.0** rather than 0.0.0. They have content in
them, often a great deal, and calling that "nothing yet" would be wrong the
moment an author looked at the field.

**A warning if a version would go backwards**, which is the one version mistake
a player actually notices.

### What the game's menu tells you

The banner now names the runtime doing the reading — **Mods · ModForge 1.2.0** —
because every judgement below it is relative to that number. Each pack is listed
with whatever is wrong with it:

- **red** — it will not work as authored: an archive that will not open, one
  built for a different build of the game, or one made with a **newer ModForge
  than this runtime**, which may use things this runtime has never heard of.
- **amber** — it works, but it is not what you think: built for an older
  ModForge, no ModForge version recorded at all, or **installed twice**.

Everything is loaded anyway. A pack that half-works is more use than one that
refuses, and the row is what explains the difference.

### Conditions and actions

**Thirteen types became three.** The operator and the store used to be encoded
in the type name, which is why there were ten ways to compare a variable; both
are fields now.

- **Variable** — one condition with a **Source** (Pack or Vanilla) and a
  **Comparison** (equals, greater than, less than, exists), replacing
  `VariableEquals`, `VariableGreaterThan` and the seven others like them.
- **Set Active** — one action with a category, replacing the separate
  `ActivateScene`.
- **Emit Signal** — one action with an optional delay, replacing
  `EmitSignalDelayed`.

Existing packs are converted when they load, reported, and backed up the first
time you save. Nothing needs doing by hand.

**Checking a boolean is now two radio buttons**, True and False, instead of a
comparison and a Negate box that had to be reasoned about together.

### The editor

- **The long dropdowns are grouped.** The game's 1,644 variables are filed under
  the 60 lists that own them; levels split into this pack's and the game's; your
  own variables follow the folders you put them in. A flat list of that length is
  a scroll bar with no landmarks.
- **The export size warning** now speaks up at 150 MB rather than 512, allows
  twice as many files before it counts them as suspicious, and can be turned off
  for a pack that is genuinely that large.

### Vanilla bust art

**The busts shipped with the editor were being mangled.** They travel to a
smaller size for the build and back again for the screen, and both halves of
that trip point-sampled at a non-integer ratio — which does not read as low
resolution art, it deforms faces and gives a character one eye larger than the
other. Both halves interpolate now, and the shipped copies are reduced by 1.25
rather than 1.5. They are recognisably the art again.

### Tutorials

- **A screen of your own** — a new tutorial for the UI tab, which shipped
  without one.
- **First steps now ends with the release**: exporting to test, publishing to
  hand it over, and what the version means.
- **Two checks that no amount of following the tutorials could have caught**:
  one that fails if any tab has no tutorial visiting it, and one that fails if a
  tutorial names something the editor no longer offers. Both found real
  problems, which is why they exist.

### Fixed

- **The editor no longer jumps to another tab** when you press a list button
  like **+ Rule** or **+ Variable**. Those toolbars were focus scopes, so WPF
  handed keyboard focus back at the end of the click — and what it handed it
  back to was a tab header, which selects its own tab when focused. It only
  misfired when the header it remembered was not the tab you were looking at,
  which is why it came and went. Focus now stays on the button you pressed.

### Added

- **The editor checks for a new version when it starts**, and offers it with
  the release notes in a window — rendered, not raw, so headings read as
  headings instead of hashes: the version, what changed, how big the
  download is, and whether the plugin will be updated too. Say yes and it
  downloads, closes (asking about an unsaved pack exactly as the X does),
  puts the new build in place, starts it again — and the editor that comes back
  says which version it updated from, so the restart is confirmation rather
  than something to infer from the title bar.
- **Options ▸ Check for updates on start** — on by default. Off means no
  request is made at all, not one whose answer is ignored. The prompt carries
  the same setting as a checkbox, so you can turn it off from the window that
  prompted you — however you then close that window.
- **Options ▸ Install updates without asking** — off by default. On, an update
  installs itself and says so in a line at the top of the window rather than
  opening anything.
- **Options ▸ Starmaker Story folder** lets the updater replace the plugin in
  your game folder at the same time, so the pair cannot drift apart. It writes
  only the plugin and what it needs — never BepInEx itself, and never
  ModPacks. A file that cannot be replaced — something has it open — no longer
  stops the rest, and the update says which file and why in a window rather
  than a line that the next step overwrites.
- **Options ▸ Starmaker Story folder** lets the updater replace the plugin in
  your game folder at the same time. If it has never been set, the prompt offers to set it there
  and then, rather than naming a menu you would have to close the prompt to
  reach.
- **Options ▸ Check for updates now**, which answers either way.

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
