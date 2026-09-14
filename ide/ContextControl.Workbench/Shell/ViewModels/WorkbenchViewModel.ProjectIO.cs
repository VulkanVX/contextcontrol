// CC-DESC: Owns project loading, visible tree rows, document loading, and external-change queues.

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
    public void ScanExternalChangesNow()
    {
        if (CurrentProject is null)
        {
            return;
        }

        StartExternalTracker(CurrentProject);
        if (_trackersByProjectId.TryGetValue(CurrentProject.Id, out var tracker))
        {
            tracker.ForceScanNow();
        }
    }

    public async Task LoadProjectAsync(string folderPath)
    {
        var loadedProject = await ProjectLoader.LoadAsync(folderPath, showSkippedFiles: ShowSkippedFiles);
        var existing = Projects.FirstOrDefault(project =>
            string.Equals(project.ProjectRoot, loadedProject.Project.ProjectRoot, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var existingIndex = Projects.IndexOf(existing);
            Projects[existingIndex] = loadedProject.Project;
            if (_trackersByProjectId.Remove(existing.Id, out var oldTracker))
            {
                oldTracker.Dispose();
            }
        }
        else
        {
            Projects.Add(loadedProject.Project);
        }

        var loadedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new ObservableCollection<ExternalChangeItemViewModel>(),
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        if (existing is not null && _workspaceByProjectId.TryGetValue(existing.Id, out var previousWorkspace))
        {
            loadedWorkspace.CopyScannerStateFrom(previousWorkspace);
        }

        _workspaceByProjectId[loadedProject.Project.Id] = loadedWorkspace;
        SelectProject(loadedProject.Project);
    }

    private async Task IncludeExternalNodeAsync(ProjectNodeViewModel? node)
    {
        if (CurrentProject is null
            || node is not { CanIncludeExternal: true }
            || string.IsNullOrWhiteSpace(CurrentProject.ProjectRoot)
            || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        var includedPaths = workspace.IncludedExternalPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        includedPaths.Add(node.Path);

        var loadedProject = await ProjectLoader.LoadAsync(CurrentProject.ProjectRoot, includedPaths, ShowSkippedFiles);
        var existingIndex = Projects.IndexOf(CurrentProject);
        if (existingIndex >= 0)
        {
            Projects[existingIndex] = loadedProject.Project;
        }

        var refreshedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            includedPaths,
            workspace.ExternalChanges,
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        refreshedWorkspace.CopyScannerStateFrom(workspace);
        _workspaceByProjectId[loadedProject.Project.Id] = refreshedWorkspace;
        SelectProject(loadedProject.Project);
    }

    private void ToggleNode(TreeRowViewModel? row)
    {
        if (row?.Node is not { HasChildren: true } node)
        {
            return;
        }

        var nodeRowIndex = VisibleTreeRows.IndexOf(row);
        var nodeIndex = nodeRowIndex;
        if (nodeRowIndex < 0
            || nodeIndex >= VisibleProjectNodes.Count
            || !ReferenceEquals(VisibleProjectNodes[nodeIndex], node))
        {
            nodeRowIndex = FindNodeRowIndex(node);
            nodeIndex = VisibleProjectNodes.IndexOf(node);
        }

        if (nodeIndex < 0 || nodeRowIndex < 0)
        {
            RefreshVisibleProjectNodes();
            return;
        }

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
            row.RefreshExpansionState();
            CollapseVisibleNodeBranch(node, nodeIndex, nodeRowIndex);
            return;
        }

        node.IsExpanded = true;
        row.RefreshExpansionState();
        ExpandVisibleNodeBranch(node, nodeIndex, nodeRowIndex);
    }

    private void RefreshVisibleProjectNodes()
    {
        var visibleNodes = new List<ProjectNodeViewModel>();
        var visibleRows = new List<TreeRowViewModel>();

        foreach (var node in ProjectTree)
        {
            AddVisibleNode(node, visibleNodes, visibleRows);
        }

        VisibleProjectNodes.Clear();
        VisibleProjectNodes.AddRange(visibleNodes);
        VisibleTreeRows.Clear();
        VisibleTreeRows.AddRange(visibleRows);
        if (IsTopLocMode)
        {
            RefreshTopLocTreeRows();
        }
    }

    private void RefreshTopLocTreeRows()
    {
        var rows = EnumerateProjectFileNodes(ProjectTree)
            .Where(node => !node.IsExternal && node.Loc > 0)
            .OrderByDescending(node => node.Loc)
            .ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
            .Select((node, index) => TreeRowViewModel.ForTopLoc(node, index + 1))
            .ToArray();

        TopLocTreeRows.Clear();
        TopLocTreeRows.AddRange(rows);
    }

    private static IEnumerable<ProjectNodeViewModel> EnumerateProjectFileNodes(IEnumerable<ProjectNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                yield return node;
            }

            foreach (var child in EnumerateProjectFileNodes(node.Children))
            {
                yield return child;
            }
        }
    }

    private void ExpandVisibleNodeBranch(ProjectNodeViewModel node, int nodeIndex, int nodeRowIndex)
    {
        var expandedNodes = new List<ProjectNodeViewModel>();
        var expandedRows = new List<TreeRowViewModel>();
        CollectExpandedBranchRows(node, expandedNodes, expandedRows);
        if (expandedRows.Count == 0)
        {
            return;
        }

        VisibleProjectNodes.InsertRange(nodeIndex + 1, expandedNodes);
        VisibleTreeRows.InsertRange(nodeRowIndex + 1, expandedRows);
    }

    private void CollapseVisibleNodeBranch(ProjectNodeViewModel node, int nodeIndex, int nodeRowIndex)
    {
        var removeNodeCount = CountVisibleDescendantNodes(nodeIndex, node.Depth);
        var removeRowCount = CountVisibleDescendantRows(nodeRowIndex, node.Depth);
        if (removeNodeCount == 0 && removeRowCount == 0)
        {
            return;
        }

        VisibleProjectNodes.RemoveRange(nodeIndex + 1, removeNodeCount);
        VisibleTreeRows.RemoveRange(nodeRowIndex + 1, removeRowCount);

        var visibleRows = VisibleTreeRows.ToHashSet();
        var selectedRows = SelectedTreeRows
            .Where(visibleRows.Contains)
            .ToArray();
        if (selectedRows.Length == SelectedTreeRows.Count)
        {
            return;
        }

        SetSelectedTreeRows(selectedRows.Length > 0
            ? selectedRows
            : [VisibleTreeRows[nodeRowIndex]]);
    }

    private int CountVisibleDescendantNodes(int nodeIndex, int parentDepth)
    {
        var removeCount = 0;
        for (var index = nodeIndex + 1; index < VisibleProjectNodes.Count; index++)
        {
            if (VisibleProjectNodes[index].Depth <= parentDepth)
            {
                break;
            }

            removeCount++;
        }

        return removeCount;
    }

    private int CountVisibleDescendantRows(int nodeRowIndex, int parentDepth)
    {
        var removeCount = 0;
        for (var index = nodeRowIndex + 1; index < VisibleTreeRows.Count; index++)
        {
            if (VisibleTreeRows[index].Depth <= parentDepth)
            {
                break;
            }

            removeCount++;
        }

        return removeCount;
    }

    private int FindNodeRowIndex(ProjectNodeViewModel node)
    {
        for (var index = 0; index < VisibleTreeRows.Count; index++)
        {
            if (ReferenceEquals(VisibleTreeRows[index].Node, node))
            {
                return index;
            }
        }

        return -1;
    }

    private static void CollectExpandedBranchRows(
        ProjectNodeViewModel parent,
        ICollection<ProjectNodeViewModel> expandedNodes,
        ICollection<TreeRowViewModel> expandedRows)
    {
        foreach (var child in parent.Children)
        {
            expandedNodes.Add(child);
            expandedRows.Add(TreeRowViewModel.ForNode(child));

            if (child.IsExpanded && child.HasChildren)
            {
                CollectExpandedBranchRows(child, expandedNodes, expandedRows);
            }
        }
    }

    private void RefreshCurrentRowHighlights(ProjectNodeViewModel? previous, ProjectNodeViewModel? current)
    {
        foreach (var row in VisibleTreeRows)
        {
            if (ReferenceEquals(row.Node, previous) || ReferenceEquals(row.Node, current))
            {
                row.RefreshCurrentState();
            }
        }
    }

    private static void AddVisibleNode(
        ProjectNodeViewModel node,
        ICollection<ProjectNodeViewModel> visibleNodes,
        ICollection<TreeRowViewModel> visibleRows)
    {
        visibleNodes.Add(node);
        visibleRows.Add(TreeRowViewModel.ForNode(node));

        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisibleNode(child, visibleNodes, visibleRows);
        }
    }

    private void PrepareTree()
    {
        for (var index = 0; index < ProjectTree.Count; index++)
        {
            PrepareNode(ProjectTree[index], 0, index == ProjectTree.Count - 1, []);
        }
    }

    private static void PrepareNode(
        ProjectNodeViewModel node,
        int depth,
        bool isLast,
        IReadOnlyList<bool> ancestorContinues)
    {
        node.SetTreeState(depth, isLast, ancestorContinues);
        if (depth <= DefaultExpandedDepth && !node.IsExternal)
        {
            node.IsExpanded = true;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var childIsLast = index == node.Children.Count - 1;
            var childAncestors = ancestorContinues.Concat([!childIsLast]).ToArray();

            PrepareNode(child, depth + 1, childIsLast, childAncestors);
        }
    }

    private void LoadWorkspace(ProjectWorkspaceState workspace)
    {
        ProjectTree.Clear();
        foreach (var node in workspace.ProjectTree)
        {
            ProjectTree.Add(node);
        }

        _historyByPath = workspace.HistoryByPath;
        ExternalChanges.Clear();
        foreach (var change in workspace.ExternalChanges)
        {
            RegisterExternalChangeItem(change);
            ExternalChanges.Add(change);
        }

        SelectedNode = null;
        SelectedTreeRow = null;
        SelectedHistory = null;
        ClearActiveVersion();
        Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Empty();
        ApplyFileRulesToEditor(workspace.FileRules, "");
        ApplyProjectScanResult(workspace.ProjectScanResult, workspace.ProjectScanAutoSetupStatus);
        CloseHistory();
        if (!workspace.TreeStatePrepared)
        {
            PrepareTree();
            workspace.TreeStatePrepared = true;
        }

        RefreshVisibleProjectNodes();
        RefreshProjectGraph();
        RefreshExternalChangeLabels();
        RecalculateExternalChangeFlows(workspace.ExternalChanges);
    }

    private void OpenDocument(ProjectNodeViewModel node, bool switchToEditor = true)
    {
        var path = ResolveNodePath(node);
        if (path is null)
        {
            Interlocked.Increment(ref _documentLoadVersion);
            ActiveDocument = EditorDocumentViewModel.Empty();
            if (switchToEditor)
            {
                SelectedWorkspaceMode = WorkspaceModes[0];
            }
            return;
        }

        OpenDocumentAsync(path, node.Path, node.VersionLabel, node.Loc);
        if (switchToEditor)
        {
            SelectedWorkspaceMode = WorkspaceModes[0];
        }
    }

    private void SelectFileNode(ProjectNodeViewModel node)
    {
        ClearActiveVersion();
        SelectHistory(node.Path);
        OpenDocument(node);
    }

    private void SelectHistory(string path, bool loadStats = false)
    {
        SelectedHistory = _historyByPath.TryGetValue(path, out var history)
            ? history
            : new FileHistoryViewModel(
                Path.GetFileName(path),
                path,
                [new VersionEntryViewModel(
                    "v1",
                    "2026-05-10",
                    CurrentProject?.Commit ?? "local",
                    "tracked by Context Control",
                    Path.GetFileName(path),
                    path,
                    currentFilePath: SelectedNode is { } selectedNode
                        ? ResolveNodePath(selectedNode) ?? ""
                        : "")]);
        if (loadStats || IsHistoryOpen)
        {
            SelectedHistory.EnsureStatsLoaded();
        }
    }

    private void OpenVersion(VersionEntryViewModel? version)
    {
        if (version is null)
        {
            return;
        }

        ClearActiveVersion();
        SelectedHistory?.EnsureStatsLoaded();
        version.IsActive = true;
        _selectedVersion = version;
        OpenVersionAsync(version);
        IsHistoryOpen = true;
        HistoryWidth = 330;
        HistoryOpacity = 1;
        HistoryGutter = 8;
    }

    private void OpenDocumentAsync(string absolutePath, string displayPath, string version = "", long loc = 0)
    {
        var loadVersion = Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Loading(displayPath);
        _ = LoadDocumentCoreAsync(loadVersion, absolutePath, displayPath, version, loc);
    }

    private async Task LoadDocumentCoreAsync(int loadVersion, string absolutePath, string displayPath, string version, long loc)
    {
        var document = await Task.Run(() => EditorDocumentViewModel.Load(absolutePath, displayPath, version, loc));
        PostDocument(loadVersion, document);
    }

    private void OpenVersionAsync(VersionEntryViewModel version)
    {
        var loadVersion = Interlocked.Increment(ref _documentLoadVersion);
        ActiveDocument = EditorDocumentViewModel.Loading(version.FilePath);
        _ = LoadVersionCoreAsync(loadVersion, version);
    }

    private async Task LoadVersionCoreAsync(int loadVersion, VersionEntryViewModel version)
    {
        var document = await Task.Run(() => EditorDocumentViewModel.LoadVersion(version));
        PostDocument(loadVersion, document);
    }

    private void PostDocument(int loadVersion, EditorDocumentViewModel document)
    {
        PostToUi(() =>
        {
            if (loadVersion == Volatile.Read(ref _documentLoadVersion))
            {
                ActiveDocument = document;
            }
        });
    }

    private void ClearActiveVersion()
    {
        if (_selectedVersion is not null)
        {
            _selectedVersion.IsActive = false;
            _selectedVersion = null;
        }
    }

    private string? ResolveNodePath(ProjectNodeViewModel node)
    {
        if (string.IsNullOrWhiteSpace(node.Path))
        {
            return null;
        }

        if (Path.IsPathRooted(node.Path))
        {
            return node.Path;
        }

        return string.IsNullOrWhiteSpace(CurrentProject?.ProjectRoot)
            ? null
            : Path.Combine(CurrentProject.ProjectRoot, node.Path);
    }

    private string GetAttachmentDisplayPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(CurrentProject?.ProjectRoot))
        {
            return fullPath;
        }

        var projectRoot = CurrentProject.ProjectRoot;
        if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(projectRoot, fullPath)
                .Replace('\\', '/');
            relative = NormalizeProjectPath(relative);
            return string.IsNullOrWhiteSpace(relative) ? fullPath : relative;
        }

        return fullPath;
    }

    private bool TryResolveAttachment(string? path, out string fullPath, out string displayPath)
    {
        fullPath = "";
        displayPath = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        displayPath = GetAttachmentDisplayPath(fullPath);
        return true;
    }

    private void StartExternalTracker(ProjectTabViewModel project)
    {
        if (_trackersByProjectId.ContainsKey(project.Id)
            || string.IsNullOrWhiteSpace(project.ProjectRoot)
            || !Directory.Exists(project.ProjectRoot))
        {
            return;
        }

        var tracker = new ExternalChangeTracker(project.ProjectRoot);
        tracker.ChangeCaptured += (_, change) => PostToUi(() => OnExternalChangeCaptured(project.Id, change));
        _trackersByProjectId[project.Id] = tracker;

        if (_workspaceByProjectId.TryGetValue(project.Id, out var workspace))
        {
            workspace.FileRules = tracker.FileRules;
            if (ReferenceEquals(CurrentProject, project))
            {
                ApplyFileRulesToEditor(tracker.FileRules, FileRulesStatus);
            }
        }
    }

    private void QueueExternalTrackerStart(ProjectTabViewModel project, int switchVersion)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            PostToUi(() =>
            {
                if (switchVersion != Volatile.Read(ref _projectSwitchVersion)
                    || CurrentProject?.Id != project.Id)
                {
                    return;
                }

                StartExternalTracker(project);
            });
        });
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private void OnExternalChangeCaptured(string projectId, ExternalFileChange change)
    {
        if (!_workspaceByProjectId.TryGetValue(projectId, out var workspace))
        {
            return;
        }

        if (workspace.ExternalChanges.Any(item => item.QueueId.Equals($"{change.RelativePath}|{change.VersionAfter}", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var item = new ExternalChangeItemViewModel(change);
        RegisterExternalChangeItem(item);
        workspace.ExternalChanges.Add(item);

        if (CurrentProject?.Id == projectId)
        {
            ExternalChanges.Add(item);
            RefreshExternalChangeLabels();
            ReloadActiveDocumentIfChanged(change);
        }

        RecalculateExternalChangeFlows(workspace.ExternalChanges);
    }


    private void ReloadActiveDocumentIfChanged(ExternalFileChange change)
    {
        if (SelectedNode is not { IsFile: true })
        {
            return;
        }

        if (!string.Equals(NormalizeProjectPath(SelectedNode.Path), NormalizeProjectPath(change.RelativePath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ClearActiveVersion();
        SelectHistory(SelectedNode.Path);
        OpenDocument(SelectedNode);
    }

    private static string NormalizeProjectPath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private void RegisterExternalChangeItem(ExternalChangeItemViewModel item)
    {
        item.SelectionChanged -= OnExternalChangeSelectionChanged;
        item.SelectionChanged += OnExternalChangeSelectionChanged;
    }

    private void OnExternalChangeSelectionChanged(object? sender, EventArgs e)
    {
        if (CurrentProject is not null && _workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            RecalculateExternalChangeFlows(workspace.ExternalChanges);
        }
    }

    private void ToggleExternalChange(ExternalChangeItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        item.IsSelected = !item.IsSelected;
    }

    private void AcceptAllExternalChanges()
    {
        foreach (var item in ExternalChanges)
        {
            item.IsSelected = true;
        }

        AcceptSelectedExternalChanges();
    }

    private void AcceptFinalExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        var queued = workspace.ExternalChanges.ToArray();
        if (queued.Length == 0)
        {
            return;
        }

        ExternalVersionQueueStore.AcceptOnlyFinal(CurrentProject.ProjectRoot, queued.Select(item => item.Change));
        RemoveExternalChanges(workspace, item => queued.Contains(item));
        _ = RefreshCurrentProjectFromDiskAsync();
    }

    private void AcceptSelectedExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        RemoveExternalChanges(workspace, item => item.IsSelected);
        _ = RefreshCurrentProjectFromDiskAsync();
    }

    private void DismissSelectedExternalChanges()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var workspace))
        {
            return;
        }

        RemoveExternalChanges(workspace, item => item.IsSelected);
    }

    private void RemoveExternalChanges(ProjectWorkspaceState workspace, Func<ExternalChangeItemViewModel, bool> predicate)
    {
        var toRemove = workspace.ExternalChanges.Where(predicate).ToArray();
        foreach (var item in toRemove)
        {
            item.SelectionChanged -= OnExternalChangeSelectionChanged;
            workspace.ExternalChanges.Remove(item);
            ExternalChanges.Remove(item);
        }

        RecalculateExternalChangeFlows(workspace.ExternalChanges);
        RefreshExternalChangeLabels();
    }

    private async Task RefreshCurrentProjectFromDiskAsync()
    {
        if (CurrentProject is null || !_workspaceByProjectId.TryGetValue(CurrentProject.Id, out var currentWorkspace))
        {
            return;
        }

        var projectRoot = CurrentProject.ProjectRoot;
        var projectId = CurrentProject.Id;
        var loadedProject = await ProjectLoader.LoadAsync(projectRoot, currentWorkspace.IncludedExternalPaths, ShowSkippedFiles);
        var existingIndex = Projects.IndexOf(CurrentProject);
        if (existingIndex >= 0)
        {
            Projects[existingIndex] = loadedProject.Project;
        }

        var refreshedWorkspace = new ProjectWorkspaceState(
            loadedProject.Tree,
            loadedProject.HistoryByPath,
            currentWorkspace.IncludedExternalPaths,
            currentWorkspace.ExternalChanges,
            loadedProject.FileRules,
            loadedProject.IsTreePrepared);
        refreshedWorkspace.CopyScannerStateFrom(currentWorkspace);
        _workspaceByProjectId[loadedProject.Project.Id] = refreshedWorkspace;

        if (loadedProject.Project.Id != projectId && _trackersByProjectId.Remove(projectId, out var tracker))
        {
            _trackersByProjectId[loadedProject.Project.Id] = tracker;
        }

        SelectProject(loadedProject.Project);
    }

    private void RecalculateExternalChangeFlows(IEnumerable<ExternalChangeItemViewModel> changes)
    {
        foreach (var group in changes.GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(item => item.Change.VersionAfter).ToArray();
            var baseVersion = ordered.FirstOrDefault()?.Change.VersionBefore ?? 0;
            var baseSnapshot = ordered.FirstOrDefault()?.Change.PreviousSnapshotPath ?? "";

            foreach (var item in ordered)
            {
                item.SetEffectiveBase(baseVersion, baseSnapshot);
                if (item.IsSelected)
                {
                    baseVersion = item.Change.VersionAfter;
                    baseSnapshot = item.Change.SnapshotPath;
                }
            }
        }
    }

    private void RefreshExternalChangeLabels()
    {
        OnPropertyChanged(nameof(HasExternalChanges));
        OnPropertyChanged(nameof(ExternalQueueTitle));
    }

    private static ProjectNodeViewModel? FindProjectNodeByPath(IEnumerable<ProjectNodeViewModel> nodes, string path)
    {
        var normalized = NormalizeProjectPath(path);
        foreach (var node in nodes)
        {
            if (node.IsFile && string.Equals(NormalizeProjectPath(node.Path), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            var child = FindProjectNodeByPath(node.Children, normalized);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static ObservableCollection<ProjectNodeViewModel> CloneTree(IEnumerable<ProjectNodeViewModel> nodes)
    {
        return new ObservableCollection<ProjectNodeViewModel>(nodes.Select(CloneNode));
    }

    private static ProjectNodeViewModel CloneNode(ProjectNodeViewModel node)
    {
        var clone = new ProjectNodeViewModel(
            node.Name,
            node.Path,
            node.IsFolder,
            node.VersionLabel,
            node.Children.Select(CloneNode),
            node.IsExternal,
            node.CanIncludeExternal,
            node.Loc,
            node.FileCount,
            node.DiskFileCount,
            node.DirectDiskFileCount);
        clone.IsExpanded = node.IsExpanded;
        clone.SetTreeState(node.Depth, node.IsLast, node.AncestorContinues);
        return clone;
    }

    private static readonly JsonSerializerOptions ProjectSettingsJsonOptions = new()
    {
        WriteIndented = true
    };

}
