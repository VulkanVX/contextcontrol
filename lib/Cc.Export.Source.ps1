# CC-DESC: Orchestrates Context Control source/function/FIND exports.

. (Join-Path $script:CcLibRoot "Cc.Export.Config.ps1")
. (Join-Path $script:CcLibRoot "Cc.Export.Hash.ps1")
. (Join-Path $script:CcLibRoot "Cc.Export.AutoDeps.ps1")
. (Join-Path $script:CcLibRoot "Cc.Export.FileBlocks.ps1")
. (Join-Path $script:CcLibRoot "Cc.Export.Functions.ps1")

function Invoke-CcSourceExport {
    param(
        [string]$OutputFile = "cc_code_export.md",
        [int]$MaxFileKB = 512,
        [switch]$ForceLargeFiles,
        [switch]$NoClipboard,
        [switch]$HashHints
    )

    $Backtick = [char]96
    $script:Fence3 = "$Backtick$Backtick$Backtick"
    $script:Fence4 = "$Backtick$Backtick$Backtick$Backtick"
    $script:OutputFileForFilters = $OutputFile
    $script:OutputLines = New-Object System.Collections.Generic.List[string]
    $script:ExportedFileKeys = @{}
    $script:FunctionSearchKeys = @{}
    $script:FindSearchKeys = @{}
    $script:SearchCandidateFilesCacheRoot = $null
    $script:SearchCandidateFilesCache = $null
    $script:SearchTextCache = @{}
    $script:SearchLineCache = @{}
    $script:SearchFunctionRangeCache = @{}
    $script:SearchRegexCache = @{}
    $script:PathKeyCache = @{}
    $script:NormalizedPathKeyCache = @{}
    $script:RelativeDisplayPathCache = @{}
    $script:FindTokenCache = @{}

    $script:CcSharedSettings = Read-CcSharedSettings
    $script:CcProjectRoot = Resolve-CcSharedProjectRoot $script:CcSharedSettings
    $script:CcOutputRoot = Resolve-CcSharedOutputRoot $script:CcSharedSettings
    $script:CcProjectFileRules = Read-CcProjectFileRules $script:CcSharedSettings
    Set-CcExportFilterRules -Settings $script:CcSharedSettings -Rules $script:CcProjectFileRules

    if (-not (Test-Path -LiteralPath $script:CcProjectRoot)) {
        throw "Configured ProjectRoot does not exist: $script:CcProjectRoot. Change it in contextcontrol/.ccReplace.settings.json or ccReplace Settings option 11."
    }

    if (-not (Test-Path -LiteralPath $script:CcOutputRoot)) {
        New-Item -ItemType Directory -Path $script:CcOutputRoot | Out-Null
    }

    # Export/search paths are relative to the configured project root.
    # Output files are still written to the configured Context Control output folder.
    Set-Location -LiteralPath $script:CcProjectRoot
    $script:OutputFileForFilters = Resolve-CcSharedOutputPath $OutputFile $script:CcSharedSettings

    Write-Host "Project root: $script:CcProjectRoot" -ForegroundColor DarkGray
    Write-Host "Output folder: $script:CcOutputRoot" -ForegroundColor DarkGray

    Write-Host ""
    Write-Host "Paste file/folder paths, one per line."
    Write-Host "Use FUNCTION src/path/File.cpp :: name to extract a specific function body."
    Write-Host "FUNCTION paths may use wildcards, e.g. FUNCTION src/world/World*.cpp :: World::foo."
    Write-Host "Use FUNC: name or FUNCTION name to search all code files and extract matching function bodies."
    Write-Host "Use FIND: text to list matching files and line previews without exporting file contents."
    Write-Host "SYMBOL: is disabled because it exported whole matching files and caused token explosions."
    Write-Host "Auto-adds matching headers for .cpp and direct GLSL #includes for shaders."
    Write-Host "Optional: run with -HashHints to emit compact HASH values for safer patches."
    Write-Host "Finish by pressing Enter on an empty line, or type END."
    Write-Host ""

    $InputPaths = @()
    $InputFunctions = @()
    $InputFindSearches = @()
    $InputScopedFunctions = @()

    while ($true) {
        $line = [Console]::ReadLine()

        if ($null -eq $line) {
            break
        }

        $clean = Clean-InputLine $line

        if ($clean -eq "") {
            break
        }

        if ($clean.ToUpperInvariant() -eq "END") {
            break
        }

        # Context Control function request syntax:
        # FUNCTION src/path/File.cpp :: FunctionName
        # FUNCTION paths may include wildcards for split implementation files:
        # FUNCTION src/world/World*.cpp :: World::beginTerrainEditVisualTracking
        if ($clean -match '(?i)^\s*(FUNC|FUNCTION|FIND|SYMBOL)\s+(.+?)\s+::\s*(.+?)\s*$') {
            $requestKind = $Matches[1].Trim().ToUpperInvariant()
            $requestPath = $Matches[2].Trim()
            $requestSymbol = $Matches[3].Trim()

            if ($requestPath -eq "" -or $requestSymbol -eq "") {
                throw "Malformed function request: '$clean'. Use: FUNCTION src/path/File.cpp :: SymbolName"
            }

            if ($requestKind -eq "FIND") {
                throw "Malformed FIND request: '$clean'. Use FIND: TextToLocate for discovery, or FUNCTION src/path/File.cpp :: SymbolName for body extraction."
            }

            if ($requestKind -eq "SYMBOL") {
                throw "SYMBOL: is disabled because it exported whole matching files and caused token explosions. Use FIND: TextToLocate for discovery, or request an exact path/FUNCTION export."
            }

            $InputScopedFunctions += [pscustomobject]@{
                Path = $requestPath
                Symbol = $requestSymbol
            }

            # Scoped FUNCTION requests extract only the requested body. Do not route
            # them through InputPaths, or the whole owning file gets exported first.
            continue
        }

        # Global search syntax:
        # FUNCTION: FunctionName / FUNC: FunctionName -> extract function bodies.
        # FIND: TextToLocate -> list matching files and line previews only.
        # SYMBOL: is intentionally disabled because whole-file symbol search causes token explosions.
        if ($clean -match '(?i)^\s*(FUNC|FUNCTION|FIND|SYMBOL)\s*:\s*(.+?)\s*$') {
            $requestKind = $Matches[1].Trim().ToUpperInvariant()
            $requestSymbol = $Matches[2].Trim()

            if ($requestKind -eq "FIND") {
                $InputFindSearches += $requestSymbol
            }
            elseif ($requestKind -eq "SYMBOL") {
                throw "SYMBOL: is disabled because it exported whole matching files and caused token explosions. Use FIND: $requestSymbol for discovery, or request an exact path/FUNCTION export."
            }
            else {
                $InputFunctions += $requestSymbol
            }
            continue
        }

        # Lenient unscoped function search syntax:
        # FUNCTION FunctionName / FUNC FunctionName
        if ($clean -match '(?i)^\s*(FUNC|FUNCTION)\s+(.+?)\s*$') {
            $requestSymbol = $Matches[2].Trim()

            if ($requestSymbol -ne "" -and
                $requestSymbol -notmatch '[\\/]' -and
                $requestSymbol -notmatch '\s+::\s+') {
                $InputFunctions += $requestSymbol
                continue
            }
        }

        # Hard guard: never silently treat malformed FUNCTION/SYMBOL lines as file paths.
        if ($clean -match '(?i)^\s*(FUNC|FUNCTION|FIND|SYMBOL)\b') {
            throw "Malformed request: '$clean'. Use exact file paths, FIND: TextToLocate, FUNCTION: SymbolName, or FUNCTION src/path/File.cpp :: SymbolName."
        }

        $InputPaths += $clean
    }

    if ($InputPaths.Count -eq 0 -and $InputFunctions.Count -eq 0 -and $InputFindSearches.Count -eq 0 -and $InputScopedFunctions.Count -eq 0) {
        Write-Host "No paths or functions entered. Export cancelled."
        exit
    }

    $InputPaths = @(Expand-InputPathsWithAutoDependencies $InputPaths)

    Add-Line "# Code export"
    Add-Line ""
    Add-Line "Generated from project files."
    Add-Line ""
    Add-Line "Project root: $(Get-Location)"
    Add-Line ""
    Add-Line "## Instructions for Context Control"
    Add-Line ""
    Add-Line "This export is source context only. Use the standing Context Control instructions for workflow rules and patch format."
    Add-Line ""
    Add-Line "Minimal rules for this turn:"
    Add-Line "- Spend reasoning on the requested code fix, not tool mechanics."
    Add-Line "- Prefer a single patch.txt containing raw BEGIN CC-REPLACE blocks. Inline only if tiny."
    Add-Line "- Keep the existing architecture and modular ownership boundaries."
    Add-Line "- Use MODE: insert_include for include-only edits."
    Add-Line "- FIND: reports are discovery only; they never include source bodies. Request exact files/functions after discovery."
    Add-Line "- If this export contains Hash hints, copy the matching HASH: value into function/replace_region patch headers. If no hash hint is present, omit HASH:."
    Add-Line "- If critical context is missing, ask only for exact paths, FUNCTION exports, or FIND discovery queries, one per line, ending with END."
    Add-Line ""
    Add-Line "Default CMake build, when applicable: cmake --build build --config Release -j"
    Add-Line ""

    foreach ($path in $InputPaths) {
        Add-PathToExport $path
    }

    foreach ($request in $InputScopedFunctions) {
        Add-ScopedFunctionRequestReport -Path $request.Path -Symbol $request.Symbol
    }

    foreach ($symbol in $InputFunctions) {
        Add-FunctionSearchExport $symbol
    }

    if ($InputFindSearches.Count -gt 0) {
        Add-FindSearchExports $InputFindSearches
    }

    $SavedOutputFile = Save-OutputFile $OutputFile

    Write-Host ""
    Write-Host "Done."
    Write-Host "Created: $SavedOutputFile"
    Write-Host "Open it: code `"$SavedOutputFile`""
    Write-Host "Fallback copy: Get-Content -LiteralPath `"$SavedOutputFile`" -Raw | Set-Clipboard"

    if (-not $NoClipboard) {
        $exportText = Get-OutputText
        try {
            Set-Clipboard -Value $exportText -ErrorAction Stop
            Write-Host "Copied export to clipboard too."
        }
        catch {
            $setClipboardError = $_.Exception.Message
            try {
                $clipExe = Join-Path $env:SystemRoot "System32\clip.exe"
                if (-not (Test-Path -LiteralPath $clipExe)) {
                    throw "clip.exe not found; Set-Clipboard failed: $setClipboardError"
                }

                $exportText | & $clipExe
                Write-Host "Copied export to clipboard via clip.exe."
            }
            catch {
                Write-Host "Clipboard copy skipped: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }
    }
    else {
        Write-Host "Clipboard copy disabled by -NoClipboard."
    }
}
