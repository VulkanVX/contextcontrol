# CC-DESC: Shared Context Control settings, project file-rule config, and path resolution helpers.

function New-CcSharedDefaultSettings {
    return [pscustomobject]@{
        # "auto" detects the project root from the Context Control tool location.
        # Default layout: <project-root>/contextcontrol/, so auto selects the parent
        # project. Explicit paths are respected exactly; use "." to make the
        # Context Control tool folder itself the project root.
        ProjectRoot = "auto"
        OutputRoot = "."

        # These are shared defaults for tools that need to decide whether a file is
        # useful source context or should be treated as generated/cache/snapshot data.
        # The IDE persists the editable project copy to .ccFileRules.json.
        SupportedFileExtensions = @(
            ".axaml", ".bat", ".c", ".cc", ".cmd", ".comp", ".cpp", ".cs", ".csproj",
            ".css", ".cxx", ".fc", ".frag", ".fs", ".fsproj", ".geom", ".glsl", ".h", ".hh",
            ".hpp", ".go", ".gradle", ".html", ".hlsl", ".hxx", ".inc", ".ini", ".inl", ".ipp",
            ".java", ".js", ".json", ".jsx", ".kt", ".kts", ".lock", ".lua", ".m", ".md",
            ".mesh", ".metal", ".mm", ".mod", ".props", ".ps1", ".psd1", ".psm1", ".py", ".rs",
            ".shader", ".sh", ".slang", ".sum", ".targets", ".task", ".tesc", ".tese", ".toml",
            ".ts", ".tsx", ".txt", ".vert", ".wgsl", ".xaml", ".xml", ".yaml", ".yml"
        )
        IgnoredFileExtensions = @(
            ".bak", ".bin", ".bmp", ".cache", ".collision", ".db", ".dds", ".dll", ".exe",
            ".exp", ".flac", ".ilk", ".import", ".ipch", ".jpg", ".jpeg", ".lastbuildstate",
            ".lib", ".log", ".mp3", ".o", ".obj", ".ogg", ".opendb", ".pdb", ".png",
            ".sdf", ".snapshot", ".spv", ".svo", ".tga", ".tlog", ".tmp", ".uid",
            ".unsuccessfulbuild", ".wav", ".webp"
        )
        IgnoredDirectories = @(
            ".ccReplace.versions", ".git", ".idea", ".vs", ".vscode", "bin", "build",
            "build-debug", "build-release", "cmake-build-debug", "cmake-build-release",
            "CMakeFiles", "Debug", "dist", "external", "extern", "node_modules", "obj", "out",
            "packages", "Release", "third_party", "thirdparty", "vendor", "vcpkg_installed", "x64"
        )
        IgnoredFiles = @(
            ".ccDirProfile.json", ".ccFileRules.json", ".ccReplace.settings*.json",
            ".ccWorkbench.chat-history*.json", ".ccWorkbench.settings.json",
            ".DS_Store", "cc_chat_export_*.md", "cc_code_export*.md",
            "cc_project_dir.md", "cc_semantic_map.md", "desktop.ini",
            "patch.txt", "Thumbs.db"
        )
    }
}

