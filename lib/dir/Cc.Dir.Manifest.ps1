# CC-DESC: Helper functions dot-sourced by lib/Cc.Dir.Export.ps1.

function Get-CcDirRootRecords {
    param(
        [System.IO.FileInfo[]]$AllFiles,
        [object[]]$SelectedAnchors = @(),
        [object[]]$SelectedFamilies = @()
    )

    $rootCounts = @{}
    $rootCodingCounts = @{}
    $rootHeaderCounts = @{}
    $rootAssetCounts = @{}
    $secondCounts = @{}
    $secondCodingCounts = @{}
    $secondHeaderCounts = @{}
    $secondAssetCounts = @{}
    $thirdCounts = @{}
    $thirdCodingCounts = @{}
    $thirdHeaderCounts = @{}
    $thirdAssetCounts = @{}
    $requiredAssetRoots = @{}
    $totalCodingFiles = 0

    foreach ($file in @($AllFiles)) {
        $relative = Get-CcDirRelativePath $file
        $kind = Get-CcDirKind $relative
        $isCodingFile = Test-CcDirCodingKind $kind
        $isHeaderFile = Test-CcDirHeaderKind $kind
        $isAssetFile = Test-CcDirCodeLikeAssetKind $kind
        if ($isCodingFile) {
            $totalCodingFiles++
        }

        if ($isAssetFile) {
            $assetRoot = Get-CcDirStableAssetRoot $relative
            if (-not [string]::IsNullOrWhiteSpace($assetRoot) -and -not (Test-CcDirArtifactPath $assetRoot)) {
                if (-not $requiredAssetRoots.ContainsKey($assetRoot)) {
                    $requiredAssetRoots[$assetRoot] = 0
                }
                $requiredAssetRoots[$assetRoot] = [int]$requiredAssetRoots[$assetRoot] + 1
            }
        }

        $parts = @($relative -split '/' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($parts.Count -lt 2) {
            continue
        }

        $top = $parts[0] + "/"
        Add-CcDirCount $rootCounts $top
        if ($isCodingFile) { Add-CcDirCount $rootCodingCounts $top }
        if ($isHeaderFile) { Add-CcDirCount $rootHeaderCounts $top }
        if ($isAssetFile) { Add-CcDirCount $rootAssetCounts $top }

        if ($parts.Count -ge 3) {
            $second = "$($parts[0])/$($parts[1])/"
            Add-CcDirCount $secondCounts $second
            if ($isCodingFile) { Add-CcDirCount $secondCodingCounts $second }
            if ($isHeaderFile) { Add-CcDirCount $secondHeaderCounts $second }
            if ($isAssetFile) { Add-CcDirCount $secondAssetCounts $second }
        }

        if ($parts.Count -ge 4) {
            $third = "$($parts[0])/$($parts[1])/$($parts[2])/"
            Add-CcDirCount $thirdCounts $third
            if ($isCodingFile) { Add-CcDirCount $thirdCodingCounts $third }
            if ($isHeaderFile) { Add-CcDirCount $thirdHeaderCounts $third }
            if ($isAssetFile) { Add-CcDirCount $thirdAssetCounts $third }
        }
    }

    $candidates = @{}
    foreach ($key in @($rootCounts.Keys)) {
        $codingFiles = Get-CcDirMapCount $rootCodingCounts $key
        $score = Get-CcDirRootCategoryScore $key $codingFiles (Get-CcDirMapCount $rootHeaderCounts $key) (Get-CcDirMapCount $rootAssetCounts $key)
        if ($score -lt 0) { continue }
        Add-CcDirRootCandidate $candidates $key ([int]$rootCounts[$key]) $score $codingFiles (Test-CcDirSourceRootName $key)
    }

    foreach ($key in @($secondCounts.Keys)) {
        $parts = $key.TrimEnd("/") -split "/"
        $top = $parts[0] + "/"
        $topCoding = Get-CcDirMapCount $rootCodingCounts $top
        $codingFiles = Get-CcDirMapCount $secondCodingCounts $key
        $isLargeTopRoot = $topCoding -gt 40 -or ($totalCodingFiles -gt 0 -and ($topCoding / [double]$totalCodingFiles) -gt 0.25)
        if ($isLargeTopRoot -and $codingFiles -ge 5 -and -not (Test-CcDirArtifactPath $key)) {
            $score = Get-CcDirRootCategoryScore $key $codingFiles (Get-CcDirMapCount $secondHeaderCounts $key) (Get-CcDirMapCount $secondAssetCounts $key)
            if ($score -ge 0) {
                Add-CcDirRootCandidate $candidates $key ([int]$secondCounts[$key]) $score $codingFiles (Test-CcDirSourceRootName $key)
            }
        }
    }

    foreach ($key in @($thirdCounts.Keys)) {
        $parts = $key.TrimEnd("/") -split "/"
        $parent = "$($parts[0])/$($parts[1])/"
        $parentCoding = Get-CcDirMapCount $secondCodingCounts $parent
        $codingFiles = Get-CcDirMapCount $thirdCodingCounts $key
        $isLargeParentRoot = $parentCoding -gt 40 -or ($totalCodingFiles -gt 0 -and ($parentCoding / [double]$totalCodingFiles) -gt 0.25)
        if ($isLargeParentRoot -and $codingFiles -ge 5 -and -not (Test-CcDirArtifactPath $key)) {
            $score = Get-CcDirRootCategoryScore $key $codingFiles (Get-CcDirMapCount $thirdHeaderCounts $key) (Get-CcDirMapCount $thirdAssetCounts $key)
            if ($score -ge 0) {
                Add-CcDirRootCandidate $candidates $key ([int]$thirdCounts[$key]) $score $codingFiles (Test-CcDirSourceRootName $key)
            }
        }
    }

    $projectLeaf = (Split-Path -Leaf (Get-Location).Path).ToLowerInvariant()
    foreach ($key in @($secondCounts.Keys)) {
        $parts = $key.TrimEnd("/") -split "/"
        if ($parts.Count -ne 2 -or -not $parts[0].Equals("src", [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $includeKey = "include/$($parts[1])/"
        if (-not $secondCounts.ContainsKey($includeKey)) {
            continue
        }

        $pairLeaf = $parts[1].ToLowerInvariant()
        $projectMatchedPair = -not [string]::IsNullOrWhiteSpace($projectLeaf) -and
            ($projectLeaf.Contains($pairLeaf) -or $pairLeaf.Contains($projectLeaf))
        $sourceScore = if ($projectMatchedPair) { 118 } else { 104 }
        $includeScore = if ($projectMatchedPair) { 116 } else { 102 }
        Add-CcDirExplicitRootCandidates $candidates $AllFiles @($key) $sourceScore $projectMatchedPair $true
        Add-CcDirExplicitRootCandidates $candidates $AllFiles @($includeKey) $includeScore $projectMatchedPair $true
    }

    if ($null -ne $script:CcDirProjectHints) {
        Add-CcDirExplicitRootCandidates $candidates $AllFiles @($script:CcDirProjectHints.PinRoots) 120 $true $true
        Add-CcDirExplicitRootCandidates $candidates $AllFiles @($script:CcDirProjectHints.PreferredRepresentativeRoots) 110 $false $true
        Add-CcDirExplicitRootCandidates $candidates $AllFiles @($script:CcDirProjectHints.OwnedRoots) 95 $false $true
    }

    foreach ($key in @($requiredAssetRoots.Keys)) {
        $files = if ($rootCounts.ContainsKey($key)) {
            [int]$rootCounts[$key]
        }
        elseif ($secondCounts.ContainsKey($key)) {
            [int]$secondCounts[$key]
        }
        elseif ($thirdCounts.ContainsKey($key)) {
            [int]$thirdCounts[$key]
        }
        else {
            [int]$requiredAssetRoots[$key]
        }

        $codingFiles = [Math]::Max(
            [int]$requiredAssetRoots[$key],
            [Math]::Max(
                (Get-CcDirMapCount $rootCodingCounts $key),
                [Math]::Max((Get-CcDirMapCount $secondCodingCounts $key), (Get-CcDirMapCount $thirdCodingCounts $key))))
        Add-CcDirRootCandidate $candidates $key $files 85 $codingFiles $false $true
    }

    foreach ($anchor in @($SelectedAnchors)) {
        $anchorRoot = Get-CcDirStableAnchorRoot ([string]$anchor.Path)
        if ([string]::IsNullOrWhiteSpace($anchorRoot) -or (Test-CcDirArtifactPath $anchorRoot)) {
            continue
        }

        $stats = Get-CcDirFileStatsForRoot $AllFiles $anchorRoot
        if ([int]$stats.Files -eq 0) {
            continue
        }

        $codingFiles = [int]$stats.CodingFiles
        $headerFiles = [int]$stats.HeaderFiles
        $assetFiles = [int]$stats.AssetFiles
        $score = Get-CcDirRootCategoryScore $anchorRoot $codingFiles $headerFiles $assetFiles
        if ($score -lt 0) {
            continue
        }

        $anchorCategory = Get-CcDirRoutingCategory ([string]$anchor.Path) ([string]$anchor.Kind)
        $anchorPath = Normalize-CcDirRulePath ([string]$anchor.Path)
        $isIdeProjectBuildAnchor = $anchorPath -match '^ide/[^/]+/[^/]+\.csproj$'
        $requiredRoot = (@("control-plane", "runtime-entrypoint", "core-source", "ui-source", "test-source", "asset-shader", "support-script") -contains $anchorCategory) -or $isIdeProjectBuildAnchor
        switch ($anchorCategory) {
            "support-script" { $score = [Math]::Max($score, 95) }
            "test-source" { $score = [Math]::Max($score, 90) }
            "asset-shader" { $score = [Math]::Max($score, 90) }
        }

        if ($isIdeProjectBuildAnchor) {
            $score = [Math]::Max($score, 80)
        }

        Add-CcDirRootCandidate $candidates $anchorRoot ([int]$stats.Files) $score $codingFiles (Test-CcDirSourceRootName $anchorRoot) $requiredRoot $true
    }

    foreach ($family in @($SelectedFamilies)) {
        $familyPath = Normalize-CcDirRulePath ([string]$family.Path)
        if ([string]::IsNullOrWhiteSpace($familyPath) -or (Test-CcDirArtifactPath $familyPath)) {
            continue
        }

        $familyParent = Normalize-CcDirRulePath ([System.IO.Path]::GetDirectoryName($familyPath))
        if ([string]::IsNullOrWhiteSpace($familyParent)) {
            continue
        }

        $familyRoot = $familyParent.TrimEnd("/") + "/"
        $stats = Get-CcDirFileStatsForRoot $AllFiles $familyRoot
        if ([int]$stats.Files -eq 0) {
            continue
        }

        $codingFiles = [int]$stats.CodingFiles
        $headerFiles = [int]$stats.HeaderFiles
        $assetFiles = [int]$stats.AssetFiles
        $score = Get-CcDirRootCategoryScore $familyRoot $codingFiles $headerFiles $assetFiles
        if ($score -lt 0) {
            continue
        }

        Add-CcDirRootCandidate $candidates $familyRoot ([int]$stats.Files) $score $codingFiles (Test-CcDirSourceRootName $familyRoot) $true $true
    }

    $fileCount = @($AllFiles).Count
    $limit = if ($fileCount -le 150) { 12 } elseif ($fileCount -le 800) { 22 } else { 30 }
    $sortedRoots = @(Select-CcDirRootRecords @($candidates.Values) $limit $SelectedAnchors)

    return @($sortedRoots |
        Sort-Object @{ Expression = { -[int]$_.Score } }, @{ Expression = { -[int]$_.CodingFiles } }, @{ Expression = { if ($_.SourceRootMatch) { 0 } else { 1 } } }, @{ Expression = { [int]$_.Depth } }, @{ Expression = { $_.Path } } |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.Path
                Role = $_.Role
                Files = [int]$_.Files
            }
        })
}

function Get-CcDirManifestHashes {
    param([System.IO.FileInfo[]]$AllFiles)

    $hashes = @{}
    foreach ($file in @($AllFiles)) {
        $relative = Get-CcDirRelativePath $file
        if (Test-CcDirBuildFile $relative) {
            $hashes[$relative] = Get-CcDirFileHash $file
        }
    }

    return $hashes
}

function Get-CcDirFileListFingerprint {
    param([System.IO.FileInfo[]]$AllFiles)

    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($file in @($AllFiles)) {
        [void]$paths.Add((Get-CcDirRelativePath $file).ToLowerInvariant())
    }

    $ordered = [string[]]($paths.ToArray() | Sort-Object)
    $text = [string]::Join("`n", $ordered)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha.ComputeHash($bytes)
        return -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-CcDirLanguageCounts {
    param([object[]]$FileRecords)

    $counts = @{}
    foreach ($record in @($FileRecords)) {
        $kind = [string]$record.Kind
        if ([string]::IsNullOrWhiteSpace($kind)) {
            continue
        }

        if (-not $counts.ContainsKey($kind)) { $counts[$kind] = 0 }
        $counts[$kind]++
    }

    return $counts
}

function Get-CcDirStackLabels {
    param([object[]]$FileRecords)

    $stacks = New-Object System.Collections.Generic.List[string]
    $paths = @($FileRecords | ForEach-Object { ([string]$_.Path).ToLowerInvariant() })
    $kinds = @($FileRecords | ForEach-Object { [string]$_.Kind } | Select-Object -Unique)

    if ($paths -contains "cargo.toml" -or $kinds -contains "rust") { [void]$stacks.Add("Rust") }
    if ($paths -contains "package.json" -or $kinds -contains "typescript" -or $kinds -contains "typescript-react" -or $kinds -contains "javascript") { [void]$stacks.Add("JavaScript/TypeScript") }
    if ($paths -contains "pom.xml" -or $paths -contains "build.gradle" -or $kinds -contains "java" -or $kinds -contains "kotlin") { [void]$stacks.Add("Java/Kotlin") }
    if ($paths -contains "go.mod" -or $kinds -contains "go") { [void]$stacks.Add("Go") }
    if ($paths -contains "pyproject.toml" -or $paths -contains "setup.py" -or $kinds -contains "python") { [void]$stacks.Add("Python") }
    if ($paths -contains "cmakelists.txt" -or $kinds -contains "cpp" -or $kinds -contains "c" -or $kinds -contains "cpp-header") { [void]$stacks.Add("C/C++") }
    if ($kinds -contains "csharp" -or $kinds -contains "csharp-project" -or $kinds -contains "avalonia-xaml") { [void]$stacks.Add(".NET/Avalonia") }
    if ($kinds -contains "shader") { [void]$stacks.Add("Shaders") }
    if ($kinds -contains "powershell") { [void]$stacks.Add("PowerShell") }

    return @($stacks.ToArray() | Select-Object -Unique)
}

function Get-CcDirFamilyPrefix {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ([string]::IsNullOrWhiteSpace($name)) {
        return ""
    }

    $segments = @($name -split '[._-]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($segments.Count -gt 1 -and $segments[0].Length -ge 3) {
        if ($segments[0].Equals("Cc", [System.StringComparison]::OrdinalIgnoreCase) -and $segments.Count -gt 1) {
            return "$($segments[0]).$($segments[1])"
        }

        return $segments[0]
    }

    $match = [regex]::Match($name, '^([A-Z][A-Za-z0-9]{3,}?)(?=[A-Z][a-z]|$)')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return ""
}

function Get-CcDirFamilyRecords {
    param([object[]]$FileRecords)

    $groups = @{}
    $genericPrefixes = @("App", "Base", "Core", "Local", "Project", "External", "Browser", "Chat", "Main", "Config", "Service", "Manager", "Controller", "View")
    foreach ($record in @($FileRecords)) {
        $kind = [string]$record.Kind
        if (-not (Test-CcDirFunctionExportKind $kind)) {
            continue
        }

        $relative = [string]$record.Path
        $dir = Normalize-CcDirRulePath ([System.IO.Path]::GetDirectoryName($relative))
        $ext = [System.IO.Path]::GetExtension($relative)
        $prefix = Get-CcDirFamilyPrefix $relative
        if ([string]::IsNullOrWhiteSpace($prefix) -or $prefix.Length -lt 3) {
            continue
        }

        if (@($genericPrefixes | Where-Object { $prefix.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            continue
        }

        $key = "$dir|$prefix|$ext"
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = New-Object System.Collections.Generic.List[string]
        }
        [void]$groups[$key].Add($relative)
    }

    $families = New-Object System.Collections.Generic.List[object]
    foreach ($key in @($groups.Keys | Sort-Object)) {
        $items = @($groups[$key].ToArray())
        if ($items.Count -lt 2) {
            continue
        }

        $parts = $key -split '\|'
        $dir = $parts[0]
        $prefix = $parts[1]
        $ext = $parts[2]
        if ([string]::IsNullOrWhiteSpace($dir)) {
            continue
        }

        $path = "$dir/$prefix*$ext"
        $score = 40
        if ($prefix.StartsWith("Cc.", [System.StringComparison]::OrdinalIgnoreCase)) {
            $score = 100
        }
        elseif ($prefix -match '(?i)(Workbench|Skillbook|MainWindow|CodeEditor|ProjectGraph|ProjectTree|LocalLlmCatalog|RenderControl)') {
            $score = 80
        }

        $rolePrefix = Convert-CcDirTokenRole $prefix $prefix
        [void]$families.Add([pscustomobject]@{
            Path = $path
            Role = "split $rolePrefix family"
            Exports = "wildcard-function,find"
            Score = $score
        })
    }

    return @($families.ToArray() |
        Sort-Object @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } } |
        Select-Object -First 8 |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.Path
                Role = $_.Role
                Exports = $_.Exports
            }
        })
}

