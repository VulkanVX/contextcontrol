# CC-DESC: Source-export configuration, path filtering, display-path, and input-line helpers.

# Files that may be explicitly exported if the user asks for them.
$script:TextExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx", ".ipp", ".tpp",
    ".cmake", ".txt", ".md", ".json", ".ini", ".cfg", ".fc", ".toml", ".yaml", ".yml",
    ".tl", ".tlb", ".td",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".axaml", ".xaml", ".xml",
    ".cs", ".csproj", ".fs", ".fsproj", ".vb", ".vbproj",
    ".props", ".targets", ".sln",
    ".ts", ".tsx", ".js", ".jsx", ".html", ".css",
    ".py", ".rs", ".sh", ".bash", ".zsh", ".lua", ".metal", ".slang", ".wgsl"
)

# Files searched by FUNC:/FIND:.
# Intentional: excludes .md and normal .txt so generated Context Control exports do not recursively match.
$script:CodeSearchExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx", ".ipp", ".tpp",
    ".cmake",
    ".tl", ".tlb", ".td",
    ".json", ".ini", ".cfg", ".fc", ".toml", ".yaml", ".yml",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".axaml", ".xaml", ".xml",
    ".cs", ".csproj", ".fs", ".fsproj", ".vb", ".vbproj",
    ".props", ".targets", ".sln",
    ".ts", ".tsx", ".js", ".jsx", ".html", ".css",
    ".py", ".rs", ".sh", ".bash", ".zsh", ".lua", ".metal", ".slang", ".wgsl"
)

$script:ExcludeDirs = @(
    ".git", ".vs", ".vscode", ".idea", ".cache",
    ".project", ".import",
    "node_modules", "dist",
    "build", "build-debug", "build-release",
    "cmake-build-debug", "cmake-build-release",
    "CMakeFiles", "out", "bin", "obj", "x64",
    "Debug", "Release", "RelWithDebInfo", "MinSizeRel",
    "vcpkg_installed", "packages", "PackageCache",
    "external", "extern", "third_party", "third-party", "thirdparty", "vendor", "deps", "dependencies",
    "contextcontrol",
    "__pycache__"
)

$script:BinaryExtensions = @(
    ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tga", ".dds",
    ".wav", ".mp3", ".ogg", ".flac",
    ".bin", ".collision", ".svo", ".spv",
    ".pdb", ".ilk", ".obj", ".o", ".lib", ".dll", ".exe", ".exp",
    ".db", ".opendb", ".sdf", ".ipch"
)

$script:IgnoredFiles = @(
    ".ccDirProfile.json",
    ".ccFileRules.json",
    ".ccReplace.settings*.json",
    ".ccWorkbench.chat-history*.json",
    ".ccWorkbench.settings.json",
    "cc_chat_export_*.md",
    "cc_code_export*.md",
    "cc_project_dir.md",
    "cc_semantic_map.md",
    "patch.txt"
)
$script:DefaultTextExtensions = @($script:TextExtensions)
$script:DefaultCodeSearchExtensions = @($script:CodeSearchExtensions)
$script:DefaultExcludeDirs = @($script:ExcludeDirs)
$script:DefaultIgnoredFiles = @($script:IgnoredFiles)
$script:SearchSupportedExtensions = @($script:CodeSearchExtensions)
$script:SearchIgnoredExtensions = @()
$script:TextExtensionSet = $null
$script:SearchSupportedExtensionSet = $null
$script:SearchIgnoredExtensionSet = $null
$script:BinaryExtensionSet = $null
$script:ExcludeDirSet = $null

if ($null -eq $script:ExportedFileKeys) {
    $script:ExportedFileKeys = @{}
}

function New-CcExportStringSet {
    param($Values)

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($value in @($Values)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$value)) {
            [void]$set.Add([string]$value)
        }
    }

    return $set
}

