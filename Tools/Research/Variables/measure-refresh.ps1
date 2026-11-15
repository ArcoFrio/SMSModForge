# Measures whether a Starmaker gallery tile or comment whose flag turns true during play shows before a
# reload (1.9.0 variable hints, findings.md "To settle in the game"). Each tile and comment hides itself when
# switched on while its flag is false, and nothing in the files switches it back on: so one unlocked after
# the page was first switched on may show only after loading a game.
#
#   powershell -File Tools\Research\Variables\measure-refresh.ps1
#
# The TEST COPY and the test save only, by Tools/Game's rules (built into the copy, its own desktop, muted).
# Nothing is saved: the two flags are set in memory and lost when the game stops.
# Subjects, from the run's lists.tsv (the test save's values): Gallery_2[56] (false; tile Image (137)) and
# Comments[31] (false; CoreComments/31 (2)). Controls, true in the save: Gallery_2[72] (Image (177)) and
# Comments[25] (CoreComments/25) must show throughout.
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 900
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
if (-not (Test-Path "$Test\Starmaker Story.exe")) { throw "No test copy - run Tools\Game\setup-test-game.ps1 -Build" }

$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-refresh-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error '; throw "The plugin did not build (see $log)." }

$core = '9_MainCanvas/Starmaker/Starmaker_Core'
$tiles = "$core/Gallery/Scrollarea/CoreGallery"
$notes = "$core/Comments/Comments/Scrollarea/CoreComments"
$look = "find $core/Gallery;find $core/Comments;find $tiles/Image (137);find $tiles/Image (177);find $notes/31 (2);find $notes/25"
$open = "set-active 9_MainCanvas/Starmaker true;wait 1;set-active $core true;wait 1"
$close = "set-active $core false;set-active 9_MainCanvas/Starmaker false;wait 1"

& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 10;gget Gallery_2[56];gget Gallery_2[72];gget Comments[31];gget Comments[25]",
        "log == after the load, page not opened;$look",
        "log == page opened;$open;$look",
        "log == page closed, subject flags set true;$close;gset Gallery_2[56] true;gset Comments[31] true",
        "log == page opened again;$open;$look"
    )
    foreach ($s in $steps) {
        "---- $s".Substring(0, [Math]::Min(120, $s.Length + 5))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
