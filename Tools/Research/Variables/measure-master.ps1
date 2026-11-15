# Does the Master trophy miss Pervert when it is the last trait (GAME_BUGS.md)? The car wash's own script
# (8_Room_Talk/Gasstation/CheckCarWashTotals, on carwashtotals reaching 3) sets new-trait to Pervert BEFORE
# Pervert-5 = 5, and the trophy check (6_Effects/Effect_Canvas/NewTrait) runs on new-trait changing. Run
# here with the game's own script: every other trait at 5, Pervert at 0, then carwashtotals = 3.
# Control: Pervert already 5, a new-trait change must grant the trophy (the check and the setup work).
# The test save only, by Tools/Game's rules; nothing is saved.
#
#   powershell -File Tools\Research\Variables\measure-master.ps1
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 900
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
$others = 'Fit-5', 'Athlete-5', 'Photography-Enthusiast-5', 'Aspiring-Photographer-5', 'Professional-Photographer-5', 'Urban-Linguist-5', 'Hacker', 'heavenly-husband-5', 'Living-Weapon'
$setup = (($others | ForEach-Object { "gset Traits[$_] 5" }) -join ';') + ';gset Traits[Pervert-5] 0;gset Achievements[master] false;gset Core_NoSave[new-trait] none;gset Events_8[carwashtotals] 2'
$read = 'gget Events_8[carwashtotals];gget Core_NoSave[new-trait];gget Traits[Pervert-5];gget Achievements[master]'
$wash = '8_Room_Talk/Gasstation/CheckCarWashTotals'
& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 8;find $wash;find 6_Effects/Effect_Canvas/NewTrait",
        # The script lives in the Gas Station room, switched off unless you are there: switch the room on.
        "log == the car wash, Pervert last;$setup;wait 1;set-active 8_Room_Talk/Gasstation true;set-active $wash true;wait 1;find $wash;gset Events_8[carwashtotals] 3;wait 3;$read",
        "log == control: Pervert already 5, new-trait changes;gset Traits[Pervert-5] 5;gset Achievements[master] false;gset Core_NoSave[new-trait] none;wait 1;gset Core_NoSave[new-trait] Fit;wait 3;$read"
    )
    foreach ($s in $steps) {
        "---- " + $s.Substring(0, [Math]::Min(100, $s.Length))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
