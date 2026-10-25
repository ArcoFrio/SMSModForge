# Builds ModForge's installer (1.8.0) from a release's two zips: one .exe that carries them, with the
# uninstaller it leaves behind inside it too (the same program, built without what it installs).
#   powershell -File Tools\Release\make-setup.ps1 -Release "D:\...\Starmaker - ModForge 1.8.0" -Version 1.8.0
# Writes "Starmaker - ModForge Setup <version>.exe" beside the zips. It needs the .NET 8 Desktop Runtime to
# run, as the editor does.
param(
    [Parameter(Mandatory = $true)][string]$Release,
    [Parameter(Mandatory = $true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$project = Join-Path $repo 'SMSModForge.Setup\SMSModForge.Setup.csproj'
$editor = Join-Path $Release "Starmaker - ModForge Editor $Version.zip"
$plugin = Join-Path $Release "Starmaker - ModForge Plugin $Version.zip"
foreach ($zip in $editor, $plugin) { if (-not (Test-Path $zip)) { throw "No $zip" } }

$work = Join-Path ([IO.Path]::GetTempPath()) ("SMSModForge-setup-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishSingleFile=true', '-nologo', '-v', 'quiet')

# The uninstaller: nothing inside it.
& dotnet publish $project @common -o "$work\uninstaller" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'the uninstaller did not build' }

# The installer: both zips and the uninstaller inside it.
& dotnet publish $project @common -o "$work\setup" "-p:EditorZip=$editor" "-p:PluginZip=$plugin" "-p:Uninstaller=$work\uninstaller\SMSModForge.Setup.exe" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'the installer did not build' }

$out = Join-Path $Release "Starmaker - ModForge Setup $Version.exe"
Copy-Item "$work\setup\SMSModForge.Setup.exe" $out -Force
Remove-Item $work -Recurse -Force
$size = (Get-Item $out).Length / 1MB
if ($size -lt ((Get-Item $editor).Length + (Get-Item $plugin).Length) / 1MB) { throw "the installer is too small to carry the zips ($([math]::Round($size,1)) MB)" }
"written: $out ($([math]::Round($size, 1)) MB)"
