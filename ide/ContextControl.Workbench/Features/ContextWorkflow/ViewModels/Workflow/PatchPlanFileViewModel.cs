// CC-DESC: Presents a ccReplace patch plan grouped by target file for compact review.

using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class PatchPlanFileViewModel : ObservableObject
{
    public PatchPlanFileViewModel(string target, IReadOnlyList<PatchPlanActionSummary> actions)
    {
        Target = string.IsNullOrWhiteSpace(target) ? "(unknown target)" : target;
        Actions = actions;
        IsDirectory = actions.Any(action => action.IsDirectory);
        FileName = BuildFileName(Target, IsDirectory);

        var effective = actions.Where(action => action.IsEffective).ToArray();
        var deltaSource = effective.Length > 0 ? effective : actions.Where(action => !action.IsDuplicate).ToArray();
        Added = deltaSource.Sum(action => action.Added);
        Removed = deltaSource.Sum(action => action.Removed);
        AddedLabel = $"+{Added:N0}";
        RemovedLabel = $"-{Removed:N0}";

        var versionAction = actions.LastOrDefault(action => !string.IsNullOrWhiteSpace(action.VersionLabel));
        Version = versionAction?.VersionLabel ?? "";
        Loc = actions.LastOrDefault(action => action.TotalLocAfter > 0)?.TotalLocAfter ?? 0;
        LocLabel = IsDirectory ? "directory" : $"{Loc:N0} LOC";
        ActionSummary = BuildActionSummary(actions);
        ToolTipText = BuildToolTipText(Target, actions);
    }

    public string Target { get; }
    public string FileName { get; }
    public string Version { get; }
    public string AddedLabel { get; }
    public string RemovedLabel { get; }
    public string LocLabel { get; }
    public string ActionSummary { get; }
    public string ToolTipText { get; }
    public int Added { get; }
    public int Removed { get; }
    public int Loc { get; }
    public bool IsDirectory { get; }
    public bool CanOpen => !IsDirectory && !Target.StartsWith("(", StringComparison.Ordinal);
    public IReadOnlyList<PatchPlanActionSummary> Actions { get; }

    public string Summary
    {
        get
        {
            var version = string.IsNullOrWhiteSpace(Version) ? "" : $" | {Version}";
            return $"{FileName} {AddedLabel} {RemovedLabel} | {LocLabel}{version}";
        }
    }

    private static string BuildFileName(string target, bool isDirectory)
    {
        var normalized = (target ?? "").Replace('\\', '/').TrimEnd('/');
        var fileName = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = isDirectory ? "directory" : "(unknown target)";
        }

        return isDirectory ? $"{fileName}/" : fileName;
    }

    private static string BuildActionSummary(IReadOnlyList<PatchPlanActionSummary> actions)
    {
        if (actions.Count == 0)
        {
            return "No actions";
        }

        var parts = actions
            .GroupBy(action => action.BucketLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => BucketOrder(group.Key))
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{NormalizeBucketLabel(group.Key)} {group.Count():N0}")
            .ToArray();
        var effective = actions.Count(action => action.IsEffective);
        var duplicate = actions.Count(action => action.IsDuplicate);
        var status = duplicate > 0
            ? $"{effective:N0} effective, {duplicate:N0} duplicate"
            : $"{effective:N0} effective";
        return $"{string.Join(", ", parts)} | {status}";
    }

    private static string BuildToolTipText(string target, IReadOnlyList<PatchPlanActionSummary> actions)
    {
        var lines = new List<string> { target };
        foreach (var action in actions
                     .OrderBy(action => BucketOrder(action.BucketLabel))
                     .ThenBy(action => action.PartLabel, StringComparer.OrdinalIgnoreCase)
                     .Take(10))
        {
            lines.Add($"{action.KindLabel}: {action.PartLabel} {action.DeltaLabel}");
        }

        if (actions.Count > 10)
        {
            lines.Add($"{actions.Count - 10:N0} more action(s)");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string NormalizeBucketLabel(string bucket)
    {
        return string.IsNullOrWhiteSpace(bucket) ? "planned" : bucket.ToLowerInvariant();
    }

    private static int BucketOrder(string bucket)
    {
        return (bucket ?? "").ToLowerInvariant() switch
        {
            "created" => 0,
            "changed" => 1,
            "removed" => 2,
            "duplicate" => 3,
            _ => 9
        };
    }
}
