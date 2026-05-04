# CC-DESC: Exports a Context Control-ready project tree, optimized for large C++/Vulkan/Godot projects.
# ccDir.ps1
# First-step exporter.
# Run from your project root.
# Creates a filtered project directory tree + Context Control prompt and copies it to clipboard.

param(
    [string]$OutputFile = "cc_project_dir.md",
    [int]$MaxDepth = 20,
    [string]$Profile = "auto",
    [switch]$IncludeAllTopLevel
)

$ErrorActionPreference = "Stop"

$Fence3 = [string]::Concat(([char]96), ([char]96), ([char]96))

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

function Add-Line {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    [void]$script:OutputLines.Add($Text)
}

function Get-OutputText {
    $text = [string]::Join([Environment]::NewLine, [string[]]$script:OutputLines.ToArray())

    if (-not $text.EndsWith([Environment]::NewLine)) {
        $text += [Environment]::NewLine
    }

    return $text
}

function Save-OutputFile {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)

    if ([string]::IsNullOrWhiteSpace($dir)) {
        $dir = (Get-Location).Path
    }

    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    $leaf = [System.IO.Path]::GetFileName($fullPath)
    $tmpPath = Join-Path $dir (".$leaf.$PID.$([System.Guid]::NewGuid().ToString('N')).tmp")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText($tmpPath, (Get-OutputText), $utf8NoBom)

    $lastError = $null

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            if ([System.IO.File]::Exists($fullPath)) {
                try {
                    [System.IO.File]::Replace($tmpPath, $fullPath, $null, $true)
                }
                catch {
                    Copy-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
                    Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                Move-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
            }

            return
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds ([Math]::Min(1000, 50 * $attempt))
        }
    }

    throw "Failed to write '$Path' after retries. Close any editor/preview/indexer using it and try again. Temp output was left at: $tmpPath. Last error: $lastError"
}

function Detect-Profile {
    if ($Profile.ToLowerInvariant() -ne "auto") {
        return $Profile.ToLowerInvariant()
    }

    if ((Test-Path -LiteralPath "CMakeLists.txt") -and
        (Test-Path -LiteralPath "src") -and
        (Test-Path -LiteralPath "include") -and
        (Test-Path -LiteralPath "shaders")) {
        return "vulkanvx"
    }

    if ((Test-Path -LiteralPath "project.godot") -or (Test-Path -LiteralPath "scenes")) {
        return "godot"
    }

    return "generic"
}

$ResolvedProfile = Detect-Profile

function Is-ExcludedItem($Item) {
    if ($Item.Name -like "*.ccbak.*") {
        return $true
    }

    if ($Item.PSIsContainer) {
        return $ExcludeDirs -contains $Item.Name
    }

    $ext = [System.IO.Path]::GetExtension($Item.Name).ToLower()
    return $ExcludeFileExtensions -contains $ext
}

function Should-Include-TopLevelItem($Item) {
    if ($IncludeAllTopLevel) {
        return $true
    }

    if ($ResolvedProfile -ne "vulkanvx") {
        return $true
    }

    $root = (Get-Location).Path
    $relative = $Item.FullName.Substring($root.Length)
    $relative = $relative -replace '^[\\/]+', ''
    $relative = $relative -replace '\\', '/'

    if ($relative -eq "") {
        return $true
    }

    $top = ($relative -split '/')[0]

    foreach ($allowed in $VulkanVXTopLevelAllowList) {
        if ($relative -eq $allowed) {
            return $true
        }

        if (($allowed -notmatch '\.') -and ($top -eq $allowed)) {
            return $true
        }
    }

    return $false
}

function Add-Tree($Dir, $Prefix, $Depth) {
    if ($Depth -ge $MaxDepth) {
        Add-Line "$Prefix..."
        return
    }

    $children = Get-ChildItem -LiteralPath $Dir -Force |
        Where-Object { -not (Is-ExcludedItem $_) } |
        Where-Object { if ($Depth -eq 0) { Should-Include-TopLevelItem $_ } else { $true } } |
        Sort-Object @{ Expression = { -not $_.PSIsContainer } }, Name

    for ($i = 0; $i -lt $children.Count; $i++) {
        $item = $children[$i]
        $isLast = ($i -eq $children.Count - 1)

        if ($isLast) {
            $connector = "└── "
            $nextPrefix = $Prefix + "    "
        }
        else {
            $connector = "├── "
            $nextPrefix = $Prefix + "│   "
        }

        $name = $item.Name
        if ($item.PSIsContainer) {
            $name += "/"
        }

        Add-Line "$Prefix$connector$name"

        if ($item.PSIsContainer) {
            Add-Tree $item.FullName $nextPrefix ($Depth + 1)
        }
    }
}