function Normalize-CcExportExtensionList {
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

function Normalize-CcExportNameList {
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

function Set-CcExportFilterRules {
    param(
        $Settings = $null,
        $Rules = $null
    )

    $ignoreDirs = New-Object System.Collections.Generic.List[string]
    $ignoreFiles = New-Object System.Collections.Generic.List[string]
    $textSupported = New-Object System.Collections.Generic.List[string]
    $searchSupported = New-Object System.Collections.Generic.List[string]
    $searchIgnored = New-Object System.Collections.Generic.List[string]

    foreach ($value in @($script:DefaultExcludeDirs)) { [void]$ignoreDirs.Add([string]$value) }
    foreach ($value in @($script:DefaultIgnoredFiles)) { [void]$ignoreFiles.Add([string]$value) }
    foreach ($value in @($script:DefaultTextExtensions)) { [void]$textSupported.Add([string]$value) }
    foreach ($value in @($script:DefaultCodeSearchExtensions)) { [void]$searchSupported.Add([string]$value) }

    if ($null -ne $Settings) {
        if ($Settings.PSObject.Properties.Name -contains "SupportedFileExtensions") {
            foreach ($value in @($Settings.SupportedFileExtensions)) {
                [void]$textSupported.Add([string]$value)
                [void]$searchSupported.Add([string]$value)
            }
        }
        if ($Settings.PSObject.Properties.Name -contains "IgnoredDirectories") {
            foreach ($value in @($Settings.IgnoredDirectories)) { [void]$ignoreDirs.Add([string]$value) }
        }
        if ($Settings.PSObject.Properties.Name -contains "IgnoredFiles") {
            foreach ($value in @($Settings.IgnoredFiles)) { [void]$ignoreFiles.Add([string]$value) }
        }
        if ($Settings.PSObject.Properties.Name -contains "IgnoredFileExtensions") {
            foreach ($value in @($Settings.IgnoredFileExtensions)) { [void]$searchIgnored.Add([string]$value) }
        }
    }

    if ($null -ne $Rules) {
        if ($Rules.PSObject.Properties.Name -contains "SupportedExtensions") {
            foreach ($value in @($Rules.SupportedExtensions)) {
                [void]$textSupported.Add([string]$value)
                [void]$searchSupported.Add([string]$value)
            }
        }
        if ($Rules.PSObject.Properties.Name -contains "IgnoredDirectories") {
            foreach ($value in @($Rules.IgnoredDirectories)) { [void]$ignoreDirs.Add([string]$value) }
        }
        if ($Rules.PSObject.Properties.Name -contains "IgnoredFiles") {
            foreach ($value in @($Rules.IgnoredFiles)) { [void]$ignoreFiles.Add([string]$value) }
        }
        if ($Rules.PSObject.Properties.Name -contains "IgnoredExtensions") {
            foreach ($value in @($Rules.IgnoredExtensions)) { [void]$searchIgnored.Add([string]$value) }
        }
    }

    $script:ExcludeDirs = @(Normalize-CcExportNameList $ignoreDirs.ToArray())
    $script:IgnoredFiles = @(Normalize-CcExportNameList $ignoreFiles.ToArray())
    $script:SearchIgnoredExtensions = @(Normalize-CcExportExtensionList $searchIgnored.ToArray())
    $script:TextExtensions = @(Normalize-CcExportExtensionList $textSupported.ToArray() | Where-Object { $script:SearchIgnoredExtensions -notcontains $_ })
    $script:SearchSupportedExtensions = @(Normalize-CcExportExtensionList $searchSupported.ToArray() | Where-Object { $script:SearchIgnoredExtensions -notcontains $_ })
    $script:TextExtensionSet = New-CcExportStringSet $script:TextExtensions
    $script:SearchSupportedExtensionSet = New-CcExportStringSet $script:SearchSupportedExtensions
    $script:SearchIgnoredExtensionSet = New-CcExportStringSet $script:SearchIgnoredExtensions
    $script:BinaryExtensionSet = New-CcExportStringSet $script:BinaryExtensions
    $script:ExcludeDirSet = New-CcExportStringSet $script:ExcludeDirs
    $script:SearchCandidateFilesCacheRoot = $null
    $script:SearchCandidateFilesCache = $null
}

function Get-NormalizedPathKey {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    if ($null -eq $script:NormalizedPathKeyCache) {
        $script:NormalizedPathKeyCache = @{}
    }

    $cacheKey = [string]$Path
    if ($script:NormalizedPathKeyCache.ContainsKey($cacheKey)) {
        return [string]$script:NormalizedPathKeyCache[$cacheKey]
    }

    $result = ""
    try {
        if (Test-Path -LiteralPath $Path) {
            $resolved = (Resolve-Path -LiteralPath $Path).Path
            $result = ($resolved -replace '\\', '/').ToLowerInvariant()
            $script:NormalizedPathKeyCache[$cacheKey] = $result
            return $result
        }
    }
    catch {
    }

    try {
        $full = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
        $result = ($full -replace '\\', '/').ToLowerInvariant()
    }
    catch {
        $result = ($Path -replace '\\', '/').ToLowerInvariant()
    }

    $script:NormalizedPathKeyCache[$cacheKey] = $result
    return $result
}

function Is-GptGeneratedExportFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $fileName = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()

    # Main protection: never search/export generated Context Control exports.
    if ($fileName -like "cc_code_export*") {
        return $true
    }

    # Also protect whatever active output file name the user chose.
    $pathKey = Get-NormalizedPathKey $Path
    $outputKey = Get-NormalizedPathKey $script:OutputFileForFilters

    if ($pathKey -eq $outputKey) {
        return $true
    }

    $outputLeaf = [System.IO.Path]::GetFileName($script:OutputFileForFilters).ToLowerInvariant()
    if ($outputLeaf -ne "" -and ($fileName -eq $outputLeaf -or $fileName.StartsWith($outputLeaf + "."))) {
        return $true
    }

    return $false
}

function Is-IgnoredFilePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    if ($null -eq $script:IgnoredFiles -or $script:IgnoredFiles.Count -eq 0) {
        return $false
    }

    $normalizedPath = ($Path -replace '\\', '/').Trim()
    $normalizedPathLower = $normalizedPath.ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()

    $relativePath = $null
    if ($null -ne $script:CcProjectRoot -and -not [string]::IsNullOrWhiteSpace($script:CcProjectRoot)) {
        try {
            $root = ($script:CcProjectRoot -replace '\\', '/').TrimEnd('/')
            if ($normalizedPathLower.StartsWith(($root.ToLowerInvariant() + '/'))) {
                $relativePath = $normalizedPathLower.Substring($root.Length).TrimStart('/')
            }
        }
        catch {
        }
    }

    foreach ($entry in $script:IgnoredFiles) {
        if ([string]::IsNullOrWhiteSpace($entry)) {
            continue
        }

        $entryNorm = ($entry -replace '\\', '/').Trim().ToLowerInvariant()
        if ($entryNorm -eq "") {
            continue
        }

        $hasWildcard = [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($entryNorm)
        $pattern = if ($hasWildcard) {
            New-Object System.Management.Automation.WildcardPattern($entryNorm, ([System.Management.Automation.WildcardOptions]::IgnoreCase))
        }
        else {
            $null
        }

        if ($entryNorm -contains "/") {
            if ($hasWildcard) {
                if ($pattern.IsMatch($normalizedPathLower)) {
                    return $true
                }
                if ($null -ne $relativePath -and $pattern.IsMatch($relativePath)) {
                    return $true
                }
            }
            elseif ($entryNorm -eq $normalizedPathLower) {
                return $true
            }
            elseif ($null -ne $relativePath -and $entryNorm -eq $relativePath) {
                return $true
            }
        }
        elseif ($hasWildcard) {
            if ($pattern.IsMatch($name)) {
                return $true
            }
        }
        else {
            if ($entryNorm -eq $name) {
                return $true
            }
        }
    }

    return $false
}

function Get-CodeFenceLanguage {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name -eq "cmakelists.txt") {
        return "cmake"
    }

    switch ($ext) {
        ".h" { return "cpp" }
        ".hh" { return "cpp" }
        ".hpp" { return "cpp" }
        ".hxx" { return "cpp" }
        ".inl" { return "cpp" }
        ".c" { return "c" }
        ".cc" { return "cpp" }
        ".cpp" { return "cpp" }
        ".cxx" { return "cpp" }
        ".ixx" { return "cpp" }
        ".cmake" { return "cmake" }
        ".glsl" { return "glsl" }
        ".vert" { return "glsl" }
        ".frag" { return "glsl" }
        ".comp" { return "glsl" }
        ".geom" { return "glsl" }
        ".tesc" { return "glsl" }
        ".tese" { return "glsl" }
        ".mesh" { return "glsl" }
        ".task" { return "glsl" }
        ".shader" { return "glsl" }
        ".gd" { return "gdscript" }
        ".gdshader" { return "glsl" }
        ".json" { return "json" }
        ".tscn" { return "ini" }
        ".tres" { return "ini" }
        ".cfg" { return "ini" }
        ".fc" { return "text" }
        ".ini" { return "ini" }
        ".toml" { return "toml" }
        ".yaml" { return "yaml" }
        ".ps1" { return "powershell" }
        ".bat" { return "batch" }
        ".cmd" { return "batch" }
        ".ts" { return "typescript" }
        ".js" { return "javascript" }
        ".html" { return "html" }
        ".css" { return "css" }
        ".md" { return "markdown" }
        default { return "" }
    }
}

