# Makes the save the in-game checks play in. The author's saves are only read - never loaded by a check, never
# written (the author, 2026-10-24: "I need the save file (both vanilla and any modforge mods) to not change").
#
# The test copy of the game saves into a folder of its own (see test-save.ps1): this fills it with a copy of
# the author's slot 0 (the game's list of saves, shared by every slot) and of one slot of theirs as the test
# copy's slot 1, named "ModForge test save" in ModForge's amber. A check may do anything to it ("whatever you
# want to test with it, just make sure previous testing doesn't break future testing"): the clean copy is kept
# beside the test copy of the game (TestSave\), and launch.ps1 puts it back before every start.
#
#   powershell -File Tools\Game\make-test-save.ps1              # from the author's slot 1, once
#   powershell -File Tools\Game\make-test-save.ps1 -Refresh     # made again
#   powershell -File Tools\Game\make-test-save.ps1 -From 2
#
# Each slot carries everything in its folder: the game's save, ModForge's per-pack files, any other mod's.
# The author's files are hashed before and after, and the script fails if one changed. It refuses while a copy
# of the game runs (one may be writing the very save being copied) unless the author has said it is not theirs.
param(
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [int]$From = 1,
    [switch]$Refresh,
    [switch]$NotTheAuthors
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'test-save.ps1')
$authors = AuthorsSaves
$global = Join-Path $authors 'NANOSAVE_0000'
$source = Join-Path $authors ('NANOSAVE_{0:D4}' -f $From)
if ($From -lt 1) { throw "Slot 0 is the game's list of saves, not a save." }
if (-not (Test-Path (Join-Path $source 'SAVE.GZ'))) { throw "No save in slot $From ($source)." }
foreach ($p in Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue) {
    if ($p.Path -like "$Test*") { throw "The test copy is running; stop.ps1 first." }
    if (-not $NotTheAuthors) { throw "A copy of the game is running (pid $($p.Id)); not copying (-NotTheAuthors only if the author said it is not theirs)." }
}

$clean = CleanTestSaves $Test
if ((Test-Path $clean) -and -not $Refresh) { "clean test saves already kept ($clean)"; RestoreTestSave $Test; return }

function Hashes($dir) { Get-ChildItem $dir -File | ForEach-Object { $_.FullName + ' ' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash } }
$before = @(Hashes $global) + @(Hashes $source)

if (Test-Path $clean) { Remove-Item $clean -Recurse -Force }
New-Item -ItemType Directory -Force $clean | Out-Null
Copy-Item $global (Join-Path $clean 'NANOSAVE_0000') -Recurse
$slot = Join-Path $clean ('NANOSAVE_{0:D4}' -f $TestSlot)
Copy-Item $source $slot -Recurse

# Its name in the Load window: ModForge's amber. Written in place, not through ConvertTo-Json, which would write
# the tag's brackets as \u003c escapes.
$meta = Join-Path $slot 'game_data.meta'
$raw = [IO.File]::ReadAllText($meta)
$titled = [Text.RegularExpressions.Regex]::Replace($raw, '"m_Title":"[^"]*"', '"m_Title":"' + $TestSaveTitle + '"')
if ($titled -eq $raw) { throw "No m_Title in $meta - the game's save format has changed (GAME_UPDATE_CHECKS.md)." }
[IO.File]::WriteAllText($meta, $titled, (New-Object Text.UTF8Encoding($false)))

$after = @(Hashes $global) + @(Hashes $source)
if (Compare-Object $before $after) { throw "The author's save changed while it was copied - stop and tell the author." }
"clean test saves kept from the author's slots 0 and $From ($($before.Count) files, unchanged): $clean"
RestoreTestSave $Test
