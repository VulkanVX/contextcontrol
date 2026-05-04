# CC-DESC: Applies CC-REPLACE patch blocks from pasted text or a file.
# ccReplace.ps1
# Supports:
#   function
#   insert_after_function
#   insert_before_function
#   delete_function
#   replace_region
#   append_to_file
#   whole_file
#   create_directory
#   insert_include
#
# Usage:
#   .\ccReplace.ps1 -InputFile .\patch.txt -DryRun
#   .\ccReplace.ps1 -InputFile .\patch.txt
#
# Important:
#   Patch files must contain raw CC-REPLACE blocks, not PowerShell here-string commands.

param(
    [string]$InputFile = "",
    [switch]$DryRun,
    [switch]$NoBackup,
    [switch]$Help,
    [switch]$AgentMode
)

$ErrorActionPreference = "Stop"

function ConvertTo-LineArray {
    param($Value)

    if ($null -eq $Value) {
        return [string[]]@()
    }

    $out = New-Object System.Collections.Generic.List[string]

    foreach ($item in @($Value)) {
        if ($null -eq $item) {
            continue
        }

        $text = [string]$item
        $text = $text -replace "`0", ""

        # Do this with plain string/char replacement, not PowerShell -split regex.
        # Your debug shows the old version is keeping the whole patch as one line.
        $text = $text.Replace([char]0x0085, [char]10)
        $text = $text.Replace([char]0x2028, [char]10)
        $text = $text.Replace([char]0x2029, [char]10)
        $text = $text.Replace("`r`n", "`n")
        $text = $text.Replace("`r", "`n")

        $parts = $text.Split([char]10)

        foreach ($part in $parts) {
            $out.Add($part)
        }
    }

    return [string[]]$out.ToArray()
}

function Read-TextFileAutoEncoding {
    param([string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $bytes = [System.IO.File]::ReadAllBytes($resolved)

    if ($bytes.Length -eq 0) {
        return ""
    }

    $encoding = $null

    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        $encoding = [System.Text.Encoding]::UTF8
    }
    elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFF -and
            $bytes[1] -eq 0xFE) {
        $encoding = [System.Text.Encoding]::Unicode
    }
    elseif ($bytes.Length -ge 2 -and
            $bytes[0] -eq 0xFE -and
            $bytes[1] -eq 0xFF) {
        $encoding = [System.Text.Encoding]::BigEndianUnicode
    }
    else {
        $sampleCount = [Math]::Min($bytes.Length, 400)
        $evenZero = 0
        $oddZero = 0

        for ($i = 0; $i -lt $sampleCount; $i++) {
            if ($bytes[$i] -eq 0) {
                if (($i % 2) -eq 0) {
                    $evenZero++
                }
                else {
                    $oddZero++
                }
            }
        }

        if ($oddZero -gt 10 -and $oddZero -gt ($evenZero * 2)) {
            $encoding = [System.Text.Encoding]::Unicode
        }
        elseif ($evenZero -gt 10 -and $evenZero -gt ($oddZero * 2)) {
            $encoding = [System.Text.Encoding]::BigEndianUnicode
        }
        else {
            $encoding = New-Object System.Text.UTF8Encoding($false, $false)
        }
    }

    $text = $encoding.GetString($bytes)
    $text = $text -replace "`0", ""
    return $text
}

function Read-PasteInputText {
    Write-Host ""
    Write-Host "Paste CC-REPLACE blocks with Ctrl+V."
    Write-Host "Finish with a line containing only: ENDCC"
    Write-Host ""

    $lines = New-Object System.Collections.Generic.List[string]

    while ($true) {
        $line = [Console]::ReadLine()

        if ($null -eq $line) {
            break
        }

        if ($line -eq "ENDCC") {
            break
        }

        $lines.Add($line)
    }

    return ($lines -join [Environment]::NewLine)
}

function Read-InputText {
    if ($InputFile -ne "") {
        if (-not (Test-Path -LiteralPath $InputFile)) {
            throw "Input file not found: $InputFile"
        }

        return Read-TextFileAutoEncoding $InputFile
    }

    return Read-PasteInputText
}

function Normalize-CcReplaceLine {
    param([string]$Line)

    if ($null -eq $Line) {
        return ""
    }

    $s = [string]$Line
    $s = $s -replace "`0", ""

    # BOM / invisible paste chars.
    $s = $s -replace "^\uFEFF", ""
    $s = $s -replace "^\u00EF\u00BB\u00BF", ""
    $s = $s -replace "\u200B", ""
    $s = $s -replace "\u200C", ""
    $s = $s -replace "\u200D", ""
    $s = $s -replace "\u2060", ""

    return $s.Trim()
}

function Trim-CodeBlankEdges {
    param([string]$Code)

    $lines = @(ConvertTo-LineArray $Code)

    if ($lines.Count -eq 0) {
        return ""
    }

    $start = 0
    $end = $lines.Count - 1

    while ($start -le $end -and $lines[$start].Trim() -eq "") {
        $start++
    }

    while ($end -ge $start -and $lines[$end].Trim() -eq "") {
        $end--
    }

    if ($start -gt $end) {
        return ""
    }

    return ($lines[$start..$end] -join "`n")
}

function Parse-CcReplaceBlocks {
    param([string]$Text)

    if ($null -eq $Text) {
        return @()
    }

    $Text = $Text -replace "`0", ""
    $Text = [regex]::Replace($Text, "\r\n|\r|\n|\x85|\u2028|\u2029", "`n")

    $lines = @(ConvertTo-LineArray $Text)
    $blocks = @()
    $i = 0

    while ($i -lt $lines.Count) {
        $sentinel = Normalize-CcReplaceLine $lines[$i]

        # Ignore markdown fences.
        if ($sentinel -match '^```') {
            $i++
            continue
        }

        # Only real patch blocks count.
        # Commented examples like "# BEGIN CC-REPLACE" are ignored.
        if ($sentinel -notmatch '^BEGIN\s+CC-REPLACE$') {
            $i++
            continue
        }

        $blockStartLine = $i + 1
        $i++
        $headers = @{}
        $hasSeparator = $false

        while ($i -lt $lines.Count) {
            $line = Normalize-CcReplaceLine $lines[$i]

            if ($line -eq "---") {
                $hasSeparator = $true
                break
            }

            if ($line -match '^END\s+CC-REPLACE$') {
                break
            }

            if ($line -match '^([A-Za-z0-9_\-]+)\s*:\s*(.*)$') {
                $key = $Matches[1].ToUpperInvariant()
                $value = $Matches[2].Trim()
                $headers[$key] = $value
            }

            $i++
        }

        if (-not ($headers.ContainsKey("FILE") -or $headers.ContainsKey("DIR") -or $headers.ContainsKey("PATH"))) {
            $near = New-Object System.Collections.Generic.List[string]
            $from = [Math]::Max(0, $blockStartLine - 1)
            $to = [Math]::Min($lines.Count - 1, $from + 8)

            for ($d = $from; $d -le $to; $d++) {
                $lineNo = $d + 1
                $near.Add("  ${lineNo}: $($lines[$d])")
            }

            throw "CC-REPLACE block starting at line ${blockStartLine} has no FILE/DIR/PATH header. Nearby lines:`n$($near -join "`n")"
        }

        $mode = "function"
        if ($headers.ContainsKey("MODE")) {
            $mode = $headers["MODE"].ToLowerInvariant()
        }

        $codeLines = New-Object System.Collections.Generic.List[string]

        if ($hasSeparator) {
            $i++

            while ($i -lt $lines.Count -and (Normalize-CcReplaceLine $lines[$i]) -notmatch '^END\s+CC-REPLACE$') {
                $codeLines.Add($lines[$i])
                $i++
            }

            if ($i -ge $lines.Count) {
                throw "Malformed CC-REPLACE block starting at line ${blockStartLine}: missing END CC-REPLACE."
            }
        }
        else {
            if ($i -ge $lines.Count -or (Normalize-CcReplaceLine $lines[$i]) -notmatch '^END\s+CC-REPLACE$') {
                throw "Malformed CC-REPLACE block starting at line ${blockStartLine}: missing --- separator or END CC-REPLACE."
            }

            if (@("insert_include", "create_directory") -notcontains $mode) {
                throw "Malformed CC-REPLACE block starting at line ${blockStartLine}: MODE:$mode requires a --- separator and body."
            }
        }

        $i++

        $blocks += [pscustomobject]@{
            Headers = $headers
            Code = ($codeLines -join "`n")
            HasSeparator = $hasSeparator
        }
    }

    return $blocks
}

function Resolve-TargetPath {
    param([string]$PathText)

    if ($PathText -eq "") {
        throw "Missing FILE/DIR/PATH header."
    }

    $clean = $PathText.Trim()
    $clean = $clean -replace '/', [System.IO.Path]::DirectorySeparatorChar

    if ([System.IO.Path]::IsPathRooted($clean)) {
        return $clean
    }

    return Join-Path (Get-Location).Path $clean
}

function Get-HeaderValue {
    param($Headers, [string]$Name)

    $key = $Name.ToUpperInvariant()

    if ($Headers.ContainsKey($key)) {
        return $Headers[$key]
    }

    return ""
}

function Get-TargetHeader {
    param($Headers)

    $file = Get-HeaderValue $Headers "FILE"
    if ($file -ne "") { return $file }

    $dir = Get-HeaderValue $Headers "DIR"
    if ($dir -ne "") { return $dir }

    $path = Get-HeaderValue $Headers "PATH"
    if ($path -ne "") { return $path }

    return ""
}

function Get-IndentWidth {
    param([string]$Line)

    $width = 0

    for ($i = 0; $i -lt $Line.Length; $i++) {
        $ch = $Line[$i]

        if ($ch -eq ' ') {
            $width += 1
        }
        elseif ($ch -eq "`t") {
            $width += 4
        }
        else {
            break
        }
    }

    return $width
}

function Read-TargetLines {
    param([string]$Path)

    if ($null -eq $script:CcReplaceReadCache) {
        $script:CcReplaceReadCache = @{}
    }

    # Fast in-process cache. ccReplace is a single-run mechanical applier:
    # files are only changed by Write-TargetLines, which updates this cache.
    # Avoid Get-Item timestamp checks on every function lookup; those were
    # becoming visible when many blocks targeted the same file.
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $cacheKey = $fullPath.ToLowerInvariant()

    if ($script:CcReplaceReadCache.ContainsKey($cacheKey)) {
        $cached = $script:CcReplaceReadCache[$cacheKey]
        if ($null -ne $cached) {
            return [pscustomobject]@{
                Lines = [string[]]$cached.Lines
                Newline = [string]$cached.Newline
            }
        }
    }

    $raw = Read-TextFileAutoEncoding $fullPath

    $newline = "`n"
    if ($raw.Contains("`r`n")) {
        $newline = "`r`n"
    }
    elseif ($raw.Contains("`r")) {
        $newline = "`r"
    }

    $lines = @(ConvertTo-LineArray $raw)

    if ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq "") {
        if ($lines.Count -eq 1) {
            $lines = @()
        }
        else {
            $lines = $lines[0..($lines.Count - 2)]
        }
    }

    $result = [pscustomobject]@{
        Lines = [string[]]$lines
        Newline = $newline
    }

    $script:CcReplaceReadCache[$cacheKey] = $result
    return $result
}

function Write-TargetLines {
    param(
        [string]$Path,
        $Lines,
        [string]$Newline
    )

    if ($null -eq $Lines) {
        $safeLines = [string[]]@()
    }
    elseif ($Lines -is [System.Array]) {
        $safeLines = [string[]]@($Lines)
    }
    else {
        $safeLines = [string[]]@(ConvertTo-LineArray $Lines)
    }

    $out = ($safeLines -join $Newline) + $Newline
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $fullPath = [System.IO.Path]::GetFullPath($Path)

    [System.IO.File]::WriteAllText($fullPath, $out, $utf8NoBom)

    if ($null -eq $script:CcReplaceReadCache) {
        $script:CcReplaceReadCache = @{}
    }

    $cacheKey = $fullPath.ToLowerInvariant()
    $script:CcReplaceReadCache[$cacheKey] = [pscustomobject]@{
        Lines = [string[]]$safeLines
        Newline = $Newline
    }
}

