// CC-DESC: Owns shell toggles, creation, appearance persistence, and lifetime cleanup.

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
    public bool IsHistoryOpen
    {
        get => _isHistoryOpen;
        private set => SetProperty(ref _isHistoryOpen, value);
    }

    public bool ShowFileDetails
    {
        get => _showFileDetails;
        set
        {
            if (SetProperty(ref _showFileDetails, value))
            {
                OnPropertyChanged(nameof(FileDetailsToggleLabel));
            }
        }
    }

    public string FileDetailsToggleLabel => ShowFileDetails ? "File Details" : "Details Off";

    public bool IsTopLocMode
    {
        get => _isTopLocMode;
        set
        {
            if (SetProperty(ref _isTopLocMode, value))
            {
                if (value)
                {
                    RefreshTopLocTreeRows();
                }

                OnPropertyChanged(nameof(ProjectTreeDisplayRows));
                OnPropertyChanged(nameof(TopLocToggleLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public string TopLocToggleLabel => IsTopLocMode ? "TREE" : "TOP LOC";

    public bool ShowSkippedFiles
    {
        get => _showSkippedFiles;
        private set
        {
            if (SetProperty(ref _showSkippedFiles, value))
            {
                OnPropertyChanged(nameof(SkippedFilesToggleLabel));
            }
        }
    }

    public string SkippedFilesToggleLabel => ShowSkippedFiles ? "Hide Skip" : "Show Skip";

    public bool IsProjectFilesPaneOpen
    {
        get => _isProjectFilesPaneOpen;
        set
        {
            if (SetProperty(ref _isProjectFilesPaneOpen, value))
            {
                OnPropertyChanged(nameof(ProjectFilesPaneWidth));
                OnPropertyChanged(nameof(ProjectFilesTreeViewLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength ProjectFilesPaneWidth => IsProjectFilesPaneOpen
        ? new GridLength(252)
        : new GridLength(0);

    public string ProjectFilesTreeViewLabel => BuildViewToggleLabel(IsProjectFilesPaneOpen, "Project files tree");

    public bool IsBrowserRoutingPaneOpen
    {
        get => _isBrowserRoutingPaneOpen;
        set
        {
            if (SetProperty(ref _isBrowserRoutingPaneOpen, value))
            {
                OnPropertyChanged(nameof(BrowserRoutingPaneWidth));
                OnPropertyChanged(nameof(BrowserRoutingWindowViewLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength BrowserRoutingPaneWidth => IsBrowserRoutingPaneOpen
        ? new GridLength(282)
        : new GridLength(0);

    public string BrowserRoutingWindowViewLabel => BuildViewToggleLabel(IsBrowserRoutingPaneOpen, "Browser routing window");

    public bool IsProjectGraphTreePaneOpen
    {
        get => _isProjectGraphTreePaneOpen;
        set
        {
            if (SetProperty(ref _isProjectGraphTreePaneOpen, value))
            {
                OnPropertyChanged(nameof(ProjectGraphTreePaneWidth));
                OnPropertyChanged(nameof(ProjectGraphTreeColumnSpacing));
                OnPropertyChanged(nameof(ProjectGraphTreeToggleLabel));
                SaveAppearanceSettings();
            }
        }
    }

    public GridLength ProjectGraphTreePaneWidth => IsProjectGraphTreePaneOpen
        ? new GridLength(260)
        : new GridLength(0);

    public double ProjectGraphTreeColumnSpacing => IsProjectGraphTreePaneOpen ? 6 : 0;

    public string ProjectGraphTreeToggleLabel => IsProjectGraphTreePaneOpen ? "Hide Tree" : "Show Tree";

    public string ProjectGraphLayoutMode
    {
        get => _projectGraphLayoutMode;
        private set
        {
            var normalized = string.Equals(value, "cube", StringComparison.OrdinalIgnoreCase)
                ? "cube"
                : "graph";
            if (SetProperty(ref _projectGraphLayoutMode, normalized))
            {
                ProjectGraphVersion++;
                OnPropertyChanged(nameof(ProjectGraphLayoutToggleLabel));
            }
        }
    }

    public string ProjectGraphLayoutToggleLabel => string.Equals(ProjectGraphLayoutMode, "cube", StringComparison.OrdinalIgnoreCase)
        ? "G1 Tree"
        : "G1 Cube";

    public string ProjectGraphGenerationPalette => _projectGraphGenerationPalette;

    public string PromptWindowViewLabel => BuildViewToggleLabel(ContextControl.IsPromptOpen, "Prompt window");

    private static string BuildViewToggleLabel(bool isEnabled, string label)
    {
        return $"{(isEnabled ? "✓" : " ")} {label}";
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        return TryParseColor(value, out var color) ? color : fallback;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        try
        {
            color = Color.Parse(string.IsNullOrWhiteSpace(value) ? "#DDE6E8" : value.Trim());
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static string FormatColor(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public double HistoryWidth
    {
        get => _historyWidth;
        private set => SetProperty(ref _historyWidth, value);
    }

    public double HistoryOpacity
    {
        get => _historyOpacity;
        private set => SetProperty(ref _historyOpacity, value);
    }

    public double HistoryGutter
    {
        get => _historyGutter;
        private set => SetProperty(ref _historyGutter, value);
    }

    public static WorkbenchViewModel Create()
    {
        var settings = WorkbenchSettings.Load();
        var defaultRoot = FindDefaultContextControlRoot();
        if (defaultRoot is not null)
        {
            try
            {
                var loaded = ProjectLoader.Load(defaultRoot, showSkippedFiles: settings.ShowSkippedFiles);
                return new WorkbenchViewModel([loaded.Project], loaded.Tree, loaded.HistoryByPath, loaded.FileRules, loaded.IsTreePrepared, settings);
            }
            catch
            {
                // Fall back to the small design-time shell if local scanning fails.
            }
        }

        var projects = new ObservableCollection<ProjectTabViewModel>
        {
            new("cc", "CC", "Context Control", "18,284 LOC", "601", "239", "b9ef261", @"D:\Projects\contextcontrol"),
            new("vx", "VX", "VulkanVX", "open to scan", "project", "project", "linked", @"D:\Projects\vulkanas"),
            new("ide", "IDE", "Workbench", "native shell", "app", "native", "b9ef261", @"contextcontrol\ide"),
            new("ps", "PS", "PowerShell Core", "script core", "32", "3", "b9ef261", @"contextcontrol\lib")
        };

        return new WorkbenchViewModel(projects, BuildProjectTree(), BuildHistory(), workbenchSettings: settings);
    }

    private static ThemeOptionViewModel FindOptionByKey(
        IEnumerable<ThemeOptionViewModel> options,
        string? key,
        ThemeOptionViewModel fallback)
    {
        return options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? fallback;
    }

    private WorkbenchModeOptionViewModel FindModeByKey(string? key)
    {
        if (key?.Trim().Equals("imagegen", StringComparison.OrdinalIgnoreCase) == true
            || key?.Trim().Equals("image-gen", StringComparison.OrdinalIgnoreCase) == true
            || key?.Trim().Equals("image", StringComparison.OrdinalIgnoreCase) == true)
        {
            key = "chat";
        }

        return WorkspaceModes.FirstOrDefault(mode => string.Equals(mode.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? WorkspaceModes[0];
    }

    private void RefreshWorkspaceModeState()
    {
        foreach (var mode in WorkspaceModes)
        {
            mode.IsActive = ReferenceEquals(mode, _selectedWorkspaceMode);
        }
    }

    private void SwitchWorkspaceMode(WorkbenchModeOptionViewModel? mode)
    {
        if (mode is null)
        {
            return;
        }

        SelectedWorkspaceMode = mode;
        if (IsProjectScannerMode)
        {
            _ = ScanProjectRulesAsync();
        }
    }

    private void ApplySummaryFoldKinds(string? value)
    {
        var selected = ParseSummaryFoldKinds(value);
        _summarizeNamespace = selected.Contains("namespace");
        _summarizeClass = selected.Contains("class");
        _summarizeStruct = selected.Contains("struct");
        _summarizeInterface = selected.Contains("interface");
        _summarizeEnum = selected.Contains("enum");
        _summarizeMethod = selected.Contains("method");
        _summarizeProperty = selected.Contains("property");
        _summarizeObject = selected.Contains("object");
        _summarizeBlock = selected.Contains("block");
        _summarizeArray = selected.Contains("array");
        _summarizeArguments = selected.Contains("arguments");
    }

    private static HashSet<string> ParseSummaryFoldKinds(string? value)
    {
        const string defaultKinds = "namespace,class,struct,interface,enum,method,property,object,block,array,arguments";
        var source = value is null ? defaultKinds : value;
        return source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => item.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool SetSummaryKind(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SummaryFoldKinds));
        SaveAppearanceSettings();
        return true;
    }

    private string BuildSummaryFoldKinds()
    {
        var kinds = new List<string>(11);
        AddSummaryKind(kinds, _summarizeNamespace, "namespace");
        AddSummaryKind(kinds, _summarizeClass, "class");
        AddSummaryKind(kinds, _summarizeStruct, "struct");
        AddSummaryKind(kinds, _summarizeInterface, "interface");
        AddSummaryKind(kinds, _summarizeEnum, "enum");
        AddSummaryKind(kinds, _summarizeMethod, "method");
        AddSummaryKind(kinds, _summarizeProperty, "property");
        AddSummaryKind(kinds, _summarizeObject, "object");
        AddSummaryKind(kinds, _summarizeBlock, "block");
        AddSummaryKind(kinds, _summarizeArray, "array");
        AddSummaryKind(kinds, _summarizeArguments, "arguments");
        return string.Join(",", kinds);
    }

    private static void AddSummaryKind(List<string> kinds, bool enabled, string key)
    {
        if (enabled)
        {
            kinds.Add(key);
        }
    }

    private void SaveAppearanceSettings()
    {
        _workbenchSettings.SkinKey = SkinKey;
        _workbenchSettings.ThemeKey = SelectedTheme.Key;
        _workbenchSettings.SyntaxThemeKey = SelectedSyntaxTheme.Key;
        _workbenchSettings.CodeFontKey = CodeFontKey;
        _workbenchSettings.UiFontKey = UiFontKey;
        _workbenchSettings.UiFontColorModeKey = UiFontColorModeKey;
        _workbenchSettings.ChatAppearanceKey = ChatAppearanceKey;
        _workbenchSettings.CustomUiFontColor = CustomUiFontColorHex;
        _workbenchSettings.FoldArrowPositionKey = SelectedFoldArrowPosition.Key;
        _workbenchSettings.ShowFoldArrows = ShowFoldArrows;
        _workbenchSettings.ShowSummaryArrowBorders = ShowSummaryArrowBorders;
        _workbenchSettings.UseParentChildArrowIndentation = UseParentChildArrowIndentation;
        _workbenchSettings.ShowVerticalScopeLines = ShowVerticalScopeLines;
        _workbenchSettings.SummaryFoldKinds = SummaryFoldKinds;
        _workbenchSettings.UseColorfulFamilies = UseColorfulFamilies;
        _workbenchSettings.ShowAppearanceCodePreview = ShowAppearanceCodePreview;
        _workbenchSettings.ThemeAdaptFileCountColor = ThemeAdaptFileCountColor;
        _workbenchSettings.ThemeAdaptLocColor = ThemeAdaptLocColor;
        _workbenchSettings.ThemeAdaptVersionColor = ThemeAdaptVersionColor;
        _workbenchSettings.ThemeAdaptBytesColor = ThemeAdaptBytesColor;
        _workbenchSettings.AutoSwitchPromptModesToChat = AutoSwitchPromptModesToChat;
        _workbenchSettings.UiFontSize = UiFontSize;
        _workbenchSettings.CodeEditorFontSize = CodeEditorFontSize;
        _workbenchSettings.PromptWindowFontSize = PromptWindowFontSize;
        _workbenchSettings.ChatWindowFontSize = ChatWindowFontSize;
        _workbenchSettings.WorkspaceModeKey = SelectedWorkspaceMode.Key;
        _workbenchSettings.ExternalBrowserKey = BrowserPane.SelectedExternalBrowser?.Key ?? "default";
        _workbenchSettings.ShowSkippedFiles = ShowSkippedFiles;
        _workbenchSettings.ShowProjectFilesPane = IsProjectFilesPaneOpen;
        _workbenchSettings.ProjectFilesTopLocMode = IsTopLocMode;
        _workbenchSettings.ShowBrowserRoutingPane = IsBrowserRoutingPaneOpen;
        _workbenchSettings.ShowProjectGraphTreePane = IsProjectGraphTreePaneOpen;
        _workbenchSettings.ProjectGraphGenerationColors = ProjectGraphGenerationPalette;

        try
        {
            _workbenchSettings.Save();
        }
        catch
        {
            // Appearance changes should never take the editor down if the settings
            // file is temporarily unavailable.
        }
    }

    public void Dispose()
    {
        _externalScanTimer.Dispose();

        foreach (var tracker in _trackersByProjectId.Values)
        {
            tracker.Dispose();
        }

        _trackersByProjectId.Clear();
    }

    private static string? FindDefaultContextControlRoot()
    {
        return FindContextRoot(Directory.GetCurrentDirectory())
            ?? FindContextRoot(AppContext.BaseDirectory);
    }

    private static string? FindContextRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ccStart.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "cc.ps1"))
                && File.Exists(Path.Combine(directory.FullName, "ccReplace.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static double NormalizeAppearanceFontSize(double value, double fallback)
    {
        var next = double.IsFinite(value) && value > 0 ? value : fallback;
        return Math.Round(Math.Clamp(next, 8.0, 22.0), 1);
    }

    private void SwitchToChatFromPromptMode()
    {
        if (!AutoSwitchPromptModesToChat)
        {
            return;
        }

        var chatMode = FindModeByKey("chat");
        if (!ReferenceEquals(_selectedWorkspaceMode, chatMode))
        {
            SelectedWorkspaceMode = chatMode;
        }
    }

    private void ToggleProjectGraphLayoutMode()
    {
        ProjectGraphLayoutMode = string.Equals(ProjectGraphLayoutMode, "cube", StringComparison.OrdinalIgnoreCase)
            ? "graph"
            : "cube";
    }

    private void LoadProjectGraphGenerationColors(string colors)
    {
        ProjectGraphGenerationColors.Clear();
        var source = string.IsNullOrWhiteSpace(colors)
            ? WorkbenchSettings.DefaultProjectGraphGenerationColors
            : colors;
        var parsed = source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(color => FormatColor(ParseColor(color, Color.Parse("#7A858B"))))
            .Take(8)
            .ToArray();
        if (parsed.Length == 0)
        {
            parsed = WorkbenchSettings.DefaultProjectGraphGenerationColors
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        for (var index = 0; index < parsed.Length; index++)
        {
            ProjectGraphGenerationColors.Add(new ProjectGraphGenerationColorViewModel(index + 1, parsed[index], OnProjectGraphGenerationColorChanged));
        }

        _projectGraphGenerationPalette = string.Join(",", ProjectGraphGenerationColors.Select(color => color.ColorHex));
        OnPropertyChanged(nameof(ProjectGraphGenerationPalette));
    }

    private void OnProjectGraphGenerationColorChanged(ProjectGraphGenerationColorViewModel color)
    {
        _projectGraphGenerationPalette = string.Join(",", ProjectGraphGenerationColors.Select(item => item.ColorHex));
        OnPropertyChanged(nameof(ProjectGraphGenerationPalette));
        SaveAppearanceSettings();
    }

}
