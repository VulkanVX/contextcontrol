# CC-DESC: File, folder, and markdown block emission for Context Control source exports.

function Get-GptDescription {
    param([string]$Path)

    $sidecar = "$Path.ccdesc"

    if (Test-Path -LiteralPath $sidecar) {
        $sidecarText = Read-TextFileAutoEncoding $sidecar
        $sidecarText = $sidecarText.Trim()

        if ($sidecarText.Length -gt 0) {
            return $sidecarText
        }
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return "Missing file."
    }

    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    $isText = if ($null -ne $script:TextExtensionSet) {
        $script:TextExtensionSet.Contains($ext)
    }
    else {
        $script:TextExtensions -contains $ext
    }

    if (-not $isText) {
        return "Binary or non-text file."
    }

    $raw = Read-TextFileAutoEncoding $Path
    $allLines = @(ConvertTo-LineArray $raw)
    $max = [Math]::Min(120, $allLines.Count)
    $lines = @()

    for ($i = 0; $i -lt $max; $i++) {
        $lines += $allLines[$i]
    }

    foreach ($line in $lines) {
        if ($line -match '(?i)CC-DESC\s*:\s*(.+)$') {
            $desc = $Matches[1].Trim()
            $desc = $desc -replace '\*/\s*$', ''
            $desc = $desc -replace '-->\s*$', ''
            return $desc.Trim()
        }
    }

    foreach ($line in $lines) {
        if ($line -match '^\s*class\s+([A-Za-z_][A-Za-z0-9_]*)') {
            return "No CC-DESC found. C++ class '$($Matches[1])'."
        }

        if ($line -match '^\s*struct\s+([A-Za-z_][A-Za-z0-9_]*)') {
            return "No CC-DESC found. C++ struct '$($Matches[1])'."
        }

        if ($line -match '^\s*class_name\s+([A-Za-z0-9_]+)') {
            return "No CC-DESC found. GDScript class '$($Matches[1])'."
        }

        if ($line -match '^\s*extends\s+([A-Za-z0-9_\.]+)') {
            return "No CC-DESC found. GDScript file extends '$($Matches[1])'."
        }
    }

    return "No CC-DESC found."
}

function Add-DirectoryTree {
    param([string]$Dir)

    Add-Line ""
    Add-Line "## Folder tree: $Dir"
    Add-Line ""

    if (-not (Test-Path -LiteralPath $Dir)) {
        Add-Line "MISSING: $Dir"
        return
    }

    Add-Line ($script:Fence3 + "text")

    $root = (Resolve-Path -LiteralPath $Dir).Path
    $items = Get-ChildItem -LiteralPath $Dir -Recurse -Force |
        Where-Object { -not (Is-ExcludedPath $_.FullName) } |
        Sort-Object FullName

    foreach ($item in $items) {
        $relative = $item.FullName.Substring($root.Length)
        $relative = $relative -replace '^[\\/]+', ''
        $relative = $relative -replace '\\', '/'

        if ($relative -eq "") {
            continue
        }

        if ($item.PSIsContainer) {
            Add-Line "$relative/"
        }
        else {
            $ext = [System.IO.Path]::GetExtension($item.Name).ToLowerInvariant()

            $isBinary = if ($null -ne $script:BinaryExtensionSet) {
                $script:BinaryExtensionSet.Contains($ext)
            }
            else {
                $script:BinaryExtensions -contains $ext
            }

            if ($isBinary) {
                continue
            }

            if (Is-GptGeneratedExportFile $item.FullName) {
                continue
            }

            Add-Line "$relative"
        }
    }

    Add-Line $script:Fence3
}

function Add-FileBlock {
    param([string]$Path)

    Add-Line ""
    Add-Line "## $Path"
    Add-Line ""

    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Line "MISSING: $Path"
        return
    }

    if (Is-ExcludedPath $Path) {
        Add-Line "Skipped excluded/generated file: $Path"
        return
    }

    $description = Get-GptDescription $Path
    Add-Line "Description: $description"
    Add-Line ""

    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    $isBinary = if ($null -ne $script:BinaryExtensionSet) {
        $script:BinaryExtensionSet.Contains($ext)
    }
    else {
        $script:BinaryExtensions -contains $ext
    }

    if ($isBinary) {
        Add-Line "Skipped binary/runtime cache file: $Path"
        return
    }

    $isText = if ($null -ne $script:TextExtensionSet) {
        $script:TextExtensionSet.Contains($ext)
    }
    else {
        $script:TextExtensions -contains $ext
    }

    if (-not $isText) {
        Add-Line "Skipped non-text file: $Path"
        return
    }

    $fileInfo = Get-Item -LiteralPath $Path
    $sizeKB = [math]::Ceiling($fileInfo.Length / 1024.0)

    if ((-not $ForceLargeFiles) -and $sizeKB -gt $MaxFileKB) {
        Add-Line "Skipped large text file: $Path ($sizeKB KB > $MaxFileKB KB)."
        Add-Line "Re-run cc.ps1 with -ForceLargeFiles if this exact file is truly needed."
        return
    }

    $lang = Get-CodeFenceLanguage $Path
    $content = Read-TextFileAutoEncoding $Path

    Add-HashHintsForFile $Path $content

    Add-Line ($script:Fence4 + $lang)
    Add-Line $content
    Add-Line $script:Fence4
}

function Add-FileBlockOnce {
    param(
        [string]$Path,
        [switch]$QuietDuplicate
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Add-FileBlock $Path
        return
    }

    $key = Get-PathKey $Path

    if ($script:ExportedFileKeys.ContainsKey($key)) {
        if (-not $QuietDuplicate) {
            Add-Line ""
            Add-Line "Already exported above: $(Get-RelativeDisplayPath $Path)"
        }
        return
    }

    $script:ExportedFileKeys[$key] = $true
    Add-FileBlock $Path
}

function Add-PathToExport {
    param([string]$Path)

    $clean = $Path -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if (-not (Test-Path -LiteralPath $clean)) {
        Add-Line ""
        Add-Line "## MISSING: $Path"
        Add-Line ""
        return
    }

    if (Is-ExcludedPath $clean) {
        Add-Line ""
        Add-Line "## SKIPPED EXCLUDED/GENERATED PATH: $Path"
        Add-Line ""
        return
    }

    $item = Get-Item -LiteralPath $clean

    if ($item.PSIsContainer) {
        Add-DirectoryTree $clean
    }
    else {
        Add-FileBlockOnce $clean
    }
}
