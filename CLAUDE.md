# Working rules for this repository

## Changing anything a saved pack already contains

Packs are in the hands of authors who have spent months in them. Any change to
the shape of the manifest — a renamed field, a merged action, a setting that
becomes two settings — **must** carry every existing pack across without the
author doing anything.

That means all five of these, every time:

1. **Read the old form.** A pack written by any previous build still loads.
2. **Convert on load, in memory.** The author sees the new shape immediately.
3. **Do not write until they save.** Opening a pack must never touch the file
   on disk. An author who opens a pack to look at it and closes it has changed
   nothing.
4. **Tell them it happened.** A migration the author was not told about is a
   diff they will find later and not understand. Report it when the pack loads,
   naming what changed and how many of them there were.
5. **Back up on the first save after a migration.** Write the untouched original
   beside the manifest before the new one replaces it, once — not on every save,
   and not when nothing was migrated. The backup must be excluded from export,
   or it ships to players.

`PackMigration` is where this lives; add to it rather than writing a new pass
somewhere else, so a pack picks up every migration in one place and the report
is complete.

The rule applies to the vanilla-character adoption and to anything like it: if
a pack that worked yesterday would look different today, it is a migration.

## Verifying things you cannot run

The runtime plugin loads into a game this repository cannot start, so a claim
about the engine is a guess unless something checks it.

- **Compile-probe the API.** A temporary file referencing the real assemblies
  proves a member exists. `SMSModForge.PackPlugin.csproj` lists its sources
  **explicitly**, so a probe file that is not in the csproj is not compiled and
  will "succeed" while proving nothing.
- **Always include a case that must fail.** A silent no-op reads exactly like
  success. Reference a member that does not exist alongside the ones that do; if
  the build passes anyway, the probe is not running.

## Anything that puts a window on screen

`MessageBox.Show`, `ShowDialog`, **file pickers**, and **custom windows** must
all be guarded with `if (!Services.TestMode.Active)`.

All four, not just the first. Guarding only `MessageBox` leaves the save
confirmation window and the folder pickers able to stop a run, which is exactly
what happened: two separate hangs, each found by a person watching the suite sit
still rather than by the suite failing.

`MainViewModel` routes its notices through `Tell(...)` and its questions through
`Ask(..., whenNobodyIsThere)`, which handle this. Use them rather than calling
`MessageBox` directly; pick the unattended answer per question, since some should
carry on (a save proceeds) and some should not (a discard is refused).

The harness builds real windows and opens real packs on the machine of whoever
is running the tests. A modal raised from that code does not fail the suite — it
**stops it**, waiting for a click nobody is there to give, on a dialog that
appears over that person's work. It looks exactly like a hang.

The same applies to anything else the editor does for a person's benefit rather
than the pack's: writing window layout, prompting about unsaved changes, opening
a browser. `Services.TestMode` is one switch for all of it.

## Write the changelog as you go

Keep an `## Unreleased` section at the top of `CHANGELOG.md` and add to it in
the same change that does the work. Not at release time.

A changelog assembled at the end is assembled from memory, and memory keeps the
wrong half: it keeps the big feature and loses the fix somebody will actually
notice, and it loses the REASON, which is the part worth writing down. By the
time a release is being cut, "why was this done this way" has already gone.

An entry is worth adding when a person using the editor or playing a pack would
notice the difference. Refactors, test-only changes and internal renames do not
need one.

At release, that section becomes the version heading, and the GitHub release
body is the same text. So write it for an author reading it in the update
prompt, not for whoever wrote the code:

- What changed, and what it means for them.
- Where the old behaviour was wrong, said plainly. "It looked like it was
  working" is more use than "fixed a null reference".
- Anything they must know BEFORE updating, under its own heading — a migration,
  a deliberate divergence, a setting that now means something else.

## Before publishing a release

**Build the plugin with `-c Release`.** Its diagnostics — the F10/F11/F12 scene
dumps, and the 745 lines of reflection behind them — are compiled only under
`#if DEBUG`. A Debug build ships three function keys a player can press by
accident that write megabytes into their game folder, and every release up to
and including 1.2.0 did exactly that.

The editor's own debug features are not conditional and are meant to ship.

