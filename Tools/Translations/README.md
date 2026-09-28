# Machine-translating the editor's text

`SMSModForge/Languages/en.txt` is the English every other language file is a
copy of. Translating one is a long job — four thousand texts — so it is done
in numbered chunks that can be checked one at a time.

Shipped so far: **es, pt-BR, zh-Hans**. The folders here are the five that are
part done: `de`, `fr`, `ja`, `ko`, `ru`, each with `00.txt` (the file's own
header) and as many numbered chunks as have been translated.

## The two scripts

    python mt_extract.py            # splits en.txt into mt/src/01.txt … NN.txt
    python mt_check.py <code> <NN>  # checks one translated chunk

`mt_check.py` compares a chunk against its English source and reports:

- a key that is missing, or one the English does not have,
- a `{gap}`, a `<tag>` or a `[PV:...]` token lost, gained or altered — those
  are filled in or read by the editor and must survive word for word,
- a plural form the language needs and the file lacks (and a form it does not
  need, which Chinese and Japanese must not carry).

It prints `<code> <NN> <translated> of <total>` when a chunk is clean.

## Doing one

1. `python mt_extract.py`, then read `mt/src/NN.txt`.
2. Write `<code>/NN.txt` — `key = text` lines only, no notes, no headings.
3. `python mt_check.py <code> NN` until it reports no findings.
4. When every chunk is done, assemble the shipped file:

       set SMSMODFORGE_REFRESH_TRANSLATIONS=<this folder>
       dotnet test SMSModForge.Tests --filter RefreshShippedTranslations
       dotnet test SMSModForge.Tests --filter EveryShippedTranslationReadsCleanly

   The first writes `SMSModForge/Languages/<code>.txt` from the chunks, keeping
   anything already translated there; the second is the check that has to pass
   before the file ships.

**The chunks can lag `en.txt`.** `mt_extract.py` splits the file as it was
when it last ran, so texts added since are in no chunk and would quietly ship
in English. Before assembling, diff the English keys against the chunks and
translate whatever is not covered as one more numbered chunk.

## What stays in English

Type names (`SetGameObjectActive`, `DailyChance`), keyboard shortcuts, file
names and extensions, the game's own names, and every token: `[PV:name]`,
`{PC}`, `{M}`, `{D}`, `{B}`, `$name`, `{item}`, `$$`. `glossary.md` has the
terms that must read the same way throughout a language.

A text left identical to the English is reported as untranslated rather than
failed — which is right for `SFX` or `Ctrl+C`, and worth looking at for
anything longer.
