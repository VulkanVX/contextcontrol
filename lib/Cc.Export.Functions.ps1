# CC-DESC: FUNCTION/FUNC/FIND source discovery and body extraction for Context Control exports.

function Get-SearchCandidateFiles {
    $rootKey = Get-PathKey "."
    if ($null -ne $script:SearchCandidateFilesCache -and
        $script:SearchCandidateFilesCacheRoot -eq $rootKey) {
        return @($script:SearchCandidateFilesCache)
    }

    $seen = @{}
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $pending = New-Object System.Collections.Generic.Stack[string]

    if (-not (Test-Path -LiteralPath ".")) {
        return @()
    }

    $root = (Resolve-Path -LiteralPath ".").Path
    [void]$pending.Push($root)

    while ($pending.Count -gt 0) {
        $dir = $pending.Pop()
        if (Is-ExcludedPath $dir) {
            continue
        }

        try {
            $items = @(Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue)
        }
        catch {
            continue
        }

        foreach ($item in $items) {
            if ($null -eq $item) {
                continue
            }

            if ($item.PSIsContainer) {
                if (Is-ExcludedPath $item.FullName) {
                    continue
                }

                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    continue
                }

                [void]$pending.Push($item.FullName)
                continue
            }

            if (-not (Is-TextSearchCandidate $item)) {
                continue
            }

            $key = ($item.FullName -replace '\\', '/').ToLowerInvariant()
            if ($seen.ContainsKey($key)) {
                continue
            }

            $seen[$key] = $true
            $files.Add($item)
        }
    }

    $result = @($files.ToArray() | Sort-Object FullName)
    $script:SearchCandidateFilesCacheRoot = $rootKey
    $script:SearchCandidateFilesCache = $result
    return @($result)
}

function Get-CachedSearchFileText {
    param([string]$Path)

    if ($null -eq $script:SearchTextCache) {
        $script:SearchTextCache = @{}
    }

    $key = Get-PathKey $Path
    if (-not $script:SearchTextCache.ContainsKey($key)) {
        $script:SearchTextCache[$key] = Read-TextFileAutoEncoding $Path
    }

    return [string]$script:SearchTextCache[$key]
}

function Get-CachedSearchFileLines {
    param([string]$Path)

    if ($null -eq $script:SearchLineCache) {
        $script:SearchLineCache = @{}
    }

    $key = Get-PathKey $Path
    if (-not $script:SearchLineCache.ContainsKey($key)) {
        $script:SearchLineCache[$key] = [string[]](ConvertTo-LineArray (Get-CachedSearchFileText $Path))
    }

    return [string[]]$script:SearchLineCache[$key]
}

function Get-CachedHashableFunctionRanges {
    param(
        [string]$Path,
        [int]$MaxCount = 100000
    )

    if ($null -eq $script:SearchFunctionRangeCache) {
        $script:SearchFunctionRangeCache = @{}
    }

    $key = "$(Get-PathKey $Path)|$MaxCount"
    if (-not $script:SearchFunctionRangeCache.ContainsKey($key)) {
        $script:SearchFunctionRangeCache[$key] = @(Find-HashableFunctionRanges $Path (Get-CachedSearchFileLines $Path) $MaxCount)
    }

    return @($script:SearchFunctionRangeCache[$key])
}

function Get-CachedSymbolSearchRegex {
    param([string]$Symbol)

    if ($null -eq $script:SearchRegexCache) {
        $script:SearchRegexCache = @{}
    }

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    $hasWildcard = [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)
    $cacheKey = if ($hasWildcard) { "wildcard|$($symbolText.ToLowerInvariant())" } else { "exact|$symbolText" }

    if (-not $script:SearchRegexCache.ContainsKey($cacheKey)) {
        if ($hasWildcard) {
            $regexText = [regex]::Escape($symbolText)
            $regexText = $regexText -replace '\\`\\*', '.*'
            $regexText = $regexText -replace '\\`\\?', '.'
            $regexText = $regexText -replace '\\\*', '.*'
            $regexText = $regexText -replace '\\\?', '.'
            $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
            $script:SearchRegexCache[$cacheKey] = New-Object System.Text.RegularExpressions.Regex($regexText, $options)
        }
        else {
            $escaped = [System.Text.RegularExpressions.Regex]::Escape($symbolText)
            $pattern = "(?<![A-Za-z0-9_])$escaped(?![A-Za-z0-9_])"
            $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
            $script:SearchRegexCache[$cacheKey] = New-Object System.Text.RegularExpressions.Regex($pattern, $options)
        }
    }

    return [System.Text.RegularExpressions.Regex]$script:SearchRegexCache[$cacheKey]
}