function New-CcDirProjectProfile {
    param([System.IO.FileInfo[]]$AllFiles)

    $fileRecords = @($AllFiles | ForEach-Object { Get-CcDirFileRecord $_ })
    $visibleCount = @($AllFiles).Count
    $limit = if ($visibleCount -le 60) { $visibleCount } elseif ($visibleCount -le 800) { 28 } else { 32 }
    $anchorThreshold = if ($visibleCount -le 60) { 0 } else { 40 }
    $preliminaryRootRecords = @(Get-CcDirRootRecords $AllFiles @())
    $anchorFiles = @(Select-CcDirAnchorFiles $fileRecords $limit $anchorThreshold $preliminaryRootRecords)
    $familyRecords = @(Get-CcDirFamilyRecords $fileRecords)
    $rootRecords = @(Get-CcDirRootRecords $AllFiles $anchorFiles $familyRecords)
    $anchorFiles = @(Select-CcDirAnchorFiles $fileRecords $limit $anchorThreshold $rootRecords)
    $rootRecords = @(Get-CcDirRootRecords $AllFiles $anchorFiles $familyRecords)

    $rootCounts = @{}
    foreach ($root in $rootRecords) {
        $rootCounts[$root.Path] = [int]$root.Files
    }

    return [pscustomobject]@{
        SchemaVersion = 1
        GeneratedUtc = [DateTime]::UtcNow.ToString("O")
        ProjectRoot = (Get-Location).Path
        ProfileSource = "ccDir.ps1"
        FileRuleFingerprint = (($ExcludeDirs + $ExcludeFileNames + $ExcludeFileExtensions) -join "|").GetHashCode()
        FileListFingerprint = Get-CcDirFileListFingerprint $AllFiles
        VisibleFileCount = $visibleCount
        RootCounts = $rootCounts
        MajorManifestHashes = Get-CcDirManifestHashes $AllFiles
        Languages = Get-CcDirLanguageCounts $fileRecords
        Stacks = Get-CcDirStackLabels $fileRecords
        Roots = $rootRecords
        Files = @($anchorFiles | ForEach-Object {
            [pscustomobject]@{
                Path = $_.Path
                Kind = $_.Kind
                Role = $_.Role
                Exports = $_.Exports
                Score = [int]$_.Score
            }
        })
        Families = $familyRecords
    }
}

