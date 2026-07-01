# CC-DESC: Owns CC-REPLACE application runtime and file-apply orchestration.

function Apply-CcReplacePlanEntry {
    param(
        $Entry,
        $BackedUpMap,
        $Settings,
        [switch]$SkipVersionSnapshot
    )

    if ($Entry.Mode -eq "create_directory") {
        if ($DryRun) {
            Write-Host "Dry run create directory: $($Entry.TargetHeader)"
            return $true
        }

        if (-not (Test-Path -LiteralPath $Entry.TargetPath)) {
            New-Item -ItemType Directory -Path $Entry.TargetPath -Force | Out-Null
            Write-Host "Create directory: $($Entry.TargetHeader)"
        }
        else {
            Write-Host "Directory already exists: $($Entry.TargetHeader)"
        }

        return $true
    }

    switch ($Entry.Mode) {
        "function" { Write-Host "Replace function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_after_function" { Write-Host "Insert after function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_before_function" { Write-Host "Insert before function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "delete_function" { Write-Host "Delete function: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "replace_region" { Write-Host "Replace marker region: $($Entry.TargetHeader) :: $($Entry.Name)" }
        "insert_include" { Write-Host "Insert/include check: $($Entry.TargetHeader) :: $($Entry.PartLabel)" }
        "append_to_file" { Write-Host "Append to file: $($Entry.TargetHeader)" }
        "whole_file" {
            if ($Entry.CreatesFile) {
                Write-Host "Create whole file: $($Entry.TargetHeader)"
            }
            else {
                Write-Host "Replace whole file: $($Entry.TargetHeader)"
            }
        }
    }

    if ($DryRun) {
        Write-Host "Dry run: no write."
        return $true
    }

    if ($Entry.SkipWrite) {
        return $true
    }

    Ensure-ParentDirectory $Entry.TargetPath

    $useVersionCache = ([bool]$Settings.VersionCacheEnabled -and -not $NoBackup)
    if ($useVersionCache -and -not [bool]$Entry.IsDuplicate -and -not [bool]$Entry.CreatesFile) {
        Ensure-CcReplaceVersionBaseline $Settings $Entry.TargetHeader $Entry.TargetPath
    }

    Backup-TargetOnce $Entry.TargetPath $BackedUpMap
    Write-TargetLines $Entry.TargetPath $Entry.NewLines $Entry.Newline

    if ((-not $SkipVersionSnapshot) -and $useVersionCache -and -not [bool]$Entry.IsDuplicate) {
        Save-CcReplaceCurrentFileVersion $Settings $Entry.TargetHeader $Entry.TargetPath ("applied {0}" -f $Entry.Mode)
    }

    return $true
}

function New-CcReplacePlanSummary {
    param($Plan, $Settings = $null)

    if ($null -eq $Settings) {
        $Settings = Read-CcReplaceSettings
    }

    $items = @(Set-CcReplacePlanVersionHints $Plan $Settings)
    $effective = @($items | Where-Object { $_.IsEffective })
    $duplicates = @($items | Where-Object { $_.IsDuplicate })
    $fileEntries = @($items | Where-Object { -not $_.IsDirectory })
    $directoryEntries = @($items | Where-Object { $_.IsDirectory })
    $fileGroups = @($fileEntries | Group-Object TargetHeader)
    $bucketCounts = Get-CcReplaceBucketCounts $items

    $added = 0
    $removed = 0
    foreach ($entry in $effective | Where-Object { -not $_.IsDirectory }) {
        $added += [int]$entry.Added
        $removed += [int]$entry.Removed
    }

    $actions = @($items | ForEach-Object {
        $kind = Get-CcReplaceActionKind $_
        $bucket = Get-CcReplaceActionBucket $_
        $versionBefore = 0
        $versionAfter = 0

        if ($_.PSObject.Properties.Name -contains "VersionBefore") {
            $versionBefore = [int]$_.VersionBefore
        }

        if ($_.PSObject.Properties.Name -contains "VersionAfter") {
            $versionAfter = [int]$_.VersionAfter
        }

        [pscustomobject]@{
            Mode = $_.Mode
            Target = $_.TargetHeader
            Name = $_.Name
            Part = $_.PartLabel
            Action = $_.ActionLabel
            Kind = $kind
            Bucket = $bucket
            Added = [int]$_.Added
            Removed = [int]$_.Removed
            TotalLocAfter = [int]$_.TotalLocAfter
            IsDirectory = [bool]$_.IsDirectory
            IsDuplicate = [bool]$_.IsDuplicate
            IsEffective = [bool]$_.IsEffective
            CreatesFile = [bool]$_.CreatesFile
            CreatesDirectory = [bool]$_.CreatesDirectory
            DuplicateAction = [string]$_.DuplicateAction
            DuplicateTarget = [string]$_.DuplicateTarget
            VersionBefore = $versionBefore
            VersionAfter = $versionAfter
        }
    })

    return [pscustomobject]@{
        EffectiveCount = $effective.Count
        DuplicateCount = $duplicates.Count
        FileCount = $fileGroups.Count
        DirectoryCount = $directoryEntries.Count
        CreatedCount = [int]$bucketCounts.Created
        ChangedCount = [int]$bucketCounts.Changed
        RemovedCount = [int]$bucketCounts.Removed
        Added = $added
        Removed = $removed
        Actions = $actions
    }
}

function Invoke-CcReplacePlanText {
    param(
        [string]$Text,
        [switch]$Json
    )

    $blocks = @(Parse-CcReplaceBlocks $Text)
    if ($blocks.Count -eq 0) {
        $summary = [pscustomobject]@{
            EffectiveCount = 0
            DuplicateCount = 0
            FileCount = 0
            Added = 0
            Removed = 0
            Actions = @()
            Error = "No CC-REPLACE blocks found."
        }

        if ($Json) {
            $summary | ConvertTo-Json -Depth 8
        }
        else {
            Write-Host "No CC-REPLACE blocks found."
        }

        return
    }

    $settings = Read-CcReplaceSettings
    $plan = @(Analyze-CcReplaceBlocks $blocks)

    if ($Json) {
        New-CcReplacePlanSummary $plan $settings | ConvertTo-Json -Depth 8
        return
    }

    $effective = @($plan | Where-Object { $_.IsEffective })
    $duplicates = @($plan | Where-Object { $_.IsDuplicate })
    Write-CcReplaceCompactPlan $effective $duplicates $settings
}

function Invoke-CcReplacePlanFile {
    param(
        [string]$Path,
        [switch]$Json
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Input file not found: $Path"
    }

    $text = Read-TextFileAutoEncoding $Path
    Invoke-CcReplacePlanText $text -Json:$Json
}

function Convert-CcApplyDecision {
    param([string]$ApplyDecision)

    if ([string]::IsNullOrWhiteSpace($ApplyDecision)) {
        return ""
    }

    $clean = $ApplyDecision.Trim().ToLowerInvariant()
    if ($clean -eq "all") {
        return "all"
    }

    if ($clean -eq "effective" -or $clean -eq "effective_only") {
        return "effective_only"
    }

    throw "Unknown -Apply value '$ApplyDecision'. Use 'effective' or 'all'."
}

function Invoke-CcReplaceText {
    param(
        [string]$Text,
        [switch]$NoExitOnCancel,
        [string]$ApplyDecision = ""
    )

    Start-CcReplaceEditPhase

    try {
        $blocks = @(Parse-CcReplaceBlocks $Text)

        if ($blocks.Count -eq 0) {
            Write-Host "No CC-REPLACE blocks found."
            Write-Host ""
            Write-Host "Expected raw patch file format:"
            Write-Host "BEGIN CC-REPLACE"
            Write-Host "FILE: shaders/terrain/cube.frag"
            Write-Host "MODE: function"
            Write-Host "NAME: lookupMaterialOverlay"
            Write-Host "---"
            Write-Host "code here"
            Write-Host "END CC-REPLACE"
            return
        }

        $settings = Read-CcReplaceSettings
        $plan = @(Analyze-CcReplaceBlocks $blocks)
        $forcedDecision = Convert-CcApplyDecision $ApplyDecision
        $decision = if ($forcedDecision -ne "") {
            if ([bool]$settings.ShowPreflightStatistics) {
                $effectivePreview = @($plan | Where-Object { $_.IsEffective })
                $duplicatePreview = @($plan | Where-Object { $_.IsDuplicate })
                Write-CcReplaceCompactPlan $effectivePreview $duplicatePreview $settings
            }

            $forcedDecision
        }
        else {
            Get-CcReplacePreflightDecision $plan $settings
        }

        if ($decision -eq "cancel") {
            return
        }

        $selected = @()
        if ($decision -eq "all") {
            $selected = @($plan)
        }
        else {
            $selected = @($plan | Where-Object { $_.IsEffective })
        }

        if ($selected.Count -eq 0) {
            Write-Host ""
            Write-Host "No effective edits selected."
            return
        }

        $backedUp = @{}
        $changedTargets = @{}
        $applied = 0

        foreach ($entry in $selected) {
            $entryToApply = $entry

            if (-not $DryRun -and -not [bool]$entry.IsDirectory) {
                $targetKey = ([System.IO.Path]::GetFullPath($entry.TargetPath)).ToLowerInvariant()

                if ($changedTargets.ContainsKey($targetKey)) {
                    $entryToApply = Analyze-CcReplaceBlock $entry.Block

                    if ((-not [bool]$entry.IsDuplicate) -and [bool]$entryToApply.IsDuplicate) {
                        Write-Host "Skip now-duplicate action: $($entryToApply.TargetHeader) :: $($entryToApply.PartLabel)" -ForegroundColor Yellow
                        continue
                    }
                }
            }

            if (Apply-CcReplacePlanEntry $entryToApply $backedUp $settings) {
                $applied++

                if (-not $DryRun -and -not [bool]$entryToApply.IsDirectory -and -not [bool]$entryToApply.SkipWrite) {
                    $targetKey = ([System.IO.Path]::GetFullPath($entryToApply.TargetPath)).ToLowerInvariant()
                    $changedTargets[$targetKey] = $true
                }
            }
        }

        Write-CcReplaceActionSummary $selected $settings "Actions Done:"

        Write-Host ""
        if ($DryRun) {
            Write-Host "Done. Dry-run CC-REPLACE actions checked: $applied"
        }
        else {
            Write-Host "Done. Applied CC-REPLACE actions: $applied"
        }
    }
    finally {
        Stop-CcReplaceEditPhase
    }
}

function Invoke-CcReplaceFile {
    param(
        [string]$Path,
        [switch]$NoExitOnCancel,
        [string]$ApplyDecision = ""
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Input file not found: $Path"
    }

    $text = Read-TextFileAutoEncoding $Path
    return (Invoke-CcReplaceText $text -NoExitOnCancel:$NoExitOnCancel -ApplyDecision $ApplyDecision)
}

function Apply-Block {
    param($Block, $BackedUpMap)

    $settings = Read-CcReplaceSettings
    $entry = Analyze-CcReplaceBlock $Block
    return Apply-CcReplacePlanEntry $entry $BackedUpMap $settings
}
