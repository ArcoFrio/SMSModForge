# The game's variables: research behind the editor's hints (1.9.0)

The editor's Vanilla variable picker shows a hint under each of the game's variables, and typing part of a
hint finds the variable. The hints promise things about the game, so each one rests on evidence taken
from the running game and is then checked by someone trying to prove it wrong. See `PLAN-1.9.0.md` §1.

Everything here can be run again on a new version of the game, and the scripts say what changed.
`runs\` holds the game's own text and values: it is ignored by git and stays on this machine.

## The steps

1. **Collect**: `powershell -File Tools\Research\Variables\collect.ps1`. In the test copy (Tools/Game's
   rules), with its packs set aside and anything ModForge makes skipped, so the evidence is the game alone
   (the first run counted the in-game suite's packs: 8 references, 34 conversations), the Debug plugin's
   `variable-evidence` command (VariableEvidence.cs) writes `menu\` and `game\`:
   - `lists.tsv`: every list, whether it is saved, and every variable with its type and value now (on the
     main menu that is a new game's value).
   - `refs.tsv`: every reference, found by walking every script, prefab and Game Creator asset field by
     field. A reference's name is the holder's string that IS a name in that list, never a guess; one that
     matches nothing is written as `?`.
   - `mentions.tsv`: names written in text (weak evidence: "Room 41" mentions a variable named "41").
   - `dialogues\`, `scripts.txt`: every conversation (722) and script, in the Gallery evidence's form.
   - `texts.tsv`: every label on the game's screens with its object path.
   - `targets.tsv`: every instruction that names an object (Play, Run, Set Active) with the object's full
     path. A title prints only the name, and "Play DefaultDialoge" names one of 19 conversations; this says
     which, and so which components and conversations anything actually runs (added 2026-11-13; first used
     to show Amelia's ending has no path).
   - `list-variables.tsv`: what each global LIST variable holds (Random3 = 1, 2, 3...), which a "Random3
     [Random]" pick draws from; `trigger-events.tsv`: each trigger's settings (the signal it waits for).
   - `line-values.tsv`, `text-tokens.tsv`: every line as written, with the game's `{key}` words, and what
     fills each key (`{M}` = `Names[M-Term]`).
   - Everything in Resources is loaded first, so two collections come out identical (checked 2026-11-13).
   - "Check N Conditions" lines list the conditions they check (since the evidence swap of 2026-11-13; the
     first writers' copy did not, and some rows were worked out from `refs.tsv` instead).
2. **Compare**: `python compare.py <run> [<Gallery evidence>]` checks the catalog against the game, and
   uses the Gallery's title-based evidence as a control: every name it sees must have a reference here
   (2026-11-13: 1435 of 1435).
3. **Catalog**: `python update_catalog.py <run>` brings `SMSModForge/Model/VanillaGameVariables.cs` in line
   with the game: same order, the game's variables only.
4. **Files on disk**: `python scan_files.py <run> "<test copy>\Starmaker Story_Data"` is a second witness
   for variables with no reference: Unity stores a name as length + bytes, so any scene, prefab or asset
   naming it, loaded or not, shows a copy beyond the list's own. Control: every referenced variable with a
   name of its own shows one (2026-11-13: 1350 of 1350). `shared_names.py` and `unreferenced.py` look into
   the ones that remain.
5. **Dossiers**: `python make_dossiers.py <run>` writes `dossiers\vNN.md` (35 batches) and `batches.tsv`.
6. **Writers**: one agent per batch with `brief-hints.md` (`{RUN}`, `{BATCH}`, `{RESEARCH}` = this folder).
   Each writes `hints\vNN.tsv`, renamed from `.partial` only when complete. `python status.py <run>` says
   which batches are done, so a stopped run carries on with the missing ones only.
7. **Skeptics** (next): independent agents try to prove each hint wrong from the same evidence; each
   dispute is checked in the test game before a hint changes.

## Known from the author

- **Two galleries.** The Starmaker PC page has a gallery (`9_MainCanvas/Starmaker/Starmaker_Core/Gallery`)
  that is used: it reads the `Gallery`, `Gallery_2` and `Gallery_3` lists, which the scene pictures set when
  shown. The game also has a half-baked gallery of its own, an unused asset: `Canvas_Gallery_Core` reads
  some of `Gallery`. Do not credit one with the other; check that nothing opens `Canvas_Gallery_Core`
  before saying a variable only feeds it.

## Measured, 2026-11-13 (1.8E)

- 64 lists, 1926 variables, 1649 names. 1638 variables have a structural reference; 288 have none.
  (Before on-screen value displays were walked: 1637 and 289. Game Creator's TextPropertyString is a
  uGUI Text that shows a variable - the money counter, the payout figures - and was read only for its
  words; 53 variables gained readers once it was walked too.)
- The catalog had 36 variables that were never the game's (another mod's runtime list, an unnamed asset)
  and lacked 38 of its own. Fixed by `update_catalog.py`.
- Of the 177 unreferenced variables with a name of their own, 170 have no other copy in the game's files
  or code. The other 7 are explained: object, material or actor names (`Merch`, `poolchick`, `Amelia`,
  `Isabella`), single letters (`M`, `D`, `B`; the one `"M"` in the game's code is a minutes suffix).
  `your-share` was once in this list, "explained" as a local variable of the same name: that was wrong.
  It is Payout[your-share], shown on the payout screen, and is referenced since displays are walked.
- **Why "no reference" means none anywhere** (checked 2026-11-13). Every data file that holds Game
  Creator variable data is loaded during collection: `level0` (the main menu) with `sharedassets0`,
  `level1` (the game) with `sharedassets1`, `resources.assets` (loaded in full first) and
  `globalgamemanagers.assets` (always loaded). The only other scene, the loading screen (`level2`, with
  `sharedassets2`), holds none. The game's code touches no variable. So what the walk does not find is not
  in the game, as long as the walk misses no kind of holder: its controls (1435 of the Gallery's names,
  every referenced one-list name's copies on disk) found three such gaps, each fixed (dialogue nodes,
  assets not yet loaded, value displays). This settles the numbered names too short to count on disk
  (Gallery_3[30]-[110], Comments[40]-[45]...), which the copy count could not; `attribute_lists.py`, which
  tries to credit each copy to its list, cannot learn ids for lists whose names are all shared.
- 56 of 1528 one-list variables have more copies on disk than references; nearly all are ordinary words
  (Condoms, Beer, River, Charlotte) used as item, object and actor names too.
- A copy of a name beyond its references is not explained by its bytes: a rule taking a type id before
  the name for a definition also took every reference for one (its control failed). Hints that rest on
  "nothing reads it" with spare copies stay likely.
- 16 references name a variable their list does not have (`quicktravel-cooldown` on several travel
  triggers): the game reads something that is not there.
- The game's C# code names no variable. The extract's Game Creator assets are empty, so the running game
  is the only source.
- **Unity events were a blind spot until 2026-11-13.** Game Creator's "Invoke Methods" is a Unity event,
  and so is every button's own On Click; the walk read neither, and "nothing switches it on" was said of
  the Starmaker gallery tiles and comments, which the game does show (measured: `measure-refresh.ps1`).
  `VariableEvidence` now writes each call (target, method, argument) into scripts.txt and targets.tsv:
  1347 more rows, every other file byte-identical. The control: the new rows show the Starmaker page
  calling `EnableAllChildren()` on the gallery, comments and mails, exactly what the game did.
  `recheck_events.py` lists the hints written before that which rest on "nothing switches it on". The
  game's own C# code acting by itself is still unseen, so such a claim is `likely` at most.
- **The game tells capitals apart** (Game Creator's `NameVariableRuntime` is a plain Dictionary):
  `Mei-corrupted` and `mei-corrupted` are two variables. **Lists are searched in the variables
  repository's order** (bridge `glists`); for all 121 names in several lists, the first matches the
  catalog's first, and `get 1` read Comments' value.
- **How a conversation plays**, measured by ModForge's in-game suite (`InGameSuiteTests` J2 and J2b,
  built through its Talk pack): every root plays in turn; a jump replays the plain lines above its target;
  a plain line's children all play in order; an Exit in an earlier child stops the later ones, and an
  Exit in a first root stops the second root. Hints resting on these are `sure`.
