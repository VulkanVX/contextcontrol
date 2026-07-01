# CC-DESC: Helper functions dot-sourced by lib/Cc.Dir.Export.ps1.

function Get-CcDirChildItems {
    param([string]$Dir)

    try {
        return @(Get-ChildItem -LiteralPath $Dir -Force -ErrorAction SilentlyContinue |
            Where-Object { -not (Is-ExcludedItem $_) } |
            Where-Object { if ((Get-CcDirRelativePath $_).Contains("/")) { $true } else { Should-Include-TopLevelItem $_ } } |
            Sort-Object @{ Expression = { -not $_.PSIsContainer } }, Name)
    }
    catch {
        return @()
    }
}

function Get-CcDirIncludedFiles {
    param([string]$StartDir)

    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    function Visit-CcDir {
        param([string]$Dir, [int]$Depth)

        if ($Depth -ge $MaxDepth) {
            return
        }

        foreach ($item in Get-CcDirChildItems $Dir) {
            if ($item.PSIsContainer) {
                Visit-CcDir $item.FullName ($Depth + 1)
            }
            else {
                [void]$files.Add($item)
            }
        }
    }

    Visit-CcDir $StartDir 0
    return @($files.ToArray() | Sort-Object @{ Expression = { Get-CcDirRelativePath $_ } })
}

function Resolve-CcDirScope {
    if ($Lod -ne 0 -and $Lod -ne 1) {
        throw "Unsupported DIR LOD: $Lod. Use -Lod 0 or -Lod 1."
    }

    if ($Lod -eq 0) {
        return [pscustomobject]@{
            FullPath = (Get-Location).Path
            Relative = ""
            Label = "L0_GLOBAL"
            Scoped = $false
        }
    }

    $clean = Normalize-CcDirRulePath $Scope
    if ([string]::IsNullOrWhiteSpace($clean)) {
        throw "DIR LOD 1 requires -Scope <relative-directory>."
    }

    if ([System.IO.Path]::IsPathRooted(($clean -replace '/', [System.IO.Path]::DirectorySeparatorChar))) {
        throw "DIR scope must be relative to the project root: $Scope"
    }

    $root = (Get-Location).Path
    $full = [System.IO.Path]::GetFullPath((Join-Path $root ($clean -replace '/', [System.IO.Path]::DirectorySeparatorChar)))
    if (-not $full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "DIR scope escapes the project root: $Scope"
    }

    if (-not (Test-Path -LiteralPath $full -PathType Container)) {
        throw "DIR scope was not found: $clean"
    }

    return [pscustomobject]@{
        FullPath = $full
        Relative = $clean.TrimEnd("/") + "/"
        Label = "L1_SCOPED"
        Scoped = $true
    }
}

function Get-CcDirFileHash {
    param([System.IO.FileInfo]$File)

    try {
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $File.FullName -ErrorAction Stop).Hash.ToLowerInvariant()
    }
    catch {
        return ""
    }
}

function Get-CcDirFileRecord {
    param([System.IO.FileInfo]$File)

    $relative = Get-CcDirRelativePath $File
    $kind = Get-CcDirKind $relative
    $score = Get-CcDirAnchorScore $relative $kind
    return [pscustomobject]@{
        Path = $relative
        Kind = $kind
        Role = Get-CcDirRole $relative $kind
        Exports = Get-CcDirExports $kind
        Score = $score
    }
}

function Add-CcDirRootCandidate {
    param(
        [hashtable]$Map,
        [string]$Path,
        [int]$Files,
        [int]$Score,
        [int]$CodingFiles = 0,
        [bool]$SourceRootMatch = $false,
        [bool]$RequiredRoot = $false,
        [bool]$ImpliedRoot = $false
    )

    $clean = Normalize-CcDirRulePath $Path
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return
    }

    $clean = $clean.TrimEnd("/") + "/"
    if ($Files -le 0) {
        return
    }

    if ($Map.ContainsKey($clean)) {
        $existing = $Map[$clean]
        $existing.RequiredRoot = [bool]$existing.RequiredRoot -or $RequiredRoot
        $existing.ImpliedRoot = [bool]$existing.ImpliedRoot -or $ImpliedRoot
        $existing.SourceRootMatch = [bool]$existing.SourceRootMatch -or $SourceRootMatch
        if ([int]$existing.Score -lt $Score) {
            $existing.Role = Get-CcDirRootRole $clean
            $existing.Files = $Files
            $existing.Score = $Score
            $existing.CodingFiles = $CodingFiles
            $existing.Depth = Get-CcDirPathDepth $clean
        }
        elseif ([int]$existing.CodingFiles -lt $CodingFiles) {
            $existing.CodingFiles = $CodingFiles
        }
        return
    }

    if (-not $Map.ContainsKey($clean)) {
        $Map[$clean] = [pscustomobject]@{
            Path = $clean
            Role = Get-CcDirRootRole $clean
            Files = $Files
            Score = $Score
            CodingFiles = $CodingFiles
            SourceRootMatch = $SourceRootMatch
            RequiredRoot = $RequiredRoot
            ImpliedRoot = $ImpliedRoot
            Depth = Get-CcDirPathDepth $clean
        }
    }
}

