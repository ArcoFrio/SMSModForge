# What to recheck when Starmaker Story updates

ModForge copies, reads and hooks into Starmaker Story in many places. Everything
here was made or checked against **1.8E**. When the game updates (1.9 reworks
nearly everything but its frameworks, the UI included), go down this list
before releasing a ModForge for it, and re-extract what the "how" column says.

**Add to this file in the same change** whenever something leans on how the
game is built: a name, a path, a number, a reflected field, a script read by
hand. A line nobody adds is a break nobody finds until a player does.

Game install used for 1.8E: `D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E`
(the plugin build's output; `Directory.Build.props`). Extracted project: the
author's AssetRipper export (they will say where the 1.9 one is).

---

## 1. Data shipped with the editor, made from the game

Each is a snapshot; a new game version needs each one re-made and diffed.

| Data | Where it lives | How it was made |
|---|---|---|
| Bust art, per character/outfit | `SMSModForge/Resources/VanillaBustArt/` | Unity bust extractor (`Tools/UnityEditor/`), `Tools/regen_vanilla_busts.py` |
| Full-size faces (blink, mouths, expressions) | `Resources/VanillaBustFaces/` | `Tools/MakeFullSizeFaces.py` |
| Which busts have which faces | `Shared/VanillaBustExpressions.cs` | `Tools/regen_vanilla_expressions.py` |
| Bust list, names, characters | `SMSModForge/Model/VanillaBusts.cs`, `Shared/VanillaCastData.cs` | extractor output |
| Speech: expression variable + numbers (0 neutral, 1 Happy, 2 Angry, 3 Sad, 4 Flirty), typewriter voice, skin, name colours | `Shared/VanillaSpeech.cs` | `Tools/regen_vanilla_speech.py` from the plugin's Debug F10/F11 dumps; name colours from `TMPWordColorizer` (sharedassets1, GameObject "Actor", path id 11339): 37 words, default #FFFFFF, player "you" #B0B0B0 |
| Levels: art + hierarchy + transforms | `Resources/VanillaLevelArt/` (`vanilla_levels.json`, 146 levels / 1310 nodes) | `Tools/UnityEditor/SMSModForgeLevelExtractor.cs` in the game's Unity project |
| Places list, world-map districts, room talks | `Model/VanillaPlaces.cs`, `Model/WorldMapDistricts.cs`, `Model/VanillaRoomTalks.cs` | from the level catalog / hierarchy dump |
| The game's conversations (folders, lines, keys) | `Resources/VanillaDialogues/`, `Shared/VanillaDialogueKeys.cs` | `Tools/Dialogue`, `Tools/Convert` (GC2 extract) |
| The game's quests and what references them | `Resources/VanillaQuests/references.json`, `Shared/QuestIds.cs` | `Tools/regen_vanilla_quests.py`. **Quest ids key players' saved progress** — `QuestIdsTests` pins them; a renumbered quest needs a migration, not an edit |
| Global name variables (GNV) catalog | `Model/VanillaGameVariables.cs` | GC2 variable assets |
| Music tracks | `Model/VanillaMusic.cs` | `12_AudioPlayer` children |
| UI screens and their objects | `Resources/VanillaUi/`, `Rendering/VanillaUiLibrary.cs` | `Tools/Ui` |
| UI art, fonts (TMP assets + TTFs) | `Resources/VanillaOverlays/` | AssetRipper export. Dynamic TMP atlases are snapshots: missing glyphs are baked from the TTF by `Rendering/TmpGlyphBaker.cs` — check each dynamic font still has its TTF shipped |
| Photo frames | `Resources/VanillaFrames/` (also in the plugin bundle) | game sprites PhotoFrame, SexyFrame. A NEW scene defaults to SexyFrame + the kiss sound because the game's own scenes under `4_CG_Manager-Sexy` are made that way (author, 1.8.0; `MainViewModel.AddScene`) - recheck that they still are |
| Art thumbnails | `Resources/VanillaArtThumbs/` | from the bust/level art |
| Direct Path examples (objects packs switch most) | `Model/GamePathExamples.cs` | counted from `VanillaDialogues` |
| Signals and what they set off | `Model/VanillaSignals.cs` | read from the game's signal receivers: drink = blink + sound; flash = sound + white canvas; kiss = flash + random SFX; FadeUI / ForceEnableUI = interface out / back |
| Components (game scripts, Unity's own) | `VanillaComponentCatalog` | game assemblies |
| Full scene hierarchy (reference) | `SMSAndroids/ReferenceClasses/CoreGameScene_Hierarchy_1.8E.txt` | the plugin's dump |

## 2. Numbers read from the game's files

- [ ] **Loudness** (`Model/GameLoudness.cs`): one AudioMixer "CoreAudio"
      (sharedassets0), Master snapshot **-10.414 dB**; GC2 `AudioBuffer.Rescale`
      squares the volume; pack music clones `12_AudioPlayer/Beach` at volume
      **0.5**; pack SFX play on GC2's UI channel.
- [ ] **Name colours** — see the table above; re-read the colorizer.
- [ ] **Text in brackets** (`Rendering/DialogueLook.BracketHex`, the rule in
      `DialogueLinePreview.Pieces`): the game's `ParenthesesHighlighter`
      (Assembly-CSharp; decompiled 2026-10-22) wraps an opening bracket to its
      partner - nesting counted, unclosed to the end - in one colour tag.
      Set to **#99D0FF** on `Dialogue_Default_Speech`, **#A2D4FF** on
      `Dialogue_Cyberpunk_Choice/Content/Text` (choices), **#FD8FFF** on
      `Journal_Quest/Title` (sharedassets1) and #FF72D9 on a `Title` in level1.
      Read with UnityPy: each MonoBehaviour's `m_Script` -> `m_ClassName`, the
      colour is the 16 bytes after `m_Name` in its raw data. 1.9's new UI may
      move or recolour them - re-probe and re-decompile.
- [ ] **TMP colour tags** the game accepts (`Rendering/TmpColor.cs`): #RGB,
      #RGBA, #RRGGBB, #RRGGBBAA and 10 names, from `Unity.TextMeshPro.dll`
      (`TMP_Text.ValidateHtmlTag`). Re-read if TMP is upgraded.
- [ ] **Dialogue box and main menu measurements** (1.6.0 `LayoutDump`, Debug):
      canvas 1920x1080 reference, dialogue box TMP rect/margins/auto-size, the
      Settings panel and Exit button positions the language flags sit by. 1.9's
      new UI will move all of it — re-run the dump.
- [ ] **Transition canvases' sort orders**: game transitions 100, Effect_Canvas
      5, Transition_TravelNew -1; the dialogue SpeechUI canvas is built at
      runtime (`TransitionRuntime` raises the chosen canvas above everything).
- [ ] **Typewriter timing** (`TypewriterTiming.cs`, `Shared/VanillaSpeech.cs`).
- [ ] **When a line's sounds start** (1.8.0, `SfxRegistry.PlayLine`/`RunLine`):
      GC2 runs a node as `Story.EventStartNext` -> the node's On Start
      instructions -> `Node.EventStartText`, and only at that last one does
      `SpeechUI.OnStartText` put the line in the box (`m_Text.Text`,
      `CharactersVisible = 0`). The sounds wait for `EventStartText`; a sound
      "when typed" reads `SpeechUI.Current.m_Text.CharactersVisible`, which
      `SpeechUI.Update` sets from `Typewriter.GetCharactersVisible` - linear,
      `floor(elapsed * m_Frequency)`. Checked in game with `cuecheck`
      (`InGameCuePackTests`): a sound 0.5 s after a 0.5 s one at 1.000 s, a
      typed one 40 letters in at 20 a second at 2.10 s.

## 3. Where the plugin hooks into the game

- [ ] **Harmony patches**: `grep -rn "HarmonyPatch\|\.Patch(" SMSModForge.PackPlugin`
      — GC2 `Node.CanRun`, `NodeTypeChoice.GetChoices`, Typewriter
      (`TypewriterTiming.cs`), `PackNodeConditions.cs`, `QuestPlaceRules.cs`.
      A renamed or re-signatured method fails silently (the patch just does not
      apply): check the BepInEx log for Harmony errors on first start.
- [ ] **GC2 private fields read by reflection** (break silently if renamed):
      `Node.m_Text/m_Acting/m_Conditions/m_Duration/m_Jump/m_NodeType/m_Tag/m_Timeout`,
      `Actor.m_Actant/m_Typewriter`, `Actant.m_Name`,
      `Typewriter.m_Frequency/m_Gibberish/m_Pitch/m_UseTypewriter` and
      `GetDuration`, `TextReference.AreAllCharactersVisible`, `SpeechUI.m_Text` (1.8.0),
      `ConditionList/RunConditionsList.m_Conditions`, `Branch.m_InstructionList`,
      `BranchList.m_Branches`, `Journal.m_Tasks`, `QuestsList.m_QuestsLut/m_TasksLut`,
      `TwoCateCode.ConditionChance.m_Threshold` (a game-specific condition).
      Unity: `AudioClip.GetData/SetData` (float[] overloads, by reflection).
- [ ] **GameObject names and paths looked up by name** (110 lookups in 1.8E):
      `grep -rhoE "(Find|FindDeep|FindChild|FindByPath)\(\"[^\"]+\"" SMSModForge.PackPlugin`.
      Most used: `5_Levels`, `9_MainCanvas`, `2_Bust_Manager` (+ `Mouth`,
      `Blink`, `Expressions`, `MBase1`), `12_AudioPlayer`/`Beach`, `6_Effects`,
      `Effect_Canvas/AFewMinutesLater`, `Effect_Canvas/Game_Saved/Saved`,
      `8_Room_Talk`, `Navigator`/`MapButtons`, `10_Gameplay`, `TransferScene`,
      weather `Prefab_Rainy_Day/Rain_Core`, `Prefab_Snowy_Day/Snow_Core`,
      main menu `Canvas_MM`, `MainMenu`, save list `Right/Save 0001`,
      `AfterSleepEvents`, `4_CG_Manager-Sexy`. Each is a place 1.9 may have moved.
- [ ] **NPCs** are recognised by material `NPCCore*` (`VanillaLevelCatalog.IsNpc`).
- [ ] **Bust faces follow a global variable**: each bust resets its face from
      its character's number under `Expressions` whenever any GNV changes. If
      1.9 changes this, `ActionRuntime`'s expression handling must follow.
- [ ] **Speaker labels**: the player's Actor is named the constant "You".
- [ ] **Saves**: GC2 save layout (SAVE.GZ: GNVs, lists, one local set, quest
      journal); the sleep autosave writes slot 1 (+ slot 2 on Mondays);
      `Shared/SaveLoadChecks.cs`, `SaveRecord.cs`, `VanillaSaveSlot.cs`.

## 4. Test tooling (1.8.0, Debug only)

`Tools/Game` (launch / bridge / stop / setup-test-game) and the plugin's
`DebugBridge.cs`. After an update: `setup-test-game.ps1 -Build`, then
`launch.ps1` + `bridge.ps1 -Commands "wait-ingame 120;ping"` must answer
`scene=CoreGameScene loaded=True slot=1` within ~10 s (1.8E: 8.2 s from launch,
6.2 s of it the engine starting).

- [ ] Scene names: the menu is `GameStart`, the game `CoreGameScene`.
- [ ] Loading: GC2 `SaveLoadManager.Instance.Load(slot)` / `HasSaveAt` /
      `IsLoading` - the same call the menu's Load button makes. It changes the
      scene FIRST and restores the save's variables after, so anything set "on
      arrival" must wait for `IsLoading` false (measured: set too early, the save
      overwrote it).
- [ ] Unity keeps window size/mode/monitor in `HKCU\Software\Arvus Games\Starmaker
      Story` (`Screenmanager*`, `UnitySelectMonitor*`) - shared by every copy of
      the game; `launch.ps1` puts them back when the test copy exits.
- [ ] Unity's top-level window class `UnityWndClass` (used by `-Visible`), and
      that Unity activates its window while starting (why the game runs on a
      desktop of its own).
- [ ] Rendering works on that desktop: `shot` gives a real picture once the
      load's fade-in is over (the first second is black).
- [ ] `UnityEngine.ScreenCapture` in `UnityEngine.ScreenCaptureModule`
      (reflection); `screen` uses `CaptureScreenshotAsTexture` at end of frame.
- [ ] The in-game checks of 1.8.0 (`InGameCheckPacksTests` exports `gamecheck`
      and `gameorder` into the test copy's `Mods`; results 2026-10-24):
      - Wait and Transitions on the real clock, to the frame: Wait 1 s = 0.994 s;
        a fade to black covers in 1.006 s and is gone 3.0 s after it began; the
        black screen with words covers in 0.751 s. Raised, the game's
        `Transition Root` sorts 251 and the plugin's own canvas 252, over the
        dialogue box (`Dialogue_Default_Dialogue(Clone)`, order 125); `screen`
        reads the frame fully black over a showing line.
      - A line a pack adds to one of the game's conversations
        (`8_Room_Talk/Kitchen/Annanotthere`) plays its sound, and its speaker
        label is the game's "You" in #B0B0B0.
      - Speaker labels: the TMP text carrying `TMPWordColorizer`; drawn colour
        = its colour. The colorizer holds one entry per word, so the last
        written wins - every paint must be all packs in load order
        (`SpeechColorApplier`). Main menu rows are `SMSModForgeBanner_<n>`.
      - ModForge's own load warning comes up over everything, transitions too;
        `click Continue` answers it.
      - ModForge's in-game look (1.8.0, `ForgeLook`, `ForgeEmblem`): the
        game's sprites `Semi Rounded`, `Semi Rounded - Stroke 2px`, `Rounded`,
        `Circle - 256`, fonts `Barton SDF` (titles) and `Curse Casual SDF`;
        each drawn or stood in for when missing. The blur behind a window is a
        copy of the game's own volume: `blur volume` (under NanoSave, main
        menu) or `NanoSave_Volume` (in the game; the Quest Journal's too),
        Beautify. The game renders in linear light: a black of alpha 0.7 only
        halves brightness (measured 255 -> 112 at 0.84), so dims are set high.
      - The main-menu notice: kept in the plugin's settings, `[Notices] Last
        shown`; `click Got it` closes it (bridge). Rows of the pack list are
        `SMSModForgeBanner_<n>`, its header buttons `..._About` / `..._Folder`
        (never clicked by a test: Explorer opens on the author's desktop).
      - `setup-test-game.ps1` mirrors the author's `BepInEx\config` into the
        test copy each time, so a test's own setting there (language, order,
        Last shown) is reset by the next build.
      - The test save (2026-10-24, IN_GAME_LOOK.md): the product name
        `Starmaker Story` once each in `Starmaker Story_Data\globalgamemanagers`
        (length-prefixed, after `Arvus Games`) and `app.info`, renamed to
        `Starmaker Tests` in the test copy - Unity's persistentDataPath and
        registry key follow it. Game Creator lists the save slots in slot 0
        (`NANOSAVE_0000\SAVE.GZ`, key `data-0000-slots`); a save's title is
        `m_Title` in `game_data.meta` (rich text shown in the Load window).
      - Measuring sound inside the game: a filter on each AudioSource is called
        ~47 times a second, but with `AudioListener.volume` 0 (`-mfMute`) every
        sample it gets is 0, though Unity culls none of the sources
        (`isVirtual` false). So `listen` hears nothing in a game muted that way.
        With `mute off` (the game's own volume up) and Windows' mute of the test
        copy left on (checked before and after; a process with no sound reads
        empty, the control) it measures, after the source's volume
        (2026-10-24): line sounds go by the SQUARE of their volume - 150 % =
        2.28x, 50 % = 0.25x, 200 % = 4.04x the 100 % level (GC2's channel
        squares; `GameAudio.Louder` scales the clip past 1); a line whose
        pattern has no sound plays nothing. Pack music is plain gain - 50 % =
        0.50x, 200 % = 2.00x (`SoundGain`). Pitch read back 440/880/330 Hz.
        Line sounds play on the pooled `User Interface/Audio Source`, music on
        `12_AudioPlayer/<key>`.
- [ ] The in-game suite (12.13, `Tools/Game/ingame-suite.ps1`,
      `InGameSuiteTests`) leans on these of the game's, 1.8E (2026-10-24):
      - Places: `5_Levels` children; a level drifts with the mouse
        (`ParallaxMouseEffect.startPosition`, private); journeys through
        `Upcoming-Level` / `Start-Transfer` and `10_Gameplay/TransferScene`'s
        Trigger; the Storage Room (`32_StorageRoom`) starts no conversation on
        arrival, the Beach (a date) and the hallway (small talk) can;
        Downtown's `NPCs` switches its crowd off on arrival (DisableChildren).
      - The World Map: `World_Map/Canvas/Core/Radial_Buttons/<district>`; a
        click during its opening fade goes nowhere (its CanvasGroup).
      - The night: `5_Levels/5_MyRoom/PlayerRoom_ButtonCanvas/Player_Room_Buttons/
        SleepButton`, then the payout's `Confirm`, then the morning's choice
        (`Relax`), then `6_Effects/Effect_Canvas/Game_Saved/Saved` - ModForge
        commits there. Pack files: `Saves/NANOSAVE_0001/SMSModForge_<pack>.json`.
      - Scenes under `4_CG_Manager-Sexy` (`pack:<id>.<key>/Core/Art`); the
        Starmaker gallery row `9_MainCanvas/Starmaker/Starmaker_Core/Gallery/
        Scrollarea/CoreGallery`; wallpapers `9_MainCanvas/Desktop/Wallpaper`
        and `9_MainCanvas/Wallpaperselection/UI_Core/List`; busts under
        `2_Bust_Manager` (body on `MBase1`); a game screen patched:
        `vanillaui:9_MainCanvas/Quitagme`.
      - Conversations: the skin's box is a root `Dialogue_*` (speech under
        `/Speech/`, choices under `/Choices/`, speaker `Actor`); Game Creator's
        `Story.Continue()` is a player's click; `Story.m_CurrentId` the line.
        The skin SKIPS a chosen option (NodeTypeChoice.GetNext) - ModForge runs
        its actions itself; every root plays in turn; a jump replays the
        target's plain-line ancestors (Story.StackFromJump).
      - A pack meeting a save for the first time catches the day up: its
        Daily refresh and on-day-change rules run once at load.