function Test-TextContainsSymbol {
    param(
        [string]$Text,
        [string]$Symbol
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    if ($symbolText -eq "") {
        return $false
    }

    if (-not [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)) {
        if ($Text.IndexOf($symbolText, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            return $false
        }
    }

    return (Get-CachedSymbolSearchRegex $symbolText).IsMatch($Text)
}

function Get-FindSearchTokens {
    param([string]$Pattern)

    $patternText = if ($null -eq $Pattern) { "" } else { [string]$Pattern }
    if ($null -eq $script:FindTokenCache) {
        $script:FindTokenCache = @{}
    }

    $cacheKey = $patternText.ToLowerInvariant()
    if ($script:FindTokenCache.ContainsKey($cacheKey)) {
        return [string[]]$script:FindTokenCache[$cacheKey]
    }

    $patternText = [regex]::Replace($patternText, '([a-z0-9])([A-Z])', '$1 $2')
    $tokens = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    $stopWords = @{
        a = $true
        an = $true
        and = $true
        for = $true
        in = $true
        of = $true
        on = $true
        or = $true
        the = $true
        to = $true
        with = $true
    }

    foreach ($match in [regex]::Matches($patternText, '[A-Za-z0-9]+')) {
        $token = $match.Value.ToLowerInvariant()
        if ($token.Length -lt 2) {
            continue
        }

        if ($stopWords.ContainsKey($token)) {
            continue
        }

        if ($seen.ContainsKey($token)) {
            continue
        }

        $seen[$token] = $true
        [void]$tokens.Add($token)
    }

    $result = [string[]]$tokens.ToArray()
    $script:FindTokenCache[$cacheKey] = $result
    return $result
}

function ConvertTo-FindComparableText {
    param([string]$Text)

    $value = if ($null -eq $Text) { "" } else { [string]$Text }
    $value = [regex]::Replace($value, '([a-z0-9])([A-Z])', '$1 $2')
    $value = [regex]::Replace($value, '[^A-Za-z0-9]+', ' ')
    $value = [regex]::Replace($value, '\s+', ' ')
    return " $($value.Trim().ToLowerInvariant()) "
}

function Test-FindComparableContainsAllTokens {
    param(
        [string]$Comparable,
        [string[]]$Tokens,
        [int]$MaxSpan = 220
    )

    if ($Tokens.Count -eq 0) {
        return $false
    }

    $minPosition = [int]::MaxValue
    $maxPosition = -1
    foreach ($token in @($Tokens)) {
        $position = $Comparable.IndexOf(" $token ", [System.StringComparison]::Ordinal)
        if ($position -lt 0) {
            return $false
        }

        if ($position -lt $minPosition) {
            $minPosition = $position
        }

        if ($position -gt $maxPosition) {
            $maxPosition = $position
        }
    }

    return (($maxPosition - $minPosition) -le $MaxSpan)
}

function Test-TextContainsFindTokenWindow {
    param(
        [string]$Text,
        [string[]]$Tokens
    )

    if ($Tokens.Count -lt 2) {
        return $false
    }

    $lines = @(ConvertTo-LineArray $Text)
    if ($lines.Count -eq 0) {
        return $false
    }

    $candidateIndices = New-Object System.Collections.Generic.List[int]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = [string]$lines[$i]
        $containsAnyToken = $false
        foreach ($token in @($Tokens)) {
            if ($line.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $containsAnyToken = $true
                break
            }
        }

        if (-not $containsAnyToken) {
            continue
        }

        [void]$candidateIndices.Add($i)
        if (Test-FindComparableContainsAllTokens -Comparable (ConvertTo-FindComparableText $line) -Tokens $Tokens) {
            return $true
        }
    }

    $seenWindows = @{}
    foreach ($index in @($candidateIndices.ToArray())) {
        $start = [Math]::Max(0, $index - 3)
        $end = [Math]::Min($lines.Count - 1, $index + 3)
        $windowKey = "$start|$end"
        if ($seenWindows.ContainsKey($windowKey)) {
            continue
        }
        $seenWindows[$windowKey] = $true

        $windowLines = New-Object System.Collections.Generic.List[string]
        for ($j = $start; $j -le $end; $j++) {
            [void]$windowLines.Add([string]$lines[$j])
        }

        $windowText = ($windowLines.ToArray() -join "`n")
        if (Test-FindComparableContainsAllTokens -Comparable (ConvertTo-FindComparableText $windowText) -Tokens $Tokens -MaxSpan 260) {
            return $true
        }
    }

    return $false
}

function Test-TextContainsFindPattern {
    param(
        [string]$Text,
        [string]$Pattern
    )

    $patternText = if ($null -eq $Pattern) { "" } else { $Pattern.Trim() }
    if ($patternText -eq "") {
        return $false
    }

    if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($patternText)) {
        $regexText = [regex]::Escape($patternText)
        $regexText = $regexText -replace '\\\*', '.*'
        $regexText = $regexText -replace '\\\?', '.'
        $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
        if ([regex]::IsMatch($Text, $regexText, $options)) {
            return $true
        }
    }
    elseif ($Text.IndexOf($patternText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $true
    }

    $tokens = @(Get-FindSearchTokens $patternText)
    if ($tokens.Count -lt 2) {
        return $false
    }

    if ([regex]::IsMatch($patternText, '\s')) {
        return $false
    }

    foreach ($token in @($tokens)) {
        if ($Text.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            return $false
        }
    }

    return Test-TextContainsFindTokenWindow -Text $Text -Tokens $tokens
}

function Test-LineContainsFindPreview {
    param(
        [string]$Line,
        [string]$Pattern,
        [string[]]$Tokens
    )

    $patternText = if ($null -eq $Pattern) { "" } else { $Pattern.Trim() }
    if ($patternText -eq "") {
        return $false
    }

    if ($Line.IndexOf($patternText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $true
    }

    if ($Tokens.Count -eq 0) {
        return $false
    }

    return Test-FindComparableContainsAllTokens -Comparable (ConvertTo-FindComparableText $Line) -Tokens $Tokens
}

function Get-FindRelatedClassNames {
    param(
        [string]$Text,
        [string]$Pattern,
        [string[]]$Identifiers = @()
    )

    $patternTokens = @(Get-FindSearchTokens $Pattern)
    $relatedText = Get-FindRelatedTextWindow -Text $Text -Pattern $Pattern -Tokens $patternTokens -RelatedTerms $Identifiers
    $classes = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($match in [regex]::Matches($relatedText, '(?i)(?:Classes|class)\s*=\s*"(?<value>[^"]+)"')) {
        foreach ($className in ($match.Groups["value"].Value -split '\s+')) {
            Add-FindRelatedClassName $classes $seen $className $patternTokens
        }
    }

    foreach ($match in [regex]::Matches($relatedText, '(?i)Selector\s*=\s*"(?<value>[^"]+)"')) {
        foreach ($classMatch in [regex]::Matches($match.Groups["value"].Value, '\.([A-Za-z0-9_-]+)')) {
            Add-FindRelatedClassName $classes $seen $classMatch.Groups[1].Value $patternTokens
        }
    }

    return @($classes.ToArray())
}

function Add-FindRelatedClassName {
    param(
        [System.Collections.Generic.List[string]]$Classes,
        [hashtable]$Seen,
        [string]$ClassName,
        [string[]]$PatternTokens
    )

    $clean = if ($null -eq $ClassName) { "" } else { $ClassName.Trim() }
    if ($clean.Length -lt 3) {
        return
    }

    $classComparable = ConvertTo-FindComparableText $clean
    $matchesPattern = $false
    foreach ($token in @($PatternTokens)) {
        if ($classComparable.IndexOf(" $token ", [System.StringComparison]::Ordinal) -ge 0) {
            $matchesPattern = $true
            break
        }
    }

    if (-not $matchesPattern -and
        $clean.IndexOf("send", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
        $clean.IndexOf("prompt", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        return
    }

    $key = $clean.ToLowerInvariant()
    if ($Seen.ContainsKey($key)) {
        return
    }

    $Seen[$key] = $true
    [void]$Classes.Add($clean)
}

function Test-TextContainsAnyFindRelatedClass {
    param(
        [string]$Text,
        [string[]]$Classes
    )

    foreach ($className in @($Classes)) {
        if ($Text.IndexOf($className, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Get-FindRelatedIdentifiers {
    param(
        [string]$Text,
        [string]$Pattern
    )

    $patternTokens = @(Get-FindSearchTokens $Pattern)
    $relatedText = Get-FindRelatedTextWindow -Text $Text -Pattern $Pattern -Tokens $patternTokens
    $identifiers = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($match in [regex]::Matches($relatedText, '\b[A-Za-z_][A-Za-z0-9_]*(?:Command|Label|ToolTip|Text|Button|Prompt|Send|Codex)\b')) {
        $identifier = $match.Value.Trim()
        if ($identifier.Length -lt 4) {
            continue
        }

        $identifierComparable = ConvertTo-FindComparableText $identifier
        $matchesPattern = $false
        foreach ($token in @($patternTokens)) {
            if ($identifierComparable.IndexOf(" $token ", [System.StringComparison]::Ordinal) -ge 0) {
                $matchesPattern = $true
                break
            }
        }

        if (-not $matchesPattern) {
            continue
        }

        if (($patternTokens -contains "send") -and
            $identifier.IndexOf("send", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        if (($patternTokens -contains "button") -and
            $identifier.IndexOf("button", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        if (($patternTokens -contains "send") -and
            $identifierComparable.IndexOf(" codex ", [System.StringComparison]::Ordinal) -lt 0 -and
            $identifier.IndexOf("button", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $identifier.IndexOf("label", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $identifier.IndexOf("tooltip", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $identifier.IndexOf("text", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $identifier.IndexOf("prompt", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        $key = $identifier.ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            continue
        }

        $seen[$key] = $true
        [void]$identifiers.Add($identifier)
    }

    return @($identifiers.ToArray())
}

function Get-FindRelatedTextWindow {
    param(
        [string]$Text,
        [string]$Pattern,
        [string[]]$Tokens,
        [string[]]$RelatedTerms = @()
    )

    $lines = @(ConvertTo-LineArray $Text)
    if ($lines.Count -eq 0) {
        return ""
    }

    $patternText = if ($null -eq $Pattern) { "" } else { $Pattern.Trim() }
    $indices = New-Object System.Collections.Generic.List[int]

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = [string]$lines[$i]
        if ($patternText -ne "" -and $line.IndexOf($patternText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            [void]$indices.Add($i)
        }
    }

    if ($indices.Count -eq 0) {
        foreach ($term in @($RelatedTerms)) {
            $termText = if ($null -eq $term) { "" } else { $term.Trim() }
            if ($termText -eq "") {
                continue
            }

            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = [string]$lines[$i]
                if ($line.IndexOf($termText, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    [void]$indices.Add($i)
                }
            }
        }
    }

    if ($indices.Count -eq 0) {
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if (Test-LineContainsFindPreview -Line ([string]$lines[$i]) -Pattern $patternText -Tokens $Tokens) {
                [void]$indices.Add($i)
            }
        }
    }

    if ($indices.Count -eq 0) {
        return ""
    }

    $selected = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($index in @($indices.ToArray())) {
        $start = [Math]::Max(0, $index - 4)
        $end = [Math]::Min($lines.Count - 1, $index + 4)
        for ($i = $start; $i -le $end; $i++) {
            if ($seen.ContainsKey($i)) {
                continue
            }

            $seen[$i] = $true
            [void]$selected.Add([string]$lines[$i])
        }
    }

    return ($selected.ToArray() -join "`n")
}

function Test-TextContainsAnyFindRelatedIdentifier {
    param(
        [string]$Text,
        [string[]]$Identifiers
    )

    foreach ($identifier in @($Identifiers)) {
        if ($Text.IndexOf($identifier, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-FileContainsSymbol {
    param(
        [string]$Path,
        [string]$Symbol
    )

    if (Is-ExcludedPath $Path) {
        return $false
    }

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    if ($symbolText -eq "") {
        return $false
    }

    try {
        $text = Get-CachedSearchFileText $Path
    }
    catch {
        return $false
    }

    return Test-TextContainsSymbol $text $symbolText
}

function Test-RequestPathHasWildcard {
    param([string]$Path)

    if ($null -eq $Path) {
        return $false
    }

    return [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($Path)
}

function Get-FunctionLeafName {
    param([string]$Symbol)

    if ($null -eq $Symbol) {
        return ""
    }

    $text = $Symbol.Trim()
    if ($text -eq "") {
        return ""
    }

    return (($text -split '::')[-1]).Trim()
}

function Add-FunctionResolverVariant {
    param(
        [System.Collections.Generic.List[string]]$Variants,
        [hashtable]$Seen,
        [string]$Symbol
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    if ($symbolText -eq "") {
        return
    }

    $key = $symbolText.ToLowerInvariant()
    if ($Seen.ContainsKey($key)) {
        return
    }

    $Seen[$key] = $true
    [void]$Variants.Add($symbolText)
}

function Get-CppFunctionResolverVariants {
    param([string]$Symbol)

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    $variants = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    Add-FunctionResolverVariant $variants $seen $symbolText

    if ($symbolText -eq "" -or
        $symbolText.Contains("::") -or
        [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)) {
        return @($variants.ToArray())
    }

    switch -Exact ($symbolText) {
        "new_block_broadcast" {
            Add-FunctionResolverVariant $variants $seen "ValidatorManagerImpl::new_block_broadcast"
            Add-FunctionResolverVariant $variants $seen "FullNodeImpl::process_block_broadcast"
            Add-FunctionResolverVariant $variants $seen "FullNodeShardImpl::process_block_broadcast"
        }
    }

    return @($variants.ToArray())
}

function Resolve-FunctionRequestFiles {
    param([string]$Path)

    $pathText = if ($null -eq $Path) { "" } else { $Path.Trim() }
    $normalizedRequest = ($pathText -replace '\\', '/').Trim()
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $seen = @{}

    if ($normalizedRequest -eq "") {
        return @()
    }

    $items = @()

    if (Test-RequestPathHasWildcard $normalizedRequest) {
        # PowerShell wildcard expansion treats ** inconsistently across versions and
        # providers. Resolve FUNCTION globs ourselves so src/**/*.cpp means true
        # recursive matching and path tokens are never stripped/sanitized away.
        $segments = @($normalizedRequest -split '/')
        $rootParts = New-Object System.Collections.Generic.List[string]

        foreach ($segment in $segments) {
            if ($segment -eq "" -or [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($segment)) {
                break
            }
            $rootParts.Add($segment)
        }

        $rootText = if ($rootParts.Count -gt 0) { [string]::Join([System.IO.Path]::DirectorySeparatorChar, [string[]]$rootParts.ToArray()) } else { "." }
        if (-not (Test-Path -LiteralPath $rootText)) {
            return @()
        }

        $rootFull = (Resolve-Path -LiteralPath $rootText).Path
        $allFiles = @(Get-ChildItem -LiteralPath $rootFull -Recurse -Force -File -ErrorAction SilentlyContinue)

        $requestRegex = [regex]::Escape($normalizedRequest)
        $requestRegex = $requestRegex -replace '\\\*\\\*', '.*'
        $requestRegex = $requestRegex -replace '\\\*', '[^/]*'
        $requestRegex = $requestRegex -replace '\\\?', '[^/]'
        $requestRegex = '^' + $requestRegex + '$'

        foreach ($candidate in $allFiles) {
            $relative = Get-RelativeDisplayPath $candidate.FullName
            $relative = ($relative -replace '\\', '/')
            if ([regex]::IsMatch($relative, $requestRegex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $items += $candidate
            }
        }
    }
    else {
        $clean = $normalizedRequest -replace '/', [System.IO.Path]::DirectorySeparatorChar

        if (Test-Path -LiteralPath $clean) {
            $item = Get-Item -LiteralPath $clean
            if ($item.PSIsContainer) {
                $items = @(Get-ChildItem -LiteralPath $clean -Recurse -Force -File -ErrorAction SilentlyContinue)
            }
            else {
                $items = @($item)
            }
        }
        else {
            return @()
        }
    }

    foreach ($item in $items) {
        if ($null -eq $item) {
            continue
        }

        if (Is-ExcludedPath $item.FullName) {
            continue
        }

        $ext = [System.IO.Path]::GetExtension($item.FullName).ToLowerInvariant()
        if ($script:BinaryExtensions -contains $ext) {
            continue
        }

        if (-not (Is-CodeSearchExtension $item.FullName)) {
            continue
        }

        $key = ($item.FullName -replace '\\', '/').ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            continue
        }

        $seen[$key] = $true
        $files.Add($item)
    }

    return @($files.ToArray() | Sort-Object FullName)
}

function Test-FunctionBlockMatchesScopedSymbol {
    param(
        [string]$BlockText,
        [string]$Symbol
    )

    if ($null -eq $BlockText -or $null -eq $Symbol) {
        return $false
    }

    $symbolText = $Symbol.Trim()
    if ($symbolText -eq "") {
        return $false
    }

    if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)) {
        $regexText = [regex]::Escape($symbolText)
        $regexText = $regexText -replace '\\\*', '.*'
        $regexText = $regexText -replace '\\\?', '.'
        return [regex]::IsMatch($BlockText, $regexText, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }

    if ($BlockText.IndexOf($symbolText, [System.StringComparison]::Ordinal) -ge 0) {
        return $true
    }

    if ($symbolText.Contains("::")) {
        $pattern = [regex]::Escape($symbolText) -replace '::', '\s*::\s*'
        return [regex]::IsMatch($BlockText, $pattern)
    }

    return $false
}

function Add-RawSymbolOccurrencePreview {
    param(
        [string]$Path,
        [string]$Symbol,
        [int]$MaxHits = 12,
        [switch]$FindMode
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    if ($symbolText -eq "") {
        return
    }

    try {
        $lines = @(Get-CachedSearchFileLines $Path)
    }
    catch {
        return
    }

    $escaped = [regex]::Escape($symbolText)
    if ([System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)) {
        $escaped = $escaped -replace '\\\*', '.*'
        $escaped = $escaped -replace '\\\?', '.'
    }

    $previewOptions = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::CultureInvariant
    $regex = New-Object System.Text.RegularExpressions.Regex($escaped, $previewOptions)
    $findTokens = if ($FindMode) { @(Get-FindSearchTokens $symbolText) } else { @() }

    $hitCount = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineText = [string]$lines[$i]
        $lineMatches = if ($FindMode) {
            Test-LineContainsFindPreview -Line $lineText -Pattern $symbolText -Tokens $findTokens
        }
        else {
            $regex.IsMatch($lineText)
        }

        if ($lineMatches) {
            $lineNo = $i + 1
            $text = $lineText.Trim()
            if ($text.Length -gt 220) {
                $text = $text.Substring(0, 220) + " ..."
            }
            Add-Line "- $(Get-RelativeDisplayPath $Path):${lineNo}: $text"
            $hitCount++
            if ($hitCount -ge $MaxHits) { break }
        }
    }
}

function Add-FunctionBodyBlocksFromFile {
    param(
        [string]$Path,
        [string]$Symbol,
        [switch]$ReportNoMatch,
        [hashtable]$ExportedRangeKeys = $null
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    $leafName = Get-FunctionLeafName $symbolText
    $symbolHasWildcard = [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($symbolText)
    $leafHasWildcard = [System.Management.Automation.WildcardPattern]::ContainsWildcardCharacters($leafName)

    if ($leafName -eq "") {
        if ($ReportNoMatch) {
            Add-Line "Malformed function request: empty function name after ::."
            Add-Line ""
        }
        return 0
    }

    $fileInfo = Get-Item -LiteralPath $Path
    $sizeKB = [math]::Ceiling($fileInfo.Length / 1024.0)
    if ((-not $ForceLargeFiles) -and $sizeKB -gt ($MaxFileKB * 4)) {
        if ($ReportNoMatch) {
            Add-Line "Skipped function extraction from very large text file: $(Get-RelativeDisplayPath $Path) ($sizeKB KB)."
            Add-Line "Re-run cc.ps1 with -ForceLargeFiles if this exact function is truly needed."
            Add-Line ""
        }
        return 0
    }

    try {
        $lines = @(Get-CachedSearchFileLines $Path)
    }
    catch {
        if ($ReportNoMatch) {
            Add-Line "Failed to read file for function extraction: $(Get-RelativeDisplayPath $Path)"
            Add-Line ""
        }
        return 0
    }

    $ranges = @(Get-CachedHashableFunctionRanges $Path 100000)

    if ($leafHasWildcard) {
        $leafPattern = New-Object System.Management.Automation.WildcardPattern($leafName, ([System.Management.Automation.WildcardOptions]::IgnoreCase))
        $leafMatches = @($ranges | Where-Object { $leafPattern.IsMatch($_.Name) })
    }
    else {
        $leafMatches = @($ranges | Where-Object { $_.Name -eq $leafName })
    }

    if ($leafMatches.Count -eq 0) {
        if ($ReportNoMatch) {
            Add-Line "No brace-delimited function body found for symbol: $symbolText"
            Add-Line "File: $(Get-RelativeDisplayPath $Path)"
            Add-Line ""

            if (Test-FileContainsSymbol $Path $symbolText) {
                Add-Line "Raw symbol occurrences exist, but they do not look like function bodies. Nearest raw occurrences:"
                Add-RawSymbolOccurrencePreview -Path $Path -Symbol $symbolText
            }
            else {
                $leaf = Get-FunctionLeafName $symbolText
                if ($leaf -ne $symbolText -and (Test-FileContainsSymbol $Path $leaf)) {
                    Add-Line "The leaf name '$leaf' occurs in the file, but the full scoped symbol '$symbolText' was not found as a body."
                    Add-Line "Nearest leaf occurrences:"
                    Add-RawSymbolOccurrencePreview -Path $Path -Symbol $leaf
                }
                else {
                    Add-Line "Requested symbol was not found in the requested file."
                }
            }

            Add-Line ""
        }
        return 0
    }

    $selected = New-Object System.Collections.Generic.List[object]
    $scopeWasExact = $true

    if ($symbolText.Contains("::") -or $symbolHasWildcard) {
        foreach ($range in $leafMatches) {
            $blockText = (($lines[$range.Start..($range.End - 1)]) -join "`n")
            if (Test-FunctionBlockMatchesScopedSymbol $blockText $symbolText) {
                $selected.Add($range)
            }
        }

        if ($selected.Count -eq 0) {
            $scopeWasExact = $false
        }
    }

    if ($selected.Count -eq 0) {
        foreach ($range in $leafMatches) {
            $selected.Add($range)
        }
    }

    $lang = Get-CodeFenceLanguage $Path
    $displayPath = Get-RelativeDisplayPath $Path
    $count = 0

    if ($selected.Count -gt 1) {
        Add-Line "Multiple matching function bodies found in $displayPath; exporting all matching overloads/scopes."
        Add-Line ""
    }

    if (-not $scopeWasExact -and $symbolText.Contains("::")) {
        Add-Line "WARNING: Found leaf function '$leafName' in $displayPath, but the exact scoped text '$symbolText' was not visible in the detected signature. Exporting leaf match as fallback."
        Add-Line ""
    }

    foreach ($range in $selected) {
        if ($null -ne $ExportedRangeKeys) {
            $rangeKey = ("$(Get-PathKey $Path)|$($range.Start)|$($range.End)").ToLowerInvariant()
            if ($ExportedRangeKeys.ContainsKey($rangeKey)) {
                continue
            }
            $ExportedRangeKeys[$rangeKey] = $true
        }

        $startLine = $range.Start + 1
        $endLine = $range.End
        $blockText = (($lines[$range.Start..($range.End - 1)]) -join "`n")

        Add-Line "Source: $displayPath lines $startLine-$endLine"
        if ($HashHints) {
            Add-Line "Hash hint for optional CC-REPLACE header: MODE: function | NAME: $symbolText | HASH: $($range.Hash)"
        }
        Add-Line ""
        Add-Line ($script:Fence4 + $lang)
        Add-Line $blockText
        Add-Line $script:Fence4
        Add-Line ""
        $count++
    }

    return $count
}

function Add-FunctionSearchExport {
    param([string]$Symbol)

    $symbol = $Symbol.Trim()

    if ($symbol -eq "") {
        return
    }

    # De-duplicate repeated FUNC:/FUNCTION: input lines.
    if ($null -eq $script:FunctionSearchKeys) {
        $script:FunctionSearchKeys = @{}
    }

    $symbolKey = $symbol.ToLowerInvariant()
    if ($script:FunctionSearchKeys.ContainsKey($symbolKey)) {
        Write-Host "Skipping duplicate function search: $symbol"
        return
    }
    $script:FunctionSearchKeys[$symbolKey] = $true

    Write-Host "Searching function body: $symbol"

    $candidates = @(Get-SearchCandidateFiles)
    Write-Host "  Candidate text files: $($candidates.Count)"

    $resolverSymbols = @(Get-CppFunctionResolverVariants $symbol)
    $rawMatches = New-Object System.Collections.Generic.List[string]
    $exportedRangeKeys = @{}
    $bodyMatches = 0

    Add-Line ""
    Add-Line "## FOUND FUNCTION: $symbol"
    Add-Line ""

    if ($resolverSymbols.Count -gt 1) {
        Add-Line "Resolver variants tried:"
        foreach ($resolverSymbol in $resolverSymbols) {
            Add-Line "- $resolverSymbol"
        }
        Add-Line ""
    }

    foreach ($file in $candidates) {
        $matchedResolverSymbols = New-Object System.Collections.Generic.List[string]

        foreach ($resolverSymbol in $resolverSymbols) {
            $resolverLeaf = Get-FunctionLeafName $resolverSymbol

            if ($resolverSymbol.Contains("::")) {
                if ((Test-FileContainsSymbol $file.FullName $resolverSymbol) -or (Test-FileContainsSymbol $file.FullName $resolverLeaf)) {
                    [void]$matchedResolverSymbols.Add($resolverSymbol)
                }
            }
            elseif (Test-FileContainsSymbol $file.FullName $resolverLeaf) {
                [void]$matchedResolverSymbols.Add($resolverSymbol)
            }
        }

        if ($matchedResolverSymbols.Count -eq 0) {
            continue
        }

        $rawMatches.Add($file.FullName)

        foreach ($resolverSymbol in $matchedResolverSymbols) {
            $count = Add-FunctionBodyBlocksFromFile -Path $file.FullName -Symbol $resolverSymbol -ExportedRangeKeys $exportedRangeKeys
            $bodyMatches += $count
        }
    }

    if ($bodyMatches -eq 0) {
        if ($rawMatches.Count -eq 0) {
            Add-Line "No matching code file found for function symbol: $symbol"
        }
        else {
            Add-Line "Raw symbol matches were found, but no brace-delimited function bodies were isolated."
            Add-Line "Use FIND: $symbol to discover exact matching files, then request the specific file/path you actually need."
            Add-Line ""
            Add-Line "Raw matched files:"
            foreach ($path in $rawMatches) {
                Add-Line "- $(Get-RelativeDisplayPath $path)"
            }
        }
        Add-Line ""
    }

    Write-Host "  Function bodies exported: $bodyMatches"
}

function Add-FindSearchExports {
    param([string[]]$Patterns)

    $requests = New-Object System.Collections.Generic.List[object]

    foreach ($pattern in @($Patterns)) {
        $patternText = if ($null -eq $pattern) { "" } else { $pattern.Trim() }

        if ($patternText -eq "") {
            continue
        }

        # De-duplicate repeated FIND: input lines.
        if ($null -eq $script:FindSearchKeys) {
            $script:FindSearchKeys = @{}
        }

        $patternKey = $patternText.ToLowerInvariant()
        if ($script:FindSearchKeys.ContainsKey($patternKey)) {
            Write-Host "Skipping duplicate file discovery search: $patternText"
            continue
        }
        $script:FindSearchKeys[$patternKey] = $true

        Write-Host "Discovering matching files: $patternText"
        $requests.Add([pscustomobject]@{
            Pattern = $patternText
            Matches = (New-Object System.Collections.Generic.List[string])
        })
    }

    if ($requests.Count -eq 0) {
        return
    }

    $candidates = @(Get-SearchCandidateFiles)

    foreach ($request in $requests) {
        Write-Host "  Candidate text files: $($candidates.Count)"
    }

    foreach ($file in $candidates) {
        try {
            $text = Get-CachedSearchFileText $file.FullName
        }
        catch {
            continue
        }

        foreach ($request in $requests) {
            if (Test-TextContainsFindPattern $text $request.Pattern) {
                $request.Matches.Add($file.FullName)
            }
        }
    }

    foreach ($request in $requests) {
        if ($request.Matches.Count -eq 0) {
            continue
        }

        $relatedIdentifiers = New-Object System.Collections.Generic.List[string]
        $seenIdentifiers = @{}
        foreach ($path in @($request.Matches.ToArray())) {
            try {
                $text = Get-CachedSearchFileText $path
            }
            catch {
                continue
            }

            foreach ($identifier in @(Get-FindRelatedIdentifiers -Text $text -Pattern $request.Pattern)) {
                $identifierKey = $identifier.ToLowerInvariant()
                if ($seenIdentifiers.ContainsKey($identifierKey)) {
                    continue
                }

                $seenIdentifiers[$identifierKey] = $true
                [void]$relatedIdentifiers.Add($identifier)
            }
        }

        if ($relatedIdentifiers.Count -eq 0) {
            continue
        }

        $request | Add-Member -NotePropertyName RelatedIdentifiers -NotePropertyValue ([string[]]$relatedIdentifiers.ToArray()) -Force

        $matchedKeys = @{}
        foreach ($path in @($request.Matches)) {
            $matchedKeys[(Get-PathKey $path)] = $true
        }

        foreach ($file in $candidates) {
            $fileKey = Get-PathKey $file.FullName
            if ($matchedKeys.ContainsKey($fileKey)) {
                continue
            }

            try {
                $text = Get-CachedSearchFileText $file.FullName
            }
            catch {
                continue
            }

            if (Test-TextContainsAnyFindRelatedIdentifier -Text $text -Identifiers ([string[]]$relatedIdentifiers.ToArray())) {
                $matchedKeys[$fileKey] = $true
                $request.Matches.Add($file.FullName)
            }
        }
    }

    foreach ($request in $requests) {
        if ($request.Matches.Count -eq 0) {
            continue
        }

        $relatedClasses = New-Object System.Collections.Generic.List[string]
        $seenClasses = @{}
        foreach ($path in @($request.Matches.ToArray())) {
            try {
                $text = Get-CachedSearchFileText $path
            }
            catch {
                continue
            }

            $identifierTerms = @()
            if ($request.PSObject.Properties.Name -contains "RelatedIdentifiers") {
                $identifierTerms = [string[]]$request.RelatedIdentifiers
            }

            foreach ($className in @(Get-FindRelatedClassNames -Text $text -Pattern $request.Pattern -Identifiers $identifierTerms)) {
                $classKey = $className.ToLowerInvariant()
                if ($seenClasses.ContainsKey($classKey)) {
                    continue
                }

                $seenClasses[$classKey] = $true
                [void]$relatedClasses.Add($className)
            }
        }

        if ($relatedClasses.Count -eq 0) {
            continue
        }

        $matchedKeys = @{}
        foreach ($path in @($request.Matches)) {
            $matchedKeys[(Get-PathKey $path)] = $true
        }

        foreach ($file in $candidates) {
            $fileKey = Get-PathKey $file.FullName
            if ($matchedKeys.ContainsKey($fileKey)) {
                continue
            }

            try {
                $text = Get-CachedSearchFileText $file.FullName
            }
            catch {
                continue
            }

            if (Test-TextContainsAnyFindRelatedClass -Text $text -Classes ([string[]]$relatedClasses.ToArray())) {
                $matchedKeys[$fileKey] = $true
                $request.Matches.Add($file.FullName)
            }
        }
    }

    foreach ($request in $requests) {
        Add-Line ""
        Add-Line "## FIND: $($request.Pattern)"
        Add-Line ""

        if ($request.Matches.Count -eq 0) {
            Add-Line "No matching code file found for: $($request.Pattern)"
            Add-Line ""
            Write-Host "  Found: 0"
            continue
        }

        Add-Line "Matched code files only; file contents were not exported."
        Add-Line "Request exact paths or FUNCTION exports from this list in the next cc.ps1 run."
        Add-Line ""
        Add-Line "Matched code files:"
        foreach ($path in $request.Matches) {
            Add-Line "- $(Get-RelativeDisplayPath $path)"
        }

        Add-Line ""
        Add-Line "Occurrence preview:"
        foreach ($path in $request.Matches) {
            Add-RawSymbolOccurrencePreview -Path $path -Symbol $request.Pattern -MaxHits 4 -FindMode
        }
        Add-Line ""

        Write-Host "  Found: $($request.Matches.Count)"
    }
}

function Add-FindSearchExport {
    param([string]$Pattern)

    Add-FindSearchExports @($Pattern)
}

function Add-ScopedFunctionRequestReport {
    param(
        [string]$Path,
        [string]$Symbol
    )

    $pathText = if ($null -eq $Path) { "" } else { $Path.Trim() }
    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    $cleanPath = $pathText -replace '/', [System.IO.Path]::DirectorySeparatorChar

    Add-Line ""
    Add-Line "## FUNCTION $pathText :: $symbolText"
    Add-Line ""

    if ([string]::IsNullOrWhiteSpace($pathText) -or [string]::IsNullOrWhiteSpace($symbolText)) {
        Add-Line "Malformed function request."
        Add-Line "Expected syntax: FUNCTION src/path/File.cpp :: SymbolName"
        Add-Line ""
        return
    }

    $files = @(Resolve-FunctionRequestFiles $pathText)

    if ($files.Count -eq 0) {
        if (Test-RequestPathHasWildcard $cleanPath) {
            Add-Line "No files matched FUNCTION path pattern: $pathText"
        }
        else {
            Add-Line "Requested FUNCTION path is missing: $pathText"
        }
        Add-Line ""
        return
    }

    $isBroadRequest = (Test-RequestPathHasWildcard $cleanPath) -or ($files.Count -gt 1)
    if ((-not (Test-RequestPathHasWildcard $cleanPath)) -and (Test-Path -LiteralPath $cleanPath)) {
        $item = Get-Item -LiteralPath $cleanPath
        if ($item.PSIsContainer) {
            $isBroadRequest = $true
        }
    }

    if ($isBroadRequest) {
        Add-Line "Resolved FUNCTION target to $($files.Count) candidate files. Exporting only matching function bodies."
        Add-Line ""
    }

    $totalBodies = 0
    $rawMatches = New-Object System.Collections.Generic.List[string]
    $leafName = Get-FunctionLeafName $symbolText

    foreach ($file in $files) {
        if ((Test-FileContainsSymbol $file.FullName $symbolText) -or (Test-FileContainsSymbol $file.FullName $leafName)) {
            $rawMatches.Add($file.FullName)
        }

        $reportNoMatch = -not $isBroadRequest
        $count = Add-FunctionBodyBlocksFromFile -Path $file.FullName -Symbol $symbolText -ReportNoMatch:$reportNoMatch
        $totalBodies += $count
    }

    if ($totalBodies -eq 0 -and $isBroadRequest) {
        Add-Line "No function body found for symbol: $symbolText in resolved FUNCTION target: $pathText"
        Add-Line ""

        if ($rawMatches.Count -gt 0) {
            Add-Line "Raw symbol/leaf occurrences were found in:"
            foreach ($path in $rawMatches) {
                Add-Line "- $(Get-RelativeDisplayPath $path)"
            }
            Add-Line ""
            Add-Line "This usually means the request hit declarations, macros, generated wrappers, or a parser edge case. Use FIND: $symbolText to discover exact matching files, then request the specific file/path you actually need."
        }
        else {
            Add-Line "No raw symbol occurrences found either. The function name or path pattern is probably wrong."
        }

        Add-Line ""
    }
}
