using System.Collections.ObjectModel;

namespace ContextControl.Workbench.ViewModels;

public sealed class FileHistoryViewModel(
    string name,
    string path,
    IEnumerable<VersionEntryViewModel> versions)
{
    private const int MaxSnapshotDiffLines = 4000;
    private const long MaxExactDiffCells = 3_000_000;
    private bool _statsLoaded;

    public string Name { get; } = name;
    public string Path { get; } = path;
    public ObservableCollection<VersionEntryViewModel> Versions { get; } = new(versions);

    public void EnsureStatsLoaded()
    {
        if (_statsLoaded)
        {
            return;
        }

        _statsLoaded = true;
        var ordered = Versions
            .Where(version => !string.IsNullOrWhiteSpace(GetContentPath(version)))
            .OrderBy(ParseVersionNumber)
            .ToArray();

        SnapshotInfo? previous = null;
        foreach (var version in ordered)
        {
            var current = ReadSnapshotInfo(GetContentPath(version));
            var stats = CalculateChangeStats(previous, current);
            version.SetStats(stats.Added, stats.Removed, current.Loc);
            previous = current;
        }
    }

    private static string GetContentPath(VersionEntryViewModel version)
    {
        return !string.IsNullOrWhiteSpace(version.SnapshotPath)
            ? version.SnapshotPath
            : version.CurrentFilePath;
    }

    private static int ParseVersionNumber(VersionEntryViewModel version)
    {
        var text = version.Version.TrimStart('v', 'V');
        return int.TryParse(text, out var parsed) ? parsed : 0;
    }

    private static SnapshotInfo ReadSnapshotInfo(string snapshotPath)
    {
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return new SnapshotInfo(0, []);
        }

        try
        {
            var lines = new List<string>();
            var keepLines = true;
            var loc = 0L;
            foreach (var line in File.ReadLines(snapshotPath))
            {
                loc++;
                if (!keepLines)
                {
                    continue;
                }

                if (lines.Count >= MaxSnapshotDiffLines)
                {
                    lines.Clear();
                    keepLines = false;
                    continue;
                }

                lines.Add(line);
            }

            return new SnapshotInfo(loc, keepLines ? lines : null);
        }
        catch
        {
            return new SnapshotInfo(0, []);
        }
    }

    private static ChangeStats CalculateChangeStats(SnapshotInfo? previous, SnapshotInfo current)
    {
        if (previous is null)
        {
            return new ChangeStats(ClampLineCount(current.Loc), 0);
        }

        if (previous.Lines is { } previousLines && current.Lines is { } currentLines)
        {
            var common = (long)previousLines.Count * currentLines.Count <= MaxExactDiffCells
                ? CountCommonLines(previousLines, currentLines)
                : CountStableEdges(previousLines, currentLines);
            return new ChangeStats(
                Math.Max(0, currentLines.Count - common),
                Math.Max(0, previousLines.Count - common));
        }

        var shared = Math.Min(previous.Loc, current.Loc);
        return new ChangeStats(
            ClampLineCount(Math.Max(0, current.Loc - shared)),
            ClampLineCount(Math.Max(0, previous.Loc - shared)));
    }

    private static int CountCommonLines(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var previousRow = new int[current.Count + 1];
        var currentRow = new int[current.Count + 1];

        for (var previousIndex = previous.Count - 1; previousIndex >= 0; previousIndex--)
        {
            for (var currentIndex = current.Count - 1; currentIndex >= 0; currentIndex--)
            {
                currentRow[currentIndex] = previous[previousIndex] == current[currentIndex]
                    ? previousRow[currentIndex + 1] + 1
                    : Math.Max(previousRow[currentIndex], currentRow[currentIndex + 1]);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[0];
    }

    private static int CountStableEdges(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        var prefix = 0;
        while (prefix < previous.Count
            && prefix < current.Count
            && previous[prefix] == current[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix + prefix < previous.Count
            && suffix + prefix < current.Count
            && previous[previous.Count - suffix - 1] == current[current.Count - suffix - 1])
        {
            suffix++;
        }

        return prefix + suffix;
    }

    private static int ClampLineCount(long value)
    {
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private sealed record SnapshotInfo(long Loc, IReadOnlyList<string>? Lines);

    private sealed record ChangeStats(int Added, int Removed);
}