function Add-CcDirCount {
    param([hashtable]$Map, [string]$Key, [int]$Amount = 1)

    if ([string]::IsNullOrWhiteSpace($Key)) {
        return
    }

    if (-not $Map.ContainsKey($Key)) { $Map[$Key] = 0 }
    $Map[$Key] = [int]$Map[$Key] + $Amount
}

function Get-CcDirMapCount {
    param([hashtable]$Map, [string]$Key)

    if ($Map.ContainsKey($Key)) {
        return [int]$Map[$Key]
    }

    return 0
}

function Get-CcDirFileStatsForRoot {
    param(
        [System.IO.FileInfo[]]$AllFiles,
        [string]$RootPath
    )

    $root = (Normalize-CcDirRulePath $RootPath).TrimEnd("/") + "/"
    $files = 0
    $codingFiles = 0
    $headerFiles = 0
    $assetFiles = 0

    foreach ($file in @($AllFiles)) {
        $relative = Get-CcDirRelativePath $file
        if (-not $relative.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $files++
        $kind = Get-CcDirKind $relative
        if (Test-CcDirCodingKind $kind) { $codingFiles++ }
        if (Test-CcDirHeaderKind $kind) { $headerFiles++ }
        if (Test-CcDirCodeLikeAssetKind $kind) { $assetFiles++ }
    }

    return [pscustomobject]@{
        Files = $files
        CodingFiles = $codingFiles
        HeaderFiles = $headerFiles
        AssetFiles = $assetFiles
    }
}

function Get-CcDirRootCategoryScore {
    param(
        [string]$Path,
        [int]$CodingFiles,
        [int]$HeaderFiles,
        [int]$AssetFiles
    )

    if (Test-CcDirArtifactPath $Path) { return -999 }
    if (Test-CcDirTestRootName $Path) { return 45 }
    if (Test-CcDirExampleRootName $Path) { return 35 }
    if (Test-CcDirCiPackagingRootName $Path) { return 25 }
    if (Test-CcDirDocsRootName $Path) { return 10 }
    if ((Test-CcDirIncludeRootName $Path) -or $HeaderFiles -gt 0 -and (Test-CcDirIncludeRootName $Path)) { return 90 }
    if ((Test-CcDirAssetRootName $Path) -or $AssetFiles -gt 0 -and $CodingFiles -eq $AssetFiles) { return 85 }
    if (Test-CcDirToolRootName $Path) { return 70 }
    if ((Test-CcDirSourceRootName $Path) -and $CodingFiles -gt 0) { return 100 }
    if ($CodingFiles -gt 0) { return 100 }
    return 0
}

function Add-CcDirExplicitRootCandidates {
    param(
        [hashtable]$Candidates,
        [System.IO.FileInfo[]]$AllFiles,
        [string[]]$Roots,
        [int]$MinimumScore,
        [bool]$RequiredRoot,
        [bool]$ImpliedRoot
    )

    foreach ($root in @($Roots)) {
        $rootPath = (Normalize-CcDirRulePath $root).TrimEnd("/") + "/"
        if ([string]::IsNullOrWhiteSpace($rootPath.Trim("/")) -or (Test-CcDirArtifactPath $rootPath)) {
            continue
        }

        $stats = Get-CcDirFileStatsForRoot $AllFiles $rootPath
        if ([int]$stats.Files -eq 0) {
            continue
        }

        $codingFiles = [int]$stats.CodingFiles
        $headerFiles = [int]$stats.HeaderFiles
        $assetFiles = [int]$stats.AssetFiles
        $score = [Math]::Max($MinimumScore, (Get-CcDirRootCategoryScore $rootPath $codingFiles $headerFiles $assetFiles))
        Add-CcDirRootCandidate $Candidates $rootPath ([int]$stats.Files) $score $codingFiles (Test-CcDirSourceRootName $rootPath) $RequiredRoot $ImpliedRoot
    }
}

function Get-CcDirRootBucket {
    param($Root)

    $path = [string]$Root.Path
    if ([bool]$Root.RequiredRoot -or (Test-CcDirAssetRootName $path)) { return "asset" }
    if ((Test-CcDirIncludeRootName $path) -or [int]$Root.Score -eq 90) { return "include" }
    if (Test-CcDirToolRootName $path) { return "tool" }
    if (Test-CcDirTestRootName $path) { return "test" }
    if (Test-CcDirExampleRootName $path) { return "example" }
    if ((Test-CcDirCiPackagingRootName $path) -or (Test-CcDirDocsRootName $path)) { return "support" }
    if ([bool]$Root.ImpliedRoot) { return "implied" }
    if ([int]$Root.Score -ge 100 -and [int]$Root.Depth -gt 1) { return "source-child" }
    if ([int]$Root.Score -ge 100) { return "source-primary" }
    return "other"
}

function Sort-CcDirRootCandidates {
    param([object[]]$Roots)

    return @($Roots |
        Sort-Object @{ Expression = { -[int]$_.Score } }, @{ Expression = { -[int]$_.CodingFiles } }, @{ Expression = { if ([bool]$_.SourceRootMatch) { 0 } else { 1 } } }, @{ Expression = { [int]$_.Depth } }, @{ Expression = { $_.Path } })
}

function Add-CcDirRootSelection {
    param(
        [System.Collections.Generic.List[object]]$Selection,
        [object[]]$Candidates,
        [int]$MaxToAdd,
        [int]$Limit,
        [hashtable]$Suppressed = $null
    )

    if ($MaxToAdd -le 0 -or $Selection.Count -ge $Limit) {
        return
    }

    $added = 0
    foreach ($candidate in @(Sort-CcDirRootCandidates $Candidates)) {
        if ($Selection.Count -ge $Limit -or $added -ge $MaxToAdd) {
            break
        }

        $path = [string]$candidate.Path
        if ($null -ne $Suppressed -and $Suppressed.ContainsKey($path)) {
            continue
        }

        if (@($Selection.ToArray() | Where-Object { ([string]$_.Path).Equals($path, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
            continue
        }

        [void]$Selection.Add($candidate)
        $added++
    }
}

function Remove-CcDirRedundantAncestorRoots {
    param([object[]]$Roots)

    $selected = New-Object System.Collections.Generic.List[object]
    foreach ($root in @(Sort-CcDirRootCandidates $Roots)) {
        $path = [string]$root.Path
        $descendants = @($Roots | Where-Object {
            $childPath = [string]$_.Path
            -not $childPath.Equals($path, [System.StringComparison]::OrdinalIgnoreCase) -and
                $childPath.StartsWith($path, [System.StringComparison]::OrdinalIgnoreCase)
        })

        $isRequiredHighLevel = [bool]$root.RequiredRoot -or ([bool]$root.SourceRootMatch -and [int]$root.Depth -eq 1)
        if ($descendants.Count -ge 3 -and -not $isRequiredHighLevel) {
            continue
        }

        [void]$selected.Add($root)
    }

    return @($selected.ToArray())
}

function Test-CcDirRootCoversAnchorRoot {
    param([object[]]$Roots, [string]$AnchorRoot)

    $clean = Normalize-CcDirRulePath $AnchorRoot
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return $true
    }

    $clean = $clean.TrimEnd("/") + "/"
    $anchorDepth = Get-CcDirPathDepth $clean
    foreach ($root in @($Roots)) {
        $rootPath = ([string]$root.Path).TrimEnd("/") + "/"
        if ($clean.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $rootDepth = Get-CcDirPathDepth $rootPath
            if (($anchorDepth - $rootDepth) -le 1) {
                return $true
            }
        }
    }

    return $false
}

function Get-CcDirRootReplacementPriority {
    param($Root)

    if ([bool]$Root.RequiredRoot) {
        return 999
    }

    $bucket = Get-CcDirRootBucket $Root
    switch ($bucket) {
        "support" { return 0 }
        "example" { return 0 }
        "test" { return 0 }
        "other" { return 1 }
        "tool" { return 2 }
        "implied" { return 3 }
        "source-child" { return 4 }
        "source-primary" { return 5 }
        "include" { return 6 }
        "asset" { return 8 }
        default { return 7 }
    }
}

function Add-CcDirAnchorImpliedRootSelection {
    param(
        [System.Collections.Generic.List[object]]$Selection,
        $Candidate,
        [int]$Limit
    )

    if ($null -eq $Candidate -or $Limit -le 0) {
        return
    }

    $path = [string]$Candidate.Path
    if ([string]::IsNullOrWhiteSpace($path) -or (Test-CcDirArtifactPath $path)) {
        return
    }

    if (@($Selection.ToArray() | Where-Object { ([string]$_.Path).Equals($path, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) {
        return
    }

    if ($Selection.Count -lt $Limit) {
        [void]$Selection.Add($Candidate)
        return
    }

    $replaceable = @(
        for ($index = 0; $index -lt $Selection.Count; $index++) {
            $root = $Selection[$index]
            $priority = Get-CcDirRootReplacementPriority $root
            if ($priority -lt 999) {
                [pscustomobject]@{
                    Index = $index
                    Root = $root
                    Priority = $priority
                    Score = [int]$root.Score
                    CodingFiles = [int]$root.CodingFiles
                    Depth = [int]$root.Depth
                    Path = [string]$root.Path
                }
            }
        }
    )

    $replacement = @($replaceable |
        Sort-Object @{ Expression = { [int]$_.Priority } }, @{ Expression = { [int]$_.Score } }, @{ Expression = { [int]$_.CodingFiles } }, @{ Expression = { -[int]$_.Depth } }, @{ Expression = { $_.Path } } |
        Select-Object -First 1)
    if ($replacement.Count -eq 0) {
        return
    }

    $Selection.RemoveAt([int]$replacement[0].Index)
    [void]$Selection.Add($Candidate)
}

function Select-CcDirRootRecords {
    param(
        [object[]]$Candidates,
        [int]$Limit,
        [object[]]$SelectedAnchors = @()
    )

    $eligible = @(Sort-CcDirRootCandidates (@($Candidates | Where-Object { [int]$_.Score -ge 0 })))
    if ($Limit -le 0 -or $eligible.Count -eq 0) {
        return @()
    }

    $required = @($eligible | Where-Object { [bool]$_.RequiredRoot })
    $sourcePrimary = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "source-primary" })
    $sourceChild = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "source-child" })
    $include = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "include" })
    $asset = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "asset" -and -not [bool]$_.RequiredRoot })
    $implied = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "implied" })
    $tool = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "tool" })
    $support = @($eligible | Where-Object { @("test", "example", "support") -contains (Get-CcDirRootBucket $_) })
    $other = @($eligible | Where-Object { (Get-CcDirRootBucket $_) -eq "other" })

    $selection = New-Object System.Collections.Generic.List[object]
    Add-CcDirRootSelection $selection $required $Limit $Limit
    Add-CcDirRootSelection $selection $sourcePrimary ([Math]::Max(1, [Math]::Ceiling($Limit * 0.35))) $Limit
    Add-CcDirRootSelection $selection $include ([Math]::Max(1, [Math]::Ceiling($Limit * 0.20))) $Limit
    Add-CcDirRootSelection $selection $sourceChild ([Math]::Max(1, [Math]::Ceiling($Limit * 0.35))) $Limit
    Add-CcDirRootSelection $selection $asset $Limit $Limit
    Add-CcDirRootSelection $selection $implied $Limit $Limit
    Add-CcDirRootSelection $selection $tool ([Math]::Max(1, [Math]::Ceiling($Limit * 0.15))) $Limit
    Add-CcDirRootSelection $selection $support ([Math]::Max(1, [Math]::Floor($Limit * 0.10))) $Limit

    $suppressed = @{}
    $selectionAfterSuppression = @(Remove-CcDirRedundantAncestorRoots $selection.ToArray())
    foreach ($root in @($selection.ToArray())) {
        if (@($selectionAfterSuppression | Where-Object { ([string]$_.Path).Equals([string]$root.Path, [System.StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) {
            $suppressed[[string]$root.Path] = $true
        }
    }

    $selection = New-Object System.Collections.Generic.List[object]
    foreach ($root in $selectionAfterSuppression) {
        [void]$selection.Add($root)
    }

    foreach ($anchor in @($SelectedAnchors)) {
        if ((Get-CcDirAnchorCategory $anchor) -notin @("source", "asset", "support")) {
            continue
        }

        $anchorRoot = Get-CcDirStableAnchorRoot ([string]$anchor.Path)
        if ([string]::IsNullOrWhiteSpace($anchorRoot) -or (Test-CcDirRootCoversAnchorRoot $selection.ToArray() $anchorRoot)) {
            continue
        }

        $candidate = @($eligible | Where-Object { ([string]$_.Path).Equals($anchorRoot, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)
        if ($candidate.Count -gt 0) {
            Add-CcDirAnchorImpliedRootSelection $selection $candidate[0] $Limit
        }
    }

    Add-CcDirRootSelection $selection $tool $Limit $Limit $suppressed
    Add-CcDirRootSelection $selection $support $Limit $Limit $suppressed
    Add-CcDirRootSelection $selection $other $Limit $Limit $suppressed
    Add-CcDirRootSelection $selection $sourcePrimary $Limit $Limit $suppressed
    Add-CcDirRootSelection $selection $sourceChild $Limit $Limit $suppressed
    Add-CcDirRootSelection $selection $eligible $Limit $Limit $suppressed

    return @(Sort-CcDirRootCandidates $selection.ToArray() | Select-Object -First $Limit)
}

