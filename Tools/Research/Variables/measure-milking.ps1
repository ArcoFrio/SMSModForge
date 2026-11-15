# Does the milking minigame show Anna's objects whoever is milked (GAME_BUGS.md)? Each girl's barn talk runs
# her own list entry (MilkingMinigame/List/N_Name), then switches MilkingMinigame on, whose on-enable switches
# every entry off and runs 1_Anna, which points the milk-* variables at Anna's objects. Here: Emma's barn
# talk, "Let's do it by hand today.", then which entry is showing and where the pointers point.
# The test save only, by Tools/Game's rules; nothing is saved.
#
#   powershell -File Tools\Research\Variables\measure-milking.ps1
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 900
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-milking-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error '; throw "The plugin did not build (see $log)." }
$talk = '5_Levels/125_BarnBackInterior/Dialogue_MilkDefaulttalk/Defaultmilktalk_Emma'
$read = 'gget Minigame[milk-default];gget Minigame[milk-default-before];gget Minigame[milk-default-after];find MilkingMinigame;find MilkingMinigame/List/1_Anna;find MilkingMinigame/List/5_Emma'
& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 8;gset Milking Save[bafa-milkprogress] 0;gset Minigame[cd-milked-emma] false;$read",
        "log == Emma's barn talk;vanilla-dialogue $talk;wait 3;line;next;wait 2;line;" + ((@("wait 2;press-text do it by hand today.") * 5) -join ';') + ";wait 2;line;next;wait 6;line",
        "log == the minigame is on;$read"
    )
    foreach ($s in $steps) {
        "---- " + $s.Substring(0, [Math]::Min(100, $s.Length))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
