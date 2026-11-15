# Settles in the game what the files leave open for the variable hints and the game-bug list
# (findings.md "To settle in the game", game-bugs/GAME_BUGS.md). One run, several questions, each with a
# control.
#
#   powershell -File Tools\Research\Variables\measure-items.ps1
#
# The TEST COPY and the test save only, by Tools/Game's rules. Nothing is saved: whatever is bought or set
# lives in memory and is gone when the game stops.
#
# Done before (2026-11-14): "Buy Stars" is off with the Services page open; Cerise's busts have the four
# faces. This run, the shops, opened as a shopkeeper's line does (Core[active-store], then the shop window):
# 1. The Gas Station: are Beer, Condoms, the Energy Drink and the Vape on sale? Buy the Beer: does its tile
#    go? Load the save and reopen: is it on sale again (a load restocks)?
# 2. The General Store: is the Delicate Meat on sale at the werewolf story's current stage
#    (Vacation_Save[crazyoldman-pro])? Control: its Beer, which nothing gates.
# 3. Tasha's The Panel, Tabletop tab: is the Min/Max book on sale, against its own flag and the dice set's?
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest),
    [int]$Timeout = 900
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot))
$game = Join-Path $repo 'Tools\Game'
if (-not (Test-Path "$Test\Starmaker Story.exe")) { throw "No test copy - run Tools\Game\setup-test-game.ps1 -Build" }

$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-items-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error '; throw "The plugin did not build (see $log)." }

$shop = '9_MainCanvas/ShopCore'
$gas = "$shop/gasstationstore/Core"
function Open([int]$store) { "set-active $shop false;wait 1;gset Core[active-store] $store;set-active $shop true;wait 2" }
$values = 'gget Inventory[Beer];gget Inventory[Condoms];gget InventoryTwo[Inv-energydrink];gget InventoryTwo[inv-vape];gget Core[Cash];gget Vacation_Save[crazyoldman-pro];gget Inventory[red-meat];gget Tabletop Save[tabletop-manaboost];gget Tabletop Save[tabletop-healthboosttwo]'
$gasTiles = "find $gas/Beer;find $gas/Condoms;find $gas/Groceries (3);find $gas/Groceries (2)"

& powershell -NoProfile -File "$game\launch.ps1" -Slot 0
try {
    # The test save owns everything these shops sell, so each question is asked by setting the flags in
    # memory to "not owned" (nothing is saved) and reopening the shop as a shopkeeper's line does.
    $panel = "$shop/nerdstore/Tabletop"
    $steps = @(
        "ping;wait 8;load;wait-ingame 240;wait 10;$values",
        "log == 1. Gas Station as loaded (all owned);$(Open 9);$gasTiles",
        "log == 1b. Gas Station, Beer and Energy Drink NOT owned: back on sale?;gset Inventory[Beer] false;gset InventoryTwo[Inv-energydrink] false;$(Open 9);$gasTiles",
        "log == 2. General Store, Beer NOT owned (control) and Delicate Meat NOT owned with the werewolf story at 0;gset Vacation_Save[crazyoldman-pro] 0;gset Inventory[red-meat] false;$(Open 1);find $shop/GeneralStore/Core/Beer;find $shop/GeneralStore/Core/RedMeat",
        "log == 2b. werewolf story at 3;gset Vacation_Save[crazyoldman-pro] 3;$(Open 1);find $shop/GeneralStore/Core/RedMeat",
        # A tile checks its flags only the first time the tab shows it (hidden, it stays hidden), so each
        # case loads the save afresh and sets the flags before the tab first opens.
        "log == 3a. The Panel: book owned, dice NOT owned;set-active $shop false;load;wait-ingame 240;wait 10;gset Tabletop Save[tabletop-manaboost] true;gset Tabletop Save[tabletop-healthboosttwo] false;$(Open 6);set-active $panel true;wait 1;find $panel/mini (5)",
        "log == 3b. book NOT owned, dice owned;set-active $shop false;load;wait-ingame 240;wait 10;gset Tabletop Save[tabletop-manaboost] false;gset Tabletop Save[tabletop-healthboosttwo] true;$(Open 6);set-active $panel true;wait 1;find $panel/mini (5)",
        "log == 3c. control, neither owned;set-active $shop false;load;wait-ingame 240;wait 10;gset Tabletop Save[tabletop-manaboost] false;gset Tabletop Save[tabletop-healthboosttwo] false;$(Open 6);set-active $panel true;wait 1;find $panel/mini (5)"
    )
    foreach ($s in $steps) {
        "---- " + $s.Substring(0, [Math]::Min(110, $s.Length))
        & powershell -NoProfile -File "$game\bridge.ps1" -Timeout $Timeout -Commands $s
    }
}
finally {
    & powershell -NoProfile -File "$game\stop.ps1"
}
