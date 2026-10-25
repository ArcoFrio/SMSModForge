# Closes the test copy of the game: asks it through the bridge, then ends it if it has not gone in 15 s.
# Never touches a game the author is in, nor their own copy.
param(
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [string]$Bridge = 'b0'
)
Add-Type -Path (Join-Path $PSScriptRoot 'GameNative.cs')
$games = @(Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Test*" })
if ($games.Count -eq 0) { "not running"; return }
foreach ($g in $games) {
    if ($g.Id -eq [GameNative]::ForegroundProcess()) { "the author is in the test copy (pid $($g.Id)) - leaving it"; continue }
    Set-Content (Join-Path $Test "BepInEx\plugins\SMSModForge\DebugBridge\$Bridge\commands.txt") 'quit'
    if (-not $g.WaitForExit(15000)) { Stop-Process -Id $g.Id -Force; "ended pid $($g.Id)" } else { "closed pid $($g.Id)" }
}
