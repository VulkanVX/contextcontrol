# CC-DESC: Helper functions dot-sourced by lib/Cc.Dir.Export.ps1.

function Is-ExcludedItem($Item) {
    if ($Item.Name -like "*.ccbak.*") {
        return $true
    }

    if ($Item.PSIsContainer) {
        # Context Control is usually a tool folder under the real project root.
        # Keep parent-project DIR exports clean by excluding that child folder.
        # To work on Context Control itself, set ProjectRoot to "." or to the
        # absolute contextcontrol path; then this rule does not exclude the root.
        if ($Item.Name -ieq "contextcontrol") {
            $relativeDirectory = Get-CcDirRelativePath $Item
            if ($relativeDirectory -ieq "contextcontrol") {
                return $true
            }
        }

        foreach ($rule in $ExcludeDirs) {
            if (Test-CcDirDirectoryRule $Item $rule) {
                return $true
            }
        }

        return $false
    }

    foreach ($rule in $ExcludeFileNames) {
        if (Test-CcDirFileRule $Item $rule) {
            return $true
        }
    }

    $ext = [System.IO.Path]::GetExtension($Item.Name).ToLower()
    return $ExcludeFileExtensions -contains $ext
}

function Should-Include-TopLevelItem($Item) {
    if ($IncludeAllTopLevel) {
        return $true
    }

    if ($ResolvedProfile -ne "vulkanvx") {
        return $true
    }

    $root = (Get-Location).Path
    $relative = $Item.FullName.Substring($root.Length)
    $relative = $relative -replace '^[\\/]+', ''
    $relative = $relative -replace '\\', '/'

    if ($relative -eq "") {
        return $true
    }

    $top = ($relative -split '/')[0]

    foreach ($allowed in $VulkanVXTopLevelAllowList) {
        if ($relative -eq $allowed) {
            return $true
        }

        if (($allowed -notmatch '\.') -and ($top -eq $allowed)) {
            return $true
        }
    }

    return $false
}

function Format-CcDirField {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    $clean = ([string]$Value).Replace('"', "'").Trim()
    return '"' + $clean + '"'
}

function Get-CcDirKind {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path)
    $lowerName = $name.ToLowerInvariant()
    $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()

    if ($name.Equals("CMakeLists.txt", [System.StringComparison]::OrdinalIgnoreCase)) { return "cmake" }
    if ($lowerName -eq "go.mod") { return "go-module" }
    if ($lowerName -eq "go.sum") { return "go-checksums" }
    if ($lowerName -eq "cargo.lock") { return "rust-lock" }
    if ($lowerName.EndsWith(".gradle") -or $lowerName.EndsWith(".gradle.kts")) { return "gradle" }

    switch ($ext) {
        ".ps1" { return "powershell" }
        ".cs" { return "csharp" }
        ".csproj" { return "csharp-project" }
        ".fs" { return "fsharp" }
        ".fsproj" { return "fsharp-project" }
        ".vb" { return "vbnet" }
        ".cpp" { return "cpp" }
        ".cc" { return "cpp" }
        ".cxx" { return "cpp" }
        ".c" { return "c" }
        ".h" { return "cpp-header" }
        ".hpp" { return "cpp-header" }
        ".hlsl" { return "shader" }
        ".glsl" { return "shader" }
        ".wgsl" { return "shader" }
        ".vert" { return "shader" }
        ".frag" { return "shader" }
        ".comp" { return "shader" }
        ".geom" { return "shader" }
        ".tesc" { return "shader" }
        ".tese" { return "shader" }
        ".mesh" { return "shader" }
        ".task" { return "shader" }
        ".shader" { return "shader" }
        ".metal" { return "shader" }
        ".slang" { return "shader" }
        ".axaml" { return "avalonia-xaml" }
        ".xaml" { return "xaml" }
        ".xml" { return "xml" }
        ".json" { return "json" }
        ".md" { return "markdown" }
        ".txt" { return "text" }
        ".yaml" { return "yaml" }
        ".yml" { return "yaml" }
        ".toml" { return "toml" }
        ".ts" { return "typescript" }
        ".tsx" { return "typescript-react" }
        ".js" { return "javascript" }
        ".jsx" { return "javascript" }
        ".css" { return "css" }
        ".html" { return "html" }
        ".py" { return "python" }
        ".rs" { return "rust" }
        ".java" { return "java" }
        ".kt" { return "kotlin" }
        ".kts" { return "kotlin" }
        ".go" { return "go" }
        ".gd" { return "gdscript" }
        default {
            $trimmed = $ext.TrimStart(".")
            if ([string]::IsNullOrWhiteSpace($trimmed)) { return "file" }
            return $trimmed
        }
    }
}

