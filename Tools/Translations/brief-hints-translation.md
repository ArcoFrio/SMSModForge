# Brief: translating the game-variable hints

ModForge's editor shows a short hint under each of Starmaker Story's own variables, in the variable
dropdowns (1.9.0). An author picks a variable for a condition or an action by reading it, and searches the
hints by typing words. Every hint was researched against the running game; a translation must say exactly
what the English says, no more and no less. A wrong hint is worse than none.

Source chunks: `{HINTS}\src\hNN.txt`. Each line `vhint.<list>.<name> = <hint>`, with a note above naming the
variable (`# Events[anna-met]`): context only, never translate or copy it.

Write `{HINTS}\{CODE}\hNN.txt`: plain `key = text` lines, one per source key, in the same order, nothing
else (no notes, no headings). Check each with
`python {TOOLS}\mt_check.py {CODE} hNN --src {HINTS}\src --out {HINTS}` until it prints no MISSING,
EXTRA, CODES or EMPTY line: every source key exactly once, no other key.

## Stays in English, word for word (the game is English-only, and the author meets these in the game)

- Anything in quotes: the game's own lines, choices and menu or button labels ('Let's take a photo.',
  "[Use Terminal]", 'Sex (Raw)'). Keep the quotes; ja/zh may use 「」 around them, ko ''.
- Names of characters, places as the map labels them, shops, items, outfits, perks, traits, quests and
  minigames (Anna, Josef, Starmaker, Boutique, Terrace, Motivation, Dungeon Master, Frosty Vacation...),
  and a variable named in quotes ('2026-tutorialsenabled').
- Numbers, money ($1,500), percentages, node ids (#123), weekdays may be translated (Mondays → lunes).
- The shared texts `vhint.notUsed`, `vhint.mark.inferred`, `vhint.mark.unconfirmed` are translated like
  any other line.

## How it reads

- Short and plain, like the English: the editor draws it in small type under the name. Keep it about as
  long as the English; a Japanese, Korean or Chinese hint is naturally shorter.
- The terms in `{TOOLS}\glossary.md` (variable, scene, outfit, place...) as the rest of the editor uses
  them in your language. Second person as the glossary's tone line says.
- Neutral about the game's explicit scenes, as the English is: a kind of scene and where it happens, never
  cruder words than the English uses.
- "you" is the player. Characters' pronouns as the English has them (River is "he").

## Report

Chunks written; any line where the English was ambiguous and how you read it.

Keep any helper file in `{SCRATCH}\translate-hints-{CODE}\`. Edit no other file. Work only from local files.
