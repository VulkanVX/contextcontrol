# CC-DESC: Helper functions dot-sourced by lib/Cc.Dir.Export.ps1.

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

function Normalize-CcDirRulePath {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $clean = ([string]$Value).Trim() -replace '\\', '/'
    while ($clean.StartsWith("./")) {
        $clean = $clean.Substring(2)
    }

    return $clean.Trim("/")
}

function Normalize-CcDirExtension {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $clean = ([string]$Value).Trim().ToLowerInvariant()
    if (-not $clean.StartsWith(".")) {
        $clean = ".$clean"
    }

    return $clean
}

function New-CcDirProjectHints {
    return [pscustomobject]@{
        OwnedRoots = @()
        ThirdPartyRoots = @()
        GeneratedRoots = @()
        PinRoots = @()
        PinFiles = @()
        DemoteRoots = @()
        DemoteFiles = @()
        PreferredRepresentativeRoots = @()
        RootCategories = @{}
        BuildFocused = $false
        DocsHeavy = $false
        ToolsHeavy = $false
        SourcePath = ""
    }
}

function ConvertTo-CcDirStringArray {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    return @($Value | ForEach-Object {
        $clean = Normalize-CcDirRulePath ([string]$_)
        if (-not [string]::IsNullOrWhiteSpace($clean)) {
            if ($clean -notmatch '\.[A-Za-z0-9]+$' -and -not $clean.EndsWith("/")) {
                $clean += "/"
            }

            $clean
        }
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function ConvertTo-CcDirFilePatternArray {
    param($Value)

    if ($null -eq $Value) {
        return @()
    }

    return @($Value | ForEach-Object {
        Normalize-CcDirRulePath ([string]$_)
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Read-CcDirOptionalProjectHints {
    $hints = New-CcDirProjectHints
    $path = Join-Path (Get-Location).Path ".contextcontrol/dir-profile.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return $hints
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw
        if ([string]::IsNullOrWhiteSpace($json)) {
            return $hints
        }

        $loaded = $json | ConvertFrom-Json
        foreach ($prop in @("OwnedRoots", "ThirdPartyRoots", "GeneratedRoots", "PinRoots", "DemoteRoots", "PreferredRepresentativeRoots")) {
            $jsonName = $prop.Substring(0, 1).ToLowerInvariant() + $prop.Substring(1)
            if ($loaded.PSObject.Properties.Name -contains $jsonName) {
                $hints.$prop = @(ConvertTo-CcDirStringArray $loaded.$jsonName)
            }
        }

        foreach ($prop in @("PinFiles", "DemoteFiles")) {
            $jsonName = $prop.Substring(0, 1).ToLowerInvariant() + $prop.Substring(1)
            if ($loaded.PSObject.Properties.Name -contains $jsonName) {
                $hints.$prop = @(ConvertTo-CcDirFilePatternArray $loaded.$jsonName)
            }
        }

        if ($loaded.PSObject.Properties.Name -contains "rootCategories") {
            $map = @{}
            foreach ($property in $loaded.rootCategories.PSObject.Properties) {
                $key = Normalize-CcDirRulePath ([string]$property.Name)
                if (-not [string]::IsNullOrWhiteSpace($key)) {
                    if (-not $key.EndsWith("/")) {
                        $key += "/"
                    }

                    $map[$key] = ([string]$property.Value).Trim()
                }
            }

            $hints.RootCategories = $map
        }

        foreach ($flag in @("buildFocused", "docsHeavy", "toolsHeavy")) {
            if ($loaded.PSObject.Properties.Name -contains $flag) {
                $target = $flag.Substring(0, 1).ToUpperInvariant() + $flag.Substring(1)
                $hints.$target = [bool]$loaded.$flag
            }
        }

        $hints.SourcePath = $path
        Write-Host "DIR profile hints: $path" -ForegroundColor DarkGray
        return $hints
    }
    catch {
        Write-Host "DIR profile hints could not be read, using default heuristics: $($_.Exception.Message)" -ForegroundColor Yellow
        return $hints
    }
}

function Test-CcDirPathUnderRoot {
    param([string]$Path, [string]$Root)

    $cleanPath = (Normalize-CcDirRulePath $Path).TrimEnd("/") + "/"
    $cleanRoot = (Normalize-CcDirRulePath $Root).TrimEnd("/") + "/"
    if ([string]::IsNullOrWhiteSpace($cleanRoot.Trim("/"))) {
        return $false
    }

    return $cleanPath.StartsWith($cleanRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-CcDirPathMatchesAnyRoot {
    param([string]$Path, [string[]]$Roots)

    foreach ($root in @($Roots)) {
        if (Test-CcDirPathUnderRoot $Path $root) {
            return $true
        }
    }

    return $false
}

function Test-CcDirPathMatchesAnyPattern {
    param([string]$Path, [string[]]$Patterns)

    $clean = Normalize-CcDirRulePath $Path
    foreach ($pattern in @($Patterns)) {
        $cleanPattern = Normalize-CcDirRulePath $pattern
        if ([string]::IsNullOrWhiteSpace($cleanPattern)) {
            continue
        }

        if ($clean -like $cleanPattern -or $clean.Equals($cleanPattern, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Get-CcDirProfileRootCategory {
    param([string]$Path)

    if ($null -eq $script:CcDirProjectHints -or $null -eq $script:CcDirProjectHints.RootCategories) {
        return ""
    }

    $best = ""
    $bestDepth = -1
    foreach ($key in @($script:CcDirProjectHints.RootCategories.Keys)) {
        if (Test-CcDirPathUnderRoot $Path $key) {
            $depth = Get-CcDirPathDepth $key
            if ($depth -gt $bestDepth) {
                $best = [string]$script:CcDirProjectHints.RootCategories[$key]
                $bestDepth = $depth
            }
        }
    }

    return $best
}

function Test-CcDirProfileFlag {
    param([string]$Name)

    if ($null -eq $script:CcDirProjectHints) {
        return $false
    }

    return [bool]$script:CcDirProjectHints.$Name
}

function Get-CcDirRelativePath {
    param([System.IO.FileSystemInfo]$Item)

    if ($null -eq $Item) {
        return ""
    }

    if ($null -eq $script:CcDirRelativePathCache) {
        $script:CcDirRelativePathCache = @{}
    }

    $cacheKey = $Item.FullName
    if ($script:CcDirRelativePathCache.ContainsKey($cacheKey)) {
        return [string]$script:CcDirRelativePathCache[$cacheKey]
    }

    $root = if (-not [string]::IsNullOrWhiteSpace($script:CcDirRootPath)) {
        $script:CcDirRootPath
    }
    else {
        (Get-Location).Path
    }

    try {
        $relative = Normalize-CcDirRulePath ([System.IO.Path]::GetRelativePath($root, $Item.FullName))
    }
    catch {
        $relative = $Item.FullName
        if ($relative.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            $relative = $relative.Substring($root.Length)
        }

        $relative = Normalize-CcDirRulePath $relative
    }

    $script:CcDirRelativePathCache[$cacheKey] = $relative
    return $relative
}

function Test-CcDirDirectoryRule {
    param(
        [System.IO.FileSystemInfo]$Item,
        [string]$Rule
    )

    $cleanRule = Normalize-CcDirRulePath $Rule
    if ([string]::IsNullOrWhiteSpace($cleanRule)) {
        return $false
    }

    $hasWildcard = $cleanRule.Contains("*") -or $cleanRule.Contains("?")
    if ($cleanRule.Contains("/")) {
        $relative = Get-CcDirRelativePath $Item
        if ($hasWildcard) {
            return ($relative -like $cleanRule)
        }

        return $relative.Equals($cleanRule, [System.StringComparison]::OrdinalIgnoreCase) -or
            $relative.StartsWith("$cleanRule/", [System.StringComparison]::OrdinalIgnoreCase)
    }

    if ($hasWildcard) {
        return ($Item.Name -like $cleanRule)
    }

    return $Item.Name.Equals($cleanRule, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-CcDirFileRule {
    param(
        [System.IO.FileSystemInfo]$Item,
        [string]$Rule
    )

    $cleanRule = Normalize-CcDirRulePath $Rule
    if ([string]::IsNullOrWhiteSpace($cleanRule)) {
        return $false
    }

    $hasWildcard = $cleanRule.Contains("*") -or $cleanRule.Contains("?")
    if ($cleanRule.Contains("/")) {
        $relative = Get-CcDirRelativePath $Item
        if ($hasWildcard) {
            return ($relative -like $cleanRule)
        }

        return $relative.Equals($cleanRule, [System.StringComparison]::OrdinalIgnoreCase)
    }

    if ($hasWildcard) {
        return ($Item.Name -like $cleanRule)
    }

    return $Item.Name.Equals($cleanRule, [System.StringComparison]::OrdinalIgnoreCase)
}

function Add-CcDirDefaultArtifactRules {
    if ($IncludeArtifacts) {
        return
    }

    foreach ($value in $ArtifactExcludeDirs) {
        $clean = Normalize-CcDirRulePath $value
        if (-not [string]::IsNullOrWhiteSpace($clean) -and $ExcludeDirs -notcontains $clean) {
            $script:ExcludeDirs += $clean
        }
    }

    foreach ($value in $ArtifactExcludeFileNames) {
        $clean = Normalize-CcDirRulePath $value
        if (-not [string]::IsNullOrWhiteSpace($clean) -and $ExcludeFileNames -notcontains $clean) {
            $script:ExcludeFileNames += $clean
        }
    }
}

function Import-CcDirWorkbenchFileRules {
    $path = $env:CC_WORKBENCH_FILE_RULES_PATH
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path)) {
        return
    }

    try {
        $rules = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json

        if ($rules.PSObject.Properties.Name -contains "IgnoredDirectories") {
            foreach ($value in @($rules.IgnoredDirectories)) {
                $clean = Normalize-CcDirRulePath $value
                if (-not [string]::IsNullOrWhiteSpace($clean) -and $ExcludeDirs -notcontains $clean) {
                    $script:ExcludeDirs += $clean
                }
            }
        }

        $ignoredFiles = @()
        if ($rules.PSObject.Properties.Name -contains "IgnoredFileNames") {
            $ignoredFiles += @($rules.IgnoredFileNames)
        }
        if ($rules.PSObject.Properties.Name -contains "IgnoredFiles") {
            $ignoredFiles += @($rules.IgnoredFiles)
        }
        foreach ($value in $ignoredFiles) {
            $clean = Normalize-CcDirRulePath $value
            if (-not [string]::IsNullOrWhiteSpace($clean) -and $ExcludeFileNames -notcontains $clean) {
                $script:ExcludeFileNames += $clean
            }
        }

        if ($rules.PSObject.Properties.Name -contains "IgnoredExtensions") {
            foreach ($value in @($rules.IgnoredExtensions)) {
                $clean = Normalize-CcDirExtension $value
                if (-not [string]::IsNullOrWhiteSpace($clean) -and $ExcludeFileExtensions -notcontains $clean) {
                    $script:ExcludeFileExtensions += $clean
                }
            }
        }

        Write-Host "File rules: $path" -ForegroundColor DarkGray
    }
    catch {
        Write-Host "Workbench file rules could not be read, using DIR defaults: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function New-CcSharedDefaultSettings {
    return [pscustomobject]@{
        # "auto" detects the project root from the Context Control tool location.
        # Default layout: <project-root>/contextcontrol/, so auto selects the parent
        # project. Explicit paths are respected exactly; use "." to make the
        # Context Control tool folder itself the project root.
        ProjectRoot = "auto"
        OutputRoot = "."
    }
}

function Get-CcSharedScriptDirectory {
    if (-not [string]::IsNullOrWhiteSpace($script:CcToolRoot)) {
        return $script:CcToolRoot
    }

    if ($PSScriptRoot -ne "") {
        if ((Split-Path -Leaf $PSScriptRoot) -ieq "lib") {
            return (Split-Path -Parent $PSScriptRoot)
        }

        return $PSScriptRoot
    }

    return (Get-Location).Path
}

function Get-CcSharedSettingsPath {
    return (Join-Path (Get-CcSharedScriptDirectory) ".ccReplace.settings.json")
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

    return $settings
}

function Read-CcSharedSettings {
    $path = Get-CcSharedSettingsPath

    if (-not (Test-Path -LiteralPath $path)) {
        return New-CcSharedDefaultSettings
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw
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

    if ([string]::IsNullOrWhiteSpace($PathText)) {
        return (Get-CcSharedScriptDirectory)
    }

    $clean = $PathText.Trim()
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

    $clean = $root.Trim()

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

    $clean = $PathText.Trim()
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return [System.IO.Path]::GetFullPath($clean)
    }

    if ($null -eq $Settings) {
        $Settings = Read-CcSharedSettings
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Resolve-CcSharedOutputRoot $Settings) $clean))
}

function Save-OutputFile {
    param([string]$Path)

    $fullPath = Resolve-CcSharedOutputPath $Path $script:CcSharedSettings
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)

    if ([string]::IsNullOrWhiteSpace($dir)) {
        $dir = Resolve-CcSharedOutputRoot $script:CcSharedSettings
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

            if (-not [System.IO.File]::Exists($fullPath)) {
                throw "Export write reported success, but final file does not exist: $fullPath"
            }

            return $fullPath
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds ([Math]::Min(1000, 50 * $attempt))
        }
    }

    throw "Failed to write '$Path' after retries. Close any editor/preview/indexer using it and try again. Temp output was left at: $tmpPath. Last error: $lastError"
}

function Invoke-CcClipboardProgram {
    param(
        [string]$Program,
        [string[]]$Arguments,
        [AllowNull()][string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Program)) {
        throw "Clipboard backend program was empty."
    }

    if ($null -eq $Arguments) {
        $Arguments = @()
    }

    if ($null -eq $Text) {
        $Text = ""
    }

    $cmd = Get-Command $Program -ErrorAction SilentlyContinue
    if ($null -eq $cmd) {
        throw "$Program was not found."
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $cmd.Source
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    foreach ($arg in $Arguments) {
        [void]$psi.ArgumentList.Add($arg)
    }

    try {
        $psi.StandardInputEncoding = New-Object System.Text.UTF8Encoding($false)
    }
    catch {
        # Older runtimes may not expose StandardInputEncoding. The default still
        # works for normal ASCII/UTF-8 project paths; this is only best-effort.
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Write($Text)
    $process.StandardInput.Close()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        if ([string]::IsNullOrWhiteSpace($stderr)) {
            $stderr = "exit code $($process.ExitCode)"
        }
        throw "$Program failed: $stderr"
    }
}

function Copy-CcTextToClipboard {
    [CmdletBinding()]
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    $errors = New-Object System.Collections.Generic.List[string]

    try {
        Microsoft.PowerShell.Management\Set-Clipboard -Value $Text -ErrorAction Stop
        return "Set-Clipboard"
    }
    catch {
        [void]$errors.Add("Set-Clipboard: $($_.Exception.Message)")
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        try {
            Invoke-CcClipboardProgram "pbcopy" @() $Text
            return "pbcopy"
        }
        catch {
            [void]$errors.Add("pbcopy: $($_.Exception.Message)")
        }
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        try {
            $clipExe = Join-Path $env:SystemRoot "System32\clip.exe"
            Invoke-CcClipboardProgram $clipExe @() $Text
            return "clip.exe"
        }
        catch {
            [void]$errors.Add("clip.exe: $($_.Exception.Message)")
        }
    }

    foreach ($candidate in @("wl-copy", "xclip", "xsel")) {
        try {
            if ($candidate -eq "xclip") {
                Invoke-CcClipboardProgram $candidate @("-selection", "clipboard") $Text
            }
            elseif ($candidate -eq "xsel") {
                Invoke-CcClipboardProgram $candidate @("--clipboard", "--input") $Text
            }
            else {
                Invoke-CcClipboardProgram $candidate @() $Text
            }
            return $candidate
        }
        catch {
            [void]$errors.Add("${candidate}: $($_.Exception.Message)")
        }
    }

    throw "No clipboard backend worked. Tried: $([string]::Join('; ', [string[]]$errors.ToArray()))"
}

function Set-Clipboard {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true)]
        [AllowNull()]
        [string]$Value
    )

    begin {
        $parts = New-Object System.Collections.Generic.List[string]
    }

    process {
        if ($null -ne $Value) {
            [void]$parts.Add([string]$Value)
        }
    }

    end {
        $text = [string]::Join([Environment]::NewLine, [string[]]$parts.ToArray())
        [void](Copy-CcTextToClipboard $text)
    }
}


function Detect-Profile {
    if ($Profile.ToLowerInvariant() -ne "auto") {
        return $Profile.ToLowerInvariant()
    }

    if ((Test-Path -LiteralPath "cc.ps1") -and
        (Test-Path -LiteralPath "ccDir.ps1") -and
        (Test-Path -LiteralPath "ccReplace.ps1") -and
        (Test-Path -LiteralPath "lib")) {
        return "contextcontrol"
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
