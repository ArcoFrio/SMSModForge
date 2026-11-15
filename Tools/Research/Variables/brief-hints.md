# Brief: hints for the game's variables

You are writing the hint ModForge's editor will show under each of the game's variables in its variable
picker. A pack author reads it to decide whether a variable is the one they want. **A wrong hint is worse
than none:** an author builds a quest on it and it never comes true. Accuracy comes first, then being useful.
Write only what the evidence shows. When it does not show something, say less, or mark the row `unsure`.

The game is Starmaker Story 1.8E. Its logic is Game Creator 2: scripts on objects (Trigger, Actions,
Conditions, buttons) and conversations, reading and writing "global name variables", grouped in lists.
A variable is addressed as `List[name]`.

## What you have (read only)

- `{RUN}\dossiers\{BATCH}.md`: **your batch.** For every variable: type, value in a new game, value in a
  test save, and every place in the running game that sets it, reads it or runs when it changes, with the
  conversation line or script around it. It was taken structurally from the game's memory: if a dossier
  says NO REFERENCE, nothing in the loaded game names that variable at all. That was checked against the
  game's files on disk too ("name in the game's files").
- `{RUN}\game\dialogues\*.txt`: every conversation. Each node is `#id type "text"  if conditions  -> jump`,
  with `start:`/`end:` actions under it; children are indented, a Choice's children are its options. A
  dossier line names the file to open.
- **Unity events** (added 2026-11-13): `scripts.txt` and `targets.tsv` now also list what each "Invoke
  Methods" calls and every button's or trigger's own Unity event ("Unity event: Image.SetActive(True)",
  "Invoke Methods: EnableChildren on CoreGallery.EnableAllChildren()"). Before, they were invisible, and
  "nothing switches it on" was wrong about the gallery tiles: the Starmaker page switches every tile,
  comment and mail back on each time it opens (measured in the game). What the game's own C# code does
  by itself is still not in the files: a claim that something can NEVER happen because nothing switches
  an object on is `likely` at most, unless the object switches itself off or a check blocks it.
- **Which objects start switched off**: the game scene's full hierarchy, dumped from the running 1.8E
  game, `C:\Users\gabri\source\repos\SMSAndroids\SMSAndroids\ReferenceClasses\CoreGameScene_Hierarchy_1.8E.txt`
  (read only): each object with `[self:True|False, hierarchy:...]`. An object `self:False` there that nothing
  switches on is never seen (the payout screen's old lines).
- `{RUN}\game\scripts.txt`: every script, by its instructions in order. Search it for an object path a
  dossier line names, to see what else that script does (what it shows, where it sends the player).
- `{RUN}\game\refs.tsv`: every reference, when a dossier says "... and N more". A reference whose root is a
  `TextPropertyString` is an on-screen display showing the variable's value (the money counter, the payout
  figures): that IS a reader.
- `{RUN}\game\texts.tsv`: every label on the game's screens with its object path (`UI_Love` -> "Love").
  Use it for the words a player reads on a button, a tooltip or a status. `{RUN}\menu\texts.tsv` has the
  main menu's and the new-game screen's.
- `{RUN}\game\text-tokens.tsv`: the game's `{key}` words and what fills each: `{M}` = `Names[M-Term]`,
  `{PC}` = `Names[PCName]`, `{days}` = `Core[DaysPassed]`... The conversation files show lines with these
  already filled in from the test save ("stepmom", "Peter"); `{RUN}\game\line-values.tsv` has every such
  line as written, with its tokens (1949 lines). A variable a token reads is shown wherever a line holds it.
- Python: print with `sys.stdout.reconfigure(encoding="utf-8")`; the evidence holds "≥" and the like.
- `{RUN}\hints\*.tsv`: other batches' finished hints. Read them for consistent wording of a family that
  spans batches, never as evidence: every claim of yours rests on your own dossier and the game's files.
- `{RESEARCH}\README.md`: facts about the game from its author. Read the "Known from the author" section.

## How the game runs a conversation (measured in the game)

- Every root of a conversation plays in turn; a node whose conditions fail is skipped and the next plays.
- A Choice shows only the options whose conditions hold. The chosen option's own node does not run as a
  line; its children do (its `start:` actions do run).
- A Random node is a spoken line that then plays one of its children.
- A plain line's children all play in order after it, each skipped when its conditions fail (depth first,
  as the roots do), and an Exit ends the conversation there: a later child, or a later root, does not play.
  **Measured in the game** (in-game check J2b, 2026-11-13). A claim resting on it is `sure`.
