# CC-DESC: Exports selected project files and FUNC symbol matches for Context Control.
# cc.ps1

param(
    [string]$OutputFile = "cc_code_export.md",
    [int]$MaxFileKB = 512,
    [switch]$ForceLargeFiles,
    [switch]$NoClipboard,
    [switch]$HashHints
)

$ErrorActionPreference = "Stop"

$script:OutputFileForFilters = $OutputFile

$Backtick = [char]96
$Fence3 = "$Backtick$Backtick$Backtick"
$Fence4 = "$Backtick$Backtick$Backtick$Backtick"

# Files that may be explicitly exported if the user asks for them.
$TextExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx",
    ".cmake", ".txt", ".md", ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".cs", ".ts", ".js", ".html", ".css"
)

# Files searched by FUNC:/SYMBOL:.
# Intentional: excludes .md and normal .txt so generated Context Control exports do not recursively match.
$CodeSearchExtensions = @(
    ".h", ".hh", ".hpp", ".hxx", ".inl",
    ".c", ".cc", ".cpp", ".cxx", ".ixx",
    ".cmake",
    ".json", ".ini", ".cfg", ".toml", ".yaml", ".yml",
    ".ps1", ".bat", ".cmd",
    ".glsl", ".vert", ".frag", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".shader",
    ".gd", ".gdshader", ".tscn", ".tres",
    ".cs", ".ts", ".js", ".html", ".css"
)

$ExcludeDirs = @(
    ".git", ".vs", ".vscode", ".idea", ".cache",
    ".project", ".import",
    "node_modules", "dist",
    "build", "build-debug", "build-release",
    "cmake-build-debug", "cmake-build-release",
    "CMakeFiles", "out", "bin", "obj", "x64",
    "Debug", "Release", "RelWithDebInfo", "MinSizeRel",
    "vcpkg_installed", "packages", "PackageCache",
    "external", "extern", "third_party", "thirdparty", "vendor", "deps", "dependencies",
    "__pycache__"
)

$BinaryExtensions = @(
    ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tga", ".dds",
    ".wav", ".mp3", ".ogg", ".flac",
    ".bin", ".collision", ".svo", ".spv",
    ".pdb", ".ilk", ".obj", ".o", ".lib", ".dll", ".exe", ".exp",
    ".db", ".opendb", ".sdf", ".ipch"
)

$script:ExportedFileKeys = @{}

function Get-NormalizedPathKey {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    try {
        if (Test-Path -LiteralPath $Path) {
            $resolved = (Resolve-Path -LiteralPath $Path).Path
            return ($resolved -replace '\\', '/').ToLowerInvariant()
        }
    }
    catch {
    }

    try {
        $full = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
        return ($full -replace '\\', '/').ToLowerInvariant()
    }
    catch {
        return ($Path -replace '\\', '/').ToLowerInvariant()
    }
}

function Is-GptGeneratedExportFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $fileName = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()

    # Main protection: never search/export generated Context Control exports.
    if ($fileName -like "cc_code_export*") {
        return $true
    }

    # Also protect whatever active output file name the user chose.
    $pathKey = Get-NormalizedPathKey $Path
    $outputKey = Get-NormalizedPathKey $script:OutputFileForFilters

    if ($pathKey -eq $outputKey) {
        return $true
    }

    $outputLeaf = [System.IO.Path]::GetFileName($script:OutputFileForFilters).ToLowerInvariant()
    if ($outputLeaf -ne "" -and ($fileName -eq $outputLeaf -or $fileName.StartsWith($outputLeaf + "."))) {
        return $true
    }

    return $false
}

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

function Write-TextFileUtf8NoBom {
    param(
        [string]$Path,
        [string]$Text
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

$script:OutputLines = New-Object System.Collections.Generic.List[string]

function Add-Line {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        $Text = ""
    }

    [void]$script:OutputLines.Add($Text)
}

function Get-OutputText {
    $text = [string]::Join([Environment]::NewLine, [string[]]$script:OutputLines.ToArray())

    if (-not $text.EndsWith([Environment]::NewLine)) {
        $text += [Environment]::NewLine
    }

    return $text
}

function Save-OutputFile {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $dir = [System.IO.Path]::GetDirectoryName($fullPath)

    if ([string]::IsNullOrWhiteSpace($dir)) {
        $dir = (Get-Location).Path
    }

    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    $leaf = [System.IO.Path]::GetFileName($fullPath)
    $tmpPath = Join-Path $dir (".$leaf.$PID.$([System.Guid]::NewGuid().ToString('N')).tmp")
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)

    [System.IO.File]::WriteAllText($tmpPath, (Get-OutputText), $utf8NoBom)

    $lastError = $null

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            if ([System.IO.File]::Exists($fullPath)) {
                try {
                    [System.IO.File]::Replace($tmpPath, $fullPath, $null, $true)
                }
                catch {
                    Copy-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
                    Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
                }
            }
            else {
                Move-Item -LiteralPath $tmpPath -Destination $fullPath -Force -ErrorAction Stop
            }

            return
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds ([Math]::Min(1000, 50 * $attempt))
        }
    }

    throw "Failed to write '$Path' after retries. Close any editor/preview/indexer using it and try again. Temp output was left at: $tmpPath. Last error: $lastError"
}

