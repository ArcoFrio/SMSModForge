# Does the race's speed-bump upgrade ever pay (GAME_BUGS.md)? A speed bump switches Racing_Minigame/RampBump
# on. Its two on-enable scripts start together: one moves things, waits 0.1 s, moves them back and switches
# RampBump off; the other, with the upgrade (Events_7[racing-update-dildo]), waits 0.2 s and adds +50.
# Game Creator cancels a switched-off object's running scripts, so the +50 lands only if RampBump is still
# on at 0.2 s - which turns on how long the moves take, not in the files. This times it: RampBump's state
# read right after it is switched on and every few hundredths of a second after.
# The test save only, by Tools/Game's rules; nothing is saved.
#
#   powershell -File Tools\Research\Variables\measure-speedbump.ps1
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 600
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
$bump = 'Racing_Minigame/RampBump'
$sample = ((@("wait 0.05;find $bump") * 6) + (@("wait 0.25;find $bump") * 12)) -join ';'
& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 8;find Racing_Minigame;find $bump",
        "log == RampBump on, upgrade owned;gset Events_7[racing-update-dildo] true;set-active Racing_Minigame true;wait 1;set-active $bump false;wait 0.5;set-active $bump true;find $bump;$sample"
    )
    foreach ($s in $steps) {
        "---- " + $s.Substring(0, [Math]::Min(100, $s.Length))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