function Test-CcDirFunctionExportKind {
    param([string]$Kind)

    return @(
        "powershell",
        "csharp",
        "fsharp",
        "vbnet",
        "cpp",
        "c",
        "python",
        "rust",
        "java",
        "kotlin",
        "go",
        "gdscript",
        "typescript",
        "typescript-react",
        "javascript"
    ) -contains $Kind
}

function Get-CcDirExports {
    param([string]$Kind)

    if (Test-CcDirFunctionExportKind $Kind) {
        return "full,function,find"
    }

    return "full,find"
}

function Test-CcDirPathPattern {
    param([string]$Path, [string]$Pattern)

    $cleanPath = (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    $cleanPattern = (Normalize-CcDirRulePath $Pattern).ToLowerInvariant()
    return $cleanPath -like $cleanPattern
}

function Test-CcDirBuildFile {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileName($Path).ToLowerInvariant()
    $lower = (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    if (@(
        "cmakelists.txt",
        "cargo.toml",
        "cargo.lock",
        "package.json",
        "tsconfig.json",
        "pyproject.toml",
        "setup.py",
        "requirements.txt",
        "go.mod",
        "go.sum",
        "pom.xml",
        "build.gradle",
        "settings.gradle",
        "gradle.properties",
        "project.godot",
        "global.json",
        "appsettings.json"
    ) -contains $name) {
        return $true
    }

    if ($name.EndsWith(".csproj") -or
        $name.EndsWith(".fsproj") -or
        $name.EndsWith(".vbproj") -or
        $name.EndsWith(".sln") -or
        $name.EndsWith(".props") -or
        $name.EndsWith(".targets")) {
        return $true
    }

    return $name -like "vite.config.*" -or
        $name -like "next.config.*" -or
        $name -like "webpack.config.*"
}

function Test-CcDirRuntimeEntrypoint {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $lower = $relative.ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($relative).ToLowerInvariant()
    if (@(
        "program.cs",
        "main.cpp",
        "main.c",
        "main.cc",
        "main.cxx",
        "main.go",
        "main.py",
        "app.py",
        "cli.py",
        "__main__.py",
        "src/main.rs",
        "src/lib.rs",
        "build.rs"
    ) -contains $lower -or @(
        "program.cs",
        "main.cpp",
        "main.c",
        "main.go",
        "main.py",
        "app.py",
        "cli.py",
        "__main__.py",
        "main.rs",
        "lib.rs",
        "mod.rs"
    ) -contains $name) {
        return $true
    }

    if ($lower -like "cmd/*/main.go") {
        return $true
    }

    if (@("mainwindow.axaml", "mainwindow.axaml.cs", "mainwindow.xaml", "mainwindow.xaml.cs") -contains $name) {
        return $true
    }

    return $name -like "index.*" -or
        $name -like "main.*" -or
        $name -like "app.*" -or
        $name -like "server.*" -or
        $name -like "route.*" -or
        $name -like "layout.*"
}

function Test-CcDirSubsystemOrchestrator {
    param([string]$Path)

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    if ($name.Contains(".")) {
        return $false
    }

    return $name -match '(?i)(Engine|App|World|Renderer|Render|Server|Router|Route|Pipeline|Manager|Service|Controller|Repository|Config|Store|System|Context|Application)'
}

function Test-CcDirCentralDeclaration {
    param([string]$Path, [string]$Kind)

    if ($Kind -ne "cpp-header") {
        return $false
    }

    return (Test-CcDirSubsystemOrchestrator $Path)
}

function Test-CcDirShaderOrToolEntrypoint {
    param([string]$Path, [string]$Kind)

    $lower = (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    return $Kind -eq "shader" -or $lower.StartsWith("shaders/")
}

function Test-CcDirToolEntrypoint {
    param([string]$Path, [string]$Kind)

    $lower = (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    return $lower.StartsWith("tools/") -or
        $lower.StartsWith("scripts/") -or
        (($Kind -eq "powershell" -or $Kind -eq "python" -or $Kind -eq "javascript" -or $Kind -eq "typescript") -and
            ($lower.Contains("/tools/") -or $lower.Contains("/scripts/")))
}

function Test-CcDirProjectDocAnchor {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $name = [System.IO.Path]::GetFileName($relative).ToLowerInvariant()
    return -not $relative.Contains("/") -and (
        $name -eq "readme.md" -or
        $name -eq "architecture.md" -or
        $name -eq "project.md")
}

function Test-CcDirArtifactPath {
    param([string]$Path)

    $clean = (Normalize-CcDirRulePath $Path).Trim("/")
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return $false
    }

    if ($null -eq $script:CcDirArtifactPathCache) {
        $script:CcDirArtifactPathCache = @{}
    }

    $cacheKey = $clean.ToLowerInvariant()
    if ($script:CcDirArtifactPathCache.ContainsKey($cacheKey)) {
        return [bool]$script:CcDirArtifactPathCache[$cacheKey]
    }

    $result = $false

    if ($null -ne $script:CcDirProjectHints) {
        if ((Test-CcDirPathMatchesAnyRoot $clean @($script:CcDirProjectHints.ThirdPartyRoots)) -or
            (Test-CcDirPathMatchesAnyRoot $clean @($script:CcDirProjectHints.GeneratedRoots))) {
            $script:CcDirArtifactPathCache[$cacheKey] = $true
            return $true
        }
    }

    foreach ($part in $clean.ToLowerInvariant().Split([char]'/')) {
        if ([string]::IsNullOrWhiteSpace($part)) {
            continue
        }

        if (@(
            ".ccreplace.versions",
            ".ccworkbench.browser-data",
            ".ccworkbench.generated-projects",
            ".gptreplace.versions",
            ".git",
            ".idea",
            ".tmp",
            ".tmp-build",
            ".vs",
            ".vscode",
            "__pycache__",
            "artifact",
            "artifacts",
            "bin",
            "build",
            "cache",
            "cmakefiles",
            "codex-harness",
            "debug",
            "deps",
            "dependencies",
            "dist",
            "dotnet-obj",
            "dotnet-out",
            "external",
            "extern",
            "generated",
            "history",
            "node_modules",
            "obj",
            "out",
            "packages",
            "packagecache",
            "ps1_nat",
            "release",
            "test-harness",
            "test-results",
            "third_party",
            "thirdparty",
            "vendor",
            "vcpkg_installed",
            "x64"
        ) -contains $part) {
            $result = $true
            break
        }
    }

    $script:CcDirArtifactPathCache[$cacheKey] = $result
    return $result
}

function Test-CcDirCodingKind {
    param([string]$Kind)

    return (Test-CcDirFunctionExportKind $Kind) -or @(
        "avalonia-xaml",
        "cpp-header",
        "css",
        "html",
        "shader",
        "xaml"
    ) -contains $Kind
}

function Test-CcDirHeaderKind {
    param([string]$Kind)

    return @("cpp-header") -contains $Kind
}

function Test-CcDirCodeLikeAssetKind {
    param([string]$Kind)

    return @("shader") -contains $Kind
}

function Get-CcDirRootLeaf {
    param([string]$Path)

    $clean = (Normalize-CcDirRulePath $Path).Trim("/")
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return ""
    }

    return ($clean -split "/")[-1].ToLowerInvariant()
}

function Test-CcDirSourceRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @(
        "app",
        "apps",
        "client",
        "cmd",
        "components",
        "controls",
        "core",
        "crates",
        "engine",
        "internal",
        "lib",
        "libs",
        "network",
        "networking",
        "packages",
        "pages",
        "pkg",
        "protocol",
        "protocols",
        "render",
        "rendering",
        "routes",
        "server",
        "services",
        "source",
        "sources",
        "src",
        "storage",
        "viewmodels",
        "views",
        "world"
    ) -contains $leaf
}

function Test-CcDirIncludeRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("api", "header", "headers", "inc", "include", "includes", "interface", "interfaces", "public") -contains $leaf
}

