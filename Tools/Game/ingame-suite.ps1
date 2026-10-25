# ModForge's features checked in the game itself (1.8.0, 12.13) - the suite to run before a release.
#
#   powershell -File Tools\Game\ingame-suite.ps1                      # every area
#   powershell -File Tools\Game\ingame-suite.ps1 -Filter "FullyQualifiedName~.B"   # one area (B: places)
#
# Builds the plugin's Debug build - the one with the test bridge - into the TEST COPY (never the author's
# game), brings the test copy's plugin languages up to date, and runs InGameSuiteTests with
# SMSMODFORGE_INGAME pointing at the copy. Those start the copy through launch.ps1 - its own desktop, muted,
# only the test save, never while the author's game runs - and close it through stop.ps1. Each area is one
# test; each reads what the game holds and carries a control that must come out the other way.
# A transcript of everything the bridge said is written beside the suite's packs, in
# %TEMP%\SMSModForge-ingame\<time>\.
param(
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [string]$Filter = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
if (-not (Test-Path "$Test\Starmaker Story.exe")) { throw "No test copy at $Test - run setup-test-game.ps1 -Build" }
if (Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Test*" }) {
    throw "The test copy is running; stop.ps1 first."
}

$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-ingame-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error ' ; throw "The plugin did not build (see $log)." }
"plugin (Debug, with the bridge) built into the test copy"

$languages = "$Test\BepInEx\plugins\SMSModForge\Languages"
New-Item -ItemType Directory -Force $languages | Out-Null
Get-ChildItem "$repo\SMSModForge\Languages\*.txt" | Where-Object { $_.Name -ne 'en.txt' } | Copy-Item -Destination $languages -Force

$env:SMSMODFORGE_INGAME = $Test
$which = 'FullyQualifiedName~InGameSuiteTests'
if ($Filter) { $which = "$which&$Filter" }
dotnet test "$repo\SMSModForge.Tests\SMSModForge.Tests.csproj" --filter $which --logger "console;verbosity=normal"
$code = $LASTEXITCODE
Remove-Item Env:\SMSMODFORGE_INGAME
exit $code