function Get-CodeFenceLanguage {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name -eq "cmakelists.txt") {
        return "cmake"
    }

    switch ($ext) {
        ".h" { return "cpp" }
        ".hh" { return "cpp" }
        ".hpp" { return "cpp" }
        ".hxx" { return "cpp" }
        ".inl" { return "cpp" }
        ".c" { return "c" }
        ".cc" { return "cpp" }
        ".cpp" { return "cpp" }
        ".cxx" { return "cpp" }
        ".ixx" { return "cpp" }
        ".cmake" { return "cmake" }
        ".glsl" { return "glsl" }
        ".vert" { return "glsl" }
        ".frag" { return "glsl" }
        ".comp" { return "glsl" }
        ".geom" { return "glsl" }
        ".tesc" { return "glsl" }
        ".tese" { return "glsl" }
        ".mesh" { return "glsl" }
        ".task" { return "glsl" }
        ".shader" { return "glsl" }
        ".gd" { return "gdscript" }
        ".gdshader" { return "glsl" }
        ".json" { return "json" }
        ".tscn" { return "ini" }
        ".tres" { return "ini" }
        ".cfg" { return "ini" }
        ".ini" { return "ini" }
        ".toml" { return "toml" }
        ".yaml" { return "yaml" }
        ".yml" { return "yaml" }
        ".ps1" { return "powershell" }
        ".bat" { return "batch" }
        ".cmd" { return "batch" }
        ".ts" { return "typescript" }
        ".js" { return "javascript" }
        ".html" { return "html" }
        ".css" { return "css" }
        ".md" { return "markdown" }
        default { return "" }
    }
}

function Clean-InputLine {
    param([string]$Line)

    if ($null -eq $Line) {
        return ""
    }

    $x = $Line.Trim()

    if ($x -eq "") {
        return ""
    }

    if ($x.StartsWith($script:Fence3)) {
        return ""
    }

    if ($x.StartsWith("#")) {
        return ""
    }

    if ($x.StartsWith("- ")) {
        $x = $x.Substring(2).Trim()
    }

    if ($x.StartsWith("* ")) {
        $x = $x.Substring(2).Trim()
    }

    $x = $x -replace '^\d+[\.\)]\s+', ''

    $Backtick = [string][char]96

    if ($x.StartsWith($Backtick) -and $x.EndsWith($Backtick) -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith('"') -and $x.EndsWith('"') -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    if ($x.StartsWith("'") -and $x.EndsWith("'") -and $x.Length -ge 2) {
        $x = $x.Substring(1, $x.Length - 2)
    }

    return $x.Trim()
}

function Is-ExcludedPath {
    param([string]$Path)

    if (Is-GptGeneratedExportFile $Path) {
        return $true
    }

    if ($Path -like "*.ccbak.*") {
        return $true
    }

    $parts = $Path -replace '\\', '/'
    $split = $parts -split '/'

    foreach ($part in $split) {
        if ($ExcludeDirs -contains $part) {
            return $true
        }
    }

    return $false
}

function Is-CodeSearchExtension {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name -eq "cmakelists.txt") {
        return $true
    }

    return ($script:CodeSearchExtensions -contains $ext)
}