function Replace-LineRange {
    param($Lines, [int]$Start, [int]$End, [string]$ReplacementCode)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $trimmed = Trim-CodeBlankEdges $ReplacementCode
    $replacement = @()

    if ($trimmed -ne "") {
        $replacement = @(ConvertTo-LineArray $trimmed)
    }

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid replace range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $Start; $i++) {
        $newLines.Add($safeLines[$i])
    }

    foreach ($line in $replacement) {
        $newLines.Add($line)
    }

    for ($i = $End; $i -lt $safeLines.Count; $i++) {
        $newLines.Add($safeLines[$i])
    }

    return [string[]]$newLines.ToArray()
}

function Insert-LinesAt {
    param($Lines, [int]$Index, [string]$Code)

    $safeLines = @(ConvertTo-LineArray $Lines)

    if ($Index -lt 0 -or $Index -gt $safeLines.Count) {
        throw "Invalid insert index: Index=$Index Lines=$($safeLines.Count)"
    }

    $trimmed = Trim-CodeBlankEdges $Code
    $insert = @()

    if ($trimmed -ne "") {
        $insert = @(ConvertTo-LineArray $trimmed)
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $Index; $i++) {
        $newLines.Add($safeLines[$i])
    }

    if ($insert.Count -gt 0) {
        if ($Index -gt 0 -and $newLines.Count -gt 0 -and $newLines[$newLines.Count - 1].Trim() -ne "") {
            $newLines.Add("")
        }

        foreach ($line in $insert) {
            $newLines.Add($line)
        }

        if ($Index -lt $safeLines.Count -and $insert[$insert.Count - 1].Trim() -ne "") {
            $newLines.Add("")
        }
    }

    if ($Index -lt $safeLines.Count) {
        for ($i = $Index; $i -lt $safeLines.Count; $i++) {
            $newLines.Add($safeLines[$i])
        }
    }

    return [string[]]$newLines.ToArray()
}

function Find-GdFunctionRange {
    param($Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($FunctionName)
    $pattern = '^\s*(static\s+)?func\s+' + $escaped + '\s*\('

    $start = -1

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match $pattern) {
            $start = $i
            break
        }
    }

    if ($start -lt 0) {
        throw "Function not found: $FunctionName"
    }

    $startIndent = Get-IndentWidth $safeLines[$start]
    $end = $safeLines.Count

    for ($j = $start + 1; $j -lt $safeLines.Count; $j++) {
        if ($safeLines[$j].Trim() -eq "") {
            continue
        }

        $indent = Get-IndentWidth $safeLines[$j]

        if ($indent -le $startIndent) {
            $end = $j
            break
        }
    }

    return [pscustomobject]@{
        Start = $start
        End = $end
    }
}

function Strip-CodeLineForBraces {
    param(
        [string]$Line,
        [ref]$InBlockComment
    )

    $result = New-Object System.Text.StringBuilder
    $inString = $false
    $inChar = $false
    $escape = $false
    $i = 0

    while ($i -lt $Line.Length) {
        $ch = $Line[$i]
        $next = if ($i + 1 -lt $Line.Length) { $Line[$i + 1] } else { [char]0 }

        if ($InBlockComment.Value) {
            if ($ch -eq '*' -and $next -eq '/') {
                $InBlockComment.Value = $false
                [void]$result.Append('  ')
                $i += 2
                continue
            }

            [void]$result.Append(' ')
            $i++
            continue
        }

        if (-not $inString -and -not $inChar) {
            if ($ch -eq '/' -and $next -eq '/') {
                break
            }

            if ($ch -eq '/' -and $next -eq '*') {
                $InBlockComment.Value = $true
                [void]$result.Append('  ')
                $i += 2
                continue
            }
        }

        if ($escape) {
            [void]$result.Append(' ')
            $escape = $false
            $i++
            continue
        }

        if (($inString -or $inChar) -and $ch -eq '\') {
            [void]$result.Append(' ')
            $escape = $true
            $i++
            continue
        }

        if (-not $inChar -and $ch -eq '"') {
            $inString = -not $inString
            [void]$result.Append(' ')
            $i++
            continue
        }

        if (-not $inString -and $ch -eq "'") {
            $inChar = -not $inChar
            [void]$result.Append(' ')
            $i++
            continue
        }

        if ($inString -or $inChar) {
            [void]$result.Append(' ')
        }
        else {
            [void]$result.Append($ch)
        }

        $i++
    }

    return $result.ToString()
}

function Test-LooksLikeFunctionSignaturePrefix {
    param([string]$Prefix)

    $p = $Prefix.Trim()

    if ($p -eq "") {
        return $true
    }

    # Reject obvious call/expression contexts.
    if ($p -match '(=|,|\[|\]|\.)') {
        return $false
    }

    if ($p -match '\b(return|if|while|for|switch|case|sizeof|new|delete|catch)\b') {
        return $false
    }

    return $true
}

function Get-RawOccurrenceHint {
    param($Lines, [string]$Needle)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($Needle)
    $hits = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match $escaped) {
            $lineNo = $i + 1
            $text = $safeLines[$i].Trim()

            if ($text.Length -gt 220) {
                $text = $text.Substring(0, 220) + " ..."
            }

            $hits.Add("  line ${lineNo}: $text")

            if ($hits.Count -ge 12) {
                break
            }
        }
    }

    if ($hits.Count -eq 0) {
        return ""
    }

    return "Raw occurrences of '$Needle' found:`n" + ($hits -join "`n")
}

function Find-BraceFunctionRange {
    param($Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($FunctionName)

    $psPattern = '^\s*function\s+' + $escaped + '\b'
    $cPattern = '(?<![A-Za-z0-9_~])(?:[A-Za-z_][A-Za-z0-9_]*::)*' + $escaped + '\s*\('

    $searchBlockComment = $false

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        $rawLine = [string]$safeLines[$i]
        $trimmed = $rawLine.Trim()

        if ($trimmed -eq "") {
            continue
        }

        if (($trimmed.StartsWith("//") -or $trimmed.StartsWith("#")) -and
            ($trimmed -notmatch $escaped)) {
            continue
        }

        $cleanLine = Strip-CodeLineForBraces $rawLine ([ref]$searchBlockComment)

        $isPowerShellFunction = $cleanLine -match $psPattern
        $cMatch = [regex]::Match($cleanLine, $cPattern)
        $isCStyleFunction = $cMatch.Success

        if (-not $isPowerShellFunction -and -not $isCStyleFunction) {
            continue
        }

        if ($isCStyleFunction -and -not $isPowerShellFunction) {
            $prefix = $cleanLine.Substring(0, $cMatch.Index)

            if (-not (Test-LooksLikeFunctionSignaturePrefix $prefix)) {
                continue
            }
        }

        $start = $i

        while ($start -gt 0) {
            $prev = $safeLines[$start - 1].Trim()

            if ($prev -eq "") {
                break
            }

            if ($prev.StartsWith("template") -or
                $prev.StartsWith("[[") -or
                $prev.StartsWith("__attribute__") -or
                $prev.StartsWith("VKAPI_ATTR") -or
                $prev.StartsWith("static_assert")) {
                $start--
                continue
            }

            break
        }

        $openLine = -1
        $sawSemicolonBeforeBrace = $false
        $scanBlockComment = $false

        for ($j = $i; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForBraces ([string]$safeLines[$j]) ([ref]$scanBlockComment)

            $braceIndex = $clean.IndexOf("{")
            $semiIndex = $clean.IndexOf(";")

            if ($semiIndex -ge 0 -and ($braceIndex -lt 0 -or $semiIndex -lt $braceIndex)) {
                $sawSemicolonBeforeBrace = $true
                break
            }

            if ($braceIndex -ge 0) {
                $openLine = $j
                break
            }
        }

        if ($openLine -lt 0 -or $sawSemicolonBeforeBrace) {
            continue
        }

        $depth = 0
        $started = $false
        $bodyBlockComment = $false

        for ($j = $openLine; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForBraces ([string]$safeLines[$j]) ([ref]$bodyBlockComment)

            for ($k = 0; $k -lt $clean.Length; $k++) {
                if ($clean[$k] -eq '{') {
                    $depth++
                    $started = $true
                }
                elseif ($clean[$k] -eq '}') {
                    $depth--
                }

                if ($started -and $depth -eq 0) {
                    return [pscustomobject]@{
                        Start = $start
                        End = $j + 1
                    }
                }
            }
        }

        throw "Function body did not close: $FunctionName"
    }

    $hint = Get-RawOccurrenceHint $safeLines $FunctionName
    if ($hint -ne "") {
        throw "Function not found: $FunctionName`n$hint"
    }

    throw "Function not found: $FunctionName"
}

function Find-FunctionRange {
    param([string]$Path, $Lines, [string]$FunctionName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($ext -eq ".gd") {
        return Find-GdFunctionRange $safeLines $FunctionName
    }

    return Find-BraceFunctionRange $safeLines $FunctionName
}

function Find-MarkerRange {
    param($Lines, [string]$MarkerName)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $escaped = [regex]::Escape($MarkerName)

    $beginPattern = '^\s*(#|//|/\*)?\s*CC-REPLACE-BEGIN\s*:\s*' + $escaped + '\b.*$'
    $endPattern   = '^\s*(#|//|/\*)?\s*CC-REPLACE-END\s*:\s*'   + $escaped + '\b.*$'

    $begin = -1
    $end = -1

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($begin -lt 0 -and $safeLines[$i] -match $beginPattern) {
            $begin = $i
            continue
        }

        if ($begin -ge 0 -and $safeLines[$i] -match $endPattern) {
            $end = $i
            break
        }
    }

    if ($begin -lt 0) {
        throw "Marker begin not found: CC-REPLACE-BEGIN: $MarkerName"
    }

    if ($end -lt 0) {
        throw "Marker end not found after begin line $($begin + 1): CC-REPLACE-END: $MarkerName"
    }

    return [pscustomobject]@{
        Start = $begin + 1
        End = $end
    }
}

function Backup-TargetOnce {
    param([string]$Path, $BackedUpMap)

    if ($NoBackup) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if ($BackedUpMap.ContainsKey($Path)) {
        return
    }


}

function Ensure-ParentDirectory {
    param([string]$Path)

    $parent = Split-Path -Parent $Path

    if ($parent -eq "" -or $null -eq $parent) {
        return
    }

    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Write-Host "Create parent directory: $parent"
    }
}


