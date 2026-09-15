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
    ProjectStackRuleSet AutoSetupRules)
{
    public IReadOnlyList<ProjectScanFile> Files { get; init; } = [];
    public IReadOnlyList<string> Notices { get; init; } = [];
    public bool IsComplete { get; init; } = true;
}

public sealed record ProjectScanFile(string RelativePath, string Language, string Scope, bool IsCode,
    bool IsTracked, string HiddenReason, long Bytes)
{
    public string Name => Path.GetFileName(RelativePath);
    public string Status => IsTracked ? "Included" : HiddenReason.Length > 0 ? "Hidden" : "Unsupported";
    public string Detail => HiddenReason.Length > 0 ? HiddenReason : IsTracked ? "Included by current file rules" : "File type is not enabled";
    public string SizeLabel => Bytes < 1024 ? $"{Bytes:N0} B" : Bytes < 1048576 ? $"{Bytes / 1024d:N1} KB" : $"{Bytes / 1048576d:N1} MB";
}

public sealed record ProjectScanProgress(int Files, int Directories, string Path);

public sealed record ProjectStackMetric(string Key, string Value, string Detail);

public sealed record ProjectStackSection(string Title, IReadOnlyList<string> Items);

public sealed record ProjectStackRuleSet(
    IReadOnlyList<string> IgnoredDirectories,
    IReadOnlyList<string> IgnoredFileNames,
    IReadOnlyList<string> IgnoredExtensions,
    IReadOnlyList<string> SupportedExtensions,
    IReadOnlyList<string> LocExtensions)
{
    public IReadOnlyList<string> ShownFiles { get; init; } = [];
    public static ProjectStackRuleSet Empty() => new([], [], [], [], []);
}
