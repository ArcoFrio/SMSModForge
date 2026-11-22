# The one test run before a release, in one progress window with one estimate (the author, 2026-11-22):
#   1. every test, split over the testers         2. the timing tests, alone
#   3. the window tests again in EX               4. EX's timing tests, alone
#   5. the in-game suite, in the test copy (its own desktop, muted, the test save only)
#
#   powershell -File Tools\Testing\pre-release.ps1
#   powershell -File Tools\Testing\pre-release.ps1 -Testers 6     # a machine busy with something else
#
# Close the game first if you are playing anything: the timing tests measure frames a second and fail beside
# a game, and the in-game suite will not start beside your own copy of Starmaker Story.
param(
    [int]$Testers = 0,
    [switch]$NoBuild
)
& (Join-Path $PSScriptRoot 'run-tests.ps1') -WithEx -InGame -Testers $Testers -NoBuild:$NoBuild
exit $LASTEXITCODE
