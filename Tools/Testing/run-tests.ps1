# Runs the test suite split across several test processes at once - the
# testers - and prints one result for all of them.
#
#   powershell -File Tools/Testing/run-tests.ps1                  # everything, default testers
#   powershell -File Tools/Testing/run-tests.ps1 -Testers 8
#   powershell -File Tools/Testing/run-tests.ps1 -Filter "FullyQualifiedName~Navigator"
#   powershell -File Tools/Testing/run-tests.ps1 -WithEx          # then the window tests again in EX
#   powershell -File Tools/Testing/run-tests.ps1 -Theme Neon      # every test with that theme on
#
# Each tester runs its share of the classes (split by what they took last time,
# see SMSModForge.Tests/Shards.cs), on a desktop of its own, with no console
# window. One progress window covers the whole run: the first tester's, on the
# leftmost monitor, open until the last tester is done. The testers' output is
# kept in the run's folder, printed at the end on a failure.
#
# EX (1.8.0): the EX themes draw every control with templates of their own, so
# the tests that build a window run a second time with an EX theme on
# (SMSMODFORGE_TEST_THEME, read by WindowHarness). The same tests - the theme
# is a parameter of the run, not a copy of the suite - and only the window
# ones: a test that builds no window cannot tell the themes apart.
#
# Exit code: 0 when every tester reported, none crashed, and nothing failed.
param(
    [int]$Testers = 0,
    [string]$Filter = "",
    [string]$Theme = "",
    [switch]$WithEx,
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

if (-not $NoBuild) {
    & dotnet build $project --nologo -v quiet | Out-Host
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed."; exit $LASTEXITCODE }
}
if (-not (Test-Path $dll)) { Write-Host "No test build at $dll"; exit 1 }

# One pass: every tester started, waited for, and their results added up.
function Invoke-Pass([string]$passTheme, [string]$passFilter, [string]$label) {
    $run = Join-Path ([IO.Path]::GetTempPath()) ("SMSModForge-tests\run-" + (Get-Date -Format "yyyyMMdd-HHmmss") + $label)
    New-Item -ItemType Directory -Force -Path $run | Out-Null

    $started = Get-Date
    $procs = @()
    for ($k = 1; $k -le $Testers; $k++) {
        $log = Join-Path $run "tester-$k.log"
        $arguments = "test `"$dll`" --nologo"
        if ($passFilter) { $arguments += " --filter `"$passFilter`"" }
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "cmd.exe"
        $psi.Arguments = "/c dotnet $arguments > `"$log`" 2>&1"
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true            # nothing of a tester reaches the screen
        $psi.WorkingDirectory = $repo
        $psi.EnvironmentVariables["SMSMODFORGE_SHARD"] = "$k/$Testers"
        $psi.EnvironmentVariables["SMSMODFORGE_SHARD_DIR"] = $run
        if ($passTheme) { $psi.EnvironmentVariables["SMSMODFORGE_TEST_THEME"] = $passTheme }
        $procs += [System.Diagnostics.Process]::Start($psi)
    }
    $what = if ($passTheme) { " in $passTheme" } else { "" }
    Write-Host "$Testers testers started$what; notes and output in $run"
    foreach ($p in $procs) { $p.WaitForExit() }
    $wall = (Get-Date) - $started

    $summary = '^\s*(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+),\s+Duration:\s+(.+?)\s+-'
    $r = [ordered]@{ Failed = 0; Passed = 0; Skipped = 0; Total = 0; Missing = @(); Crashed = @(); Failures = @(); Wall = $wall; Theme = $passTheme }
    for ($k = 1; $k -le $Testers; $k++) {
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

$passes = @(Invoke-Pass $Theme $Filter "")
if ($WithEx) {
    $exFilter = if ($Filter) { "($Filter)&Speed=Slow" } else { "Speed=Slow" }
    Write-Host ""
    $passes += Invoke-Pass $ExTheme $exFilter "-ex"
}

# ── One result for the lot ──
$failed = 0; $missing = 0
Write-Host ""
foreach ($r in $passes) {
    if ($r.Failures.Count -gt 0) { $r.Failures | ForEach-Object { Write-Host $_ }; Write-Host "" }
}
foreach ($r in $passes) {
    $verdict = if ($r.Failed -eq 0 -and $r.Missing.Count -eq 0 -and $r.Crashed.Count -eq 0) { "Passed!" } else { "Failed!" }
    $in = if ($r.Theme) { " in " + $r.Theme } else { "" }
    Write-Host ("{0}  Failed: {1}, Passed: {2}, Skipped: {3}, Total: {4}, Wall time: {5}:{6:00} ({7} testers){8}" -f `
        $verdict, $r.Failed, $r.Passed, $r.Skipped, $r.Total, [int][math]::Floor($r.Wall.TotalMinutes), $r.Wall.Seconds, $Testers, $in)
    if ($r.Missing.Count -gt 0) { Write-Host ("Testers with no result: " + ($r.Missing -join ", ")) }
    if ($r.Crashed.Count -gt 0) { Write-Host ("Testers that crashed, the rest of their share not run: " + ($r.Crashed -join ", ")) }
    $failed += $r.Failed; $missing += $r.Missing.Count + $r.Crashed.Count
}
if ($failed -gt 0 -or $missing -gt 0) { exit 1 }
exit 0
