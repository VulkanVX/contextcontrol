// CC-DESC: Persists per-project supported/ignored file rules for tree export and external change tracking.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed class ProjectFileRules
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly string[] DefaultIgnoredDirectories =
    [
        ".ccReplace.versions",
        ".ccWorkbench.browser-data",
        ".ccWorkbench.generated-projects",
        ".angular",
        ".claude",
        ".conan",
        ".conan2",
        ".codex",
        ".cursor",
        ".dart_tool",
        ".expo",
        ".git",
        ".godot",
        ".gptReplace.versions",
        ".gradle",
        ".idea",
        ".import",
        ".m2",
        ".next",
        ".nuxt",
        ".nuget",
        ".parcel-cache",
        ".pnpm-store",
        ".serverless",
        ".svelte-kit",
        ".terraform",
        ".tmp",
        ".tmp-build",
        ".turbo",
        ".venv",
        ".vs",
        ".vscode",
        ".yarn",
        "__pycache__",
        "_build",
        "_deps",
        "bin",
        "build",
        "build-debug",
        "build-release",
        "cmake-build-debug",
        "cmake-build-release",
        "CMakeFiles",
        "codex-harness",
        "coverage",
        "DerivedData",
        "deps",
        "dist",
        "dotnet-obj",
        "dotnet-out",
        "external",
        "extern",
        "generated",
        "node_modules",
        "obj",
        "out",
        "packages",
        "Pods",
        "ps1_nat",
        "ps1_nat_gist_review",
        "test-harness",
        "test-results",
        "third_party",
        "thirdparty",
        "vendor",
        "venv",
        "vcpkg_installed"
    ];

    private static readonly string[] DefaultIgnoredExtensions =
    [
        ".bak",
        ".bin",
        ".bmp",
        ".cache",
        ".collision",
        ".db",
        ".dds",
        ".dll",
        ".exe",
        ".exp",
        ".flac",
        ".ilk",
        ".import",
        ".ipch",
        ".jpg",
        ".jpeg",
        ".lastbuildstate",
        ".lib",
        ".log",
        ".mp3",
        ".o",
        ".obj",
        ".ogg",
        ".opendb",
        ".pdb",
        ".png",
        ".sdf",
        ".snapshot",
        ".spv",
        ".svo",
        ".tga",
        ".tlog",
        ".tmp",
        ".uid",
        ".unsuccessfulbuild",
        ".wav",
        ".webp"
    ];

    private static readonly string[] DefaultIgnoredFileNames =
    [
        ".ccFileRules.json",
        ".ccWorkbench.chat-history*.json",
        ".ccWorkbench.settings.json",
        ".DS_Store",
        "*.generated.json",
        "*.generated.md",
        "*.nat.*",
        "*.tmp.md",
        "*.tmp.txt",
        "*-nat-*.ps1",
        "*_nat_*",
        "*bug-hunt*",
        "*bughunt*",
        "cc_chat_export_*.md",
        "cc_code_export.md",
        "cc_project_dir.md",
        "cc_semantic_map.md",
        "cleanup-*.ps1",
        "desktop.ini",
        "mainwindow_axaml.txt",
        "nat-*.ps1",
        "patch.txt",
        "Thumbs.db"
    ];

    private static readonly string[] DefaultSupportedExtensions =
    [
        ".axaml",
        ".bat",
        ".c",
        ".cc",
        ".cmd",
        ".comp",
        ".cpp",
        ".cs",
        ".csproj",
        ".css",
        ".cxx",
        ".frag",
        ".fs",
        ".fsproj",
        ".geom",
        ".glsl",
        ".h",
        ".hh",
        ".hpp",
        ".html",
        ".hlsl",
        ".hxx",
        ".inc",
        ".ini",
        ".inl",
        ".ipp",
        ".go",
        ".gradle",
        ".java",
        ".js",
        ".json",
        ".jsx",
        ".kt",
        ".kts",
        ".lock",
        ".lua",
        ".m",
        ".md",
        ".mesh",
        ".metal",
        ".mm",
        ".mod",
        ".props",
        ".ps1",
        ".psd1",
        ".psm1",
        ".py",
        ".rs",
        ".shader",
        ".sh",
        ".slang",
        ".sum",
        ".targets",
        ".task",
        ".tesc",
        ".tese",
        ".toml",
        ".ts",
        ".tsx",
        ".txt",
        ".vert",
        ".wgsl",
        ".xaml",
        ".xml",
        ".yaml",
        ".yml"
    ];

    private readonly HashSet<string> _ignoredDirectories;
    private readonly HashSet<string> _ignoredExtensions;
    private readonly HashSet<string> _ignoredFileNames;
    private readonly HashSet<string> _shownFileNames;
    private readonly HashSet<string> _locExtensions;
    private readonly HashSet<string> _supportedExtensions;

    private ProjectFileRules(
        string projectRoot,
        string rulesPath,
        IEnumerable<string> ignoredDirectories,
        IEnumerable<string> ignoredFileNames,
        IEnumerable<string> shownFileNames,
        IEnumerable<string> ignoredExtensions,
        IEnumerable<string> locExtensions,
        IEnumerable<string> supportedExtensions)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        RulesPath = rulesPath;
        _ignoredDirectories = NormalizeNames(ignoredDirectories).ToHashSet(NameComparer);
        _ignoredExtensions = NormalizeExtensions(ignoredExtensions).ToHashSet(NameComparer);
        _ignoredFileNames = NormalizeNames(ignoredFileNames).ToHashSet(NameComparer);
        _shownFileNames = NormalizeNames(shownFileNames).ToHashSet(NameComparer);
        _locExtensions = NormalizeExtensions(locExtensions).ToHashSet(NameComparer);
        _supportedExtensions = NormalizeExtensions(supportedExtensions).ToHashSet(NameComparer);
        RemoveSupportedExtensionsFromIgnored();
    }

    public string ProjectRoot { get; }
    public string RulesPath { get; }
    public IReadOnlyCollection<string> IgnoredDirectories => _ignoredDirectories.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> IgnoredExtensions => _ignoredExtensions.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> IgnoredFileNames => _ignoredFileNames.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> ShownFileNames => _shownFileNames.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> LocExtensions => _locExtensions.OrderBy(value => value, NameComparer).ToArray();
    public IReadOnlyCollection<string> SupportedExtensions => _supportedExtensions.OrderBy(value => value, NameComparer).ToArray();
    public string SupportedLabel => string.Join(", ", SupportedExtensions);
    public string IgnoredLabel => string.Join(", ", IgnoredExtensions);
    public string SupportedExtensionsText => string.Join(Environment.NewLine, SupportedExtensions);
    public string IgnoredExtensionsText => string.Join(Environment.NewLine, IgnoredExtensions);
    public string LocExtensionsText => string.Join(Environment.NewLine, LocExtensions);
    public string IgnoredFileNamesText => string.Join(Environment.NewLine, IgnoredFileNames);
    public string IgnoredDirectoriesText => string.Join(Environment.NewLine, IgnoredDirectories);

    public static ProjectFileRules Load(string projectRoot)
    {
        var fullProjectRoot = Path.GetFullPath(projectRoot);
        var rulesPath = ResolveRulesPath(fullProjectRoot);
        var data = new ProjectFileRulesJson();

        if (File.Exists(rulesPath))
        {
            try
            {
                data = JsonSerializer.Deserialize<ProjectFileRulesJson>(File.ReadAllText(rulesPath), JsonOptions) ?? new ProjectFileRulesJson();
            }
            catch
            {
                data = new ProjectFileRulesJson();
            }
        }

        IEnumerable<string> supportedExtensions = data.SupportedExtensions is not null ? data.SupportedExtensions : DefaultSupportedExtensions;
        IEnumerable<string> ignoredFileNames = data.IgnoredFileNames is not null
            ? data.IgnoredFileNames
            : data.IgnoredFiles is not null ? data.IgnoredFiles : DefaultIgnoredFileNames;
        IEnumerable<string> shownFileNames = data.ShownFileNames is not null
            ? data.ShownFileNames
            : data.ShownFiles is not null ? data.ShownFiles : [];

        return new ProjectFileRules(
            fullProjectRoot,
            rulesPath,
            data.IgnoredDirectories is null ? DefaultIgnoredDirectories : data.IgnoredDirectories,
            ignoredFileNames,
            shownFileNames,
            data.IgnoredExtensions is null ? DefaultIgnoredExtensions : data.IgnoredExtensions,
            data.LocExtensions ?? supportedExtensions,
            supportedExtensions);
    }

    public void Save()
    {
        var parent = Path.GetDirectoryName(RulesPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var data = new ProjectFileRulesJson
        {
            IgnoredDirectories = IgnoredDirectories.ToList(),
            IgnoredFileNames = IgnoredFileNames.ToList(),
            ShownFileNames = ShownFileNames.ToList(),
            IgnoredExtensions = IgnoredExtensions.ToList(),
            LocExtensions = LocExtensions.ToList(),
            SupportedExtensions = SupportedExtensions.ToList()
        };
        File.WriteAllText(RulesPath, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, Utf8NoBom);
    }

    public void UpdateRules(
        string ignoredDirectoriesText,
        string ignoredFileNamesText,
        string ignoredExtensionsText,
        string supportedExtensionsText,
        string locExtensionsText)
    {
        ReplaceSet(_ignoredDirectories, NormalizeNames(SplitRuleText(ignoredDirectoriesText)));
        ReplaceSet(_ignoredFileNames, NormalizeNames(SplitRuleText(ignoredFileNamesText)));
        ReplaceSet(_ignoredExtensions, NormalizeExtensions(SplitRuleText(ignoredExtensionsText)));
        ReplaceSet(_supportedExtensions, NormalizeExtensions(SplitRuleText(supportedExtensionsText)));
        ReplaceSet(_locExtensions, NormalizeExtensions(SplitRuleText(locExtensionsText)));
        RemoveSupportedExtensionsFromIgnored();
    }

    public void ApplyCleanRules(
        IEnumerable<string> ignoredDirectories,
        IEnumerable<string> ignoredFileNames,
        IEnumerable<string> ignoredExtensions,
        IEnumerable<string> supportedExtensions,
        IEnumerable<string> locExtensions)
    {
        ReplaceSet(_ignoredDirectories, NormalizeNames(ignoredDirectories));
        ReplaceSet(_ignoredFileNames, NormalizeNames(ignoredFileNames));
        _shownFileNames.Clear();
        ReplaceSet(_ignoredExtensions, NormalizeExtensions(ignoredExtensions));
        ReplaceSet(_supportedExtensions, NormalizeExtensions(supportedExtensions));
        ReplaceSet(_locExtensions, NormalizeExtensions(locExtensions));
        RemoveSupportedExtensionsFromIgnored();
    }

    public ProjectFileRules CreateSnapshot(
        string ignoredDirectoriesText,
        string ignoredFileNamesText,
        string ignoredExtensionsText,
        string supportedExtensionsText,
        string locExtensionsText)
    {
        return new ProjectFileRules(
            ProjectRoot,
            RulesPath,
            NormalizeNames(SplitRuleText(ignoredDirectoriesText)),
            NormalizeNames(SplitRuleText(ignoredFileNamesText)),
            _shownFileNames,
            NormalizeExtensions(SplitRuleText(ignoredExtensionsText)),
            NormalizeExtensions(SplitRuleText(locExtensionsText)),
            NormalizeExtensions(SplitRuleText(supportedExtensionsText)));
    }

    public void ResetToDefaults()
    {
        ReplaceSet(_ignoredDirectories, NormalizeNames(DefaultIgnoredDirectories));
        ReplaceSet(_ignoredFileNames, NormalizeNames(DefaultIgnoredFileNames));
        _shownFileNames.Clear();
        ReplaceSet(_ignoredExtensions, NormalizeExtensions(DefaultIgnoredExtensions));
        ReplaceSet(_supportedExtensions, NormalizeExtensions(DefaultSupportedExtensions));
        ReplaceSet(_locExtensions, NormalizeExtensions(DefaultSupportedExtensions));
        RemoveSupportedExtensionsFromIgnored();
    }

    public ProjectFileVisibilityDecision GetVisibilityDecision(
        string relativePath,
        string fileName,
        string extension)
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        var cleanFileName = string.IsNullOrWhiteSpace(fileName)
            ? GetLastPathPart(normalizedRelativePath)
            : fileName.Trim();

        if (MatchesShownFile(normalizedRelativePath, cleanFileName))
        {
            return ProjectFileVisibilityDecision.Show();
        }

        if (MatchesIgnoredFile(normalizedRelativePath, cleanFileName))
        {
            return ProjectFileVisibilityDecision.Skip($"ignored file: {cleanFileName}");
        }

        if (IsBackupOrTemporaryFile(cleanFileName))
        {
            return ProjectFileVisibilityDecision.Skip("backup/temp/snapshot file");
        }

        var normalizedExtension = NormalizeExtension(extension);
        if (_ignoredExtensions.Contains(normalizedExtension))
        {
            return ProjectFileVisibilityDecision.Skip($"ignored extension: {normalizedExtension}");
        }

        return ProjectFileVisibilityDecision.Show();
    }

    public ProjectFileTrackDecision GetTrackDecision(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return ProjectFileTrackDecision.Ignore("empty path");
        }

        var normalizedFullPath = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(ProjectRoot, normalizedFullPath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
        {
            return ProjectFileTrackDecision.Ignore("outside project");
        }

        var fileName = Path.GetFileName(normalizedFullPath);
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        if (FindIgnoredDirectory(normalizedRelativePath) is { } ignoredDirectory)
        {
            return ProjectFileTrackDecision.Ignore($"ignored directory: {ignoredDirectory}");
        }

        var visibility = GetVisibilityDecision(normalizedRelativePath, fileName, Path.GetExtension(normalizedFullPath));
        if (!visibility.ShouldShow)
        {
            return ProjectFileTrackDecision.Ignore(visibility.IgnoredReason);
        }

        var extension = NormalizeExtension(Path.GetExtension(normalizedFullPath));
        if (!_supportedExtensions.Contains(extension))
        {
            return ProjectFileTrackDecision.Ignore($"unsupported extension: {extension}");
        }

        if (_ignoredExtensions.Contains(extension))
        {
            return ProjectFileTrackDecision.Ignore($"ignored extension: {extension}");
        }

        return ProjectFileTrackDecision.Track();
    }

    public bool ShouldShowFile(string relativePath, string fileName, string extension)
    {
        return GetVisibilityDecision(relativePath, fileName, extension).ShouldShow;
    }

    public bool ShouldTrackFile(string relativePath, string fileName, string extension)
    {
        var visibility = GetVisibilityDecision(relativePath, fileName, extension);
        if (!visibility.ShouldShow)
        {
            return false;
        }

        var normalizedExtension = NormalizeExtension(extension);
        return _supportedExtensions.Contains(normalizedExtension)
            && !_ignoredExtensions.Contains(normalizedExtension);
    }

    public bool ShouldSkipExtension(string extension)
    {
        return _ignoredExtensions.Contains(NormalizeExtension(extension));
    }

    public bool ShouldCountLocExtension(string extension)
    {
        return _locExtensions.Contains(NormalizeExtension(extension));
    }

    public bool ShouldSkipDirectory(string directoryName, string relativePath = "")
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        var cleanDirectoryName = string.IsNullOrWhiteSpace(directoryName)
            ? GetLastPathPart(normalizedRelativePath)
            : directoryName.Trim();

        return MatchesIgnoredDirectory(normalizedRelativePath, cleanDirectoryName);
    }

    public bool SkipFile(string relativePath)
    {
        var normalizedPath = NormalizeRulePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var changed = _ignoredFileNames.Add(normalizedPath);
        changed |= _shownFileNames.Remove(normalizedPath);
        var fileName = GetLastPathPart(normalizedPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            changed |= _shownFileNames.Remove(fileName);
        }

        return changed;
    }

    public bool ShowFile(string relativePath)
    {
        var normalizedPath = NormalizeRulePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var changed = _ignoredFileNames.Remove(normalizedPath);
        changed |= _shownFileNames.Add(normalizedPath);
        return changed;
    }

    public bool SkipExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length <= 1)
        {
            return false;
        }

        var changed = _ignoredExtensions.Add(normalizedExtension);
        changed |= _supportedExtensions.Remove(normalizedExtension);
        changed |= RemoveShownFileOverridesForExtension(normalizedExtension);
        return changed;
    }

    public bool ShowExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        if (normalizedExtension.Length <= 1)
        {
            return false;
        }

        var changed = _ignoredExtensions.Remove(normalizedExtension);
        changed |= _supportedExtensions.Add(normalizedExtension);
        changed |= RemoveShownFileOverridesForExtension(normalizedExtension);
        return changed;
    }

    public bool HideLocExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        return normalizedExtension.Length > 1 && _locExtensions.Remove(normalizedExtension);
    }

    public bool ShowLocExtension(string extension)
    {
        var normalizedExtension = NormalizeExtension(extension);
        return normalizedExtension.Length > 1 && _locExtensions.Add(normalizedExtension);
    }

    public bool SkipDirectory(string relativePath)
    {
        var normalizedPath = NormalizeRulePath(relativePath);
        return !string.IsNullOrWhiteSpace(normalizedPath) && _ignoredDirectories.Add(normalizedPath);
    }

    public bool ShowDirectory(string relativePath)
    {
        var normalizedPath = NormalizeRulePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var changed = _ignoredDirectories.Remove(normalizedPath);
        var directoryName = GetLastPathPart(normalizedPath);
        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            changed |= _ignoredDirectories.Remove(directoryName);
        }

        return changed;
    }

    private static IEnumerable<string> SplitRuleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['\r', '\n', ',', ';', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(NameComparer);
    }

    private static void ReplaceSet(HashSet<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void RemoveSupportedExtensionsFromIgnored()
    {
        _ignoredExtensions.ExceptWith(_supportedExtensions);
    }

    private static string ResolveRulesPath(string projectRoot)
    {
        var contextControlRoot = FindContextControlRoot(projectRoot) ?? projectRoot;
        return Path.Combine(contextControlRoot, ".ccFileRules.json");
    }

    private static string? FindContextControlRoot(string projectRoot)
    {
        if (LooksLikeContextControl(projectRoot))
        {
            return projectRoot;
        }

        var nested = Path.Combine(projectRoot, "contextcontrol");
        return LooksLikeContextControl(nested) ? nested : null;
    }

    private static bool LooksLikeContextControl(string path)
    {
        return Directory.Exists(path)
            && File.Exists(Path.Combine(path, "ccStart.ps1"))
            && File.Exists(Path.Combine(path, "ccDir.ps1"))
            && File.Exists(Path.Combine(path, "cc.ps1"))
            && File.Exists(Path.Combine(path, "ccReplace.ps1"));
    }

    private static IEnumerable<string> NormalizeNames(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeRulePath)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(NameComparer);
    }

    private static IEnumerable<string> NormalizeExtensions(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeExtension)
            .Distinct(NameComparer);
    }

    private static string NormalizeExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private bool MatchesIgnoredFile(string relativePath, string fileName)
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        var cleanFileName = string.IsNullOrWhiteSpace(fileName) ? GetLastPathPart(normalizedRelativePath) : fileName.Trim();
        foreach (var ignoredFile in _ignoredFileNames)
        {
            if (IsPathRule(ignoredFile))
            {
                if (RuleMatchesText(normalizedRelativePath, ignoredFile))
                {
                    return true;
                }

                continue;
            }

            if (RuleMatchesText(cleanFileName, ignoredFile))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesShownFile(string relativePath, string fileName)
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        var cleanFileName = string.IsNullOrWhiteSpace(fileName) ? GetLastPathPart(normalizedRelativePath) : fileName.Trim();
        foreach (var shownFile in _shownFileNames)
        {
            if (IsPathRule(shownFile))
            {
                if (RuleMatchesText(normalizedRelativePath, shownFile))
                {
                    return true;
                }

                continue;
            }

            if (RuleMatchesText(cleanFileName, shownFile))
            {
                return true;
            }
        }

        return false;
    }

    private bool RemoveShownFileOverridesForExtension(string extension)
    {
        var matchingOverrides = _shownFileNames
            .Where(rule => string.Equals(NormalizeExtension(Path.GetExtension(GetLastPathPart(rule))), extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var rule in matchingOverrides)
        {
            _shownFileNames.Remove(rule);
        }

        return matchingOverrides.Length > 0;
    }

    private bool MatchesIgnoredDirectory(string relativePath, string directoryName)
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        var cleanDirectoryName = string.IsNullOrWhiteSpace(directoryName) ? GetLastPathPart(normalizedRelativePath) : directoryName.Trim();
        foreach (var ignoredDirectory in _ignoredDirectories)
        {
            if (IsPathRule(ignoredDirectory))
            {
                if (PathMatchesDirectoryRule(normalizedRelativePath, ignoredDirectory))
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(cleanDirectoryName, ignoredDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string? FindIgnoredDirectory(string relativePath)
    {
        var normalizedRelativePath = NormalizeRulePath(relativePath);
        foreach (var ignoredDirectory in _ignoredDirectories)
        {
            if (IsPathRule(ignoredDirectory) && PathMatchesDirectoryRule(normalizedRelativePath, ignoredDirectory))
            {
                return ignoredDirectory;
            }
        }

        return normalizedRelativePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => _ignoredDirectories.Contains(part));
    }

    private static bool PathMatchesDirectoryRule(string relativePath, string directoryRule)
    {
        return string.Equals(relativePath, directoryRule, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(directoryRule + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathRule(string rule)
    {
        return rule.Contains('/', StringComparison.Ordinal);
    }

    private static bool RuleMatchesText(string value, string rule)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(rule))
        {
            return false;
        }

        if (!ContainsWildcard(rule))
        {
            return string.Equals(value, rule, StringComparison.OrdinalIgnoreCase);
        }

        var pattern = "^" + Regex.Escape(rule).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsWildcard(string rule)
    {
        return rule.Contains('*', StringComparison.Ordinal) || rule.Contains('?', StringComparison.Ordinal);
    }

    private static bool IsBackupOrTemporaryFile(string fileName)
    {
        return fileName.Contains(".ccbak.", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith("~", StringComparison.Ordinal)
            || fileName.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRulePath(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.Trim('/');
    }

    private static string GetLastPathPart(string path)
    {
        return NormalizeRulePath(path)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ProjectFileRulesJson
    {
        public List<string>? IgnoredDirectories { get; set; }
        public List<string>? IgnoredFileNames { get; set; }
        public List<string>? IgnoredFiles { get; set; }
        public List<string>? ShownFileNames { get; set; }
        public List<string>? ShownFiles { get; set; }
        public List<string>? IgnoredExtensions { get; set; }
        public List<string>? LocExtensions { get; set; }
        public List<string>? SupportedExtensions { get; set; }
    }
}

public sealed record ProjectFileTrackDecision(bool ShouldTrack, string IgnoredReason)
{
    public static ProjectFileTrackDecision Track() => new(true, "");
    public static ProjectFileTrackDecision Ignore(string reason) => new(false, reason);
}

public sealed record ProjectFileVisibilityDecision(bool ShouldShow, string IgnoredReason)
{
    public static ProjectFileVisibilityDecision Show() => new(true, "");
    public static ProjectFileVisibilityDecision Skip(string reason) => new(false, reason);
}
