param(
	[ValidateSet("Debug", "Release")]
	[string]$Configuration = "Debug",
	[string]$ComicRackDir = $env:COMICRACK_DIR,
	[switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$solution = Join-Path $PSScriptRoot "LibraryOrganizer.sln"

if (!$ComicRackDir) {
	Write-Error "Set COMICRACK_DIR, or pass -ComicRackDir, to the output folder of a ComicRack CE build that has the DotNetPlugin API."
	exit 1
}

dotnet build $solution -c $Configuration "-p:ComicRackDir=$ComicRackDir" -nologo -v:minimal "-clp:ErrorsOnly;Summary"
if ($LASTEXITCODE -ne 0) {
	Write-Error "Build failed."
	exit 1
}
if ($SkipTests) {
	exit 0
}

$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"
$failed = @()
foreach ($name in "LibraryOrganizer.Tests", "LibraryOrganizer.LoadTests") {
	$exe = Join-Path $PSScriptRoot "tests\$name\bin\$Configuration\net48\$name.exe"
	if (!(Test-Path $exe)) {
		Write-Error "The build reported success, but $exe does not exist."
		exit 1
	}
	Write-Output "Running $name..."
	& $exe --progress off --minimum-expected-tests 1
	if ($LASTEXITCODE -ne 0) {
		$failed += $name
	}
}
if ($failed) {
	Write-Error "Tests failed: $($failed -join ', ')"
	exit 1
}
Write-Output "All test suites passed."
