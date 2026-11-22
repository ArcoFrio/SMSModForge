# ModForge's features checked in the game itself (1.8.0, 12.13) - the suite to run before a release.
#
#   powershell -File Tools\Game\ingame-suite.ps1                      # every area
#   powershell -File Tools\Game\ingame-suite.ps1 -Filter "FullyQualifiedName~.B"   # one area (B: places)
#   ... -Detailed                                                     # and what each check printed, passed or not
#
# Before a release, run it as part of the one pre-release run instead: Tools\Testing\pre-release.ps1.
# Builds the plugin's Debug build - the one with the test bridge - into the TEST COPY (never the author's
# game), brings the test copy's plugin languages up to date, and runs InGameSuiteTests with
# SMSMODFORGE_INGAME pointing at the copy. Those start the copy through launch.ps1 - its own desktop, muted,
# only the test save, never while the author's game runs - and close it through stop.ps1. Each area is one
# test; each reads what the game holds and carries a control that must come out the other way.
# A transcript of everything the bridge said is written beside the suite's packs, in
# %TEMP%\SMSModForge-ingame\<time>\.
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [string]$Filter = '',
    [switch]$Detailed
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
# The checks, the plugin's Debug build into the test copy, its languages (shared with run-tests.ps1 -InGame).
& (Join-Path $PSScriptRoot 'ingame-prepare.ps1') -Test $Test

$env:SMSMODFORGE_INGAME = $Test
$which = 'FullyQualifiedName~InGameSuiteTests'
if ($Filter) { $which = "$which&$Filter" }
$verbosity = if ($Detailed) { 'detailed' } else { 'normal' }
dotnet test "$repo\SMSModForge.Tests\SMSModForge.Tests.csproj" --filter $which --logger "console;verbosity=$verbosity"
$code = $LASTEXITCODE
Remove-Item Env:\SMSMODFORGE_INGAME
exit $code
