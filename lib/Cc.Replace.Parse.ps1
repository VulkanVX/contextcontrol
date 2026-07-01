# CC-DESC: Parses CC-REPLACE input text and patch blocks.

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

            if (@("insert_include", "delete_function", "create_directory") -notcontains $mode) {
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
