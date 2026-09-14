// CC-DESC: Loads a project tree, version history, and persisted file-rule metadata.

using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Services;

public static class ProjectLoader
{
    private const int MaxDepth = 20;
    private const int DefaultExpandedDepth = 2;
    private const long MaxExactLineCountBytes = 512 * 1024;
    private const long EstimatedBytesPerLine = 44;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly EnumerationOptions SafeEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false
    };
    private static readonly HashSet<string> AlwaysIgnoredDirectories = new(PathComparer)
    {
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        ".cache",
        ".godot",
        ".import",
        ".ccReplace.versions",
        "__pycache__",
        "bin",
        "build",
        "build-debug",
        "build-release",
        "cmake-build-debug",
        "cmake-build-release",
        "CMakeFiles",
        "obj",
        "out",
    };
    private static readonly HashSet<string> TopLevelBuildConfigurationDirectories = new(PathComparer)
    {
        "Debug",
        "MinSizeRel",
        "Release",
        "RelWithDebInfo",
        "x64"
    };
    private static readonly HashSet<string> IgnoredDirectories = new(PathComparer)
    {
        "deps",
        "dependencies",
        "dist",
        "external",
        "extern",
        "node_modules",
        "packages",
        "PackageCache",
        "third_party",
        "thirdparty",
        "vendor",
        "vcpkg_installed"
    };
    private static readonly HashSet<string> IncludeableExternalDirectories = new(PathComparer)
    {
        "dependencies",
        "deps",
        "external",
        "extern",
        "node_modules",
        "packages",
        "third_party",
        "thirdparty",
        "vendor",
        "vcpkg_installed"
    };
    private static readonly HashSet<string> VulkanVxTopLevelAllowList = new(PathComparer)
    {
        "assets",
        "CMakeLists.txt",
        "include",
        "maps",
        "README.md",
        "shaders",
        "src",
        "tools"
    };

    public static Task<LoadedProject> LoadAsync(
        string folderPath,
        IEnumerable<string>? includedExternalPaths = null,
        bool showSkippedFiles = false)
    {
        return Task.Run(() => Load(folderPath, includedExternalPaths, showSkippedFiles));
    }

    public static LoadedProject Load(
        string folderPath,
        IEnumerable<string>? includedExternalPaths = null,
        bool showSkippedFiles = false)
    {
        var root = new DirectoryInfo(folderPath);
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException(folderPath);
        }

        var fileRules = ProjectFileRules.Load(root.FullName);
        fileRules.Save();
        var ccRoot = FindContextControlDirectory(root);
        var versionIndexPath = ccRoot is null ? null : Path.Combine(ccRoot.FullName, ".ccReplace.versions", "index.json");
        var commit = ReadGitCommit(root.FullName) ?? (ccRoot is null ? null : ReadGitCommit(ccRoot.FullName)) ?? "none";
        var versionData = LoadVersionData(versionIndexPath, commit, root.Name, root.FullName);
        var profile = DetectProfile(root);
        var includedSet = NormalizeIncludedPaths(includedExternalPaths);

        var fileCount = 0;
        var directoryCount = 0;
        var lineCount = 0L;
        var rootNode = BuildNode(root, root.FullName, 0, profile, includedSet, versionData.CurrentVersions, fileRules, showSkippedFiles, ref fileCount, ref directoryCount, ref lineCount);
        PrepareTree([rootNode]);
        var id = StableId(root.FullName);
        var icon = BuildIcon(root.Name);
        var project = new ProjectTabViewModel(
            id,
            icon,
            root.Name,
            FormatLoc(lineCount),
            fileCount.ToString(),
            directoryCount.ToString(),
            commit,
            root.FullName);

        return new LoadedProject(project, [rootNode], versionData.HistoryByPath, fileRules, isTreePrepared: true);
    }

    private static DirectoryInfo? FindContextControlDirectory(DirectoryInfo root)
    {
        if (LooksLikeContextControl(root))
        {
            return root;
        }

        var nested = Path.Combine(root.FullName, "contextcontrol");
        if (Directory.Exists(nested))
        {
            var nestedInfo = new DirectoryInfo(nested);
            if (LooksLikeContextControl(nestedInfo))
            {
                return nestedInfo;
            }
        }

        return null;
    }

    private static bool LooksLikeContextControl(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, "ccStart.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "cc.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccReplace.ps1"));
    }

    private static ProjectNodeViewModel BuildNode(
        DirectoryInfo directory,
        string rootPath,
        int depth,
        string profile,
        IReadOnlySet<string> includedExternalPaths,
        IReadOnlyDictionary<string, int> currentVersions,
        ProjectFileRules fileRules,
        bool showSkippedFiles,
        ref int fileCount,
        ref int directoryCount,
        ref long lineCount)
    {
        directoryCount++;
        var children = new List<ProjectNodeViewModel>();
        var allFiles = Array.Empty<FileInfo>();

        if (depth < MaxDepth)
        {
            foreach (var childDirectory in EnumerateAllDirectories(directory))
            {
                var relativePath = NormalizePath(Path.GetRelativePath(rootPath, childDirectory.FullName));
                if (ShouldSkipDirectory(childDirectory, relativePath, depth, profile, includedExternalPaths, fileRules))
                {
                    if (showSkippedFiles)
                    {
                        children.Add(BuildSkippedDirectoryNode(childDirectory, rootPath, depth + 1));
                    }

                    continue;
                }

                children.Add(BuildNode(childDirectory, rootPath, depth + 1, profile, includedExternalPaths, currentVersions, fileRules, showSkippedFiles, ref fileCount, ref directoryCount, ref lineCount));
            }

            allFiles = EnumerateAllFiles(directory).ToArray();
            foreach (var file in allFiles)
            {
                var relativePath = NormalizePath(Path.GetRelativePath(rootPath, file.FullName));
                if (!fileRules.ShouldTrackFile(relativePath, file.Name, file.Extension)
                    || !fileRules.ShouldCountLocExtension(file.Extension))
                {
                    if (showSkippedFiles)
                    {
                        children.Add(new ProjectNodeViewModel(file.Name, relativePath, false, "skip", isExternal: true, diskFileCount: 1));
                    }

                    continue;
                }

                var fileLoc = EstimateLoc(file);
                if (fileLoc <= 0)
                {
                    if (showSkippedFiles)
                    {
                        children.Add(new ProjectNodeViewModel(file.Name, relativePath, false, "skip", isExternal: true, diskFileCount: 1));
                    }

                    continue;
                }

                fileCount++;
                lineCount += fileLoc;
                var version = FindVersion(currentVersions, relativePath, directory.Name);
                children.Add(new ProjectNodeViewModel(file.Name, relativePath, false, version is null ? "v1" : $"v{version.Value}", loc: fileLoc, fileCount: 1, diskFileCount: 1));
            }
        }

        var nodePath = depth == 0 ? "" : NormalizePath(Path.GetRelativePath(rootPath, directory.FullName));
        var label = depth == 0 ? "root" : "/";
        var activeChildren = children.Where(child => !child.IsExternal).ToArray();
        var directoryLoc = activeChildren.Sum(child => child.Loc);
        var directoryFileCount = activeChildren.Sum(child => child.FileCount);
        var directoryDiskFileCount = allFiles.Length + activeChildren
            .Where(child => child.IsFolder)
            .Sum(child => child.DiskFileCount);
        return new ProjectNodeViewModel(directory.Name, nodePath, true, label, children, loc: directoryLoc, fileCount: directoryFileCount, diskFileCount: directoryDiskFileCount, directDiskFileCount: allFiles.Length);
    }

    private static ProjectNodeViewModel BuildSkippedDirectoryNode(
        DirectoryInfo directory,
        string rootPath,
        int depth)
    {
        var includeable = IncludeableExternalDirectories.Contains(directory.Name);
        var relativePath = NormalizePath(Path.GetRelativePath(rootPath, directory.FullName));
        var label = includeable ? "dep" : "skip";
        var children = new List<ProjectNodeViewModel>();
        var allFiles = Array.Empty<FileInfo>();

        if (depth < MaxDepth)
        {
            foreach (var childDirectory in EnumerateAllDirectories(directory))
            {
                children.Add(BuildSkippedDirectoryNode(childDirectory, rootPath, depth + 1));
            }

            allFiles = EnumerateAllFiles(directory).ToArray();
            foreach (var file in allFiles)
            {
                var fileRelativePath = NormalizePath(Path.GetRelativePath(rootPath, file.FullName));
                children.Add(new ProjectNodeViewModel(file.Name, fileRelativePath, false, "skip", isExternal: true, diskFileCount: 1));
            }
        }

        var diskFileCount = allFiles.Length + children
            .Where(child => child.IsFolder)
            .Sum(child => child.DiskFileCount);
        return new ProjectNodeViewModel(directory.Name, relativePath, true, label, children, true, includeable, diskFileCount: diskFileCount, directDiskFileCount: allFiles.Length);
    }

    private static IEnumerable<DirectoryInfo> EnumerateAllDirectories(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static bool ShouldSkipDirectory(
        DirectoryInfo directory,
        string relativePath,
        int depth,
        string profile,
        IReadOnlySet<string> includedExternalPaths,
        ProjectFileRules fileRules)
    {
        if (directory.Name.Equals("contextcontrol", StringComparison.OrdinalIgnoreCase) && LooksLikeContextControl(directory))
        {
            return true;
        }

        if (AlwaysIgnoredDirectories.Contains(directory.Name) || fileRules.ShouldSkipDirectory(directory.Name, relativePath))
        {
            return true;
        }

        if (TopLevelBuildConfigurationDirectories.Contains(directory.Name)
            && IsTopLevelPath(relativePath))
        {
            return true;
        }

        if (IsIncludedExternalPath(relativePath, includedExternalPaths))
        {
            return false;
        }

        if (IgnoredDirectories.Contains(directory.Name))
        {
            return true;
        }

        if (profile == "vulkanvx" && depth == 0)
        {
            return !ShouldIncludeVulkanVxTopLevel(relativePath);
        }

        return false;
    }

    private static bool IsIncludedExternalPath(string relativePath, IReadOnlySet<string> includedExternalPaths)
    {
        var normalized = NormalizePath(relativePath);
        return includedExternalPaths.Any(path =>
            normalized.Equals(path, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTopLevelPath(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.Contains('/', StringComparison.Ordinal);
    }

    private static bool ShouldIncludeVulkanVxTopLevel(string relativePath)
    {
        var top = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return top is not null && VulkanVxTopLevelAllowList.Contains(top);
    }

    private static void PrepareTree(IReadOnlyList<ProjectNodeViewModel> roots)
    {
        for (var index = 0; index < roots.Count; index++)
        {
            PrepareNode(roots[index], 0, index == roots.Count - 1, []);
        }
    }

    private static void PrepareNode(
        ProjectNodeViewModel node,
        int depth,
        bool isLast,
        IReadOnlyList<bool> ancestorContinues)
    {
        node.SetTreeState(depth, isLast, ancestorContinues);
        if (depth <= DefaultExpandedDepth && !node.IsExternal)
        {
            node.IsExpanded = true;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var childIsLast = index == node.Children.Count - 1;
            var childAncestors = ancestorContinues.Concat([!childIsLast]).ToArray();

            PrepareNode(child, depth + 1, childIsLast, childAncestors);
        }
    }

    private static IEnumerable<FileInfo> EnumerateAllFiles(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private static int? FindVersion(IReadOnlyDictionary<string, int> currentVersions, string relativePath, string rootName)
    {
        if (currentVersions.TryGetValue(relativePath, out var direct))
        {
            return direct;
        }

        var rootedPath = NormalizePath(Path.Combine(rootName, relativePath));
        if (currentVersions.TryGetValue(rootedPath, out var rooted))
        {
            return rooted;
        }

        return null;
    }

    private static VersionData LoadVersionData(string? indexPath, string commit, string rootName, string rootPath)
    {
        var currentVersions = new Dictionary<string, int>(PathComparer);
        var historyByPath = new Dictionary<string, FileHistoryViewModel>(PathComparer);

        if (string.IsNullOrWhiteSpace(indexPath) || !File.Exists(indexPath))
        {
            return new VersionData(currentVersions, historyByPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(indexPath));
        if (!document.RootElement.TryGetProperty("Files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            return new VersionData(currentVersions, historyByPath);
        }

        var versionRoot = Path.GetDirectoryName(indexPath) ?? "";
        foreach (var file in files.EnumerateArray())
        {
            var path = file.TryGetProperty("Path", out var pathElement) ? pathElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalizedPath = NormalizePath(path);
            var fullPath = file.TryGetProperty("FullPath", out var fullPathElement)
                ? fullPathElement.GetString()
                : null;
            var aliases = BuildVersionAliases(normalizedPath, fullPath, rootName, rootPath);
            if (aliases.Count == 0)
            {
                continue;
            }

            var currentVersion = file.TryGetProperty("CurrentVersion", out var currentElement)
                && currentElement.TryGetInt32(out var parsedVersion)
                    ? parsedVersion
                    : 1;

            foreach (var alias in aliases)
            {
                AddVersionAlias(currentVersions, alias, rootName, currentVersion);
            }

            var versions = new List<VersionEntryViewModel>();
            if (file.TryGetProperty("Versions", out var versionsElement) && versionsElement.ValueKind == JsonValueKind.Array)
            {
                var snapshots = new List<VersionSnapshotRecord>();
                foreach (var versionElement in versionsElement.EnumerateArray())
                {
                    var version = versionElement.TryGetProperty("Version", out var versionId)
                        && versionId.TryGetInt32(out var parsedId)
                            ? parsedId
                            : 1;
                    var timestamp = versionElement.TryGetProperty("Timestamp", out var timestampElement)
                        ? FormatDate(timestampElement.GetString())
                        : "";
                    var reason = versionElement.TryGetProperty("Reason", out var reasonElement)
                        ? reasonElement.GetString() ?? "version snapshot"
                        : "version snapshot";
                    var snapshotPath = versionElement.TryGetProperty("Snapshot", out var snapshotElement)
                        ? ResolveSnapshotPath(versionRoot, snapshotElement.GetString())
                        : "";

                    snapshots.Add(new VersionSnapshotRecord(version, timestamp, reason, snapshotPath));
                }

                snapshots.Sort((left, right) => left.Version.CompareTo(right.Version));
                for (var index = 0; index < snapshots.Count; index++)
                {
                    var current = snapshots[index];
                    var previous = index > 0 ? snapshots[index - 1] : null;
                    var currentFilePath = current.Version == currentVersion
                        ? ResolveCurrentFilePath(rootPath, aliases[0], fullPath)
                        : "";
                    versions.Add(new VersionEntryViewModel(
                        $"v{current.Version}",
                        current.Date,
                        commit,
                        current.Reason,
                        Path.GetFileName(aliases[0]),
                        aliases[0],
                        current.SnapshotPath,
                        previous?.SnapshotPath ?? "",
                        currentFilePath: currentFilePath));
                }

                versions.Reverse();
            }

            if (versions.Count == 0)
            {
                versions.Add(new VersionEntryViewModel(
                    $"v{currentVersion}",
                    "",
                    commit,
                    "tracked by Context Control",
                    Path.GetFileName(aliases[0]),
                    aliases[0],
                    currentFilePath: ResolveCurrentFilePath(rootPath, aliases[0], fullPath)));
            }

            var primaryPath = aliases[0];
            var history = new FileHistoryViewModel(Path.GetFileName(primaryPath), primaryPath, versions);
            foreach (var alias in aliases)
            {
                AddHistoryAlias(historyByPath, alias, rootName, history);
            }
        }

        return new VersionData(currentVersions, historyByPath);
    }

    private static List<string> BuildVersionAliases(string normalizedPath, string? fullPath, string rootName, string rootPath)
    {
        var aliases = new List<string>();
        var seen = new HashSet<string>(PathComparer);

        void Add(string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                return;
            }

            var normalized = NormalizePath(alias);
            if (seen.Add(normalized))
            {
                aliases.Add(normalized);
            }
        }

        var hasRootedFullPath = TryGetRelativeFullPath(rootPath, fullPath, out var relativeFullPath);
        if (hasRootedFullPath)
        {
            Add(relativeFullPath);
            Add(normalizedPath);
        }
        else if (string.IsNullOrWhiteSpace(fullPath))
        {
            Add(normalizedPath);
        }

        var rootPrefix = NormalizePath(rootName) + "/";
        if (hasRootedFullPath && normalizedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Add(normalizedPath[rootPrefix.Length..]);
        }

        return aliases;
    }

    private static bool TryGetRelativeFullPath(string rootPath, string? fullPath, out string relativePath)
    {
        relativePath = "";
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        try
        {
            var rootFullPath = Path.GetFullPath(rootPath);
            var fileFullPath = Path.GetFullPath(fullPath);
            var relative = Path.GetRelativePath(rootFullPath, fileFullPath);
            if (relative.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relative))
            {
                return false;
            }

            relativePath = NormalizePath(relative);
            return !string.IsNullOrWhiteSpace(relativePath);
        }
        catch
        {
            return false;
        }
    }

    private static void AddVersionAlias(Dictionary<string, int> map, string path, string rootName, int version)
    {
        map[path] = version;

        var rootPrefix = NormalizePath(rootName) + "/";
        if (path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            map[path[rootPrefix.Length..]] = version;
        }
    }

    private static void AddHistoryAlias(Dictionary<string, FileHistoryViewModel> map, string path, string rootName, FileHistoryViewModel history)
    {
        map[path] = history;

        var rootPrefix = NormalizePath(rootName) + "/";
        if (path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            map[path[rootPrefix.Length..]] = history;
        }
    }

    private static string? ReadGitCommit(string workingDirectory)
    {
        var headCommit = ReadGitHeadCommit(workingDirectory);
        if (!string.IsNullOrWhiteSpace(headCommit))
        {
            return headCommit;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short HEAD",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(250))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup; commit detection can safely fall back to "none".
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadGitHeadCommit(string workingDirectory)
    {
        try
        {
            var gitDirectory = FindGitDirectory(workingDirectory);
            if (string.IsNullOrWhiteSpace(gitDirectory))
            {
                return null;
            }

            var headPath = Path.Combine(gitDirectory, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                var reference = head[4..].Trim().Replace('/', Path.DirectorySeparatorChar);
                var refPath = Path.Combine(gitDirectory, reference);
                if (File.Exists(refPath))
                {
                    return ShortCommit(File.ReadAllText(refPath).Trim());
                }

                return ReadPackedRef(gitDirectory, head[4..].Trim());
            }

            return ShortCommit(head);
        }
        catch
        {
            return null;
        }
    }

    private static string? FindGitDirectory(string workingDirectory)
    {
        var directory = new DirectoryInfo(workingDirectory);
        while (directory is not null)
        {
            var dotGit = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(dotGit))
            {
                return dotGit;
            }

            if (File.Exists(dotGit))
            {
                var content = File.ReadAllText(dotGit).Trim();
                const string prefix = "gitdir:";
                if (content.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var gitDir = content[prefix.Length..].Trim();
                    return Path.IsPathRooted(gitDir)
                        ? gitDir
                        : Path.GetFullPath(Path.Combine(directory.FullName, gitDir));
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? ReadPackedRef(string gitDirectory, string reference)
    {
        var packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
        if (!File.Exists(packedRefsPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(packedRefsPath))
        {
            if (line.Length == 0 || line[0] is '#' or '^')
            {
                continue;
            }

            var separator = line.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[(separator + 1)..].Trim(), reference, StringComparison.Ordinal))
            {
                return ShortCommit(line[..separator]);
            }
        }

        return null;
    }

    private static string? ShortCommit(string commit)
    {
        var clean = commit.Trim();
        if (clean.Length < 7 || clean.Any(value => !Uri.IsHexDigit(value)))
        {
            return null;
        }

        return clean[..7];
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static HashSet<string> NormalizeIncludedPaths(IEnumerable<string>? paths)
    {
        return paths is null
            ? new HashSet<string>(PathComparer)
            : paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .ToHashSet(PathComparer);
    }

    private static string FormatDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : IsIsoDateOnly(trimmed)
                ? $"{trimmed} 00:00:00"
                : trimmed;
    }

    private static bool IsIsoDateOnly(string value)
    {
        return value.Length == 10
            && char.IsDigit(value[0])
            && char.IsDigit(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3])
            && value[4] == '-'
            && char.IsDigit(value[5])
            && char.IsDigit(value[6])
            && value[7] == '-'
            && char.IsDigit(value[8])
            && char.IsDigit(value[9]);
    }

    private static string DetectProfile(DirectoryInfo root)
    {
        if (File.Exists(Path.Combine(root.FullName, "CMakeLists.txt"))
            && Directory.Exists(Path.Combine(root.FullName, "src"))
            && Directory.Exists(Path.Combine(root.FullName, "include"))
            && Directory.Exists(Path.Combine(root.FullName, "shaders")))
        {
            return "vulkanvx";
        }

        if (File.Exists(Path.Combine(root.FullName, "project.godot"))
            || Directory.Exists(Path.Combine(root.FullName, "scenes")))
        {
            return "godot";
        }

        return "generic";
    }

    public static long CountLoc(FileInfo file)
    {
        if (file.Length <= 0)
        {
            return 0;
        }

        if (file.Length > MaxExactLineCountBytes)
        {
            return Math.Max(1, file.Length / EstimatedBytesPerLine);
        }

        byte[]? rented = null;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(32 * 1024);
            using var stream = file.OpenRead();
            var count = 0L;
            var lastByte = (byte)0;
            int read;
            while ((read = stream.Read(rented, 0, rented.Length)) > 0)
            {
                for (var index = 0; index < read; index++)
                {
                    if (rented[index] == (byte)'\n')
                    {
                        count++;
                    }
                }

                lastByte = rented[read - 1];
            }

            return count == 0 || lastByte != (byte)'\n' ? count + 1 : count;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static long EstimateLoc(FileInfo file)
    {
        return file.Length <= 0
            ? 0
            : Math.Max(1, file.Length / EstimatedBytesPerLine);
    }

    private static string FormatLoc(long lineCount)
    {
        return lineCount.ToString("N0") + " LOC";
    }

    private static string ResolveSnapshotPath(string versionRoot, string? snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return "";
        }

        return Path.IsPathRooted(snapshotPath)
            ? snapshotPath
            : Path.GetFullPath(Path.Combine(versionRoot, snapshotPath));
    }

    private static string ResolveCurrentFilePath(string rootPath, string relativePath, string? fullPath)
    {
        if (!string.IsNullOrWhiteSpace(fullPath) && Path.IsPathRooted(fullPath))
        {
            return Path.GetFullPath(fullPath);
        }

        return Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string BuildIcon(string name)
    {
        var letters = name.Where(char.IsLetterOrDigit).Take(2).ToArray();
        return letters.Length == 0 ? "CC" : new string(letters).ToUpperInvariant();
    }

    private static string StableId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }

    private sealed record VersionSnapshotRecord(
        int Version,
        string Date,
        string Reason,
        string SnapshotPath);

    private sealed class VersionData(
        Dictionary<string, int> currentVersions,
        Dictionary<string, FileHistoryViewModel> historyByPath)
    {
        public Dictionary<string, int> CurrentVersions { get; } = currentVersions;
        public Dictionary<string, FileHistoryViewModel> HistoryByPath { get; } = historyByPath;
    }
}
