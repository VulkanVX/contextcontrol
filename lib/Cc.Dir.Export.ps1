# CC-DESC: Exports a Context Control-ready project tree, optimized for large C++/Vulkan/Godot projects.
# ccDir.ps1
# First-step exporter.
# Run from your project root.
# Creates a filtered project directory tree + Context Control prompt and copies it to clipboard.

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

$ExcludeDirs = @(
    ".git",
    ".vs",
    ".vscode",
    ".idea",
    ".cache",
    ".godot",
    ".import",
    "node_modules",
    "dist",
    "build",
    "build-debug",
    "build-release",
    "cmake-build-debug",
    "cmake-build-release",
    "CMakeFiles",
    "out",
    "bin",
    "obj",
    "x64",
    "Debug",
    "Release",
    "RelWithDebInfo",
    "MinSizeRel",
    "vcpkg_installed",
    "packages",
    "PackageCache",
    "external",
    "extern",
    "third_party",
    "thirdparty",
    "vendor",
    "deps",
    "dependencies",
    "__pycache__"
)

$ExcludeFileExtensions = @(
    ".import",
    ".uid",
    ".tmp",
    ".log",
    ".bak",
    ".pdb",
    ".ilk",
    ".obj",
    ".o",
    ".lib",
    ".dll",
    ".exe",
    ".exp",
    ".spv",
    ".cache",
    ".db",
    ".opendb",
    ".sdf",
    ".ipch",
    ".tlog",
    ".lastbuildstate",
    ".unsuccessfulbuild",
    ".png",
    ".jpg",
    ".jpeg",
    ".webp",
    ".bmp",
    ".tga",
    ".dds",
    ".wav",
    ".mp3",
    ".ogg",
    ".flac",
    ".bin",
    ".collision",
    ".svo"
)

$ExcludeFileNames = @()

$ArtifactExcludeDirs = @(
    ".contextcontrol",
    ".ccReplace.versions",
    ".ccWorkbench.browser-data",
    ".ccWorkbench.generated-projects",
    ".gptReplace.versions",
    ".tmp",
    ".tmp-build",
    "codex-harness",
    "dotnet-obj",
    "dotnet-out",
    "generated",
    "ps1_nat",
    "ps1_nat_gist_review",
    "test-harness",
    "test-results"
)

$ArtifactExcludeFileNames = @(
    ".ccWorkbench.chat-history*.json",
    "cc_chat_export_*.md",
    "cc_code_export.md",
    ".ccDirProfile.json",
    "cc_project_dir.md",
    "cc_semantic_map.md",
    "cleanup-*.ps1",
    "mainwindow_axaml.txt",
    "nat-*.ps1",
    "*-nat-*.ps1",
    "*.generated.json",
    "*.generated.md",
    "*.nat.*",
    "*.tmp.md",
    "*.tmp.txt",
    "*_nat_*",
    "*bug-hunt*",
    "*bughunt*",
    "patch.txt"
)

$VulkanVXTopLevelAllowList = @(
    "include",
    "src",
    "shaders",
    "tools",
    "maps",
    "assets",
    "CMakeLists.txt",
    "README.md"
)

$script:OutputLines = New-Object System.Collections.Generic.List[string]
$script:CcDirProjectHints = $null
$script:CcDirRootPath = ""
$script:CcDirRelativePathCache = @{}
$script:CcDirArtifactPathCache = @{}
$script:CcDirPathDepthCache = @{}
$script:CcDirStableAnchorRootCache = @{}
$script:CcDirRoutingCategoryCache = @{}
$script:CcDirAnchorCategoryCache = @{}
$script:CcDirAnchorScoreCache = @{}
$script:CcDirRoleCache = @{}
$script:CcDirRootRoleCache = @{}

$script:CcToolRoot = if ((Split-Path -Leaf $PSScriptRoot) -ieq "lib") { Split-Path -Parent $PSScriptRoot } else { $PSScriptRoot }
$script:CcDirModuleRoot = Join-Path $PSScriptRoot "dir"
. (Join-Path $script:CcDirModuleRoot "Cc.Dir.Shared.ps1")
$script:CcSharedSettings = Read-CcSharedSettings
$script:CcProjectRoot = Resolve-CcSharedProjectRoot $script:CcSharedSettings
$script:CcOutputRoot = Resolve-CcSharedOutputRoot $script:CcSharedSettings

if (-not (Test-Path -LiteralPath $script:CcProjectRoot)) {
    throw "Configured ProjectRoot does not exist: $script:CcProjectRoot. Change it in contextcontrol/.ccReplace.settings.json or ccReplace Settings option 11."
}

