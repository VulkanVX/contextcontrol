# CC-DESC: Helper functions dot-sourced by lib/Cc.Dir.Export.ps1.

function Test-CcDirBuildAnchorAllowed {
    param($Record, [object[]]$Roots)

    $path = Normalize-CcDirRulePath ([string]$Record.Path)
    if (-not (Test-CcDirBuildFile $path)) {
        return $true
    }

    if (Test-CcDirProfileFlag "BuildFocused") {
        return $true
    }

    if ($path -match '^ide/[^/]+/[^/]+\.csproj$') {
        return $true
    }

    if (-not $path.Contains("/")) {
        return $true
    }

    $sourceRoots = @($Roots | Where-Object { Test-CcDirSelectedSourceRoot $_ })
    return Test-CcDirAnchorCoveredByRoots $Record $sourceRoots
}

function Get-CcDirSelectedSourceRoots {
    param([object[]]$SelectedRoots, [int]$AnchorLimit)

    $sourceRoots = @($SelectedRoots | Where-Object { Test-CcDirSelectedSourceRoot $_ })
    return @($sourceRoots | Select-Object -First (Get-CcDirRepresentativeRootLimit $AnchorLimit $sourceRoots.Count))
}

function Get-CcDirRepresentativeRootLimit {
    param([int]$AnchorLimit, [int]$RootCount)

    if ($AnchorLimit -le 0 -or $RootCount -le 0) {
        return 0
    }

    $default = [Math]::Min(3, $RootCount)
    if ($AnchorLimit -ge 12) {
        return [Math]::Min(5, $RootCount)
    }

    return [Math]::Min($default, $AnchorLimit)
}