function Is-TextSearchCandidate {
    param([System.IO.FileInfo]$File)

    if ($null -eq $File) {
        return $false
    }

    if (Is-ExcludedPath $File.FullName) {
        return $false
    }

    $name = [System.IO.Path]::GetFileName($File.Name).ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($File.Name).ToLowerInvariant()

    # FUNC:/SYMBOL: search should only scan real programming/project source files.
    # Do not let previous Context Control exports or analysis markdown recursively match
    # function names and bloat the next export.
    if ($name -like "cc_code_export*.md" -or
        $name -like "*code_export*.md" -or
        $name -like "engine_analysis*.md" -or
        $name -like "*.ccbak.*") {
        return $false
    }

    # Markdown exports/docs are useful when explicitly requested as paths,
    # but they are bad symbol-search candidates because they contain copied code.
    if ($ext -eq ".md") {
        return $false
    }

    if ($BinaryExtensions -contains $ext) {
        return $false
    }

    if (-not ($script:TextExtensions -contains $ext)) {
        return $false
    }

    if ((-not $ForceLargeFiles) -and $File.Length -gt ($MaxFileKB * 1024)) {
        return $false
    }

    return $true
}

function Get-RelativeDisplayPath {
    param([string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $root = (Get-Location).Path

    if ($resolved.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        $rel = $resolved.Substring($root.Length)
        $rel = $rel -replace '^[\\/]+', ''
        return ($rel -replace '\\', '/')
    }

    return ($resolved -replace '\\', '/')
}

function Get-PathKey {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return ($Path -replace '\\', '/').ToLowerInvariant()
    }

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    return ($resolved -replace '\\', '/').ToLowerInvariant()
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

function Add-UniquePathToList {
    param(
        [System.Collections.Generic.List[string]]$List,
        $Seen,
        [string]$Path,
        [string]$Reason
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if (Is-ExcludedPath $Path) {
        return
    }

    $key = Get-PathKey $Path
    if ($Seen.ContainsKey($key)) {
        return
    }

    $Seen[$key] = $true
    $List.Add((Get-RelativeDisplayPath $Path))

    if ($Reason -ne "") {
        Write-Host "Auto-added ${Reason}: $(Get-RelativeDisplayPath $Path)"
    }
}

function Find-MatchingHeadersForSource {
    param([string]$SourcePath)

    $result = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return @()
    }

    $resolved = (Resolve-Path -LiteralPath $SourcePath).Path
    $rel = Get-RelativeDisplayPath $resolved
    $dir = Split-Path -Parent $rel
    $base = [System.IO.Path]::GetFileNameWithoutExtension($rel)
    $exts = @(".h", ".hpp", ".hh", ".hxx", ".inl")

    $candidateRelDirs = New-Object System.Collections.Generic.List[string]

    if ($dir -ne "") {
        $candidateRelDirs.Add($dir)

        if ($dir -match '^src/(.+)$') {
            $candidateRelDirs.Add("include/$($Matches[1])")
        }
        elseif ($dir -eq "src") {
            $candidateRelDirs.Add("include")
        }
    }

    $candidateRelDirs.Add("include")
    $candidateRelDirs.Add("src")

    $seen = @{}

    foreach ($candDir in $candidateRelDirs) {
        foreach ($e in $exts) {
            $candidate = Join-Path (Get-Location).Path (($candDir.TrimEnd('/') + "/" + $base + $e) -replace '/', [System.IO.Path]::DirectorySeparatorChar)
            if (Test-Path -LiteralPath $candidate) {
                $key = Get-PathKey $candidate
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $result.Add($candidate)
                }
            }
        }
    }

    return @($result.ToArray())
}

function Resolve-ShaderIncludePath {
    param(
        [string]$ShaderPath,
        [string]$IncludeText
    )

    if ([string]::IsNullOrWhiteSpace($IncludeText)) {
        return ""
    }

    $includeClean = $IncludeText.Trim() -replace '\\', '/'
    $shaderResolved = (Resolve-Path -LiteralPath $ShaderPath).Path
    $shaderDir = Split-Path -Parent $shaderResolved
    $root = (Get-Location).Path

    $candidates = @(
        (Join-Path $shaderDir ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar)),
        (Join-Path $root ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar)),
        (Join-Path (Join-Path $root "shaders") ($includeClean -replace '/', [System.IO.Path]::DirectorySeparatorChar))
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if (Test-Path -LiteralPath "shaders") {
        $matches = Get-ChildItem -LiteralPath "shaders" -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { ((Get-RelativeDisplayPath $_.FullName) -replace '\\', '/').EndsWith($includeClean, [System.StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1

        if ($null -ne $matches) {
            return $matches.FullName
        }
    }

    return ""
}

function Find-ShaderIncludesForFile {
    param([string]$ShaderPath)

    $result = New-Object System.Collections.Generic.List[string]

    if (-not (Test-Path -LiteralPath $ShaderPath)) {
        return @()
    }

    $content = Read-TextFileAutoEncoding $ShaderPath
    $lines = @(ConvertTo-LineArray $content)
    $max = [Math]::Min(100, $lines.Count)
    $seen = @{}

    for ($i = 0; $i -lt $max; $i++) {
        $line = [string]$lines[$i]
        if ($line -match '^\s*#\s*include\s*[<"]([^>"]+)[>"]') {
            $inc = $Matches[1]
            $resolved = Resolve-ShaderIncludePath $ShaderPath $inc
            if ($resolved -ne "") {
                $key = Get-PathKey $resolved
                if (-not $seen.ContainsKey($key)) {
                    $seen[$key] = $true
                    $result.Add($resolved)
                }
            }
        }
    }

    return @($result.ToArray())
}

function Expand-InputPathsWithAutoDependencies {
    param($Paths)

    $expanded = New-Object System.Collections.Generic.List[string]
    $seen = @{}

    foreach ($path in @($Paths)) {
        $cleanPath = $path -replace '/', [System.IO.Path]::DirectorySeparatorChar

        if (Test-Path -LiteralPath $cleanPath) {
            Add-UniquePathToList $expanded $seen $cleanPath ""

            $item = Get-Item -LiteralPath $cleanPath
            if (-not $item.PSIsContainer) {
                $ext = [System.IO.Path]::GetExtension($item.Name).ToLowerInvariant()

                if (@(".cpp", ".cc", ".cxx", ".c").Contains($ext)) {
                    foreach ($header in @(Find-MatchingHeadersForSource $item.FullName)) {
                        Add-UniquePathToList $expanded $seen $header "matching header"
                    }
                }
                elseif (@(".frag", ".vert", ".comp", ".geom", ".tesc", ".tese", ".mesh", ".task", ".glsl").Contains($ext)) {
                    foreach ($includePath in @(Find-ShaderIncludesForFile $item.FullName)) {
                        Add-UniquePathToList $expanded $seen $includePath "shader include"
                    }
                }
            }
        }
        else {
            $key = ($path -replace '\\', '/').ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $expanded.Add($path)
            }
        }
    }

    return @($expanded.ToArray())
}

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

    if (-not ($script:TextExtensions -contains $ext)) {
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

            if ($BinaryExtensions -contains $ext) {
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

    if ($BinaryExtensions -contains $ext) {
        Add-Line "Skipped binary/runtime cache file: $Path"
        return
    }

    if (-not ($script:TextExtensions -contains $ext)) {
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

function Get-SearchCandidateFiles {
    $candidateRoots = @("src", "include", "shaders", "tools")
    $seen = @{}
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]

    foreach ($rootName in $candidateRoots) {
        if (-not (Test-Path -LiteralPath $rootName)) {
            continue
        }

        $rootItem = Get-Item -LiteralPath $rootName

        if (-not $rootItem.PSIsContainer) {
            continue
        }

        $items = Get-ChildItem -LiteralPath $rootName -Recurse -Force -File -ErrorAction SilentlyContinue

        foreach ($file in $items) {
            if (-not (Is-TextSearchCandidate $file)) {
                continue
            }

            $key = ($file.FullName -replace '\\', '/').ToLowerInvariant()
            if ($seen.ContainsKey($key)) {
                continue
            }

            $seen[$key] = $true
            $files.Add($file)
        }
    }

    # Root-level code/config only. This will include CMakeLists.txt and scripts,
    # but not cc_code_export.md or normal markdown exports.
    $rootFiles = Get-ChildItem -LiteralPath "." -Force -File -ErrorAction SilentlyContinue
    foreach ($file in $rootFiles) {
        if (-not (Is-TextSearchCandidate $file)) {
            continue
        }

        $key = ($file.FullName -replace '\\', '/').ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            continue
        }

        $seen[$key] = $true
        $files.Add($file)
    }

    return @($files.ToArray() | Sort-Object FullName)
}

function Test-FileContainsSymbol {
    param(
        [string]$Path,
        [string]$Symbol
    )

    if (Is-ExcludedPath $Path) {
        return $false
    }

    $escaped = [System.Text.RegularExpressions.Regex]::Escape($Symbol)
    $pattern = "(?<![A-Za-z0-9_])$escaped(?![A-Za-z0-9_])"
    $regex = New-Object System.Text.RegularExpressions.Regex($pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

    try {
        $text = Read-TextFileAutoEncoding $Path
        return $regex.IsMatch($text)
    }
    catch {
        return $false
    }
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

function Resolve-FunctionRequestFiles {
    param([string]$Path)

    $pathText = if ($null -eq $Path) { "" } else { $Path.Trim() }
    $clean = $pathText -replace '/', [System.IO.Path]::DirectorySeparatorChar
    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $seen = @{}

    if ($clean -eq "") {
        return @()
    }

    $items = @()

    if (Test-RequestPathHasWildcard $clean) {
        $items = @(Get-ChildItem -Path $clean -Force -File -ErrorAction SilentlyContinue)
    }
    elseif (Test-Path -LiteralPath $clean) {
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

    foreach ($item in $items) {
        if ($null -eq $item) {
            continue
        }

        if (Is-ExcludedPath $item.FullName) {
            continue
        }

        $ext = [System.IO.Path]::GetExtension($item.FullName).ToLowerInvariant()
        if ($BinaryExtensions -contains $ext) {
            continue
        }

        if (-not ($script:TextExtensions -contains $ext)) {
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
        [int]$MaxHits = 12
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    if ($symbolText -eq "") {
        return
    }

    try {
        $content = Read-TextFileAutoEncoding $Path
        $lines = @(ConvertTo-LineArray $content)
    }
    catch {
        return
    }

    $escaped = [regex]::Escape($symbolText)
    $hitCount = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ([string]$lines[$i] -match $escaped) {
            $lineNo = $i + 1
            $text = ([string]$lines[$i]).Trim()
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
        [switch]$ReportNoMatch
    )

    $symbolText = if ($null -eq $Symbol) { "" } else { $Symbol.Trim() }
    $leafName = Get-FunctionLeafName $symbolText

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
        $content = Read-TextFileAutoEncoding $Path
        $lines = @(ConvertTo-LineArray $content)
    }
    catch {
        if ($ReportNoMatch) {
            Add-Line "Failed to read file for function extraction: $(Get-RelativeDisplayPath $Path)"
            Add-Line ""
        }
        return 0
    }

    $ranges = @(Find-HashableFunctionRanges $Path $lines 100000)
    $leafMatches = @($ranges | Where-Object { $_.Name -eq $leafName })

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

    if ($symbolText.Contains("::")) {
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

    $leafName = Get-FunctionLeafName $symbol
    $rawMatches = New-Object System.Collections.Generic.List[string]
    $bodyMatches = 0

    Add-Line ""
    Add-Line "## FOUND FUNCTION: $symbol"
    Add-Line ""

    foreach ($file in $candidates) {
        $shouldTry = $false

        if ($symbol.Contains("::")) {
            if ((Test-FileContainsSymbol $file.FullName $symbol) -or (Test-FileContainsSymbol $file.FullName $leafName)) {
                $shouldTry = $true
            }
        }
        elseif (Test-FileContainsSymbol $file.FullName $leafName) {
            $shouldTry = $true
        }

        if (-not $shouldTry) {
            continue
        }

        if ((Test-FileContainsSymbol $file.FullName $symbol) -or (Test-FileContainsSymbol $file.FullName $leafName)) {
            $rawMatches.Add($file.FullName)
        }

        $count = Add-FunctionBodyBlocksFromFile -Path $file.FullName -Symbol $symbol
        $bodyMatches += $count
    }

    if ($bodyMatches -eq 0) {
        if ($rawMatches.Count -eq 0) {
            Add-Line "No matching code file found for function symbol: $symbol"
        }
        else {
            Add-Line "Raw symbol matches were found, but no brace-delimited function bodies were isolated."
            Add-Line "Use SYMBOL: $symbol to export whole matching files, or request the exact file if this is a declaration/macro/template edge case."
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

function Add-SymbolSearchExport {
    param([string]$Symbol)

    $symbol = $Symbol.Trim()

    if ($symbol -eq "") {
        return
    }

    # De-duplicate repeated SYMBOL: input lines.
    if ($null -eq $script:SymbolSearchKeys) {
        $script:SymbolSearchKeys = @{}
    }

    $symbolKey = $symbol.ToLowerInvariant()
    if ($script:SymbolSearchKeys.ContainsKey($symbolKey)) {
        Write-Host "Skipping duplicate symbol search: $symbol"
        return
    }
    $script:SymbolSearchKeys[$symbolKey] = $true

    Write-Host "Searching symbol/files: $symbol"

    $candidates = @(Get-SearchCandidateFiles)
    Write-Host "  Candidate text files: $($candidates.Count)"

    $matches = New-Object System.Collections.Generic.List[string]

    foreach ($file in $candidates) {
        if (Test-FileContainsSymbol $file.FullName $symbol) {
            $matches.Add($file.FullName)
        }
    }

    Add-Line ""
    Add-Line "## FOUND SYMBOL: $symbol"
    Add-Line ""

    if ($matches.Count -eq 0) {
        Add-Line "No matching code file found for symbol: $symbol"
        Add-Line ""
        Write-Host "  Found: 0"
        return
    }

    Add-Line "Matched code files:"
    foreach ($path in $matches) {
        Add-Line "- $(Get-RelativeDisplayPath $path)"
    }

    Write-Host "  Found: $($matches.Count)"

    foreach ($path in $matches) {
        Add-FileBlockOnce -Path $path -QuietDuplicate
    }
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
            Add-Line "This usually means the request hit declarations, macros, generated wrappers, or a parser edge case. Use SYMBOL: $symbolText to export whole matching files if needed."
        }
        else {
            Add-Line "No raw symbol occurrences found either. The function name or path pattern is probably wrong."
        }

        Add-Line ""
    }
}

Write-Host ""
Write-Host "Paste file/folder paths, one per line."
Write-Host "Use FUNCTION src/path/File.cpp :: name to extract a specific function body."
Write-Host "FUNCTION paths may use wildcards, e.g. FUNCTION src/world/World*.cpp :: World::foo."
Write-Host "Use FUNC: name to search all code files and extract matching function bodies."
Write-Host "Use SYMBOL: name to search all code files and export whole matching files for declarations/types/constants."
Write-Host "Auto-adds matching headers for .cpp and direct GLSL #includes for shaders."
Write-Host "Optional: run with -HashHints to emit compact HASH values for safer patches."
Write-Host "Finish by pressing Enter on an empty line, or type END."
Write-Host ""

$InputPaths = @()
$InputFunctions = @()
$InputSymbolSearches = @()
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
    if ($clean -match '(?i)^\s*(FUNC|FUNCTION|SYMBOL)\s+(.+?)\s+::\s*(.+?)\s*$') {
        $requestKind = $Matches[1].Trim().ToUpperInvariant()
        $requestPath = $Matches[2].Trim()
        $requestSymbol = $Matches[3].Trim()

        if ($requestPath -eq "" -or $requestSymbol -eq "") {
            throw "Malformed function request: '$clean'. Use: FUNCTION src/path/File.cpp :: SymbolName"
        }

        if ($requestKind -eq "SYMBOL") {
            throw "Malformed SYMBOL request: '$clean'. Use SYMBOL: SymbolName for whole-file symbol search, or FUNCTION src/path/File.cpp :: SymbolName for body extraction."
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
    # SYMBOL: SymbolName -> export whole matching files for declarations/types/constants.
    if ($clean -match '(?i)^\s*(FUNC|FUNCTION|SYMBOL)\s*:\s*(.+?)\s*$') {
        $requestKind = $Matches[1].Trim().ToUpperInvariant()
        $requestSymbol = $Matches[2].Trim()

        if ($requestKind -eq "SYMBOL") {
            $InputSymbolSearches += $requestSymbol
        }
        else {
            $InputFunctions += $requestSymbol
        }
        continue
    }

    # Hard guard: never silently treat malformed FUNCTION/SYMBOL lines as file paths.
    if ($clean -match '(?i)^\s*(FUNC|FUNCTION|SYMBOL)\b') {
        throw "Malformed function/symbol request: '$clean'. Use either 'FUNCTION: SymbolName' or 'FUNCTION src/path/File.cpp :: SymbolName'."
    }

    $InputPaths += $clean
}

if ($InputPaths.Count -eq 0 -and $InputFunctions.Count -eq 0 -and $InputSymbolSearches.Count -eq 0 -and $InputScopedFunctions.Count -eq 0) {
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
Add-Line "- If this export contains Hash hints, copy the matching HASH: value into function/replace_region patch headers. If no hash hint is present, omit HASH:."
Add-Line "- If critical context is missing, ask only for the exact missing files/functions, one path per line, ending with END."
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

foreach ($symbol in $InputSymbolSearches) {
    Add-SymbolSearchExport $symbol
}

Save-OutputFile $OutputFile

Write-Host ""
Write-Host "Done."
Write-Host "Created: $OutputFile"

if (-not $NoClipboard) {
    try {
        Get-OutputText | Set-Clipboard
        Write-Host "Copied export to clipboard too."
    }
    catch {
        Write-Host "Clipboard copy skipped."
    }
}
else {
    Write-Host "Clipboard copy disabled by -NoClipboard."
}