function Test-CcDirAssetRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("asset-shaders", "glsl", "hlsl", "shader", "shaders", "wgsl") -contains $leaf
}

function Get-CcDirStableAssetRoot {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $parts = @($relative -split "/" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -lt 2) {
        return ""
    }

    for ($index = 0; $index -lt ($parts.Count - 1); $index++) {
        $candidate = ([string]::Join("/", [string[]]($parts | Select-Object -First ($index + 1)))) + "/"
        if (Test-CcDirAssetRootName $candidate) {
            if ($index -lt ($parts.Count - 2)) {
                return ([string]::Join("/", [string[]]($parts | Select-Object -First ($index + 2)))) + "/"
            }

            return $candidate
        }
    }

    $parentParts = @($parts | Select-Object -First ($parts.Count - 1))
    if ($parentParts.Count -gt 0) {
        return ([string]::Join("/", [string[]]$parentParts)) + "/"
    }

    return ""
}

function Get-CcDirPathDepth {
    param([string]$Path)

    $clean = (Normalize-CcDirRulePath $Path).Trim("/")
    if ([string]::IsNullOrWhiteSpace($clean)) {
        return 0
    }

    if ($null -eq $script:CcDirPathDepthCache) {
        $script:CcDirPathDepthCache = @{}
    }

    $cacheKey = $clean.ToLowerInvariant()
    if ($script:CcDirPathDepthCache.ContainsKey($cacheKey)) {
        return [int]$script:CcDirPathDepthCache[$cacheKey]
    }

    $count = 0
    foreach ($part in $clean.Split([char]'/')) {
        if (-not [string]::IsNullOrWhiteSpace($part)) {
            $count++
        }
    }

    $script:CcDirPathDepthCache[$cacheKey] = $count
    return $count
}

