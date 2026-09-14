// CC-DESC: Carries a loaded project tree, history, and project file-rule state.

using System.Collections.ObjectModel;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Services;

public sealed class LoadedProject(
    ProjectTabViewModel project,
    ObservableCollection<ProjectNodeViewModel> tree,
    Dictionary<string, FileHistoryViewModel> historyByPath,
    ProjectFileRules fileRules,
    bool isTreePrepared = false)
{
    public ProjectTabViewModel Project { get; } = project;
    public ObservableCollection<ProjectNodeViewModel> Tree { get; } = tree;
    public Dictionary<string, FileHistoryViewModel> HistoryByPath { get; } = historyByPath;
    public ProjectFileRules FileRules { get; } = fileRules;
    public bool IsTreePrepared { get; } = isTreePrepared;
}
