// CC-DESC: Project rule, project settings, scanner, and graph bindable properties.

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
    public string SupportedFileTypesLabel
    {
        get => _supportedFileTypesLabel;
        private set => SetProperty(ref _supportedFileTypesLabel, value);
    }

    public string IgnoredFileTypesLabel
    {
        get => _ignoredFileTypesLabel;
        private set => SetProperty(ref _ignoredFileTypesLabel, value);
    }

    public string FileRulesPath
    {
        get => _fileRulesPath;
        private set => SetProperty(ref _fileRulesPath, value);
    }

    public string SupportedFileTypesText
    {
        get => _supportedFileTypesText;
        set
        {
            if (SetProperty(ref _supportedFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredFileTypesText
    {
        get => _ignoredFileTypesText;
        set
        {
            if (SetProperty(ref _ignoredFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredFileNamesText
    {
        get => _ignoredFileNamesText;
        set
        {
            if (SetProperty(ref _ignoredFileNamesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string IgnoredDirectoriesText
    {
        get => _ignoredDirectoriesText;
        set
        {
            if (SetProperty(ref _ignoredDirectoriesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string LocFileTypesText
    {
        get => _locFileTypesText;
        set
        {
            if (SetProperty(ref _locFileTypesText, value ?? ""))
            {
                OnPropertyChanged(nameof(FileRulesSummary));
            }
        }
    }

    public string NewIgnoredDirectoryRuleText
    {
        get => _newIgnoredDirectoryRuleText;
        set => SetProperty(ref _newIgnoredDirectoryRuleText, value ?? "");
    }

    public string NewIgnoredFileNameRuleText
    {
        get => _newIgnoredFileNameRuleText;
        set => SetProperty(ref _newIgnoredFileNameRuleText, value ?? "");
    }

    public string NewIgnoredFileTypeRuleText
    {
        get => _newIgnoredFileTypeRuleText;
        set => SetProperty(ref _newIgnoredFileTypeRuleText, value ?? "");
    }

    public string NewSupportedFileTypeRuleText
    {
        get => _newSupportedFileTypeRuleText;
        set => SetProperty(ref _newSupportedFileTypeRuleText, value ?? "");
    }

    public string NewLocFileTypeRuleText
    {
        get => _newLocFileTypeRuleText;
        set => SetProperty(ref _newLocFileTypeRuleText, value ?? "");
    }

    public string FileRulesSummary =>
        $"{SupportedFileTypeRules.Count} allowed types | "
        + $"{LocFileTypeRules.Count} LOC types | "
        + $"{IgnoredFileTypeRules.Count} skipped types | "
        + $"{IgnoredFileNameRules.Count} skipped files | "
        + $"{IgnoredDirectoryRules.Count} skipped folders";

    public string FileRulesStatus
    {
        get => _fileRulesStatus;
        private set => SetProperty(ref _fileRulesStatus, value);
    }

    public string ProjectRulesActiveProjectRoot => CurrentProject?.ProjectRoot ?? "No project open.";

    public string ProjectRulesContextControlRoot => _workbenchSettings.ContextControlRoot;

    public string ProjectSettingsPath
    {
        get => _projectSettingsPath;
        private set => SetProperty(ref _projectSettingsPath, value ?? "");
    }

    public string ProjectSettingsProjectRootText
    {
        get => _projectSettingsProjectRootText;
        set => SetProperty(ref _projectSettingsProjectRootText, value ?? "");
    }

    public string ProjectSettingsOutputRootText
    {
        get => _projectSettingsOutputRootText;
        set => SetProperty(ref _projectSettingsOutputRootText, value ?? "");
    }

    public string ProjectSettingsVersionCacheRootText
    {
        get => _projectSettingsVersionCacheRootText;
        set => SetProperty(ref _projectSettingsVersionCacheRootText, value ?? "");
    }

    public string ProjectSettingsStatus
    {
        get => _projectSettingsStatus;
        private set => SetProperty(ref _projectSettingsStatus, value ?? "");
    }

    public string ProjectScanSummary
    {
        get => _projectScanSummary;
        private set => SetProperty(ref _projectScanSummary, value ?? "");
    }

    public string ProjectScanResultText
    {
        get => _projectScanResultText;
        private set => SetProperty(ref _projectScanResultText, value ?? "");
    }

    public string ProjectScanRuleSummary
    {
        get => _projectScanRuleSummary;
        private set => SetProperty(ref _projectScanRuleSummary, value ?? "");
    }

    public string ProjectScanAutoSetupStatus
    {
        get => _projectScanAutoSetupStatus;
        private set => SetProperty(ref _projectScanAutoSetupStatus, value ?? "");
    }

    public bool ShowProjectScanMetrics
    {
        get => _showProjectScanMetrics;
        set => SetProperty(ref _showProjectScanMetrics, value);
    }

    public string ProjectGraphSummary
    {
        get => _projectGraphSummary;
        private set => SetProperty(ref _projectGraphSummary, value ?? "");
    }

    public string ProjectGraphTreeText
    {
        get => _projectGraphTreeText;
        private set => SetProperty(ref _projectGraphTreeText, value ?? "");
    }

    public ProjectNodeViewModel? ProjectGraphSelectedNode
    {
        get => _projectGraphSelectedNode;
        set
        {
            if (SetProperty(ref _projectGraphSelectedNode, value))
            {
                OnPropertyChanged(nameof(ProjectGraphSelectedLabel));
                if (value is not null)
                {
                    FocusProjectTreeNode(value);
                    if (value.IsFile)
                    {
                        ClearActiveVersion();
                        SelectHistory(value.Path);
                        OpenDocument(value, switchToEditor: false);
                    }
                }
            }
        }
    }

    public string ProjectGraphSelectedLabel => ProjectGraphSelectedNode is null
        ? "No graph node selected."
        : BuildProjectGraphSelectedLabel(ProjectGraphSelectedNode);

    public int ProjectGraphVersion
    {
        get => _projectGraphVersion;
        private set => SetProperty(ref _projectGraphVersion, value);
    }

    public int ProjectGraphCenterVersion
    {
        get => _projectGraphCenterVersion;
        private set => SetProperty(ref _projectGraphCenterVersion, value);
    }

    public int ProjectTreeFocusVersion
    {
        get => _projectTreeFocusVersion;
        private set => SetProperty(ref _projectTreeFocusVersion, value);
    }

    public bool IsProjectGraphSearchOpen
    {
        get => _isProjectGraphSearchOpen;
        set
        {
            if (SetProperty(ref _isProjectGraphSearchOpen, value))
            {
                if (!value)
                {
                    ProjectGraphSearchText = "";
                }

                OnPropertyChanged(nameof(HasProjectGraphSearchSuggestions));
            }
        }
    }

    public string ProjectGraphSearchText
    {
        get => _projectGraphSearchText;
        set
        {
            if (SetProperty(ref _projectGraphSearchText, value ?? ""))
            {
                UpdateProjectGraphSearchSuggestions();
            }
        }
    }

    public bool HasProjectGraphSearchSuggestions => ProjectGraphSearchSuggestions.Count > 0;

    public bool IsProjectTreeSearchOpen
    {
        get => _isProjectTreeSearchOpen;
        set
        {
            if (SetProperty(ref _isProjectTreeSearchOpen, value) && !value)
            {
                ProjectTreeSearchText = "";
            }
        }
    }

    public string ProjectTreeSearchText
    {
        get => _projectTreeSearchText;
        set
        {
            if (SetProperty(ref _projectTreeSearchText, value ?? ""))
            {
                FocusProjectTreeSearchResult();
            }
        }
    }

    public bool IsProjectScanRunning
    {
        get => _isProjectScanRunning;
        private set
        {
            if (SetProperty(ref _isProjectScanRunning, value))
            {
                OnPropertyChanged(nameof(CanScanProjectRules));
                OnPropertyChanged(nameof(CanAutoSetupProjectRules));
                OnPropertyChanged(nameof(ProjectScanButtonLabel));
            }
        }
    }

    public bool CanScanProjectRules => !IsProjectScanRunning && CurrentProject is not null;
    public bool CanAutoSetupProjectRules => !IsProjectScanRunning && CurrentProject is not null;
    public string ProjectScanButtonLabel => IsProjectScanRunning ? "Scanning" : "Scan";

}
