# Starts the TEST COPY of the game for an in-game check, by the author's rules (2026-10-21): never on their
# screen, never taking their focus, and muted - and it refuses while a game is what the author is in. Make the
# copy first: setup-test-game.ps1 -Build.
#
# It runs on a desktop of its own, which nobody switches to: Unity activates its window while it starts, and
# on the author's machine any activation takes the foreground (measured: started minimized, it took their
# focus 5 s in). -Visible puts it on their leftmost monitor, minimized, instead - for when they want to watch,
# at the price of that one activation.
#
#   powershell -File Tools\Game\launch.ps1                 # the test save, straight into the game, muted
#   powershell -File Tools\Game\launch.ps1 -Slot 0         # stay on the main menu
#
#   powershell -File Tools\Game\launch.ps1 -Vars my.vars   # then set these game variables (name = value)
#   powershell -File Tools\Game\launch.ps1 -Sound          # a check that listens: sound on, Windows' state shown
#
# It plays only the test save - in the test copy's own saves folder (it is renamed "Starmaker Tests", see
# test-save.ps1), put back clean before every start - so no check ever loads or writes one of the author's
# saves and none is changed by the check before it. Its settings and window state are its own too: nothing of
# the author's game is touched. It does not start while the author's own game is running.
#
# Then drive it with bridge.ps1, and close it with stop.ps1.
param(
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [string]$Bridge = 'b0',
    [int]$Slot = 1,
    [string]$Vars = '',
    [switch]$Sound,
    [int]$Width = 960,
    [int]$Height = 540,
    [switch]$Visible,
    [switch]$NotTheAuthors
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'GameNative.cs')
Add-Type -AssemblyName System.Windows.Forms

$exe = Join-Path $Test 'Starmaker Story.exe'
if (-not (Test-Path $exe)) { throw "No test copy at $Test - run setup-test-game.ps1 -Build" }

# Never while the author's game is running - not only while they are in it: a second game beside theirs is in
# the way of their play - and never two test copies at once. -NotTheAuthors: the author has said the game
# running is not theirs (another agent's, 2026-10-24); even then, never one in front, where somebody may be in it.
# Another check's test copy - a folder ending " test", made for checks the way this one was (the Gallery's
# is "Starmaker Story 1.8E - Gallery test") - is not the author's game either.
foreach ($p in Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue) {
    if ($p.Path -like "$Test*") { throw "The test copy is already running (pid $($p.Id)); stop.ps1 first." }
    if ($p.Id -eq [GameNative]::ForegroundProcess()) { throw "Somebody is in the game (pid $($p.Id)); not starting." }
    $anotherTestCopy = $p.Path -and ((Split-Path (Split-Path $p.Path) -Leaf) -match ' test$')
    if (-not $NotTheAuthors -and -not $anotherTestCopy) { throw "Another copy of the game is running (pid $($p.Id)) - the author's? Not starting (-NotTheAuthors only if they said it is not)." }
}

# Only the test save is ever played, in the test copy's own folder; it goes back to clean first. A test copy
# not renamed would save among the author's: refused.
. (Join-Path $PSScriptRoot 'test-save.ps1')
$info = Get-Content (Join-Path $Test 'Starmaker Story_Data\app.info') -Raw
if ($info -notmatch [Regex]::Escape($TestProduct)) { throw "The test copy is not renamed '$TestProduct' - it would save among the author's saves. Run setup-test-game.ps1." }
if ($Slot -ne 0 -and $Slot -ne $TestSlot) { throw "Checks play only the test save (slot $TestSlot of the test copy's own saves)." }
RestoreTestSave $Test

if ($Sound) { "Windows' sound: " + [GameNative]::SpeakersState() }

# A clean bridge folder.
$dir = Join-Path $Test "BepInEx\plugins\SMSModForge\DebugBridge\$Bridge"
New-Item -ItemType Directory -Force $dir | Out-Null
Remove-Item "$dir\commands.txt", "$dir\results.txt" -ErrorAction SilentlyContinue

$gameArgs = @('-screen-fullscreen', '0', '-screen-width', $Width, '-screen-height', $Height,
          '-mfBridge', $Bridge, '-logFile', "`"$Test\Player-$Bridge.log`"")
if ($Slot -gt 0) { $gameArgs += @('-mfLoadSlot', $Slot) } else { $gameArgs += @('-mfTestSlot', $TestSlot) }
if ($Vars) { $gameArgs += @('-mfVars', "`"$((Resolve-Path $Vars).Path)`"") }
if (-not $Sound) { $gameArgs += '-mfMute' }

if ($Visible) { $gamePid = [GameNative]::StartMinimized($exe, ($gameArgs -join ' '), $Test) }
else { $gamePid = [GameNative]::StartOnOwnDesktop($exe, ($gameArgs -join ' '), $Test, "SMSModForgeGame-$Bridge") }
"started pid $gamePid" + $(if ($Visible) { ' (leftmost monitor, minimized)' } else { ' (on a desktop of its own)' })

# Its window: parked on the leftmost monitor, minimized, until it settles. Its sound: muted as soon as it
# has any. Unity may show and size its window more than once while it starts, so this keeps at it.
$left = [System.Windows.Forms.Screen]::AllScreens | Sort-Object { $_.Bounds.Left } | Select-Object -First 1
$x = $left.WorkingArea.Left + 20; $y = $left.WorkingArea.Top + 20
$muted = $Sound
$deadline = (Get-Date).AddSeconds(45)
$settled = 0
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $gamePid -ErrorAction SilentlyContinue)) { throw "The game exited while starting - see $Test\Player-$Bridge.log" }
    $w = if ($Visible) { [GameNative]::MainWindowOf($gamePid) } else { [IntPtr]::Zero }
    if (-not $Visible) { $settled = 20 }
    if ($w -ne [IntPtr]::Zero) {
        [GameNative]::ParkMinimized($w, $x, $y)
        if ([GameNative]::IsMinimized($w)) { $settled++ } else { $settled = 0 }
    }
    if (-not $muted -and [GameNative]::SetProcessMute($gamePid, $true) -gt 0) { $muted = $true; "muted (its own sound only)" }
    if ($settled -ge 20 -and $muted) { break }
    Start-Sleep -Milliseconds 150
}
"sound " + $(if ($Sound) { 'on' } else { 'muted' })
$gamePid