function Get-CcDirStableAnchorRoot {
    param([string]$Path)

    $clean = Normalize-CcDirRulePath $Path
    if ($null -eq $script:CcDirStableAnchorRootCache) {
        $script:CcDirStableAnchorRootCache = @{}
    }

    $cacheKey = $clean.ToLowerInvariant()
    if (-not $script:CcDirStableAnchorRootCache.ContainsKey($cacheKey)) {
        $script:CcDirStableAnchorRootCache[$cacheKey] = Get-CcDirNearestStableRootForAnchor $clean
    }

    return [string]$script:CcDirStableAnchorRootCache[$cacheKey]
}

function Get-CcDirNearestStableRootForAnchor {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $kind = Get-CcDirKind $relative
    if (Test-CcDirCodeLikeAssetKind $kind) {
        $assetRoot = Get-CcDirStableAssetRoot $relative
        if (-not [string]::IsNullOrWhiteSpace($assetRoot)) {
            return $assetRoot
        }
    }

    $parts = @($relative -split "/" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($parts.Count -lt 2) {
        return ""
    }

    $top = $parts[0].ToLowerInvariant()
    if ($top -eq "ide") {
        if ($parts.Count -ge 5 -and
            $parts[2].Equals("Services", [System.StringComparison]::OrdinalIgnoreCase) -and
            $parts[3].Equals("ContextControl", [System.StringComparison]::OrdinalIgnoreCase)) {
            return ([string]::Join("/", [string[]]($parts | Select-Object -First 4))) + "/"
        }

        if ($parts.Count -ge 4) {
            return ([string]::Join("/", [string[]]($parts | Select-Object -First 3))) + "/"
        }

        if ($parts.Count -ge 3) {
            return ([string]::Join("/", [string[]]($parts | Select-Object -First 2))) + "/"
        }
    }

    $parentParts = @($parts | Select-Object -First ($parts.Count - 1))
    $genericInnerFolders = @("base", "common", "core", "detail", "details", "impl", "implementation", "private", "shared")
    while ($parentParts.Count -gt 2 -and $genericInnerFolders -contains $parentParts[$parentParts.Count - 1].ToLowerInvariant()) {
        $parentParts = @($parentParts | Select-Object -First ($parentParts.Count - 1))
    }

    if (@("src", "source", "sources") -contains $top -and $parentParts.Count -ge 2) {
        $take = if ($parentParts.Count -ge 3) { 3 } else { $parentParts.Count }
        return ([string]::Join("/", [string[]]($parentParts | Select-Object -First $take))) + "/"
    }

    if (@("cmd", "components", "controls", "crates", "include", "includes", "internal", "lib", "libs", "pkg", "services", "styles", "viewmodels", "views") -contains $top -and $parts.Count -ge 3) {
        return ([string]::Join("/", [string[]]($parts | Select-Object -First 2))) + "/"
    }

    return $parts[0] + "/"
}

function Test-CcDirToolRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("bin-scripts", "cli", "script", "scripts", "tool", "tools", "util", "utilities", "utils") -contains $leaf
}

