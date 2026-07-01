# CC-DESC: Thin launcher for the modular Context Control project tree exporter.

[CmdletBinding()]
param(
    [string]$OutputFile = "cc_project_dir.md",
    [int]$MaxDepth = 20,
    [string]$Profile = "auto",
    [int]$Lod = 0,
    [string]$Scope = "",
    [switch]$ProfileOnly,
    [string]$ProfileOutput = "",
    [string]$ProfileFile = "",
    [switch]$IncludeArtifacts,
    [switch]$IncludeAllTopLevel,
    [switch]$NoClipboard
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "lib/Cc.Dir.Export.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Context Control directory exporter module was not found: $scriptPath"
}

& $scriptPath @PSBoundParameters