$rootPath = (Get-Location).Path
$rootName = Split-Path -Leaf $rootPath

if ($rootName -eq "") {
    $rootName = "project"
}

Add-Line "# Project directory for Context Control"
Add-Line ""
Add-Line "Project root: $rootPath"
Add-Line "Detected profile: $ResolvedProfile"
Add-Line ""
Add-Line "## Prompt"
Add-Line ""
Add-Line "Use the standing Context Control agent instructions for the full workflow. This export is only the project map/navigation layer."
Add-Line ""
Add-Line "For the user's request, return only the smallest safe file/function list needed for cc.ps1. One path per line, relative to project root, ending with END."
Add-Line ""
Add-Line "File-list example:"
Add-Line ""
Add-Line "$Fence3`text"

if ($ResolvedProfile -eq "vulkanvx") {
    Add-Line "src/core/engine/Engine.cpp"
    Add-Line "include/core/engine/Engine.h"
    Add-Line "src/rendering/lighting/ShadowSystemUpdate.cpp"
    Add-Line "include/rendering/lighting/ShadowSystem.h"
    Add-Line "shaders/common/shadow_sampling.glsl"
}
elseif ($ResolvedProfile -eq "godot") {
    Add-Line "scenes/main.gd"
    Add-Line "scenes/main.tscn"
    Add-Line "scripts/core/player.gd"
    Add-Line "scripts/ui/hud.gd"
}
else {
    Add-Line "src/main.cpp"
    Add-Line "include/main.h"
}

Add-Line "END"
Add-Line "$Fence3"
Add-Line ""
Add-Line "Function examples accepted by upgraded cc.ps1:"
Add-Line ""
Add-Line "$Fence3`text"
Add-Line "FUNCTION src/world/World.cpp :: World::markTextureMaterialsDirty"
Add-Line "FUNCTION src/world/World*.cpp :: World::beginTerrainEditVisualTracking"
Add-Line "FUNC: beginTerrainEditVisualTracking"
Add-Line "SYMBOL: TerrainEditDiag"
Add-Line "END"
Add-Line "$Fence3"
Add-Line ""
Add-Line "Map-stage reminders:"
Add-Line "- Do not solve yet; request context only."
Add-Line "- Prefer narrow subsystem files/functions over giant exports."
Add-Line "- Function syntax must be exactly: FUNCTION src/path/File.cpp :: SymbolName."
Add-Line "- Wildcard FUNCTION paths are supported for split implementation families, e.g. FUNCTION src/world/World*.cpp :: World::foo, but prefer exact files when known."
Add-Line "- Use FUNC: SymbolName only when the owning file is unknown and you want cc.ps1 to search and extract function bodies."
Add-Line "- Use SYMBOL: SymbolName only for non-function declarations/types/constants because it exports whole matching files."
Add-Line "- cc.ps1 now auto-adds matching headers for .cpp files and direct GLSL #include files, so do not list obvious duplicates unless the specific header/include is central to the change."
Add-Line "- Include CMakeLists.txt only when adding/removing C++ source files or build config."
Add-Line "- Never request build/, vcpkg_installed/, vendor/external/third_party/, generated caches, binaries, or unrelated modules."
Add-Line ""

Add-Line "## Project tree"
Add-Line ""
Add-Line "$Fence3`text"
Add-Line "$rootName/"
Add-Tree $rootPath "" 0
Add-Line "$Fence3"

Save-OutputFile $OutputFile

Write-Host ""
Write-Host "Done."
Write-Host "Created: $OutputFile"
Write-Host "Detected profile: $ResolvedProfile"

try {
    Get-OutputText | Set-Clipboard
    Write-Host "Copied project tree + prompt to clipboard."
}
catch {
    Write-Host "Clipboard copy skipped."
}
