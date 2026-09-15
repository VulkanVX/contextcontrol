// CC-DESC: Inventories accessible files without depth or file-count truncation.
namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    private static void ScanDirectory(DirectoryInfo directory, string rootPath, int depth,
        ProjectFileRules rules, ScanState state, bool hiddenByCurrentRules)
    {
        var pending = new Stack<(DirectoryInfo Directory, string HiddenReason)>();
        pending.Push((directory, ""));
        while (pending.TryPop(out var entry))
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            var current = entry.Directory;
            var relative = NormalizePath(Path.GetRelativePath(rootPath, current.FullName));
            state.DirectoriesVisited++;
            var hidden = entry.HiddenReason;
            try
            {
                if (current.FullName != directory.FullName &&
                    (InventoryMetadataDirectories.Contains(current.Name) || IsDependencyDirectory(current, relative, rules)
                        || LooksLikeGeneratedBuildRoot(current) || LooksLikePackageManagerRoot(current)
                        || (current.Attributes & FileAttributes.ReparsePoint) != 0))
                {
                    var reason = (current.Attributes & FileAttributes.ReparsePoint) != 0 ? "directory link (not followed)"
                        : IsDependencyDirectory(current, relative, rules) || LooksLikePackageManagerRoot(current) ? "dependency packages"
                        : LooksLikeGeneratedBuildRoot(current) ? "generated build output" : "metadata/cache";
                    state.DirectoriesExcluded++;
                    state.Notices.Add($"{relative}: {reason}");
                    state.AutoSkippedDirectoryRules.Add(relative);
                    continue;
                }
                if (current.FullName != directory.FullName && rules.ShouldSkipDirectory(current.Name, relative))
                {
                    if (rules.HasCustomDirectoryExclusion(current.Name, relative))
                    {
                        state.DirectoriesExcluded++;
                        state.Notices.Add($"{relative}: project skipped folder");
                        continue;
                    }
                    hidden = $"ignored directory: {relative}";
                    state.DirectoriesSkippedByRules++;
                    AddSample(state.SkippedDirectorySamples, hidden);
                }
                // Streaming enumeration retains files read before an access or IO error.
                foreach (var item in current.EnumerateFileSystemInfos("*", SafeEnumerationOptions))
                {
                    state.CancellationToken.ThrowIfCancellationRequested();
                    if (item is DirectoryInfo child) pending.Push((child, hidden));
                    else if (item is FileInfo file)
                    {
                        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            state.Notices.Add($"{NormalizePath(Path.GetRelativePath(rootPath, file.FullName))}: file link (not followed)");
                            continue;
                        }
                        try { ScanFile(file, rootPath, rules, state, hidden); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            state.IsComplete = false;
                            state.Notices.Add($"{file.FullName}: {ex.Message}");
                        }
                    }
                    if (state.ProgressClock.ElapsedMilliseconds >= 200)
                    {
                        state.Progress?.Report(new(state.FilesSeen, state.DirectoriesVisited, relative));
                        state.ProgressClock.Restart();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                state.IsComplete = false;
                state.Notices.Add($"{relative}: {ex.Message}");
            }
        }
        state.Progress?.Report(new(state.FilesSeen, state.DirectoriesVisited, "Complete"));
    }

    private static void ScanFile(FileInfo file, string rootPath, ProjectFileRules rules, ScanState state, string directoryHiddenReason)
    {
        var bytes = file.Length;
        var relativePath = NormalizePath(Path.GetRelativePath(rootPath, file.FullName));
        var extension = NormalizeExtension(file.Extension);
        const string scope = "Project";
        var fileVisibility = rules.GetVisibilityDecision(relativePath, file.Name, extension);
        var language = GetCodeLanguage(file, state);
        state.FilesSeen++;
        if (extension.Length > 0) Increment(state.ExtensionCounts, extension);
        if (language.Length > 0) Increment(state.LanguageCounts, language);
        // Vendored/generated manifests must not define the host project's stack.
        // Inspect owned source even when its extension or a default folder rule is
        // currently disabled: autosetup must detect the full stack in one pass.
        // Custom skipped folders, package stores and build roots were excluded above;
        // explicit skipped-file patterns remain excluded from content analysis.
        if (fileVisibility.ShouldShow || fileVisibility.IgnoredReason.StartsWith("ignored extension:", StringComparison.OrdinalIgnoreCase))
        {
            DetectManifestSignals(file, relativePath, state);
            DetectFileSignals(file, relativePath, state);
            DetectTextUseSignals(file, relativePath, extension, state);
        }
        var visibility = directoryHiddenReason.Length > 0
            ? ProjectFileVisibilityDecision.Skip(directoryHiddenReason)
            : fileVisibility;
        var tracked = visibility.ShouldShow && rules.ShouldTrackFile(relativePath, file.Name, extension);
        state.Files.Add(new(relativePath, language.Length > 0 ? language : "Other", scope, language.Length > 0,
            tracked, visibility.ShouldShow ? "" : visibility.IgnoredReason, bytes));
        if (!visibility.ShouldShow)
        {
            state.FilesSkippedByRules++;
            if (extension.Length > 0 && visibility.IgnoredReason.StartsWith("ignored extension:", StringComparison.OrdinalIgnoreCase))
                Increment(state.SkippedExtensionCounts, extension);
            AddSample(state.SkippedFileSamples, $"{relativePath} ({visibility.IgnoredReason})");
        }
        else
        {
            state.VisibleFiles++;
            if (tracked) state.TrackedFiles++;
            else
            {
                state.UnsupportedVisibleFiles++;
                Increment(state.UnsupportedExtensionCounts, extension.Length == 0 ? "(no extension)" : extension);
            }
        }
    }
}
