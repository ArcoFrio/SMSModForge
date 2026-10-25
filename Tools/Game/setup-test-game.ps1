# Makes, or brings up to date, the copy of the game ModForge's in-game checks run in - beside the real one.
# The author's own game folder is only read: their plugins, packs, saves and dumps stay as they are, and the
# copy carries nothing but the game, BepInEx's own core and what ModForge needs. The Debug plugin with the
# test bridge is built into the copy (-Build), never into the author's game.
#   powershell -File Tools\Game\setup-test-game.ps1 -Build
param(
    [string]$Game = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E',
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path "$Game\Starmaker Story.exe")) { throw "No game at $Game" }
if (Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Test*" }) {
    throw "The test copy is running; close it first."
}

function Mirror($from, $to) {
    robocopy $from $to /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copy failed: $from" }
}

New-Item -ItemType Directory -Force $Test, "$Test\BepInEx\plugins\SMSModForge", "$Test\Mods" | Out-Null

# The game itself.
foreach ($f in 'Starmaker Story.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'winhttp.dll',
               '.doorstop_version', 'doorstop_config.ini') {
    Copy-Item -LiteralPath (Join-Path $Game $f) -Destination $Test -Force
}
foreach ($d in 'Starmaker Story_Data', 'MonoBleedingEdge', 'D3D12') { Mirror (Join-Path $Game $d) (Join-Path $Test $d) }

# Renamed, so that it saves - and keeps its settings and window state - in a place of its own: the author's
# saves are never in reach of a check (test-save.ps1). The copy above puts the game's own name back each time.
. (Join-Path $PSScriptRoot 'test-save.ps1')
RenameProduct $Test $AuthorsProduct $TestProduct

# BepInEx: its own core and configuration - none of the author's other plugins.
foreach ($d in 'core', 'config', 'patchers') {
    if (Test-Path "$Game\BepInEx\$d") { Mirror "$Game\BepInEx\$d" "$Test\BepInEx\$d" }
}
# Never a console window: it would open on the author's screen and take the focus.
$cfg = "$Test\BepInEx\config\BepInEx.cfg"
if (Test-Path $cfg) {
    $text = Get-Content $cfg -Raw
    $text = $text -replace '(?ms)(\[Logging\.Console\].*?^Enabled\s*=\s*)true', '${1}false'
    Set-Content $cfg $text -NoNewline -Encoding UTF8
}

# What ModForge's plugin needs beside it.
Copy-Item "$Game\BepInEx\plugins\Newtonsoft.Json.dll" "$Test\BepInEx\plugins\" -Force
if (Test-Path "$Game\BepInEx\plugins\VanillaFrames") { Mirror "$Game\BepInEx\plugins\VanillaFrames" "$Test\BepInEx\plugins\VanillaFrames" }
$repo = Split-Path (Split-Path $PSScriptRoot)
Mirror "$repo\SMSModForge\Languages" "$Test\BepInEx\plugins\SMSModForge\Languages"
Remove-Item "$Test\BepInEx\plugins\SMSModForge\Languages\en.txt" -ErrorAction SilentlyContinue

if ($Build) {
    dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\" | Select-String -Pattern ' error |Build succeeded'
}
"test copy ready: $Test"
