# CC-DESC: Hash and function-range scanning helpers for Context Control source exports.

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
        return ""
    }

    if ($Start -eq $End) {
        return Get-Sha1Short ""
    }

    return Get-Sha1Short (($safeLines[$Start..($End - 1)]) -join "`n")
}

function Strip-CodeLineForHashScan {
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

function Test-LooksLikeHashableFunctionPrefix {
    param([string]$Prefix)

    $p = $Prefix.Trim()

    if ($p -eq "") {
        return $true
    }

    if ($p -match '(=|,|\[|\]|\.)') {
        return $false
    }

    if ($p -match '\b(return|if|while|for|switch|case|sizeof|new|delete|catch)\b') {
        return $false
    }

    return $true
}

function Find-HashableFunctionRanges {
    param(
        [string]$Path,
        $Lines,
        [int]$MaxCount = 40
    )

    $safeLines = @(ConvertTo-LineArray $Lines)
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    $results = New-Object System.Collections.Generic.List[object]
    $seen = @{}

    $blockComment = $false

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        $rawLine = [string]$safeLines[$i]
        $trimmed = $rawLine.Trim()

        if ($trimmed -eq "") {
            continue
        }

        $cleanLine = Strip-CodeLineForHashScan $rawLine ([ref]$blockComment)
        $name = ""
        $isFunction = $false
        $isExpressionMember = $false

        if ($ext -eq ".ps1") {
            if ($cleanLine -match '^\s*function\s+([A-Za-z0-9_\-]+)\b') {
                $name = $Matches[1]
                $isFunction = $true
            }
        }
        elseif ($ext -eq ".gd") {
            if ($cleanLine -match '^\s*(static\s+)?func\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(') {
                $name = $Matches[2]
                $isFunction = $true
            }
        }
        elseif ($ext -eq ".cs" -and
            $cleanLine -match '^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|new|required|readonly|partial|unsafe)\s+)+[A-Za-z0-9_<>,\.\?\[\]\s]+?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=>') {
            $name = $Matches["name"]
            $isFunction = $true
            $isExpressionMember = $true
        }
        elseif ($ext -eq ".cs" -and
            $cleanLine -match '^\s*(?:(?:public|private|protected|internal|static|virtual|override|sealed|new|required|readonly|partial|unsafe)\s+)+[A-Za-z0-9_<>,\.\?\[\]\s]+?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(?:get|set|init)\b') {
            $name = $Matches["name"]
            $isFunction = $true
        }
        else {
            $match = [regex]::Match($cleanLine, '(?<![A-Za-z0-9_~])((?:[A-Za-z_][A-Za-z0-9_]*::)*~?[A-Za-z_][A-Za-z0-9_]*)\s*\(')
            if ($match.Success) {
                $candidate = $match.Groups[1].Value
                $leaf = ($candidate -split '::')[-1]
                if ($leaf -notmatch '^(if|for|while|switch|return|sizeof|catch|static_cast|reinterpret_cast|const_cast|dynamic_cast)$') {
                    $prefix = $cleanLine.Substring(0, $match.Index)
                    if (Test-LooksLikeHashableFunctionPrefix $prefix) {
                        $name = $leaf
                        $isFunction = $true
                    }
                }
            }
        }

        if (-not $isFunction -or $name -eq "") {
            continue
        }

        if ($isExpressionMember) {
            $end = -1
            $expressionBlockComment = $false

            for ($j = $i; $j -lt $safeLines.Count; $j++) {
                $clean = Strip-CodeLineForHashScan ([string]$safeLines[$j]) ([ref]$expressionBlockComment)
                if ($clean.IndexOf(";") -ge 0) {
                    $end = $j + 1
                    break
                }
            }

            if ($end -lt 0) {
                continue
            }

            $key = ("$name|$i|$end").ToLowerInvariant()
            if ($seen.ContainsKey($key)) {
                continue
            }
            $seen[$key] = $true

            $results.Add([pscustomobject]@{
                Name = $name
                Start = $i
                End = $end
                Hash = (Get-RegionHashFromLines $safeLines $i $end)
            })

            if ($results.Count -ge $MaxCount) {
                break
            }

            continue
        }

        $openLine = -1
        $sawSemicolonBeforeBrace = $false
        $scanBlockComment = $false

        for ($j = $i; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForHashScan ([string]$safeLines[$j]) ([ref]$scanBlockComment)
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

        $start = $i
        while ($start -gt 0) {
            $prev = $safeLines[$start - 1].Trim()
            if ($prev -eq "") { break }
            if ($prev.StartsWith("template") -or
                $prev.StartsWith("[[") -or
                $prev.StartsWith("__attribute__") -or
                $prev.StartsWith("VKAPI_ATTR")) {
                $start--
                continue
            }
            break
        }

        $depth = 0
        $started = $false
        $bodyBlockComment = $false
        $end = -1

        for ($j = $openLine; $j -lt $safeLines.Count; $j++) {
            $clean = Strip-CodeLineForHashScan ([string]$safeLines[$j]) ([ref]$bodyBlockComment)

            for ($k = 0; $k -lt $clean.Length; $k++) {
                if ($clean[$k] -eq '{') {
                    $depth++
                    $started = $true
                }
                elseif ($clean[$k] -eq '}') {
                    $depth--
                }

                if ($started -and $depth -eq 0) {
                    $end = $j + 1
                    break
                }
            }

            if ($end -ge 0) { break }
        }

        if ($end -lt 0) {
            continue
        }

        $key = ("$name|$start|$end").ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            continue
        }
        $seen[$key] = $true

        $results.Add([pscustomobject]@{
            Name = $name
            Start = $start
            End = $end
            Hash = (Get-RegionHashFromLines $safeLines $start $end)
        })

        if ($results.Count -ge $MaxCount) {
            break
        }
    }

    return @($results.ToArray())
}

