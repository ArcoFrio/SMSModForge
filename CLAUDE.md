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
dotnet test --filter ReleaseReadinessTests
```

That reads the bytes of the packaged DLL, because nothing in the ordinary suite
can see this: the tests compile in Debug, and the plugin is a separate assembly
for a different framework.

## Tests

Committed tests must not hard-code a path to anyone's pack. Where a real pack is
genuinely the only meaningful subject — backward compatibility, for one — gate it
behind an environment variable and ship the harness with no subject.