function Read-CcDirProjectProfile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    }
    catch {
        Write-Host "DIR profile could not be read, using in-memory profile: $($_.Exception.Message)" -ForegroundColor Yellow
        return $null
    }
}

function Test-CcDirProjectProfileFresh {
    param($ProfileObject, [System.IO.FileInfo[]]$AllFiles)

    if ($null -eq $ProfileObject) {
        return $false
    }

    $propertyNames = @($ProfileObject.PSObject.Properties.Name)
    foreach ($required in @("SchemaVersion", "ProjectRoot", "FileRuleFingerprint", "FileListFingerprint", "VisibleFileCount")) {
        if ($propertyNames -notcontains $required) {
            return $false
        }
    }

    if ([int]$ProfileObject.SchemaVersion -ne 1) {
        return $false
    }

    $currentRoot = (Get-Location).Path
    if (-not ([string]$ProfileObject.ProjectRoot).Equals($currentRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $currentRuleFingerprint = (($ExcludeDirs + $ExcludeFileNames + $ExcludeFileExtensions) -join "|").GetHashCode()
    if ([int]$ProfileObject.FileRuleFingerprint -ne [int]$currentRuleFingerprint) {
        return $false
    }

    if ([int]$ProfileObject.VisibleFileCount -ne @($AllFiles).Count) {
        return $false
    }

    $currentFileListFingerprint = Get-CcDirFileListFingerprint $AllFiles
    return ([string]$ProfileObject.FileListFingerprint).Equals($currentFileListFingerprint, [System.StringComparison]::OrdinalIgnoreCase)
}

function Save-CcDirProjectProfile {
    param($ProfileObject, [string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = ".ccDirProfile.json"
    }

    $pathText = $Path -replace '/', [System.IO.Path]::DirectorySeparatorChar
    $fullPath = if ([System.IO.Path]::IsPathRooted($pathText)) {
        [System.IO.Path]::GetFullPath($pathText)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $pathText))
    }
    $parent = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }

    $json = $ProfileObject | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($fullPath, $json + [Environment]::NewLine, $utf8NoBom)
    return $fullPath
}

