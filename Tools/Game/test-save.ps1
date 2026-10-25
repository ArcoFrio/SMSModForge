# The test copy's own saves, shared by setup-test-game.ps1, make-test-save.ps1 and launch.ps1 (dot-sourced).
#
# The author's saves must never change (2026-10-24). Every copy of the game saves into
# LocalLow\<company>\<product>\Saves, and the game keeps its list of save slots inside slot 0 - a save shared
# by every slot - so a test copy saving beside the author's would write their slot 0 even when it played a
# slot of its own. So the test copy is renamed (TestProduct, patched into its game data by
# setup-test-game.ps1): its saves, settings and window state are all its own, and nothing of the author's is
# in reach. Its saves are a copy of the author's slots 0 and 1, kept clean beside the test copy and put back
# before every start.

$AuthorsProduct = 'Starmaker Story'
# The same length as the name it replaces, so nothing after it in the game's data moves.
$TestProduct = 'Starmaker Tests'
$TestSlot = 1
$TestSaveTitle = '<color=#F5A93B>ModForge test save</color>'

function LocalLow { Join-Path (Split-Path ([Environment]::GetFolderPath('ApplicationData'))) 'LocalLow\Arvus Games' }
function AuthorsSaves { Join-Path (LocalLow) "$AuthorsProduct\Saves" }
function TestSaves { Join-Path (LocalLow) "$TestProduct\Saves" }
function CleanTestSaves([string]$test) { Join-Path $test 'TestSave\Saves' }

# The test copy's saves as the clean copy has them, whatever the last check did. Never the author's folder.
function RestoreTestSave([string]$test) {
    $clean = CleanTestSaves $test
    if (-not (Test-Path (Join-Path $clean ('NANOSAVE_{0:D4}\SAVE.GZ' -f $TestSlot)))) {
        throw "No clean test save at $clean - run make-test-save.ps1 first."
    }
    $saves = TestSaves
    if ($saves -eq (AuthorsSaves) -or $saves -notlike "*\$TestProduct\Saves") { throw "Not the test copy's saves: $saves" }
    if (Test-Path $saves) { Remove-Item $saves -Recurse -Force }
    New-Item -ItemType Directory -Force (Split-Path $saves) | Out-Null
    Copy-Item $clean $saves -Recurse
    "test saves put back clean ($saves)"
}

# The product name in a copy of the game's data, from one name to the other. Only ever the test copy's.
function RenameProduct([string]$test, [string]$from, [string]$to) {
    if ($from.Length -ne $to.Length) { throw "The names must be the same length." }
    $data = Join-Path $test 'Starmaker Story_Data'
    foreach ($file in 'globalgamemanagers', 'app.info') {
        $path = Join-Path $data $file
        $bytes = [IO.File]::ReadAllBytes($path)
        $text = [Text.Encoding]::ASCII.GetString($bytes)
        $at = $text.IndexOf($from)
        if ($at -lt 0) {
            if ($text.IndexOf($to) -ge 0) { continue }   # already renamed
            throw "No '$from' in $path - the game's data has changed (GAME_UPDATE_CHECKS.md)."
        }
        if ($text.IndexOf($from, $at + 1) -ge 0) { throw "'$from' is in $path more than once; not guessing which." }
        [Text.Encoding]::ASCII.GetBytes($to).CopyTo($bytes, $at)
        [IO.File]::WriteAllBytes($path, $bytes)
    }
}