function Add-CcDirAnchorSelection {
    param(
        [System.Collections.Generic.List[object]]$Selection,
        [object[]]$Candidates,
        [int]$MaxToAdd,
        [int]$Limit,
        [hashtable]$RootCounts = $null,
        [hashtable]$CategoryCounts = $null,
        [int]$MaxPerRoot = [int]::MaxValue,
        [int]$BuildMax = [int]::MaxValue,
        [bool]$EnforceRootLimit = $false,
        [bool]$EnforceBuildMax = $true,
        [bool]$PreserveCandidateOrder = $false
    )

    if ($MaxToAdd -le 0 -or $Selection.Count -ge $Limit) {
        return
    }

    $added = 0
    $orderedCandidates = if ($PreserveCandidateOrder) { @($Candidates) } else { @(Sort-CcDirAnchorRecords $Candidates) }
    foreach ($candidate in $orderedCandidates) {
        if ($Selection.Count -ge $Limit -or $added -ge $MaxToAdd) {
            break
        }

        $path = [string]$candidate.Path
        if (@($Selection.ToArray() | Where-Object { ([string]$_.Path).Equals($path, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            continue
        }

        $category = Get-CcDirAnchorCategory $candidate
        if ($EnforceBuildMax -and $category -eq "build" -and $null -ne $CategoryCounts) {
            $buildCount = if ($CategoryCounts.ContainsKey("build")) { [int]$CategoryCounts["build"] } else { 0 }
            if ($buildCount -ge $BuildMax) {
                continue
            }
        }

        $root = Get-CcDirStableAnchorRoot $path
        if ($EnforceRootLimit -and -not [string]::IsNullOrWhiteSpace($root) -and $null -ne $RootCounts) {
            if ((Get-CcDirAnchorRootCount $RootCounts $root) -ge $MaxPerRoot) {
                continue
            }
        }

        [void]$Selection.Add($candidate)
        if ($null -ne $CategoryCounts) {
            if (-not $CategoryCounts.ContainsKey($category)) { $CategoryCounts[$category] = 0 }
            $CategoryCounts[$category] = [int]$CategoryCounts[$category] + 1
        }

        if (-not [string]::IsNullOrWhiteSpace($root) -and $null -ne $RootCounts) {
            if (-not $RootCounts.ContainsKey($root)) { $RootCounts[$root] = 0 }
            $RootCounts[$root] = [int]$RootCounts[$root] + 1
        }

        $added++
    }
}

function Select-CcDirAnchorFiles {
    param([object[]]$FileRecords, [int]$Limit, [int]$AnchorThreshold, [object[]]$SelectedRoots = @())

    $allUsable = @(Sort-CcDirAnchorRecords (@($FileRecords | Where-Object { [int]$_.Score -ge 0 })))
    $eligible = @(Sort-CcDirAnchorRecords (@($FileRecords | Where-Object { [int]$_.Score -ge $AnchorThreshold })))
    if ($Limit -le 0 -or $eligible.Count -eq 0) {
        return @()
    }

    $isBuildFocused = Test-CcDirProfileFlag "BuildFocused"
    $eligible = @($eligible | Where-Object {
        (Get-CcDirAnchorCategory $_) -ne "build" -or (Test-CcDirBuildAnchorAllowed $_ $SelectedRoots)
    })
    $build = @($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -eq "build" })
    $source = @($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -eq "source" })
    $asset = @($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -eq "asset" })
    $support = @($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -eq "support" })
    $other = @($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -eq "other" })

    $selection = New-Object System.Collections.Generic.List[object]
    $codingFileCount = @($FileRecords | Where-Object { Test-CcDirCodingKind ([string]$_.Kind) }).Count
    $isTinyProject = $codingFileCount -le 30
    $isLargeProject = @($FileRecords).Count -gt 500
    $buildRatio = if ($isBuildFocused) { 0.40 } elseif ($isTinyProject) { 0.35 } else { 0.20 }
    $buildMax = if ($source.Count -lt 3) { $Limit } else { [Math]::Max(1, [Math]::Floor($Limit * $buildRatio)) }
    if ($source.Count -ge 3) {
        $buildMax = [Math]::Min($buildMax, [Math]::Max(1, [Math]::Floor($source.Count * 0.25)))
    }
    $sourceMinRatio = if ($isTinyProject) { 0.40 } else { 0.60 }
    $sourceMin = [Math]::Min($source.Count, [Math]::Ceiling($Limit * $sourceMinRatio))
    $maxPerRoot = if ($isTinyProject) { [int]::MaxValue } else { [Math]::Max(2, [Math]::Floor($Limit * 0.30)) }
    $rootCounts = @{}
    $categoryCounts = @{}

    $selectedSourceRoots = @(Get-CcDirSelectedSourceRoots $SelectedRoots $Limit)
    foreach ($root in $selectedSourceRoots) {
        $rootPath = [string]$root.Path
        $representatives = @(Sort-CcDirRepresentativeAnchorRecords (@($eligible | Where-Object { (Get-CcDirAnchorCategory $_) -in @("source", "asset") -and (Test-CcDirAnchorUnderRoot $_ $rootPath) -and (Test-CcDirRepresentativeAnchor $_) })))
        if ($representatives.Count -eq 0) {
            $representatives = @(Sort-CcDirRepresentativeAnchorRecords (@($allUsable | Where-Object { (Get-CcDirAnchorCategory $_) -in @("source", "asset") -and (Test-CcDirAnchorUnderRoot $_ $rootPath) })))
        }

        Add-CcDirAnchorSelection $selection $representatives 1 $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax $false $true $true
    }

    if ($asset.Count -gt 0) {
        Add-CcDirAnchorSelection $selection $asset 1 $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax $false $true
    }

    $coveredSource = if ($SelectedRoots.Count -gt 0) {
        @($source | Where-Object { Test-CcDirAnchorCoveredByRoots $_ $SelectedRoots })
    }
    else {
        $source
    }
    $uncoveredSource = if ($SelectedRoots.Count -gt 0) {
        @($source | Where-Object { -not (Test-CcDirAnchorCoveredByRoots $_ $SelectedRoots) })
    }
    else {
        @()
    }

    $currentSourceCount = if ($categoryCounts.ContainsKey("source")) { [int]$categoryCounts["source"] } else { 0 }
    Add-CcDirAnchorSelection $selection $coveredSource ([Math]::Max(0, $sourceMin - $currentSourceCount)) $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    $currentSourceCount = if ($categoryCounts.ContainsKey("source")) { [int]$categoryCounts["source"] } else { 0 }
    if ($currentSourceCount -lt $sourceMin) {
        Add-CcDirAnchorSelection $selection $coveredSource ([Math]::Max(0, $sourceMin - $currentSourceCount)) $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax $false $true
    }
    $currentSourceCount = if ($categoryCounts.ContainsKey("source")) { [int]$categoryCounts["source"] } else { 0 }
    if ($currentSourceCount -lt $sourceMin) {
        Add-CcDirAnchorSelection $selection $uncoveredSource ([Math]::Max(0, $sourceMin - $currentSourceCount)) $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax $false $true
    }

    Add-CcDirAnchorSelection $selection $build $buildMax $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $support $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $other $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $source $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $asset $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $eligible $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax (-not $isTinyProject) $true
    Add-CcDirAnchorSelection $selection $eligible $Limit $Limit $rootCounts $categoryCounts $maxPerRoot $buildMax $false $true

    return @(Sort-CcDirAnchorRecords $selection.ToArray() | Select-Object -First $Limit)
}

