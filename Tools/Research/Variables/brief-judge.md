# Brief: settle the disputed variable hints

ModForge's editor will show a hint under each of the game's variables (Starmaker Story 1.8E, Game Creator
2). Pack authors build quests and conversations on what these hints promise, so **a wrong hint is worse
than none**. One agent wrote the hints from the evidence; a second agent then tried to prove them wrong
and disputed some. You settle those disputes. Either agent can be wrong: the writer may have missed a
setter, and the checker may have misread a branch or asked for a rule the brief does not have. Neither
one's word counts. Only the raw evidence does.

Read first, in this order:

1. `{RESEARCH}\brief-hints.md`: what a hint may and may not say, how the game runs a conversation
   (measured), and what each evidence file is. Its rules bind your final rows exactly as they bound the
   writer's.
2. `{RESEARCH}\brief-checker.md`: what the checker was told to look for.
3. `{RESEARCH}\README.md`: "Known from the author" and the "Measured" section.

## Your batch

- `{RUN}\verdicts\{BATCH}.tsv`: the disputes. One row per fault; a variable can have several.
- `{RUN}\hints\{BATCH}.tsv`: the hints as written.
- `{RUN}\dossiers\{BATCH}.md`: what the writer had.
- The raw facts: `{RUN}\game\refs.tsv`, `scripts.txt`, `dialogues\`, `targets.tsv`, `trigger-events.tsv`,
  `list-variables.tsv`, `line-values.tsv`, `text-tokens.tsv`, `texts.tsv`, and `{RUN}\menu\`.
- `{RUN}\findings.md`: the game's own mistakes found so far, each marked checked or not. Use a checked
  one; verify an unchecked one yourself before relying on it.

Settle every variable that has at least one verdict row, including `unsure` ones: follow those to the end
yourself where the files allow it.

**Then the Unity-event recheck.** `{RUN}\recheck-events.tsv` lists rows (written before Unity events were
in the evidence, 2026-11-13) that claim something never happens and name an object a "Unity event:" or
"Invoke Methods:" row now reaches. For each row of your batch there, open those rows in targets.tsv and
scripts.txt and decide whether the claim still holds. Give each a row (`reject` with the reason when the
event changes nothing, e.g. the claim is about a variable no event sets; `amend` when it does).

**Then the word check.** Run `python {RESEARCH}\finalize.py {RUN} --lint-hints` and take the lines for
your batch (`  {BATCH} ...`). It flags, by pattern, family words, the test save's names ("Peter" is the
player: write "you"), raw `{tokens}` and list names in hints the checker may have let through. Give every
flagged variable a row too. Where the word is wrong, fix it (`amend`, or `accept` if a verdict already
asked for it). Where it is right for that row, use `keep` and say why in `reason`: the variable IS the
family word (`Names[M-Term]` names what you call Anna), or the word is plain English the brief allows
("you want to help the family"), or it is another character's relative the game names outright without a
token (check `line-values.tsv`: "That's my {M}, Samantha" is a token, and the brief's rule applies).

## How to settle one

For each disputed variable, find the setters and readers yourself in refs.tsv, open the conversation or
script around each one, and decide what is true. Then write the variable's FINAL row: the hint, values,
confidence and evidence the editor should ship. Combine all of a variable's disputes into that one row.

- `decision`:
  - `accept`: the checker is right; the final row is its `should_be`, edited only to meet the brief.
  - `amend`: both are partly right, or the fix the checker proposed is itself off; the final row is yours.
  - `reject`: the writer is right; the final row is the hint unchanged. Say in `reason` what the checker
    misread, with the line that shows it.
  - `keep`: the word check flagged a word that is right for this row (below). A row that also needs
    another fix is `amend,keep` (or `accept,keep`).
  - `game`: only the running game can settle it (a timing the files cannot show, whether a screen
    refreshes). The final row says only what the files prove, and says less where it must. `reason` says
    exactly what to play and watch, so it can be measured in the test game.
- A final row meets every rule of brief-hints.md: at most 160 characters of hint, no list name, no "this
  variable", no "probably", characters by name and never by relationship or a filled-in family word,
  neutral words for explicit scenes, values only as the game uses them.
- Keep what the writer got right. Change only what is wrong or missing; an author compares neighbouring
  rows, so a family (numbered photos, comments, cooldowns) keeps its shared wording.
- `confidence`: `sure` only where the evidence says it outright. Child-line order and Exits are measured
  and do not cap a row. A shared conversation name that `targets.tsv` resolves does not cap one either.
- A "Not used" row keeps the exact wording `Not used by the game: nothing sets or reads it (1.8E).`
  A verdict that only raises one from `likely` to `sure` is settled `accept` without more work: the
  coverage proof settles every NO REFERENCE row (`finalize.py` raises them anyway).
- A checker's `evidence` dispute changes only the evidence column, unless you find the hint wrong too.
- A row you find wrong with no verdict and no word-check flag (another batch's finding, a note in
  findings.md) gets a row too: `amend`, with the reason.
- Filled-in family words and the player's name stay out of `values` and `evidence` too: cite node ids.
- Anna's stages (`M-Stage-N-...`): name the scene that sets the stage ("after her 'We need to talk'
  scene"), never the variable's name made into words ("her 'everything but sex' stage", "no-condom
  stage"); the files do not tie a stage to a heart count.
- Another variable may be named in a hint only to point an author at the one that really does the job
  ("the real switch is '2026-tutorialsenabled'"), bare and in quotes, never with its list name.
- A `likely` the evidence fully proves (a Random list's entries in list-variables.tsv, a target in
  targets.tsv) is raised to `sure` with `amend`, verdict or not.
- When a row shows a mistake in the game itself (a photo setting the wrong flag, a price check that
  differs from what is taken, a talk nothing plays), say so in your report: the coordinator keeps a list
  for the game's author.
- Swapping in the game's own label or only changing confidence is `amend`; `accept` is the checker's
  `should_be` taken as it stands, or edited only to meet the brief.

## What to write

`{RUN}\settled\{BATCH}.tsv.partial` while you work, renamed to `{RUN}\settled\{BATCH}.tsv` when every
disputed variable has a row. Tab separated, UTF-8, header:

`list	variable	decision	hint	values	confidence	evidence	reason`

- `evidence`: under 300 characters, the final row's proof (the dialogue file and node id, script path or
  condition), as the editor's data keeps it.
- `reason`: under 400 characters, why you decided as you did: the line that settles it.
- Last row: `#summary	-	-	-	-	-	-	<variables settled>, accept <n>, amend <n>, reject <n>, keep <n>, game <n>`.
- Before you rename: `python {RESEARCH}\finalize.py {RUN}` checks your `.partial` rows and lists every
  breach left; none may be left for your batch. After renaming, run it once more: that run builds
  `{RUN}\final\{BATCH}.tsv`.

Do not edit any other file. Do not run the game. Keep helper files in `{SCRATCH}\judge-{BATCH}\` only.
Work only from local files; do not use the web. Do not read the Gallery mod's or any pack's files.
