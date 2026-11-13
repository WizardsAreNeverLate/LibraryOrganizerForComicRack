<#
.SYNOPSIS
In-app smoke test: a real ComicRack runs Library Organizer (Quick) on two books, then undoes the run from History.

.DESCRIPTION
Builds the plugin and the probe (tools\smoke\Probe.cs) in Release, installs both under the ComicRack build's Scripts folder,
starts ComicRack with an alternate configuration (-ac LoSmoke), and waits for the probe to close it. The plugin's data folder
is redirected with LIBRARYORGANIZER_NET_DATA, so the user's %APPDATA%\LibraryOrganizer.NET is never touched; the script
checks that. Everything lands in tools\smoke\runs\<stamp>\. Exit code 0 only when every assertion passed.

.PARAMETER Bin
The output folder of a ComicRack CE build with the DotNetPlugin API. Defaults to COMICRACK_DIR.
#>
param([string]$Bin = $env:COMICRACK_DIR)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here "..\..")).Path
if (!$Bin) { throw "Set COMICRACK_DIR, or pass -Bin." }
if (Get-Process ComicRack -ErrorAction SilentlyContinue) { Write-Output "BLOCKED: a ComicRack process is already running"; exit 2 }

foreach ($project in "$repo\src\LibraryOrganizer\LibraryOrganizer.csproj", "$here\LoSmoke.csproj") {
	dotnet build $project -c Release "-p:ComicRackDir=$Bin" -nologo -v:quiet | Out-Null
	if ($LASTEXITCODE -ne 0) { throw "build failed: $project" }
}
$crplugin = "$repo\src\LibraryOrganizer\bin\Release\net48\LibraryOrganizer.crplugin"
$probeDll = "$here\bin\Release\net48\LoSmoke.dll"

function Get-UserData {
	$folder = Join-Path $env:APPDATA "LibraryOrganizer.NET"
	if (Test-Path $folder) { Get-ChildItem $folder -Recurse -File | ForEach-Object { "$($_.FullName) $($_.Length) $($_.LastWriteTimeUtc.Ticks)" } } else { @("(absent)") }
}
$before = @(Get-UserData)

$run = Join-Path $here ("runs\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$report = Join-Path $run "out\report.txt"
New-Item -ItemType Directory -Force (Join-Path $run "out"), (Join-Path $run "data") | Out-Null

$plugin = Join-Path $Bin "Scripts\Library Organizer .NET"
$probe = Join-Path $Bin "Scripts\LoSmoke"
if ((Test-Path $plugin) -or (Test-Path $probe)) { throw "an earlier install is still in $Bin\Scripts; remove it first" }
New-Item -ItemType Directory -Force $plugin, $probe | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($crplugin, $plugin)
Copy-Item $probeDll $probe

$failures = @()
function Assert-Smoke([string]$name, [bool]$ok, [string]$detail) {
	Add-Content -Path $report -Value "ASSERT $(if ($ok) { 'PASS' } else { 'FAIL' }) ${name}: $detail"
	if (-not $ok) { $script:failures += $name }
}

$env:LO_SMOKE_ROOT = $run
$env:LIBRARYORGANIZER_NET_DATA = Join-Path $run "data"
try {
	$p = Start-Process (Join-Path $Bin "ComicRack.exe") -ArgumentList "-ac", "LoSmoke" -PassThru
	$closedItself = $p.WaitForExit(180000)
	if (-not $closedItself) { $p.Kill(); $p.WaitForExit() }
	Assert-Smoke "ComicRack closed itself" $closedItself "the probe calls MainForm.MenuClose; pid $($p.Id) was killed only if this fails"
}
finally {
	Remove-Item Env:LO_SMOKE_ROOT, Env:LIBRARYORGANIZER_NET_DATA -ErrorAction SilentlyContinue
	Remove-Item -Recurse -Force $plugin, $probe -ErrorAction SilentlyContinue
	foreach ($root in $env:APPDATA, $env:LOCALAPPDATA) {
		Remove-Item -Recurse -Force (Join-Path $root "cYo\ComicRack Community Edition\Configurations\LoSmoke") -ErrorAction SilentlyContinue
	}
}

$text = if (Test-Path $report) { Get-Content $report -Raw } else { "" }
Assert-Smoke "probe ran without an exception" ($text -notmatch "(?m)^EXCEPTION") "no EXCEPTION line in the report"
Assert-Smoke "probe made its assertions" ($text -match "(?m)^ASSERT (PASS|FAIL) undo moved both files back:") "the report has the undo assertion"
[regex]::Matches($text, "(?m)^ASSERT FAIL (.+?):") | ForEach-Object { $failures += $_.Groups[1].Value }
$after = @(Get-UserData)
Assert-Smoke "user data unchanged" ($null -eq (Compare-Object $before $after)) "$($before.Count) entries before, $($after.Count) after, in %APPDATA%\LibraryOrganizer.NET"

Get-Content $report
Write-Output "--- report: $report"
if ($failures) { Write-Output ("SMOKE FAILED: " + (($failures | Select-Object -Unique) -join "; ")); exit 1 }
Write-Output "SMOKE PASSED"
