# CC-DESC: Exports a Context Control-ready project tree for source-navigation turns.
# ccDir.ps1
# First-step exporter. Run from your project root.
# Creates a filtered project directory tree + small navigation prompt and copies it to clipboard.

param(
    [string]$OutputFile = "cc_project_dir.md",
    [int]$MaxDepth = 20,
    [string]$Profile = "auto",
    [switch]$IncludeHiddenTopLevel
)

$ErrorActionPreference = "Stop"

$Fence3 = [string]::Concat(([char]96), ([char]96), ([char]96))

$ExcludeDirs = @(
    ".git",
    ".vs",
    ".vscode",
    ".idea",
    ".cache",
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

function Add-Line {
    param([string]$Text)
    Add-Content -LiteralPath $OutputFile -Value $Text -Encoding UTF8
}

function Detect-Profile {
    if ($Profile.ToLowerInvariant() -ne "auto") {
        return $Profile.ToLowerInvariant()
    }

    if ((Test-Path -LiteralPath "CMakeLists.txt") -and
        ((Test-Path -LiteralPath "src") -or (Test-Path -LiteralPath "include"))) {
        return "cmake-cpp"
    }

    if ((Test-Path -LiteralPath "package.json") -or (Test-Path -LiteralPath "tsconfig.json")) {
        return "web"
    }

    if ((Test-Path -LiteralPath "pyproject.toml") -or (Test-Path -LiteralPath "requirements.txt")) {
        return "python"
    }

    return "generic"
}

$ResolvedProfile = Detect-Profile

function Is-ExcludedItem {
    param($Item)

    if ($Item.Name -like "*.ccbak.*") {
        return $true
    }

    if (-not $IncludeHiddenTopLevel -and $Item.Name.StartsWith(".")) {
        if ($Item.Name -ne ".github") {
            return $true
        }
    }

    if ($Item.PSIsContainer) {
        return $ExcludeDirs -contains $Item.Name
    }

    $name = $Item.Name.ToLowerInvariant()
    if ($name -like "cc_code_export*.md" -or $name -like "cc_project_dir*.md") {
        return $true
    }

    $ext = [System.IO.Path]::GetExtension($Item.Name).ToLowerInvariant()
    return $ExcludeFileExtensions -contains $ext
}

function Add-Tree {
    param(
        [string]$Dir,
        [string]$Prefix,
        [int]$Depth
    )

    if ($Depth -ge $MaxDepth) {
        Add-Line "$Prefix..."
        return
    }

    $children = Get-ChildItem -LiteralPath $Dir -Force |
        Where-Object { -not (Is-ExcludedItem $_) } |
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

if (Test-Path -LiteralPath $OutputFile) {
    Remove-Item -LiteralPath $OutputFile
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
Add-Line "Use the standing Context Control instructions for the full workflow. This export is only the project map/navigation layer."
Add-Line ""
Add-Line "For the user's request, return only the smallest safe file/function list needed for cc.ps1. One path per line, relative to project root, ending with END."
Add-Line ""
Add-Line "File-list example:"
Add-Line ""
Add-Line "$Fence3`text"

switch ($ResolvedProfile) {
    "cmake-cpp" {
        Add-Line "src/main.cpp"
        Add-Line "include/main.h"
        Add-Line "CMakeLists.txt"
    }
    "web" {
        Add-Line "src/main.ts"
        Add-Line "src/App.tsx"
        Add-Line "package.json"
    }
    "python" {
        Add-Line "src/main.py"
        Add-Line "pyproject.toml"
    }
    default {
        Add-Line "src/main.cpp"
        Add-Line "include/main.h"
    }
}

Add-Line "END"
Add-Line "$Fence3"
Add-Line ""
Add-Line "Function examples accepted by cc.ps1:"
Add-Line ""
Add-Line "$Fence3`text"
Add-Line "FUNCTION src/path/File.cpp :: Namespace::functionName"
Add-Line "FUNCTION src/path/File*.cpp :: ClassName::methodName"
Add-Line "FUNC: functionName"
Add-Line "SYMBOL: ImportantTypeOrConstant"
Add-Line "END"
Add-Line "$Fence3"
Add-Line ""
Add-Line "Map-stage reminders:"
Add-Line "- Do not solve yet; request context only."
Add-Line "- Prefer narrow subsystem files/functions over giant exports."
Add-Line "- Function syntax must be exactly: FUNCTION src/path/File.cpp :: SymbolName."
Add-Line "- Wildcard FUNCTION paths are supported for split implementation families, but prefer exact files when known."
Add-Line "- Use FUNC: SymbolName only when the owning file is unknown and cc.ps1 should search and extract function bodies."
Add-Line "- Use SYMBOL: SymbolName only for non-function declarations/types/constants because it exports whole matching files."
Add-Line "- cc.ps1 auto-adds matching headers for .cpp files and direct GLSL #include files, so do not list obvious duplicates unless the specific header/include is central to the change."
Add-Line "- Include build files only when adding/removing compiled sources or changing build configuration."
Add-Line "- Never request build/, dependency folders, generated caches, binaries, or unrelated modules."
Add-Line ""
Add-Line "## Project tree"
Add-Line ""
Add-Line "$Fence3`text"
Add-Line "$rootName/"
Add-Tree $rootPath "" 0
Add-Line "$Fence3"

Write-Host ""
Write-Host "Done."
Write-Host "Created: $OutputFile"
Write-Host "Detected profile: $ResolvedProfile"

try {
    Get-Content -LiteralPath $OutputFile -Raw | Set-Clipboard
    Write-Host "Copied project tree + prompt to clipboard."
}
catch {
    Write-Host "Clipboard copy skipped."
}