function Test-CcDirTestRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("__tests__", "spec", "specs", "test", "tests") -contains $leaf
}

function Test-CcDirExampleRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("bench", "benches", "demo", "demos", "example", "examples", "sample", "samples") -contains $leaf
}

function Test-CcDirCiPackagingRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @(".github", "assembly", "ci", "deploy", "deployment", "install", "installer", "package", "packaging", "release", "releases") -contains $leaf
}

function Test-CcDirDocsRootName {
    param([string]$Path)

    $leaf = Get-CcDirRootLeaf $Path
    return @("doc", "docs", "documentation") -contains $leaf
}

function Test-CcDirPublicInterfaceFile {
    param([string]$Path, [string]$Kind)

    $lower = (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    return (Test-CcDirHeaderKind $Kind) -or
        $lower.StartsWith("include/") -or
        $lower.Contains("/include/") -or
        $lower.StartsWith("api/") -or
        $lower.Contains("/api/") -or
        $lower.StartsWith("interfaces/") -or
        $lower.Contains("/interfaces/") -or
        $lower.StartsWith("public/")
}

function Test-CcDirRepresentativeTestFile {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $lower = $relative.ToLowerInvariant()
    $name = [System.IO.Path]::GetFileNameWithoutExtension($lower)
    return $lower.StartsWith("test/") -or
        $lower.StartsWith("tests/") -or
        $lower.Contains("/test/") -or
        $lower.Contains("/tests/") -or
        $name.StartsWith("test") -or
        $name.EndsWith("test") -or
        $name.EndsWith("tests")
}

function Test-CcDirControlPlaneFile {
    param([string]$Path)

    $relative = Normalize-CcDirRulePath $Path
    $lower = $relative.ToLowerInvariant()
    $name = [System.IO.Path]::GetFileName($lower)
    if (@("ccdir.ps1", "cc.ps1", "ccreplace.ps1") -contains $name) {
        return $true
    }

    return $lower -match '^lib/(cc\.dir\.export|cc\.export\.(source|functions|fileblocks|autodeps|config)|cc\.replace\.(parse|plan|apply|pipeline|target|ranges|versioning))\.ps1$' -or
        $lower -match '^lib/(export|replace)/(cc\.export\.|cc\.replace\.)'
}

function Test-CcDirUiSourceFile {
    param([string]$Path, [string]$Kind)

    $lower = "/" + (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    return @("avalonia-xaml", "xaml", "typescript-react") -contains $Kind -or
        $lower.Contains("/views/") -or
        $lower.Contains("/viewmodels/") -or
        $lower.Contains("/controls/") -or
        $lower.Contains("/components/") -or
        $lower.Contains("/styles/")
}

function Get-CcDirRoutingCategory {
    param([string]$Path, [string]$Kind)

    $relative = Normalize-CcDirRulePath $Path
    if ([string]::IsNullOrWhiteSpace($relative)) { return "other" }

    if ($null -eq $script:CcDirRoutingCategoryCache) {
        $script:CcDirRoutingCategoryCache = @{}
    }

    $cacheKey = "$($relative.ToLowerInvariant())|$(([string]$Kind).ToLowerInvariant())"
    if ($script:CcDirRoutingCategoryCache.ContainsKey($cacheKey)) {
        return [string]$script:CcDirRoutingCategoryCache[$cacheKey]
    }

    $result = "other"
    $profileCategory = (Get-CcDirProfileRootCategory $relative).Trim().ToLowerInvariant()
    switch ($profileCategory) {
        "control-plane" { $result = "control-plane"; break }
        "core-source" { $result = "core-source"; break }
        "ui-source" { $result = "ui-source"; break }
        "test-source" { $result = "test-source"; break }
        "asset-shader" { $result = "asset-shader"; break }
        "support-script" { $result = "support-script"; break }
        "owned-tool" { $result = "support-script"; break }
        "build-config" { $result = "build-config"; break }
        "docs" { $result = "docs"; break }
        default {
            if (Test-CcDirBuildFile $relative) { $result = "build-config" }
            elseif (Test-CcDirProjectDocAnchor $relative) { $result = "docs" }
            elseif (Test-CcDirControlPlaneFile $relative) { $result = "control-plane" }
            elseif (Test-CcDirCodeLikeAssetKind $Kind) { $result = "asset-shader" }
            elseif (Test-CcDirRepresentativeTestFile $relative) { $result = "test-source" }
            elseif (Test-CcDirRuntimeEntrypoint $relative) { $result = "runtime-entrypoint" }
            elseif (Test-CcDirUiSourceFile $relative $Kind) { $result = "ui-source" }
            elseif ((Test-CcDirFunctionExportKind $Kind) -and ((Test-CcDirSubsystemOrchestrator $relative) -or (Test-CcDirCoreImplementationFile $relative $Kind))) { $result = "core-source" }
            elseif (Test-CcDirPublicInterfaceFile $relative $Kind) { $result = "core-source" }
            elseif (Test-CcDirToolEntrypoint $relative $Kind) { $result = "support-script" }
            elseif (Test-CcDirFunctionExportKind $Kind) { $result = "core-source" }
        }
    }

    $script:CcDirRoutingCategoryCache[$cacheKey] = $result
    return $result
}

function Get-CcDirRoutingCategoryPriority {
    param([string]$Category)

    switch ($Category) {
        "control-plane" { return 1000 }
        "runtime-entrypoint" { return 900 }
        "core-source" { return 800 }
        "ui-source" { return 700 }
        "asset-shader" { return 650 }
        "test-source" { return 550 }
        "support-script" { return 450 }
        "build-config" { return 250 }
        "docs" { return 100 }
        default { return 50 }
    }
}

function Get-CcDirAnchorCategory {
    param($Record)

    $path = [string]$Record.Path
    $kind = [string]$Record.Kind
    $score = [int]$Record.Score
    if ($score -lt 0) { return "excluded" }

    if ($null -eq $script:CcDirAnchorCategoryCache) {
        $script:CcDirAnchorCategoryCache = @{}
    }

    $cacheKey = "$($path.ToLowerInvariant())|$($kind.ToLowerInvariant())|$score"
    if ($script:CcDirAnchorCategoryCache.ContainsKey($cacheKey)) {
        return [string]$script:CcDirAnchorCategoryCache[$cacheKey]
    }

    $routingCategory = Get-CcDirRoutingCategory $path $kind
    $result = "other"
    if ($routingCategory -eq "build-config") { $result = "build" }
    elseif ($routingCategory -eq "asset-shader") { $result = "asset" }
    if (@("control-plane", "runtime-entrypoint", "core-source", "ui-source") -contains $routingCategory) {
        $result = "source"
    }
    elseif (@("support-script", "test-source", "docs") -contains $routingCategory) {
        $result = "support"
    }

    $script:CcDirAnchorCategoryCache[$cacheKey] = $result
    return $result
}

function Sort-CcDirAnchorRecords {
    param([object[]]$Records)

    return @($Records |
        Sort-Object @{ Expression = { -(Get-CcDirRoutingCategoryPriority (Get-CcDirRoutingCategory ([string]$_.Path) ([string]$_.Kind))) } }, @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } })
}

