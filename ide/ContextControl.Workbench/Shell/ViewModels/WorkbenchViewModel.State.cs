// CC-DESC: Contains private state records for per-project workspace and graph search entries.

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
    private sealed class ProjectWorkspaceState(
        ObservableCollection<ProjectNodeViewModel> projectTree,
        Dictionary<string, FileHistoryViewModel> historyByPath,
        IReadOnlySet<string> includedExternalPaths,
        ObservableCollection<ExternalChangeItemViewModel> externalChanges,
        ProjectFileRules fileRules,
        bool treeStatePrepared = false)
    {
        public ObservableCollection<ProjectNodeViewModel> ProjectTree { get; } = projectTree;
        public Dictionary<string, FileHistoryViewModel> HistoryByPath { get; } = historyByPath;
        public IReadOnlySet<string> IncludedExternalPaths { get; } = includedExternalPaths;
        public ObservableCollection<ExternalChangeItemViewModel> ExternalChanges { get; } = externalChanges;
        public ProjectFileRules FileRules { get; set; } = fileRules;
        public bool TreeStatePrepared { get; set; } = treeStatePrepared;
        public ProjectStackScanResult? ProjectScanResult { get; set; }
        public string ProjectScanAutoSetupStatus { get; set; } = "";

        public void CopyScannerStateFrom(ProjectWorkspaceState other)
        {
            ProjectScanResult = other.ProjectScanResult;
            ProjectScanAutoSetupStatus = other.ProjectScanAutoSetupStatus;
        }
    }

    private readonly record struct ProjectGraphSearchEntry(
        ProjectNodeViewModel Node,
        string Title,
        string Detail,
        string Meta,
        int Depth,
        int Order);
}
