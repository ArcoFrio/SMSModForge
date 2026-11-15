# What follows Chloe's "Please leave my home!" (ChloeStart/FirstTalk #180338115), whose jump has no target -
# the only such jump in the game's 722 conversations (GAME_BUGS.md). Plays her first talk in the test copy,
# declines the shoot ("Sorry, I'm not interested.") and reads every line after it: does the talk end, start
# over, or go on into the shoot it just declined (the next top-level line, "Wow, it looks even bigger in
# person...")? The test save only, by Tools/Game's rules; nothing is saved.
#
#   powershell -File Tools\Research\Variables\measure-chloe.ps1
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 900
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
$step = 'next;wait 2;line'
& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 8;gget Events_4[chloe-declined];vanilla-dialogue 8_Room_Talk/ChloeStart/FirstTalk;wait 3;line",
        (@($step) * 2 -join ';'),
        # The options come up a few seconds after the line: try again until one is pressed (a press once
        # they are gone finds nothing and changes nothing).
        "next;" + (@('wait 2;press-text Hey, Anna.') * 5 -join ';') + ";wait 2;line",
        (@($step) * 7 -join ';'),
        "log == declining;next;" + (@("wait 2;press-text Sorry, I'm not interested.") * 5 -join ';') + ";wait 2;line",
        (@($step) * 6 -join ';') + ';gget Events_4[chloe-declined]'
    )
    foreach ($s in $steps) {
        "---- " + $s.Substring(0, [Math]::Min(100, $s.Length))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