function Get-CcDirAnchorScore {
    param([string]$Path, [string]$Kind)

    $relative = Normalize-CcDirRulePath $Path
    $lower = $relative.ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($lower)
    if ($null -eq $script:CcDirAnchorScoreCache) {
        $script:CcDirAnchorScoreCache = @{}
    }

    $cacheKey = "$lower|$(([string]$Kind).ToLowerInvariant())"
    if ($script:CcDirAnchorScoreCache.ContainsKey($cacheKey)) {
        return [int]$script:CcDirAnchorScoreCache[$cacheKey]
    }

    $score = 0
    if ([string]::IsNullOrWhiteSpace($relative) -or
        [System.IO.Path]::GetFileName($relative).Equals(".ccDirProfile.json", [System.StringComparison]::OrdinalIgnoreCase)) {
        $score = -999
    }
    elseif (Test-CcDirArtifactPath $relative) {
        $score = -999
    }
    elseif ($null -ne $script:CcDirProjectHints -and
        ((Test-CcDirPathMatchesAnyPattern $relative @($script:CcDirProjectHints.DemoteFiles)) -or
            (Test-CcDirPathMatchesAnyRoot $relative @($script:CcDirProjectHints.DemoteRoots)))) {
        $score = -50
    }
    elseif ($null -ne $script:CcDirProjectHints -and (Test-CcDirPathMatchesAnyPattern $relative @($script:CcDirProjectHints.PinFiles))) {
        $score = 100
    }
    elseif (@("ccdir.ps1", "cc.ps1", "ccreplace.ps1") -contains $name) { $score = 100 }
    elseif ($lower -match '^lib/(cc\.dir\.export|cc\.export\.(source|functions)|cc\.replace\.(parse|plan|apply))\.ps1$') { $score = 90 }
    elseif (@("cargo.lock", "go.sum") -contains $name) { $score = 20 }
    elseif (Test-CcDirBuildFile $relative) { $score = 100 }
    elseif ((Test-CcDirRuntimeEntrypoint $relative) -and ((Test-CcDirFunctionExportKind $Kind) -or @("avalonia-xaml", "xaml", "shader") -contains $Kind)) { $score = 90 }
    elseif ($lower -match '(^|/)styles/[^/]*(design|style)[^/]*\.(axaml|xaml)$') { $score = 90 }
    elseif ((Test-CcDirFunctionExportKind $Kind) -and (Test-CcDirSubsystemOrchestrator $relative)) { $score = 80 }
    elseif (Test-CcDirPublicInterfaceFile $relative $Kind) { $score = 70 }
    elseif (Test-CcDirShaderOrToolEntrypoint $relative $Kind) { $score = 60 }
    elseif (Test-CcDirToolEntrypoint $relative $Kind) { $score = 50 }
    elseif (Test-CcDirProjectDocAnchor $relative) { $score = 40 }
    elseif ((Test-CcDirCodingKind $Kind) -and (Test-CcDirRepresentativeTestFile $relative)) { $score = 20 }
    elseif ((Test-CcDirFunctionExportKind $Kind) -or $Kind -eq "cpp-header" -or $Kind -eq "shader") { $score = 10 }

    $script:CcDirAnchorScoreCache[$cacheKey] = $score
    return $score
}