function Get-CcDirAnchorRootCount {
    param([hashtable]$RootCounts, [string]$Root)

    if ($RootCounts.ContainsKey($Root)) {
        return [int]$RootCounts[$Root]
    }

    return 0
}

function Test-CcDirRepresentativeAnchor {
    param($Record)

    $path = [string]$Record.Path
    $kind = [string]$Record.Kind
    if (Test-CcDirBuildFile $path) { return $false }
    if (Test-CcDirRepresentativeTestFile $path) { return $false }
    if (Test-CcDirToolEntrypoint $path $kind) { return $false }
    return (Test-CcDirRuntimeEntrypoint $path) -or
        (Test-CcDirSubsystemOrchestrator $path) -or
        (Test-CcDirPublicInterfaceFile $path $kind) -or
        (Test-CcDirCoreImplementationFile $path $kind) -or
        (Test-CcDirShaderOrToolEntrypoint $path $kind) -or
        ((Test-CcDirCodingKind $kind) -and [int]$Record.Score -ge 50)
}

function Test-CcDirCoreImplementationFile {
    param([string]$Path, [string]$Kind)

    if (-not (Test-CcDirCodingKind $Kind) -or
        (Test-CcDirBuildFile $Path) -or
        (Test-CcDirRepresentativeTestFile $Path) -or
        (Test-CcDirToolEntrypoint $Path $Kind)) {
        return $false
    }

    $lower = "/" + (Normalize-CcDirRulePath $Path).ToLowerInvariant()
    foreach ($token in @(
        "/core/",
        "/runtime/",
        "/protocol/",
        "/storage/",
        "/network/",
        "/networking/",
        "/render/",
        "/rendering/",
        "/world/",
        "/engine/",
        "/domain/",
        "/model/",
        "/models/",
        "/module/",
        "/modules/")) {
        if ($lower.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Get-CcDirRepresentativeAnchorPriority {
    param($Record)

    $path = [string]$Record.Path
    $kind = [string]$Record.Kind
    if (Test-CcDirRuntimeEntrypoint $path) { return 700 }
    if (Test-CcDirSubsystemOrchestrator $path) { return 650 }
    if (Test-CcDirPublicInterfaceFile $path $kind) { return 600 }
    if (Test-CcDirCoreImplementationFile $path $kind) { return 500 }
    if (Test-CcDirShaderOrToolEntrypoint $path $kind) { return 450 }
    if (Test-CcDirCodingKind $kind) { return 100 }
    return 0
}

function Sort-CcDirRepresentativeAnchorRecords {
    param([object[]]$Records)

    return @($Records |
        Sort-Object @{ Expression = { -(Get-CcDirRepresentativeAnchorPriority $_) } }, @{ Expression = { -[int]$_.Score } }, @{ Expression = { $_.Path } })
}

function Test-CcDirSelectedSourceRoot {
    param($Root)

    $path = [string]$Root.Path
    if ([string]::IsNullOrWhiteSpace($path) -or (Test-CcDirArtifactPath $path)) {
        return $false
    }

    if ((Test-CcDirTestRootName $path) -or
        (Test-CcDirExampleRootName $path) -or
        (Test-CcDirCiPackagingRootName $path) -or
        (Test-CcDirDocsRootName $path)) {
        return $false
    }

    if ((Test-CcDirSourceRootName $path) -or
        (Test-CcDirIncludeRootName $path) -or
        (Test-CcDirAssetRootName $path)) {
        return $true
    }

    if (-not ($Root.PSObject.Properties.Name -contains "Score")) {
        $role = if ($Root.PSObject.Properties.Name -contains "Role") { [string]$Root.Role } else { "" }
        return $role.IndexOf("source", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $role.IndexOf("workbench", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $role.IndexOf("systems", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $role.IndexOf("packages", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    }

    $bucket = Get-CcDirRootBucket $Root
    return @("source-primary", "source-child", "include", "asset", "implied") -contains $bucket
}

function Test-CcDirAnchorUnderRoot {
    param($Record, [string]$RootPath)

    $path = Normalize-CcDirRulePath ([string]$Record.Path)
    $root = (Normalize-CcDirRulePath $RootPath).TrimEnd("/") + "/"
    return $path.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-CcDirAnchorCoveredByRoots {
    param($Record, [object[]]$Roots)

    $anchorRoot = Get-CcDirStableAnchorRoot ([string]$Record.Path)
    return Test-CcDirRootCoversAnchorRoot $Roots $anchorRoot
}

