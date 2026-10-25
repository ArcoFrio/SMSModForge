# ModForge in the game: its look, and its notice on the main menu

What ModForge's plugin puts on screen in Starmaker Story (1.8.0), how it is
built, and the convention another mod can follow to put a notice of its own
on the same menu without the two covering each other.

## The look

ModForge draws its windows, the pack list on the main menu and the language
flags the way the game draws its own UI, so they read as part of the game:

- **The game's parts.** Its rounded panel sprites (`Semi Rounded`,
  `Semi Rounded - Stroke 2px`, `Rounded`, `Circle - 256`), its title font
  (`Barton SDF`) and interface font (`Curse Casual SDF`), its navy
  (`#000E20`, raised `#0A1A33`, hover `#142849`) and its white pill buttons
  with dark words. Anything an update renamed is drawn or stood in for.
- **The game's blur.** Behind a window, a copy of the game's own blur volume
  (`blur volume` on the main menu, `NanoSave_Volume` in the game), held for
  as long as any ModForge window is up.
- **Linear light.** The game renders in linear colour space: a see-through
  colour darkens far less than its alpha says (black at 0.84 took a white
  button from 255 to 112). Alphas are measured, not guessed.
- **ModForge's mark.** Amber (`#F5A93B`) where it is ModForge speaking: a lit
  line along the top of a panel, headings in lists, the main button's hover,
  and the emblem - an anvil on an amber disc with a slow glow - beside a
  window's title and the pack list's heading.
- **What is chosen** (the language in use, beside the flags) is marked in
  amber on the navy - an amber frame, its words and outline in amber - never
  as dark words on an amber fill: the author did not like dark on orange.
- **Motion.** A window fades in while its panel springs up to size, and fades
  out a little smaller; buttons lift under the pointer and dip when pressed.
  All on the real clock, so they move with the game paused.

Code: `SMSModForge.PackPlugin/ForgeLook.cs` (colours, the game's parts, panel
and button builders, `ForgeButton`, `ForgeAppear`), `ForgeEmblem.cs` (the
emblem and `GameBlur`), `SaveLoadWarning.Window` (every ModForge window is
built there), `MenuRowLook.cs` and `MenuAboutButton.cs` (the pack list).

## The notice on the main menu

The first time the game starts with ModForge, a small window says that it is
installed, where packs go and where they are listed - two lines. Rules
(`Shared/ForgeNotice.cs`, tested in `SMSModForge.Tests/ForgeNoticeTests.cs`):

- **Counted from 1.8.0**, the first version with the notice. Nobody has a
  record before it, so every player - one of an older version included - sees
  the whole notice once. A player whose ModForge settings file was already
  there gets a line on what is new for them as well.
- **After an update**, only when the update changes something for players: a
  heading and a line or two per version (`ForgeNotice.VersionsWithNews`, the
  lines in the language files under `game.notice.news.<version>`). Nothing
  otherwise - a notice after every update teaches players to close it unread.
- **Kept in the plugin's own settings**, `[Notices] Last shown`, written when
  the player closes the window: one never read comes back next time.
- **In the player's language**, which follows the computer's (Unity's
  `Application.systemLanguage`) until they pick one with the flags.
- **Shown again** from the "?" on the pack list's heading.

Code: `SMSModForge.PackPlugin/MainMenuNotice.cs`.

## One notice at a time, from any mod

Two mods' notices up at once would cover each other. The convention ModForge
follows, which any mod can follow without knowing about ModForge:

- A notice window is a **root GameObject whose name starts with
  `ModNotice`** (ModForge's is `ModNotice ModForge`).
- A mod puts its notice up **only while there is no such object** in any
  loaded scene (or the scene kept across loads), and otherwise looks again on
  the next frame.
- Unity runs every mod's `Update` on one thread, one after another, so the
  first to look and find none puts its own up in that same call, and every
  mod after it sees it and waits.
- Wait until the menu has settled (ModForge waits a second after the main
  menu's scene loads) and for the scene to have an event system, so the
  window can be closed.

`MainMenuNotice.AnotherNoticeIsUp` is the whole check.

## Testing in the game without touching the author's saves

The author's rule (2026-10-24): a check run in the game must not change any of
their saves - the game's own files or any mod's beside them. "You can do
whatever you want to test with [a save of the checks' own], just make sure
previous testing doesn't break future testing."

**Why a slot of its own is not enough.** Every copy of the game saves into
`%UserProfile%\AppData\LocalLow\<company>\<product>\Saves` - one
`NANOSAVE_xxxx` folder per slot - so a test copy of the game shares the
author's folder. And Game Creator keeps its list of save slots inside
**slot 0** (`NANOSAVE_0000\SAVE.GZ`, key `data-0000-slots`), a save shared by
every slot: a slot copied in beside the author's cannot even be loaded until
it is in that list, and any save the test copy made would rewrite it. So
ModForge's checks keep the test copy out of the author's folder altogether:

- **The test copy is renamed.** `Tools/Game/setup-test-game.ps1` changes the
  product name in the test copy's own game data (`globalgamemanagers` and
  `app.info`, under `Starmaker Story_Data`) from `Starmaker Story` to
  `Starmaker Tests` - the same length, so nothing after it moves. Unity then
  gives it its own saves folder (`...\Arvus Games\Starmaker Tests\Saves`) and
  its own settings in the registry (window size included), so nothing of the
  author's game is in its reach. The launcher refuses a test copy that is not
  renamed. A mod that builds the saves path itself must read it from Unity
  (`Application.persistentDataPath` + `Saves`) to follow the rename -
  ModForge's `Plugin.SavesRoot` does.
- **Its saves are a copy of the author's.** `Tools/Game/make-test-save.ps1`
  copies the author's slot 0 and one of their slots (slot 1 by default) -
  whole folders, so every mod's files come too - and keeps them clean beside
  the test copy (`TestSave\Saves`). The author's files are only read: hashed
  before and after, and the script fails if one changed.
- **Marked as the test save.** Its name (`m_Title` in `game_data.meta`) is
  `<color=#F5A93B>ModForge test save</color>`: amber in the game's Load window.
- **Clean before every run.** A check may do anything to it; the launcher puts
  the clean copy back before every start, so no check is affected by the one
  before it.
- **Nothing else is loaded.** The launcher plays only the test save (or the
  main menu); the test bridge's `load` refuses any other slot.
- **Not while the author plays.** Nothing starts while another copy of the
  game is running, unless the author has said it is not theirs - and never
  while one is in front, where somebody may be in it.
- **Proven, not assumed.** After a run: the author's slots byte for byte the
  same as the copies taken before it, no file in their saves folder written,
  their registry window settings unchanged.

Another mod can follow the same arrangement: its own test copy, renamed to a
name of its own, with its own clean saves.
