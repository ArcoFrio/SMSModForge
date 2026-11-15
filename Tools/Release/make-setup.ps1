# Builds ModForge's installer.
#
# Since 1.9.0 the setup carries nothing it installs: it downloads the editor's and the plugin's zips
# from the GitHub release of its own version (the same two the editor's updates download), checked
# against the size and SHA-256 GitHub lists, and only the plugin's when only the plugin is wanted.
# A few megabytes, so it can go on Discord whatever size ModForge grows to. Put beside the two zips,
# it installs from them without the internet. The uninstaller it leaves behind is inside it.
#
#   powershell -File Tools\Release\make-setup.ps1 -Release "<release folder>" -Version 1.9.0
#   ... -Full      # the 1.8.x setup instead: both zips inside it (80+ MB), named "... (offline).exe"
#
# Writes "Starmaker - ModForge Setup <version>.exe" into the release folder. It needs the .NET 8
# Desktop Runtime to run, as the editor does. The release must be published with both zips for the
# setup to find them.
param(
    [Parameter(Mandatory = $true)][string]$Release,
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Full
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot)
$project = Join-Path $repo 'SMSModForge.Setup\SMSModForge.Setup.csproj'
# The setup downloads the release of the version compiled into it (Shared/ForgeVersion.cs), not
# the one this script names the file after: a setup made before the version moved would install
# the previous release under the new one's name.
$compiled = [regex]::Match((Get-Content -Raw (Join-Path $repo 'Shared\ForgeVersion.cs')), 'const string Current = "([^"]+)"').Groups[1].Value
if ($compiled -ne $Version) { throw "ForgeVersion.Current is $compiled, not ${Version}: move the version first (its five places), then make the setup." }
$editor = Join-Path $Release "Starmaker - ModForge Editor $Version.zip"
$plugin = Join-Path $Release "Starmaker - ModForge Plugin $Version.zip"
if ($Full) { foreach ($zip in $editor, $plugin) { if (-not (Test-Path $zip)) { throw "No $zip" } } }

$work = Join-Path ([IO.Path]::GetTempPath()) ("SMSModForge-setup-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$common = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-p:PublishSingleFile=true', '-nologo', '-v', 'quiet')

# The uninstaller: nothing inside it.
& dotnet publish $project @common -o "$work\uninstaller" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'the uninstaller did not build' }

$inside = @("-p:Uninstaller=$work\uninstaller\SMSModForge.Setup.exe")
if ($Full) { $inside += @("-p:EditorZip=$editor", "-p:PluginZip=$plugin") }
& dotnet publish $project @common -o "$work\setup" @inside | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'the installer did not build' }

$name = if ($Full) { "Starmaker - ModForge Setup $Version (offline).exe" } else { "Starmaker - ModForge Setup $Version.exe" }
$out = Join-Path $Release $name
Copy-Item "$work\setup\SMSModForge.Setup.exe" $out -Force
Remove-Item $work -Recurse -Force
$size = (Get-Item $out).Length / 1MB
if ($Full -and $size -lt ((Get-Item $editor).Length + (Get-Item $plugin).Length) / 1MB) { throw "the installer is too small to carry the zips ($([math]::Round($size,1)) MB)" }
if (-not $Full -and $size -gt 10) { throw "the downloading setup should be small, and is $([math]::Round($size,1)) MB: did a zip get inside it?" }
"written: $out ($([math]::Round($size, 1)) MB)"
