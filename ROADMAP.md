# Roadmap

What is planned but not built. Items move out of here into `CHANGELOG.md` when
they ship.

---

## Now (1.6.0) — translation, in order

The order is the order to work in. Each step says what is decided, what
"done" means, and what it waits on. **Update the status marks as work lands** —
this list is what survives a lost context, so it has to be true.

Decided by the author (2026-09-24), not to be re-asked:

- Saving while a translation is up never touches the pack's own words. Proven
  by `EditingLanguageTests.SavingInALanguage...` (byte-identical save).
- A line **added** while a translation is up goes into that translation only.
  The pack's own words for it stay **empty**, flagged as an error. In game it
  falls back: player's language → pack's own words → any translation, so it
  is never blank.
- In game, the language is chosen **on the main menu only** — inside the game's
  Settings panel, and as flags above the Exit button, bottom-right, stacked
  from the bottom. Packs load when CoreGameScene is entered, so choosing on
  the menu needs no live re-application to pack content.
- A pack without the chosen language falls back to **its own words**, per pack
  and per line — never to another pack's choice.
- Switching language in game warns about packs under **50%** coverage for it,
  listed like the risky-mod warning on load.
- ModForge's own texts (mod list, save warning, menu) are translated once in
  ModForge's files, never per pack. Already true.
- Google web engine stays primary, a browser-driven ("RPA") engine is the
  fallback, and ModForge must **detect** when an engine is not working as
  intended rather than write what it returns.
- Editor switching language without a restart: only if the live screen walk
  proves every text updates. Otherwise it keeps needing a restart.

1. **Machine translation you can trust** — done
   - [x] `client=at` on `translate_a/single`; `gtx` and `translate_a/t` are
     refused since 2026-09-15. Live tests pass (`SMSMODFORGE_LIVE_TRANSLATE=1`).
   - [x] A block is never retried.
   - [x] Repeated lines sent once; lines with no letters not sent.
   - [x] Per-line identity markers instead of counting line breaks, so a merged
     or split line is detected rather than filed under a neighbour's key.
   - [x] Length sanity per line; a suspicious line is retried on its own.
   - [x] Wordless lines ("...", "?") converted for Chinese and Japanese
     (full-width, "……") rather than copied through.
   - [x] Health check: a known probe before a run (passes against the real
     service), and a stop when over a quarter of 20+ lines come back damaged.
2. **One game run for two measurements** — Debug build with (a) a dump of
   the main menu's Settings panel and Exit button, and (b) the dialogue box's
   TMP settings (rect, margins, overflow mode, auto-size bounds). Hand to the
   author early; steps 5 and 6 wait on the result.
   - [x] `SMSModForge.PackPlugin/LayoutDump.cs` (Debug-only, no key): writes
     `BepInEx/SMSModForge-menu*.json` on the main menu (again each time what is
     switched on changes, e.g. Settings opened) and
     `BepInEx/SMSModForge-dialoguebox-*.json` on the first conversation.
     Debug plugin built into the game folder 2026-09-24.
   - [x] Author ran it 2026-09-24 14:13. Results (canvas 1920x1080 reference,
     CanvasScaler; screen was 2560x1440, scale 1.333):
     - **Exit button** = `Part_One/Canvas_MM/MainMenu/Bar/Button (Legacy) (3)`,
       55x55, sprite `function_icon_quit`, at (904.8, -488.9) from the canvas
       centre - bottom-right corner. Flags stack upward from above it.
     - **Settings button** = `.../Bar/Button (Legacy) (4)` ("Settings").
     - **Settings panel** = `.../MainMenu/Quitagme` (sic), off until opened.
       Background `Quitagme/Quitagme` 650x407 at (0, 89.8) of the panel, sprite
       `Rounded`; title "Settings" at y 240; close button 85x85 at (344.5,
       293.5); "Audio" at 137.9 + slider at 91; `Screenmodesettings` row
       (HorizontalLayoutGroup) at -19.8 holding "SCREEN MODE" (legacy Text,
       255x55) and a `TMP_Dropdown` + `TMPResolutionDropdown` (255x55) - a
       language row fits below at about -100 if the background grows ~100
       down. Hidden "On"/"Off" buttons at -187.8.
     - **Dialogue body** = `Dialogue_Default_Speech(Clone)` TMP: rect
       1010x125, font "Curse Casual Dialogue", 38pt auto-size 28-38,
       **overflow mode Overflow** (too much text spills past the box, it is
       not cut), word wrap on, line spacing 0, margins 0, top-left.
       Name label: 1010x85, 62pt, no auto-size.
     Files: `BepInEx/SMSModForge-menu*.json`, `SMSModForge-dialoguebox-*.json`
     in the game folder.
   - [x] Release plugin back in the game folder (2026-09-24 17:45; checked: no
     `LayoutDump`, no debug marker).
   - [x] Freeze on the first Chinese line (author, 2026-09-24): fallback
     fonts made letters on first use, loading e.g. Microsoft YaHei inside the
     frame. `PluginFonts.Prepare` now makes every letter a pack uses while it
     loads (logged: "N letter(s) ... made ready in X ms"), multi-atlas on.
     Confirmed fixed in game by the author 2026-09-24; in the changelog.