function Clean-InputLine {
    param([string]$Line)

    if ($null -eq $Line) {
        return ""
    }

    $x = $Line.Trim()

    if ($x -eq "") {
        return ""
    }

    if ($x.StartsWith($script:Fence3)) {
        return ""
    }

    if ($x.StartsWith("#")) {
        return ""
    }

    if ($x.StartsWith("- ")) {
        $x = $x.Substring(2).Trim()
    }

    if ($x.StartsWith("* ")) {
        $x = $x.Substring(2).Trim()
    }

    $x = $x -replace '^\d+[\.!\)]\s+', ''

    $Backtick = [string][char]96

    if ($x.StartsWith($Backtick) -and $x.EndsWith($Backtick) -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith('"') -and $x.EndsWith('"') -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith("'") -and $x.EndsWith("'") -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    return $x.Trim()
}

function Is-ExcludedPath {
    param([string]$Path)

    if (Is-GptGeneratedExportFile $Path) {
        return $true
    }

    if (Is-IgnoredFilePath $Path) {
        return $true
    }

    if ($Path -like "*.ccbak.*") {
        return $true
    }

    $parts = ($Path -replace '\\', '/').TrimEnd('/')

    if ($null -ne $script:CcProjectRoot -and -not [string]::IsNullOrWhiteSpace($script:CcProjectRoot)) {
        try {
            $root = ($script:CcProjectRoot -replace '\\', '/').TrimEnd('/')
            if ($parts.Equals($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $false
            }

            if ($parts.StartsWith(($root + '/'), [System.StringComparison]::OrdinalIgnoreCase)) {
                $parts = $parts.Substring($root.Length).TrimStart('/')
            }
        }
        catch {
        }
    }

    $split = $parts.Split([char]'/')

    for ($i = 0; $i -lt $split.Count; $i++) {
        $part = $split[$i]
        if ($part -eq "") {
            continue
        }

        $excluded = if ($null -ne $script:ExcludeDirSet) {
            $script:ExcludeDirSet.Contains($part)
        }
        else {
            $script:ExcludeDirs -contains $part
        }

        if ($excluded) {
            if ($part -ieq "contextcontrol" -and $i -ne 0) {
                continue
            }

            return $true
        }
    }

    return $false
}

function Is-CodeSearchExtension {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    $ignored = if ($null -ne $script:SearchIgnoredExtensionSet) {
        $script:SearchIgnoredExtensionSet.Contains($ext)
    }
    else {
        $script:SearchIgnoredExtensions -contains $ext
    }

    if ($ignored) {
        return $false
    }

    if ($name -eq "cmakelists.txt") {
        return $true
    }

    if ($null -ne $script:SearchSupportedExtensionSet) {
        return $script:SearchSupportedExtensionSet.Contains($ext)
    }

    return ($script:SearchSupportedExtensions -contains $ext)
}

function Is-TextSearchCandidate {
    param([System.IO.FileInfo]$File)

    if ($null -eq $File) {
        return $false
    }

    if (Is-ExcludedPath $File.FullName) {
        return $false
    }

    $name = [System.IO.Path]::GetFileName($File.Name).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($File.Name).ToLowerInvariant()

    # FUNC:/FIND: search should only scan real programming/project source files.
    # Do not let previous Context Control exports or analysis markdown recursively match
    # function names and bloat the next export.
    if ($name -like "cc_code_export*.md" -or
        $name -like "*code_export*.md" -or
        $name -like "engine_analysis*.md" -or
        $name -like "*.ccbak.*") {
        return $false
    }

    # Markdown exports/docs are useful when explicitly requested as paths,
    # but they are bad symbol-search candidates because they contain copied code.
    if ($ext -eq ".md") {
        return $false
    }

    $isBinary = if ($null -ne $script:BinaryExtensionSet) {
        $script:BinaryExtensionSet.Contains($ext)
    }
    else {
        $script:BinaryExtensions -contains $ext
    }

    if ($isBinary) {
        return $false
    }

    if (-not (Is-CodeSearchExtension $File.FullName)) {
        return $false
    }

    return $true
}

function Get-RelativeDisplayPath {
    param([string]$Path)

    if ($null -eq $script:RelativeDisplayPathCache) {
        $script:RelativeDisplayPathCache = @{}
    }

    $cacheKey = [string]$Path
    if ($script:RelativeDisplayPathCache.ContainsKey($cacheKey)) {
        return [string]$script:RelativeDisplayPathCache[$cacheKey]
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $root = (Get-Location).Path

    $result = ""
    if ($resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $resolved.Substring($root.Length)
        $rel = $rel -replace '^[\\/]+', ''
        $result = ($rel -replace '\\', '/')
    }
    else {
        $result = ($resolved -replace '\\', '/')
    }

    $script:RelativeDisplayPathCache[$cacheKey] = $result
    return $result
}

function Get-PathKey {
    param([string]$Path)

    if ($null -eq $script:PathKeyCache) {
        $script:PathKeyCache = @{}
    }

    $cacheKey = [string]$Path
    if ($script:PathKeyCache.ContainsKey($cacheKey)) {
        return [string]$script:PathKeyCache[$cacheKey]
    }

    $result = ""
    if (-not (Test-Path -LiteralPath $Path)) {
        $result = ($Path -replace '\\', '/').ToLowerInvariant()
        $script:PathKeyCache[$cacheKey] = $result
        return $result
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $result = ($resolved -replace '\\', '/').ToLowerInvariant()
    $script:PathKeyCache[$cacheKey] = $result
    return $result
}
