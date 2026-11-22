# Runs the test suite split across several test processes at once - the
# testers - and prints one result for all of them.
#
#   powershell -File Tools/Testing/run-tests.ps1                  # everything, default testers
#   powershell -File Tools/Testing/run-tests.ps1 -Testers 8
#   powershell -File Tools/Testing/run-tests.ps1 -Filter "FullyQualifiedName~Navigator"
#   powershell -File Tools/Testing/run-tests.ps1 -WithEx          # then the window tests again in EX
#   powershell -File Tools/Testing/run-tests.ps1 -WithEx -InGame  # and the in-game suite: the pre-release run
#   powershell -File Tools/Testing/run-tests.ps1 -Theme Neon      # every test with that theme on
#
# A run is made of parts, run one after another: every test, the timing tests
# alone, with -WithEx both again in EX, with -InGame the in-game suite last.
# ONE progress window covers all of them, with one estimate for the whole run
# (the author, 2026-11-22: each part used to open a window of its own, each
# saying only how long it had left). The window is this test assembly started
# as a program (SMSModForge.Tests/WholeRun.cs); before anything runs, each part
# is planned by a test process that works out its tests and their last times
# and runs none, so the estimate covers the parts still to come from the start.
#
# Each tester runs its share of the classes (split by what they took last time,
# see SMSModForge.Tests/Shards.cs), on a desktop of its own, with no console
# window. The testers' output is kept in the run's folder, one folder a part,
# printed at the end on a failure.
#
# Tests that time themselves (Trait Timing=Alone: frames a second, cycles a frame)
# run after the others, by one tester alone, so they measure the editor and not
# eleven other testers' load.
#
# EX (1.8.0): the EX themes draw every control with templates of their own, so
# the tests that build a window run a second time with an EX theme on
# (SMSMODFORGE_TEST_THEME, read by WindowHarness). The same tests - the theme
# is a parameter of the run, not a copy of the suite - and only the window
# ones: a test that builds no window cannot tell the themes apart.
#
# The in-game suite (-InGame, 1.10.0) is what Tools/Game/ingame-suite.ps1 runs:
# the plugin's Debug build into the TEST COPY (Tools/Game/ingame-prepare.ps1),
# then InGameSuiteTests with SMSMODFORGE_INGAME naming the copy - its own
# desktop, muted, only the test save, never while the author's game runs.
#
# Exit code: 0 when every tester reported, none crashed, and nothing failed.
param(
    [int]$Testers = 0,
    [string]$Filter = "",
    [string]$Theme = "",
    [switch]$WithEx,
    [switch]$InGame,
    [string]$Test = "",
    [switch]$NoBuild
)
$ErrorActionPreference = "Stop"

# Measured on the author's i9-14900K (24 cores, 32 threads, 64 GB), 2026-10-22,
# whole suite: 1 tester 40:32 - 8 testers 2:02 - 12 testers 1:29 (CPU 57% average,
# 85% peak) - 16 testers 1:17 (71%, 94% peak). 12 leaves the machine room for what
# else it is doing (the author trains and renders on it); -Testers 16 when it is idle.
$DefaultTesters = 12

# The EX theme the second pass runs in: a dark one, the look most unlike Normal's.
$ExTheme = "Neon"

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repo "SMSModForge.Tests\SMSModForge.Tests.csproj"
$dll = Join-Path $repo "SMSModForge.Tests\bin\Debug\net8.0-windows\SMSModForge.Tests.dll"
if ($Testers -le 0) { $Testers = $DefaultTesters }
$utf8 = New-Object System.Text.UTF8Encoding $false

