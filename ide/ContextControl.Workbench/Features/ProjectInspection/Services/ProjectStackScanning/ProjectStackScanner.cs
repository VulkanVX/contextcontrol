// CC-DESC: Public facade for deterministic project stack scanning.

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    static ProjectStackScanner()
    {
        foreach (var pair in AdditionalCodeExtensions) LanguageByExtension.TryAdd(pair.Key, pair.Value);
    }

    public static Task<ProjectStackScanResult> ScanAsync(string projectRoot, ProjectFileRules rules,
        CancellationToken cancellationToken = default, IProgress<ProjectScanProgress>? progress = null)
        => Task.Run(() => Scan(projectRoot, rules, cancellationToken, progress), cancellationToken);

    public static ProjectStackScanResult Scan(string projectRoot, ProjectFileRules rules,
        CancellationToken cancellationToken = default, IProgress<ProjectScanProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = new DirectoryInfo(projectRoot);
        if (!root.Exists)
        {
            return new ProjectStackScanResult(
                "Project folder is missing.",
                $"Project folder is missing: {projectRoot}",
                projectRoot,
                "Missing project",
                "No rules loaded",
                "No scan completed",
                [],
                [],
                ProjectStackRuleSet.Empty()) { IsComplete = false };
        }

        var state = new ScanState(root.FullName, rules) { CancellationToken = cancellationToken, Progress = progress };
        ScanDirectory(root, root.FullName, 0, rules, state, hiddenByCurrentRules: false);
        AddPostScanStackSignals(state);
        return BuildResult(state);
    }
}
