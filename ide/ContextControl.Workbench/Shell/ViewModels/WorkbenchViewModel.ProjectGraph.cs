// CC-DESC: Owns project graph summaries, search, focus, and graph tree text.

using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class WorkbenchViewModel
{
    private void RefreshProjectGraph()
    {
        _projectGraphSearchIndex = null;
        ProjectGraphVersion++;
        ProjectGraphSummary = BuildProjectGraphSummary();
        ProjectGraphTreeText = BuildProjectGraphTreeText();

        if (ProjectGraphSelectedNode is not null && !ContainsProjectNode(ProjectGraphSelectedNode))
        {
            ProjectGraphSelectedNode = null;
        }
        else
        {
            OnPropertyChanged(nameof(ProjectGraphSelectedLabel));
        }

        UpdateProjectGraphSearchSuggestions();
    }

    public void OpenProjectGraphSearch()
    {
        IsProjectGraphSearchOpen = true;
        UpdateProjectGraphSearchSuggestions();
    }

    public void CloseProjectGraphSearch()
    {
        IsProjectGraphSearchOpen = false;
    }

    public void OpenProjectTreeSearch()
    {
        IsProjectFilesPaneOpen = true;
        IsProjectTreeSearchOpen = true;
        FocusProjectTreeSearchResult();
    }

    public void CloseProjectTreeSearch()
    {
        IsProjectTreeSearchOpen = false;
    }

    public void AcceptProjectTreeSearch()
    {
        FocusProjectTreeSearchResult();
    }

    public void AcceptProjectGraphSearch()
    {
        SelectProjectGraphSearchSuggestion(ProjectGraphSearchSuggestions.FirstOrDefault());
    }

    private void FocusProjectTreeNode(ProjectNodeViewModel node)
    {
        if (!TryBuildProjectNodePath(ProjectTree, node, out var path) || path.Count == 0)
        {
            return;
        }

        for (var index = 0; index < path.Count - 1; index++)
        {
            if (path[index].IsFolder)
            {
                path[index].IsExpanded = true;
            }
        }

        RefreshVisibleProjectNodes();
        var rowIndex = FindNodeRowIndex(node);
        if (rowIndex < 0)
        {
            return;
        }

        var row = VisibleTreeRows[rowIndex];
        ReplaceSelectedTreeRows([row]);
        SetProperty(ref _selectedTreeRow, row, nameof(SelectedTreeRow));
        ProjectTreeFocusVersion++;
    }

    private void SelectProjectGraphSearchSuggestion(ProjectGraphSearchSuggestionViewModel? suggestion)
    {
        if (suggestion is null)
        {
            return;
        }

        ProjectGraphSelectedNode = suggestion.Node;
        ProjectGraphCenterVersion++;
    }

    private void FocusProjectTreeSearchResult()
    {
        var query = ProjectTreeSearchText.Trim();
        if (query.Length == 0 || ProjectTree.Count == 0)
        {
            return;
        }

        if (IsTopLocMode)
        {
            var topLocRow = FindBestTopLocSearchRow(query);
            if (topLocRow is null)
            {
                return;
            }

            SetSelectedTreeRows([topLocRow]);
            ProjectTreeFocusVersion++;
            return;
        }

        var entry = FindBestProjectTreeSearchEntry(query);
        if (entry is null)
        {
            return;
        }

        FocusProjectTreeNode(entry.Value.Node);
        if (entry.Value.Node.IsFile)
        {
            SelectedNode = entry.Value.Node;
        }
    }

    private TreeRowViewModel? FindBestTopLocSearchRow(string query)
    {
        TreeRowViewModel? bestRow = null;
        var bestScore = int.MaxValue;
        for (var index = 0; index < TopLocTreeRows.Count; index++)
        {
            var row = TopLocTreeRows[index];
            if (row.Node is not { } node)
            {
                continue;
            }

            var score = ScoreProjectTreeSearch(node.Name, node.Path, row.Depth, index, query);
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestRow = row;
        }

        return bestScore == int.MaxValue ? null : bestRow;
    }

    private ProjectGraphSearchEntry? FindBestProjectTreeSearchEntry(string query)
    {
        var index = GetProjectGraphSearchIndex();
        ProjectGraphSearchEntry? bestEntry = null;
        var bestScore = int.MaxValue;
        for (var indexPosition = 0; indexPosition < index.Count; indexPosition++)
        {
            var entry = index[indexPosition];
            var score = ScoreProjectGraphSearch(entry, query);
            if (score >= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestEntry = entry;
        }

        return bestScore == int.MaxValue ? null : bestEntry;
    }

    private void UpdateProjectGraphSearchSuggestions()
    {
        ProjectGraphSearchSuggestions.Clear();
        var query = ProjectGraphSearchText.Trim();
        if (query.Length == 0 || ProjectTree.Count == 0)
        {
            OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
            return;
        }

        var index = GetProjectGraphSearchIndex();
        Span<int> bestScores = stackalloc int[5];
        Span<int> bestIndexes = stackalloc int[5];
        for (var slot = 0; slot < bestScores.Length; slot++)
        {
            bestScores[slot] = int.MaxValue;
            bestIndexes[slot] = -1;
        }

        for (var indexPosition = 0; indexPosition < index.Count; indexPosition++)
        {
            var score = ScoreProjectGraphSearch(index[indexPosition], query);
            if (score == int.MaxValue || score >= bestScores[^1])
            {
                continue;
            }

            var insertAt = bestScores.Length - 1;
            while (insertAt > 0 && score < bestScores[insertAt - 1])
            {
                bestScores[insertAt] = bestScores[insertAt - 1];
                bestIndexes[insertAt] = bestIndexes[insertAt - 1];
                insertAt--;
            }

            bestScores[insertAt] = score;
            bestIndexes[insertAt] = indexPosition;
        }

        for (var slot = 0; slot < bestIndexes.Length; slot++)
        {
            var entryIndex = bestIndexes[slot];
            if (entryIndex < 0)
            {
                continue;
            }

            var entry = index[entryIndex];
            ProjectGraphSearchSuggestions.Add(new ProjectGraphSearchSuggestionViewModel(
                entry.Node,
                entry.Title,
                entry.Detail,
                entry.Meta));
        }

        OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
        if (ProjectGraphSearchSuggestions.FirstOrDefault() is { } first)
        {
            ProjectGraphSelectedNode = first.Node;
            ProjectGraphCenterVersion++;
        }
    }

    private List<ProjectGraphSearchEntry> GetProjectGraphSearchIndex()
    {
        if (_projectGraphSearchIndex is { } index)
        {
            return index;
        }

        index = new List<ProjectGraphSearchEntry>(Math.Max(256, ProjectTree.Count * 8));
        var stack = new Stack<ProjectNodeViewModel>();
        for (var indexRoot = ProjectTree.Count - 1; indexRoot >= 0; indexRoot--)
        {
            stack.Push(ProjectTree[indexRoot]);
        }

        var order = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var title = string.IsNullOrWhiteSpace(node.DisplayName) ? node.Name : node.DisplayName;
            var detail = string.IsNullOrWhiteSpace(node.Path) ? node.Name : node.Path;
            index.Add(new ProjectGraphSearchEntry(
                node,
                title,
                detail,
                BuildProjectGraphNodeMeta(node),
                node.Depth,
                order++));

            for (var childIndex = node.Children.Count - 1; childIndex >= 0; childIndex--)
            {
                stack.Push(node.Children[childIndex]);
            }
        }

        _projectGraphSearchIndex = index;
        return index;
    }

    private static int ScoreProjectGraphSearch(ProjectGraphSearchEntry entry, string query)
    {
        return ScoreProjectTreeSearch(entry.Title, entry.Detail, entry.Depth, entry.Order, query);
    }

    private static int ScoreProjectTreeSearch(string title, string detail, int depth, int order, string query)
    {
        if (string.Equals(title, query, StringComparison.OrdinalIgnoreCase))
        {
            return depth * 4;
        }

        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100 + depth * 4 + Math.Max(0, title.Length - query.Length);
        }

        var titleIndex = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (titleIndex >= 0)
        {
            return 400 + titleIndex * 8 + depth * 4 + Math.Max(0, title.Length - query.Length);
        }

        if (detail.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 900 + depth * 4 + Math.Max(0, detail.Length - query.Length);
        }

        var detailIndex = detail.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (detailIndex >= 0)
        {
            return 1300 + detailIndex * 4 + depth * 4 + Math.Min(order, 1000);
        }

        return int.MaxValue;
    }

    private static bool TryBuildProjectNodePath(
        IEnumerable<ProjectNodeViewModel> nodes,
        ProjectNodeViewModel target,
        out List<ProjectNodeViewModel> path)
    {
        path = [];
        foreach (var node in nodes)
        {
            if (TryBuildProjectNodePath(node, target, path))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildProjectNodePath(
        ProjectNodeViewModel node,
        ProjectNodeViewModel target,
        List<ProjectNodeViewModel> path)
    {
        path.Add(node);
        if (ReferenceEquals(node, target))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryBuildProjectNodePath(child, target, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private string BuildProjectGraphSummary()
    {
        if (CurrentProject is null)
        {
            return "No project loaded.";
        }

        var skipState = ShowSkippedFiles ? "skipped shown" : "skipped hidden";
        return $"{CurrentProject.FileCount} files | {CurrentProject.DirectoryCount} dirs | {skipState} | current file rules";
    }

    private string BuildProjectGraphTreeText()
    {
        if (CurrentProject is null || ProjectTree.Count == 0)
        {
            return "No project loaded.";
        }

        const int maxLines = 6000;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(CurrentProject.Name);
        builder.AppendLine(CurrentProject.ProjectRoot);
        builder.AppendLine(BuildProjectGraphSummary());
        builder.AppendLine();

        var remainingLines = maxLines;
        var omitted = 0;
        foreach (var node in ProjectTree)
        {
            AppendProjectGraphTreeNode(builder, node, "", true, ref remainingLines, ref omitted);
            if (remainingLines <= 0)
            {
                break;
            }
        }

        if (omitted > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"... {omitted:N0}+ more nodes omitted from this text preview.");
        }

        return builder.ToString();
    }

    private static void AppendProjectGraphTreeNode(
        System.Text.StringBuilder builder,
        ProjectNodeViewModel node,
        string prefix,
        bool isLast,
        ref int remainingLines,
        ref int omitted)
    {
        if (remainingLines <= 0)
        {
            omitted += CountProjectGraphTextNodesCapped(node, 100000);
            return;
        }

        var connector = node.Depth <= 0 ? "" : isLast ? "`-- " : "|-- ";
        builder.Append(prefix);
        builder.Append(connector);
        builder.Append(node.DisplayName);

        var meta = BuildProjectGraphNodeMeta(node);
        if (!string.IsNullOrWhiteSpace(meta))
        {
            builder.Append("  ");
            builder.Append(meta);
        }

        builder.AppendLine();
        remainingLines--;

        if (remainingLines <= 0)
        {
            foreach (var child in node.Children)
            {
                omitted += CountProjectGraphTextNodesCapped(child, 100000 - Math.Min(omitted, 100000));
            }

            return;
        }

        var childPrefix = node.Depth <= 0
            ? ""
            : prefix + (isLast ? "    " : "|   ");
        for (var index = 0; index < node.Children.Count; index++)
        {
            AppendProjectGraphTreeNode(builder, node.Children[index], childPrefix, index == node.Children.Count - 1, ref remainingLines, ref omitted);
            if (remainingLines <= 0)
            {
                for (var rest = index + 1; rest < node.Children.Count; rest++)
                {
                    omitted += CountProjectGraphTextNodesCapped(node.Children[rest], 100000 - Math.Min(omitted, 100000));
                }

                break;
            }
        }
    }

    private static string BuildProjectGraphNodeMeta(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            return "[skip]";
        }

        if (node.IsFolder)
        {
            return string.IsNullOrWhiteSpace(node.DirectoryStatsLabel)
                ? ""
                : $"[{node.DirectoryStatsLabel}]";
        }

        var parts = new[]
            {
                string.IsNullOrWhiteSpace(node.VersionLabel) ? "" : node.VersionLabel,
                string.IsNullOrWhiteSpace(node.LocMetricLabel) ? "" : node.LocMetricLabel
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? "" : $"[{string.Join(" ", parts)}]";
    }

    private static int CountProjectGraphTextNodesCapped(ProjectNodeViewModel node, int cap)
    {
        if (cap <= 0)
        {
            return 0;
        }

        var count = 0;
        var stack = new Stack<ProjectNodeViewModel>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            count++;
            if (count >= cap)
            {
                return cap;
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return count;
    }

    private static string BuildProjectGraphSelectedLabel(ProjectNodeViewModel node)
    {
        var kind = node.IsFolder ? "Folder" : "File";
        var path = string.IsNullOrWhiteSpace(node.Path) ? node.Name : node.Path;
        var meta = BuildProjectGraphNodeMeta(node);
        return string.IsNullOrWhiteSpace(meta)
            ? $"{kind}: {path}"
            : $"{kind}: {path} {meta}";
    }

    private bool ContainsProjectNode(ProjectNodeViewModel target)
    {
        foreach (var node in ProjectTree)
        {
            if (ContainsProjectNode(node, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsProjectNode(ProjectNodeViewModel current, ProjectNodeViewModel target)
    {
        if (ReferenceEquals(current, target))
        {
            return true;
        }

        foreach (var child in current.Children)
        {
            if (ContainsProjectNode(child, target))
            {
                return true;
            }
        }

        return false;
    }
}
