using System.Diagnostics;
using System.Text.Json.Nodes;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class SmokeTestHelpers
{
internal static string NormalizeManifestPath(string path)
{
    var clean = (path ?? "").Trim().Trim('"', '\'', '`').Replace('\\', '/');
    while (clean.StartsWith("./", StringComparison.Ordinal))
    {
        clean = clean[2..];
    }

    return clean.Trim('/');
}

internal static string ClassifyDirExportFile(ContextDirManifestFile file)
{
    var path = file.Path;
    var lower = path.ToLowerInvariant();
    if (IsBuildAnchor(file)) { return "build-config"; }
    if (lower.Equals("readme.md", StringComparison.OrdinalIgnoreCase)
        || lower.Equals("architecture.md", StringComparison.OrdinalIgnoreCase)
        || lower.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
    {
        return "docs";
    }

    if (IsControlPlaneAnchor(file)) { return "control-plane"; }
    if (file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase)) { return "asset-shader"; }
    if (IsTestAnchor(file)) { return "test-source"; }
    if (IsRuntimeEntrypointAnchor(file)) { return "runtime-entrypoint"; }
    if (IsUiAnchor(file)) { return "ui-source"; }
    if (IsSourceAnchor(file) || file.Kind.Equals("cpp-header", StringComparison.OrdinalIgnoreCase)) { return "core-source"; }
    if (lower.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) || lower.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase)) { return "support-script"; }
    return "other";
}

internal static bool IsRepresentativeDirExportCategory(string category)
{
    return category is "control-plane" or "runtime-entrypoint" or "core-source" or "ui-source" or "test-source" or "asset-shader";
}

internal static bool IsDirExportFileRootCovered(ContextDirManifestFile file, string category, IReadOnlyList<ContextDirManifestRoot> roots)
{
    var parent = ParentDirectory(file.Path);
    if (string.IsNullOrWhiteSpace(parent))
    {
        return IsRepoRootCoverageCategory(category);
    }

    var maxDistance = category.Equals("build-config", StringComparison.Ordinal) ? 1 : 2;
    if (IsIdeProjectLevelAnchor(file))
    {
        var projectRoot = IdeProjectRoot(file.Path);
        return roots.Any(root => root.Path.Equals(projectRoot, StringComparison.OrdinalIgnoreCase));
    }

    if (roots.Any(root => RootCoversParent(root.Path, parent, maxDistance)))
    {
        return true;
    }

    var stableRoot = StableManifestAnchorRoot(file.Path, file.Kind);
    return !string.IsNullOrWhiteSpace(stableRoot) && roots.Any(root => root.Path.Equals(stableRoot, StringComparison.OrdinalIgnoreCase));
}

internal static bool IsRepoRootCoverageCategory(string category)
{
    return category is "control-plane" or "runtime-entrypoint" or "support-script" or "build-config";
}

internal static bool IsDirExportFamilyRootCovered(ContextDirManifestFamily family, IReadOnlyList<ContextDirManifestRoot> roots)
{
    var parent = ParentDirectory(family.Path);
    return string.IsNullOrWhiteSpace(parent) || roots.Any(root => RootCoversParent(root.Path, parent, 2));
}

internal static string SuggestedManifestRootForFile(ContextDirManifestFile file)
{
    if (IsIdeProjectLevelAnchor(file))
    {
        return IdeProjectRoot(file.Path);
    }

    var stableRoot = StableManifestAnchorRoot(file.Path, file.Kind);
    if (!string.IsNullOrWhiteSpace(stableRoot))
    {
        return stableRoot;
    }

    var parent = ParentDirectory(file.Path);
    return string.IsNullOrWhiteSpace(parent) ? "<repo-root>" : parent;
}

internal static string SuggestedManifestRootForFamily(ContextDirManifestFamily family)
{
    var parent = ParentDirectory(family.Path);
    return string.IsNullOrWhiteSpace(parent) ? "<repo-root>" : parent;
}

