// CC-DESC: Owns project selection, history, context inclusion, and tree rule mutations.

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
    private void SelectProject(ProjectTabViewModel? project)
    {
        if (project is null)
        {
            return;
        }

        if (ReferenceEquals(CurrentProject, project) && project.IsActive)
        {
            return;
        }

        var switchVersion = Interlocked.Increment(ref _projectSwitchVersion);

        foreach (var item in Projects)
        {
            item.IsActive = ReferenceEquals(item, project);
        }

        CurrentProject = project;
        OnPropertyChanged(nameof(ProjectRulesActiveProjectRoot));
        if (_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            ContextControl.SetActiveProject(project.ProjectRoot, workspace.FileRules.RulesPath);
            // Let the active project tile render first; loading workspace content can be expensive.
            PostToUi(() =>
            {
                if (switchVersion != Volatile.Read(ref _projectSwitchVersion)
                    || CurrentProject?.Id != project.Id)
                {
                    return;
                }

                LoadWorkspace(workspace);
                if (workspace.ProjectScanResult is null)
                {
                    QueueProjectScanForProject(project, switchVersion);
                }
            });
        }
        else
        {
            ContextControl.SetActiveProject(project.ProjectRoot, "");
        }

        QueueExternalTrackerStart(project, switchVersion);
    }

    private void QueueProjectScanForProject(ProjectTabViewModel project, int switchVersion)
    {
        var projectId = project.Id;
        PostToUi(() =>
        {
            _ = ScanProjectRulesWhenIdleAsync(projectId, switchVersion);
        });
    }

    private async Task ScanProjectRulesWhenIdleAsync(string projectId, int switchVersion)
    {
        while (IsProjectScanRunning)
        {
            await Task.Delay(100);
        }

        if (switchVersion != Volatile.Read(ref _projectSwitchVersion)
            || CurrentProject?.Id != projectId)
        {
            return;
        }

        await ScanProjectRulesAsync();
    }

    private void OpenHistory()
    {
        if (SelectedNode is { IsFile: true })
        {
            OpenHistory(SelectedNode.Path);
            return;
        }

        OpenHistory("ide/ContextControl.Workbench/Views/MainWindow.axaml");
    }

    public void OpenHistory(string path)
    {
        SelectHistory(path, loadStats: true);
        ShowHistory();
    }

    public void SelectTreeRow(TreeRowViewModel row, bool toggleHistory)
    {
        if (row.Node is not { } node)
        {
            return;
        }

        SetProperty(ref _selectedTreeRow, row, nameof(SelectedTreeRow));
        SelectedNode = node;

        if (toggleHistory)
        {
            ToggleHistoryForNode(node);
        }
    }

    public void SetSelectedTreeRows(IEnumerable<TreeRowViewModel> rows)
    {
        var selectedRows = rows
            .Where(row => row.Node is not null)
            .Distinct()
            .ToArray();

        ReplaceSelectedTreeRows(selectedRows);
        var primaryRow = selectedRows.LastOrDefault();
        if (primaryRow is not null)
        {
            SelectTreeRow(primaryRow, false);
            return;
        }

        SetProperty(ref _selectedTreeRow, null, nameof(SelectedTreeRow));
        SelectedNode = null;
    }

    public async Task CopyTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        var selectedNodes = GetContextNodes(rows);
        var requestLines = selectedNodes
            .SelectMany(EnumerateContextFilePaths)
            .Select(NormalizeProjectPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestLines.Length == 0)
        {
            FileRulesStatus = "No shown files are available for the selected context.";
            return;
        }

        var label = selectedNodes.Count == 1
            ? selectedNodes[0].IsFolder ? "folder" : "file"
            : "selection";
        await ContextControl.CopyCodeContextAsync(requestLines, label);
    }

    public string BuildTopLocSelectionCopyText()
    {
        return string.Join(
            Environment.NewLine,
            SelectedTreeRows
                .Where(row => row.IsTopLocRow && row.Node is not null)
                .OrderBy(row => row.TopLocRank)
                .Select(row =>
                {
                    var node = row.Node!;
                    return $"{row.TopLocRank:N0}. {node.Name} {node.Loc:N0} LOC";
                }));
    }

    public Task SkipTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        return UpdateTreeContextRulesAsync(rows, skip: true);
    }

    public Task ShowTreeContextAsync(IEnumerable<TreeRowViewModel> rows)
    {
        return UpdateTreeContextRulesAsync(rows, skip: false);
    }

    public void ReportProjectTreeActionError(string message)
    {
        FileRulesStatus = string.IsNullOrWhiteSpace(message)
            ? "Project tree action failed."
            : $"Project tree action failed: {message}";
    }

    public bool CanToggleTreeFileExtension(ProjectNodeViewModel node)
    {
        return node.IsFile && !string.IsNullOrWhiteSpace(GetTreeFileExtension(node));
    }

    public string GetTreeFileExtensionRuleLabel(ProjectNodeViewModel node)
    {
        var extension = GetTreeFileExtension(node);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File type";
        }

        return IsTreeFileExtensionSkipped(node)
            ? $"Show {extension} file types"
            : $"Hide {extension} file types";
    }

    public bool IsTreeFileExtensionSkipped(ProjectNodeViewModel node)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return false;
        }

        var extension = GetTreeFileExtension(node);
        return !string.IsNullOrWhiteSpace(extension) && workspace.FileRules.ShouldSkipExtension(extension);
    }

    public Task ToggleTreeFileExtensionAsync(ProjectNodeViewModel node)
    {
        if (!CanToggleTreeFileExtension(node))
        {
            FileRulesStatus = "This file has no extension to show or hide.";
            return Task.CompletedTask;
        }

        return UpdateTreeFileExtensionRuleAsync(GetTreeFileExtension(node), !IsTreeFileExtensionSkipped(node));
    }

    public string GetTreeFileLocRuleLabel(ProjectNodeViewModel node)
    {
        var extension = GetTreeFileExtension(node);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "File LOC";
        }

        return IsTreeFileLocExtensionShown(node)
            ? $"Hide LOC for {extension} file types"
            : $"Show LOC for {extension} file types";
    }

    public bool IsTreeFileLocExtensionShown(ProjectNodeViewModel node)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return false;
        }

        var extension = GetTreeFileExtension(node);
        return !string.IsNullOrWhiteSpace(extension) && workspace.FileRules.ShouldCountLocExtension(extension);
    }

    public Task ToggleTreeFileLocExtensionAsync(ProjectNodeViewModel node)
    {
        if (!CanToggleTreeFileExtension(node))
        {
            FileRulesStatus = "This file has no extension for LOC settings.";
            return Task.CompletedTask;
        }

        return UpdateTreeFileLocExtensionRuleAsync(GetTreeFileExtension(node), !IsTreeFileLocExtensionShown(node));
    }

    public void OpenVersionFromHistory(VersionEntryViewModel version)
    {
        OpenVersion(version);
    }

    public void OpenExternalChange(ExternalChangeItemViewModel? item)
    {
        if (item is null || CurrentProject is null)
        {
            return;
        }

        var node = FindProjectNodeByPath(ProjectTree, item.RelativePath);
        if (node is not null)
        {
            SelectedNode = node;
            SelectHistory(node.Path, loadStats: true);
            OpenDocument(node);
            ShowHistory();
            return;
        }

        SelectHistory(item.RelativePath, loadStats: true);
        ShowHistory();

        var fullPath = Path.Combine(CurrentProject.ProjectRoot, item.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath))
        {
            ClearActiveVersion();
            OpenDocumentAsync(fullPath, item.RelativePath, $"v{item.Change.VersionAfter}", item.Change.Loc);
        }
    }

    private void OpenContextControlPatchTarget(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || CurrentProject is null)
        {
            return;
        }

        var cleanPath = NormalizeProjectPath(relativePath);
        if (string.IsNullOrWhiteSpace(cleanPath))
        {
            return;
        }

        IsProjectFilesPaneOpen = true;
        var node = FindProjectNodeByPath(ProjectTree, cleanPath);
        if (node is not null)
        {
            FocusProjectTreeNode(node);
            SelectedNode = node;
            SelectedWorkspaceMode = WorkspaceModes[0];
            return;
        }

        var fullPath = Path.Combine(CurrentProject.ProjectRoot, cleanPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return;
        }

        ClearActiveVersion();
        SelectHistory(cleanPath);
        OpenDocumentAsync(fullPath, cleanPath, "live");
        SelectedWorkspaceMode = WorkspaceModes[0];
    }

    public void OpenAttachment(string? path)
    {
        if (!TryResolveAttachment(path, out var fullPath, out var displayPath))
        {
            return;
        }

        ClearActiveVersion();
        OpenDocumentAsync(fullPath, displayPath, "live");
        SelectedWorkspaceMode = WorkspaceModes[0];
        if (IsHistoryOpen)
        {
            CloseHistory();
        }
    }

    public void ToggleHistoryForNode(ProjectNodeViewModel node)
    {
        if (!node.IsFile)
        {
            return;
        }

        var nodePath = NormalizeProjectPath(node.Path);
        var selectedHistoryPath = NormalizeProjectPath(SelectedHistory?.Path ?? "");

        if (IsHistoryOpen && string.Equals(selectedHistoryPath, nodePath, StringComparison.OrdinalIgnoreCase))
        {
            CloseHistory();
            return;
        }

        SelectHistory(node.Path, loadStats: true);
        ShowHistory();
    }

    private void ShowHistory()
    {
        IsHistoryOpen = true;
        HistoryWidth = 330;
        HistoryOpacity = 1;
        HistoryGutter = 8;
    }

    public void CloseHistory()
    {
        IsHistoryOpen = false;
        HistoryWidth = 0;
        HistoryOpacity = 0;
        HistoryGutter = 0;
    }

    private void ReplaceSelectedTreeRows(IReadOnlyList<TreeRowViewModel> rows)
    {
        var selected = rows.ToHashSet();
        foreach (var oldRow in SelectedTreeRows)
        {
            if (!selected.Contains(oldRow))
            {
                oldRow.IsSelected = false;
            }
        }

        SelectedTreeRows.Clear();
        SelectedTreeRows.AddRange(rows);
        foreach (var row in rows)
        {
            row.IsSelected = true;
        }
    }

    private IReadOnlyList<ProjectNodeViewModel> GetContextNodes(IEnumerable<TreeRowViewModel> rows)
    {
        return rows
            .Select(row => row.Node)
            .Where(node => node is not null)
            .Cast<ProjectNodeViewModel>()
            .Distinct()
            .ToArray();
    }

    private static IEnumerable<string> EnumerateContextFilePaths(ProjectNodeViewModel node)
    {
        if (node.IsExternal)
        {
            yield break;
        }

        if (node.IsFile)
        {
            if (!string.IsNullOrWhiteSpace(node.Path))
            {
                yield return node.Path;
            }

            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var path in EnumerateContextFilePaths(child))
            {
                yield return path;
            }
        }
    }

    private Task UpdateTreeContextRulesAsync(IEnumerable<TreeRowViewModel> rows, bool skip)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing file rules.";
            return Task.CompletedTask;
        }

        var nodes = GetContextNodes(rows)
            .Where(node => !string.IsNullOrWhiteSpace(node.Path))
            .ToArray();
        if (nodes.Length == 0)
        {
            FileRulesStatus = "Choose a file or folder before changing file rules.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = false;
        foreach (var node in nodes)
        {
            if (!skip && !node.IsExternal)
            {
                continue;
            }

            changed |= node.IsFolder
                ? skip ? rules.SkipDirectory(node.Path) : rules.ShowDirectory(node.Path)
                : skip ? rules.SkipFile(node.Path) : rules.ShowFile(node.Path);
        }

        if (!changed)
        {
            FileRulesStatus = skip
                ? "Selected paths were already skipped."
                : "Selected paths were already shown.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = skip
            ? $"Skipped {nodes.Length} selected path(s)."
            : $"Shown {nodes.Length} selected path(s).";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);
        ApplyTreePathRuleMutation(workspace, rules, nodes, skip);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private Task UpdateTreeFileExtensionRuleAsync(string extension, bool skip)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing file rules.";
            return Task.CompletedTask;
        }

        var cleanExtension = NormalizeFileExtension(extension);
        if (string.IsNullOrWhiteSpace(cleanExtension))
        {
            FileRulesStatus = "This file has no extension to show or hide.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = skip
            ? rules.SkipExtension(cleanExtension)
            : rules.ShowExtension(cleanExtension);

        if (!changed)
        {
            FileRulesStatus = skip
                ? $"{cleanExtension} file types were already skipped."
                : $"{cleanExtension} file types were already shown.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = skip
            ? $"Skipped {cleanExtension} file types."
            : $"Shown {cleanExtension} file types.";
        ApplyFileRulesToEditor(rules, status);
        ReloadTrackerRules(CurrentProject);
        ApplyTreeExtensionRuleMutation(rules, cleanExtension, skip);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private Task UpdateTreeFileLocExtensionRuleAsync(string extension, bool showLoc)
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            FileRulesStatus = "Open a project before changing LOC rules.";
            return Task.CompletedTask;
        }

        var cleanExtension = NormalizeFileExtension(extension);
        if (string.IsNullOrWhiteSpace(cleanExtension))
        {
            FileRulesStatus = "This file has no extension for LOC settings.";
            return Task.CompletedTask;
        }

        var rules = workspace.FileRules;
        var changed = showLoc
            ? rules.ShowLocExtension(cleanExtension)
            : rules.HideLocExtension(cleanExtension);

        if (!changed)
        {
            FileRulesStatus = showLoc
                ? $"LOC was already shown for {cleanExtension} file types."
                : $"LOC was already hidden for {cleanExtension} file types.";
            return Task.CompletedTask;
        }

        rules.Save();
        var status = showLoc
            ? $"Showing LOC for {cleanExtension} file types."
            : $"Hiding LOC for {cleanExtension} file types.";
        ApplyFileRulesToEditor(rules, status);
        ApplyTreeLocExtensionRuleMutation(cleanExtension, showLoc);

        FileRulesStatus = status;
        return Task.CompletedTask;
    }

    private static string GetTreeFileExtension(ProjectNodeViewModel node)
    {
        var extension = Path.GetExtension(string.IsNullOrWhiteSpace(node.Name) ? node.Path : node.Name);
        return NormalizeFileExtension(extension);
    }

    private static string NormalizeFileExtension(string extension)
    {
        var clean = string.IsNullOrWhiteSpace(extension) ? "" : extension.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "";
        }

        return clean.StartsWith(".", StringComparison.Ordinal) ? clean : "." + clean;
    }

    private void ApplyTreePathRuleMutation(
        ProjectWorkspaceState workspace,
        ProjectFileRules rules,
        IReadOnlyList<ProjectNodeViewModel> nodes,
        bool skip)
    {
        var fallback = SelectedNode is null ? null : FindParentNode(SelectedNode);

        if (skip && !ShowSkippedFiles)
        {
            foreach (var node in nodes.OrderByDescending(node => node.Depth))
            {
                RemoveProjectNode(workspace, node);
            }

            FinishProjectTreeStructureMutation(fallback);
            return;
        }

        foreach (var node in nodes)
        {
            if (skip)
            {
                SetSubtreeExternal(node, true);
                continue;
            }

            if (FindParentNode(node)?.IsExternal == true)
            {
                continue;
            }

            ApplyNodeVisibilityFromRules(node, rules);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyTreeExtensionRuleMutation(ProjectFileRules rules, string extension, bool skip)
    {
        var fallback = SelectedNode is null ? null : FindParentNode(SelectedNode);
        var nodes = EnumerateProjectNodesWithParents()
            .Where(item => item.Node.IsFile && string.Equals(GetTreeFileExtension(item.Node), extension, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Node.Depth)
            .ToArray();

        if (skip && !ShowSkippedFiles)
        {
            if (CurrentProject is not null && _workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
            {
                foreach (var item in nodes)
                {
                    RemoveProjectNode(workspace, item.Node);
                }
            }

            FinishProjectTreeStructureMutation(fallback);
            return;
        }

        foreach (var item in nodes)
        {
            if (!skip && item.Parent?.IsExternal == true)
            {
                continue;
            }

            ApplyNodeVisibilityFromRules(item.Node, rules);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyTreeLocExtensionRuleMutation(string extension, bool showLoc)
    {
        foreach (var item in EnumerateProjectNodesWithParents())
        {
            var node = item.Node;
            if (node.IsExternal
                || !node.IsFile
                || !string.Equals(GetTreeFileExtension(node), extension, StringComparison.OrdinalIgnoreCase)
                || item.Parent?.IsExternal == true)
            {
                continue;
            }

            node.UpdateVersionAndLoc(node.VersionLabel, showLoc ? CountLocForNode(node) : 0);
        }

        RecalculateProjectTreeMetrics();
    }

    private void ApplyNodeVisibilityFromRules(ProjectNodeViewModel node, ProjectFileRules rules)
    {
        if (node.IsFolder)
        {
            var shouldSkip = !string.IsNullOrWhiteSpace(node.Path) && rules.ShouldSkipDirectory(node.Name, node.Path);
            node.SetExternalState(shouldSkip, shouldSkip && node.CanIncludeExternal);
            if (shouldSkip)
            {
                foreach (var child in node.Children)
                {
                    SetSubtreeExternal(child, true);
                }

                return;
            }

            foreach (var child in node.Children)
            {
                ApplyNodeVisibilityFromRules(child, rules);
            }

            return;
        }

        var extension = GetTreeFileExtension(node);
        var shouldShow = rules.ShouldShowFile(node.Path, node.Name, extension);
        node.SetExternalState(!shouldShow);
        node.UpdateVersionAndLoc(
            node.VersionLabel,
            shouldShow && rules.ShouldCountLocExtension(extension) ? CountLocForNode(node) : 0);
    }

    private static void SetSubtreeExternal(ProjectNodeViewModel node, bool isExternal)
    {
        node.SetExternalState(isExternal);
        if (isExternal)
        {
            node.UpdateVersionAndLoc(node.VersionLabel, 0);
        }

        foreach (var child in node.Children)
        {
            SetSubtreeExternal(child, isExternal);
        }
    }

    private long CountLocForNode(ProjectNodeViewModel node)
    {
        var path = ResolveNodePath(node);
        return path is not null && File.Exists(path)
            ? ProjectLoader.EstimateLoc(new FileInfo(path))
            : 0;
    }

    private void FinishProjectTreeStructureMutation(ProjectNodeViewModel? fallbackNode)
    {
        var selectedNodes = SelectedTreeRows
            .Select(row => row.Node)
            .Where(node => node is not null)
            .Cast<ProjectNodeViewModel>()
            .ToHashSet();

        RecalculateProjectTreeMetrics();
        PrepareTree();
        RefreshVisibleProjectNodes();

        var selectedRows = VisibleTreeRows
            .Where(row => row.Node is not null && selectedNodes.Contains(row.Node))
            .ToArray();

        if (selectedRows.Length == 0 && fallbackNode is not null)
        {
            selectedRows = VisibleTreeRows
                .Where(row => ReferenceEquals(row.Node, fallbackNode))
                .ToArray();
        }

        SetSelectedTreeRows(selectedRows);
    }

    private void RecalculateProjectTreeMetrics()
    {
        foreach (var node in ProjectTree)
        {
            node.RecalculateDirectoryLoc();
        }

        foreach (var row in VisibleTreeRows)
        {
            row.RefreshNodeMetrics();
            row.RefreshExpansionState();
            row.RefreshCurrentState();
        }

        if (IsTopLocMode)
        {
            RefreshTopLocTreeRows();
        }

        RefreshProjectGraph();
    }

    private IEnumerable<(ProjectNodeViewModel Node, ProjectNodeViewModel? Parent)> EnumerateProjectNodesWithParents()
    {
        foreach (var node in ProjectTree)
        {
            foreach (var item in EnumerateProjectNodesWithParents(node, null))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<(ProjectNodeViewModel Node, ProjectNodeViewModel? Parent)> EnumerateProjectNodesWithParents(
        ProjectNodeViewModel node,
        ProjectNodeViewModel? parent)
    {
        yield return (node, parent);

        foreach (var child in node.Children)
        {
            foreach (var item in EnumerateProjectNodesWithParents(child, node))
            {
                yield return item;
            }
        }
    }

    private ProjectNodeViewModel? FindParentNode(ProjectNodeViewModel target)
    {
        foreach (var node in ProjectTree)
        {
            var parent = FindParentNode(node, target);
            if (parent is not null)
            {
                return parent;
            }
        }

        return null;
    }

    private static ProjectNodeViewModel? FindParentNode(ProjectNodeViewModel parent, ProjectNodeViewModel target)
    {
        foreach (var child in parent.Children)
        {
            if (ReferenceEquals(child, target))
            {
                return parent;
            }

            var nested = FindParentNode(child, target);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void RemoveProjectNode(ProjectWorkspaceState workspace, ProjectNodeViewModel node)
    {
        RemoveProjectNode(ProjectTree, node);
        RemoveProjectNode(workspace.ProjectTree, node);
    }

    private static bool RemoveProjectNode(IList<ProjectNodeViewModel> nodes, ProjectNodeViewModel target)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (ReferenceEquals(node, target))
            {
                nodes.RemoveAt(index);
                return true;
            }

            if (RemoveProjectNode(node.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    private async Task ToggleSkippedFilesAsync()
    {
        ShowSkippedFiles = !ShowSkippedFiles;
        SaveAppearanceSettings();
        await RefreshCurrentProjectFromDiskAsync();
    }
}