`#if DEBUG` makes a Release build clean by construction; it does not stop
somebody packaging a Debug one. Check the artefact itself:

```
set SMSMODFORGE_RELEASE_PLUGIN=...\Starmaker - ModForge Plugin 1.3.0.zip
git show v<last>:SMSModForge/Languages/en.txt > last-en.txt
set SMSMODFORGE_LAST_RELEASE_EN=last-en.txt
dotnet test --filter ReleaseReadinessTests
```

That reads the bytes of the packaged DLL, because nothing in the ordinary suite
can see this: the tests compile in Debug, and the plugin is a separate assembly
for a different framework.

**Play it in the game.** `powershell -File Tools/Game/ingame-suite.ps1` runs
ModForge's in-game suite (`InGameSuiteTests`, 1.8.0): every feature checked in
the test copy of the game itself - built by the editor's own code, read back
from the game, each check with a control that must come out the other way.
About ten minutes, one game on a desktop of its own, the test save only
(`Tools/Game`'s rules). Its packs are in `InGameSuitePacks`; the game names it
relies on are in GAME_UPDATE_CHECKS.md. It found two runtime bugs the ordinary
suite could not see (a choice's actions never ran; `[PV:]` read at load).

**Every language must have this version's texts.** A text added or reworded
during a version shows in English in every other language until somebody
translates it, and the ordinary suite lets that through: it fails only on a
damaged line. The same command checks it against the last release's English
(`SMSMODFORGE_LAST_RELEASE_EN=none` when the last release had no language
files). It fails on any text a language is missing, has empty, or translated
from English that has since changed, and lists what this version added that is
still word for word the English - a name can be right that way, so that part
is for a person to read. Translate what it names, bring the files in with
`SMSMODFORGE_REFRESH_TRANSLATIONS` (see `ShippedTranslationsTests`), and run it
again.

## Tests

### Which tests to run, and when

The suite is in two tiers, because one of them is a hundred times slower than
the other. Anything that builds a real `MainWindow` through `WindowHarness`
costs seconds per test and cannot be parallelised inside one process — the
harness serialises everything onto one STA thread — while the pure-logic tests
run in microseconds. Across processes it can, which is what the runner below
does.

```
dotnet test --filter "Speed!=Slow"                       # the edit loop: seconds
powershell -File Tools/Testing/run-tests.ps1             # the full suite: about a minute and a half
powershell -File Tools/Testing/run-tests.ps1 -WithEx     # and the window tests again in EX: before anything ships
```

**EX is a second set of templates over the same window** (1.8.0,
`View/Themes/Ex.xaml`). Every control style in App.xaml reaches its template
through a `Theme.*Template` DynamicResource, and an EX theme swaps in the
template of the same key. So: a new App.xaml template goes in as a keyed
resource with an EX counterpart, and an EX template keeps every named part,
binding and trigger of its Normal one - EX may draw differently, never behave
differently. `-WithEx` is what proves that: the first run of the window tests
under EX found a real bug in Normal's own dropdowns (an action's Level wiped
while its row was built) that only EX's timing showed.

The harness puts the run's theme on (`SMSMODFORGE_TEST_THEME`, else Light),
and the app's own start-up - which the harness runs too - must never put a
saved theme on over it. It did until 2026-10-24: the EX pass ran in Light and
said nothing, and the first run truly in EX found the suggestions' line hidden
under EX's rows. `HarnessThemeTests` holds every run to the theme it asked for.

`run-tests.ps1` splits the suite across several test processes at once - 12
testers by default, `-Testers N` for more or fewer, `-Filter` as `dotnet test`
takes it - and prints one result. Each tester keeps its share of the classes
(dealt out by what they took last time, `Shards.cs`), works on a desktop of its
own, and shows nothing; the first tester's progress window covers the whole run
and stays until the last tester is done. Measured on the author's machine
(2026-10-22): one process 40:32, 12 testers 1:29, 16 testers 1:17. A plain
`dotnet test` still runs everything in one process, slowly - and slower than
the sum of its parts: a long-lived test process slows itself down, and the same
tests take 17 minutes added up when spread over short ones.

What made running several at once safe, and must stay true:

- **A test run keeps its own settings folder**, one per process
  (`Services.AppDataFolder`): before, the suite read and wrote the author's
  real `%LocalAppData%\SMSModForge` - their export and publish history, their
  own translations. Anything new the editor remembers goes through
  `AppDataFolder.PathOf`; `AuthorsSettingsUntouchedTests` fails otherwise.
- **COM's main apartment is taken first** (`TestRun.TakeComsMainApartment`).
  The first thread in a process to join a COM apartment gets a hidden window
  from COM, and a thread with a window cannot move to the run's desktop. The
  progress window's thread used to take it by luck; testers without one could
  not put any window test on their desktop.
- **A program a test starts goes on the run's desktop too**
  (`TestDesktop.Launch`): the editor started as a process of its own opened on
  the author's screen for every run until a foreground sampler caught it.
- **Failing to make the run's desktop stops the tester** rather than letting
  it carry on on the author's.

Run the fast tier constantly and the full suite before anything ships. Do not
settle into running only what you think you touched: the failures that justify
having a suite at all are the ones nobody would have thought to target. Two
real examples, both caught by a full run and by nothing else — a scan that
found eleven hard-coded English sentences in a file that had just been written,
and a check that the shipped translations were complete after a key was added
somewhere unrelated.

Measured, not guessed (before the parallel runner): **672 tests take 12.9
minutes and the other 981 take 17 seconds.** Nearly all of the cost is
building a real `MainWindow`, but not all of it — a few classes are slow for
their own reasons, so the tag goes on a class that either uses `WindowHarness`
or measures over about five seconds.

The threshold is per CLASS rather than per test, deliberately. The
cross-cutting checks — the code scan for untranslated sentences, the shipped
translation checks — cost around a second each, and a per-test threshold would
have exiled exactly the tests a fast tier most needs to keep.

Re-measure with `dotnet test --logger "trx;LogFileName=timings.trx"` and read
the durations out of the TRX rather than re-deriving this by eye.

### How far along a run is

Every run opens a small window saying so: tests done, time left, the test
running now and for how long (orange past a minute - the first sign of one
waiting on a click), and what has failed. The taskbar button carries the same
bar. Time left comes from what each test took last time, kept in
`bin/.../test-timings.tsv`; the first run on a machine counts tests instead.
See `RunProgress`.

It is the author's standard (2026-10-21) and there is no switch to turn it
off: it opens on their **leftmost monitor**, on top, and never takes focus.

### Nothing a test does may reach the author's screen

The author works on the other monitors while tests run. So:

- **Every window a test builds lives on a desktop of the run's own**
  (`TestDesktop`), never on theirs. `WindowHarness` does this; any other
  thread that makes a window must call `TestDesktop.Enter()` first, which
  `TestWindowRulesTests` enforces. Off-screen was not enough - on their
  machine any activation takes the foreground (their foreground lock timeout
  is 0), and a sampler caught the harness doing it for seconds at a time.
  Minimized is not an option either: WPF lays a minimized window out at
  160x39 with no keyboard focus. `Tools/Testing/foreground-sampler.ps1`
  proves a run took nobody's focus.
- That desktop has no mouse, so a top-level menu cannot open (menu mode
  takes the mouse). Lay out its popup's content instead - see
  `UiBindingTests.OpenSubmenu`.
- **Nothing makes a sound.** `TestMode` keeps the editor's previews silent.
- **The game**, when a check needs it, is started only through the launcher
  in `Tools/Game` (its own desktop, never focused, muted), never while the
  author's own game is running, and plays only **the test save**: the test
  copy is renamed "Starmaker Tests" so it saves in a folder of its own (the
  game keeps its slot list in slot 0, shared by every slot - a shared folder
  cannot be made safe), its saves a copy of the author's slots 0 and 1
  ("ModForge test save" in amber), made by `make-test-save.ps1` and put back
  clean before every start. The author's saves - the game's files and every
  mod's - are never loaded or written by a check (the author, 2026-10-24).
  See `IN_GAME_LOOK.md`.

### Never run a suite while editing the files it reads

A background run reading `en.txt` while it is being rewritten fails in a way
that looks exactly like a real defect and is not. Finish the edits, then run.

Committed tests must not hard-code a path to anyone's pack. Where a real pack is
genuinely the only meaningful subject — backward compatibility, for one — gate it
behind an environment variable and ship the harness with no subject.
