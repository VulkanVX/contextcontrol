// CC-DESC: Public facade for deterministic project stack scanning.

namespace ContextControl.Workbench.Services;

public static partial class ProjectStackScanner
{
    public static Task<ProjectStackScanResult> ScanAsync(string projectRoot, ProjectFileRules rules)
    {
        return Task.Run(() => Scan(projectRoot, rules));
    }

    public static ProjectStackScanResult Scan(string projectRoot, ProjectFileRules rules)
    {
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
                ProjectStackRuleSet.Empty());
        }

        var state = new ScanState(root.FullName, rules);
        ScanDirectory(root, root.FullName, 0, rules, state, hiddenByCurrentRules: false);
        AddPostScanStackSignals(state);
        return BuildResult(state);
    }
}
