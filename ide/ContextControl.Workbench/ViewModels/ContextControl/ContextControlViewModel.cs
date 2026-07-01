// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using Avalonia.Collections;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel : ObservableObject
{
    private const string ChatConversationKind = "chat";
    private const string ImageGenConversationKind = "imagegen";
    private const string DefaultCodexModelId = "gpt-5.3-codex-spark";
    private const string CustomFlowCodexModelId = "gpt customflow";
    private const string DefaultCodexReasoningEffort = "low";
    private const double CompactPromptBarHeight = 206;
    private const double MaximumPromptBarHeight = 360;
    private const double BusyPromptProgressHeight = 8;
    private const double PromptLineHeight = 18;
    private const int CompactPromptLines = 4;
    private const int EstimatedPromptWrapColumn = 82;
    private const int LargePromptCharacterThreshold = 24_000;
    private const int LargePromptLineThreshold = 600;
    private const int PromptHeightEstimationCharacterLimit = 12_000;
    private const int PromptHeightEstimationLineLimit = 240;
    private static readonly int PromptVisualLinesForMaximumHeight =
        CompactPromptLines + (int)Math.Ceiling((MaximumPromptBarHeight - CompactPromptBarHeight) / PromptLineHeight);
    private static readonly HashSet<string> AutoAttachmentKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "dir",
        "code",
        "patch"
    };
    private static readonly HashSet<string> ImageAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    };
    private const string LlmSortNewest = "Newest";
    private const string LlmSortOldest = "Oldest";
    private const string LlmSortProvider = "Provider";
    private const string LlmSortName = "Name";
    private const string LlmSortFit = "Fit";
    private const string LlmSortInstalled = "Installed first";
    private const string LlmSortContext = "Context window";
    private const string LlmProviderAll = "All providers";
    private const string LlmSourceAll = "All";
    private const string LlmSourceLocalOnly = "Local";
    private const string LlmSourceCloudOnly = "Cloud";
    private const string LlmOwnershipAll = "All";
    private const string LlmOwnershipOwned = "Owned";
    private const string LlmOwnershipNotOwned = "Not Owned";
    private const string LlmPurposeAll = "All purposes";
    private const string LlmBaseAll = "All bases";
    private const string LlmContextAny = "Any";
    private const string LlmRequirementAny = "Any requirement";
    private const string CodexDefaultOptionLabel = "Codex config default";
    private const int CcStageRequest = 0;
    private const int CcStageDir = 0;
    private const int CcStageResolve = 0;
    private const int CcStageExport = 1;
    private const int CcStagePatch = 1;
    private const int CcStagePreview = 2;
    private const int CcStageApply = 2;

    private readonly WorkbenchSettings _settings;
    private readonly ContextControlProcessService _processService;
    private readonly ContextPromptBuilder _promptBuilder;
    private readonly IAiConnectionService _apiConnection;
    private readonly IAiConnectionService _browserConnection;
    private readonly LocalLlmService _localLlmService;
    private readonly SkillbookService _skillbookService;
    private readonly CodexHarnessService _codexHarnessService;
    private readonly ContextCapsuleBuilder _capsuleBuilder;
    private readonly ContextSemanticMapBuilder _semanticMapBuilder;
    private readonly ContextFileResolverService _fileResolver;
    private readonly ChatHistoryService _chatHistoryService;
    private readonly object _settingsSaveLock = new();
    private Func<string, Task>? _clipboardWriter;
    private Func<ChatSnippetViewModel, Task<string?>>? _snippetFileSaver;
    private Action<string>? _projectFileOpener;
    private Action? _promptModeWorkspaceRequester;
    private bool _isBusy;
    private bool _isPromptOpen;
    private bool _isPromptTypingActive;
    private bool _isLargePrompt;
    private bool _isPatchPlanReady;
    private bool _isRefreshingLocalModels;
    private bool _isInstallingOllama;
    private bool _isOllamaInstalled;
    private bool _isOllamaInstallerPromptOpen;
    private bool _isExternalDependencyDeletePromptOpen;
    private bool _isTransferProgressActive;
    private bool _isTransferProgressIndeterminate = true;
    private bool _isCcTimelineExpanded = true;
    private bool _isAutopilotEnabled;
    private bool _isCodexRequestRunning;
    private bool _isCodexCliInstalled;
    private bool _isInstallingCodex;
    private bool _isCodexAuthenticated;
    private bool _isCodexLoginRequired;
    private bool _isRefreshingCodexStatus;
    private bool _isRefreshingCodexUsage;
    private bool _isCodexUsagePanelExpanded;
    private CancellationTokenSource? _codexAuthWatchCancellation;
    private int _currentCcTimelineStageIndex = CcStageRequest;
    private double _transferProgressValue;
    private string _dockPanelKey = "log";
    private string _promptModeKey;
    private string _phaseTitle = "Ready";
    private string _phaseDetail = "Write the request, run DIR, send the attached tree to the model, then run CC on the returned lines.";
    private string _transferProgressTitle = "";
    private string _transferProgressStatus = "";
    private string _transferProgressSizeLabel = "";
    private string _transferProgressSpeedLabel = "";
    private string _transferProgressPercentLabel = "";
    private readonly List<string> _transferProgressHistory = [];
    private int _transferProgressHistoryIndex = -1;
    private bool _isTransferProgressDismissible;
    private CancellationTokenSource? _transferProgressCancellation;
    private DateTime _lastTransferProgressUiUpdateUtc = DateTime.MinValue;
    private string _lastTransferProgressOperation = "";
    private string _lastTransferProgressStatus = "";
    private double? _lastTransferProgressPercent;
    private string _terminalOutputText = "";
    private readonly StringBuilder _terminalOutputBuilder = new();
    private string _promptText = "";
    private string _selectedRoute;
    private LocalLlmModelViewModel? _selectedLocalModel;
    private LocalLlmModelViewModel? _selectedImageGenerationModel;
    private LlmBackendDependencyViewModel? _pendingExternalDependencyDelete;
    private string _providerStatus = "Browser route selected";
    private string _hardwareSummary = "Detecting GPU...";
    private string _localLlmStatus = "Local model scan pending.";
    private string _codexStatus = "Codex CLI read-only CC capsule";
    private string _codexUsageSummary = "Codex usage appears after a Codex prompt completes.";
    private string _codexRateLimitSummary = "5h and weekly limits appear when Codex reports an account snapshot.";
    private string _codexFiveHourPercentLeftLabel = "--";
    private string _codexWeeklyPercentLeftLabel = "--";
    private string _codexUsageResetLabel = "minor reset --";
    private string _codexFiveHourResetLabel = "5h reset --";
    private string _codexWeeklyResetLabel = "weekly reset --";
    private double _codexFiveHourPercentLeftValue;
    private double _codexWeeklyPercentLeftValue;
    private double _codexFiveHourUsageBarFillWidth;
    private double _codexWeeklyUsageBarFillWidth;
    private bool _isCodexFiveHourLimitDepleted;
    private bool _isCodexWeeklyLimitDepleted;
    private string _lastAssistantPatchBlocks = "";
    private string _lastUserRequest = "";
    private IReadOnlyList<string> _lastFindDiscoveryRequestPaths = [];
    private string _fileRequestModelId;
    private string _patchWriteModelId;
    private string _patchReviewModelId;
    private string _chatModelId;
    private string _codexModelId;
    private string _codexReasoningEffort;
    private string _ollamaModelsDirectory;
    private string _ollamaModelsDirectoryStatus;
    private string _huggingFaceToken;
    private string _selectedLocalLlmSortOption = LlmSortNewest;
    private string _selectedLocalLlmProviderFilter = LlmProviderAll;
    private string _selectedLocalLlmSourceFilter = LlmSourceAll;
    private string _selectedLocalLlmPurposeFilter = LlmPurposeAll;
    private string _selectedLocalLlmBaseFilter = LlmBaseAll;
    private string _selectedLocalLlmContextFilter = LlmContextAny;
    private string _selectedLocalLlmRequirementFilter = LlmRequirementAny;
    private string _selectedLocalLlmOwnershipFilter = LlmOwnershipAll;
    private string _localLlmSearchText = "";
    private string _dependencySearchText = "";
    private string _lastMirroredStatusLine = "";
    private string _lastExportPath = "";
    private string _patchSummary = "No patch loaded.";
    private string _activeProjectRoot = "";
    private string _activeProjectRulesPath = "";
    private string _chatHistoryScopeKey = "";
    private string _activeConversationKind = ChatConversationKind;
    private bool _isSwitchingProjectState;
    private bool _isSyncingChatAttachments;
    private bool _isSyncingChatDraft;
    private bool _isSwitchingChatSession;
    private bool _isSwitchingConversationKind;
    private bool _isLlmInfoExpanded;
    private bool _isLlmFiltersExpanded = true;
    private bool _isLocalLlmSearchOpen;
    private bool _isDependencySearchOpen;
    private ChatSessionViewModel? _selectedChatSession;
    private ContextSemanticIndex? _semanticIndex;
    private string _legacyPromptText = "";
    private IReadOnlyList<ChatHistoryAttachmentData> _legacyPendingAttachments = [];
    private CancellationTokenSource? _codexCancellation;
    private ChatRequestProgressViewModel? _codexProgressItem;

    public ContextControlViewModel(WorkbenchSettings settings)
    {
        _settings = settings;
        _processService = new ContextControlProcessService(settings.ContextControlRoot);
        _promptBuilder = new ContextPromptBuilder();
        _apiConnection = new ApiAiConnectionService();
        _browserConnection = new BrowserAiConnectionService();
        _localLlmService = new LocalLlmService();
        _skillbookService = new SkillbookService(settings.ContextControlRoot);
        _codexHarnessService = new CodexHarnessService();
        _capsuleBuilder = new ContextCapsuleBuilder();
        _semanticMapBuilder = new ContextSemanticMapBuilder();
        _fileResolver = new ContextFileResolverService();
        _chatHistoryService = new ChatHistoryService(settings.ContextControlRoot);
        _chatHistoryScopeKey = BuildProjectScopeKey(_processService.ContextRoot);
        _isAutopilotEnabled = settings.IsAutopilotEnabled;
        _fileRequestModelId = settings.FileRequestModel;
        _patchWriteModelId = settings.PatchWriteModel;
        _patchReviewModelId = settings.PatchReviewModel;
        _chatModelId = settings.ChatModel;
        var cleanCodexModel = CleanCodexOption(settings.CodexModel);
        var cleanCodexReasoning = NormalizeCodexReasoningOption(settings.CodexReasoningEffort);
        _codexModelId = string.IsNullOrWhiteSpace(cleanCodexModel) ? DefaultCodexModelId : cleanCodexModel;
        _codexReasoningEffort = string.IsNullOrWhiteSpace(cleanCodexReasoning) ? DefaultCodexReasoningEffort : cleanCodexReasoning;
        _ollamaModelsDirectory = LocalLlmService.ResolveOllamaModelsDirectory(settings.OllamaModelsDirectory);
        _ollamaModelsDirectoryStatus = $"Ollama model storage: {_ollamaModelsDirectory}";
        _huggingFaceToken = settings.HuggingFaceToken;
        _selectedLocalLlmSortOption = NormalizeLocalLlmSortOption(settings.LocalLlmSortOption);
        _selectedLocalLlmProviderFilter = CleanLocalLlmFilter(settings.LocalLlmProviderFilter, LlmProviderAll);
        _selectedLocalLlmSourceFilter = NormalizeLocalLlmSourceFilter(settings.LocalLlmSourceFilter);
        _selectedLocalLlmPurposeFilter = CleanLocalLlmFilter(settings.LocalLlmPurposeFilter, LlmPurposeAll);
        _selectedLocalLlmBaseFilter = CleanLocalLlmFilter(settings.LocalLlmBaseFilter, LlmBaseAll);
        _selectedLocalLlmContextFilter = NormalizeLocalLlmContextFilter(settings.LocalLlmContextFilter);
        _selectedLocalLlmRequirementFilter = NormalizeLocalLlmRequirementFilter(settings.LocalLlmRequirementFilter);
        LocalLlmService.ApplyOllamaModelsDirectoryToProcess(_ollamaModelsDirectory);
        LocalLlmService.ApplyHuggingFaceTokenToProcess(_huggingFaceToken);

        RouteOptions =
        [
            "Browser: ChatGPT",
            "Browser: DeepSeek",
            "Browser: Claude",
            "Local: Ollama",
            "API: OpenAI",
            "API: Custom"
        ];

        _selectedRoute = RouteOptions.Contains(settings.SelectedAiRoute)
            ? settings.SelectedAiRoute
            : RouteOptions[0];
        _promptModeKey = NormalizePromptModeKey(settings.PromptModeKey);
        _activeConversationKind = string.Equals(_promptModeKey, "imagegen", StringComparison.OrdinalIgnoreCase)
            ? ImageGenConversationKind
            : ChatConversationKind;

        IsPromptOpen = settings.PromptBarOpenByDefault;
        LocalLlmModels = new ObservableCollection<LocalLlmModelViewModel>(
            LocalLlmService.Catalog.Select(model => new LocalLlmModelViewModel(model)));
        VisibleLocalLlmModels = [];
        LocalLlmSortOptions = new ObservableCollection<string>(
            new[]
            {
                LlmSortNewest,
                LlmSortOldest,
                LlmSortProvider,
                LlmSortName,
                LlmSortFit,
                LlmSortInstalled,
                LlmSortContext
            });
        LocalLlmProviderFilters = [];
        LocalLlmSourceFilters = new ObservableCollection<string>(
            new[]
            {
                LlmSourceAll,
                LlmSourceLocalOnly,
                LlmSourceCloudOnly
            });
        LocalLlmPurposeFilters = [];
        LocalLlmBaseFilters = [];
        LocalLlmContextFilters = new ObservableCollection<string>(
            new[]
            {
                LlmContextAny,
                "4K+",
                "16K+",
                "32K+",
                "128K+",
                "256K+",
                "1M+",
                "10M+"
            });
        LocalLlmRequirementFilters = new ObservableCollection<string>(
            new[]
            {
                LlmRequirementAny,
                "CPU-safe",
                "4 GB VRAM or less",
                "8 GB VRAM or less",
                "16 GB VRAM or less",
                "24 GB VRAM or less",
                "Workstation/server"
            });
        RefreshLocalLlmProviderFilters();
        RefreshLocalLlmPurposeFilters();
        RefreshLocalLlmBaseFilters();
        ApplyLocalLlmFilters();
        LocalModelIdOptions = new ObservableCollection<string>(LocalLlmService.Catalog.Select(model => model.Id));
        CodexModelOptions = new ObservableCollection<string>(
            new[]
            {
                CustomFlowCodexModelId,
                "gpt-5.5",
                "gpt-5.4",
                "gpt-5.4-mini",
                "gpt-5.3-codex",
                "gpt-5.3-codex-spark",
                "gpt-5.2",
                "codex-auto-review"
            });
        if (!string.IsNullOrWhiteSpace(_codexModelId) && !CodexModelOptions.Contains(_codexModelId))
        {
            CodexModelOptions.Add(_codexModelId);
        }

        CodexReasoningEffortOptions = new ObservableCollection<string>(
            new[]
            {
                "low",
                "medium",
                "high",
                "xhigh"
            });
        if (!string.IsNullOrWhiteSpace(_codexReasoningEffort) && !CodexReasoningEffortOptions.Contains(_codexReasoningEffort))
        {
            CodexReasoningEffortOptions.Add(_codexReasoningEffort);
        }

        LlmBackendDependencies = new ObservableCollection<LlmBackendDependencyViewModel>(CreateLlmBackendDependencies());
        VisibleLlmBackendDependencies = [];
        ApplyDependencyFilters();
        UpdateOllamaBackendDependency();
        CcTimelineStages =
        [
            new CcTimelineStageViewModel("dir-request", "DIR + Request", "Map project and ask for source context", "The user request and DIR map are sent together. The model returns a minimal file/function list ending with END."),
            new CcTimelineStageViewModel("cc", "CC", "Export source and decide next step", "CC attaches selected source. The model either asks for more CC context or emits GO-ready CC-REPLACE blocks."),
            new CcTimelineStageViewModel("go", "GO", "Preview and apply patch", "GO and Apply are local ccReplace actions. They do not send a model prompt.")
        ];
        UpdateCcTimelineState();
        SkillbookEntries = [];
        InitializeSkillbookCommands();
        LoadSkillbookDocument();
        ChatSessions = [];
        ChatRequestProgressItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasChatRequestProgress));
            OnPropertyChanged(nameof(ShowBusyPromptProgress));
            OnPromptBarLayoutChanged();
        };

        RunDirCommand = new RelayCommand<object>(_ => _ = RunDirAsync(), _ => !IsBusy);
        RunCcCommand = new RelayCommand<object>(_ => _ = RunCcAsync(), _ => !IsBusy);
        RunGoCommand = new RelayCommand<object>(_ => _ = RunGoPreviewAsync(), _ => !IsBusy);
        RunDirTreeExportCommand = new RelayCommand<object>(_ => _ = RunDirTreeExportAsync(), _ => !IsBusy);
        ApplyPatchCommand = new RelayCommand<object>(_ => _ = ApplyPatchAsync(), _ => !IsBusy && IsPatchPlanReady);
        ApplyAllPatchCommand = new RelayCommand<object>(_ => _ = ApplyPatchAsync("all"), _ => !IsBusy && IsPatchPlanReady);
        OpenPatchPlanFileCommand = new RelayCommand<PatchPlanFileViewModel>(
            OpenPatchPlanFile,
            file => file?.CanOpen == true);
        SendCommand = new RelayCommand<object>(_ => _ = SendAsync(), _ => !IsBusy && !IsCodexPromptAuthBlocked);
        ToggleLogPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("log"));
        SelectLogPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("log"));
        SelectChatPanelCommand = new RelayCommand<object>(_ => SelectDockPanel("chat"));
        RefreshLocalModelsCommand = new RelayCommand<object>(_ => _ = RefreshLocalModelsAsync(), _ => !IsRefreshingLocalModels);
        InstallOllamaCommand = new RelayCommand<object>(_ => _ = InstallOllamaAsync(), _ => !IsOllamaInstalled && !IsBusy && !IsRefreshingLocalModels && !IsInstallingOllama);
        OpenOllamaDownloadPageCommand = new RelayCommand<object>(_ => OpenOllamaDownloadPage());
        ApplyOllamaModelsDirectoryCommand = new RelayCommand<object>(_ => ApplyOllamaModelsDirectory());
        CycleLocalLlmOwnershipFilterCommand = new RelayCommand<object>(_ => CycleLocalLlmOwnershipFilter());
        ToggleLlmInfoCommand = new RelayCommand<object>(_ => IsLlmInfoExpanded = !IsLlmInfoExpanded);
        ToggleLlmFiltersCommand = new RelayCommand<object>(_ => IsLlmFiltersExpanded = !IsLlmFiltersExpanded);
        InstallBackendDependencyCommand = new RelayCommand<LlmBackendDependencyViewModel>(
            dependency => _ = InstallBackendDependencyAsync(dependency),
            dependency => dependency is { CanInstall: true } && !IsBusy && !IsInstallingOllama && !IsRefreshingLocalModels);
        UninstallBackendDependencyCommand = new RelayCommand<LlmBackendDependencyViewModel>(
            dependency => _ = UninstallBackendDependencyAsync(dependency),
            dependency => dependency is not null
                && (dependency.CanUninstall || dependency.CanForceInstall)
                && !IsBusy
                && !IsInstallingOllama
                && !IsRefreshingLocalModels);
        PullLocalModelCommand = new RelayCommand<LocalLlmModelViewModel>(
            model => _ = PullLocalModelAsync(model),
            model => model is not null
                && (model.CanPull || model.CanInstallDependency || model.CanDownloadBackendModel || model.CanUninstall)
                && !IsBusy
                && !IsRefreshingLocalModels
                && !IsInstallingOllama);
        SwitchPromptToContextCommand = new RelayCommand<object>(_ => SwitchPromptModeFromButton("context"));
        SwitchPromptToChatCommand = new RelayCommand<object>(_ => SwitchPromptModeFromButton("context"));
        SwitchPromptToCodexCommand = new RelayCommand<object>(_ =>
        {
            ActivateCodexPromptMode();
            _promptModeWorkspaceRequester?.Invoke();
        });
        SwitchPromptToImageGenCommand = new RelayCommand<object>(_ => SwitchPromptModeFromButton("imagegen"));
        SwitchPromptToTerminalCommand = new RelayCommand<object>(_ => PromptModeKey = "terminal");
        ClearTerminalCommand = new RelayCommand<object>(_ => ClearTerminalOutput());
        PreviousTransferStatusCommand = new RelayCommand<object>(_ => MoveTransferProgressHistory(-1), _ => CanMoveTransferProgressHistory(-1));
        NextTransferStatusCommand = new RelayCommand<object>(_ => MoveTransferProgressHistory(1), _ => CanMoveTransferProgressHistory(1));
        CloseTransferProgressCommand = new RelayCommand<object>(_ => CloseTransferProgress(), _ => CanCloseTransferProgress);
        InstallCodexCommand = new RelayCommand<object>(
            _ => InstallCodex(),
            _ => !IsBusy && !IsCodexRequestRunning && !IsInstallingCodex);
        OpenCodexGuideCommand = new RelayCommand<object>(
            _ => OpenCodexGuide(),
            _ => !IsBusy && !IsCodexRequestRunning);
        OpenCodexLoginCommand = new RelayCommand<object>(
            _ => OpenCodexLogin(),
            _ => !IsBusy && !IsCodexRequestRunning && IsCodexCliInstalled);
        LogoutCodexCommand = new RelayCommand<object>(
            _ => _ = LogoutCodexAsync(),
            _ => IsCodexAuthenticated && !IsBusy && !IsCodexRequestRunning && !IsRefreshingCodexStatus);
        RefreshCodexStatusCommand = new RelayCommand<object>(
            _ => _ = RefreshCodexStatusAsync(),
            _ => !IsCodexRequestRunning && !IsRefreshingCodexStatus);
        RunCodexDoctorCommand = new RelayCommand<object>(
            _ => _ = RunCodexDoctorAsync(),
            _ => !IsBusy && !IsCodexRequestRunning && !IsRefreshingCodexStatus && IsCodexCliInstalled);
        CancelCodexRequestCommand = new RelayCommand<ChatRequestProgressViewModel>(
            CancelCodexRequest,
            item => CanCancelCodexRequest(item));
        ToggleCodexUsagePanelCommand = new RelayCommand<object>(_ =>
        {
            IsPromptOpen = true;
            IsCodexUsagePanelExpanded = !IsCodexUsagePanelExpanded;
            if (IsCodexUsagePanelExpanded)
            {
                _ = RefreshCodexUsageFromLogsAsync(showChecking: false);
            }
        });
        CopyRoutingLogCommand = new RelayCommand<object>(_ => _ = CopyRoutingLogAsync());
        CopySnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => _ = CopySnippetAsync(snippet));
        SaveSnippetAsCommand = new RelayCommand<ChatSnippetViewModel>(
            snippet => _ = SaveSnippetAsAsync(snippet),
            snippet => snippet?.CanSaveAsFile == true);
        CreateProjectFromMessageCommand = new RelayCommand<LocalLlmChatMessageViewModel>(
            message => _ = CreateProjectFromMessageAsync(message),
            message => message?.CanCreateProject == true);
        CopyChatTextCommand = new RelayCommand<LocalLlmChatPartViewModel>(part => _ = CopyChatTextAsync(part));
        ExportChatCommand = new RelayCommand<object>(_ => _ = ExportChatAsync());
        UseSnippetForCcCommand = new RelayCommand<ChatSnippetViewModel>(UseSnippetForCc);
        PreviewSnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => _ = PreviewSnippetAsync(snippet), snippet => snippet?.IsPatch == true);
        ToggleAttachmentIncludeCommand = new RelayCommand<ContextControlAttachmentViewModel>(ToggleAttachmentInclude);
        RemoveAttachmentCommand = new RelayCommand<ContextControlAttachmentViewModel>(RemoveAttachment);
        ToggleThinkingCommand = new RelayCommand<LocalLlmChatMessageViewModel>(message => message?.ToggleThinking());
        ToggleDiagnosticCommand = new RelayCommand<LocalLlmChatMessageViewModel>(message => message?.ToggleDiagnostic());
        ToggleSnippetCommand = new RelayCommand<ChatSnippetViewModel>(snippet => snippet?.ToggleExpanded());
        NewChatSessionCommand = new RelayCommand<object>(_ => CreateNewChatSession());
        SelectChatSessionCommand = new RelayCommand<ChatSessionViewModel>(SelectChatSession);
        RemoveChatSessionCommand = new RelayCommand<ChatSessionViewModel>(RemoveChatSession);
        CloseOllamaInstallerPromptCommand = new RelayCommand<object>(_ => IsOllamaInstallerPromptOpen = false);
        ConfirmExternalDependencyDeleteCommand = new RelayCommand<object>(
            _ => _ = ConfirmExternalDependencyDeleteAsync(),
            _ => _pendingExternalDependencyDelete is not null && !IsBusy && !IsRefreshingLocalModels);
        IgnoreExternalDependencyDeleteCommand = new RelayCommand<object>(_ => CloseExternalDependencyDeletePrompt());
        OpenPromptCommand = new RelayCommand<object>(_ => IsPromptOpen = true);
        ClosePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = false);
        TogglePromptCommand = new RelayCommand<object>(_ => IsPromptOpen = !IsPromptOpen);
        ToggleCcTimelineCommand = new RelayCommand<object>(_ =>
        {
            IsPromptOpen = true;
            IsCcTimelineExpanded = !IsCcTimelineExpanded;
        });
        ToggleAutopilotCommand = new RelayCommand<object>(_ => ToggleAutopilotMode());

        Log("info", $"Context root: {_processService.ContextRoot}");
        LoadChatHistory();
        _ = RefreshCodexStatusAsync();
        _ = RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
    }

    public ObservableCollection<string> RouteOptions { get; }
    public ObservableCollection<ContextControlAttachmentViewModel> Attachments { get; } = [];
    public ObservableCollection<ContextControlLogEntryViewModel> LogEntries { get; } = [];
    public ObservableCollection<LocalLlmModelViewModel> LocalLlmModels { get; }
    public AvaloniaList<LocalLlmModelViewModel> VisibleLocalLlmModels { get; }
    public ObservableCollection<LocalLlmModelViewModel> InstalledLocalModels { get; } = [];
    public ObservableCollection<LocalLlmModelViewModel> InstalledImageGenerationModels { get; } = [];
    public ObservableCollection<CcTimelineStageViewModel> CcTimelineStages { get; }
    public ObservableCollection<string> LocalLlmSortOptions { get; }
    public ObservableCollection<string> LocalLlmProviderFilters { get; }
    public ObservableCollection<string> LocalLlmSourceFilters { get; }
    public ObservableCollection<string> LocalLlmPurposeFilters { get; }
    public ObservableCollection<string> LocalLlmBaseFilters { get; }
    public ObservableCollection<string> LocalLlmContextFilters { get; }
    public ObservableCollection<string> LocalLlmRequirementFilters { get; }
    public ObservableCollection<string> LocalModelIdOptions { get; }
    public ObservableCollection<string> CodexModelOptions { get; }
    public ObservableCollection<string> CodexReasoningEffortOptions { get; }
    public ObservableCollection<LlmBackendDependencyViewModel> LlmBackendDependencies { get; }
    public AvaloniaList<LlmBackendDependencyViewModel> VisibleLlmBackendDependencies { get; }
    public ObservableCollection<SkillbookEntryViewModel> SkillbookEntries { get; }
    public ObservableCollection<ChatSessionViewModel> ChatSessions { get; }
    public ChatMessageCollectionViewModel ChatMessages { get; } = new();
    public ObservableCollection<ChatRequestProgressViewModel> ChatRequestProgressItems { get; } = [];
    public ObservableCollection<PatchPlanActionViewModel> PatchPlanActions { get; } = [];
    public ObservableCollection<PatchPlanFileViewModel> PatchPlanFiles { get; } = [];
    public bool HasAttachments => Attachments.Count > 0;
    public bool HasInstalledLocalModels => InstalledLocalModels.Count > 0;
    public bool HasInstalledImageGenerationModels => InstalledImageGenerationModels.Count > 0;
    public bool HasVisibleLocalLlmModels => VisibleLocalLlmModels.Count > 0;
    public bool HasChatSessions => ChatSessions.Count > 0;
    public bool HasChatRequestProgress => ChatRequestProgressItems.Count > 0;
    public bool HasPatchPlanActions => PatchPlanActions.Count > 0;
    public bool HasPatchPlanFiles => PatchPlanFiles.Count > 0;
    public string ChatHistoryPanelTitle => IsImageGenConversationKind(_activeConversationKind) ? "ImageGen Chats" : "Chats";
    public string ChatHistorySummary => IsImageGenConversationKind(_activeConversationKind)
        ? $"{ChatSessions.Count:N0} image chat(s)"
        : $"{ChatSessions.Count:N0} chat(s)";
    public string ImageGenerationModelSummary => $"{InstalledImageGenerationModels.Count:N0} image gen model(s)";
    public string LocalLlmVisibleSummary => $"{VisibleLocalLlmModels.Count:N0}/{LocalLlmModels.Count:N0} shown";
    public string LocalLlmVisibleCountLabel => $"{VisibleLocalLlmModels.Count:N0}/{LocalLlmModels.Count:N0}";
    public int InstalledLocalModelCount => LocalLlmModels.Count(model => model.IsInstalled);
    public string DependencySummary => $"{LlmBackendDependencies.Count(dependency => dependency.IsReady):N0}/{LlmBackendDependencies.Count:N0} backend(s) ready";
    public string LlmCompactInfoLabel => "";
    public string LocalLlmOwnershipFilterLabel => _selectedLocalLlmOwnershipFilter;
    public bool IsLlmInfoExpanded
    {
        get => _isLlmInfoExpanded;
        set
        {
            if (SetProperty(ref _isLlmInfoExpanded, value))
            {
                OnPropertyChanged(nameof(LlmInfoToggleLabel));
                OnPropertyChanged(nameof(LlmInfoPanelMaxHeight));
                OnPropertyChanged(nameof(LlmInfoPanelOpacity));
            }
        }
    }

    public bool IsLlmFiltersExpanded
    {
        get => _isLlmFiltersExpanded;
        set
        {
            if (SetProperty(ref _isLlmFiltersExpanded, value))
            {
                OnPropertyChanged(nameof(LlmFiltersToggleLabel));
                OnPropertyChanged(nameof(LlmFiltersPanelMaxHeight));
                OnPropertyChanged(nameof(LlmFiltersPanelOpacity));
            }
        }
    }

    public bool IsLocalLlmSearchOpen
    {
        get => _isLocalLlmSearchOpen;
        set => SetProperty(ref _isLocalLlmSearchOpen, value);
    }

    public bool IsDependencySearchOpen
    {
        get => _isDependencySearchOpen;
        set => SetProperty(ref _isDependencySearchOpen, value);
    }

    public string LlmInfoToggleLabel => "Info";
    public string LlmFiltersToggleLabel => "Filters";
    public double LlmInfoPanelMaxHeight => IsLlmInfoExpanded ? 44 : 0;
    public double LlmInfoPanelOpacity => IsLlmInfoExpanded ? 1 : 0;
    public double LlmFiltersPanelMaxHeight => IsLlmFiltersExpanded ? 92 : 0;
    public double LlmFiltersPanelOpacity => IsLlmFiltersExpanded ? 1 : 0;
    public string CurrentCcStationLabel => CcTimelineStages.Count > _currentCcTimelineStageIndex
        ? CcTimelineStages[_currentCcTimelineStageIndex].Title
        : "Request";
    public string CcTimelineToggleLabel => IsCcTimelineExpanded ? "Hide flow" : "Flow";

    public ChatSessionViewModel? SelectedChatSession
    {
        get => _selectedChatSession;
        private set
        {
            if (SetProperty(ref _selectedChatSession, value))
            {
                OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
            }
        }
    }

    public ICommand RunDirCommand { get; }
    public ICommand RunCcCommand { get; }
    public ICommand RunGoCommand { get; }
    public ICommand RunDirTreeExportCommand { get; }
    public ICommand ApplyPatchCommand { get; }
    public ICommand ApplyAllPatchCommand { get; }
    public ICommand OpenPatchPlanFileCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ToggleLogPanelCommand { get; }
    public ICommand SelectLogPanelCommand { get; }
    public ICommand SelectChatPanelCommand { get; }
    public ICommand RefreshLocalModelsCommand { get; }
    public ICommand InstallOllamaCommand { get; }
    public ICommand OpenOllamaDownloadPageCommand { get; }
    public ICommand ApplyOllamaModelsDirectoryCommand { get; }
    public ICommand CycleLocalLlmOwnershipFilterCommand { get; }
    public ICommand ToggleLlmInfoCommand { get; }
    public ICommand ToggleLlmFiltersCommand { get; }
    public ICommand InstallBackendDependencyCommand { get; }
    public ICommand UninstallBackendDependencyCommand { get; }
    public ICommand PullLocalModelCommand { get; }
    public ICommand SwitchPromptToContextCommand { get; }
    public ICommand SwitchPromptToChatCommand { get; }
    public ICommand SwitchPromptToCodexCommand { get; }
    public ICommand SwitchPromptToImageGenCommand { get; }
    public ICommand SwitchPromptToTerminalCommand { get; }
    public ICommand ClearTerminalCommand { get; }
    public ICommand PreviousTransferStatusCommand { get; }
    public ICommand NextTransferStatusCommand { get; }
    public ICommand CloseTransferProgressCommand { get; }
    public ICommand InstallCodexCommand { get; }
    public ICommand OpenCodexGuideCommand { get; }
    public ICommand OpenCodexLoginCommand { get; }
    public ICommand LogoutCodexCommand { get; }
    public ICommand RefreshCodexStatusCommand { get; }
    public ICommand RunCodexDoctorCommand { get; }
    public ICommand CancelCodexRequestCommand { get; }
    public ICommand ToggleCodexUsagePanelCommand { get; }
    public ICommand CopyRoutingLogCommand { get; }
    public ICommand CopySnippetCommand { get; }
    public ICommand SaveSnippetAsCommand { get; }
    public ICommand CreateProjectFromMessageCommand { get; }
    public ICommand CopyChatTextCommand { get; }
    public ICommand ExportChatCommand { get; }
    public ICommand UseSnippetForCcCommand { get; }
    public ICommand PreviewSnippetCommand { get; }
    public ICommand ToggleAttachmentIncludeCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand ToggleThinkingCommand { get; }
    public ICommand ToggleDiagnosticCommand { get; }
    public ICommand ToggleSnippetCommand { get; }
    public ICommand NewChatSessionCommand { get; }
    public ICommand SelectChatSessionCommand { get; }
    public ICommand RemoveChatSessionCommand { get; }
    public ICommand CloseOllamaInstallerPromptCommand { get; }
    public ICommand ConfirmExternalDependencyDeleteCommand { get; }
    public ICommand IgnoreExternalDependencyDeleteCommand { get; }
    public ICommand OpenPromptCommand { get; }
    public ICommand ClosePromptCommand { get; }
    public ICommand TogglePromptCommand { get; }
    public ICommand ToggleCcTimelineCommand { get; }
    public ICommand ToggleAutopilotCommand { get; }

}
