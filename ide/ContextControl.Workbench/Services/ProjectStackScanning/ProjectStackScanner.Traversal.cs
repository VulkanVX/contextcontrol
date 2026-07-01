// CC-DESC: Deterministically detects project stacks and current file-rule coverage.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void ScanDirectory(
        DirectoryInfo directory,
        string rootPath,
        int depth,
        ProjectFileRules rules,
        ScanState state,
        bool hiddenByCurrentRules)
    {
        if (state.DirectoriesVisited >= MaxDirectories)
        {
            state.LimitHit = true;
            return;
        }

        state.DirectoriesVisited++;

        var relativePath = depth == 0
            ? ""
            : NormalizePath(Path.GetRelativePath(rootPath, directory.FullName));
        var childHiddenByCurrentRules = hiddenByCurrentRules;
        if (depth > 0 && TryGetExcludedDirectoryRule(directory, relativePath, out var excludedRule, out var excludedReason))
        {
            state.DirectoriesExcluded++;
            state.AutoSkippedDirectoryRules.Add(excludedRule);
            AddExcludedDirectoryUse(state, directory, relativePath, excludedReason);
            AddSample(state.SkippedDirectorySamples, $"{relativePath} ({excludedReason})");
            return;
        }

        if (depth > 0 && rules.ShouldSkipDirectory(directory.Name, relativePath))
        {
            childHiddenByCurrentRules = true;
            state.DirectoriesSkippedByRules++;
            AddSample(state.SkippedDirectorySamples, $"{relativePath} (current rules)");
        }

        if (depth >= MaxDepth)
        {
            state.LimitHit = true;
            return;
        }

        foreach (var childDirectory in EnumerateDirectories(directory))
        {
            ScanDirectory(childDirectory, rootPath, depth + 1, rules, state, childHiddenByCurrentRules);
            if (state.LimitHit)
            {
                return;
            }
        }

        foreach (var file in EnumerateFiles(directory))
        {
            ScanFile(file, rootPath, rules, state, childHiddenByCurrentRules);
            if (state.LimitHit)
            {
                return;
            }
        }
    }

    private static void ScanFile(
        FileInfo file,
        string rootPath,
        ProjectFileRules rules,
        ScanState state,
        bool hiddenByCurrentRules)
    {
        if (state.FilesSeen >= MaxFiles)
        {
            state.LimitHit = true;
            return;
        }

        state.FilesSeen++;
        var relativePath = NormalizePath(Path.GetRelativePath(rootPath, file.FullName));
        DetectManifestSignals(file, relativePath, state);

        var extension = NormalizeExtension(file.Extension);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            Increment(state.ExtensionCounts, extension);
            if (LanguageByExtension.TryGetValue(extension, out var language))
            {
                Increment(state.LanguageCounts, language);
            }
        }

        DetectFileSignals(file, relativePath, state);
        DetectTextUseSignals(file, relativePath, extension, state);

        var visibility = hiddenByCurrentRules
            ? ProjectFileVisibilityDecision.Skip("ignored directory")
            : rules.GetVisibilityDecision(relativePath, file.Name, file.Extension);
        if (!visibility.ShouldShow)
        {
            state.FilesSkippedByRules++;
            if (!string.IsNullOrWhiteSpace(extension)
                && visibility.IgnoredReason.StartsWith("ignored extension:", StringComparison.OrdinalIgnoreCase))
            {
                Increment(state.SkippedExtensionCounts, extension);
            }

            AddSample(state.SkippedFileSamples, $"{relativePath} ({visibility.IgnoredReason})");
            return;
        }

        state.VisibleFiles++;

        if (rules.ShouldTrackFile(relativePath, file.Name, file.Extension))
        {
            state.TrackedFiles++;
        }
        else
        {
            state.UnsupportedVisibleFiles++;
            Increment(state.UnsupportedExtensionCounts, string.IsNullOrWhiteSpace(extension) ? "(no extension)" : extension);
        }
    }
}