function Get-CcDirProfilePath {
    if (-not [string]::IsNullOrWhiteSpace($ProfileFile)) {
        return $ProfileFile
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CC_WORKBENCH_DIR_PROFILE_PATH)) {
        return $env:CC_WORKBENCH_DIR_PROFILE_PATH
    }

    return ".ccDirProfile.json"
}

function Get-CcDirVisibleFiles {
    param($ScopeInfo, [System.IO.FileInfo[]]$AllFiles, $ProjectProfile = $null)

    $nonArtifactFiles = @($AllFiles | Where-Object { -not (Test-CcDirArtifactPath (Get-CcDirRelativePath $_)) })

    if ($ScopeInfo.Scoped) {
        return @($nonArtifactFiles)
    }

    if ($null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Files") {
        $root = (Get-Location).Path
        $profileFiles = @($ProjectProfile.Files | ForEach-Object {
            $path = [string]$_.Path
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $full = Join-Path $root ($path -replace '/', [System.IO.Path]::DirectorySeparatorChar)
                if (Test-Path -LiteralPath $full -PathType Leaf) {
                    Get-Item -LiteralPath $full
                }
            }
        })
        if ($profileFiles.Count -gt 0) {
            return @($profileFiles)
        }
    }

    if ($nonArtifactFiles.Count -le 60) {
        return @($nonArtifactFiles)
    }

    $limit = if ($nonArtifactFiles.Count -gt 3000) { 40 } elseif ($nonArtifactFiles.Count -gt 800) { 40 } else { 60 }
    $visible = @($nonArtifactFiles |
        ForEach-Object {
            $path = Get-CcDirRelativePath $_
            $kind = Get-CcDirKind $path
            [pscustomobject]@{ File = $_; Score = Get-CcDirAnchorScore $path $kind; Path = $path }
        } |
        Where-Object { [int]$_.Score -gt 0 } |
        Sort-Object @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } } |
        Select-Object -First $limit |
        ForEach-Object { $_.File })

    if ($visible.Count -eq 0) {
        return @($AllFiles | Select-Object -First ([Math]::Min(40, $AllFiles.Count)))
    }

    return @($visible)
}