function Get-Sha1Short {
    param([string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hashBytes = $sha.ComputeHash($bytes)
        $hex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
        return $hex.Substring(0, 8)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-RegionHashFromLines {
    param(
        $Lines,
        [int]$Start,
        [int]$End
    )

    $safeLines = @(ConvertTo-LineArray $Lines)

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid hash range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    if ($Start -eq $End) {
        return Get-Sha1Short ""
    }

    return Get-Sha1Short (($safeLines[$Start..($End - 1)]) -join "`n")
}

function Assert-OptionalHashMatches {
    param(
        $Headers,
        $Lines,
        [int]$Start,
        [int]$End,
        [string]$TargetDescription
    )

    $expected = Get-HeaderValue $Headers "HASH"
    if ($expected -eq "") {
        return
    }

    if ($expected -notmatch '^[0-9A-Fa-f]{8}$') {
        throw "Invalid HASH header for ${TargetDescription}: '$expected'. Expected 8 hex characters."
    }

    $actual = Get-RegionHashFromLines $Lines $Start $End

    if ($actual.ToLowerInvariant() -ne $expected.ToLowerInvariant()) {
        throw "HASH mismatch for ${TargetDescription}. Expected $expected, actual $actual. The target region changed since export; re-run cc.ps1 and regenerate the patch."
    }
}

function Get-TrimmedCodeLineArray {
    param([string]$Code)

    $trimmed = Trim-CodeBlankEdges $Code

    if ($trimmed -eq "") {
        return [string[]]@()
    }

    return [string[]]@(ConvertTo-LineArray $trimmed)
}

function Test-LineArraysEqual {
    param($A, $B)

    $aa = @(ConvertTo-LineArray $A)
    $bb = @(ConvertTo-LineArray $B)

    if ($aa.Count -ne $bb.Count) {
        return $false
    }

    for ($i = 0; $i -lt $aa.Count; $i++) {
        if ($aa[$i] -ne $bb[$i]) {
            return $false
        }
    }

    return $true
}

function Test-CodeRegionMatches {
    param(
        $Lines,
        [int]$Start,
        [int]$End,
        [string]$Code
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $codeLines = @(Get-TrimmedCodeLineArray $Code)

    if ($Start -lt 0 -or $End -lt $Start -or $End -gt $safeLines.Count) {
        throw "Invalid duplicate-check range: Start=$Start End=$End Lines=$($safeLines.Count)"
    }

    $existingCount = $End - $Start

    if ($existingCount -ne $codeLines.Count) {
        return $false
    }

    for ($i = 0; $i -lt $existingCount; $i++) {
        if ($safeLines[$Start + $i] -ne $codeLines[$i]) {
            return $false
        }
    }

    return $true
}

function Test-CodeBlockAlreadyInLines {
    param(
        $Lines,
        [string]$Code
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $codeLines = @(Get-TrimmedCodeLineArray $Code)

    if ($codeLines.Count -eq 0) {
        return $false
    }

    if ($codeLines.Count -gt $safeLines.Count) {
        return $false
    }

    $lastStart = $safeLines.Count - $codeLines.Count

    for ($start = 0; $start -le $lastStart; $start++) {
        $matches = $true

        for ($j = 0; $j -lt $codeLines.Count; $j++) {
            if ($safeLines[$start + $j] -ne $codeLines[$j]) {
                $matches = $false
                break
            }
        }

        if ($matches) {
            return $true
        }
    }

    return $false
}

function Confirm-AlreadyImplementedProceed {
    param(
        [string]$Action,
        [string]$TargetDescription
    )

    Write-CcReplaceDuplicateWarning @([pscustomobject]@{
        Action = $Action
        TargetDescription = $TargetDescription
        TargetHeader = $TargetDescription
        Name = ""
        Mode = ""
    }) $false

    $answer = Read-CcReplaceDecision "To proceed without ignorance, type:" @("still proceed")

    if ($answer -ieq "still proceed") {
        return $true
    }

    Write-Host "cancelled"
    exit 1
}

function Confirm-AlreadyImplementedProceedOrSkip {
    param(
        [string]$Action,
        [string]$TargetDescription
    )

    $proceed = Confirm-AlreadyImplementedProceed $Action $TargetDescription

    if (-not $proceed) {
        return $false
    }

    if ($DryRun) {
        Write-Host "Dry run: proceed accepted, but no write will happen."
    }

    return $true
}


function New-CcReplaceDefaultSettings {
    return [pscustomobject]@{
        DefaultPatchFile = "patch.txt"
        ShowPreflightStatistics = $true
        ShowFileDetails = $true
        ShowCreatedRemovedLists = $true
        ShowDirectoryPrefixes = $true
        ConfirmationStage = $true
        RepeatInvalidInput = $false
        VersionCacheEnabled = $false
        VersionCacheRoot = ".ccReplace.versions"
    }
}

function Get-CcReplaceSettingsPath {
    return (Join-Path (Get-Location).Path ".ccReplace.settings.json")
}

function Merge-CcReplaceSettings {
    param($Loaded)

    $settings = New-CcReplaceDefaultSettings

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

function Read-CcReplaceSettings {
    $path = Get-CcReplaceSettingsPath

    if (-not (Test-Path -LiteralPath $path)) {
        return New-CcReplaceDefaultSettings
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ($json.Trim() -eq "") {
            return New-CcReplaceDefaultSettings
        }

        return Merge-CcReplaceSettings ($json | ConvertFrom-Json)
    }
    catch {
        Write-Host "Settings file is invalid, using defaults: $path" -ForegroundColor Yellow
        return New-CcReplaceDefaultSettings
    }
}

function Save-CcReplaceSettings {
    param($Settings)

    $path = Get-CcReplaceSettingsPath
    $json = $Settings | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $json + [Environment]::NewLine, $utf8NoBom)
}


function Get-CcReplaceVersionRoot {
    param($Settings)

    $root = ".ccReplace.versions"
    if ($null -ne $Settings -and ($Settings.PSObject.Properties.Name -contains "VersionCacheRoot") -and $Settings.VersionCacheRoot -ne "") {
        $root = [string]$Settings.VersionCacheRoot
    }

    return Resolve-TargetPath $root
}

function Get-CcReplaceVersionIndexPath {
    param($Settings)

    return Join-Path (Get-CcReplaceVersionRoot $Settings) "index.json"
}

function New-CcReplaceVersionIndex {
    return [pscustomobject]@{
        Files = @()
    }
}

function Normalize-CcReplaceVersionIndex {
    param($Index)

    if ($null -eq $Index) {
        return New-CcReplaceVersionIndex
    }

    if (-not ($Index.PSObject.Properties.Name -contains "Files") -or $null -eq $Index.Files) {
        $Index | Add-Member -NotePropertyName Files -NotePropertyValue @() -Force
    }

    $Index.Files = @($Index.Files)

    foreach ($file in @($Index.Files)) {
        if (-not ($file.PSObject.Properties.Name -contains "Versions") -or $null -eq $file.Versions) {
            $file | Add-Member -NotePropertyName Versions -NotePropertyValue @() -Force
        }
        $file.Versions = @($file.Versions)

        if (-not ($file.PSObject.Properties.Name -contains "CurrentVersion")) {
            $max = 0
            foreach ($version in @($file.Versions)) {
                if ([int]$version.Version -gt $max) { $max = [int]$version.Version }
            }
            $file | Add-Member -NotePropertyName CurrentVersion -NotePropertyValue $max -Force
        }
    }

    return $Index
}

function Read-CcReplaceVersionIndex {
    param($Settings)

    $path = Get-CcReplaceVersionIndexPath $Settings
    $cacheKey = ([System.IO.Path]::GetFullPath($path)).ToLowerInvariant()

    if (($script:CcReplaceVersionIndexCacheKey -eq $cacheKey) -and
        ($null -ne $script:CcReplaceVersionIndexCache)) {
        return $script:CcReplaceVersionIndexCache
    }

    if (-not (Test-Path -LiteralPath $path)) {
        $empty = New-CcReplaceVersionIndex
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $empty
        return $empty
    }

    try {
        $json = Read-TextFileAutoEncoding $path
        if ($json.Trim() -eq "") {
            $empty = New-CcReplaceVersionIndex
            $script:CcReplaceVersionIndexCacheKey = $cacheKey
            $script:CcReplaceVersionIndexCache = $empty
            return $empty
        }

        $index = Normalize-CcReplaceVersionIndex ($json | ConvertFrom-Json)
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $index
        return $index
    }
    catch {
        Write-Host "Version index is invalid, starting with empty index: $path" -ForegroundColor Yellow
        $empty = New-CcReplaceVersionIndex
        $script:CcReplaceVersionIndexCacheKey = $cacheKey
        $script:CcReplaceVersionIndexCache = $empty
        return $empty
    }
}

function Save-CcReplaceVersionIndex {
    param($Settings, $Index)

    $root = Get-CcReplaceVersionRoot $Settings
    if (-not (Test-Path -LiteralPath $root)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }

    $path = Get-CcReplaceVersionIndexPath $Settings
    $indexToSave = Normalize-CcReplaceVersionIndex $Index
    $json = $indexToSave | ConvertTo-Json -Depth 32
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $json + [Environment]::NewLine, $utf8NoBom)

    $script:CcReplaceVersionIndexCacheKey = ([System.IO.Path]::GetFullPath($path)).ToLowerInvariant()
    $script:CcReplaceVersionIndexCache = $indexToSave
}

function Get-CcReplaceSha1Short {
    param([string]$Text, [int]$Length = 16)

    if ($null -eq $Text) { $Text = "" }
    if ($Length -lt 8) { $Length = 8 }

    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hashBytes = $sha.ComputeHash($bytes)
        $hex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })
        if ($Length -gt $hex.Length) { $Length = $hex.Length }
        return $hex.Substring(0, $Length)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-CcReplaceVersionKey {
    param([string]$TargetHeader, [string]$TargetPath)

    $basis = $TargetHeader
    if ($basis -eq "" -or $null -eq $basis) {
        $basis = $TargetPath
    }

    return Get-CcReplaceSha1Short $basis 16
}

function Get-CcReplaceVersionFileRecord {
    param(
        $Index,
        [string]$TargetHeader,
        [string]$TargetPath,
        [bool]$CreateIfMissing
    )

    $Index = Normalize-CcReplaceVersionIndex $Index
    $files = @($Index.Files)

    foreach ($file in $files) {
        if ([string]$file.Path -eq $TargetHeader) {
            return $file
        }
    }

    if (-not $CreateIfMissing) {
        return $null
    }

    $record = [pscustomobject]@{
        Path = $TargetHeader
        FullPath = $TargetPath
        Key = (Get-CcReplaceVersionKey $TargetHeader $TargetPath)
        CurrentVersion = 0
        Versions = @()
    }

    $files += $record
    $Index.Files = @($files)
    return $record
}

function Get-CcReplaceNextVersionNumber {
    param($Record)

    $max = 0
    foreach ($version in @($Record.Versions)) {
        if ([int]$version.Version -gt $max) {
            $max = [int]$version.Version
        }
    }

    return ($max + 1)
}

function Get-CcReplaceVersionTimestamp {
    return (Get-Date).ToUniversalTime().ToString("o")
}

function Format-CcReplaceVersionTimestamp {
    param([string]$Timestamp)

    if ($Timestamp -eq "" -or $null -eq $Timestamp) {
        return "unknown time"
    }

    try {
        return ([DateTime]::Parse($Timestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
    }
    catch {
        return $Timestamp
    }
}

function Get-CcReplaceTargetTextForVersionSnapshot {
    param([string]$TargetPath)

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return ""
    }

    # Use the same read cache as patch analysis/apply. This avoids a second disk
    # read when version cache is enabled and the file was already loaded.
    $read = Read-TargetLines $TargetPath
    $lines = @($read.Lines)

    if ($lines.Count -eq 0) {
        return ""
    }

    return (($lines -join $read.Newline) + $read.Newline)
}


function Save-CcReplaceSnapshotText {
    param(
        $Settings,
        $Index,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Text,
        [string]$Reason
    )

    if (-not [bool]$Settings.VersionCacheEnabled) {
        return $Index
    }

    if ($NoBackup) {
        return $Index
    }

    $Index = Normalize-CcReplaceVersionIndex $Index
    $record = Get-CcReplaceVersionFileRecord $Index $TargetHeader $TargetPath $true
    $versionNumber = Get-CcReplaceNextVersionNumber $record
    $timestamp = Get-CcReplaceVersionTimestamp

    $root = Get-CcReplaceVersionRoot $Settings
    $fileDir = Join-Path (Join-Path $root "files") $record.Key
    if (-not (Test-Path -LiteralPath $fileDir)) {
        New-Item -ItemType Directory -Path $fileDir -Force | Out-Null
    }

    $snapshotName = ("v{0:D6}.snapshot" -f $versionNumber)
    $snapshotPath = Join-Path $fileDir $snapshotName
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($snapshotPath, $Text, $utf8NoBom)

    $relativeSnapshot = Join-Path (Join-Path "files" $record.Key) $snapshotName
    $versions = @($record.Versions)
    $versions += [pscustomobject]@{
        Version = $versionNumber
        Timestamp = $timestamp
        Snapshot = $relativeSnapshot
        Reason = $Reason
    }

    $record.Versions = @($versions)
    $record.CurrentVersion = $versionNumber
    $record.FullPath = $TargetPath

    return $Index
}

function Save-CcReplaceCurrentFileVersion {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Reason
    )

    if (-not [bool]$Settings.VersionCacheEnabled) { return }
    if ($NoBackup) { return }
    if (-not (Test-Path -LiteralPath $TargetPath)) { return }

    $index = Read-CcReplaceVersionIndex $Settings
    $text = Get-CcReplaceTargetTextForVersionSnapshot $TargetPath
    $index = Save-CcReplaceSnapshotText $Settings $index $TargetHeader $TargetPath $text $Reason
    Save-CcReplaceVersionIndex $Settings $index
}

function Ensure-CcReplaceVersionBaseline {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    if (-not [bool]$Settings.VersionCacheEnabled) { return }
    if ($NoBackup) { return }
    if (-not (Test-Path -LiteralPath $TargetPath)) { return }

    $index = Read-CcReplaceVersionIndex $Settings
    $record = Get-CcReplaceVersionFileRecord $index $TargetHeader $TargetPath $false

    if ($null -ne $record -and @($record.Versions).Count -gt 0) {
        return
    }

    $text = Get-CcReplaceTargetTextForVersionSnapshot $TargetPath
    $index = Save-CcReplaceSnapshotText $Settings $index $TargetHeader $TargetPath $text "baseline before first CC-REPLACE edit"
    Save-CcReplaceVersionIndex $Settings $index
}

function Get-CcReplaceSnapshotFullPath {
    param($Settings, $VersionRecord)

    return Join-Path (Get-CcReplaceVersionRoot $Settings) ([string]$VersionRecord.Snapshot)
}

function Invoke-CcReplaceVersionRollback {
    param($Settings, $FileRecord, $VersionRecord)

    $snapshotPath = Get-CcReplaceSnapshotFullPath $Settings $VersionRecord
    if (-not (Test-Path -LiteralPath $snapshotPath)) {
        Write-Host "Snapshot file is missing: $snapshotPath" -ForegroundColor Red
        return
    }

    $targetPath = [string]$FileRecord.FullPath
    if ($targetPath -eq "" -or $null -eq $targetPath) {
        $targetPath = Resolve-TargetPath ([string]$FileRecord.Path)
    }

    Ensure-ParentDirectory $targetPath

    $text = Read-TextFileAutoEncoding $snapshotPath
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($targetPath, $text, $utf8NoBom)

    $index = Read-CcReplaceVersionIndex $Settings
    $index = Save-CcReplaceSnapshotText $Settings $index ([string]$FileRecord.Path) $targetPath $text ("rollback to version {0}" -f [int]$VersionRecord.Version)
    Save-CcReplaceVersionIndex $Settings $index

    Write-Host "Rolled back to version $($VersionRecord.Version). New current version was created." -ForegroundColor Green
}

function Remove-CcReplaceCachedVersion {
    param($Settings, $FileRecord, $VersionRecord)

    $index = Read-CcReplaceVersionIndex $Settings
    $record = Get-CcReplaceVersionFileRecord $index ([string]$FileRecord.Path) ([string]$FileRecord.FullPath) $false
    if ($null -eq $record) { return }

    $snapshotPath = Get-CcReplaceSnapshotFullPath $Settings $VersionRecord
    if (Test-Path -LiteralPath $snapshotPath) {
        Remove-Item -LiteralPath $snapshotPath -Force
    }

    $remaining = @($record.Versions | Where-Object { [int]$_.Version -ne [int]$VersionRecord.Version })
    $record.Versions = @($remaining)

    $max = 0
    foreach ($version in $remaining) {
        if ([int]$version.Version -gt $max) { $max = [int]$version.Version }
    }
    $record.CurrentVersion = $max

    if ($remaining.Count -eq 0) {
        $index.Files = @($index.Files | Where-Object { [string]$_.Path -ne [string]$record.Path })
    }

    Save-CcReplaceVersionIndex $Settings $index
    Write-Host "Removed cached version $($VersionRecord.Version)." -ForegroundColor Yellow
}

function Show-CcReplaceVersionLogMenu {
    $settings = Read-CcReplaceSettings
    $index = Read-CcReplaceVersionIndex $settings

    while ($true) {
        $index = Normalize-CcReplaceVersionIndex $index
        $files = @($index.Files | Sort-Object Path)

        Write-Host ""
        Write-CcReplaceSectionTitle "Version cache"

        if ($files.Count -eq 0) {
            Write-CcReplaceMutedText "No cached file versions yet."
            Write-Host "Press Enter to go back." -ForegroundColor DarkGray
            [void][Console]::ReadLine()
            return
        }

        for ($i = 0; $i -lt $files.Count; $i++) {
            $file = $files[$i]
            $current = [int]$file.CurrentVersion
            $currentRecord = @($file.Versions | Where-Object { [int]$_.Version -eq $current }) | Select-Object -First 1
            $time = if ($null -ne $currentRecord) { Format-CcReplaceVersionTimestamp ([string]$currentRecord.Timestamp) } else { "unknown time" }

            Write-Host ("{0}. " -f ($i + 1)) -NoNewline
            Write-Host ([string]$file.Path) -ForegroundColor White -NoNewline
            Write-CcReplaceMutedText ("  current v{0}  {1}" -f $current, $time)
        }

        Write-Host "0. Back"
        Write-Host "Pick file: " -NoNewline
        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return }

        $choice = $choice.Trim()
        if ($choice -eq "0") { return }
        if ($choice -notmatch '^\d+$') { continue }

        $fileIndex = [int]$choice - 1
        if ($fileIndex -lt 0 -or $fileIndex -ge $files.Count) { continue }

        $selectedFile = $files[$fileIndex]
        Show-CcReplaceVersionFileMenu $settings $selectedFile
        $index = Read-CcReplaceVersionIndex $settings
    }
}

function Show-CcReplaceVersionFileMenu {
    param($Settings, $FileRecord)

    while ($true) {
        $index = Read-CcReplaceVersionIndex $Settings
        $record = Get-CcReplaceVersionFileRecord $index ([string]$FileRecord.Path) ([string]$FileRecord.FullPath) $false
        if ($null -eq $record) {
            Write-Host "Version record no longer exists." -ForegroundColor Yellow
            return
        }

        $versions = @($record.Versions | Sort-Object Version)
        Write-Host ""
        Write-Host ([string]$record.Path) -ForegroundColor White
        Write-CcReplaceMutedText ("Current Version: {0}" -f [int]$record.CurrentVersion)

        foreach ($version in $versions) {
            $time = Format-CcReplaceVersionTimestamp ([string]$version.Timestamp)
            $reason = [string]$version.Reason
            if ($reason -ne "") {
                Write-Host ("v{0}: {1}  - {2}" -f [int]$version.Version, $time, $reason)
            }
            else {
                Write-Host ("v{0}: {1}" -f [int]$version.Version, $time)
            }
        }

        Write-Host ""
        Write-Host "Pick version number, or 0 to go back: " -NoNewline
        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return }
        $choice = $choice.Trim()
        if ($choice -eq "0") { return }
        if ($choice -notmatch '^\d+$') { continue }

        $selectedVersion = @($versions | Where-Object { [int]$_.Version -eq [int]$choice }) | Select-Object -First 1
        if ($null -eq $selectedVersion) { continue }

        Write-Host ""
        Write-Host ("Selected version {0}" -f [int]$selectedVersion.Version) -ForegroundColor White
        Write-Host "1. Roll back"
        Write-Host "2. Remove"
        Write-Host "0. Back"
        Write-Host "Pick action: " -NoNewline
        $action = [Console]::ReadLine()
        if ($null -eq $action) { return }
        $action = $action.Trim()

        switch ($action) {
            "1" { Invoke-CcReplaceVersionRollback $Settings $record $selectedVersion }
            "2" { Remove-CcReplaceCachedVersion $Settings $record $selectedVersion }
            "0" { }
            default { Write-Host "Unknown action." -ForegroundColor Yellow }
        }
    }
}
function Write-CcReplaceBoolLine {
    param([int]$Index, [string]$Name, [bool]$Value)

    $state = if ($Value) { "ON" } else { "OFF" }
    $color = if ($Value) { "Green" } else { "DarkGray" }
    Write-Host ("{0}. {1}: " -f $Index, $Name) -NoNewline
    Write-Host $state -ForegroundColor $color
}

function Show-CcReplaceSettingsMenu {
    $settings = Read-CcReplaceSettings

    while ($true) {
        Write-Host ""
        Write-Host "CC-REPLACE Settings"
        Write-CcReplaceBoolLine 1 "Preflight statistics" ([bool]$settings.ShowPreflightStatistics)
        Write-CcReplaceBoolLine 2 "File detail statistics" ([bool]$settings.ShowFileDetails)
        Write-CcReplaceBoolLine 3 "Created/removed lists" ([bool]$settings.ShowCreatedRemovedLists)
        Write-CcReplaceBoolLine 4 "Directory prefixes" ([bool]$settings.ShowDirectoryPrefixes)
        Write-CcReplaceBoolLine 5 "Confirmation stage" ([bool]$settings.ConfirmationStage)
        Write-CcReplaceBoolLine 6 "Repeat after incorrect input" ([bool]$settings.RepeatInvalidInput)
        Write-Host "7. Reconfigure default patch file: $($settings.DefaultPatchFile)"
        Write-CcReplaceBoolLine 8 "Version cache" ([bool]$settings.VersionCacheEnabled)
        Write-Host "9. Version log / rollback / remove cached versions"
        Write-Host "10. Reconfigure version cache directory: $($settings.VersionCacheRoot)"
        Write-Host "0. Back"
        Write-Host ""
        Write-Host "Pick setting: " -NoNewline

        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { return $settings }
        $choice = $choice.Trim()

        switch ($choice) {
            "1" { $settings.ShowPreflightStatistics = -not [bool]$settings.ShowPreflightStatistics }
            "2" { $settings.ShowFileDetails = -not [bool]$settings.ShowFileDetails }
            "3" { $settings.ShowCreatedRemovedLists = -not [bool]$settings.ShowCreatedRemovedLists }
            "4" { $settings.ShowDirectoryPrefixes = -not [bool]$settings.ShowDirectoryPrefixes }
            "5" { $settings.ConfirmationStage = -not [bool]$settings.ConfirmationStage }
            "6" { $settings.RepeatInvalidInput = -not [bool]$settings.RepeatInvalidInput }
            "7" {
                Write-Host "Enter new default patch file path: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.DefaultPatchFile = $path.Trim()
                }
            }
            "8" { $settings.VersionCacheEnabled = -not [bool]$settings.VersionCacheEnabled }
            "9" { Show-CcReplaceVersionLogMenu }
            "10" {
                Write-Host "Enter new version cache directory: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    $settings.VersionCacheRoot = $path.Trim()
                }
            }
            "0" {
                Save-CcReplaceSettings $settings
                return $settings
            }
            default {
                Write-Host "Unknown setting." -ForegroundColor Yellow
            }
        }

        Save-CcReplaceSettings $settings
    }
}

