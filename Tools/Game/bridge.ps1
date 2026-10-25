# Sends commands to the test copy's bridge (DebugBridge.cs) and prints its answers.
#   powershell -File Tools\Game\bridge.ps1 -Commands "ping;wait-ingame 120;get tdmoneyinc"
# Separate commands with ; - powershell -File hands a list over as one string (a , works when no ; is used).
# Commands: ping | wait <s> | wait-ingame [s] | load <slot> | get <var> | set <var> <value> |
#           dialogue <packId> <key> | vanilla-dialogue <path> | wait-dialogue-end [s] | playing |
#           click <button's words> | speaker | menu | screen <name> | shot <name> |
#           inspect <root>/<path> | materials <word> | volumes |
#           watch <s> <packId> <var,var> | listen <s> <name> | mute on|off | log <text> | quit
# watch and listen answer when their time is up, and the commands after them run meanwhile. listen measures
# each sound inside the game, before its mixer - but the game's own volume at 0 (-mfMute) gives it only
# silence (1.8E, measured), so it needs "mute off"; Windows' mute of the game stays on unless -Sound.
param(
    [string]$Test = 'D:\Downloads\SkyrimM\spec\uTorrent\Starmaker Story 1.8E - ModForge test',
    [string]$Bridge = 'b0',
    [int]$Timeout = 180,
    [Parameter(Mandatory = $true)][string[]]$Commands
)
$Commands = @($Commands | ForEach-Object { if ($_ -match ';') { $_ -split ';' } elseif ($_ -match ',') { $_ -split ',' } else { $_ } } |
              ForEach-Object { $_.Trim() } | Where-Object { $_ })
$dir = Join-Path $Test "BepInEx\plugins\SMSModForge\DebugBridge\$Bridge"
$results = Join-Path $dir 'results.txt'
function Lines { if (Test-Path $results) { @(Get-Content $results) } else { @() } }
$before = (Lines).Count
Set-Content (Join-Path $dir 'commands.txt') ($Commands -join "`n")
$deadline = (Get-Date).AddSeconds($Timeout)
$answers = @()
while ((Get-Date) -lt $deadline) {
    $answers = @(Lines | Select-Object -Skip $before | Where-Object { $_ -match '^\d\d:\d\d:\d\d\.\d{3} (ok|error|timeout)' })
    # One answer per command, "wait" included.
    if ($answers.Count -ge $Commands.Count) { break }
    Start-Sleep -Milliseconds 250
}
Lines | Select-Object -Skip $before
if ($answers.Count -lt $Commands.Count) { "(gave up waiting after $Timeout s)" }