function Add-CcDirRoots {
    param($ScopeInfo, [System.IO.FileInfo[]]$AllFiles, $ProjectProfile = $null)

    Add-Line "ROOTS:"

    if (-not $ScopeInfo.Scoped -and $null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Roots") {
        $count = 0
        foreach ($root in @($ProjectProfile.Roots)) {
            $path = [string]$root.Path
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }

            $role = if ($root.PSObject.Properties.Name -contains "Role" -and -not [string]::IsNullOrWhiteSpace([string]$root.Role)) { [string]$root.Role } else { Get-CcDirRootRole $path }
            $files = if ($root.PSObject.Properties.Name -contains "Files") { [int]$root.Files } else { 0 }
            Add-Line ("ROOT path={0} role={1} files={2}" -f (Format-CcDirField $path), (Format-CcDirField $role), $files)
            $count++
        }

        if ($count -eq 0) {
            Add-Line "(none)"
        }

        Add-Line ""
        return
    }

    $rootMap = @{}
    foreach ($file in @($AllFiles)) {
        $relative = Get-CcDirRelativePath $file
        $parts = $relative -split '/'
        if ($ScopeInfo.Scoped) {
            $scope = $ScopeInfo.Relative
            $rest = if ($relative.StartsWith($scope, [System.StringComparison]::OrdinalIgnoreCase)) {
                $relative.Substring($scope.Length)
            }
            else {
                ""
            }

            $restParts = $rest -split '/'
            if ($restParts.Count -gt 1 -and -not [string]::IsNullOrWhiteSpace($restParts[0])) {
                $rootPathText = $scope + $restParts[0] + "/"
                if (-not $rootMap.ContainsKey($rootPathText)) { $rootMap[$rootPathText] = 0 }
                $rootMap[$rootPathText]++
            }
            continue
        }

        if ($parts.Count -gt 1 -and -not [string]::IsNullOrWhiteSpace($parts[0])) {
            $rootPathText = $parts[0] + "/"
            if (-not $rootMap.ContainsKey($rootPathText)) { $rootMap[$rootPathText] = 0 }
            $rootMap[$rootPathText]++
        }
    }

    foreach ($key in @($rootMap.Keys | Sort-Object)) {
        Add-Line ("ROOT path={0} role={1} files={2}" -f (Format-CcDirField $key), (Format-CcDirField (Get-CcDirRootRole $key)), $rootMap[$key])
    }

    if ($rootMap.Count -eq 0) {
        Add-Line "(none)"
    }

    Add-Line ""
}