- A failed "Check If" in an instruction list stops the rest of that list (Game Creator's own rule): an
  instruction after it runs only when the check holds.
- A Conditions or Actions component does nothing until something runs it (`Run <its object>` in a script,
  or a Trigger on it). One that nothing runs is not a way the game sets or reads anything.
- A jump goes to its tagged node and replays the plain lines above it.
- What shows a picture or an object is usually a `Set Active <object> to True` line in a conversation's
  actions or in `scripts.txt`: search for the object's name to find the moment.
- A dossier line marked **EDITOR-ONLY** is a debug key that checks "is Editor": it runs only in the Unity
  editor, never in a player's game. It is not a way the game sets anything; if it is the only setter,
  the variable is never set in play.
- A Conditions component runs only the first branch whose conditions hold: a setter in a branch behind an
  earlier one with no conditions never runs. Check the branch order before counting a setter. The same
  rule decides "on your first visit": if an earlier branch can take that visit, it is not the first visit
  but "the first time this talk plays", or name what comes first.

## What a hint says

One or two short sentences, at most 160 characters, plain English, for someone who has never seen the
game's variable names:

- **What it means in the game**: "Anna has agreed to the yacht trip", "money the player has", "how many
  times you have talked to Kirby at the bar". Use the game's own words where its lines have them.
- **When the game changes it**, if that is what makes it useful: "set when you pick 'Stay with me' at the
  end of the mall date", "goes up by 1 each night". With what must hold first where it narrows the moment:
  a weekday ("only on Mondays"), a place, the weather, a story stage, a random roll ("1 time in 3").
- **Pronouns as the game uses them**: ModForge's table, `SMSModForge\Model\VanillaPronouns.cs`, read from
  the game's lines (River is "he"). Never from how a bust looks.
- **The player** is "you". `{PC}` is the name they type; a new game starts with "Peter".
- **Its values**, in the `values` column, not the hint: `true/false`, `0-5`, `0, 1, 2 (3 = route done)`,
  `text: the player's name`. Only values the evidence shows the game using.
- Not: the list's name, whether it is saved (the editor shows both), internal object paths, "this variable".
  One exception: when nothing resets a flag and only loading a game does, say so ("only loading a game
  clears it"): that IS its behaviour.
  No "probably" inside a hint; use `confidence` instead.
- Names, places and events as the player meets them. A picture's object name (`06_MKitchen2`) is not what a
  player sees: describe it plainly ("a kitchen photo of Anna").
- **Characters by name, never by relationship.** "Dad", "stepmom", "brother", "son", "family" are words the
  player types on the new-game screen ({D}, {M}, {B}, {S}, {F}); a new game starts with "Dude", "Milf",
  "Buddy", "Friend", "Group". The test save's family words are only what one player chose. Write "Josef",
  "Anna", "Adrian". A line holding a token is paraphrased, or quoted only up to the token ("you want to
  help the family" is fine; "'help our Family'" is a filled-in token). Never a raw `{M}` in a hint: in
  the editor's language files `{words}` are placeholders, and an author would read the braces.
- **The game is explicit; the hints are not.** Name a scene by its kind and where it happens, in neutral
  words: the game's own menu labels where they are neutral, never its cruder ones. Do not quote explicit
  lines; cite their node ids in `evidence` instead. Enough to tell two scenes apart, nothing more.

**A family** (Gallery's numbered photos, Comments, a run of "cooldown" flags) can share one wording with the
part that differs filled in, but every row still says what its own variable is.

**Nothing uses it:** when the dossier says NO REFERENCE, the hint is exactly
`Not used by the game: nothing sets or reads it (1.8E).`, with `values` left empty. If a variable of the
same or a similar name elsewhere IS used, name it in `evidence` (the hint stays word for word). A name too
short or too common to count on disk is still `sure` when the dossier says NO REFERENCE: the walk covers
every data file that holds variable data (README, "Why no reference means none anywhere"). Say so in
`evidence`. (Rows written before this rule say `likely`; `finalize.py` raises every "Not used" row to
`sure`, so nobody needs to dispute that.) The coverage proof wins over the copy count: spare copies on
disk do not make a NO REFERENCE row `likely`.

**The game's own mistakes** go in the hint when they change what an author can rely on: a photo that sets
the wrong number, a flag that can never be set ("Never set: its photo sets 66 instead"). Say exactly what
the evidence shows.

**Read but never set** (or set but never read) says so: "Checked by the beach talks, but nothing in the
game sets it" is useful and true. Say what the evidence shows, not what the author of the game intended.

## Your own scratch files

Several writers work at once. Keep any helper script or scratch file in a folder of your own,
`{SCRATCH}\{BATCH}\`, never directly in `{SCRATCH}`: another writer's file of the same name would replace yours.

## What you write

`{RUN}\hints\{BATCH}.tsv.partial` while you work, then rename it to `{RUN}\hints\{BATCH}.tsv` when the batch
is complete. Only a finished `.tsv` counts; a half-written one is redone. Tab separated, UTF-8, header:

`list	variable	hint	values	confidence	evidence`

- One row for every variable in your batch, in the dossier's order. Count them before you rename.
- `confidence`: `sure` (the evidence says it outright), `likely` (it follows from the evidence but a step is
  inferred: say which in `evidence`), `unsure` (you could not follow it to the end: say where it stopped).
- `evidence`: under 300 characters. The lines that prove the hint: a dialogue file and node id, a script
  path, a quoted condition. For `likely`/`unsure`, what is inferred and what would settle it.

Agree with yourself only after looking for the counterexample: a second place that sets the variable to
something else, a condition that reads it the other way round. Do not edit any other file. Do not run the
game. Do not read the Gallery mod's or any pack's files: the hints are about the game alone.
