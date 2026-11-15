# Brief: one voice across every batch

ModForge's editor shows a hint under each of the game's variables (Starmaker Story 1.8E). 35 batches of
hints were written, checked and settled by different agents (`{RUN}\final\vNN.tsv`). Each batch is right on
its own; together they do not always say the same thing the same way. An author reads neighbouring rows
side by side and searches the hints by word, so two names for one thing, or one row saying what its
neighbour denies, is a fault even when each row alone is true.

Read first: `{RESEARCH}\brief-hints.md` (what a hint may say) and `{RESEARCH}\brief-judge.md` (how rows
were settled). Their rules bind you. The raw evidence is in `{RUN}\game\` as those briefs describe.

## What to make agree

Go through every final row, by family and by topic, and fix only what disagrees or breaks a rule:

1. **The same thing, one name.** Places by the player's map labels (`{RUN}\game\texts.tsv`), outfits by
   the game's studio-menu or shop labels or price where the label is crude (the settled rows use "the
   Boutique's $17,500 outfit"), perks by their Starmaker names ("Motivation" skill), characters by the
   names the player sees (`SMSModForge\Shared\VanillaSpeech.cs`: Mrs. Kimura, Nightheart, the Ghost).
   Known clashes: Comments rows that still say "the cool outfit" / "the bimbo outfit" / "Maid Uniform"
   where Gallery rows give the game's labels; "cow cosplay" (studio) vs "cow-print outfit" (barn).
2. **Anna's stages** by the scene that sets them ("after her 'We need to talk' scene"), never the
   variable's name made into words ("her 'everything but sex' stage", "no-condom stage", "touching
   stage", "corrupt path" without the scene). Where the scene has only a studio label, name it neutrally.
3. **One fact, one statement.** Where two rows describe the same mechanism (a talk that sets two
   variables, a photo family, a shop), they must not contradict. Known: the Gas Station never restocks
   what it sells once bought (Energy Drink, Vape, Beer, Condoms; v33), while v08's Beer and Condoms rows
   say "each shop hides its item while true": check both against targets.tsv and make them agree, saying
   only what the files prove (whether a load restocks is not settled).
4. **Families keep one wording**: numbered photos (Gallery, Gallery_2, Gallery_3), comments, cooldowns,
   face numbers. Weekdays named the same way ("on Mondays"), "Unlocks its tile." on every photo row that
   has a tile, "No gallery tile." on those that do not.
5. **Pronouns** as `SMSModForge\Model\VanillaPronouns.cs` (River is "he"), and no family word or the
   player's name anywhere in a hint (values and evidence too).

Do not reopen what a judge settled from the evidence unless two rows contradict: a wording change only,
never a weaker or stronger claim than the evidence carries. Keep every hint within 160 characters.

## How to change a row

Only with the helper, one row at a time, never by editing a file:

    python {RESEARCH}\settle_row.py {RUN} <batch> <list> <variable> <decision> <hint|-> <values|-> <confidence|-> <evidence|-> "<reason>"

`decision` is `amend` (or `amend,keep` where the word check flags a word that is right). The reason starts
"Consistency pass:" and names the rows it was made to agree with. Afterwards run
`python {RESEARCH}\finalize.py {RUN}`: no breach may be left.

Keep helper files in `{SCRATCH}\consistency\` only. Do not edit any other file. Work only from local files.

## Report

Rows changed, grouped by which rule; each contradiction found and how it was settled (with the evidence
line); anything you could not make agree without new evidence, with what would settle it.
