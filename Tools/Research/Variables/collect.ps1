# Collects what the running game holds about its global name variables, for the editor's variable hints
# (1.9.0, PLAN-1.9.0.md): every list and its values, every script, prefab and asset naming a variable, and
# names written in text. Twice: on the main menu (each value as a new game starts) and in the test save (the
# game scene's scripts). Writes runs\<date>\menu and runs\<date>\game; see README.md.
#
#   powershell -File Tools\Research\Variables\collect.ps1
#
# The TEST COPY only, by Tools/Game's rules: the Debug plugin built into it, started on a desktop of its own,
# muted, never while the author's game runs, the test save only. Nothing is changed in the game: the command
# reads memory and writes files beside the copy's log, which are moved here.
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [string]$Out = (Join-Path $PSScriptRoot ("runs\" + (Get-Date -Format 'yyyy-MM-dd'))),
    [int]$Timeout = 1200
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
if (-not (Test-Path "$Test\Starmaker Story.exe")) { throw "No test copy - run Tools\Game\setup-test-game.ps1 -Build" }

$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-variables-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error '; throw "The plugin did not build (see $log)." }
"plugin (Debug, with the bridge) built into the test copy"

$before = @(Get-ChildItem "$Test\BepInEx" -Directory -Filter 'SMSModForge-variables-*' -ErrorAction SilentlyContinue | ForEach-Object FullName)

# The game alone: the test copy's packs (the in-game suite's, exported into Mods) go aside while it runs,
# or their scripts - a copy of the game's room talk among them - read as the game's own (2026-11-13: 8
# references, 34 conversations). The suite exports its packs again on every run, so nothing is lost if
# this stops before putting them back.
$mods = Join-Path $Test 'Mods'
$aside = Join-Path $Test 'Mods.aside-for-research'
if (Test-Path $aside) { throw "$aside already exists - a run stopped before putting its packs back; move them into Mods first." }
New-Item -ItemType Directory -Force $aside | Out-Null
Get-ChildItem $mods -Force -ErrorAction SilentlyContinue | Move-Item -Destination $aside
"packs set aside: " + @(Get-ChildItem $aside -Force).Count
try {
    & powershell -NoProfile -File "$game\launch.ps1" -Slot 0
    try {
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands 'ping;wait 8;variable-evidence menu'
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands 'load;wait-ingame 240;wait 10;variable-evidence game'
    }
    finally {
        & powershell -NoProfile -File "$game\stop.ps1"
    }
}
finally {
    Get-ChildItem $aside -Force | Move-Item -Destination $mods
    Remove-Item $aside -Force
    "packs put back: " + @(Get-ChildItem $mods -Force).Count
}

New-Item -ItemType Directory -Force $Out | Out-Null
$made = @(Get-ChildItem "$Test\BepInEx" -Directory -Filter 'SMSModForge-variables-*' | Where-Object { $before -notcontains $_.FullName } | Sort-Object Name)
foreach ($d in $made) {
    $which = if ($d.Name -like 'SMSModForge-variables-menu-*') { 'menu' } elseif ($d.Name -like 'SMSModForge-variables-game-*') { 'game' } else { $d.Name }
    $target = Join-Path $Out $which
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Move-Item $d.FullName $target
    "$which -> $target : " + (Get-Content (Join-Path $target 'summary.txt') -ErrorAction SilentlyContinue)
}
if ($made.Count -lt 2) { throw "Expected the menu and the game folders, found $($made.Count)." }