function Add-CcDirFileRecords {
    param($ScopeInfo, [System.IO.FileInfo[]]$Files)

    Add-Line "FILES:"
    foreach ($file in @($Files)) {
        $relative = Get-CcDirRelativePath $file
        $kind = Get-CcDirKind $relative
        $tier = Get-CcDirTier $relative $kind $ScopeInfo.Scoped
        $role = Get-CcDirRole $relative $kind
        $exports = Get-CcDirExports $kind

        $line = "FILE path=$(Format-CcDirField $relative) tier=$tier kind=$(Format-CcDirField $kind) role=$(Format-CcDirField $role) exports=$(Format-CcDirField $exports)"

        $symbols = @()
        if ((Test-CcDirFunctionExportKind $kind) -or @("avalonia-xaml", "xaml") -contains $kind) {
            $symbols = @(Get-CcDirSymbols $file)
            if ($symbols.Count -gt 0) {
                $symbolText = Join-CcDirCompactHintList $symbols 8 160
                if (-not [string]::IsNullOrWhiteSpace($symbolText)) {
                    $line += " symbols=$(Format-CcDirField $symbolText)"
                }
            }
        }

        if ($ScopeInfo.Scoped -and $symbols.Count -gt 0) {
            $findHints = @(Get-CcDirFindHints $relative $symbols)
            if ($findHints.Count -gt 0) {
                $findText = Join-CcDirCompactHintList $findHints 8 160
                if (-not [string]::IsNullOrWhiteSpace($findText)) {
                    $line += " find=$(Format-CcDirField $findText)"
                }
            }
        }

        Add-Line $line
    }

    if ($Files.Count -eq 0) {
        Add-Line "(none)"
    }

    Add-Line ""
}