function Get-CcSharedScriptDirectory {
    if (-not [string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
        return $script:CcToolRoot
    }

    $candidateRoot = if ($PSScriptRoot -ne "") { $PSScriptRoot } else { (Get-Location).Path }

    if ((Split-Path -Leaf $candidateRoot) -ieq "lib") {
        return (Split-Path -Parent $candidateRoot)
    }

    return $candidateRoot
}

function Get-CcSharedSettingsPath {
    return (Join-Path (Get-CcSharedScriptDirectory) ".ccReplace.settings.json")
}

function Normalize-CcSharedSettingPathText {
    param(
        [string]$PathText,
        [string]$Fallback = ""
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return $Fallback
    }

    $clean = [string]$PathText
    $clean = $clean -replace "`0", ""

    $parts = @($clean -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
    if ($parts.Count -gt 0) {
        $clean = $parts[$parts.Count - 1]
    }

    $clean = $clean.Trim().Trim('"')

    for ($i = 0; $i -lt 4; $i++) {
        $next = $clean

        if ($next -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder)\s*:\s*(.+)$') {
            $next = $Matches[1].Trim()
        }
        elseif ($next -match '^\s*(?:project\s+root|output\s+folder)\s*=\s*(.+)$') {
            $next = $Matches[1].Trim()
        }

        $next = $next.Trim().Trim('"')
        if ($next -eq $clean) {
            break
        }

        $clean = $next
    }

    if ([string]::IsNullOrWhiteSpace($clean)) {
        return $Fallback
    }

    if ($clean -match '^\s*(?:Resolved\s+)?(?:project\s+root|output\s+folder)\s*:?\s*$') {
        return $Fallback
    }

    return $clean
}

function Normalize-CcSharedExtensionList {
    param($Values)

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($value in @($Values)) {
        if ([string]::IsNullOrWhiteSpace([string]$value)) {
            continue
        }

        $clean = ([string]$value).Trim().ToLowerInvariant()
        if ($clean -notmatch '^\.') {
            $clean = ".${clean}"
        }

        [void]$set.Add($clean)
    }

    return @($set)
}

function Normalize-CcSharedNameList {
    param($Values)

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($value in @($Values)) {
        if ([string]::IsNullOrWhiteSpace([string]$value)) {
            continue
        }

        [void]$set.Add(([string]$value).Trim())
    }

    return @($set)
}

function Repair-CcSharedSettingsInPlace {
    param($Settings)

    if ($null -eq $Settings) {
        return $false
    }

    $changed = $false

    $projectRoot = Normalize-CcSharedSettingPathText ([string]$Settings.ProjectRoot) "auto"
    if ([string]::IsNullOrWhiteSpace($projectRoot)) { $projectRoot = "auto" }
    if ([string]$Settings.ProjectRoot -ne $projectRoot) {
        $Settings.ProjectRoot = $projectRoot
        $changed = $true
    }

    $outputRoot = Normalize-CcSharedSettingPathText ([string]$Settings.OutputRoot) "."
    if ([string]::IsNullOrWhiteSpace($outputRoot)) { $outputRoot = "." }
    if ([string]$Settings.OutputRoot -ne $outputRoot) {
        $Settings.OutputRoot = $outputRoot
        $changed = $true
    }

    if ($Settings.PSObject.Properties.Name -contains "SupportedFileExtensions") {
        $normalized = Normalize-CcSharedExtensionList $Settings.SupportedFileExtensions
        if ((@($Settings.SupportedFileExtensions) -join "|") -ne ($normalized -join "|")) {
            $Settings.SupportedFileExtensions = $normalized
            $changed = $true
        }
    }

    if ($Settings.PSObject.Properties.Name -contains "IgnoredFileExtensions") {
        $normalized = Normalize-CcSharedExtensionList $Settings.IgnoredFileExtensions
        if ((@($Settings.IgnoredFileExtensions) -join "|") -ne ($normalized -join "|")) {
            $Settings.IgnoredFileExtensions = $normalized
            $changed = $true
        }
    }

    if ($Settings.PSObject.Properties.Name -contains "IgnoredDirectories") {
        $normalized = Normalize-CcSharedNameList $Settings.IgnoredDirectories
        if ((@($Settings.IgnoredDirectories) -join "|") -ne ($normalized -join "|")) {
            $Settings.IgnoredDirectories = $normalized
            $changed = $true
        }
    }

    if ($Settings.PSObject.Properties.Name -contains "IgnoredFiles") {
        $normalized = Normalize-CcSharedNameList $Settings.IgnoredFiles
        if ((@($Settings.IgnoredFiles) -join "|") -ne ($normalized -join "|")) {
            $Settings.IgnoredFiles = $normalized
            $changed = $true
        }
    }

    return $changed
}

function Merge-CcSharedSettings {
    param($Loaded)

    $settings = New-CcSharedDefaultSettings

    if ($null -eq $Loaded) {
        return $settings
    }

    foreach ($prop in $settings.PSObject.Properties.Name) {
        if ($Loaded.PSObject.Properties.Name -contains $prop) {
            $settings.$prop = $Loaded.$prop
        }
    }

    [void](Repair-CcSharedSettingsInPlace $settings)
    return $settings
}

function Read-CcSharedSettings {
    $path = Get-CcSharedSettingsPath

    if (-not (Test-Path -LiteralPath $path)) {
        return New-CcSharedDefaultSettings
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ([string]::IsNullOrWhiteSpace($json)) {
            return New-CcSharedDefaultSettings
        }

        return Merge-CcSharedSettings ($json | ConvertFrom-Json)
    }
    catch {
        Write-Host "Context Control settings could not be read, using defaults: $($_.Exception.Message)" -ForegroundColor Yellow
        return New-CcSharedDefaultSettings
    }
}

function Resolve-CcSharedPathRelativeToScript {
    param([string]$PathText)

    $clean = Normalize-CcSharedSettingPathText $PathText ""
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return (Get-CcSharedScriptDirectory)
    }

    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-CcSharedScriptDirectory) $clean))
}

function Resolve-CcSharedProjectRoot {
    param($Settings)

    if (-not [string]::IsNullOrWhiteSpace($env:CC_WORKBENCH_PROJECT_ROOT)) {
        return [System.IO.Path]::GetFullPath($env:CC_WORKBENCH_PROJECT_ROOT)
    }

    $root = "auto"
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "ProjectRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.ProjectRoot)) {
        $root = [string]$Settings.ProjectRoot
    }

    $clean = Normalize-CcSharedSettingPathText $root "auto"

    if ([string]::IsNullOrWhiteSpace($clean) -or $clean -ieq "auto") {
        $toolRoot = Get-CcSharedScriptDirectory
        $leaf = Split-Path -Leaf $toolRoot

        if ($leaf -ieq "contextcontrol") {
            $parent = Split-Path -Parent $toolRoot
            if (-not [string]::IsNullOrWhiteSpace($parent)) {
                return [System.IO.Path]::GetFullPath($parent)
            }
        }

        return [System.IO.Path]::GetFullPath($toolRoot)
    }

    # Explicit user paths must win. In particular, do not auto-promote
    # D:\...\contextcontrol or "." to the parent repo; those are valid when
    # editing Context Control itself.
    return Resolve-CcSharedPathRelativeToScript $clean
}

