// CC-DESC: Current project, selection, document, and external-queue bindable properties.

// CC-DESC: Coordinates project tabs, tree selection, history, and external-change queues.

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
    public string AppearanceSettingsPath => _workbenchSettings.SettingsPath;

    public ProjectTabViewModel? CurrentProject
    {
        get => _currentProject;
        private set
        {
            if (SetProperty(ref _currentProject, value))
            {
                OnPropertyChanged(nameof(CanScanProjectRules));
                OnPropertyChanged(nameof(CanAutoSetupProjectRules));
                OnPropertyChanged(nameof(ProjectRulesActiveProjectRoot));
            }
        }
    }

    public ProjectNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            var previous = _selectedNode;
            if (!SetProperty(ref _selectedNode, value))
            {
                if (value is { IsFile: true })
                {
                    SelectFileNode(value);
                }

                return;
            }

            if (previous is not null)
            {
                previous.IsCurrent = false;
            }

            if (value is not null)
            {
                value.IsCurrent = true;
            }

            RefreshCurrentRowHighlights(previous, value);

            if (value is null || value.IsFolder)
            {
                return;
            }

            SelectFileNode(value);
        }
    }

    public TreeRowViewModel? SelectedTreeRow
    {
        get => _selectedTreeRow;
        set
        {
            if (value?.Node is not { } node)
            {
                SetProperty(ref _selectedTreeRow, null);
                ReplaceSelectedTreeRows([]);
                return;
            }

            SelectTreeRow(value, false);
            if (!SelectedTreeRows.Contains(value))
            {
                ReplaceSelectedTreeRows([value]);
            }
        }
    }

    public FileHistoryViewModel? SelectedHistory
    {
        get => _selectedHistory;
        private set => SetProperty(ref _selectedHistory, value);
    }

    public EditorDocumentViewModel? ActiveDocument
    {
        get => _activeDocument;
        private set
        {
            if (SetProperty(ref _activeDocument, value))
            {
                OnPropertyChanged(nameof(HasActiveDocument));
            }
        }
    }

    public bool HasActiveDocument => ActiveDocument is not null;
    public bool HasExternalChanges => ExternalChanges.Count > 0;
    public string ExternalQueueTitle => HasExternalChanges ? $"External updates ({ExternalChanges.Count})" : "External updates";

}