3. **The pack's own language** — optional manifest field, default `en`.
   Feeds: fallbacks, the menu's "not in your language" tag, the translator's
   source language, and an editor warning when base text looks like another
   language. Lines added in a translation: base empty + error; `PackTexts`
   must list an empty-base line so its translation has a key.
   - [x] 3a `PackTexts.Of(manifest, withEmpty)`: empty fields listed only on
     request, keyed AFTER every non-empty one (two passes), so no existing
     key moves. `Apply` fills an empty one from the translation.
   - [x] 3b `LanguageSession`: anything with no words of the pack's own when
     typed in (new line, or a field that was empty) stays empty in the pack;
     its words go to the translation only. `OwnWords(pack)` finds new fields.
     Keys taken from the pack in its own words, never from what is shown.
   - [x] 3c Translation files keep such an entry (Source includes an empty
     site only where the translation has words); check counts ignore
     empty-base sites with no words anywhere.
   - [x] 3d Plugin: player's language → own words → any translation
     (alphabetical by code) for an empty-base text, so it is never blank.
   - [x] 3e Validator error "only in <language>" (validation runs on own words).
   - [x] 3f `ModPack.Language` (manifest `language`, omitted when `en`, so no
     existing pack changes on save) + editor field; used by: menu tag (same
     rule as now with `en` replaced by the pack's language), MT source, the
     translate/editing-language pickers (exclude own language), runtime
     (own language counts as an available one), TextCheck "untranslated".
   - [x] 3g Wrong-language warning: script-based (Latin vs CJK/Cyrillic/
     Hangul/Kana) is certain; Latin-vs-Latin only on strong stop-word
     evidence (per line, not per dialogue: measured zero false alarms on
     19,077 game lines and ~4,100 lines of each shipped translation; catches
     ~50-80%). Tests: `LanguageGuessTests`, `PackLanguageTests`.
   - [x] The author's machine-translation rule (2026-09-24): a line written in
     the translation counts as translated; one emptied again is the machine's
     again. Already true; pinned by
     `PackLanguageTests.TheMachineLeavesWhatYouWroteAndTakesUpWhatYouEmptied`.
   - [x] "The pack's own words" is called **Default** in the interface
     (2026-09-24, author's request), in all four shipped languages.
   - [x] **Asked again, with an example** - settled by step 11 below: game
     lines in an extended conversation became translatable slots, so typing
     over one while a translation is up changes only the translation. (The
     author answered about machine
     translation in general): a line of the GAME's in a vanilla-based dialogue
     that the pack has not rewritten has no key in the pack, so it cannot be
     translated. Typed over while a translation is up, the typed words become
     the pack's own rewrite of that line (same as before step 3). Options:
     leave it; make such lines read-only while a translation is up; or have
     the pack take the line over (a rewrite identical to the game's) so it
     gets a key. Not decided - ask.
   - [x] Full suite run after step 3: 1796/1796 (2026-09-24; the screen walk
     needed language names, which are never translated, added to its names).
4. **Editor switches language without a restart** — texts become live;
   code-built texts refreshed. Done only when a live screen walk (switch, then
   read every tab) finds nothing left in the old language, and the full suite
   passes.
   Done 2026-09-24, in place (the "rebuild the window" idea was dropped: a
   closed window's bindings and handlers stay attached to the shared view
   model, and detaching them is the WPF trap that writes null into a
   selection).
   - [x] `{l:T}` is a live binding to `LocSource` wherever a binding can go
     (every element property, style setters); `Loc.Changed` announces a switch.
   - [x] The 8 `StringFormat={l:T}` became `FormatConverter` (like plurals).
   - [x] On a switch: VM rebinds (`RefreshLanguage`), every binding in the
     window is read again (converters), previews redraw (`LocText.Follow`),
     code-set tooltips are live (`LocText.Bind`). The editing-language list is
     relabelled in place, and its setter ignores the refill (a cleared box
     would otherwise have left the translation being edited).
   - [x] Proof: `TranslationScreenTests.SwitchingLanguageLeavesNoWordInTheOldOne`
     (English → fake language → walk: 808 texts, none English) and
     `LanguageSwitchTests` (pack, unsaved, selection, undo, editing language
     all kept). The walk now also flags any exact English text, and selects a
     dialogue line - which found the Kind/Jump/Duration lists and "(none)"
     showing English in every language; fixed.
   - [x] Full suite after step 4: 1799/1799 (13 min 11 s, was 12 min 7 s).
5. **Language on the game's main menu** — Settings panel + flags (with the
   language's own name; a flag alone is ambiguous), writes the plugin's
   `Language` setting, switches ModForge's own texts and fonts, rebuilds the
   mod list, shows the <50% warning. Waits on step 2(a).
   - Flags (author, 2026-09-24): download allowed - nine 80 px PNGs from
     flagcdn.com (public-domain national flags). English = United Kingdom,
     Spanish = Spain; pt-BR = Brazil, zh-Hans = China, de, fr, ja, ko, ru the
     obvious ones.
   - [x] Built 2026-09-24. `SMSModForge.PackPlugin/Flags/*.png` (embedded as
     `SMSModForge.Flags.<code>.png`); `Shared/LanguageChoice.cs` (offered
     languages = ModForge's + every language an installed pack is written in
     or translated into, English first then by name; names as the editor's;
     coverage counted exactly as the load log counts it; warning under 50%);
     `PackTexts.Count`; `PluginLanguage.Switch` (writes the config, switches
     GameTexts and fonts); `LanguageMenu.cs` (flags follow the Exit button,
     drawn just after its bar; Settings row copied from Screen mode with the
     game's scripts taken off, background grown 90 with everything on it kept
     in place); the warning reuses the save warning's window
     (`SaveLoadWarning.Window`), buttons Keep / Back to the previous; the pack
     list redraws. Tests: `LanguageChoiceTests` (9), GameTextsTests covers the
     new words.
   - [x] Seen in game by the author (2026-09-27): flags, the warning, packs in
     the new language. The Settings rows (Language, and Game's lines under it)
     were taken out at the author's request the same day ("I think we don't
     need those"): the flags choose the language, and the plugin's settings
     file keeps `Language` (auto / a pack-only language) and `GameLines`.
6. **Dialogue box limits** — done 2026-09-24.
   - [x] Measured in the game (step 2b): the box is 1010 x 125, 38 pt
     auto-sizing down to 28, overflow mode Overflow - so a line that does not
     fit at 28 is NOT cut, it runs out of the box below. The warning says
     "runs out of the box", not "will be cut".
   - [x] `Rendering/DialogueFit.cs`: lays the line out with the game's font
     (markup skipped, `<size>` counted), height taken the TMP way (last line
     gap removed), binary search to 0.1 pt. Predictions: ~80 characters fit
     at 38; ~160 shrink to 36.5; ~240 to 34; ~320+ spill.
   - [x] Under the Text box: "shrunk to N pt" in grey, the spill note in red,
     for whichever language is being edited. Text lines only.
   - [x] Pack check: a warning (`dialogue.lineTooLong`) only for lines that
     spill, in the default text and in each translation file.
   - [x] Chinese/Japanese/Korean: not measured (drawn by a Windows fallback
     font the editor cannot measure the same way) - "cannot tell", never a
     guessed number.
   - [x] Full suite after step 6: 1821/1821 (14 min 28 s).
   - [ ] Not yet checked in game: a line the editor says spills, and one right
     at a shrink boundary, against what the game draws.
7. **Browser engine as fallback** (WebView2 driving translate.google.com), used
   when the health check fails.
   - Author, 2026-09-24: download of Microsoft.Web.WebView2 (nuget.org)
     allowed. A captcha or block page in the browser STOPS the run and is
     shown to the person; the engine never works around it.
   - [x] Done 2026-09-24. Microsoft.Web.WebView2 1.0.4191.47.
     `GoogleWebPage` (codes: zh-Hans→zh-CN, zh-Hant→zh-TW, pt-BR→pt; result
     = `span[lang=<page code>]`, read until the same twice; the page silently
     swaps an unknown code for the last language, so `tl` is checked every
     time → "does not know the code", not retried); `GoogleTranslator(Asker,
     4500)` reuses markers/halving/doubtful; `View/BrowserTranslatorWindow`
     (visible small window, data in %LOCALAPPDATA%\SMSModForge\WebView2, no
     new windows or downloads; captcha/consent → run stops, window stays);
     `PackTranslationJob.Run(fallback:)` tries it only when the test sentence
     fails WITHOUT a block, tests it the same way, `Done.ByFallback` for the
     report; `TranslationRun.Check` says whether a failure was a block.
     Tests: `TranslationFallbackTests` (19); `LiveBrowserTranslationTests`
     (opt-in) passed against the real page 2026-09-24: test sentence (es),
     three lines to Chinese, an unknown code refused.
     Only when the test sentence fails: a mid-run "not working" stop does not
     switch engines (the next run's test does).
8. ~~**Translation memory**~~ — dropped 2026-09-24. The author allowed it only
   if context would not suffer; it would (the same sentence can need a
   different translation by speaker, politeness or what came before, which a
   memory reuses blindly). Consistency is already covered where it is safe:
   identical lines are sent to the machine once per run, and a line a person
   wrote is never overwritten.
9. **The other five languages** (de, fr, ja, ko, ru) — translated and
   assembled 2026-09-25; chunks are in `Tools/Translations/<code>/`.
   - Procedure (2026-09-24; last step, after 10 and 11): `python
     Tools/Translations/mt_extract.py` splits en.txt into 19 chunks in the
     session scratchpad `mt/src`. Work copies live in `mt/out/<code>/` (the
     old `01.txt` of each language predates today's chunking; assembly is by
     KEY, so it stays). `mt/mt_check_all.py <code> [NN] [--todo]` reads all of
     a language's chunks together, reports missing/damaged keys per chunk and
     with --todo writes the English still to do to `mt/todo/<code>/NN.txt`.
     Write each chunk to `mt/out/<code>/NN.txt` AND copy it to
     `Tools/Translations/<code>/` as it is done, so nothing is lost to a lost
     context. Follow `Tools/Translations/glossary.md`. Assemble at the end
     with `SMSMODFORGE_REFRESH_TRANSLATIONS=<mt/out>` (see its README; a new
     language may need its file created first).
   - [x] All 19 chunks, plus `20.txt`: the 28 Language-menu keys at the top of
     en.txt (`language.menu*`, `language.new.*`, `language.check.*`) that the
     chunking never took. Checked against the CURRENT en.txt, not the chunk
     snapshot (`mt/check_vs_en.py`): all five complete, no damaged codes.
   - [x] Assembled from `mt/refresh` (`mt/build_refresh.py`): de/fr/ru/ja/ko
     whole; es, pt-BR and zh-Hans get ONLY the three lines changed on purpose
     below, taken from their shipped files - their scratchpad copies lack ~115
     keys added after their own assembly and could lag a hand fix. The refresh
     keeps every key a file already has and overrides only what it is given.
     Key-by-key compare against a backup: exactly those three values changed in
     each, nothing added or lost. The writer also turned their line endings
     from LF to CRLF (en.txt is CRLF).
   - [x] EveryShippedTranslationReadsCleanly: all eight 4122/4122, 0 missing,
     0 to review.
   - [x] English fixed on the way, per "Default, not the pack's own words"
     (2026-09-24): `packText.file.help` ("shows the default text") and
     `translatePack.damaged` ("left in the default text"), with the es, pt-BR
     and zh-Hans lines to match.
   - [x] Russian top menu: Edit (_Правка) and Options (_Параметры) both took
     П; Options is now Пара_метры.
   - [x] Full suite after step 9: 1879/1879 (14 min 27 s).
   - [x] Copied all eight to the game's `BepInEx/plugins/SMSModForge/Languages`
     (byte-identical to the repo's).
   - [ ] Not yet seen by the author: the five new languages in the editor and
     in game (Cyrillic, Japanese and Korean in the game's fonts).
   - [x] Found while translating, decided by the author 2026-09-25 ("Yep, you can go ahead"; "Sounds good";
     "Is there really no way to translate these as well?"), all three done the same day:
     - `docs.variables.boolHasOnlyTwo` (+ `.term`) described a tick box; the editor shows two options. Now
       "Starts true / Starts false" and "this pair of choices", in all eight languages.
     - The place preview's toolbar chips (Body, Shadow, Blink, Wet, Reflection, Move, Rotate, Scale) were typed
       in English in `View/Controls/PlacePreview.cs`. Now `preview.gizmo.*`, bound with `LocText` so they follow
       a live switch (the toolbar is built once). The screen walk never saw them: the toolbar shows only once an
       object is picked. The two tutorials that name them use each language's chip words.
       `PlacePreviewLanguageTests` reads the drawn chips before and after switching to German.
     - The naming steps `first-steps / Give them a name` and `rules / Make one` accepted any name not starting
       with the English "New ", so outside English the placeholder passed. `TutorialCatalog.StillPlaceholder`
       rejects the placeholder in the editor's language and in English; `TutorialCheckTests` Naming_* run it in
       Spanish (both failed against the old check). The tutorial texts quote the translated default name.
     - Release check added the same day, at the author's request: `ReleaseReadinessTests.
       Every_language_has_this_versions_texts` (needs `SMSMODFORGE_LAST_RELEASE_EN`; see CLAUDE.md).
     - Full suite after these: 1883/1883 (16 min 9 s).
   - [x] Export and Publish offer to translate first (author's request 2026-09-25: "only translates whatever
     legitimately untranslated lines there are"). `MainViewModel.OfferTranslationBeforeExport`, per-pack
     Options ▸ Offer to translate before exporting; Yes opens the Translate window already running.
     "Still to translate" is one definition for the offer, the window and the run
     (`PackTranslationJob.Missing`/`StillToTranslate`). Two fixes so the offer does not come back after a
     full run: a line the machine returns unchanged (a name) is marked `# same in this language`
     (`TextFile.SameNote`; a person can write it too; dropped when the pack's words change), and a line
     with nothing to translate ("...") is not counted except where the language writes its own
     punctuation (zh, ja). Unattended the offer is No: `ExportTranslationOfferTests`.
     Full suite after: 1888/1888 (15 min 41 s).
     Terms fixed along the way: `Tools/Translations/terms-de-fr-ru-ja-ko.md`.
10. **Quest steps added after a player finished the quest** — the approved
    setting: *Leave it finished / Reopen at the new steps / Start it over*.
    Shape approved 2026-09-23 ("Your shape works"); details decided by the
    author 2026-09-24:
    - **Only the pack's own quests.** Game quests a pack extends stay as
      they are (finished stays finished; the load warning says so).
    - Reopen when the change is NOT a clean addition after where the player
      finished (steps added earlier, or the player finished it without doing
      some steps) → **leave it finished**.
    - "Start it over" = **back to not started**: progress cleared, the quest
      starts again the way it normally starts (start conditions or action).
    - **Saves from before this version**: steps never done that sit after
      the last finished one count as new.
    - Order change (mine, 2026-09-24): steps 10 and 11 before step 9, so the
      words they add go into the five new languages in one pass.
    - [x] Built 2026-09-24. `Shared/QuestGrowth.cs` decides (which tasks are
      new: the save's record, else the never-done ones after the last done,
      minus optional ones under "any one"/"by action"; clean = new tasks are
      the tail in depth-first order, every old non-optional task Completed,
      each new task's old parent leads to it (in order / any order) and had
      old subtasks). `QuestDef.WhenStepsAdded` (`whenStepsAdded`, written
      only when not the default and only on the pack's own quests - additive,
      no migration). Quests tab: three radio buttons + notes; docs section.
      Plugin: `QuestGrowthRuntime` once per loaded save beside `RepairOnce`
      (after the load warning is answered); Reopen writes the journal's
      quest/task entries to Active directly (no events, no instructions
      re-run - IL: Tasks.Activate needs only the task itself Inactive), then
      `ActivateTask` on the new ones; Start over = `DeactivateQuest`. Record:
      `__questSteps` in the pack save (JSON questKey → task keys), cleared on
      binding a loaded save, written at pack load (new game) and after the
      check. Tests: `QuestGrowthTests` (16), `QuestTabTests` radio test.
    - [ ] Not yet played in game: a finished quest reopened and started over
      by a pack update.
11. **The game's own lines inside conversations a pack extends** (asked
    2026-09-24; the author took all of these as decided):
    - Editor: while a translation is up, a game line in an extended
      conversation is translatable; auto-translate fills it by the same rule
      (a line the author wrote is left alone; one emptied goes back to the
      machine). The pack's default text never changes.
    - Game: a pack's translations of game lines are used only if the PLAYER
      says so - full-game translation mods exist. Asked once (remembered),
      the first time a loaded save has such a pack for the player's language;
      inside the existing load warning when both apply, not a second window.
      **Off until the player answers.** Changeable later from the language
      setting in the game's Settings panel (step 5).
    - [x] Built 2026-09-24. Keys: the same as the pack's own lines
      (`PackTexts.LineKey`), note = the game's words, so rewriting a line in
      the pack puts its translation out of date. `PackTexts` no longer lists
      a game line whose text the pack did not override (saved with `overrides`
      lacking "text" and the text blanked) - it was an "empty text" before,
      which moved no other key. Editor: `Localization/GameLines` (plain-text
      game lines whose words are unchanged, from the catalogue);
      `PackTranslations.Source` adds them under "The game's own lines in ..."
      (so auto-translate, checks and file writing all include them);
      `LanguageSession.Slots` makes them slots with own = the game's words -
      which also FIXES typing over one while a translation is up (it used to
      become the pack's rewrite, for every player). Game: plugin config
      `Language.GameLines` = ask/on/off (`Shared/GameLineChoice`);
      `PackManifest.Translation` kept; `GameLineTranslations` writes usable
      lines (note == the game line's `Node.Text`) straight onto the game's
      nodes after the injector, idempotent, no restore needed (conversations
      are rebuilt per scene load); `SaveLoadWarning` asks (in the warning as
      a choice block, default "keep", recorded on Continue; else its own
      window), applies at once on yes; Settings row "GAME'S LINES" under
      Language (background grows 2 rows). Tests: `GameLinesTests` (12),
      GameTextsTests covers the words.
    - [x] Full suite after steps 5, 7, 10 and 11: 1879/1879 (15 min 1 s,
      2026-09-24).
    - [x] Changed 2026-09-27 at the author's request ("make it obligatory,
      rather than ask whether we should, and just warn"): the game's lines are
      always shown in a pack's translation when it has them; the question is
      now `Shared/GameLineNotice` - told once (plugin config
      `GameLinesNoticeShown`), inside the save warning or on its own with one
      button. `GameLineChoice`, the `GameLines` setting and the warning
      window's answer picker are gone.
    - [ ] Not yet seen in game: the notice (both forms) and a translated game
      line. Not in the pack check's line-length warning yet (game lines are
      not sites).

**Asked 2026-09-26** (steps 12-17). Decided by the author, not to be re-asked:
XUnity link = "Live on/off + next start" (picking XUnity's language turns its
translation on at once, any other turns it off at once, and the pick is written
into XUnity's settings for its next start); typing sound = "Pack lines only".
**Revised by the author the same day:** XUnity cannot change language while
running (its `Settings.Language` is read once, in `Settings.Configure` at
start; live it can only toggle, Alt+T), so ModForge's language choice does NOT
touch XUnity at all - no toggle, no settings write. Kept: detection, `auto`
following XUnity's language, its language offered, the leave-alone callback,
its language in the save record. Also asked: a small "Mod language" heading
over the flags, and a stronger mark on the language in use.

12. **Packs on and off from the main menu** — a checkbox left of each pack's
    row. One set for every save (the menu comes before a save is chosen);
    stored in the plugin's config. A switched-off pack is not loaded, and is
    greyed on the menu. The language menu's offered languages and its <50%
    warning count switched-on packs only.
13. **A switched-off pack keeps its progress** — today only running packs'
    files are carried into a new save (autosave and manual), so a pack's data
    would be dropped from every save made while it is off. Carry every
    `SMSModForge_<id>.json` of the loaded slot whose pack is not running,
    unchanged, into each slot the session writes (not-installed packs too).
14. **A ModForge record in each save** — `SMSModForge-save.json` in the slot
    (NOT the `SMSModForge_*.json` pack-file pattern): ModForge version, game
    build, when, language, XUnity (if loaded) and its language, and every pack
    the save has data for with its name, version and state (running / off /
    not installed). Written with the pack files.
15. **The load warning, compatible and clearer** — a save with data for a pack
    that is switched off is told apart from one that is not installed, both
    listed in the window's scrolling details (like the language warning),
    named from the save's record (name + version) where it has one, the pack
    id otherwise. Switched off: "turn it on from the main menu".
16. **XUnity.AutoTranslator** (`gravydevsupreme.xunity.autotranslator`) -
    detect it; ModForge on `auto` starts in XUnity's language
    (`BepInEx/config/AutoTranslatorConfig.ini` `[General] Language`); XUnity's
    language is offered in our menu; our buttons drive XUnity as decided; and
    XUnity is told not to translate what ModForge has already translated
    (its public `RegisterOnTranslatingCallback`), so nothing is translated twice.
    XUnity is not installed in the author's game: needs testing with a
    translation mod installed.
17. **Typing sound on pack lines** — the game times the typewriter by the raw
    text, tags included (`Typewriter.GetDuration` = `text.Length / freq`), so
    the sound runs past the text and a click in that time only stops it. For
    pack lines only: time it by the visible characters.

Status of 12-17 (2026-09-26):
- [x] 12: `PackSwitches`/`PackSwitchSetting` (config `[Packs] SwitchedOff`),
  `PackSwitchBox` on each pack's first menu row (stands on the hidden bullet,
  placed from TMP's laid-out characters), greyed row, loader skip (archive
  released), `LanguageMenu.PacksChanged` rebuilds the flags and refills the
  Settings list; the in-use language stays offered. Seen in game (author,
  2026-09-26): on the menu's first draw the names ran into the boxes and a
  redraw put them right - the row's text is laid out again after the box is
  placed, so `PackSwitchFollow` re-reads the laid-out text every frame and
  moves the box with it.
- [x] 13: `SaveCarry` - captured at binding (`SaveRecord.Carried`), written at
  `CommitSleepSave` (slot 1, Monday slot 2) and `PackManualSaveSync.CopyPacks`;
  `SaveRecord.Stale` taken out of the target. A file that could not be read is
  never taken out. New-slot saves find the slot from the saves folder, so they
  work with every pack off.
- [x] 14: `SMSModForge-save.json` (`SaveRecord`), written at the same points;
  states running / off / notLoaded / notInstalled; a carried pack's version is
  the loaded record's, empty when unknown (not guessed).
- [x] 15: `SaveLoadChecks.Absent` + `WarningFor`: three headings (switched off,
  not loaded, not installed), versions from the record.
- [x] 16: `XUnityLink` (soft dependency; settings read from the ini;
  `RegisterOnTranslatingCallback` ignores texts not in its FromLanguage, and
  ModForge's own objects when ModForge is not in it). `auto` follows XUnity;
  "Same as XUnity.AutoTranslator" in the list. The toggle and settings write
  were built, then taken out on the revision above.
- [x] Flags: "Mod language" heading (18 pt, light grey, no backing - the author
  asked for it larger and without the black) on top of the column; the row in use gets an orange backing, a white outline on its
  flag and a bold white name. Text in all eight languages (refresh5). Full suite after
  these: 1912/1912 (15 min 14 s).
- [x] 17: `TypewriterTiming` - postfixes on `Typewriter.GetDuration` (scaled
  by `RichText.VisibleLength`) and `TextReference.AreAllCharactersVisible`
  (TMP only), for lines registered by `DialogueBuilder` and
  `VanillaDialogueInjector.SetText`; `{Var}`/`[PV:]` places match anything
  (`TextTemplates`).
- [x] Texts: game.save.* reworded/new and game.language.autoXUnity in all eight
  languages (refresh4; chunk sources updated). Full suite 1913/1913
  (18 min 52 s); Release plugin builds; language files copied to the game.
- [ ] Not yet seen in game: the boxes (placement, clicks, redraw), a pack off
  across an autosave and a manual save, the three-heading warning, the record,
  the typing timing. XUnity needs a game with it installed (not the author's).

**Asked 2026-09-26, later** (editor). Decided by the author, not to be re-asked:
names in other alphabets = "Google suggests, you check".
- [x] Written in: a grey note under it (`home.packLanguage.note`), and a First
  steps tutorial step (`writtenIn`, anchor `panel:packLanguage`) placed before
  the version step, so no existing tutorial text changed.
- [x] The list shown before a save did not show translation edits (the
  author's report). Switching away wrote the file on the spot. Now a language
  switched away from is HELD (`_heldTranslations`, the file text as it would be
  written) until the save; `TranslationChangesToSave` + `TranslationDiff` list
  each language's word changes; held counts as unsaved; opening/new pack drops
  it after the usual question; anything that works on the files themselves
  (`WithTranslationFilesCurrent`) writes held files first.
- [x] Editing in moved from the menu bar to the right end of the tab row
  (overlay in a Grid); `KeepTabsClearOfLanguage` gives the tab header panel a
  right margin the width of the list, so tabs wrap before reaching it
  (measured by `EditingLanguagePlacementTests`, control fails without it).
- [x] Character names in machine translation: "Translate character names
  too", off by default, per pack (EditorPrefs). Off: name fields filled
  directly (as written, marked same; or the checked spelling in another
  alphabet), names in lines protected as codes (`PackNames.Find`: capitalised,
  whole words, longest first, never inside a code) and put back in the
  language's spelling; a lost or doubled name rejects the line. The game's cast
  counts when a line mentions them; the player ("You") never does. Other
  alphabets: the window's names grid, prefilled from the translation file or
  remembered spellings; pressing Translate with blanks asks Google once
  ("My name is X." / "Hello, X!" framed against a marker) and stops for the
  author to check; blanks after that stay Latin. Not done: re-spelling names
  inside lines already translated.
- [x] Texts in all eight languages (refresh6).
- [x] Full suite: 1933/1934. The one failure (`TheColourResetIsSomewhereAnAuthorCanReachIt`)
  is the author's saved preview zoom (1.5, read from the real prefs): the
  details pane is 270 px and the name-colour Reset is clipped. Pre-existing;
  offered to the author as its own task.
- [ ] Not yet seen in the real window: the names grid with real Google
  suggestions (the tests use a fake translator).
- [x] Google's check in the Translate window (author, 2026-09-26: "integrate
  the captcha directly into the translation window"). A block on the free
  service carries the check page (`ServiceRefused.CheckAddress`: the "sorry"
  page, or the request itself); `TranslationRun.AskAPerson` shows it in a
  WebView2 panel over the languages (same profile as the website window),
  waits until the page leaves the check, then copies Google's cookies into
  the translator's own `CookieContainer` (`GoogleTranslator.TakeCookies`,
  Google's domains only) and asks again - once; refused again at once, the
  run stops. At the test sentence (`CheckWithPerson`) and mid-run
  (`TranslationRun.Person`) and in the names step. A passed check also lets
  the website fallback run on a block, which before it never did (that "no
  website on a block" rule was mine, not the author's). ModForge never
  answers a check. Tests: `PersonCheckTests` (control fails without it).
- [ ] Not yet seen against a real block: whether Google accepts the passed
  check's cookie for translate.googleapis.com (if not, the website carries on).
- [x] Author's report: stuck on "Asking Google how the names are spelled", no
  check. The check's WebView2 was started inside a collapsed panel and never
  initialised (it only starts once shown). Now the panel shows first, start-up
  is given 30 s, and Stop reaches it. Not testable under the harness (TestMode
  never shows a check); needs the author's retry.
- [x] Author's retry (2026-09-27): the panel showed Google's refusal page for
  translate.googleapis.com - "Our systems have detected unusual traffic...
  Please try your request again later." - with NO captcha on it. So a check is
  now three outcomes (`TranslationRun.CheckOutcome`): passed, not passed,
  nothing to answer (the page has no captcha form/frame after ~8 s). Nothing
  to answer: at the start the website fallback runs; mid-run the run switches
  to it (`TranslationRun.Instead`, `SwitchedTo`) and the languages after go on
  there; the names step asks the website (`SuggestSpellings(instead:)`). One
  website window shared per run (`OpenWebsite`).
- [x] Author's retry: the website route worked, but the names list never came
  back - `SpelledLanguages` counted only ENABLED boxes, and every box is
  disabled while the window works, so the list hid during the check and nothing
  rebuilt it. Ticked boxes count while running; rebuilt after suggestions. The
  website window's page is locked (`IsEnabled = false` on the WebView2) while
  ModForge drives it, and unlocked when Google asks something (blocked/consent).
  Neither is covered by an automated test (no live Google, no WebView2 run).
- [x] Author's retry: the website page still took clicks - `IsEnabled` on the
  WPF WebView2 does not reach the browser's own window. Now a document-created
  script (`LockScript`, top frame only) swallows clicks and keys while ModForge
  works it; `Unlock` removes the script and clears the flag. Not testable
  without a live WebView2 run.
- [x] Chinese name suggestions empty: most likely the page writes markers
  full-width ("％％8000％％"), so no line of a Chinese batch could be read.
  `GoogleTranslator.HalfWidthMarkers` reads them back (only markers holding a
  full-width character; a line's own "50%" untouched). `WideMarkerTests`
  (control fails without). Cause inferred, not seen: needs the author's retry.
- [x] Still empty after that (author, 2026-09-26). Second suspect: the marker
  sentence and the name sentence are separate answers, punctuated separately
  ("！" vs "!", "，" vs ","), and `PackNames.Between` compared them exactly.
  Now compared through `Loose` (full-width forms and 。、　 as ASCII) with the
  ending punctuation ignored. And no more guessing: a language answered but
  unread shows Google's answers under the progress text
  (`translatePack.spellings.unread`, all 8 languages). Tests in
  `WideMarkerTests` (control: `Loose` as identity fails all five).
- [x] The diagnostic caught it (author, 2026-09-26): Google answered
  "我叫 %%0%%%。" - a third percent sign, taken from the line marker after it -
  so the marker sentence never matched and every name was lost. `Between` now
  matches `%+0%+` and trims a stray `%` off the name and the sentence. The name
  shown, Solid Snake, came back in Latin ("我叫 Solid Snake。") and is rightly
  refused; others should now read. Control: the old pattern fails three.
- [x] Still all blank (author, 2026-09-26). Replayed the exact zh batch
  (29 names) on translate.google.com: clean, 28 of 29 spelled, and the
  editor's reading gets all 28 from it. The author's answer ("我叫 %%0%%%。")
  matches neither the website's wording nor its markers, so it came from the
  free service (client=at, which now 429s from here too) - which left the
  names in Latin. `SuggestSpellings` now asks `instead` (the website) once for
  the names the first engine did not spell, unless the website already
  answered; website refusal keeps what the free service spelled. Four tests in
  `PersonCheckTests`; control (fallback off) fails the first. Known: the
  website renders some names as words (Guilty 有罪, Maiden 少女, Amber 琥珀,
  Viper 毒蛇, Neon 霓虹灯) - that is the "check each" step.
- [x] Author (2026-09-26): Zhen still listed; website never started; names
  empty on reopening. (1) `CastNames` had nulls (zh for Japanese names, Zhen
  ko/zh) - so those game names were listed. Filled every one (commonest form;
  genders read from the game's lines); `CastNamesTests` now fails on any null.
  (2) The spellings step went to the website only on a check page with nothing
  to answer; a plain refusal (429 with no page, timeout) just failed. Now any
  refusal a person cannot pass goes to `instead`; NotPassed (they stopped)
  still stops. (3) Suggestions lived only in the window's cells, so closing it
  lost them and the next press re-asked Google. Now `EditorPrefs.NameSuggestion`
  keeps them (empty = asked, none) until checked; typed spellings are saved as
  checked at once; `_suggestedFor` replaced by `NeedsAsking` (blank and never
  asked). Not unit-tested: writing prefs from tests would write the author's
  own prefs.json. Controls: 429 test fails with the old outcome; a null back in
  the table fails CastNamesTests.
- [x] Character pronouns (author, 2026-09-27): "a new field ... called
  pronouns ... Default should be empty so it throws an error ... Male, Female,
  and Neutral ... ingrained within the translation work", then "make sure the
  Vanilla characters have their intended gender assigned ... from all the
  vanilla extracts". Read "error to the player" as the author's validation
  list (a player never sees a pack's characters' settings).
  - `CharacterDef.Pronouns` (enum Unset/Male/Female/Neutral), saved as
    `"pronouns": "female"`, omitted when unset and for player/vanilla; read
    forgivingly (unknown -> Unset). Not a migration: nothing converts, an old
    pack simply has none chosen - hence the "Before you update" note.
  - `EffectivePronouns`: player Male (game lines: "{PC} ... his"), vanilla from
    `VanillaPronouns` (118 = 50 M, 67 F, Subject IX-Delta Neutral), from
    pronouns right after names in the dialogue extracts + self-descriptions,
    bust art for the minor ones (contact sheets in scratch). Test: every cast
    member covered, no stranger names.
  - Characters tab: Pronouns combo under the name, greyed for player/vanilla,
    "Not chosen yet" beside it. Validator: Error `character.pronounsMissing`.
    Docs bullet. 9 keys, all 8 languages (refresh12).
  - Translation: `GenderHints` - speaker hint "%%9501%% She says: %%9502%%"
    in front, "(she)"/"(he)" after each kept name's marker, only for
    languages that agree (ru/es/fr/pt/de/...), hint words in the pack's own
    language (en/pt/es/fr/de/ru; others none). Measured on the website first:
    speaker + inline name hints work (ru/es), a separate "X is a woman."
    sentence does not. `Remove` refuses anything uncertain (counts, empty,
    too long, speaker not first, name hint not after a marker) and the run
    re-asks those lines unhinted. Neutral/Unset: no hint.
  - Controls: Remove as identity, validator check off, one name out of the
    table - each fails its tests.
  - Author follow-up: River is male (was female from the bust; no line says).
    John Dick (voice) could not get pronouns: the whole middle pane was bound
    to SelectedOutfit, so a character with no outfits showed nothing - name,
    colour and voice included, a gap older than pronouns. Pane now visible per
    SelectedCharacter; outfit-only groups `FallbackValue=Collapsed`;
    AddCharacter selects the new character. `VoiceCharacterPanelTests`
    (controls: old pane binding fails "not on screen"; no fallback fails
    "sprites panel shows").
  - Then (author): speech expressions and Default outfit hidden for a voice
    (`HasBust`), the player included. The player's typing voice stays
    editable - it is per pack by design (CharacterViewModel.IsPlayer) - but it
    reaches only the pack's own "You" lines: the plugin mints its own Actor
    for key "player" (RuntimeActorFactory), and VanillaVoiceOverrides writes
    to the game's Actors only for bustSource Vanilla. The game's "You" voice is
    not in VanillaSpeech (84 speakers, no player), so an unset pack "You"
    types at 45 / 1.0-1.5, which may not be the game's.
  - Then (author): "grey out all typewriter fields for You" and make
    SMSAndroids' 45 / 0.4-0.7 "the default for You" (author's word that it is
    the game's). `VanillaSpeech.Player` (kept out of All/For - the plugin asks
    For() about faces); editor `Speaker` returns it for the player,
    `CanEditVoice` greys `TypewriterFields`; plugin `ApplyTypewriter` ignores a
    pack's player voice and uses Player; `PackMigration.ForgetThePlayersVoice`
    drops a stored one with `migration.playerVoice`. `PlayerVoiceTests` +
    window test; controls: each of the three off fails its test. Needs the
    rebuilt plugin in the game to be heard.
- [x] Translate window layout: languages box at its own height (no scroll),
  names box takes the rest and scrolls; opens wide enough for every name
  column; MinHeight measured so every language shows, capped at the work area
  (`FitToContent`; test at 200x200, control fails without it).
- [x] Amelia/Adrian in the names list (author thought: leftover records) -
  not records: two of the pack's own lines name them. But the game-cast list
  was every SpokenName, descriptions included ("Android", "Technician"), so
  those would have been kept untranslated. Now `CastNames`: the game's given
  names only, with fixed spellings in ru/ja/ko/zh where certain (null where
  not - mostly Japanese names in Chinese), always kept even with "translate
  names" ticked, and not listed in the grid when spelled for every shown
  language. No migration: nothing in any pack changes.

- [x] Overnight batch (author, 2026-09-27), while a Release build translated
  SMSAndroids in the background - so no Release builds, no writes into the
  Androids folder, no Google requests from this machine.
  - Auto SFX with translations: the dispatcher already scanned the pack's own
    words (`OriginalTextKey`); `PackTexts.CueText` also covers a line with no
    own words (was "", silent). Pack lines in the GAME's conversations never
    scanned cues at all - `VanillaDialogueInjector` now keeps `Cues` per line
    (added lines, and lines whose text the pack rewrote) and plays them on
    EventStartNext after the actions. `SfxCueTests` (controls fail).
  - Wallpapers missing (a player's report, screenshot of an Elfenlied pack):
    his Sprite path was a full `Z:\...\Elfenlied\Wallpapers\Elf.png`. The
    plugin reads only the archive, so it was skipped (log warning only). Fixes:
    `Shared/PackPaths.FindByEnding` + `PackArchive.Resolve` find a full path by
    its tail (folder boundary), for every asset kind; wallpapers also try
    `externalSpritePath`; `PackMigration.WallpapersByTheirPathInThePack`
    (needs packRoot - `Apply(pack, root)`); validator `CheckFile` reported full
    paths as found (Path.Combine returns them unchanged) - now
    `art.fullPathInPack` / `art.fullPathOutsidePack`, plus `wallpaper.noImage`.
    Plugin Update: every per-frame system in its own try/catch
    (`FailedThisFrame`, logged once) - one throwing used to end the frame and
    the wallpaper tick after it. Researched and ruled out: the game's
    `InstructionEnableChildrenUpTo` (5 uses, all trait displays). Found: the
    wallpaper List is a 5-column GridLayoutGroup, 1500x700, no mask/scroll ->
    20 buttons fit; 14 are the game's, so only 6 mod wallpapers fit before the
    rest fall off the panel. `WallpaperListFit` (plugin) shrinks cell+spacing
    in proportion when more buttons are active than fit (21 -> 85%, 6x5), and
    restores the game's size when they fit - untested in game. The old host
    mod's 4 buttons gate on its own SaveManager flags - no ModForge cause
    found beyond the overflow.
  - Language menus review: pack translation items and the Language menu are
    consistent. Spell check was fixed to en-US (`Language="en-US"` on both node
    boxes, speller cache per text only) - now `MainViewModel.TextLanguage`
    (editing language, else pack's own; CultureInfo specific) and
    `Speller.UseLanguage` (cache per language). Docs lacked Written in,
    Editing in and Translate the pack - added; "Passing one on" and "machine"
    rewritten for layering/edit mode.
  - UI edit mode: `UiTextMarks` (adorner; keys from bindings to LocSource,
    FormatConverter/PluralConverter params, TextBlock inlines; clipped by
    ScrollContentPresenters; 2 ms per walk on the start tab, 47 texts),
    `UiTextEditWindow` (search every key; plural forms per language; gap
    check), `UiTextEdits` (sparse file of corrections in the user folder;
    `Layer` over the shipped file - also applied to a whole user file now, so
    an update's new texts still arrive; `Loc.Language.ShippedPath`). English:
    disabled. Tests: UiTextEditsTests, UiTextMarksTests (+controls).
  - Texts: rounds 14 and 15 (41 keys, all 8 languages); release completeness
    check passes.

### Audit of the author's earlier list (2026-09-24, checked against the code)

The author asked to be sure every item of their earlier list was really done.
Status per item, with where it was checked:

- [x] "New or update a translation" dialog is specific: `language.new.prompt`
  lists codes with names, says any Windows code works, and names the ones the
  author already has (`MainWindow.NewTranslation`).
- [x] Starmaker photo on Scenes: payout through the game's own `PhotoBonus`
  (so its good/bad and trait logic apply) and a gallery row - done
  (`StarmakerPhotos`, `StarmakerGallery`). ONE box for both, as the author
  concluded in an earlier session (sub-boxes added and reverted 2026-09-24 -
  do not add them again). **Unverified in game:** whether the good/bad VISUALS show for a pack
  photo (the game's `PhotoBonus` decides good/bad; the Trigger's `flash` signal
  is not raised by ModForge).
- [x] Modlist version colours: game build mismatch = red; pack needs a newer
  ModForge = red; pack made with an older ModForge = amber (works; by design).
  ModForge itself on another game build: the menu's ModForge row turns red
  with "made for the game's 1.8E" (`ForgeVersion.GameBuild`, 2026-09-24) - the
  second half of the author's original request #6; the first half (the game
  version REPLACES the ModForge warning on a pack's row) was done earlier.
- [x] Expression dropdown linked to the Speech expressions - fully only as of
  2026-09-24 (`ExpressionSyncTests`); the earlier fix covered the game's four.
- [x] Bust preview bigger/smaller: ½x-2x size picker, the panel grows with it.
- [x] Quest tasks with the same name: id shown beside a shared name; the
  action stores the task's token, so the pack itself never confused them.
- [x] Quest update after a player finished it: design approved in an earlier
  session (per quest: Leave it finished [default] / Reopen at the new steps /
  Start it over); built 2026-09-24 as step 10, not yet played in game.
- [x] Bool variable default as two buttons (Starts true / Starts false).
- [x] Copy/pasted Bool condition could not be switched: the False converter
  bug, fixed with a test.
- [~] Undo/redo delay: the 640 ms pack check removed earlier. Measured
  2026-09-24 on the author's pack (`UndoTimingTests`, opt-in): undo of a moved
  line was ~0.4 s + ~3.5 s redraw; the redraw was the spell checker (~40 ms a
  row). Answers now remembered + rows checked after drawing: ~0.75 s + ~0.7 s.
  **Still to do:** undo restores the whole pack and rebuilds every view; a
  restore of only what changed would take the rest.
- [x] + Root copies speaker, expression, outfit; naming an actor brings back
  the look they last wore in that conversation.
- [x] Spelling squiggles in the game-look node rows.
- [x] Beachside Visitors (author's pack): resolved in an earlier session. The
  quest trace WAS run: `TalkToAmberAbout` does start and unhide; what was
  missing was the counter on a subtask, which the journal never draws - fixed
  (`Shared/QuestCounters.cs`). (The 2026-09-24 audit first missed this.)
- [ ] New ask (2026-09-24): translating the game's own lines inside
  conversations a pack extends, with the PLAYER deciding - step 11.

---

## Next (1.2.0)

### ~~A UI tab~~ — done

Shipped. The question this entry said had to be settled first — **which parts of
the UI a pack should be able to touch** — was answered **both**, in one tab
rather than two.

The reason they share a tab is that they are the same job from an author's side:
a tree of objects, the properties of the selected one, and a picture of the
result. Splitting them would have asked an author to decide up front which kind
of thing they were making, when the honest answer is usually a bit of both — a
screen anchored to the game's own holds vanilla objects the pack has changed and
objects the pack invented, side by side, and both are equally the point.

What that cost, and what made it affordable, was storing only the differences on
a vanilla-based screen. An object nobody touches is not in the pack, so altering
one label in the shop stays a pack that alters one label rather than a copy of
the shop that breaks when the game is patched.

### ~~Auto-update, from the GitHub releases~~ — done

Shipped. The three points this entry raised were settled like this:

- **Notify, or install.** Install. The editor stages the new build beside its
  settings, and a copy of that build started from there does the swap once the
  editor has exited — Windows will not let a running exe be overwritten. The
  plugin is replaced in the game folder first, while the editor is still up to
  say so if it fails, and only the files ModForge owns are touched.
- **Offline and rate limits.** The check is started and not awaited, and every
  way it can fail — no network, no releases, a rate limit, a repository that is
  not public — comes back as "no update" without a word. Only the manual check
  answers either way, because somebody asked it.
- **Opt out.** Read before the request, so off means no request.

One thing this depends on that is not code: **the repository has to be public**
for the releases API to answer. Until it is, every check finds nothing, exactly
as it does offline.
