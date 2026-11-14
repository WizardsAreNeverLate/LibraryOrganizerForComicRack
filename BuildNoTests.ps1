param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[string]$ComicRackDir = $env:COMICRACK_DIR
)

# Builds the solution and packs LibraryOrganizer.crplugin without running the test suites. See Build.ps1.
& (Join-Path $PSScriptRoot "Build.ps1") @PSBoundParameters -SkipTests
exit $LASTEXITCODE