function Resolve-CcSharedOutputRoot {
    param($Settings)

    $root = "."
    if ($null -ne $Settings -and
        ($Settings.PSObject.Properties.Name -contains "OutputRoot") -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.OutputRoot)) {
        $root = [string]$Settings.OutputRoot
    }

    return Resolve-CcSharedPathRelativeToScript $root
}

function Resolve-CcSharedOutputPath {
    param(
        [string]$PathText,
        $Settings = $null
    )

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        throw "Missing output path."
    }

    $clean = Normalize-CcSharedSettingPathText $PathText ""
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcSharedSettings
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-CcSharedOutputRoot $Settings) $clean))
}

function Get-CcProjectFileRulesPath {
    param($Settings = $null)

    if (-not [string]::IsNullOrWhiteSpace($env:CC_WORKBENCH_FILE_RULES_PATH)) {
        return [System.IO.Path]::GetFullPath($env:CC_WORKBENCH_FILE_RULES_PATH)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcSharedSettings
    }

    $projectRoot = Resolve-CcSharedProjectRoot $Settings
    $contextControlRoot = if ((Split-Path -Leaf $projectRoot) -ieq "contextcontrol") {
        $projectRoot
    }
    else {
        $nested = Join-Path $projectRoot "contextcontrol"
        if (Test-Path -LiteralPath (Join-Path $nested "ccStart.ps1")) { $nested } else { $projectRoot }
    }

    return (Join-Path $contextControlRoot ".ccFileRules.json")
}

function New-CcDefaultProjectFileRules {
    $settings = New-CcSharedDefaultSettings
    return [pscustomobject]@{
        IgnoredDirectories = @($settings.IgnoredDirectories)
        IgnoredExtensions = @($settings.IgnoredFileExtensions)
        SupportedExtensions = @($settings.SupportedFileExtensions)
        IgnoredFiles = @($settings.IgnoredFiles)
    }
}

function Read-CcProjectFileRules {
    param($Settings = $null)

    $defaults = New-CcDefaultProjectFileRules
    $path = Get-CcProjectFileRulesPath $Settings
    if (-not (Test-Path -LiteralPath $path)) {
        return $defaults
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ([string]::IsNullOrWhiteSpace($json)) {
            return $defaults
        }

        $loaded = $json | ConvertFrom-Json
        foreach ($prop in $defaults.PSObject.Properties.Name) {
            if ($loaded.PSObject.Properties.Name -contains $prop) {
                $defaults.$prop = $loaded.$prop
            }
        }

        if (($loaded.PSObject.Properties.Name -contains "IgnoredFileNames") -and
            ((-not ($loaded.PSObject.Properties.Name -contains "IgnoredFiles")) -or $null -eq $loaded.IgnoredFiles)) {
            $defaults.IgnoredFiles = $loaded.IgnoredFileNames
        }

        $defaults.IgnoredDirectories = Normalize-CcSharedNameList $defaults.IgnoredDirectories
        $defaults.IgnoredExtensions = Normalize-CcSharedExtensionList $defaults.IgnoredExtensions
        $defaults.SupportedExtensions = Normalize-CcSharedExtensionList $defaults.SupportedExtensions
        $defaults.IgnoredFiles = Normalize-CcSharedNameList $defaults.IgnoredFiles
        return $defaults
    }
    catch {
        Write-Host "Context Control file rules could not be read, using defaults: $($_.Exception.Message)" -ForegroundColor Yellow
        return $defaults
    }
}

function Save-CcProjectFileRules {
    param(
        [Parameter(Mandatory = $true)]$Rules,
        $Settings = $null
    )

    $path = Get-CcProjectFileRulesPath $Settings
    $parent = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $ignoredFiles = Normalize-CcSharedNameList $Rules.IgnoredFiles
    $normalized = [pscustomobject]@{
        IgnoredDirectories = Normalize-CcSharedNameList $Rules.IgnoredDirectories
        IgnoredExtensions = Normalize-CcSharedExtensionList $Rules.IgnoredExtensions
        SupportedExtensions = Normalize-CcSharedExtensionList $Rules.SupportedExtensions
        IgnoredFiles = $ignoredFiles
        IgnoredFileNames = $ignoredFiles
    }

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, (($normalized | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $utf8NoBom)
    return $path
}

function Get-CcProjectFileRulesSummary {
    param($Rules = $null)

    if ($null -eq $Rules) {
        $Rules = Read-CcProjectFileRules
    }

    return [pscustomobject]@{
        Supported = ((Normalize-CcSharedExtensionList $Rules.SupportedExtensions) -join ", ")
        Ignored = ((Normalize-CcSharedExtensionList $Rules.IgnoredExtensions) -join ", ")
        IgnoredDirectories = ((Normalize-CcSharedNameList $Rules.IgnoredDirectories) -join ", ")
        IgnoredFiles = ((Normalize-CcSharedNameList $Rules.IgnoredFiles) -join ", ")
    }
}