function Show-CcReplaceHelp {
    Write-Host ""
    Write-Host "ccReplace.ps1"
    Write-Host "Applies raw BEGIN CC-REPLACE blocks from patch files or pasted text."
    Write-Host ""
    Write-Host "Usage:"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -DryRun"
    Write-Host "  .\ccReplace.ps1 -InputFile .\patch.txt -NoBackup"
    Write-Host "  .\ccReplace.ps1"
    Write-Host "  .\ccReplace.ps1 -AgentMode"
    Write-Host "  .\ccStart.ps1"
    Write-Host "  .\ccReplace.ps1 -Help"
    Write-Host ""
    Write-Host "No-parameter menu:"
    Write-Host "  1. Run using existing default patch file"
    Write-Host "  2. Provide your own patch file path"
    Write-Host "  3. Paste CC-REPLACE text manually"
    Write-Host "  4. Settings"
    Write-Host "  5. Agent Mode: watch the default patch file and apply when it changes"
    Write-Host "  6. Run ccDir.ps1 directory export"
    Write-Host "  7. Run cc.ps1 source/function export"
    Write-Host ""
    Write-Host "Settings file: $(Get-CcReplaceSettingsPath)"
    Write-Host "Settings include UI blocks, directory prefixes, default patch file, confirmation stage, repeat-invalid-input, and version cache."
    Write-Host ""
    Write-Host "Version cache:"
    Write-Host "  Enabled by default unless disabled in Settings."
    Write-Host "  Stored under VersionCacheRoot, default .ccReplace.versions."
    Write-Host "  Existing files get a baseline version before their first CC-REPLACE edit."
    Write-Host "  Every successful non-duplicate file write creates the next file version."
    Write-Host "  Rollback writes the selected snapshot to disk and creates a new latest version."
    Write-Host "  -NoBackup disables version-cache writes for that run."
    Write-Host ""
    Write-Host "Duplicate handling:"
    Write-Host "  Duplicates only: type exactly 'still proceed' to apply duplicate actions."
    Write-Host "  Mixed effective edits + duplicates: type Y/yes/etc to ignore duplicates and apply effective edits, or 'still proceed' to apply all."
    Write-Host "  Any other input cancels unless repeat-invalid-input is enabled."
    Write-Host ""
    Write-Host "Supported modes:"
    Write-Host "  function, insert_after_function, insert_before_function, delete_function, replace_region, append_to_file, whole_file, create_directory, insert_include"
    Write-Host ""
}