function Add-CcDirFamilies {
    param([System.IO.FileInfo[]]$Files, $ScopeInfo = $null, $ProjectProfile = $null)

    Add-Line "FAMILIES:"
    if ($null -ne $ScopeInfo -and -not $ScopeInfo.Scoped -and $null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Families") {
        $count = 0
        foreach ($family in @($ProjectProfile.Families)) {
            $path = [string]$family.Path
            if ([string]::IsNullOrWhiteSpace($path)) {
                continue
            }

            $role = if ($family.PSObject.Properties.Name -contains "Role" -and -not [string]::IsNullOrWhiteSpace([string]$family.Role)) { [string]$family.Role } else { "split family" }
            $exports = if ($family.PSObject.Properties.Name -contains "Exports" -and -not [string]::IsNullOrWhiteSpace([string]$family.Exports)) { [string]$family.Exports } else { "wildcard-function,find" }
            Add-Line ("FAMILY path={0} role={1} exports={2}" -f (Format-CcDirField $path), (Format-CcDirField $role), (Format-CcDirField $exports))
            $count++
        }

        if ($count -eq 0) {
            Add-Line "(none)"
        }

        Add-Line ""
        return
    }

    $groups = @{}
    foreach ($file in @($Files)) {
        $relative = Get-CcDirRelativePath $file
        $kind = Get-CcDirKind $relative
        if (-not (Test-CcDirFunctionExportKind $kind)) {
            continue
        }

        $dir = Normalize-CcDirRulePath ([System.IO.Path]::GetDirectoryName($relative))
        $name = [System.IO.Path]::GetFileNameWithoutExtension($relative)
        $ext = [System.IO.Path]::GetExtension($relative)
        $segments = @($name -split '\.')
        $prefix = if ($segments.Count -ge 2 -and $segments[0].Equals("Cc", [System.StringComparison]::OrdinalIgnoreCase)) {
            "$($segments[0]).$($segments[1])"
        }
        elseif ($segments.Count -ge 1 -and $name.Contains(".")) {
            $segments[0]
        }
        else {
            ""
        }
        if ([string]::IsNullOrWhiteSpace($prefix)) {
            continue
        }

        $key = "$dir|$prefix|$ext"
        if (-not $groups.ContainsKey($key)) {
            $groups[$key] = New-Object System.Collections.Generic.List[string]
        }
        [void]$groups[$key].Add($relative)
    }

    $count = 0
    foreach ($key in @($groups.Keys | Sort-Object)) {
        $items = @($groups[$key].ToArray())
        if ($items.Count -lt 2) {
            continue
        }

        $parts = $key -split '\|'
        $dir = $parts[0]
        $prefix = $parts[1]
        $ext = $parts[2]
        $path = if ([string]::IsNullOrWhiteSpace($dir)) { "$prefix*$ext" } else { "$dir/$prefix*$ext" }
        $rolePrefix = Convert-CcDirTokenRole $prefix $prefix
        Add-Line ("FAMILY path={0} role={1} exports={2}" -f (Format-CcDirField $path), (Format-CcDirField "split $rolePrefix family"), (Format-CcDirField "wildcard-function,find"))
        $count++
    }

    if ($count -eq 0) {
        Add-Line "(none)"
    }

    Add-Line ""
}

function Get-CcDirExampleRootScore {
    param($Root)

    $path = [string]$Root.Path
    if (Test-CcDirSourceRootName $path) { return 100 }
    if (Test-CcDirIncludeRootName $path) { return 90 }
    if (Test-CcDirAssetRootName $path) { return 85 }
    if (Test-CcDirToolRootName $path) { return 70 }
    if (Test-CcDirTestRootName $path) { return 45 }
    if (Test-CcDirExampleRootName $path) { return 35 }
    if (Test-CcDirCiPackagingRootName $path) { return 25 }
    if (Test-CcDirDocsRootName $path) { return 10 }
    return 50
}

