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

public sealed partial class WorkbenchViewModel : ObservableObject, IDisposable
{
    public const string RuleKindIgnoredDirectories = "ignoredDirectories";
    public const string RuleKindIgnoredFileNames = "ignoredFileNames";
    public const string RuleKindIgnoredFileTypes = "ignoredFileTypes";
    public const string RuleKindSupportedFileTypes = "supportedFileTypes";
    public const string RuleKindLocFileTypes = "locFileTypes";
    private const int DefaultExpandedDepth = 2;
    private const string FontAssets = "avares://ContextControl.Workbench/Assets/Fonts";

    private readonly Dictionary<string, ProjectWorkspaceState> _workspaceByProjectId = [];
    private readonly Dictionary<string, ExternalChangeTracker> _trackersByProjectId = [];
    private readonly WorkbenchSettings _workbenchSettings;
    private readonly UpdateService _updateService = new();
    private readonly SynchronizationContext? _uiContext;
    private readonly Timer _externalScanTimer;
    private int _projectSwitchVersion;
    private int _documentLoadVersion;
    private Dictionary<string, FileHistoryViewModel> _historyByPath;
    private ProjectTabViewModel? _currentProject;
    private ProjectNodeViewModel? _selectedNode;
    private TreeRowViewModel? _selectedTreeRow;
    private FileHistoryViewModel? _selectedHistory;
    private VersionEntryViewModel? _selectedVersion;
    private EditorDocumentViewModel? _activeDocument;
    private ThemeOptionViewModel _selectedTheme;
    private ThemeOptionViewModel _selectedSyntaxTheme;
    private ThemeOptionViewModel _selectedCodeFont;
    private ThemeOptionViewModel _selectedUiFont;
    private ThemeOptionViewModel _selectedUiFontColorMode;
    private ThemeOptionViewModel _selectedChatAppearance;
    private ThemeOptionViewModel _selectedSkin;
    private ThemeOptionViewModel _selectedFoldArrowPosition;
    private WorkbenchModeOptionViewModel _selectedWorkspaceMode;
    private bool _showFoldArrows;
    private bool _showSummaryArrowBorders;
    private bool _useParentChildArrowIndentation;
    private bool _showVerticalScopeLines;
    private bool _useColorfulFamilies;
    private bool _showAppearanceCodePreview;
    private bool _themeAdaptFileCountColor;
    private bool _themeAdaptLocColor;
    private bool _themeAdaptVersionColor;
    private bool _themeAdaptBytesColor;
    private bool _autoSwitchPromptModesToChat;
    private double _uiFontSize;
    private double _codeEditorFontSize;
    private double _promptWindowFontSize;
    private double _chatWindowFontSize;
    private Color _customUiFontColor;
    private bool _summarizeNamespace;
    private bool _summarizeClass;
    private bool _summarizeStruct;
    private bool _summarizeInterface;
    private bool _summarizeEnum;
    private bool _summarizeMethod;
    private bool _summarizeProperty;
    private bool _summarizeObject;
    private bool _summarizeBlock;
    private bool _summarizeArray;
    private bool _summarizeArguments;
    private bool _isHistoryOpen;
    private bool _showFileDetails = true;
    private bool _showSkippedFiles;
    private double _historyWidth;
    private double _historyOpacity;
    private double _historyGutter;
    private string _supportedFileTypesLabel = "";
    private string _ignoredFileTypesLabel = "";
    private string _fileRulesPath = "";
    private string _supportedFileTypesText = "";
    private string _ignoredFileTypesText = "";
    private string _ignoredFileNamesText = "";
    private string _ignoredDirectoriesText = "";
    private string _newIgnoredDirectoryRuleText = "";
    private string _newIgnoredFileNameRuleText = "";
    private string _newIgnoredFileTypeRuleText = "";
    private string _newSupportedFileTypeRuleText = "";
    private string _locFileTypesText = "";
    private string _newLocFileTypeRuleText = "";
    private string _fileRulesStatus = "";
    private string _projectSettingsPath = "";
    private string _projectSettingsProjectRootText = "";
    private string _projectSettingsOutputRootText = "";
    private string _projectSettingsVersionCacheRootText = "";
    private string _projectSettingsStatus = "";
    private string _projectScanSummary = "No scan yet.";
    private string _projectScanResultText = "No scan yet.";
    private string _projectScanRuleSummary = "";
    private string _projectScanAutoSetupStatus = "";
    private bool _showProjectScanMetrics = true;
    private string _projectGraphSummary = "No project loaded.";
    private string _projectGraphTreeText = "No project loaded.";
    private ProjectNodeViewModel? _projectGraphSelectedNode;
    private int _projectGraphVersion;
    private int _projectGraphCenterVersion;
    private int _projectTreeFocusVersion;
    private string _projectGraphSearchText = "";
    private bool _isProjectGraphSearchOpen;
    private List<ProjectGraphSearchEntry>? _projectGraphSearchIndex;
    private string _projectTreeSearchText = "";
    private bool _isProjectTreeSearchOpen;
    private bool _isProjectFilesPaneOpen;
    private bool _isTopLocMode;
    private bool _isBrowserRoutingPaneOpen;
    private bool _isProjectGraphTreePaneOpen;
    private bool _isProjectScanRunning;
    private string _projectGraphLayoutMode = "graph";
    private string _projectGraphGenerationPalette = WorkbenchSettings.DefaultProjectGraphGenerationColors;
    private AppUpdateInfo? _availableUpdate;
    private bool _isCheckingForUpdates;
    private bool _isDownloadingUpdate;
    private string _updateButtonLabel = "Check updates";
    private string _updateStatusLabel = "ContextControl update check has not run yet.";