function Get-CcReplaceCodeLineCount {
    param([string]$Code)

    return @(Get-TrimmedCodeLineArray $Code).Count
}

function Get-CcReplaceDisplayPath {
    param([string]$PathText, $Settings)

    if ([bool]$Settings.ShowDirectoryPrefixes) {
        return $PathText
    }

    $clean = $PathText.Trim()
    $leaf = Split-Path -Leaf $clean
    if ($leaf -eq "" -or $null -eq $leaf) {
        return $clean
    }

    return $leaf
}

function Write-CcReplaceRule {
    param(
        [string]$Character = "=",
        [int]$Count = 60,
        [string]$Color = "DarkGray"
    )

    if ($Count -lt 1) {
        $Count = 1
    }

    Write-Host ($Character * $Count) -ForegroundColor $Color
}

function Write-CcReplaceEditBorder {
    param(
        [int]$EditNumber,
        [string]$Kind
    )

    $safeNumber = [Math]::Max(1, $EditNumber)
    $safeKind = "START"
    if ($Kind -ne "") {
        $safeKind = $Kind.ToUpperInvariant()
    }

    $esc = [char]27
    $reset = "$esc[0m"

    # Heavy phase wrapper. The label is centered into the same dark-yellow
    # block line so one full ccReplace invocation reads as one edit phase.
    $barColor = "184;134;11"
    $barAnsi = "$esc[38;2;$barColor" + "m"
    $labelAnsi = "$esc[1;30;48;2;$barColor" + "m"

    $labelText = "[EDIT $safeNumber - $safeKind]"
    $totalWidth = 76
    $remaining = [Math]::Max(4, $totalWidth - $labelText.Length)
    $leftCount = [Math]::Max(2, [int][Math]::Floor($remaining / 2.0))
    $rightCount = [Math]::Max(2, $totalWidth - $labelText.Length - $leftCount)

    $line =
        $barAnsi + ("█" * $leftCount) +
        $labelAnsi + $labelText + $reset +
        $barAnsi + ("█" * $rightCount) +
        $reset

    Write-Host $line
}


