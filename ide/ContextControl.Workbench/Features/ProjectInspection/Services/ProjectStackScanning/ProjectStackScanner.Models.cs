// CC-DESC: Result models for project-stack scanning.

namespace ContextControl.Workbench.Services;

internal sealed record TechnologyPattern(
    string Name,
    IReadOnlyList<string> Needles,
    IReadOnlyList<string> Extensions);

public sealed record ProjectStackScanResult(
    string Summary,
    string DetailsText,
    string ProjectRoot,
    string StackLabel,
    string RuleSummary,
    string ScanSummary,
    IReadOnlyList<ProjectStackMetric> Metrics,
    IReadOnlyList<ProjectStackSection> Sections,
    ProjectStackRuleSet AutoSetupRules);

public sealed record ProjectStackMetric(string Key, string Value, string Detail);

public sealed record ProjectStackSection(string Title, IReadOnlyList<string> Items);

public sealed record ProjectStackRuleSet(
    IReadOnlyList<string> IgnoredDirectories,
    IReadOnlyList<string> IgnoredFileNames,
    IReadOnlyList<string> IgnoredExtensions,
    IReadOnlyList<string> SupportedExtensions,
    IReadOnlyList<string> LocExtensions)
{
    public static ProjectStackRuleSet Empty() => new([], [], [], [], []);
}