if (-not $NoBuild) {
    & dotnet build $project --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed."; exit $LASTEXITCODE }
}
if (-not (Test-Path $dll)) { Write-Host "No test build at $dll"; exit 1 }
if ($InGame) {
    if (-not $Test) { $Test = ([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest }
    & (Join-Path $repo "Tools\Game\ingame-prepare.ps1") -Test $Test | Out-Host
}

# ── The parts ──
# What a test times (frames a second, cycles a frame) runs after the rest, by a
# tester alone (Trait Timing=Alone, 1.9.1): beside eleven others it measured the
# others' load - the bust preview drew 30 a second at Low there, 70 alone.
function Both([string]$f) {
    $parallel = if ($f) { "($f)&Timing!=Alone" } else { "Timing!=Alone" }
    $alone = if ($f) { "($f)&Timing=Alone" } else { "Timing=Alone" }
    return @($parallel, $alone)
}
function New-Part([string]$label, [string]$filter, [string]$theme, [int]$count, [string]$title, [string]$inGameCopy = "") {
    return [pscustomobject]@{ Label = $label; Filter = $filter; Theme = $theme; Count = $count; Title = $title; InGame = $inGameCopy }
}
$in = if ($Theme) { " in $Theme" } else { "" }
$f = Both $Filter
$parts = @(
    (New-Part "main" $f[0] $Theme $Testers "every test$in, $Testers testers"),
    (New-Part "alone" $f[1] $Theme 1 "the timing tests$in, alone")
)
if ($WithEx) {
    $exFilter = if ($Filter) { "($Filter)&Speed=Slow" } else { "Speed=Slow" }
    $f = Both $exFilter
    $parts += New-Part "ex" $f[0] $ExTheme $Testers "the window tests in $ExTheme, $Testers testers"
    $parts += New-Part "ex-alone" $f[1] $ExTheme 1 "the timing tests in $ExTheme, alone"
}
if ($InGame) {
    $gameFilter = if ($Filter) { "($Filter)&FullyQualifiedName~InGameSuiteTests" } else { "FullyQualifiedName~InGameSuiteTests" }
    $parts += New-Part "ingame" $gameFilter "" 1 "the in-game suite, in the test copy" $Test
}

$root = Join-Path ([IO.Path]::GetTempPath()) ("SMSModForge-tests\run-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $root | Out-Null
foreach ($p in $parts) { New-Item -ItemType Directory -Force -Path (Join-Path $root $p.Label) | Out-Null }
$lines = $parts | ForEach-Object { "{0}`t{1}`t{2}`t{3}" -f $_.Label, $_.Count, $_.Theme, $_.Title }
[IO.File]::WriteAllText((Join-Path $root "parts.txt"), (($lines -join "`n") + "`n"), $utf8)

# A test process of this run: the testers, and the planners before them.
function New-TestProcess([object]$part, [string]$log, [hashtable]$extra) {
    $arguments = "test `"$dll`" --nologo"
    if ($part.Filter) { $arguments += " --filter `"$($part.Filter)`"" }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "cmd.exe"
    $psi.Arguments = "/c dotnet $arguments > `"$log`" 2>&1"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true            # nothing of a tester reaches the screen
    $psi.WorkingDirectory = $repo
    $psi.EnvironmentVariables["SMSMODFORGE_SHARD_DIR"] = (Join-Path $root $part.Label)
    $psi.EnvironmentVariables["SMSMODFORGE_PROGRESS_HOST"] = "1"   # the run's one window is up
    if ($part.Theme) { $psi.EnvironmentVariables["SMSMODFORGE_TEST_THEME"] = $part.Theme }
    # Only the in-game part plays the game; everywhere else its tests return at once.
    if ($part.InGame) { $psi.EnvironmentVariables["SMSMODFORGE_INGAME"] = $part.InGame }
    elseif ($psi.EnvironmentVariables.ContainsKey("SMSMODFORGE_INGAME")) { $psi.EnvironmentVariables.Remove("SMSMODFORGE_INGAME") }
    foreach ($k in $extra.Keys) { $psi.EnvironmentVariables[$k] = [string]$extra[$k] }
    return [System.Diagnostics.Process]::Start($psi)
}

# ── The one window, then every part planned before any runs ──
$wpsi = New-Object System.Diagnostics.ProcessStartInfo
$wpsi.FileName = "dotnet"
$wpsi.Arguments = "`"$dll`" --whole-run `"$root`" --script $PID"
$wpsi.UseShellExecute = $false
$wpsi.CreateNoWindow = $true
$wpsi.WorkingDirectory = $repo
$window = [System.Diagnostics.Process]::Start($wpsi)

$planners = @()
foreach ($p in $parts) {
    $planners += New-TestProcess $p (Join-Path (Join-Path $root $p.Label) "plan.log") @{ SMSMODFORGE_PLAN_ONLY = $p.Count }
}
foreach ($pl in $planners) { if (-not $pl.WaitForExit(180000)) { try { $pl.Kill() } catch { } } }
# A part no test matches is never handed to the planner: it plans as nothing.
foreach ($p in $parts) {
    $dir = Join-Path $root $p.Label
    $planLog = Join-Path $dir "plan.log"
    if (-not (Test-Path (Join-Path $dir "plan.txt")) -and (Test-Path $planLog) -and
        (Select-String -Path $planLog -Pattern 'No test matches the given testcase filter' -Quiet)) {
        $none = @("total=0", "weighted=1") + (1..$p.Count | ForEach-Object { "shard=0|0" })
        [IO.File]::WriteAllText((Join-Path $dir "plan.txt"), (($none -join "`n") + "`n"), $utf8)
    }
}
Write-Host ("{0} parts, one window; notes and output in {1}" -f $parts.Count, $root)

# One part: every tester started, waited for, and their results added up.
function Invoke-Part([object]$part) {
    $run = Join-Path $root $part.Label
    $count = $part.Count
    $what = if ($part.Theme) { " in $($part.Theme)" } else { "" }
    $r = [ordered]@{ Failed = 0; Passed = 0; Skipped = 0; Total = 0; Missing = @(); Crashed = @(); Failures = @(); Wall = [TimeSpan]::Zero; Theme = $part.Theme; Count = $count; Title = $part.Title }

    $planText = if (Test-Path (Join-Path $run "plan.txt")) { Get-Content (Join-Path $run "plan.txt") -Raw } else { "" }
    if ($planText -match '(?m)^total=0\s*$') {
        [IO.File]::WriteAllText((Join-Path $run "started"), "", $utf8)
        [IO.File]::WriteAllText((Join-Path $run "done"), "", $utf8)
        Write-Host "  $($part.Title): no tests"
        return $r
    }

    [IO.File]::WriteAllText((Join-Path $run "started"), "", $utf8)
    $started = Get-Date
    $procs = @()
    for ($k = 1; $k -le $count; $k++) {
        $procs += New-TestProcess $part (Join-Path $run "tester-$k.log") @{ SMSMODFORGE_SHARD = "$k/$count" }
    }
    Write-Host "$($part.Title): started"
    foreach ($p in $procs) { $p.WaitForExit() }
    $r.Wall = (Get-Date) - $started
    [IO.File]::WriteAllText((Join-Path $run "done"), "", $utf8)

    $summary = '^\s*(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+),\s+Duration:\s+(.+?)\s+-'
    for ($k = 1; $k -le $count; $k++) {
        $log = Join-Path $run "tester-$k.log"
        $lines = if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { @() }
        $line = $lines | Where-Object { $_ -match $summary } | Select-Object -Last 1
        if ($line -and $line -match $summary) {
            $r.Failed += [int]$Matches[2]; $r.Passed += [int]$Matches[3]; $r.Skipped += [int]$Matches[4]; $r.Total += [int]$Matches[5]
            $ran = $Matches[5]
            Write-Host ("  tester {0,2}: {1,5} tests in {2}" -f $k, $Matches[5], $Matches[6])
            # A tester whose test host died still prints that line, counting only
            # what ran before it did: the rest of its share never ran, and the
            # totals alone read as a failure or two (1.8.0, a crash in the EX pass
            # that left 62 tests unrun behind "2 failed").
            if ($lines | Where-Object { $_ -match 'Test Run Aborted|Test host process crashed' }) {
                $status = Join-Path $run "tester-$k.status"
                $share = if (Test-Path $status) { ((Get-Content $status | Where-Object { $_ -match '^total=' }) -replace '^total=', '') } else { "?" }
                $r.Crashed += "tester $k (ran $ran of $share)"
                Write-Host ("  tester {0,2}: CRASHED - ran {1} of the {2} in its share; see {3}" -f $k, $ran, $share, $log)
            }
        }
        elseif ($lines | Where-Object { $_ -match 'No test matches the given testcase filter' }) {
            Write-Host ("  tester {0,2}: no tests in its share" -f $k)
        }
        else {
            $r.Missing += $k
            Write-Host ("  tester {0,2}: gave no result - see {1}" -f $k, $log)
        }
        # Each failure with its message, up to the stack trace.
        $in = $false
        foreach ($l in $lines) {
            if ($l -match '^\s+Failed\s+\S+\s+\[') { $in = $true; $r.Failures += ""; $r.Failures += ($l.Trim() + $what); continue }
            if ($in -and $l -match '^\s+Stack Trace:') { $in = $false; continue }
            if ($in -and $l.Trim().Length -gt 0 -and $l -notmatch '^\s+Error Message:') { $r.Failures += "    " + $l.Trim() }
        }
    }
    return $r
}

$runStarted = Get-Date
$results = @()
try {
    foreach ($p in $parts) { $results += Invoke-Part $p }
}
finally {
    # The window goes once it has said Done (or at once, if the run was stopped).
    [IO.File]::WriteAllText((Join-Path $root "done"), "", $utf8)
    if ($window -and -not $window.WaitForExit(10000)) { try { $window.Kill() } catch { } }
}
$runWall = (Get-Date) - $runStarted

# ── One result for the lot ──
$failed = 0; $missing = 0
Write-Host ""
foreach ($r in $results) {
    if ($r.Failures.Count -gt 0) { $r.Failures | ForEach-Object { Write-Host $_ }; Write-Host "" }
}
foreach ($r in $results) {
    $verdict = if ($r.Failed -eq 0 -and $r.Missing.Count -eq 0 -and $r.Crashed.Count -eq 0) { "Passed!" } else { "Failed!" }
    Write-Host ("{0}  Failed: {1}, Passed: {2}, Skipped: {3}, Total: {4}, Wall time: {5}:{6:00} - {7}" -f `
        $verdict, $r.Failed, $r.Passed, $r.Skipped, $r.Total, [int][math]::Floor($r.Wall.TotalMinutes), $r.Wall.Seconds, $r.Title)
    if ($r.Missing.Count -gt 0) { Write-Host ("Testers with no result: " + ($r.Missing -join ", ")) }
    if ($r.Crashed.Count -gt 0) { Write-Host ("Testers that crashed, the rest of their share not run: " + ($r.Crashed -join ", ")) }
    $failed += $r.Failed; $missing += $r.Missing.Count + $r.Crashed.Count
}
Write-Host ("The whole run: {0}:{1:00}" -f [int][math]::Floor($runWall.TotalMinutes), $runWall.Seconds)
if ($failed -gt 0 -or $missing -gt 0) { exit 1 }
exit 0
