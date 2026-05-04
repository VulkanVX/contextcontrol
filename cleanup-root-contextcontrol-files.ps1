# CC-DESC: Removes old root-level Context Control files after migrating the tool into contextcontrol/.
# Run from the project root after the contextcontrol/ folder has been created and verified.

$ErrorActionPreference = "Stop"

$scriptDir = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDir ".."))

$oldRootFiles = @(
    "cc.ps1",
    "ccDir.ps1",
    "ccReplace.ps1",
    "ccStart.ps1",
    "cc",
    "cc.cmd",
    "ccDir",
    "ccDir.cmd",
    "ccReplace",
    "ccReplace.cmd",
    "ccStart",
    "ccStart.cmd",
    "README-Context-Control-Run.md"
)

foreach ($name in $oldRootFiles) {
    $path = Join-Path $projectRoot $name
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
        Write-Host "Removed root Context Control file: $name" -ForegroundColor DarkGray
    }
}

Write-Host "Root cleanup complete. Use contextcontrol/ccStart.cmd on Windows or sh ./contextcontrol/ccStart on macOS/Linux." -ForegroundColor Green
