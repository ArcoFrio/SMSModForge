# Brief: prove the variable hints wrong

You are checking hints that ModForge's editor will show under each of the game's variables (Starmaker Story
1.8E, Game Creator 2). Pack authors build quests and conversations on what these hints promise, so **a wrong
hint is worse than none**. Another agent wrote them from the same evidence you have. Your job is to find
where they are wrong. Assume every hint in your batch is wrong until the raw evidence says otherwise.

Read `{RESEARCH}\brief-hints.md` first: it says what the hints may and may not say, how the game runs a
conversation, and what each evidence file is. Its rules bind you too. Then read `{RESEARCH}\README.md`
(the "Known from the author" section, and why "no reference" means none anywhere).

## Your batch

`{RUN}\hints\{BATCH}.tsv` (the hints) and `{RUN}\dossiers\{BATCH}.md` (what the writer had). Check every row.
Do not trust the dossier's summary either. Go to `{RUN}\game\refs.tsv`, `scripts.txt`, `dialogues\`,
`line-values.tsv`, `texts.tsv` and `{RUN}\menu\` for the raw facts.

**`{RUN}\game\targets.tsv` settles shared names.** Every instruction that names an object (Play, Run, Set
Active), with the object's FULL path: "Play DefaultDialoge" names one of 19 different conversations, and
this says which. A row capped at `likely` only for a shared conversation name, where targets.tsv shows the
exact object, should be `sure` (a `confidence` dispute); and a row whose conversation targets.tsv shows is
never played is a `truth` dispute. "(worked out at run time)" (Self, a variable) still needs following.

## What to check, row by row

1. **Truth.** Is every claim in the hint and `values` borne out? Find each setter and reader yourself in
   refs.tsv. For "set when X", open the conversation or script and confirm that X is the moment and nothing
   else sets it. For "nothing reads it" or "never set", search refs.tsv for the variable on its own list AND
   by name on other lists, and check for an on-screen display (a TextPropertyString root). For a threshold
   or a range, find the conditions that read it.
2. **The other way round.** Look for the counterexample: a second setter with another value, a branch that
   never runs (a Conditions component runs only its first branch whose conditions hold), an EDITOR-ONLY
   setter counted as real, a line behind an Exit jump, a conversation played by a shared name ("Play
   DefaultDialoge") that may be another object's.
3. **Confidence.** `sure` must rest on evidence that says it outright. A row that infers a step, or rests on
   a shared conversation name targets.tsv does not settle, or an uncounted copy on disk, is at most `likely`.
   Child-line order and Exits are measured (see the note under "What to write"): they do not cap a row.
   Spare copies on disk matter only where the hint claims an ABSENCE ("nothing reads it", "never set"), not
   for every row about a common word. A hint that leaves out a precondition (a trait, a story stage, a first
   meeting, a weekday, a place, the weather, a random roll) is a `truth` dispute where the hint reads as
   if what it names were enough and it is not, or where the missing condition can block it for good;
   160 characters cannot list every condition, so name the one that matters most; `scripts.txt` now lists what "Check N Conditions" lines check.
   And the other way: a `likely` the evidence fully proves can be `sure` (say so; that is a dispute too).
4. **Words.** (A hint may point an author at the variable that really does the job, bare and in quotes:
   "the real switch is '2026-tutorialsenabled'". That is allowed; a list name is not.)
   Would a pack author who has never seen the variable names misread it? A raw variable name
   made into words, a cut-off thought, two hints that contradict each other across rows, crude or graphic
   wording (the brief's rule), a quoted line with a `{token}` shown filled in, and a character called by a
   relationship ("your dad", "stepmom"): those are player-typed words, "Dude" and "Milf" in a new game.
5. **Live or dead.** A reference on a Conditions or Actions component counts only if something runs that
   component (a `Run` row in targets.tsv naming its object, its own Trigger, or a "Unity event:" or
   "Invoke Methods:" row calling it: those were added 2026-11-13, see brief-hints.md). "Nothing switches it
   on" is `likely` at most: the game's own C# code is not in the files. A setter or reader that
   nothing runs is a dispute of kind `truth`. The same for a button or a conversation on a screen or object
   nothing ever switches on.
6. **Evidence.** A "Not used" row whose evidence omits a used variable of the same or a similar name, when
   there is one, is a dispute of kind `evidence` (the hint itself may be fine).
7. **"On your first visit".** A Conditions component runs the FIRST branch that holds, so an earlier branch
   can take a first visit. Where one can, "on your first visit" is a `words` dispute: should_be says "the
   first time this talk plays" or names what comes first.
8. **Relatives of other characters.** The game uses {M} for other characters' mothers too ("That's my {M},
   Samantha"): "Ken's stepmom" is the same fault as "your stepmom".
9. **Random lists and signals.** `{RUN}\game\list-variables.tsv` has what each global list holds (Random3 =
   1, 2, 3; Random10 = 1-10...: a pick "Random3[Random]" is one of those), and `{RUN}\game\trigger-events.tsv`
   has each trigger's settings (the signal an `EventOnReceiveSignal` waits for, an input's button). A row
   marked `likely` only for a range read from a list's name is `sure` when the list's entries confirm it (a
   `confidence` dispute); a timing that depends on which signal fires, and who sends it, can be followed now.

## What to write

`{RUN}\verdicts\{BATCH}.tsv.partial`, renamed to `{RUN}\verdicts\{BATCH}.tsv` when the batch is done. Tab
separated, UTF-8, header:

`list	variable	kind	verdict	should_be	evidence`

- One row per DISPUTED or UNSURE fault only; a hint with two kinds of fault gets two rows. A hint you could
  not fault gets no row.
- `kind`: `truth`, `values`, `confidence`, `words`, `unused` (a "Not used" row that is used, or the
  reverse) or `evidence`.
- Do not write a `confidence` row to raise a "Not used" row from `likely` to `sure`: `finalize.py` raises
  them all on the coverage proof. Its `evidence` can still be disputed (item 6).
- Confidence: child order and Exits are MEASURED now (brief-hints.md): a row resting only on them is `sure`,
  and one a writer marked `likely` for that reason alone is a `confidence` dispute the other way. A failed
  "Check If" stops the rest of its instruction list.
- `verdict`: `dispute` (you can show it is wrong) or `unsure` (you could not follow it to the end; say where
  it stopped).
- `should_be`: the corrected hint, values or confidence, within the brief's limits (160 characters).
- `evidence`: under 400 characters. The exact refs.tsv rows, script lines or node ids that prove it.
- Last row: `#summary	-	-	-	-	<rows checked>, <disputed>, <unsure>`.

Agreeing is the default only after you have looked for the counterexample and not found it. Do not edit any
other file. Do not run the game. Keep helper files in `{SCRATCH}\check-{BATCH}\` only. Work only from local
files; do not use the web.