function Convert-CcDirTokenRole {
    param([AllowNull()][string]$Value, [string]$Fallback = "project files")

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Fallback
    }

    $clean = ([string]$Value).Trim()
    foreach ($acronym in @("DCCM", "QUIC", "HTTP", "ADNL", "RLDP", "GPU", "CPU", "FFI", "LLM", "API", "DHT", "SVO", "VM", "UI", "AO")) {
        $clean = [regex]::Replace($clean, "(?<![A-Za-z])$acronym(?![A-Za-z])|$acronym", " $acronym ")
    }

    $clean = $clean -creplace '([A-Z]+)([A-Z][a-z])', '$1 $2'
    $clean = $clean -creplace '([a-z0-9])([A-Z])', '$1 $2'
    $clean = $clean -replace '[_\.-]+', ' '
    $clean = $clean.ToLowerInvariant()
    $words = @($clean -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 5)
    if ($words.Count -eq 0) {
        return $Fallback
    }

    return [string]::Join(" ", [string[]]$words)
}

function Get-CcDirPathDerivedRole {
    param([string]$Path, [string]$FallbackNoun)

    $relative = Normalize-CcDirRulePath $Path
    $name = [System.IO.Path]::GetFileNameWithoutExtension($relative)
    $converter = [regex]::Match($name, '^(?i:convert|build|generate)[_-]+(.+?)[_-]+to[_-]+(.+)$')
    if ($converter.Success) {
        $sourceRole = Convert-CcDirTokenRole $converter.Groups[1].Value ""
        $targetRole = Convert-CcDirTokenRole $converter.Groups[2].Value ""
        if ($targetRole.EndsWith("ies", [System.StringComparison]::OrdinalIgnoreCase)) {
            $targetRole = $targetRole.Substring(0, $targetRole.Length - 3) + "y"
        }
        elseif ($targetRole.EndsWith("es", [System.StringComparison]::OrdinalIgnoreCase)) {
            $targetRole = $targetRole.Substring(0, $targetRole.Length - 2)
        }
        elseif ($targetRole.EndsWith("s", [System.StringComparison]::OrdinalIgnoreCase) -and $targetRole.Length -gt 3) {
            $targetRole = $targetRole.Substring(0, $targetRole.Length - 1)
        }

        if (-not [string]::IsNullOrWhiteSpace($sourceRole) -and -not [string]::IsNullOrWhiteSpace($targetRole)) {
            return "$sourceRole $targetRole converter"
        }
    }

    $nameRole = Convert-CcDirTokenRole $name ""
    $genericNames = @("app", "application", "base", "common", "core", "index", "lib", "main", "mod", "program", "route", "server", "service", "source", "util", "utils")
    if (-not [string]::IsNullOrWhiteSpace($nameRole) -and -not (@($genericNames | Where-Object { $nameRole.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0)) {
        return "$nameRole $FallbackNoun".Trim()
    }

    $parts = @($relative -split "/" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -ge 2) {
        $parentRole = Convert-CcDirTokenRole $parts[$parts.Count - 2] ""
        if (-not [string]::IsNullOrWhiteSpace($parentRole) -and -not (@($genericNames | Where-Object { $parentRole.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0)) {
            return "$parentRole $FallbackNoun".Trim()
        }
    }

    return $FallbackNoun
}

function Get-CcDirRole {
    param([string]$Path, [string]$Kind)

    $relative = Normalize-CcDirRulePath $Path
    if ($null -eq $script:CcDirRoleCache) {
        $script:CcDirRoleCache = @{}
    }

    $cacheKey = "$($relative.ToLowerInvariant())|$(([string]$Kind).ToLowerInvariant())"
    if (-not $script:CcDirRoleCache.ContainsKey($cacheKey)) {
        $script:CcDirRoleCache[$cacheKey] = Get-CcDirRoleCore $relative $Kind
    }

    return [string]$script:CcDirRoleCache[$cacheKey]
}

function Get-CcDirRoleCore {
    param([string]$Path, [string]$Kind)

    $name = [System.IO.Path]::GetFileName($Path)
    $lower = $Path.ToLowerInvariant()

    if ($name.Equals("ccDir.ps1", [System.StringComparison]::OrdinalIgnoreCase)) { return "dir manifest entrypoint" }
    if ($name.Equals("cc.ps1", [System.StringComparison]::OrdinalIgnoreCase)) { return "source export entrypoint" }
    if ($name.Equals("ccReplace.ps1", [System.StringComparison]::OrdinalIgnoreCase)) { return "patch apply entrypoint" }
    if ($name.Equals(".ccReplace.settings.json", [System.StringComparison]::OrdinalIgnoreCase)) { return "Context Control root/output settings" }
    if ($name.Equals("CMakeLists.txt", [System.StringComparison]::OrdinalIgnoreCase)) { return "cmake build configuration" }
    if ($name.Equals("README.md", [System.StringComparison]::OrdinalIgnoreCase)) { return "project overview documentation" }
    if ($name.Equals("package.json", [System.StringComparison]::OrdinalIgnoreCase)) { return "node package manifest" }
    if ($name.Equals("Cargo.toml", [System.StringComparison]::OrdinalIgnoreCase)) { return "rust package manifest" }
    if ($name.Equals("pyproject.toml", [System.StringComparison]::OrdinalIgnoreCase)) { return "python project manifest" }
    if ($name.Equals("go.mod", [System.StringComparison]::OrdinalIgnoreCase)) { return "go module manifest" }
    if ($name.Equals("pom.xml", [System.StringComparison]::OrdinalIgnoreCase)) { return "java maven manifest" }
    if ($name.EndsWith(".csproj", [System.StringComparison]::OrdinalIgnoreCase)) { return "dotnet project file" }
    if ($lower.Contains("cc.dir.export")) { return "dir manifest export" }
    if ($lower.Contains("cc.export.functions")) { return "function source export" }
    if ($lower.Contains("cc.export.fileblocks")) { return "file block formatting" }
    if ($lower.Contains("cc.export.autodeps")) { return "auto dependency export" }
    if ($lower.Contains("cc.export.config")) { return "source export configuration" }
    if ($lower.Contains("cc.export.source")) { return "source export orchestration" }
    if ($lower.Contains("cc.replace.parse")) { return "replace block parser" }
    if ($lower.Contains("cc.replace.plan")) { return "replace plan builder" }
    if ($lower.Contains("cc.replace.apply")) { return "replace apply runner" }
    if ($lower.Contains("cc.replace.ui")) { return "replace menu UI" }
    if ($lower.Contains("cc.replace.target")) { return "replace target resolver" }
    if ($lower.Contains("cc.replace.pipeline")) { return "replace pipeline commands" }
    if ($lower.Contains("skillbook/built-in-overrides") -or $lower.Contains("skillbook/cc-flow") -or $lower.Contains("cc-flow")) { return "phase flow instruction" }
    if (($Kind -eq "avalonia-xaml" -or $Kind -eq "xaml") -and $lower.Contains("skillbook")) { return "skillbook page layout" }
    if ($Kind -eq "csharp" -and $lower.Contains("skillbook") -and ($lower.Contains("/views/") -or $lower.Contains("/controls/") -or $lower.Contains("/viewmodels/"))) { return "skillbook UI logic" }
    if ($lower.Contains("skillbook")) { return "skillbook workflow" }
    if ($lower.Contains("projectgraph")) { return "project graph rendering" }
    if ($lower.Contains("codeeditor.minimap")) { return "code editor minimap" }
    if ($lower.Contains("codeeditor")) { return "code editor control" }
    if ($lower.Contains("mainwindow")) { return "main UI" }
    if ($lower.Contains("viewmodel")) { return "viewmodel state" }
    if ($lower.Contains("prompt")) { return "prompt workflow" }
    if ($lower.Contains("theme") -or $lower.Contains("style")) { return "styling resource" }
    if (Test-CcDirRuntimeEntrypoint $Path) { return "runtime entrypoint" }
    if (Test-CcDirSubsystemOrchestrator $Path) { return (Convert-CcDirTokenRole ([System.IO.Path]::GetFileNameWithoutExtension($Path)) "subsystem orchestrator") }

    switch ($Kind) {
        "powershell" { return (Get-CcDirPathDerivedRole $Path "powershell script") }
        "csharp" { return (Get-CcDirPathDerivedRole $Path "source") }
        "cpp" { return (Get-CcDirPathDerivedRole $Path "source") }
        "c" { return (Get-CcDirPathDerivedRole $Path "source") }
        "cpp-header" { return (Get-CcDirPathDerivedRole $Path "interfaces") }
        "avalonia-xaml" { return "avalonia UI markup" }
        "xaml" { return "xaml UI markup" }
        "markdown" { return "markdown documentation" }
        "json" { return "json configuration" }
        "cmake" { return "cmake build file" }
        "rust" { return (Get-CcDirPathDerivedRole $Path "source") }
        "go" { return (Get-CcDirPathDerivedRole $Path "source") }
        "java" { return (Get-CcDirPathDerivedRole $Path "source") }
        "kotlin" { return (Get-CcDirPathDerivedRole $Path "source") }
        "python" { return (Get-CcDirPathDerivedRole $Path "source") }
        "typescript" { return (Get-CcDirPathDerivedRole $Path "source") }
        "typescript-react" { return (Get-CcDirPathDerivedRole $Path "react component") }
        "javascript" { return (Get-CcDirPathDerivedRole $Path "source") }
        "shader" { return (Get-CcDirPathDerivedRole $Path "shader") }
        default { return (Convert-CcDirTokenRole ([System.IO.Path]::GetFileNameWithoutExtension($Path)) "project file") }
    }
}

function Get-CcDirTier {
    param([string]$Path, [string]$Kind, [bool]$Scoped)

    if ($Scoped) {
        if (Test-CcDirFunctionExportKind $Kind) {
            return "L2"
        }

        return "L1"
    }

    return "L0"
}

function Get-CcDirRootRole {
    param([string]$Path)

    $clean = (Normalize-CcDirRulePath $Path).TrimEnd("/")
    if ($null -eq $script:CcDirRootRoleCache) {
        $script:CcDirRootRoleCache = @{}
    }

    $cacheKey = $clean.ToLowerInvariant()
    if (-not $script:CcDirRootRoleCache.ContainsKey($cacheKey)) {
        $script:CcDirRootRoleCache[$cacheKey] = Get-CcDirRootRoleCore $clean
    }

    return [string]$script:CcDirRootRoleCache[$cacheKey]
}

function Get-CcDirRootRoleCore {
    param([string]$Path)

    $clean = (Normalize-CcDirRulePath $Path).TrimEnd("/")
    $name = if ([string]::IsNullOrWhiteSpace($clean)) { "" } else { ($clean -split "/")[-1] }
    $lower = $clean.ToLowerInvariant()
    $profileCategory = (Get-CcDirProfileRootCategory $Path).Trim().ToLowerInvariant()
    switch ($profileCategory) {
        "control-plane" { return "control plane" }
        "core-source" { return "$((Convert-CcDirTokenRole $name "core")) core source".Trim() }
        "ui-source" { return "$((Convert-CcDirTokenRole $name "ui")) UI source".Trim() }
        "test-source" { return "$((Convert-CcDirTokenRole $name "tests")) test source".Trim() }
        "asset-shader" { return "$((Convert-CcDirTokenRole $name "shaders")) shader assets".Trim() }
        "support-script" { return "$((Convert-CcDirTokenRole $name "support")) support scripts".Trim() }
        "owned-tool" { return "$((Convert-CcDirTokenRole $name "tools")) owned tools".Trim() }
        "build-config" { return "$((Convert-CcDirTokenRole $name "build")) build configuration".Trim() }
        "docs" { return "$((Convert-CcDirTokenRole $name "docs")) documentation".Trim() }
    }

    if ($lower -eq ".github") { return "release automation" }
    if ($lower -eq "packaging") { return "release packaging" }
    if ($lower -eq "views" -or $lower.EndsWith("/views")) { return "workbench views" }
    if ($lower -eq "controls" -or $lower.EndsWith("/controls")) { return "workbench custom controls" }
    if ($lower -eq "services" -or $lower.EndsWith("/services")) { return "workbench services" }
    if ($lower -eq "styles" -or $lower.EndsWith("/styles")) { return "workbench styling resources" }
    if ($lower -eq "viewmodels" -or $lower.EndsWith("/viewmodels")) { return "workbench viewmodels" }
    if ($lower -eq "components" -or $lower.EndsWith("/components")) { return "UI components" }
    if ($lower -eq "routes" -or $lower.EndsWith("/routes")) { return "routing" }
    if ($lower -eq "pages" -or $lower.EndsWith("/pages")) { return "page routes" }
    if ($lower -eq "server" -or $lower.EndsWith("/server")) { return "backend server" }
    if ($lower -eq "api" -or $lower.EndsWith("/api")) { return "API routes" }
    if ($lower -eq "cmd" -or $lower.EndsWith("/cmd")) { return "command entrypoints" }
    if ($lower -eq "internal" -or $lower.EndsWith("/internal")) { return "internal packages" }
    if ($lower -eq "pkg" -or $lower.EndsWith("/pkg")) { return "library packages" }
    if ($lower -eq "crates" -or $lower.EndsWith("/crates")) { return "rust crates" }
    if ($lower -eq "benches" -or $lower.EndsWith("/benches")) { return "benchmarks" }
    if ($lower -eq "examples" -or $lower.EndsWith("/examples")) { return "examples" }
    if ($lower -eq "tests" -or $lower.EndsWith("/tests") -or $lower.Contains("/test/")) { return "tests" }
    if ($lower -eq "lib") { return "Context Control scripts" }
    if ($lower.Contains("replace")) { return "CC replace apply scripts" }
    if ($lower.Contains("export")) { return "CC source export scripts" }
    if ($lower -eq "skillbook") { return "agent flow instructions" }
    if ($lower -eq "src") { return "source files" }
    if ($lower -eq "include" -or $lower.Contains("/include")) { return "headers" }
    if ($lower -eq "shaders" -or $lower.Contains("/shaders")) { return "shader programs" }
    if ($lower -eq "tools" -or $lower.Contains("/tools")) { return "offline tools" }
    if ($lower.Contains("render")) { return "rendering systems" }
    if ($lower.Contains("world")) { return "world systems" }
    if ($lower.Contains("engine")) { return "engine lifecycle" }
    if ($lower.Contains("client")) { return "client app" }
    if ($lower.Contains("app")) { return "app source" }
    if ([string]::IsNullOrWhiteSpace($name)) { return "project root" }
    return (Convert-CcDirTokenRole $name "$name subsystem")
}

function Get-CcDirFindHints {
    param(
        [string]$Path,
        [string[]]$Symbols
    )

    $tokens = New-Object System.Collections.Generic.List[string]
    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    foreach ($part in (($Path -replace '[^A-Za-z0-9_]+', ' ') -split '\s+')) {
        if ($part.Length -ge 4 -and $part.Length -le 40) {
            [void]$tokens.Add($part)
        }
    }

    foreach ($symbol in @($Symbols)) {
        $leaf = ($symbol -split '::')[-1]
        if ($leaf.Length -ge 4 -and $leaf.Length -le 60) {
            [void]$tokens.Add($leaf)
        }
    }

    if ($name.Length -ge 4) {
        [void]$tokens.Add($name)
    }

    return @($tokens.ToArray() |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique |
        Select-Object -First 6)
}

function Join-CcDirCompactHintList {
    param(
        [string[]]$Items,
        [int]$MaxItems = 8,
        [int]$MaxLength = 160
    )

    $selected = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Items)) {
        $clean = ([string]$item).Trim()
        if ([string]::IsNullOrWhiteSpace($clean)) {
            continue
        }

        if ($selected.Contains($clean)) {
            continue
        }

        $candidate = if ($selected.Count -eq 0) { $clean } else { ([string]::Join(',', [string[]]$selected.ToArray()) + "," + $clean) }
        if ($candidate.Length -gt $MaxLength) {
            break
        }

        [void]$selected.Add($clean)
        if ($selected.Count -ge $MaxItems) {
            break
        }
    }

    return [string]::Join(',', [string[]]$selected.ToArray())
}

function Get-CcDirSymbols {
    param([System.IO.FileInfo]$File)

    $path = Get-CcDirRelativePath $File
    $kind = Get-CcDirKind $path
    $isXaml = @("avalonia-xaml", "xaml") -contains $kind
    if (-not (Test-CcDirFunctionExportKind $kind) -and -not $isXaml) {
        return @()
    }

    try {
        $text = [System.IO.File]::ReadAllText($File.FullName)
    }
    catch {
        return @()
    }

    if ($text.Length -gt 160000) {
        $text = $text.Substring(0, 160000)
    }

    $symbols = New-Object System.Collections.Generic.List[string]
    $options = [System.Text.RegularExpressions.RegexOptions]::Multiline -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant

    if ($isXaml) {
        foreach ($m in [regex]::Matches($text, '(?:x:Class|Class)\s*=\s*"([^"]+)"', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, '(?:x:Name|Name)\s*=\s*"([A-Za-z_][A-Za-z0-9_.-]*)"', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, '\{Binding\s+([A-Za-z_][A-Za-z0-9_.]*)', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
    }
    elseif ($kind -eq "powershell") {
        foreach ($m in [regex]::Matches($text, '^\s*function\s+([A-Za-z_][\w.-]*)', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
    }
    elseif ($kind -eq "csharp") {
        foreach ($m in [regex]::Matches($text, '^\s*(?:public|private|protected|internal|sealed|static|partial|abstract|\s)*(?:class|record|struct|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
        foreach ($m in [regex]::Matches($text, '^\s*(?:public|private|protected|internal|static|async|override|virtual|sealed|partial|\s)+[\w<>\[\],.?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
    }
    else {
        foreach ($m in [regex]::Matches($text, '^\s*(?:[\w:<>,~*&\s]+)\s+([A-Za-z_~][\w:~]*)\s*\([^;]*\)\s*(?:const\s*)?\{', $options)) {
            [void]$symbols.Add($m.Groups[1].Value)
        }
    }

    return @($symbols.ToArray() |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique |
        Select-Object -First 8)
}

function Test-CcDirHighSignalFile {
    param([string]$Path)

    $normalized = Normalize-CcDirRulePath $Path
    $name = [System.IO.Path]::GetFileName($Path)
    $nameLower = $name.ToLowerInvariant()
    $lower = $Path.ToLowerInvariant()

    if (-not $normalized.Contains("/")) {
        if (@(
            "ccdir.ps1",
            "cc.ps1",
            "ccreplace.ps1",
            "readme.md",
            "cmakelists.txt",
            "package.json",
            "project.godot",
            "global.json",
            "appsettings.json",
            ".ccreplace.settings.json"
        ) -contains $nameLower) {
            return $true
        }

        if ($nameLower.EndsWith(".sln") -or
            $nameLower.EndsWith(".csproj") -or
            $nameLower.EndsWith(".fsproj") -or
            $nameLower.EndsWith(".vbproj") -or
            $nameLower.EndsWith(".props") -or
            $nameLower.EndsWith(".targets")) {
            return $true
        }
    }

    if ($lower -match '^lib/cc\.(dir\.export|export\.(source|functions)|replace\.(parse|plan|apply))\.ps1$') {
        return $true
    }

    if ($lower -match '^skillbook/(built-in-overrides/cc-flow/cc-flow-0[1-3]-.*\.md|cc-flow/cc-flow-0[1-3]-.*\.md)$') {
        return $true
    }

    if ($lower -match '^ide/contextcontrol\.workbench/(contextcontrol\.workbench\.csproj|views/mainwindow\.axaml|views/mainwindow\.axaml\.cs|styles/workbenchdesign\.axaml)$') {
        return $true
    }

    if (-not $lower.StartsWith("ide/") -and
        $lower -match '(^|/)(main|program|app|index)\.(cpp|cc|cxx|c|cs|py|ts|tsx|js|jsx|gd|rs)$') {
        return $true
    }

    return $false
}