internal static bool RootCoversParent(string rootPath, string parentPath, int maxDistance)
{
    var root = rootPath.Trim('/').Replace('\\', '/') + "/";
    var parent = parentPath.Trim('/').Replace('\\', '/') + "/";
    if (!parent.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return PathDepth(parent) - PathDepth(root) <= maxDistance;
}

internal static int PathDepth(string path)
{
    return path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
}

internal static string ParentDirectory(string path)
{
    var clean = path.Replace('\\', '/').Trim('/');
    var index = clean.LastIndexOf('/');
    return index < 0 ? "" : clean[..(index + 1)];
}

internal static bool IsControlPlaneAnchor(ContextDirManifestFile file)
{
    var path = file.Path.ToLowerInvariant();
    var name = Path.GetFileName(path);
    return name is "cc.ps1" or "ccdir.ps1" or "ccreplace.ps1"
        || path.StartsWith("lib/cc.dir.export", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("lib/cc.export.", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("lib/cc.replace.", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("lib/export/cc.export.", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("lib/replace/cc.replace.", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsTestAnchor(ContextDirManifestFile file)
{
    var path = file.Path.ToLowerInvariant();
    var name = Path.GetFileNameWithoutExtension(path);
    return path.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/test/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("test", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("test", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("tests", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsRuntimeEntrypointAnchor(ContextDirManifestFile file)
{
    var path = file.Path.ToLowerInvariant();
    var name = Path.GetFileName(path);
    return name is "program.cs" or "main.cpp" or "main.c" or "main.go" or "main.py" or "app.py" or "cli.py" or "__main__.py" or "main.rs" or "lib.rs"
        || path.EndsWith("/main.go", StringComparison.OrdinalIgnoreCase)
        || name is "mainwindow.axaml" or "mainwindow.axaml.cs";
}

internal static bool IsUiAnchor(ContextDirManifestFile file)
{
    var path = file.Path.ToLowerInvariant();
    return file.Kind is "avalonia-xaml" or "xaml" or "typescript-react"
        || path.Contains("/views/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/viewmodels/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/controls/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/components/", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/styles/", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsIdeProjectLevelAnchor(ContextDirManifestFile file)
{
    var parts = file.Path.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length < 3 || !parts[0].Equals("ide", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    return file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || (parts.Length == 3 && (IsRuntimeEntrypointAnchor(file) || IsTestAnchor(file)));
}

internal static string IdeProjectRoot(string path)
{
    var parts = path.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    return parts.Length < 2 ? "" : $"{parts[0]}/{parts[1]}/";
}

internal static string RunUniversalCcDirFixtureSmoke(
    string repoRoot,
    string name,
    (string Path, string Text)[] files,
    string[] requiredText,
    string[]? forbiddenText = null,
    DirExportQualityOptions? qualityOptions = null)
{
    var projectRoot = Path.Combine(Path.GetTempPath(), "ContextControlDirManifestFixtures", name, Guid.NewGuid().ToString("N"));
    try
    {
        foreach (var file in files)
        {
            var fullPath = Path.Combine(projectRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, file.Text);
        }

        var output = Path.Combine(projectRoot, "cc_project_dir.md");
        RunCcDir(repoRoot, projectRoot, output, ["-Lod", "0"]);
        var manifest = File.ReadAllText(output);
        RequireTextContains(manifest, "CC-DIR-MANIFEST-V2");
        RequireTextContains(manifest, "LOD: L0_GLOBAL");
        RequireTextContains(manifest, "FIND lines may repeat before END; do not mix FIND with source or EXPAND lines.");
        RequireTextContains(manifest, "EXPAND must be the only request line before END.");
        RequireTextNotContains(manifest, ".ccDirProfile.json");
        RequireTextNotContains(manifest, ".contextcontrol/dir-profile.json");
        RequireTextNotContains(manifest, "```");
        var report = AnalyzeUniversalDirExportQuality(
            manifest,
            (qualityOptions ?? new DirExportQualityOptions())
                .WithDefaults(name, files.Length));
        RequireUniversalDirExportQuality(report);

        foreach (var required in requiredText)
        {
            RequireTextContains(manifest, required);
        }

        foreach (var forbidden in forbiddenText ?? [])
        {
            RequireTextNotContains(manifest, forbidden);
        }

        return manifest;
    }
    finally
    {
        try
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only; temp leftovers must not fail smoke checks.
        }
    }
}

internal static void RequireRootBefore(string manifestText, string firstPath, string secondPath)
{
    var manifest = ContextDirManifestParser.Parse(manifestText);
    var firstIndex = manifest.Roots.Select((root, index) => (root, index)).FirstOrDefault(item => item.root.Path.Equals(firstPath, StringComparison.OrdinalIgnoreCase)).index;
    var secondIndex = manifest.Roots.Select((root, index) => (root, index)).FirstOrDefault(item => item.root.Path.Equals(secondPath, StringComparison.OrdinalIgnoreCase)).index;
    var firstFound = manifest.Roots.Any(root => root.Path.Equals(firstPath, StringComparison.OrdinalIgnoreCase));
    var secondFound = manifest.Roots.Any(root => root.Path.Equals(secondPath, StringComparison.OrdinalIgnoreCase));
    if (!firstFound || !secondFound || firstIndex >= secondIndex)
    {
        throw new InvalidOperationException($"Expected ROOT '{firstPath}' before '{secondPath}'. Actual roots: {string.Join(", ", manifest.Roots.Select(root => root.Path))}");
    }
}

internal static void RequireShaderRootInvariant(string manifestText)
{
    var manifest = ContextDirManifestParser.Parse(manifestText);
    if (!manifest.Roots.Any(root => root.Path.Equals("shaders/", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException($"Expected shader fixture to emit ROOT path=\"shaders/\". Actual roots: {string.Join(", ", manifest.Roots.Select(root => root.Path))}");
    }

    if (!manifest.Files.Any(file =>
            file.Path.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)
            && file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException($"Expected shader fixture to emit at least one shader FILE anchor under shaders/. Actual files: {string.Join(", ", manifest.Files.Select(file => $"{file.Path}:{file.Kind}"))}");
    }
}

internal static void RequireAnchorBudget(string manifestText)
{
    var manifest = ContextDirManifestParser.Parse(manifestText);
    var anchorCap = manifest.Files.Count;
    if (anchorCap == 0)
    {
        throw new InvalidOperationException("Expected L0 manifest to contain FILE anchors.");
    }

    var buildMax = (int)Math.Floor(anchorCap * 0.35);
    var sourceMin = (int)Math.Ceiling(anchorCap * 0.40);
    var buildCount = manifest.Files.Count(file =>
        file.Path.EndsWith("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith("package.json", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith("Cargo.toml", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith("go.mod", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith("pyproject.toml", StringComparison.OrdinalIgnoreCase)
        || file.Path.EndsWith("pom.xml", StringComparison.OrdinalIgnoreCase));
    var sourceCount = manifest.Files.Count(file =>
        file.Kind is "cpp" or "c" or "csharp" or "rust" or "go" or "java" or "kotlin" or "python" or "typescript" or "typescript-react" or "javascript"
        && !file.Path.Contains("/test/", StringComparison.OrdinalIgnoreCase)
        && !file.Path.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
        && !file.Path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase));
    var assetCount = manifest.Files.Count(file => file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase));

    if (buildCount > buildMax)
    {
        throw new InvalidOperationException($"Build/config anchors should stay under {buildMax}; got {buildCount}. Manifest: {manifestText}");
    }

    if (sourceCount < sourceMin)
    {
        throw new InvalidOperationException($"Source anchors should reach {sourceMin}; got {sourceCount}. Manifest: {manifestText}");
    }

    if (assetCount < 1)
    {
        throw new InvalidOperationException($"Expected at least one code-like asset anchor. Manifest: {manifestText}");
    }
}

internal static void RequireBalancedRouterQuality(string manifestText, bool expectAsset, bool isTinyProject = false)
{
    var manifest = ContextDirManifestParser.Parse(manifestText);
    var anchorCap = manifest.Files.Count;
    if (anchorCap == 0)
    {
        throw new InvalidOperationException("Balanced router quality requires L0 FILE anchors.");
    }

    var buildMax = (int)Math.Floor(anchorCap * 0.35);
    var sourceMin = (int)Math.Ceiling(anchorCap * 0.40);
    var maxPerRoot = Math.Max(2, (int)Math.Floor(anchorCap * 0.30));
    var buildCount = manifest.Files.Count(IsBuildAnchor);
    var sourceCount = manifest.Files.Count(IsSourceAnchor);
    if (buildCount > buildMax)
    {
        throw new InvalidOperationException($"Balanced router build/config anchors should stay under {buildMax}; got {buildCount}. Manifest: {manifestText}");
    }

    if (sourceCount < sourceMin)
    {
        throw new InvalidOperationException($"Balanced router source anchors should reach {sourceMin}; got {sourceCount}. Manifest: {manifestText}");
    }

    var sourceRootCoverageLimit = anchorCap >= 12 ? 5 : 3;
    var selectedSourceRoots = manifest.Roots
        .Where(root => IsSelectedSourceRoot(root))
        .Take(Math.Min(sourceRootCoverageLimit, manifest.Roots.Count(root => IsSelectedSourceRoot(root))))
        .ToArray();
    foreach (var root in selectedSourceRoots)
    {
        if (!manifest.Files.Any(file => file.Path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Balanced router should include at least one FILE anchor under selected source ROOT '{root.Path}'. Roots: {string.Join(", ", manifest.Roots.Select(item => item.Path))}. Files: {string.Join(", ", manifest.Files.Select(file => file.Path))}");
        }
    }

    foreach (var file in manifest.Files.Where(IsNavigationAnchor))
    {
        var anchorRoot = StableManifestAnchorRoot(file.Path, file.Kind);
        if (!string.IsNullOrWhiteSpace(anchorRoot) && !HasVisibleCloseRoot(manifest.Roots, anchorRoot))
        {
            throw new InvalidOperationException($"Balanced router should expose a ROOT or close useful ancestor for selected FILE anchor '{file.Path}' with stable root '{anchorRoot}'. Roots: {string.Join(", ", manifest.Roots.Select(item => item.Path))}. Manifest: {manifestText}");
        }
    }

    var rootShares = manifest.Files
        .Select(file => StableManifestAnchorRoot(file.Path, file.Kind))
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .GroupBy(root => root, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var largestRootShare = rootShares.Length == 0 ? 0 : rootShares.Max(group => group.Count());
    if (!isTinyProject && largestRootShare > maxPerRoot)
    {
        throw new InvalidOperationException($"Balanced router should avoid one root consuming more than {maxPerRoot} anchors; got {largestRootShare}. Manifest: {manifestText}");
    }

    if (expectAsset)
    {
        if (!manifest.Roots.Any(root => root.Path.Equals("shaders/", StringComparison.OrdinalIgnoreCase)
                || root.Path.Contains("/shaders/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Balanced router should expose a shader/code-like asset root. Manifest: {manifestText}");
        }

        if (!manifest.Files.Any(file => file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Balanced router should include at least one shader/code-like asset anchor. Manifest: {manifestText}");
        }
    }

    var genericRoleCount = manifest.Files.Count(file => IsGenericRole(file.Role));
    var genericRoleMax = Math.Max(1, (int)Math.Floor(anchorCap * 0.20));
    if (genericRoleCount > genericRoleMax)
    {
        throw new InvalidOperationException($"Balanced router should keep generic roles under {genericRoleMax}; got {genericRoleCount}. Roles: {string.Join(", ", manifest.Files.Select(file => file.Role))}");
    }

    foreach (var root in manifest.Roots)
    {
        var descendants = manifest.Roots.Count(candidate =>
            !candidate.Path.Equals(root.Path, StringComparison.OrdinalIgnoreCase)
            && candidate.Path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase));
        if (descendants >= 3 && !IsAllowedHighLevelRoot(root.Path))
        {
            throw new InvalidOperationException($"Balanced router should suppress redundant ancestor ROOT '{root.Path}' with {descendants} selected children. Roots: {string.Join(", ", manifest.Roots.Select(item => item.Path))}");
        }
    }
}

internal static bool IsSelectedSourceRoot(ContextDirManifestRoot root)
{
    var path = root.Path.Trim('/').Replace('\\', '/');
    var leaf = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
    string[] sourceLeaves =
    [
        "app",
        "apps",
        "client",
        "cmd",
        "components",
        "controls",
        "core",
        "crates",
        "engine",
        "include",
        "includes",
        "internal",
        "lib",
        "libs",
        "network",
        "networking",
        "pages",
        "pkg",
        "protocol",
        "protocols",
        "render",
        "rendering",
        "routes",
        "server",
        "services",
        "shaders",
        "source",
        "sources",
        "src",
        "storage",
        "styles",
        "viewmodels",
        "views",
        "world"
    ];
    string[] supportLeaves =
    [
        ".github",
        "assembly",
        "bench",
        "benches",
        "ci",
        "deploy",
        "deployment",
        "doc",
        "docs",
        "documentation",
        "example",
        "examples",
        "package",
        "packaging",
        "release",
        "sample",
        "samples",
        "test",
        "tests"
    ];
    if (supportLeaves.Contains(leaf, StringComparer.OrdinalIgnoreCase))
    {
        return false;
    }

    if (sourceLeaves.Contains(leaf, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    return root.Role.Contains("source", StringComparison.OrdinalIgnoreCase)
        || root.Role.Contains("workbench", StringComparison.OrdinalIgnoreCase)
        || root.Role.Contains("systems", StringComparison.OrdinalIgnoreCase)
        || root.Role.Contains("packages", StringComparison.OrdinalIgnoreCase)
        || root.Role.Contains("shader", StringComparison.OrdinalIgnoreCase)
        || root.Role.Contains("headers", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsBuildAnchor(ContextDirManifestFile file)
{
    var path = file.Path;
    var name = Path.GetFileName(path);
    return name.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Cargo.lock", StringComparison.OrdinalIgnoreCase)
        || name.Equals("package.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)
        || name.Equals("setup.py", StringComparison.OrdinalIgnoreCase)
        || name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
        || name.Equals("go.mod", StringComparison.OrdinalIgnoreCase)
        || name.Equals("go.sum", StringComparison.OrdinalIgnoreCase)
        || name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)
        || name.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)
        || name.Equals("settings.gradle", StringComparison.OrdinalIgnoreCase)
        || name.Equals("gradle.properties", StringComparison.OrdinalIgnoreCase)
        || name.Equals("project.godot", StringComparison.OrdinalIgnoreCase)
        || name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
        || name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".gradle", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".gradle.kts", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("vite.config.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("next.config.", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("webpack.config.", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsSourceAnchor(ContextDirManifestFile file)
{
    return file.Kind is "cpp" or "c" or "csharp" or "rust" or "go" or "java" or "kotlin" or "python" or "typescript" or "typescript-react" or "javascript"
        && !file.Path.Contains("/test/", StringComparison.OrdinalIgnoreCase)
        && !file.Path.StartsWith("test/", StringComparison.OrdinalIgnoreCase)
        && !file.Path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);
}

internal static bool IsNavigationAnchor(ContextDirManifestFile file)
{
    if (IsBuildAnchor(file))
    {
        return false;
    }

    if (IsSourceAnchor(file))
    {
        return true;
    }

    return file.Kind.Equals("shader", StringComparison.OrdinalIgnoreCase)
        || file.Kind.Equals("cpp-header", StringComparison.OrdinalIgnoreCase)
        || file.Kind.Equals("avalonia-xaml", StringComparison.OrdinalIgnoreCase)
        || file.Kind.Equals("xaml", StringComparison.OrdinalIgnoreCase);
}

internal static bool HasVisibleCloseRoot(IReadOnlyList<ContextDirManifestRoot> roots, string anchorRoot)
{
    var clean = anchorRoot.Trim('/').Replace('\\', '/') + "/";
    var anchorDepth = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    foreach (var root in roots)
    {
        var rootPath = root.Path.Trim('/').Replace('\\', '/') + "/";
        if (!clean.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var rootDepth = rootPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (anchorDepth - rootDepth <= 1)
        {
            return true;
        }
    }

    return false;
}

internal static string StableManifestAnchorRoot(string path, string kind)
{
    var clean = path.Replace('\\', '/').Trim('/');
    var parts = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length < 2)
    {
        return "";
    }

    if (kind.Equals("shader", StringComparison.OrdinalIgnoreCase))
    {
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var candidate = string.Join('/', parts.Take(index + 1)) + "/";
            var leaf = parts[index];
            if (leaf.Equals("shaders", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("shader", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("glsl", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("hlsl", StringComparison.OrdinalIgnoreCase)
                || leaf.Equals("wgsl", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }
    }

    if (parts[0].Equals("ide", StringComparison.OrdinalIgnoreCase)
        && parts.Length >= 5
        && parts[2].Equals("Services", StringComparison.OrdinalIgnoreCase)
        && parts[3].Equals("ContextControl", StringComparison.OrdinalIgnoreCase))
    {
        return $"{parts[0]}/{parts[1]}/{parts[2]}/{parts[3]}/";
    }

    if (parts[0].Equals("ide", StringComparison.OrdinalIgnoreCase) && parts.Length >= 4)
    {
        return $"{parts[0]}/{parts[1]}/{parts[2]}/";
    }

    if (parts[0].Equals("ide", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
    {
        return $"{parts[0]}/{parts[1]}/";
    }

    var top = parts[0];
    var parentParts = parts.Take(parts.Length - 1).ToList();
    string[] genericInnerFolders = ["base", "common", "core", "detail", "details", "impl", "implementation", "private", "shared"];
    while (parentParts.Count > 2 && genericInnerFolders.Contains(parentParts[^1], StringComparer.OrdinalIgnoreCase))
    {
        parentParts.RemoveAt(parentParts.Count - 1);
    }

    if ((top.Equals("src", StringComparison.OrdinalIgnoreCase)
            || top.Equals("source", StringComparison.OrdinalIgnoreCase)
            || top.Equals("sources", StringComparison.OrdinalIgnoreCase))
        && parentParts.Count >= 2)
    {
        var take = parentParts.Count >= 3 ? 3 : parentParts.Count;
        return string.Join('/', parentParts.Take(take)) + "/";
    }

    string[] twoLevelRoots =
    [
        "cmd",
        "components",
        "controls",
        "crates",
        "include",
        "includes",
        "internal",
        "lib",
        "libs",
        "pkg",
        "services",
        "styles",
        "viewmodels",
        "views"
    ];
    if (twoLevelRoots.Contains(top, StringComparer.OrdinalIgnoreCase) && parts.Length >= 3)
    {
        return $"{parts[0]}/{parts[1]}/";
    }

    return $"{parts[0]}/";
}

internal static bool IsGenericRole(string role)
{
    string[] genericRoles =
    [
        "csharp source",
        "cpp source",
        "c source",
        "source file",
        "project file",
        "powershell workflow script",
        "shader program",
        "rust source",
        "go source",
        "java source",
        "kotlin source",
        "python source",
        "typescript source",
        "javascript source"
    ];
    return genericRoles.Contains(role, StringComparer.OrdinalIgnoreCase);
}

internal static bool IsAllowedHighLevelRoot(string path)
{
    var clean = path.Trim('/').Replace('\\', '/');
    var leaf = clean.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
    string[] allowedLeaves =
    [
        "src",
        "source",
        "sources",
        "include",
        "includes",
        "headers",
        "shaders",
        "tools",
        "cmd",
        "internal",
        "pkg",
        "crates",
        "lib"
    ];
    return allowedLeaves.Contains(leaf, StringComparer.OrdinalIgnoreCase);
}
}
