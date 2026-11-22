# Gets the TEST COPY ready for ModForge's in-game suite: checks it is there and not running, builds the plugin's
# Debug build - the one with the test bridge - into it (never the author's game), and brings its plugin languages
# up to date. Used by ingame-suite.ps1 and by Tools\Testing\run-tests.ps1 -InGame (the pre-release run).
#
#   powershell -File Tools\Game\ingame-prepare.ps1 [-Test <test copy>]
param(
    [string]$Test = (([xml](Get-Content -Raw (Join-Path $env:USERPROFILE '.sms\game.props'))).Project.PropertyGroup.StarmakerModForgeTest)
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
if (-not (Test-Path "$Test\Starmaker Story.exe")) { throw "No test copy at $Test - run setup-test-game.ps1 -Build" }
if (Get-Process -Name 'Starmaker Story' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Test*" }) {
    throw "The test copy is running; stop.ps1 first."
}

$log = Join-Path ([IO.Path]::GetTempPath()) 'SMSModForge-ingame-build.txt'
dotnet build "$repo\SMSModForge.PackPlugin\SMSModForge.PackPlugin.csproj" -c Debug "-p:PluginOutputPath=$Test\BepInEx\plugins\\" > $log
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-String ' error ' ; throw "The plugin did not build (see $log)." }
"plugin (Debug, with the bridge) built into the test copy"

$languages = "$Test\BepInEx\plugins\SMSModForge\Languages"
New-Item -ItemType Directory -Force $languages | Out-Null
Get-ChildItem "$repo\SMSModForge\Languages\*.txt" | Where-Object { $_.Name -ne 'en.txt' } | Copy-Item -Destination $languages -Force