function Find-ReplaceRegionHashes {
    param($Lines)

    $safeLines = @(ConvertTo-LineArray $Lines)
    $regions = New-Object System.Collections.Generic.List[object]
    $activeName = ""
    $activeStart = -1

    for ($i = 0; $i -lt $safeLines.Count; $i++) {
        $line = [string]$safeLines[$i]

        if ($activeStart -lt 0 -and $line -match 'CC-REPLACE-BEGIN\s*:\s*([^\s]+)') {
            $activeName = $Matches[1].Trim()
            $activeStart = $i + 1
            continue
        }

        if ($activeStart -ge 0 -and $line -match 'CC-REPLACE-END\s*:\s*([^\s]+)') {
            $endName = $Matches[1].Trim()
            if ($endName -eq $activeName) {
                $regions.Add([pscustomobject]@{
                    Name = $activeName
                    Start = $activeStart
                    End = $i
                    Hash = (Get-RegionHashFromLines $safeLines $activeStart $i)
                })
            }
            $activeName = ""
            $activeStart = -1
        }
    }

    return @($regions.ToArray())
}

function Add-HashHintsForFile {
    param(
        [string]$Path,
        [string]$Content
    )

    if (-not $HashHints) {
        return
    }

    $lines = @(ConvertTo-LineArray $Content)
    Add-Line "Hash hints for optional HASH: patch headers:"
    Add-Line "- whole_file HASH: $(Get-Sha1Short (($lines) -join "`n"))"

    $regions = @(Find-ReplaceRegionHashes $lines)
    foreach ($region in $regions) {
        Add-Line "- replace_region $($region.Name) HASH: $($region.Hash)"
    }

    $functions = @(Find-HashableFunctionRanges $Path $lines 40)
    foreach ($fn in $functions) {
        Add-Line "- function $($fn.Name) HASH: $($fn.Hash)"
    }

    if ($functions.Count -ge 40) {
        Add-Line "- function hash list truncated at 40 entries to avoid token bloat"
    }

    Add-Line ""
}
