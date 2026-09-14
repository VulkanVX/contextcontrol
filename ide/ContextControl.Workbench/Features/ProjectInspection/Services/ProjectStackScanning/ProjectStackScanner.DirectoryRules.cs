// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static IEnumerable<DirectoryInfo> EnumerateDirectories(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> EnumerateFiles(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles("*", SafeEnumerationOptions)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool TryGetExcludedDirectoryRule(
        DirectoryInfo directory,
        string relativePath,
        out string rule,
        out string reason)
    {
        rule = BuildDirectoryRule(directory, relativePath);
        reason = "generated/dependency";

        if (AlwaysSkippedDirectories.Contains(directory.Name))
        {
            rule = directory.Name;
            return true;
        }

        if (TopLevelBuildConfigurationDirectories.Contains(directory.Name)
            && IsTopLevelPath(relativePath))
        {
            reason = "build configuration";
            return true;
        }

        if (LooksLikeGeneratedBuildRoot(directory))
        {
            reason = "build output";
            return true;
        }

        if (LooksLikePackageManagerRoot(directory))
        {
            reason = "package manager root";
            return true;
        }

        if (LooksLikeNestedToolProject(directory))
        {
            reason = "tooling project";
            return true;
        }

        if (LooksLikeNestedRepository(directory))
        {
            reason = "nested repository";
            return true;
        }

        return false;
    }

    private static string BuildDirectoryRule(DirectoryInfo directory, string relativePath)
    {
        var normalizedPath = NormalizePath(relativePath);
        return string.IsNullOrWhiteSpace(normalizedPath) ? directory.Name : normalizedPath;
    }

    private static bool IsTopLevelPath(string relativePath)
    {
        var normalized = NormalizePath(relativePath);
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.Contains('/', StringComparison.Ordinal);
    }

    private static bool LooksLikeGeneratedBuildRoot(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, "CMakeCache.txt"))
            || Directory.Exists(Path.Combine(directory.FullName, "CMakeFiles"));
    }

    private static bool LooksLikePackageManagerRoot(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, ".vcpkg-root"));
    }

    private static bool LooksLikeNestedToolProject(DirectoryInfo directory)
    {
        return LooksLikeContextControl(directory);
    }

    private static bool LooksLikeNestedRepository(DirectoryInfo directory)
    {
        return Directory.Exists(Path.Combine(directory.FullName, ".git"))
            || File.Exists(Path.Combine(directory.FullName, ".git"));
    }

    private static bool LooksLikeContextControl(DirectoryInfo directory)
    {
        return File.Exists(Path.Combine(directory.FullName, "ccStart.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "cc.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "ccReplace.ps1"));
    }

    private static void AddExcludedDirectoryUse(
        ScanState state,
        DirectoryInfo directory,
        string relativePath,
        string reason)
    {
        if (reason == "package manager root")
        {
            AddUse(state, "Package manager: " + DetectPackageManagerRootName(directory), relativePath);
        }
        else if (reason == "nested repository")
        {
            AddUse(state, "Nested repository", relativePath);
        }
        else if (reason == "tooling project")
        {
            AddUse(state, "Tooling project", relativePath);
        }
    }

    private static string DetectPackageManagerRootName(DirectoryInfo directory)
    {
        if (File.Exists(Path.Combine(directory.FullName, ".vcpkg-root")))
        {
            return "vcpkg";
        }

        return directory.Name;
    }
}
