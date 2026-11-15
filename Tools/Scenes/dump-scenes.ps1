# The game's scenes read out of the running test copy, for the editor's catalogue of them
# (1.9.0, extending the game's own scenes): every child of the six 4_CG_Manager-* roots, its
# art's sprite and sizes, its frame and background, and the art itself at most -MaxSide pixels.
# Then make_scene_catalog.py makes the shipped catalogue from it.
#
#   powershell -File Tools\Scenes\dump-scenes.ps1 -Out <folder outside the repository>
#
# The test save only, by Tools/Game's rules; nothing is saved. The pictures are the game's
# explicit art: the dump goes to -Out, never into the repository, and the copy the game wrote
# in its own folder is deleted once copied out.
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$MaxSide = 512,
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 1800
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$game = Join-Path $repo 'Tools\Game'
$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-dump-scenes-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error '; throw "The plugin did not build (see $log)." }

# No pack may be loaded while the scenes are read: a pack that changes one of the game's scenes
# changes what the dump sees (2026-11-15: the in-game suite's packs, left in the test copy, put
# their own pictures and frames on 8 scenes). Every installed pack is moved aside for the run and
# put back after, whatever happens.
$aside = Join-Path ([IO.Path]::GetTempPath()) ('SMSModForge-dump-scenes-packs-' + [Guid]::NewGuid().ToString('N'))
$moved = @()
foreach ($folder in @((Join-Path $Test 'Mods'), (Join-Path $Test 'BepInEx\plugins\SMSModForge\ModPacks'))) {
    if (-not (Test-Path $folder)) { continue }
    foreach ($item in Get-ChildItem $folder -Force) {
        $to = Join-Path $aside ([string]$moved.Count)
        New-Item -ItemType Directory -Force $to | Out-Null
        Move-Item $item.FullName $to
        $moved += [pscustomobject]@{ From = $item.FullName; At = (Join-Path $to $item.Name) }
    }
}
if ($moved.Count -gt 0) { "moved $($moved.Count) installed pack(s) aside for the dump" }
try {
    & powershell -NoProfile -File "$game\launch.ps1" -Slot 0
    try {
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands "ping;wait 8;load;wait-ingame 240;wait 6;scene-catalog $MaxSide"
    }
    finally {
        & powershell -NoProfile -File "$game\stop.ps1"
    }
}
finally {
    foreach ($m in $moved) { Move-Item $m.At $m.From }
    if ($moved.Count -gt 0) { "put $($moved.Count) pack(s) back"; Remove-Item $aside -Recurse -Force }
}

$tsv = Get-ChildItem (Join-Path $Test 'BepInEx\plugins\SMSModForge\DebugBridge') -Recurse -Filter scenes.tsv |
       Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $tsv) { throw 'The game wrote no scenes.tsv.' }
New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item (Join-Path $tsv.DirectoryName '*') $Out -Force
Remove-Item $tsv.DirectoryName -Recurse -Force
"copied $((Get-ChildItem $Out -Filter *.png).Count) pictures and scenes.tsv to $Out"