    private WorkbenchViewModel(
        ObservableCollection<ProjectTabViewModel> projects,
        ObservableCollection<ProjectNodeViewModel> projectTree,
        Dictionary<string, FileHistoryViewModel> historyByPath,
        ProjectFileRules? fileRules = null,
        bool treeStatePrepared = false,
        WorkbenchSettings? workbenchSettings = null)
    {
        _uiContext = SynchronizationContext.Current;
        _workbenchSettings = workbenchSettings ?? WorkbenchSettings.Load();
        ContextControl = new ContextControlViewModel(_workbenchSettings);
        ContextControl.SetProjectFileOpener(OpenContextControlPatchTarget);
        ContextControl.SetPromptModeWorkspaceRequester(SwitchToChatFromPromptMode);
        ContextControl.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ContextControlViewModel.IsPromptOpen))
            {
                OnPropertyChanged(nameof(PromptWindowViewLabel));
            }
        };
        BrowserPane = new BrowserPaneViewModel(
            _workbenchSettings.ContextControlRoot,
            ExternalBrowserService.DetectTargets(),
            _workbenchSettings.ExternalBrowserKey);
        BrowserPane.ExternalBrowserSelectionChanged += (_, key) =>
        {
            _workbenchSettings.ExternalBrowserKey = key;
            SaveAppearanceSettings();
        };
        Projects = projects;
        ProjectTree = projectTree;
        ExternalChanges = [];
        ProjectScanMetrics = [];
        ProjectScanSections = [];
        ProjectScanIdentitySections = [];
        ProjectScanFileSections = [];
        ProjectScanRuleSections = [];
        ProjectScanDiagnosticSections = [];
        IgnoredDirectoryRules = [];
        IgnoredFileNameRules = [];
        IgnoredFileTypeRules = [];
        SupportedFileTypeRules = [];
        LocFileTypeRules = [];
        ProjectGraphGenerationColors = [];
        Themes =
        [
            new ThemeOptionViewModel("empty", "Porcelain", "neutral light workbench with quiet blue-green state accents", null, "Light", "Experimental"),
            new ThemeOptionViewModel("alabaster", "Alabaster", "warm low-glare light palette with brass and teal accents for long sessions", null, "Light", "Professional"),
            new ThemeOptionViewModel("pearl", "Pearl", "cool soft-gray light theme with polished steel-blue accents", null, "Light", "Professional"),
            new ThemeOptionViewModel("opal", "Opal", "clean light workspace with quiet neutrals and muted jewel accents", null, "Light", "Professional"),
            new ThemeOptionViewModel("mist", "Mist", "whiteish cool-gray workspace with soft blue-green focus accents", null, "Light", "Calm"),
            new ThemeOptionViewModel("limestone", "Limestone", "whiteish stone-gray theme with muted sage and mineral blue accents", null, "Light", "Calm"),
            new ThemeOptionViewModel("dark", "Graphite", "low-glare industry-style dark theme with muted steel accents", null, "Dark", "Classic"),
            new ThemeOptionViewModel("nocturne", "Nocturne", "deep blue-black dark theme with low-glare surfaces and refined cyan contrast", null, "Dark", "Professional"),
            new ThemeOptionViewModel("onyx", "Onyx", "charcoal luxury dark theme with restrained brass accents", null, "Dark", "Professional"),
            new ThemeOptionViewModel("smoke", "Smoke", "neutral dark gray workbench with soft sage highlights for sustained focus", null, "Dark", "Professional"),
            new ThemeOptionViewModel("carbon", "Carbon", "black and graphite workbench with desaturated steel-blue accents", null, "Dark", "Calm"),
            new ThemeOptionViewModel("obsidian", "Obsidian", "near-black polished theme with quiet platinum and mineral-blue contrast", null, "Dark", "Calm"),
            new ThemeOptionViewModel("ash", "Ash", "soft smoky gray theme with muted sage accents for long focused work", null, "Dark", "Calm"),
            new ThemeOptionViewModel("graphene", "Graphene", "cool black-gray technical theme with subdued teal-blue highlights", null, "Dark", "Calm"),
            new ThemeOptionViewModel("solarized", "Solarized", "low-glare classic light palette with balanced yellow and blue", null, "Light", "Classic"),
            new ThemeOptionViewModel("cobalt", "Cobalt", "blue technical theme with crisp file-tree separation", null, "Dark", "Classic"),
            new ThemeOptionViewModel("contrast", "Contrast", "high-contrast dark workbench for maximum separation", null, "Dark", "Classic"),
            new ThemeOptionViewModel("verdant", "Verdant", "calm green light theme for long project scans", null, "Light", "Experimental"),
            new ThemeOptionViewModel("ember", "Ember", "orange dark theme with soft graphite panels", null, "Dark", "Experimental"),
            new ThemeOptionViewModel("ruby", "Ruby", "deep red command surface with clean neutral contrast", null, "Dark", "Colorful"),
            new ThemeOptionViewModel("amethyst", "Amethyst", "purple dark theme with measured violet highlights", null, "Dark", "Colorful"),
            new ThemeOptionViewModel("matrix", "Phosphor", "green phosphor variant with restrained IDE contrast", null, "Dark", "Colorful")
        ];
        SyntaxThemes =
        [
            new ThemeOptionViewModel("adaptive", "Adaptive", "matches the selected IDE theme"),
            new ThemeOptionViewModel("github-dark", "GitHub Dark", "familiar industry standard contrast"),
            new ThemeOptionViewModel("one-dark", "One Dark", "Atom-style warm dark palette"),
            new ThemeOptionViewModel("monokai", "Monokai", "classic high-separation editor colors"),
            new ThemeOptionViewModel("solarized-light", "Solarized Light", "low-glare light palette"),
            new ThemeOptionViewModel("solarized-dark", "Solarized Dark", "low-glare dark palette"),
            new ThemeOptionViewModel("high-contrast-dark", "High Contrast", "maximum token separation")
        ];
        CodeFonts =
        [
            new ThemeOptionViewModel("cascadia-code", "Cascadia Code", "modern Windows programming font with ligature support", $"{FontAssets}#Cascadia Code, Consolas"),
            new ThemeOptionViewModel("jetbrains-mono", "JetBrains Mono", "popular IDE font with tall x-height and crisp punctuation", $"{FontAssets}#JetBrains Mono, Consolas"),
            new ThemeOptionViewModel("fira-code", "Fira Code", "widely used ligature font for dense code views", $"{FontAssets}#Fira Code, Consolas"),
            new ThemeOptionViewModel("source-code-pro", "Source Code Pro", "Adobe's readable monospace for long editing sessions", $"{FontAssets}#Source Code Pro, Consolas"),
            new ThemeOptionViewModel("ibm-plex-mono", "IBM Plex Mono", "technical monospace with IBM terminal heritage", $"{FontAssets}#IBM Plex Mono, Consolas"),
            new ThemeOptionViewModel("hack", "Hack", "practical open-source coding font tuned for terminals", $"{FontAssets}#Hack, Consolas"),
            new ThemeOptionViewModel("iosevka", "Iosevka", "compact programming font for information-dense panes", $"{FontAssets}#Iosevka, Consolas"),
            new ThemeOptionViewModel("victor-mono", "Victor Mono", "distinctive coding font with cursive italic support", $"{FontAssets}#Victor Mono, Consolas"),
            new ThemeOptionViewModel("consolas", "Consolas", "classic ClearType Windows programming font", "Consolas, Cascadia Mono"),
            new ThemeOptionViewModel("lucida-console", "Lucida Console", "legacy Windows console face with sturdy glyphs", "Lucida Console, Consolas"),
            new ThemeOptionViewModel("courier-new", "Courier New", "1950s typewriter lineage for old-school code texture", "Courier New, Courier, Consolas"),
            new ThemeOptionViewModel("ocr-a", "OCR-A", "1960s machine-readable style with retro engineering character", "OCR A Extended, Courier New, Consolas"),
            new ThemeOptionViewModel("fixedsys", "Fixedsys", "bitmap-era Windows terminal feel", "Fixedsys, Terminal, Lucida Console, Consolas"),
            new ThemeOptionViewModel("ibm-3270", "IBM 3270", "mainframe terminal look with practical fallbacks", "IBM 3270, 3270, Fixedsys, Lucida Console, Consolas"),
            new ThemeOptionViewModel("vt-retro", "VT Terminal", "DEC-style terminal stack for retro command panes", "VT323, DEC Terminal, Terminal, Lucida Console, Consolas"),
            new ThemeOptionViewModel("space-mono", "Space Mono", "geometric monospace with a 1960s aerospace mood", "Space Mono, Cascadia Code, Consolas")
        ];
        UiFonts =
        [
            new ThemeOptionViewModel("aptos", "Aptos", "current Microsoft UI default with compact readable forms", "Aptos, Arial, Segoe UI"),
            new ThemeOptionViewModel("inter", "Inter", "polished app UI font with excellent small-size clarity", "fonts:Inter, Segoe UI"),
            new ThemeOptionViewModel("segoe-ui", "Segoe UI", "classic Windows interface font", "Segoe UI Variable Text, Segoe UI, Aptos"),
            new ThemeOptionViewModel("ibm-plex-sans", "IBM Plex Sans", "neutral technical UI face with strong numerals", $"{FontAssets}#IBM Plex Sans, Segoe UI"),
            new ThemeOptionViewModel("roboto", "Roboto", "familiar Material-style UI font", $"{FontAssets}#Roboto, Segoe UI"),
            new ThemeOptionViewModel("sf-pro", "SF Pro", "Apple-style system UI stack", "SF Pro Text, SF Pro Display, Trebuchet MS, Segoe UI"),
            new ThemeOptionViewModel("helvetica-neue", "Helvetica Neue", "clean Swiss-style app interface stack", "Helvetica Neue, Helvetica, Arial"),
            new ThemeOptionViewModel("arial", "Arial", "portable sans-serif fallback with broad coverage", "Arial, Helvetica"),
            new ThemeOptionViewModel("verdana", "Verdana", "wide screen-first UI font for small text", "Verdana, Segoe UI"),
            new ThemeOptionViewModel("tahoma", "Tahoma", "legacy Windows UI face with tight spacing", "Tahoma, Segoe UI"),
            new ThemeOptionViewModel("trebuchet", "Trebuchet MS", "older web UI face with warmer forms", "Trebuchet MS, Segoe UI")
        ];
        UiFontColorModes =
        [
            new ThemeOptionViewModel("theme", "Theme adapt", "use the selected theme's text colors"),
            new ThemeOptionViewModel("custom", "Custom", "use the selected color as the main UI text color")
        ];
        ChatAppearances =
        [
            new ThemeOptionViewModel("dark", "Dark", "professional dark chat palette with calm role separation"),
            new ThemeOptionViewModel("light", "Light", "professional light chat palette with clean neutral contrast"),
            new ThemeOptionViewModel("adaptive", "Theme adapt", "derive chat colors from the selected workbench theme")
        ];
        Skins = new ObservableCollection<ThemeOptionViewModel>(
            WorkbenchSkins.All.Select(skin => new ThemeOptionViewModel(skin.Key, skin.Name, skin.Description)));
        FoldArrowPositions =
        [
            new ThemeOptionViewModel("codeEditor", "Code Editor", "render summary arrows next to the code text"),
            new ThemeOptionViewModel("locBlock", "LOC block", "render summary arrows inside the line-number gutter")
        ];
        WorkspaceModes =
        [
            new WorkbenchModeOptionViewModel("code", "Code Editor"),
            new WorkbenchModeOptionViewModel("chat", "Chat"),
            new WorkbenchModeOptionViewModel("graph", "Graph"),
            new WorkbenchModeOptionViewModel("browser", "Browser"),
            new WorkbenchModeOptionViewModel("llms", "LLMs"),
            new WorkbenchModeOptionViewModel("dependencies", "Dependencies"),
            new WorkbenchModeOptionViewModel("skillbook", "Skillbook"),
            new WorkbenchModeOptionViewModel("scanner", "Project scanner")
        ];
        _selectedTheme = FindOptionByKey(Themes, _workbenchSettings.ThemeKey, Themes[0]);
        _selectedSyntaxTheme = FindOptionByKey(SyntaxThemes, _workbenchSettings.SyntaxThemeKey, SyntaxThemes[0]);
        _selectedCodeFont = FindOptionByKey(CodeFonts, _workbenchSettings.CodeFontKey, CodeFonts[0]);
        _selectedUiFont = FindOptionByKey(UiFonts, _workbenchSettings.UiFontKey, UiFonts[0]);
        _selectedUiFontColorMode = FindOptionByKey(UiFontColorModes, _workbenchSettings.UiFontColorModeKey, UiFontColorModes[0]);
        _selectedChatAppearance = FindOptionByKey(ChatAppearances, _workbenchSettings.ChatAppearanceKey, ChatAppearances[0]);
        _selectedSkin = FindOptionByKey(Skins, _workbenchSettings.SkinKey, Skins[0]);
        _selectedFoldArrowPosition = FindOptionByKey(FoldArrowPositions, _workbenchSettings.FoldArrowPositionKey, FoldArrowPositions[0]);
        _selectedWorkspaceMode = FindModeByKey(_workbenchSettings.WorkspaceModeKey);
        RefreshWorkspaceModeState();
        _showFoldArrows = _workbenchSettings.ShowFoldArrows;
        _showSummaryArrowBorders = _workbenchSettings.ShowSummaryArrowBorders;
        _useParentChildArrowIndentation = _workbenchSettings.UseParentChildArrowIndentation;
        _showVerticalScopeLines = _workbenchSettings.ShowVerticalScopeLines;
        _useColorfulFamilies = _workbenchSettings.UseColorfulFamilies;
        _showAppearanceCodePreview = _workbenchSettings.ShowAppearanceCodePreview;
        _themeAdaptFileCountColor = _workbenchSettings.ThemeAdaptFileCountColor;
        _themeAdaptLocColor = _workbenchSettings.ThemeAdaptLocColor;
        _themeAdaptVersionColor = _workbenchSettings.ThemeAdaptVersionColor;
        _themeAdaptBytesColor = _workbenchSettings.ThemeAdaptBytesColor;
        _autoSwitchPromptModesToChat = _workbenchSettings.AutoSwitchPromptModesToChat;
        _uiFontSize = _workbenchSettings.UiFontSize;
        _codeEditorFontSize = _workbenchSettings.CodeEditorFontSize;
        _promptWindowFontSize = _workbenchSettings.PromptWindowFontSize;
        _chatWindowFontSize = _workbenchSettings.ChatWindowFontSize;
        _customUiFontColor = ParseColor(_workbenchSettings.CustomUiFontColor, Color.Parse("#DDE6E8"));
        _showSkippedFiles = _workbenchSettings.ShowSkippedFiles;
        _isProjectFilesPaneOpen = _workbenchSettings.ShowProjectFilesPane;
        _isTopLocMode = _workbenchSettings.ProjectFilesTopLocMode;
        _isBrowserRoutingPaneOpen = _workbenchSettings.ShowBrowserRoutingPane;
        _isProjectGraphTreePaneOpen = _workbenchSettings.ShowProjectGraphTreePane;
        LoadProjectGraphGenerationColors(_workbenchSettings.ProjectGraphGenerationColors);
        ApplySummaryFoldKinds(_workbenchSettings.SummaryFoldKinds);
        SaveAppearanceSettings();
        _historyByPath = historyByPath;
        foreach (var project in projects)
        {
            var rules = fileRules ?? ProjectFileRules.Load(project.ProjectRoot);
            rules.Save();
            _workspaceByProjectId[project.Id] = new ProjectWorkspaceState(
                CloneTree(projectTree),
                new Dictionary<string, FileHistoryViewModel>(historyByPath),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new ObservableCollection<ExternalChangeItemViewModel>(),
                rules,
                treeStatePrepared);
        }

        SelectProjectCommand = new RelayCommand<ProjectTabViewModel>(SelectProject);
        ToggleNodeCommand = new RelayCommand<TreeRowViewModel>(ToggleNode, row => row?.HasChildren == true);
        IncludeExternalNodeCommand = new RelayCommand<ProjectNodeViewModel>(node => _ = IncludeExternalNodeAsync(node), node => node?.CanIncludeExternal == true);
        OpenVersionCommand = new RelayCommand<VersionEntryViewModel>(
            OpenVersion,
            version => !string.IsNullOrWhiteSpace(version?.SnapshotPath)
                || !string.IsNullOrWhiteSpace(version?.CurrentFilePath));
        OpenHistoryCommand = new RelayCommand<object>(_ => OpenHistory());
        CloseHistoryCommand = new RelayCommand<object>(_ => CloseHistory());
        ToggleFileDetailsCommand = new RelayCommand<object>(_ => ShowFileDetails = !ShowFileDetails);
        ToggleTopLocModeCommand = new RelayCommand<object>(_ => IsTopLocMode = !IsTopLocMode);
        ToggleSkippedFilesCommand = new RelayCommand<object>(_ => _ = ToggleSkippedFilesAsync());
        ToggleProjectFilesPaneCommand = new RelayCommand<object>(_ => IsProjectFilesPaneOpen = !IsProjectFilesPaneOpen);
        ToggleBrowserRoutingPaneCommand = new RelayCommand<object>(_ => IsBrowserRoutingPaneOpen = !IsBrowserRoutingPaneOpen);
        ToggleProjectGraphTreePaneCommand = new RelayCommand<object>(_ => IsProjectGraphTreePaneOpen = !IsProjectGraphTreePaneOpen);
        ToggleProjectGraphLayoutModeCommand = new RelayCommand<object>(_ => ToggleProjectGraphLayoutMode());
        TogglePromptWindowCommand = new RelayCommand<object>(_ => ContextControl.IsPromptOpen = !ContextControl.IsPromptOpen);
        CheckForUpdatesCommand = new RelayCommand<object>(_ => _ = CheckForUpdatesOrInstallAsync(), _ => !IsCheckingForUpdates && !IsDownloadingUpdate);
        OpenProjectGraphSearchCommand = new RelayCommand<object>(_ => OpenProjectGraphSearch());
        CloseProjectGraphSearchCommand = new RelayCommand<object>(_ => CloseProjectGraphSearch());
        OpenProjectTreeSearchCommand = new RelayCommand<object>(_ => OpenProjectTreeSearch());
        CloseProjectTreeSearchCommand = new RelayCommand<object>(_ => CloseProjectTreeSearch());
        SelectProjectGraphSearchSuggestionCommand = new RelayCommand<ProjectGraphSearchSuggestionViewModel>(SelectProjectGraphSearchSuggestion);
        SwitchWorkspaceModeCommand = new RelayCommand<WorkbenchModeOptionViewModel>(SwitchWorkspaceMode);
        AcceptAllExternalChangesCommand = new RelayCommand<object>(_ => AcceptAllExternalChanges());
        AcceptFinalExternalChangesCommand = new RelayCommand<object>(_ => AcceptFinalExternalChanges());
        AcceptSelectedExternalChangesCommand = new RelayCommand<object>(_ => AcceptSelectedExternalChanges());
        DismissSelectedExternalChangesCommand = new RelayCommand<object>(_ => DismissSelectedExternalChanges());
        ToggleExternalChangeCommand = new RelayCommand<ExternalChangeItemViewModel>(ToggleExternalChange);
        OpenExternalChangeCommand = new RelayCommand<ExternalChangeItemViewModel>(OpenExternalChange);
        OpenAttachmentCommand = new RelayCommand<string>(OpenAttachment);
        AddFileRuleEntryCommand = new RelayCommand<string>(AddFileRuleEntry);
        RemoveFileRuleEntryCommand = new RelayCommand<FileRuleEntryViewModel>(RemoveFileRuleEntry);
        SaveFileRulesCommand = new RelayCommand<object>(_ => _ = SaveFileRulesAsync());
        ResetFileRulesCommand = new RelayCommand<object>(_ => _ = ResetFileRulesAsync());
        ScanProjectRulesCommand = new RelayCommand<object>(_ => _ = ScanProjectRulesAsync());
        AutoSetupProjectRulesCommand = new RelayCommand<object>(_ => _ = AutoSetupProjectRulesAsync());
        SaveProjectSettingsCommand = new RelayCommand<object>(_ => SaveProjectSettings());
        ReloadProjectSettingsCommand = new RelayCommand<object>(_ => LoadProjectSettings());
        UseActiveProjectRootCommand = new RelayCommand<object>(_ => UseActiveProjectRootForSettings());
        UseContextControlProjectRootCommand = new RelayCommand<object>(_ => UseContextControlProjectRootForSettings());

        if (!treeStatePrepared)
        {
            PrepareTree();
        }

        SelectProject(projects.FirstOrDefault());
        LoadProjectSettings();

        ActiveDocument = EditorDocumentViewModel.Empty();
        // FileSystemWatcher plus the tracker's own background poll handles changes.
        // A UI-thread full-scan timer made medium projects feel frozen.
        _externalScanTimer = new Timer(_ => PostToUi(ScanExternalChangesNow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ = CheckForUpdatesOnStartupAsync();
    }

    public ObservableCollection<ProjectTabViewModel> Projects { get; }
    public ObservableCollection<ProjectNodeViewModel> ProjectTree { get; }
    public List<ProjectNodeViewModel> VisibleProjectNodes { get; } = [];
    public AvaloniaList<TreeRowViewModel> VisibleTreeRows { get; } = [];
    public AvaloniaList<TreeRowViewModel> TopLocTreeRows { get; } = [];
    public IReadOnlyList<TreeRowViewModel> ProjectTreeDisplayRows => IsTopLocMode ? TopLocTreeRows : VisibleTreeRows;
    public AvaloniaList<TreeRowViewModel> SelectedTreeRows { get; } = [];
    public ObservableCollection<ExternalChangeItemViewModel> ExternalChanges { get; }
    public ContextControlViewModel ContextControl { get; }
    public BrowserPaneViewModel BrowserPane { get; }
    public ObservableCollection<ProjectStackMetric> ProjectScanMetrics { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanIdentitySections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanFileSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanRuleSections { get; }
    public ObservableCollection<ProjectStackSection> ProjectScanDiagnosticSections { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredDirectoryRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredFileNameRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> IgnoredFileTypeRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> SupportedFileTypeRules { get; }
    public ObservableCollection<FileRuleEntryViewModel> LocFileTypeRules { get; }
    public ObservableCollection<ProjectGraphSearchSuggestionViewModel> ProjectGraphSearchSuggestions { get; } = [];
    public ObservableCollection<ProjectGraphGenerationColorViewModel> ProjectGraphGenerationColors { get; }
    public ObservableCollection<ThemeOptionViewModel> Themes { get; }
    public ObservableCollection<ThemeOptionViewModel> SyntaxThemes { get; }
    public ObservableCollection<ThemeOptionViewModel> CodeFonts { get; }
    public ObservableCollection<ThemeOptionViewModel> UiFonts { get; }
    public ObservableCollection<ThemeOptionViewModel> UiFontColorModes { get; }
    public ObservableCollection<ThemeOptionViewModel> ChatAppearances { get; }
    public ObservableCollection<ThemeOptionViewModel> Skins { get; }
    public ObservableCollection<ThemeOptionViewModel> FoldArrowPositions { get; }
    public ObservableCollection<WorkbenchModeOptionViewModel> WorkspaceModes { get; }
    public ICommand SelectProjectCommand { get; }
    public ICommand ToggleNodeCommand { get; }
    public ICommand IncludeExternalNodeCommand { get; }
    public ICommand OpenVersionCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand CloseHistoryCommand { get; }
    public ICommand ToggleFileDetailsCommand { get; }
    public ICommand ToggleTopLocModeCommand { get; }
    public ICommand ToggleSkippedFilesCommand { get; }
    public ICommand ToggleProjectFilesPaneCommand { get; }
    public ICommand ToggleBrowserRoutingPaneCommand { get; }
    public ICommand ToggleProjectGraphTreePaneCommand { get; }
    public ICommand ToggleProjectGraphLayoutModeCommand { get; }
    public ICommand TogglePromptWindowCommand { get; }
    public RelayCommand<object> CheckForUpdatesCommand { get; }
    public ICommand OpenProjectGraphSearchCommand { get; }
    public ICommand CloseProjectGraphSearchCommand { get; }
    public ICommand OpenProjectTreeSearchCommand { get; }
    public ICommand CloseProjectTreeSearchCommand { get; }
    public ICommand SelectProjectGraphSearchSuggestionCommand { get; }
    public ICommand SwitchWorkspaceModeCommand { get; }
    public ICommand AcceptAllExternalChangesCommand { get; }
    public ICommand AcceptFinalExternalChangesCommand { get; }
    public ICommand AcceptSelectedExternalChangesCommand { get; }
    public ICommand DismissSelectedExternalChangesCommand { get; }
    public ICommand ToggleExternalChangeCommand { get; }
    public ICommand OpenExternalChangeCommand { get; }
    public ICommand OpenAttachmentCommand { get; }
    public ICommand AddFileRuleEntryCommand { get; }
    public ICommand RemoveFileRuleEntryCommand { get; }
    public ICommand SaveFileRulesCommand { get; }
    public ICommand ResetFileRulesCommand { get; }
    public ICommand ScanProjectRulesCommand { get; }
    public ICommand AutoSetupProjectRulesCommand { get; }
    public ICommand SaveProjectSettingsCommand { get; }
    public ICommand ReloadProjectSettingsCommand { get; }
    public ICommand UseActiveProjectRootCommand { get; }
    public ICommand UseContextControlProjectRootCommand { get; }

}