if (-not (Test-Path -LiteralPath $script:CcOutputRoot)) {
    New-Item -ItemType Directory -Path $script:CcOutputRoot | Out-Null
}

# Project-tree scanning is relative to the configured project root.
# The markdown export is written to the configured Context Control output folder.
Set-Location -LiteralPath $script:CcProjectRoot
$script:CcDirRootPath = (Get-Location).Path
$script:CcDirRelativePathCache = @{}
$script:CcDirArtifactPathCache = @{}
$script:CcDirPathDepthCache = @{}
$script:CcDirStableAnchorRootCache = @{}
$script:CcDirRoutingCategoryCache = @{}
$script:CcDirAnchorCategoryCache = @{}
$script:CcDirAnchorScoreCache = @{}
$script:CcDirRoleCache = @{}
$script:CcDirRootRoleCache = @{}

Write-Host "Project root: $script:CcProjectRoot" -ForegroundColor DarkGray
Write-Host "Output folder: $script:CcOutputRoot" -ForegroundColor DarkGray
Add-CcDirDefaultArtifactRules
Import-CcDirWorkbenchFileRules
$script:CcDirProjectHints = Read-CcDirOptionalProjectHints


$ResolvedProfile = Detect-Profile

. (Join-Path $script:CcDirModuleRoot "Cc.Dir.Classification.ps1")
. (Join-Path $script:CcDirModuleRoot "Cc.Dir.Selection.ps1")
. (Join-Path $script:CcDirModuleRoot "Cc.Dir.RootSelection.ps1")
. (Join-Path $script:CcDirModuleRoot "Cc.Dir.Manifest.ps1")
$scopeInfo = Resolve-CcDirScope
$allFiles = @(Get-CcDirIncludedFiles $scopeInfo.FullPath)
$profilePath = Get-CcDirProfilePath
$projectProfile = if (-not $scopeInfo.Scoped) { Read-CcDirProjectProfile $profilePath } else { $null }
$rebuiltProjectProfile = $false
if ($null -ne $projectProfile -and -not (Test-CcDirProjectProfileFresh $projectProfile $allFiles)) {
    Write-Host "DIR profile cache is stale; rebuilding: $profilePath" -ForegroundColor DarkGray
    $projectProfile = $null
}

if ($null -eq $projectProfile -and -not $scopeInfo.Scoped) {
    $projectProfile = New-CcDirProjectProfile $allFiles
    $rebuiltProjectProfile = $true
}

if ($rebuiltProjectProfile -and -not [string]::IsNullOrWhiteSpace($profilePath)) {
    [void](Save-CcDirProjectProfile $projectProfile $profilePath)
}

if ($ProfileOnly) {
    if ($null -eq $projectProfile) {
        $projectProfile = New-CcDirProjectProfile $allFiles
    }

    $savedProfilePath = Save-CcDirProjectProfile $projectProfile $ProfileOutput
    Write-Host "DIR profile created: $savedProfilePath"
    return
}

$visibleFiles = @(Get-CcDirVisibleFiles $scopeInfo $allFiles $projectProfile)
Add-CcDirManifest $scopeInfo $allFiles $visibleFiles $projectProfile

$SavedOutputFile = Save-OutputFile $OutputFile

Write-Host ""
Write-Host "Done."
Write-Host "Created: $SavedOutputFile"
Write-Host "Detected profile: $ResolvedProfile"
Write-Host "DIR LOD: $($scopeInfo.Label)"
if ($scopeInfo.Scoped) {
    Write-Host "Scope: $($scopeInfo.Relative)"
}
Write-Host "Open it: code `"$SavedOutputFile`""
Write-Host "Fallback copy: Get-Content -LiteralPath `"$SavedOutputFile`" -Raw | Set-Clipboard"

if (-not $NoClipboard) {
    $exportText = Get-OutputText
    try {
        Set-Clipboard -Value $exportText -ErrorAction Stop
        Write-Host "Copied DIR manifest to clipboard."
    }
    catch {
        $setClipboardError = $_.Exception.Message
        try {
            $clipExe = Join-Path $env:SystemRoot "System32\clip.exe"
            if (-not (Test-Path -LiteralPath $clipExe)) {
                throw "clip.exe not found; Set-Clipboard failed: $setClipboardError"
            }

            $exportText | & $clipExe
            Write-Host "Copied DIR manifest to clipboard via clip.exe."
        }
        catch {
            Write-Host "Clipboard copy skipped: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host "Clipboard copy disabled by -NoClipboard."
}