function Write-CcReplaceSectionTitle {
    param(
        [string]$Title,
        [string]$Color = "Magenta",
        [string]$BorderColor = "DarkMagenta"
    )

    $width = 76
    $safeTitle = ""
    if ($null -ne $Title) {
        $safeTitle = [string]$Title
    }

    $visibleLength = $safeTitle.Length
    $leftPad = [Math]::Max(0, [int][Math]::Floor(($width - $visibleLength) / 2.0))
    $rightPad = [Math]::Max(0, $width - $visibleLength - $leftPad)
    $titleLine = (" " * $leftPad) + $safeTitle + (" " * $rightPad)

    Write-CcReplaceRule "=" $width $BorderColor

    $esc = [char]27
    $reset = "$esc[0m"

    # Near-black indigo gives the title row a subtle "screen panel" texture
    # without crushing readability of the bright status colors around it.
    $fg = "220;120;255"
    $bg = "11;7;18"

    if ($Color -eq "Red") {
        $fg = "255;80;80"
        $bg = "24;6;8"
    }
    elseif ($Color -eq "Yellow") {
        $fg = "255;220;80"
        $bg = "24;18;6"
    }
    elseif ($Color -eq "Cyan") {
        $fg = "80;220;255"
        $bg = "5;18;24"
    }
    elseif ($Color -eq "White") {
        $fg = "240;245;250"
        $bg = "10;12;16"
    }

    $ansi = "$esc[1;38;2;$fg;48;2;$bg" + "m"
    Write-Host ($ansi + $titleLine + $reset)
    Write-CcReplaceRule "=" $width $BorderColor
}
function Write-CcReplaceLabel {
    param(
        [string]$Text,
        [string]$Color = "Magenta"
    )

    Write-Host $Text -ForegroundColor $Color
}
function Write-CcReplaceMutedText {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;190;196;202m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceSummaryText {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;232;236;240m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceInfoAccent {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    # Important numeric/UI accent: warm orange, used for LOC and mode summaries.
    $esc = [char]27
    $value = "$esc[38;2;255;176;80m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Write-CcReplaceBucketToken {
    param(
        [string]$Label,
        [int]$Count,
        [string]$Color,
        [switch]$NoLeadingGap
    )

    if (-not $NoLeadingGap) {
        Write-CcReplaceSummaryText "    " -NoNewline
    }

    Write-Host ("{0}: " -f $Label) -ForegroundColor $Color -NoNewline
    Write-Host $Count -ForegroundColor $Color -NoNewline
}

function Get-CcReplaceBucketCounts {
    param($Entries)

    $items = @($Entries)
    $created = 0
    $changed = 0
    $removed = 0
    $duplicates = 0

    foreach ($entry in $items) {
        $bucket = Get-CcReplaceActionBucket $entry
        switch ($bucket) {
            "created" { $created++ }
            "changed" { $changed++ }
            "removed" { $removed++ }
            "duplicate" { $duplicates++ }
        }
    }

    return [pscustomobject]@{
        Created = $created
        Changed = $changed
        Removed = $removed
        Duplicate = $duplicates
    }
}

function Write-CcReplaceFileBucketStats {
    param($Entries)

    $counts = Get-CcReplaceBucketCounts $Entries

    Write-CcReplaceMutedText "    " -NoNewline
    Write-Host "CR: " -ForegroundColor DarkGreen -NoNewline
    Write-Host $counts.Created -ForegroundColor Green -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "CH: " -ForegroundColor DarkCyan -NoNewline
    Write-Host $counts.Changed -ForegroundColor Cyan -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "RM: " -ForegroundColor DarkRed -NoNewline
    Write-Host $counts.Removed -ForegroundColor Red -NoNewline
    Write-CcReplaceMutedText "  " -NoNewline
    Write-Host "DUP: " -ForegroundColor DarkRed -NoNewline
    Write-Host $counts.Duplicate -ForegroundColor Red -NoNewline
}

function Get-CcReplacePlannedVersionAfter {
    param(
        $Settings,
        [int]$CurrentVersion,
        $EntriesForFile
    )

    $effective = @($EntriesForFile | Where-Object { $_.IsEffective })
    if ($effective.Count -eq 0) {
        return $CurrentVersion
    }

    if (-not [bool]$Settings.VersionCacheEnabled -or $NoBackup) {
        return $CurrentVersion
    }

    $createsFile = @($effective | Where-Object { $_.CreatesFile }).Count -gt 0
    if ($CurrentVersion -le 0 -and -not $createsFile) {
        return 2
    }

    return ($CurrentVersion + 1)
}

function Write-CcReplaceVersionPlan {
    param(
        [int]$CurrentVersion,
        [int]$NextVersion,
        [switch]$NoNewline
    )

    $text = ("v{0}->v{1}" -f $CurrentVersion, $NextVersion)
    Write-CcReplaceVersionAccent $text -NoNewline:$NoNewline
}

function Get-CcReplacePlanVersionBefore {
    param(
        $Entry,
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    if ($null -ne $Entry -and
        ($Entry.PSObject.Properties.Name -contains "VersionBefore")) {
        return [int]$Entry.VersionBefore
    }

    return Get-CcReplaceFileVersionNumber $Settings $TargetHeader $TargetPath
}

function Get-CcReplacePlanVersionAfter {
    param(
        $Entry,
        $Settings,
        [int]$VersionBefore,
        $EntriesForFile
    )

    if ($null -ne $Entry -and
        ($Entry.PSObject.Properties.Name -contains "VersionAfter")) {
        return [int]$Entry.VersionAfter
    }

    return Get-CcReplacePlannedVersionAfter $Settings $VersionBefore $EntriesForFile
}

function Set-CcReplacePlanVersionHints {
    param($Plan, $Settings)

    $items = @($Plan)
    $fileEntries = @($items | Where-Object { -not $_.IsDirectory })
    $groups = @($fileEntries | Group-Object TargetHeader)

    foreach ($group in $groups) {
        $entriesForFile = @($group.Group)
        $firstEntry = $entriesForFile | Select-Object -First 1
        if ($null -eq $firstEntry) {
            continue
        }

        $versionBefore = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
        $versionAfter = Get-CcReplacePlannedVersionAfter $Settings $versionBefore $entriesForFile

        foreach ($entry in $entriesForFile) {
            $entry | Add-Member -NotePropertyName VersionBefore -NotePropertyValue $versionBefore -Force
            $entry | Add-Member -NotePropertyName VersionAfter -NotePropertyValue $versionAfter -Force
        }
    }

    return $items
}


function Write-CcReplaceActionPromptSection {
    Write-Host ""

    $width = 76
    $title = "  >>> ACTIONS"
    $titleLine = $title + (" " * [Math]::Max(0, $width - $title.Length))

    Write-CcReplaceRule "=" $width "DarkMagenta"

    $esc = [char]27
    $reset = "$esc[0m"

    # Keep the action phase in the same framed UI language as the other
    # sections, but make it left-aligned and white so it reads as the actual
    # decision area instead of another centered information title.
    $fg = "245;245;245"
    $bg = "11;7;18"
    $ansi = "$esc[1;38;2;$fg;48;2;$bg" + "m"

    Write-Host ($ansi + $titleLine + $reset)
    Write-CcReplaceRule "=" $width "DarkMagenta"
}

function Start-CcReplaceEditPhase {
    if ($null -eq $script:CcReplaceEditPhaseNumber) {
        $script:CcReplaceEditPhaseNumber = 0
    }

    $script:CcReplaceEditPhaseNumber++

    Write-Host ""
    Write-CcReplaceEditBorder $script:CcReplaceEditPhaseNumber "START"
}

function Stop-CcReplaceEditPhase {
    $editNumber = 1
    if ($null -ne $script:CcReplaceEditPhaseNumber) {
        $editNumber = [int]$script:CcReplaceEditPhaseNumber
    }

    Write-CcReplaceEditBorder $editNumber "END"
}



function Write-CcReplaceVersionAccent {
    param(
        [string]$Text,
        [switch]$NoNewline
    )

    $esc = [char]27
    $value = "$esc[38;2;105;185;255m$Text$esc[0m"

    if ($NoNewline) {
        Write-Host $value -NoNewline
    }
    else {
        Write-Host $value
    }
}

function Get-CcReplaceFileVersionNumber {
    param(
        $Settings,
        [string]$TargetHeader,
        [string]$TargetPath
    )

    try {
        $index = Read-CcReplaceVersionIndex $Settings
        $record = Get-CcReplaceVersionFileRecord $index $TargetHeader $TargetPath $false

        if ($null -eq $record) {
            return 0
        }

        return [int]$record.CurrentVersion
    }
    catch {
        return 0
    }
}

function Write-CcReplaceFileVersionBadge {
    param([int]$Version)

    Write-CcReplaceVersionAccent ("v{0}" -f $Version) -NoNewline
}

function Write-CcReplaceLoc {
    param([int]$Loc)

    Write-Host "$Loc LOC" -ForegroundColor White -NoNewline
}

function Write-CcReplaceDelta {
    param([int]$Added, [int]$Removed)

    Write-Host "+$Added" -ForegroundColor Green -NoNewline
    Write-Host " " -NoNewline
    Write-Host "-$Removed" -ForegroundColor Red -NoNewline
}

function New-CcReplacePlanEntry {
    param(
        $Block,
        [string]$Mode,
        [string]$TargetHeader,
        [string]$TargetPath,
        [string]$Name,
        [string]$ActionLabel,
        [string]$PartLabel,
        [bool]$IsDirectory,
        [bool]$IsDuplicate,
        [string]$DuplicateAction,
        [string]$DuplicateTarget,
        [int]$Added,
        [int]$Removed,
        [int]$TotalLocAfter,
        $NewLines,
        [string]$Newline,
        [bool]$SkipWrite,
        [bool]$CreatesFile,
        [bool]$CreatesDirectory
    )

    return [pscustomobject]@{
        Block = $Block
        Mode = $Mode
        TargetHeader = $TargetHeader
        TargetPath = $TargetPath
        Name = $Name
        ActionLabel = $ActionLabel
        PartLabel = $PartLabel
        IsDirectory = $IsDirectory
        IsDuplicate = $IsDuplicate
        IsEffective = (-not $IsDuplicate)
        DuplicateAction = $DuplicateAction
        DuplicateTarget = $DuplicateTarget
        Added = $Added
        Removed = $Removed
        TotalLocAfter = $TotalLocAfter
        NewLines = $NewLines
        Newline = $Newline
        SkipWrite = $SkipWrite
        CreatesFile = $CreatesFile
        CreatesDirectory = $CreatesDirectory
    }
}

function Analyze-CcReplaceBlock {
    param($Block)

    $headers = $Block.Headers

    $mode = "function"
    if ($headers.ContainsKey("MODE")) {
        $mode = $headers["MODE"].ToLowerInvariant()
    }

    $targetHeader = Get-TargetHeader $headers
    $targetPath = Resolve-TargetPath $targetHeader

    $name = ""
    if ($headers.ContainsKey("NAME")) {
        $name = $headers["NAME"]
    }

    if ($mode -eq "create_directory") {
        $exists = Test-Path -LiteralPath $targetPath
        $dupAction = if ($exists) { "Directory already exists" } else { "" }
        return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "create_directory" $targetHeader $true $exists $dupAction $targetHeader 0 0 0 $null "`n" $false $false (-not $exists)
    }

    if ($mode -ne "whole_file" -and -not (Test-Path -LiteralPath $targetPath)) {
        throw "Target file not found: $targetPath"
    }

    $lines = @()
    $newline = "`n"
    $fileExists = Test-Path -LiteralPath $targetPath

    if ($fileExists) {
        $read = Read-TargetLines $targetPath
        $lines = @(ConvertTo-LineArray $read.Lines)
        $newline = $read.Newline
    }

    switch ($mode) {
        "function" {
            if ($name -eq "") { throw "MODE:function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            Assert-OptionalHashMatches $headers $lines $range.Start $range.End "$targetHeader :: $name"
            $isDup = Test-CodeRegionMatches $lines $range.Start $range.End $Block.Code
            $newLines = Replace-LineRange $lines $range.Start $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "function" $name $false $isDup "Function body already matches patch" "$targetHeader :: $name" $added $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_after_function" {
            if ($name -eq "") { throw "MODE:insert_after_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_after_function" "after $name" $false $isDup "Inserted code block already exists in file" "$targetHeader :: after $name" $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_before_function" {
            if ($name -eq "") { throw "MODE:insert_before_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $range.Start $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_before_function" "before $name" $false $isDup "Inserted code block already exists in file" "$targetHeader :: before $name" $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "delete_function" {
            if ($name -eq "") { throw "MODE:delete_function requires NAME." }
            $range = Find-FunctionRange $targetPath $lines $name
            $newLines = Replace-LineRange $lines $range.Start $range.End ""
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "delete_function" $name $false $false "" "" 0 $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "replace_region" {
            if ($name -eq "") { throw "MODE:replace_region requires NAME." }
            $range = Find-MarkerRange $lines $name
            Assert-OptionalHashMatches $headers $lines $range.Start $range.End "$targetHeader :: $name"
            $isDup = Test-CodeRegionMatches $lines $range.Start $range.End $Block.Code
            $newLines = Replace-LineRange $lines $range.Start $range.End $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            $removed = $range.End - $range.Start
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "replace_region" $name $false $isDup "Marker region already matches patch" "$targetHeader :: $name" $added $removed @($newLines).Count $newLines $newline $false $false $false
        }

        "insert_include" {
            $header = Get-IncludeHeaderValue $headers
            $includeResult = Insert-IncludeLine $lines $header
            $newLines = $includeResult.Lines
            $isDup = -not [bool]$includeResult.Changed
            $added = if ($isDup) { 0 } else { 1 }
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "insert_include" "#include $header" $false $isDup "Include already exists" "$targetHeader :: #include $header" $added 0 @($newLines).Count $newLines $newline $isDup $false $false
        }

        "append_to_file" {
            $isDup = Test-CodeBlockAlreadyInLines $lines $Block.Code
            $newLines = Insert-LinesAt $lines $lines.Count $Block.Code
            $added = Get-CcReplaceCodeLineCount $Block.Code
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name "append_to_file" $targetHeader $false $isDup "Append block already exists in file" $targetHeader $added 0 @($newLines).Count $newLines $newline $false $false $false
        }

        "whole_file" {
            $trimmed = Trim-CodeBlankEdges $Block.Code
            if ($trimmed -eq "") {
                $newLines = @()
            }
            else {
                $newLines = @(ConvertTo-LineArray $trimmed)
            }

            $isDup = ($fileExists -and (Test-LineArraysEqual $lines $newLines))
            $added = @($newLines).Count
            $removed = if ($fileExists) { @($lines).Count } else { 0 }
            $action = if ($fileExists) { "whole_file" } else { "create_file" }
            $dupAction = if ($isDup) { "Whole file already matches patch" } else { "" }
            return New-CcReplacePlanEntry $Block $mode $targetHeader $targetPath $name $action $targetHeader $false $isDup $dupAction $targetHeader $added $removed @($newLines).Count $newLines $newline $false (-not $fileExists) $false
        }

        default {
            throw "Unknown MODE: $mode"
        }
    }
}

function Analyze-CcReplaceBlocks {
    param($Blocks)

    $plan = New-Object System.Collections.Generic.List[object]

    foreach ($block in @($Blocks)) {
        $plan.Add((Analyze-CcReplaceBlock $block))
    }

    return @($plan.ToArray())
}


function Write-CcReplaceActionSummary {
    param($Entries, $Settings, [string]$Title)

    $items = @($Entries)
    if ($items.Count -eq 0) {
        return
    }

    Write-CcReplaceCompactPlan $items @() $Settings $Title
}

function Write-CcReplaceEffectiveIntro {
    param($EffectiveEntries, $DuplicateEntries, $Settings)

    $effective = @($EffectiveEntries)
    $duplicates = @($DuplicateEntries)

    Write-Host ""
    Write-Host "Effective Edits:`t $($effective.Count)    Duplicates (if present): $($duplicates.Count)"

    if ($effective.Count -eq 0) {
        return
    }

    Write-Host ""
    Write-Host "You are now going to effectively edit:"
    foreach ($entry in $effective | Where-Object { -not $_.IsDirectory }) {
        $display = Get-CcReplaceDisplayPath $entry.TargetHeader $Settings
        Write-Host "$display " -NoNewline
        Write-CcReplaceDelta ([int]$entry.Added) ([int]$entry.Removed)
        Write-Host ""
    }

    $groups = @($effective | Where-Object { -not $_.IsDirectory } | Group-Object TargetHeader)
    foreach ($group in $groups) {
        $display = Get-CcReplaceDisplayPath $group.Name $Settings
        Write-Host ""
        Write-Host $display
        Write-Host "changing:"
        foreach ($entry in $group.Group | Where-Object { $_.Removed -gt 0 -and $_.Added -gt 0 -and -not $_.CreatesFile }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta ([int]$entry.Added) ([int]$entry.Removed)
            Write-Host ")"
        }
        Write-Host "creating:"
        foreach ($entry in $group.Group | Where-Object { $_.Added -gt 0 -and ($_.Removed -eq 0 -or $_.CreatesFile) }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta ([int]$entry.Added) 0
            Write-Host ")"
        }
        Write-Host "removing:"
        foreach ($entry in $group.Group | Where-Object { $_.Removed -gt 0 -and $_.Added -eq 0 }) {
            Write-Host "  $($entry.PartLabel) (" -NoNewline
            Write-CcReplaceDelta 0 ([int]$entry.Removed)
            Write-Host ")"
        }
    }
}


function Write-CcReplaceModeCountsInline {
    param($Entries, [string]$Label, [string]$Color)

    $items = @($Entries)
    if ($items.Count -eq 0) {
        return
    }

    $modes = @("function", "insert_after_function", "insert_before_function", "delete_function", "replace_region", "append_to_file", "whole_file", "create_directory", "insert_include")
    $parts = New-Object System.Collections.Generic.List[string]

    foreach ($mode in $modes) {
        $count = @($items | Where-Object { $_.Mode -eq $mode }).Count
        if ($count -gt 0) {
            $parts.Add(("{0}={1}" -f $mode, $count))
        }
    }

    Write-CcReplaceSummaryText "$Label`: " -NoNewline
    Write-CcReplaceInfoAccent ($parts -join ", ")
}
function Get-CcReplaceActionKind {
    param($Entry)

    if ([bool]$Entry.IsDuplicate) {
        return "DUP"
    }

    if ([bool]$Entry.IsDirectory) {
        if ([bool]$Entry.CreatesDirectory) {
            return "CREATE DIR"
        }
        return "CHANGE DIR"
    }

    if ([bool]$Entry.CreatesFile) {
        return "CREATE FILE"
    }

    switch ($Entry.Mode) {
        "delete_function" { return "REMOVE" }
        "insert_after_function" { return "CREATE" }
        "insert_before_function" { return "CREATE" }
        "append_to_file" { return "CREATE" }
        "insert_include" { return "CREATE" }
        default { return "CHANGE" }
    }
}

function Get-CcReplaceActionBucket {
    param($Entry)

    $kind = Get-CcReplaceActionKind $Entry

    if ($kind -eq "DUP") { return "duplicate" }
    if ($kind.StartsWith("CREATE")) { return "created" }
    if ($kind.StartsWith("REMOVE")) { return "removed" }
    return "changed"
}

function Get-CcReplaceKindColor {
    param([string]$Kind)

    if ($Kind.StartsWith("DUP")) { return "Red" }
    if ($Kind.StartsWith("CREATE")) { return "Green" }
    if ($Kind.StartsWith("REMOVE")) { return "Red" }
    return "Cyan"
}

function Write-CcReplaceKindTag {
    param([string]$Kind)

    $color = Get-CcReplaceKindColor $Kind
    Write-Host "[" -NoNewline
    Write-Host $Kind -ForegroundColor $color -NoNewline
    Write-Host "]" -NoNewline
}

function Write-CcReplaceBucketSummary {
    param($Entries)

    $counts = Get-CcReplaceBucketCounts $Entries

    Write-CcReplaceBucketToken "created" $counts.Created "Green" -NoLeadingGap
    Write-CcReplaceBucketToken "changed" $counts.Changed "Cyan"
    Write-CcReplaceBucketToken "removed" $counts.Removed "Red"
    Write-CcReplaceBucketToken "duplicate" $counts.Duplicate "Red"
    Write-Host ""
}
function Write-CcReplaceAggregateBadge {
    param(
        [string]$Kind,
        [int]$Count,
        [int]$Added,
        [int]$Removed,
        [bool]$WouldRepeat = $false
    )

    if ($Count -le 0) {
        return
    }

    Write-Host "  " -NoNewline
    Write-CcReplaceKindTag ("{0} {1}" -f $Kind, $Count)
    if ($WouldRepeat) {
        Write-Host " would repeat " -ForegroundColor Yellow -NoNewline
    }
    else {
        Write-Host " " -NoNewline
    }
    Write-CcReplaceDelta $Added $Removed
}

function Write-CcReplacePlanRow {
    param($Entry, $Settings)

    $kind = Get-CcReplaceActionKind $Entry
    $label = $Entry.PartLabel
    if ($null -eq $label -or $label -eq "") {
        $label = $Entry.TargetHeader
    }

    Write-CcReplaceMutedText "    " -NoNewline
    Write-CcReplaceKindTag $kind
    Write-CcReplaceMutedText " $label (" -NoNewline
    Write-CcReplaceDelta ([int]$Entry.Added) ([int]$Entry.Removed)
    Write-CcReplaceMutedText ")"
}

function Write-CcReplaceCompactPlan {
    param(
        $EffectiveEntries,
        $DuplicateEntries,
        $Settings,
        [string]$Title = "Preflight patch plan"
    )

    $effective = @($EffectiveEntries)
    $duplicates = @($DuplicateEntries)
    $allEntries = @($effective + $duplicates)

    $fileEntries = @($allEntries | Where-Object { -not $_.IsDirectory })
    $effectiveFileEntries = @($effective | Where-Object { -not $_.IsDirectory })
    $fileGroups = @($fileEntries | Group-Object TargetHeader)

    $filesAffected = $fileGroups.Count

    $addedAffected = 0
    $removedAffected = 0
    foreach ($entry in $effectiveFileEntries) {
        $addedAffected += [int]$entry.Added
        $removedAffected += [int]$entry.Removed
    }
    $locAffected = $addedAffected + $removedAffected

    Write-CcReplaceSectionTitle $Title

    Write-CcReplaceSummaryText "Total LOC stats: " -NoNewline
    Write-CcReplaceDelta $addedAffected $removedAffected
    Write-CcReplaceMutedText " | " -NoNewline
    Write-CcReplaceInfoAccent ("{0} total affected LOC" -f $locAffected)
    Write-CcReplaceSummaryText "Total Files Affected: " -NoNewline
    Write-CcReplaceInfoAccent ([string]$filesAffected)

    $fileIndex = 1
    foreach ($group in $fileGroups) {
        $entriesForFile = @($group.Group)
        $effectiveForFile = @($effective | Where-Object { -not $_.IsDirectory -and $_.TargetHeader -eq $group.Name })

        $fileAdded = 0
        $fileRemoved = 0
        $locAfter = 0
        foreach ($entry in $effectiveForFile) {
            $fileAdded += [int]$entry.Added
            $fileRemoved += [int]$entry.Removed
        }
        foreach ($entry in $entriesForFile) {
            if ([int]$entry.TotalLocAfter -gt 0) {
                $locAfter = [int]$entry.TotalLocAfter
            }
        }

        $display = Get-CcReplaceDisplayPath $group.Name $Settings
        $firstEntry = $entriesForFile | Select-Object -First 1
        $versionNumber = 0
        if ($null -ne $firstEntry) {
            $versionNumber = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
        }
        $nextVersion = Get-CcReplacePlannedVersionAfter $Settings $versionNumber $entriesForFile

        Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-Host $display -ForegroundColor White -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-CcReplaceVersionPlan $versionNumber $nextVersion -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-CcReplaceDelta $fileAdded $fileRemoved
        Write-CcReplaceMutedText " | " -NoNewline
        Write-CcReplaceLoc $locAfter
        Write-Host ""

        $fileIndex++
    }

    Write-Host ""

    Write-CcReplaceSummaryText "Effective actions: " -NoNewline
    Write-Host $effective.Count -ForegroundColor Green -NoNewline
    Write-CcReplaceSummaryText "    Duplicate actions: " -NoNewline
    Write-Host $duplicates.Count -ForegroundColor Red

    Write-CcReplaceBucketSummary $allEntries

    Write-Host ""
    Write-CcReplaceModeCountsInline $effective "effective modes" "DarkGray"
    Write-CcReplaceModeCountsInline $duplicates "duplicates" "DarkGray"

    if ($fileEntries.Count -gt 0) {
        Write-CcReplaceSectionTitle "Implementation Plan"

        $fileIndex = 1
        $firstFile = $true
        foreach ($group in $fileGroups) {
            if (-not $firstFile) {
                Write-Host ""
            }
            $firstFile = $false

            $entriesForFile = @($group.Group)
            $effectiveForFile = @($effective | Where-Object { -not $_.IsDirectory -and $_.TargetHeader -eq $group.Name })

            $fileAdded = 0
            $fileRemoved = 0
            $locAfter = 0
            foreach ($entry in $effectiveForFile) {
                $fileAdded += [int]$entry.Added
                $fileRemoved += [int]$entry.Removed
            }
            foreach ($entry in $entriesForFile) {
                if ([int]$entry.TotalLocAfter -gt 0) {
                    $locAfter = [int]$entry.TotalLocAfter
                }
            }

            $display = Get-CcReplaceDisplayPath $group.Name $Settings
            $firstEntry = $entriesForFile | Select-Object -First 1
            $versionNumber = 0
            if ($null -ne $firstEntry) {
                $versionNumber = Get-CcReplaceFileVersionNumber $Settings $group.Name ([string]$firstEntry.TargetPath)
            }
            $nextVersion = Get-CcReplacePlannedVersionAfter $Settings $versionNumber $entriesForFile

            Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-Host $display -ForegroundColor White -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-CcReplaceVersionPlan $versionNumber $nextVersion -NoNewline
            Write-CcReplaceMutedText " " -NoNewline
            Write-CcReplaceDelta $fileAdded $fileRemoved
            Write-CcReplaceMutedText " | " -NoNewline
            Write-CcReplaceLoc $locAfter
            Write-Host ""

            $orderedRows = @(
                $entriesForFile | Sort-Object `
                    @{ Expression = {
                        $bucket = Get-CcReplaceActionBucket $_
                        switch ($bucket) {
                            "created" { 0 }
                            "changed" { 1 }
                            "removed" { 2 }
                            "duplicate" { 3 }
                            default { 9 }
                        }
                    } },
                    @{ Expression = { $_.PartLabel } }
            )

            foreach ($entry in $orderedRows) {
                Write-CcReplacePlanRow $entry $Settings
            }

            $fileIndex++
        }
    }

    $directoryEntries = @($allEntries | Where-Object { $_.IsDirectory })
    if ($directoryEntries.Count -gt 0) {
        Write-CcReplaceSectionTitle "Directories"

        foreach ($entry in $directoryEntries) {
            $kind = Get-CcReplaceActionKind $entry
            $display = Get-CcReplaceDisplayPath $entry.TargetHeader $Settings
            Write-CcReplaceMutedText "  " -NoNewline
            Write-CcReplaceKindTag $kind
            Write-CcReplaceMutedText " " -NoNewline
            Write-Host $display -ForegroundColor White
        }
    }
}
function Write-CcReplaceDuplicateWarning {
    param($DuplicateEntries, [bool]$MixedWithEffective)

    $duplicates = @($DuplicateEntries)
    if ($duplicates.Count -eq 0) {
        return
    }

    Write-Host ""
    Write-CcReplaceSectionTitle "WARNING! (NOT RECOMMENDED)" "Red" "DarkRed"

    Write-CcReplaceSummaryText "You have " -NoNewline
    Write-Host $duplicates.Count -ForegroundColor Red -NoNewline
    Write-CcReplaceSummaryText " duplicate actions:"
    Write-Host ""

    $groups = $duplicates | Group-Object TargetHeader
    $fileIndex = 1
    foreach ($group in $groups) {
        Write-Host ("{0}." -f $fileIndex) -ForegroundColor Yellow -NoNewline
        Write-CcReplaceMutedText " " -NoNewline
        Write-Host $group.Name -ForegroundColor White

        foreach ($entry in $group.Group) {
            if ($entry.Name -ne "") {
                Write-Host ("  > {0}:" -f $entry.Name) -ForegroundColor DarkCyan
            }
            else {
                Write-Host ("  > {0}:" -f $entry.PartLabel) -ForegroundColor DarkCyan
            }

            Write-CcReplaceMutedText "    action: " -NoNewline
            Write-Host $entry.DuplicateReason -ForegroundColor Yellow
            Write-CcReplaceMutedText "    target: " -NoNewline
            Write-Host $entry.TargetDescription -ForegroundColor White
        }

        Write-Host ""
        $fileIndex++
    }

    Write-CcReplaceActionPromptSection

    if ($MixedWithEffective) {
        Write-Host "1.  To proceed with caution, ignoring duplicates, type:" -ForegroundColor Cyan
        Write-Host "    1 or Y" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "F.  To proceed without ignorance, applying duplicates, type:" -ForegroundColor Cyan
        Write-Host "    F or still proceed" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }
    else {
        Write-Host "F.  To proceed without ignorance, type:" -ForegroundColor Cyan
        Write-Host "    F or still proceed" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }
}

function Read-CcReplaceDecision {
    param(
        [string]$Prompt,
        [string[]]$AllowedDescriptions
    )

    foreach ($line in $AllowedDescriptions) {
        if ($line -ne "") {
            Write-Host $line
        }
    }

    while ($true) {
        Write-Host ""
        $answer = [Console]::ReadLine()
        if ($null -eq $answer) {
            return ""
        }

        $clean = $answer.Trim()
        if ($clean -ne "") {
            return $clean
        }

        $settings = Read-CcReplaceSettings
        if (-not [bool]$settings.RepeatInvalidInput) {
            return ""
        }

        Write-Host "Invalid input. Try again." -ForegroundColor Yellow
    }
}


function Get-CcPipelineScriptPath {
    param([string]$ScriptName)

    $candidates = @()

    if ($PSScriptRoot -ne "") {
        $candidates += (Join-Path $PSScriptRoot $ScriptName)
    }

    $candidates += (Join-Path (Get-Location).Path $ScriptName)

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return ""
}

function Invoke-CcPipelineCommand {
    param([string]$Command)

    $clean = ""
    if ($null -ne $Command) {
        $clean = $Command.Trim()
    }

    if ($clean -eq "") {
        return $false
    }

    $projectRoot = (Get-Location).Path

    if ($clean -ieq "DIR") {
        $scriptPath = Get-CcPipelineScriptPath "ccDir.ps1"
        if ($scriptPath -eq "") {
            Write-Host "ccDir.ps1 was not found next to ccReplace.ps1 or in the current directory." -ForegroundColor Yellow
            return $true
        }

        $outputFile = Join-Path $projectRoot "cc_project_dir.md"

        Write-Host ""
        Write-Host "Running Context Control directory export..." -ForegroundColor Cyan
        Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray
        Write-Host "Output file:  $outputFile" -ForegroundColor DarkGray
        & $scriptPath -OutputFile $outputFile

        if (Test-Path -LiteralPath $outputFile) {
            Write-Host "Agent export verified: $outputFile" -ForegroundColor Green
        }
        else {
            Write-Host "WARNING: ccDir.ps1 returned, but expected output was not found: $outputFile" -ForegroundColor Yellow
        }

        Write-Host "Returned to Context Control." -ForegroundColor DarkGray
        return $true
    }

    if ($clean -ieq "CC") {
        $scriptPath = Get-CcPipelineScriptPath "cc.ps1"
        if ($scriptPath -eq "") {
            Write-Host "cc.ps1 was not found next to ccReplace.ps1 or in the current directory." -ForegroundColor Yellow
            return $true
        }

        $outputFile = Join-Path $projectRoot "cc_code_export.md"

        Write-Host ""
        Write-Host "Running Context Control source export..." -ForegroundColor Cyan
        Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray
        Write-Host "Output file:  $outputFile" -ForegroundColor DarkGray
        & $scriptPath -OutputFile $outputFile

        if (Test-Path -LiteralPath $outputFile) {
            Write-Host "Agent export verified: $outputFile" -ForegroundColor Green
        }
        else {
            Write-Host "WARNING: cc.ps1 returned, but expected output was not found: $outputFile" -ForegroundColor Yellow
        }

        Write-Host "Returned to Context Control." -ForegroundColor DarkGray
        return $true
    }

    return $false
}

function Write-CcPipelineActionHints {
    Write-Host ""
    Write-Host "Pipeline helpers:" -ForegroundColor DarkCyan
    Write-Host "DIR. Run ccDir.ps1 to export the project tree without leaving this screen." -ForegroundColor Cyan
    Write-Host "CC.  Run cc.ps1 to export selected files/functions without leaving this screen." -ForegroundColor Cyan
}

function Get-CcReplacePreflightDecision {
    param($Plan, $Settings)

    $effective = @($Plan | Where-Object { $_.IsEffective })
    $duplicates = @($Plan | Where-Object { $_.IsDuplicate })
    $hasEffective = $effective.Count -gt 0
    $hasDuplicates = $duplicates.Count -gt 0

    if ([bool]$Settings.ShowPreflightStatistics) {
        Write-CcReplaceCompactPlan $effective $duplicates $Settings
    }

    if (-not [bool]$Settings.ConfirmationStage) {
        if ($hasDuplicates) {
            Write-Host ""
            Write-Host "Confirmation stage is OFF: duplicate actions will be ignored." -ForegroundColor Yellow
        }
        return "effective_only"
    }

    if ($hasDuplicates) {
        Write-CcReplaceDuplicateWarning $duplicates $hasEffective
    }

    if ($hasEffective -and -not $hasDuplicates) {
        Write-CcReplaceActionPromptSection
        Write-Host "1.  To proceed, type:" -ForegroundColor Cyan
        Write-Host "    1 or Y" -ForegroundColor Yellow
        Write-CcPipelineActionHints
    }

    while ($true) {
        Write-Host ""
        Write-Host "Your Choice: " -ForegroundColor Cyan -NoNewline
        $answer = [Console]::ReadLine()
        if ($null -eq $answer) { $answer = "" }
        $clean = $answer.Trim()

        if (Invoke-CcPipelineCommand $clean) {
            Write-CcReplaceActionPromptSection
            if ($hasDuplicates -and $hasEffective) {
                Write-Host "Back at patch decision. Type 1/Y to apply effective edits, F/still proceed to include duplicates, or use DIR/CC again." -ForegroundColor DarkGray
            }
            elseif ($hasDuplicates) {
                Write-Host "Back at patch decision. Type F/still proceed to apply duplicates, or use DIR/CC again." -ForegroundColor DarkGray
            }
            else {
                Write-Host "Back at patch decision. Type 1/Y to apply, or use DIR/CC again." -ForegroundColor DarkGray
            }
            continue
        }

        if ($hasDuplicates -and ($clean -ieq "still proceed" -or $clean -ieq "F")) {
            return "all"
        }

        if ($hasEffective -and ($clean -eq "1" -or $clean -match '^[Yy]')) {
            return "effective_only"
        }

        if (-not [bool]$Settings.RepeatInvalidInput) {
            Write-Host "cancelled"
            return "cancel"
        }

        if ($hasDuplicates -and $hasEffective) {
            Write-Host "Invalid input. Type 1/Y to apply effective edits, F/still proceed to apply duplicates too, or Ctrl+C to stop." -ForegroundColor Yellow
        }
        elseif ($hasDuplicates) {
            Write-Host "Invalid input. Type F or still proceed, or Ctrl+C to stop." -ForegroundColor Yellow
        }
        else {
            Write-Host "Invalid input. Type 1 or Y, or Ctrl+C to stop." -ForegroundColor Yellow
        }
    }
}

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

function Invoke-CcReplaceText {
    param(
        [string]$Text,
        [switch]$NoExitOnCancel
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
        $decision = Get-CcReplacePreflightDecision $plan $settings

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
        [switch]$NoExitOnCancel
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Input file not found: $Path"
    }

    $text = Read-TextFileAutoEncoding $Path
    return (Invoke-CcReplaceText $text -NoExitOnCancel:$NoExitOnCancel)
}

function Show-CcReplaceMainMenu {
    $settings = Read-CcReplaceSettings

    while ($true) {
        Write-Host ""
        Write-Host "CC-REPLACE Menu"
        Write-Host "1. Run using existing $($settings.DefaultPatchFile)"
        Write-Host "2. Provide your own filepath to another patch file"
        Write-Host "3. Paste your own text"
        Write-Host "4. Settings"
        Write-Host "5. Agent Mode - watch patch.txt and apply when it changes"
        Write-Host "6. Run ccDir.ps1 directory export"
        Write-Host "7. Run cc.ps1 source/function export"
        Write-Host "0. Exit"
        Write-Host ""
        Write-Host "Pick option: " -NoNewline

        $choice = [Console]::ReadLine()
        if ($null -eq $choice) { exit 1 }
        $choice = $choice.Trim()

        switch ($choice) {
            "1" { Invoke-CcReplaceFile (Resolve-TargetPath $settings.DefaultPatchFile); return }
            "2" {
                Write-Host "Patch file path: " -NoNewline
                $path = [Console]::ReadLine()
                if ($null -ne $path -and $path.Trim() -ne "") {
                    Invoke-CcReplaceFile $path.Trim()
                    return
                }
            }
            "3" { Invoke-CcReplaceText (Read-PasteInputText); return }
            "4" { $settings = Show-CcReplaceSettingsMenu }
            "5" { Invoke-CcReplaceAgentMode; return }
            "6" { [void](Invoke-CcPipelineCommand "DIR") }
            "7" { [void](Invoke-CcPipelineCommand "CC") }
            "0" { return }
            default { Write-Host "Unknown menu option." -ForegroundColor Yellow }
        }
    }
}

function Invoke-CcReplaceAgentMode {
    $settings = Read-CcReplaceSettings
    $path = Resolve-TargetPath $settings.DefaultPatchFile
    $lastWrite = $null

    Write-Host ""
    Write-Host "Agent Mode active. Watching: $path"
    Write-Host "Update the patch file to trigger a run. Press Ctrl+C to stop."
    Write-Host "Type DIR + Enter to run ccDir.ps1, or CC + Enter to run cc.ps1 while the watcher stays alive." -ForegroundColor DarkCyan

    while ($true) {
        try {
            if ([Console]::KeyAvailable) {
                Write-Host ""
                Write-Host "Agent command: " -ForegroundColor Cyan -NoNewline
                $agentCommand = [Console]::ReadLine()
                if (-not (Invoke-CcPipelineCommand $agentCommand)) {
                    if ($null -ne $agentCommand -and $agentCommand.Trim() -ne "") {
                        Write-Host "Unknown agent command. Use DIR, CC, or Ctrl+C." -ForegroundColor Yellow
                    }
                }
                Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
            }
        }
        catch {
            # Some hosts do not support Console.KeyAvailable. Patch watching still works.
        }

        if (Test-Path -LiteralPath $path) {
            $item = Get-Item -LiteralPath $path
            $write = $item.LastWriteTimeUtc

            if ($null -eq $lastWrite) {
                $lastWrite = $write
            }
            elseif ($write -ne $lastWrite) {
                Start-Sleep -Milliseconds 250
                $lastWrite = (Get-Item -LiteralPath $path).LastWriteTimeUtc
                Write-Host ""
                Write-Host "Patch update detected: $path"
                try {
                    [void](Invoke-CcReplaceFile $path -NoExitOnCancel)
                    Write-Host ""
                    Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
                }
                catch {
                    Write-Host "Agent Mode patch failed: $($_.Exception.Message)" -ForegroundColor Red
                    Write-Host "Agent Mode waiting for next patch update..." -ForegroundColor DarkGray
                }
            }
        }

        Start-Sleep -Milliseconds 750
    }
}


function Get-IncludeHeaderValue {
    param($Headers)

    $header = Get-HeaderValue $Headers "HEADER"
    if ($header -eq "") {
        $header = Get-HeaderValue $Headers "INCLUDE"
    }

    return $header.Trim()
}

function Insert-IncludeLine {
    param(
        $Lines,
        [string]$HeaderText
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $header = $HeaderText.Trim()

    if ($header -eq "") {
        throw 'MODE:insert_include requires HEADER: <...> or HEADER: "...".'
    }

    if ($header -notmatch '^(<[^>]+>|"[^"]+")$') {
        throw ('Invalid HEADER for insert_include: {0}. Use <system> or "local" include style.' -f $header)
    }

    $includeLine = "#include $header"
    $escaped = [regex]::Escape($header)

    foreach ($line in $safeLines) {
        if ($line -match ('^\s*#\s*include\s+' + $escaped + '\s*(//.*)?$')) {
            return [pscustomobject]@{
                Lines = [string[]]$safeLines
                Changed = $false
            }
        }
    }

    $insertAt = 0
    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        if ($safeLines[$i] -match '^\s*#\s*include\b') {
            $insertAt = $i + 1
        }
    }

    $newLines = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $insertAt; $i++) {
        $newLines.Add($safeLines[$i])
    }

    $newLines.Add($includeLine)

    for ($i = $insertAt; $i -lt $safeLines.Count; $i++) {
        $newLines.Add($safeLines[$i])
    }

    return [pscustomobject]@{
        Lines = [string[]]$newLines.ToArray()
        Changed = $true
    }
}

function Apply-Block {
    param($Block, $BackedUpMap)

    $settings = Read-CcReplaceSettings
    $entry = Analyze-CcReplaceBlock $Block
    return Apply-CcReplacePlanEntry $entry $BackedUpMap $settings
}

if ($Help) {
    Show-CcReplaceHelp
    exit 0
}

if ($AgentMode) {
    Invoke-CcReplaceAgentMode
    exit 0
}

if ($InputFile -eq "") {
    Show-CcReplaceMainMenu
    exit 0
}

Invoke-CcReplaceFile $InputFile