function Get-CcDirFindTokenFromPath {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $generic = @("app", "application", "build", "cmakelists", "common", "config", "index", "lib", "main", "package", "program", "readme", "route", "server", "src", "tsconfig")
    $base = [System.IO.Path]::GetFileNameWithoutExtension($relative)
    $candidates = New-Object System.Collections.Generic.List[string]
    [void]$candidates.Add($base)
    $parts = @($relative -split "/" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    for ($index = $parts.Count - 2; $index -ge 0 -and $candidates.Count -lt 4; $index--) {
        [void]$candidates.Add($parts[$index])
    }

    foreach ($candidate in @($candidates.ToArray())) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $clean = ($candidate -replace '[^A-Za-z0-9_]+', '')
        if ($clean.Length -ge 4 -and $clean.Length -le 40 -and -not (@($generic | Where-Object { $clean.Equals($_, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0)) {
            return $clean
        }
    }

    return "Main"
}

function Get-CcDirExampleFilePaths {
    param($ProjectProfile, [System.IO.FileInfo[]]$VisibleFiles)

    $visiblePathMap = @{}
    foreach ($file in @($VisibleFiles)) {
        $visiblePathMap[(Get-CcDirRelativePath $file).ToLowerInvariant()] = Get-CcDirRelativePath $file
    }

    $records = if ($null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Files") {
        @($ProjectProfile.Files)
    }
    else {
        @($VisibleFiles | ForEach-Object { Get-CcDirFileRecord $_ })
    }

    $ordered = @($records |
        Sort-Object @{ Expression = { if (Test-CcDirBuildFile ([string]$_.Path)) { 1 } else { 0 } } }, @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } })
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($record in $ordered) {
        if ($paths.Count -ge 3) {
            break
        }

        $path = [string]$record.Path
        $key = $path.ToLowerInvariant()
        if ($visiblePathMap.ContainsKey($key) -and -not $paths.Contains($visiblePathMap[$key])) {
            [void]$paths.Add($visiblePathMap[$key])
        }
    }

    return @($paths.ToArray())
}

function Add-CcDirManifest {
    param($ScopeInfo, [System.IO.FileInfo[]]$AllFiles, [System.IO.FileInfo[]]$VisibleFiles, $ProjectProfile = $null)

    Add-Line "CC-DIR-MANIFEST-V2"
    Add-Line "LOD: $($ScopeInfo.Label)"
    if ($ScopeInfo.Scoped) {
        Add-Line "SCOPE: $($ScopeInfo.Relative)"
    }
    Add-Line ""
    Add-Line "OUTPUT_CONTRACT:"
    Add-Line "Return exact source request lines, FIND, EXPAND, then END."
    Add-Line "Copy paths exactly from FILE path values. Copy EXPAND paths exactly from ROOT path values."
    Add-Line "Do not solve or patch."
    Add-Line "Do not mix modes: final source request lines, exactly one FIND line, or exactly one EXPAND."
    Add-Line ""
    Add-Line "REQUEST_LANGUAGE:"
    Add-Line "<relative file path>"
    Add-Line "FUNCTION <relative file path> :: <symbol>"
    Add-Line "FUNCTION <relative wildcard path> :: <symbol>"
    Add-Line "FUNC: <symbol>"
    Add-Line "FIND: <text>"
    Add-Line "EXPAND: <relative directory path>"
    Add-Line "END"
    Add-Line ""
    Add-CcDirRoots $ScopeInfo $AllFiles $ProjectProfile
    Add-CcDirFileRecords $ScopeInfo $VisibleFiles
    Add-CcDirFamilies $VisibleFiles $ScopeInfo $ProjectProfile
    Add-Line "VALID_OUTPUT_EXAMPLES:"
    if (-not $ScopeInfo.Scoped) {
        $allRoots = if ($null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Roots") {
            @($ProjectProfile.Roots |
                Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Path) } |
                Sort-Object @{ Expression = { -(Get-CcDirExampleRootScore $_) } }, @{ Expression = { -[int]$_.Files } }, @{ Expression = { $_.Path } } |
                ForEach-Object { [string]$_.Path })
        }
        else {
            @($AllFiles | ForEach-Object {
                $relative = Get-CcDirRelativePath $_
                $parts = $relative -split '/'
                if ($parts.Count -gt 1) { $parts[0] + "/" }
            } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
        }
        $preferredRoot = ($allRoots | Select-Object -First 1)
        if ($null -ne $preferredRoot -and -not [string]::IsNullOrWhiteSpace([string]$preferredRoot)) {
            Add-Line "EXPAND: $preferredRoot"
            Add-Line "END"
            Add-Line ""
        }
    }

    $findToken = "Main"
    if ($null -ne $ProjectProfile -and $ProjectProfile.PSObject.Properties.Name -contains "Files") {
        $firstRoleFile = @($ProjectProfile.Files |
            Where-Object { [int]$_.Score -ge 40 -and -not (Test-CcDirBuildFile ([string]$_.Path)) } |
            Sort-Object @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } } |
            Select-Object -First 1)
        if ($firstRoleFile.Count -gt 0) {
            $findToken = Get-CcDirFindTokenFromPath ([string]$firstRoleFile[0].Path)
        }
    }
    Add-Line "FIND: $findToken"
    Add-Line "END"
    Add-Line ""

    $examplePaths = @(Get-CcDirExampleFilePaths $ProjectProfile $VisibleFiles)
    foreach ($path in $examplePaths) {
        Add-Line $path
    }
    if ($examplePaths.Count -gt 0) {
        Add-Line "END"
        Add-Line ""
    }
    Add-Line "FINAL_CHECK:"
    Add-Line "Every file path must exactly match a FILE path value above."
    Add-Line "Every EXPAND path must exactly match a ROOT path value above."
    Add-Line "Every wildcard FUNCTION path must exactly match a FAMILY path value above."
    Add-Line "FIND lines may repeat before END; do not mix FIND with source or EXPAND lines."
    Add-Line "EXPAND must be the only request line before END."
    Add-Line "FUNC may appear only with final source request lines."
}
