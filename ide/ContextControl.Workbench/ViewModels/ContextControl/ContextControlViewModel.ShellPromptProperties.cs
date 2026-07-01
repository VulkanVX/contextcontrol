// CC-DESC: Prompt, dock, timeline, install, transfer, and terminal bindable properties.

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

public sealed partial class ContextControlViewModel
{
    private const string CodexPromptLoginMessage = "Please log in to Codex to use it";
    private const string CodexPromptInstallMessage = "Install Codex CLI or open the official Codex CLI guide";
    private const string CodexPromptAuthorizeMessage = "Authorize Codex to use Codex mode";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
                OnPromptBarLayoutChanged();
                RaiseCommandStates();
            }
        }
    }

    public bool IsPromptOpen
    {
        get => _isPromptOpen;
        set
        {
            if (SetProperty(ref _isPromptOpen, value))
            {
                if (!value)
                {
                    IsPromptTypingActive = false;
                }

                OnPromptBarLayoutChanged();
                OnPropertyChanged(nameof(PromptBarOpacity));
                _settings.PromptBarOpenByDefault = value;
                SaveSettingsQuietly();
                SaveChatHistory();
            }
        }
    }

    public double PromptBarHeight => IsPromptOpen
        ? CalculatePromptBarBaseHeight()
        : 0;

    public double PromptBarOpacity => IsPromptOpen ? 1 : 0;

    private void OnPromptBarLayoutChanged()
    {
        OnPropertyChanged(nameof(PromptBarHeight));
    }

    public bool IsPromptTypingActive
    {
        get => _isPromptTypingActive;
        private set
        {
            if (SetProperty(ref _isPromptTypingActive, value))
            {
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsLogPanelOpen => string.Equals(_dockPanelKey, "log", StringComparison.OrdinalIgnoreCase);

    public bool IsChatPanelOpen => string.Equals(_dockPanelKey, "chat", StringComparison.OrdinalIgnoreCase);

    public bool IsCcTimelineExpanded
    {
        get => _isCcTimelineExpanded;
        set
        {
            if (SetProperty(ref _isCcTimelineExpanded, value))
            {
                OnPropertyChanged(nameof(CcTimelineToggleLabel));
                OnPropertyChanged(nameof(IsCcTimelinePanelVisible));
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsAutopilotEnabled
    {
        get => _isAutopilotEnabled;
        private set
        {
            if (SetProperty(ref _isAutopilotEnabled, value))
            {
                OnPropertyChanged(nameof(AutopilotModeLabel));
                OnPropertyChanged(nameof(AutopilotModeToolTip));
                OnPropertyChanged(nameof(IsCcTimelinePanelVisible));
                OnPropertyChanged(nameof(IsCcTimelineToggleVisible));
                OnPropertyChanged(nameof(IsDirSendMissingRequestWarning));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                OnPromptBarLayoutChanged();
                _settings.IsAutopilotEnabled = value;
                SaveSettingsQuietly();
            }
        }
    }

    public string AutopilotModeLabel => IsAutopilotEnabled ? "CC flow on" : "Raw on";

    public string AutopilotModeToolTip => IsAutopilotEnabled
        ? "CC flow is active. Click to switch to raw chat, which sends only your text."
        : "Raw chat is active. Click to switch to CC flow, which sends ContextControl capsules with DIR/CC attachments and workflow instructions.";

    public bool IsRefreshingLocalModels
    {
        get => _isRefreshingLocalModels;
        private set
        {
            if (SetProperty(ref _isRefreshingLocalModels, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsInstallingOllama
    {
        get => _isInstallingOllama;
        private set
        {
            if (SetProperty(ref _isInstallingOllama, value))
            {
                OnPropertyChanged(nameof(OllamaInstallButtonLabel));
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                RaiseCommandStates();
            }
        }
    }

    public bool IsOllamaInstalled
    {
        get => _isOllamaInstalled;
        private set
        {
            if (SetProperty(ref _isOllamaInstalled, value))
            {
                OnPropertyChanged(nameof(OllamaInstallButtonLabel));
                OnPropertyChanged(nameof(LlmCompactInfoLabel));
                UpdateOllamaBackendDependency();
                RaiseCommandStates();
            }
        }
    }

    public bool IsOllamaInstallerPromptOpen
    {
        get => _isOllamaInstallerPromptOpen;
        private set => SetProperty(ref _isOllamaInstallerPromptOpen, value);
    }

    public string OllamaInstallerPromptMessage =>
        "Automatic Ollama installation is disabled because it is not ContextControl-local.";

    public bool IsExternalDependencyDeletePromptOpen
    {
        get => _isExternalDependencyDeletePromptOpen;
        private set => SetProperty(ref _isExternalDependencyDeletePromptOpen, value);
    }

    public string ExternalDependencyDeletePromptTitle => _pendingExternalDependencyDelete is { } dependency
        ? $"Force install {dependency.DisplayName}"
        : "Force install dependency";

    public string ExternalDependencyDeletePromptMessage =>
        "Our systems detected that this dependency is most likely outside the Context Control managed dependency control flow."
        + Environment.NewLine
        + Environment.NewLine
        + "Deleting it might result in issues with your existing development environment.";

    public string OllamaInstallButtonLabel => IsOllamaInstalled
        ? "Ollama installed"
        : "Open Ollama";

    public string OllamaInstallerSizeLabel => "External app install; models below download separately";

    public string OllamaInstallCommandLabel => LocalLlmService.OllamaDownloadPageUrl;

    public bool ShowBusyPromptProgress => IsBusy && !IsTransferProgressActive && !HasChatRequestProgress;

    public bool IsTransferProgressActive
    {
        get => _isTransferProgressActive;
        private set
        {
            if (SetProperty(ref _isTransferProgressActive, value))
            {
                OnPropertyChanged(nameof(ShowBusyPromptProgress));
                OnPropertyChanged(nameof(IsTransferProgressLoading));
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsTransferProgressIndeterminate
    {
        get => _isTransferProgressIndeterminate;
        private set => SetProperty(ref _isTransferProgressIndeterminate, value);
    }

    public double TransferProgressValue
    {
        get => _transferProgressValue;
        private set => SetProperty(ref _transferProgressValue, Math.Clamp(value, 0, 100));
    }

    public string TransferProgressTitle
    {
        get => _transferProgressTitle;
        private set => SetProperty(ref _transferProgressTitle, value);
    }

    public string TransferProgressStatus
    {
        get => _transferProgressStatus;
        private set
        {
            if (SetProperty(ref _transferProgressStatus, value))
            {
                OnPropertyChanged(nameof(TransferProgressHistoryPositionLabel));
            }
        }
    }

    public string TransferProgressSizeLabel
    {
        get => _transferProgressSizeLabel;
        private set => SetProperty(ref _transferProgressSizeLabel, value);
    }

    public string TransferProgressSpeedLabel
    {
        get => _transferProgressSpeedLabel;
        private set => SetProperty(ref _transferProgressSpeedLabel, value);
    }

    public string TransferProgressPercentLabel
    {
        get => _transferProgressPercentLabel;
        private set => SetProperty(ref _transferProgressPercentLabel, value);
    }

    public string TransferProgressHistoryPositionLabel =>
        _transferProgressHistory.Count == 0 || _transferProgressHistoryIndex < 0
            ? ""
            : $"{_transferProgressHistoryIndex + 1:N0}/{_transferProgressHistory.Count:N0}";

    public bool IsTransferProgressLoading => IsTransferProgressActive && !_isTransferProgressDismissible;

    public bool CanCloseTransferProgress =>
        IsTransferProgressActive
        && (_isTransferProgressDismissible
            || _transferProgressCancellation is { IsCancellationRequested: false });

    public bool IsPatchPlanReady
    {
        get => _isPatchPlanReady;
        private set
        {
            if (SetProperty(ref _isPatchPlanReady, value))
            {
                RaiseCommandStates();
                RefreshPromptFlowSteps();
            }
        }
    }

    public string PhaseTitle
    {
        get => _phaseTitle;
        private set
        {
            if (SetProperty(ref _phaseTitle, value))
            {
                UpdateCcTimelineFromStatus();
                RefreshPromptFlowSteps();
            }
        }
    }

    public string PhaseDetail
    {
        get => _phaseDetail;
        private set
        {
            if (SetProperty(ref _phaseDetail, value))
            {
                MirrorPhaseStatusToTerminal();
                UpdateCcTimelineFromStatus();
                RefreshPromptFlowSteps();
            }
        }
    }

    public string PromptText
    {
        get => _promptText;
        set
        {
            var nextText = value ?? "";
            var wasLargePrompt = _isLargePrompt;
            var isLargePrompt = IsLargePromptText(nextText);
            if (SetProperty(ref _promptText, nextText))
            {
                _isLargePrompt = isLargePrompt;
                SavePromptDraftToSelectedChat();
                OnPropertyChanged(nameof(PromptTokenomicsLabel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
                OnPropertyChanged(nameof(PromptFooterSummary));
                OnPropertyChanged(nameof(IsDirSendMissingRequestWarning));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                RefreshPromptFlowSteps();
                if (wasLargePrompt != isLargePrompt)
                {
                    OnPropertyChanged(nameof(IsLargePrompt));
                    OnPropertyChanged(nameof(PromptTextWrapping));
                    OnPropertyChanged(nameof(PromptHorizontalScrollBarVisibility));
                    OnPropertyChanged(nameof(PromptFooterSummary));
                    if (isLargePrompt)
                    {
                        IsPromptTypingActive = false;
                    }

                    OnPromptBarLayoutChanged();
                    return;
                }

                if (!isLargePrompt && IsPromptTypingActive)
                {
                    OnPromptBarLayoutChanged();
                }
            }
        }
    }

    public bool IsLargePrompt => _isLargePrompt;

    public TextWrapping PromptTextWrapping => IsLargePrompt ? TextWrapping.NoWrap : TextWrapping.Wrap;

    public ScrollBarVisibility PromptHorizontalScrollBarVisibility =>
        IsLargePrompt ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

    public string PromptModeKey
    {
        get => _promptModeKey;
        private set
        {
            var clean = NormalizePromptModeKey(value);
            var previous = _promptModeKey;
            var conversationKindChanged = !string.Equals(
                ResolveConversationKindForPromptMode(previous),
                ResolveConversationKindForPromptMode(clean),
                StringComparison.OrdinalIgnoreCase);
            if (conversationKindChanged)
            {
                SaveChatHistory();
            }

            if (SetProperty(ref _promptModeKey, clean))
            {
                OnPropertyChanged(nameof(IsContextPromptMode));
                OnPropertyChanged(nameof(IsChatPromptMode));
                OnPropertyChanged(nameof(IsLocalPromptMode));
                OnPropertyChanged(nameof(IsImageGenPromptMode));
                OnPropertyChanged(nameof(IsLocalOrImageGenPromptMode));
                OnPropertyChanged(nameof(IsCodexPromptMode));
                OnPropertyChanged(nameof(IsTerminalPromptMode));
                OnPropertyChanged(nameof(IsMessagePromptMode));
                OnPropertyChanged(nameof(PromptModeDropdownLabel));
                OnPropertyChanged(nameof(PromptModeDropdownToolTip));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                OnPropertyChanged(nameof(IsDirSendMissingRequestWarning));
                OnPropertyChanged(nameof(AttachmentSummary));
                OnPropertyChanged(nameof(AttachmentButtonLabel));
                OnPropertyChanged(nameof(ActiveInstalledLocalModels));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                OnPropertyChanged(nameof(SelectedLocalModelLabel));
                OnPropertyChanged(nameof(IsCodexPromptAuthBlocked));
                OnPropertyChanged(nameof(IsPromptInputReadOnly));
                OnPropertyChanged(nameof(CodexPromptAuthTitle));
                OnPropertyChanged(nameof(CodexPromptAuthMessage));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                OnPropertyChanged(nameof(CodexStatus));
                OnPropertyChanged(nameof(IsCodexUsagePanelVisible));
                OnPropertyChanged(nameof(CodexUsageToggleLabel));
                OnPropertyChanged(nameof(IsCcPromptChromeVisible));
                OnPropertyChanged(nameof(IsPromptModeSwitcherVisible));
                OnPropertyChanged(nameof(IsCcTimelinePanelVisible));
                OnPropertyChanged(nameof(IsCcTimelineToggleVisible));
                OnPropertyChanged(nameof(IsCcPromptActionRowVisible));
                OnPropertyChanged(nameof(IsLocalModelPickerVisible));
                OnPropertyChanged(nameof(IsImageGenerationModelPickerVisible));
                OnPropertyChanged(nameof(ChatWorkspaceTitle));
                OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
                OnPropertyChanged(nameof(IsChatHeaderLocalModelVisible));
                OnPropertyChanged(nameof(IsChatHeaderCodexStatusVisible));
                OnPropertyChanged(nameof(IsChatHeaderImageModelVisible));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(PromptTokenomicsLabel));
                OnPropertyChanged(nameof(PromptFooterSummary));
                _settings.PromptModeKey = clean;
                SwitchConversationKindForPromptMode(saveCurrent: !conversationKindChanged);

                if (IsCodexPromptAuthBlocked)
                {
                    ShowCodexPromptAuthRequired();
                }

                OnPromptBarLayoutChanged();
                RefreshPromptFlowSteps();
                RaiseCommandStates();
                SaveSettingsQuietly();
                SaveChatHistory();
            }
        }
    }

    public bool IsLocalPromptMode => string.Equals(PromptModeKey, "context", StringComparison.OrdinalIgnoreCase);

    public bool IsImageGenPromptMode => string.Equals(PromptModeKey, "imagegen", StringComparison.OrdinalIgnoreCase);

    public bool IsLocalOrImageGenPromptMode => IsLocalPromptMode || IsImageGenPromptMode;

    public bool IsContextPromptMode => IsLocalPromptMode;

    public bool IsChatPromptMode => IsLocalPromptMode;

    public bool IsCodexPromptMode => string.Equals(PromptModeKey, "codex", StringComparison.OrdinalIgnoreCase);

    public bool IsTerminalPromptMode => string.Equals(PromptModeKey, "terminal", StringComparison.OrdinalIgnoreCase);

    public bool IsMessagePromptMode => !IsTerminalPromptMode;

    public bool IsCodexPromptAuthBlocked => IsCodexPromptMode && !IsCodexAuthenticated;

    public bool IsPromptInputReadOnly => IsCodexPromptAuthBlocked;

    public string CodexPromptAuthTitle => IsCodexCliInstalled ? CodexPromptAuthorizeMessage : "Install Codex CLI";

    public string CodexPromptAuthMessage => IsCodexCliInstalled ? CodexPromptLoginMessage : CodexPromptInstallMessage;

    public string PromptWatermark => IsCodexPromptAuthBlocked
        ? CodexPromptAuthMessage
        : IsImageGenPromptMode
        ? "Describe the image you want to generate..."
        : IsChatPromptMode
        ? "Ask the selected local model, or paste CC request/patch text..."
        : IsCodexPromptMode ? "Ask Codex through the ContextControl DIR -> CC -> GO flow..."
        : IsTerminalPromptMode ? "Terminal output"
        : "Message Context Control...";

    public string PromptSendButtonLabel => IsImageGenPromptMode
        ? "Generate"
        : IsCodexPromptAuthBlocked ? IsCodexCliInstalled ? "Login required" : "Install required"
        : IsCodexPromptMode ? "Send to Codex" : "Send";

    public bool IsDirSendMissingRequestWarning =>
        IsAutopilotEnabled
        && IsMessagePromptMode
        && HasIncludedAttachmentKind("dir")
        && !HasIncludedAttachmentKind("code")
        && !HasIncludedAttachmentKind("patch")
        && !IsMeaningfulTaskPrompt(PromptText);

    public string PromptSendButtonToolTip => IsDirSendMissingRequestWarning
        ? "This message doesn't have a request. You'll waste tokens on nothing"
        : IsImageGenPromptMode
            ? "Generate image"
            : IsCodexPromptMode
                ? "Send to Codex"
                : "Send";

    public string PromptModelCapabilityHint
    {
        get
        {
            if (IsCodexPromptMode)
            {
                return CodexStatus;
            }

            if (IsImageGenPromptMode)
            {
                return SelectedImageGenerationModel is null
                    ? "Select an image generation model"
                    : "Prompt-only image generation";
            }

            return SelectedLocalModel?.IsImageModel == true
                ? "Accepts image input"
                : "";
        }
    }

    public bool HasPromptModelCapabilityHint => !string.IsNullOrWhiteSpace(PromptModelCapabilityHint);

    public bool IsCcPromptChromeVisible => IsLocalPromptMode || IsCodexPromptMode;

    public bool IsPromptModeSwitcherVisible => true;

    public string PromptModeDropdownLabel => IsImageGenPromptMode
        ? "ImageGen"
        : IsCodexPromptMode
            ? "Codex CLI"
            : "Local";

    public string PromptModeDropdownToolTip => IsImageGenPromptMode
        ? "ImageGen prompt mode"
        : IsCodexPromptMode
            ? "Codex CLI prompt mode"
            : "Local prompt mode";

    public string PromptPrimaryModeButtonLabel => "Local";

    public string PromptPrimaryModeButtonToolTip => "Local chat and ContextControl CC flow";

    public bool IsCcTimelinePanelVisible => IsCcPromptChromeVisible && IsCcTimelineExpanded && IsAutopilotEnabled;

    public bool IsCcTimelineToggleVisible => IsCcPromptChromeVisible && IsAutopilotEnabled;

    public bool IsCodexUsagePanelExpanded
    {
        get => _isCodexUsagePanelExpanded;
        private set
        {
            if (SetProperty(ref _isCodexUsagePanelExpanded, value))
            {
                OnPropertyChanged(nameof(IsCodexUsagePanelVisible));
                OnPropertyChanged(nameof(CodexUsageToggleLabel));
                OnPromptBarLayoutChanged();
            }
        }
    }

    public bool IsCodexUsagePanelVisible => IsCodexPromptMode && IsCodexUsagePanelExpanded;

    public string CodexUsageToggleLabel => IsCodexUsagePanelVisible ? "Hide usage" : "Usage";

    public string CodexUsageSummary
    {
        get => _codexUsageSummary;
        private set => SetProperty(ref _codexUsageSummary, string.IsNullOrWhiteSpace(value)
            ? "Codex usage appears after a Codex prompt completes."
            : value);
    }

    public string CodexRateLimitSummary
    {
        get => _codexRateLimitSummary;
        private set => SetProperty(ref _codexRateLimitSummary, string.IsNullOrWhiteSpace(value)
            ? "5h and weekly limits appear when Codex reports an account snapshot."
            : value);
    }

    public string CodexFiveHourPercentLeftLabel
    {
        get => _codexFiveHourPercentLeftLabel;
        private set => SetProperty(ref _codexFiveHourPercentLeftLabel, string.IsNullOrWhiteSpace(value) ? "--" : value);
    }

    public string CodexWeeklyPercentLeftLabel
    {
        get => _codexWeeklyPercentLeftLabel;
        private set => SetProperty(ref _codexWeeklyPercentLeftLabel, string.IsNullOrWhiteSpace(value) ? "--" : value);
    }

    public string CodexUsageResetLabel
    {
        get => _codexUsageResetLabel;
        private set => SetProperty(ref _codexUsageResetLabel, string.IsNullOrWhiteSpace(value) ? "minor reset --" : value);
    }

    public string CodexFiveHourResetLabel
    {
        get => _codexFiveHourResetLabel;
        private set => SetProperty(ref _codexFiveHourResetLabel, string.IsNullOrWhiteSpace(value) ? "5h reset --" : value);
    }

    public string CodexWeeklyResetLabel
    {
        get => _codexWeeklyResetLabel;
        private set => SetProperty(ref _codexWeeklyResetLabel, string.IsNullOrWhiteSpace(value) ? "weekly reset --" : value);
    }

    public double CodexFiveHourPercentLeftValue
    {
        get => _codexFiveHourPercentLeftValue;
        private set => SetProperty(ref _codexFiveHourPercentLeftValue, Math.Clamp(value, 0, 100));
    }

    public double CodexWeeklyPercentLeftValue
    {
        get => _codexWeeklyPercentLeftValue;
        private set => SetProperty(ref _codexWeeklyPercentLeftValue, Math.Clamp(value, 0, 100));
    }

    public double CodexFiveHourUsageBarFillWidth
    {
        get => _codexFiveHourUsageBarFillWidth;
        private set => SetProperty(ref _codexFiveHourUsageBarFillWidth, Math.Clamp(value, 0, 98));
    }

    public double CodexWeeklyUsageBarFillWidth
    {
        get => _codexWeeklyUsageBarFillWidth;
        private set => SetProperty(ref _codexWeeklyUsageBarFillWidth, Math.Clamp(value, 0, 98));
    }

    public bool IsCodexFiveHourLimitDepleted
    {
        get => _isCodexFiveHourLimitDepleted;
        private set => SetProperty(ref _isCodexFiveHourLimitDepleted, value);
    }

    public bool IsCodexWeeklyLimitDepleted
    {
        get => _isCodexWeeklyLimitDepleted;
        private set => SetProperty(ref _isCodexWeeklyLimitDepleted, value);
    }

    public bool IsCcPromptActionRowVisible => IsLocalPromptMode || IsCodexPromptMode;

    public bool IsLocalModelPickerVisible => IsLocalPromptMode;

    public bool IsImageGenerationModelPickerVisible => IsImageGenPromptMode;

    public bool IsChatHeaderLocalModelVisible => IsLocalPromptMode;

    public bool IsChatHeaderCodexStatusVisible => IsCodexPromptMode;

    public bool IsChatHeaderImageModelVisible => IsImageGenPromptMode;

    public string ChatWorkspaceTitle => IsImageGenPromptMode
        ? "ImageGen:"
        : IsCodexPromptMode
            ? "Codex CLI:"
            : "Local:";

    public string ChatWorkspaceSubtitle
    {
        get
        {
            var tokenLabel = SelectedChatSession?.TotalConsumedTokenEstimateBreakdownLabel
                ?? "0 tok est - 0 in - 0 out";
            if (IsCodexPromptMode)
            {
                return $"{tokenLabel} | read-only harness | no repo browsing";
            }

            return tokenLabel;
        }
    }

    public bool IsCodexRequestRunning
    {
        get => _isCodexRequestRunning;
        private set
        {
            if (SetProperty(ref _isCodexRequestRunning, value))
            {
                OnPropertyChanged(nameof(CodexStatus));
                OnPropertyChanged(nameof(IsCodexPromptAuthBlocked));
                OnPropertyChanged(nameof(IsPromptInputReadOnly));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                (InstallCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (OpenCodexGuideCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (OpenCodexLoginCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (LogoutCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RefreshCodexStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (CancelCodexRequestCommand as RelayCommand<ChatRequestProgressViewModel>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCodexCliInstalled
    {
        get => _isCodexCliInstalled;
        private set
        {
            if (SetProperty(ref _isCodexCliInstalled, value))
            {
                OnPropertyChanged(nameof(CodexLoginButtonLabel));
                OnPropertyChanged(nameof(CodexInstallButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(CodexSetupStatusKind));
                OnPropertyChanged(nameof(CodexPromptAuthTitle));
                OnPropertyChanged(nameof(CodexPromptAuthMessage));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                (OpenCodexLoginCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (InstallCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInstallingCodex
    {
        get => _isInstallingCodex;
        private set
        {
            if (SetProperty(ref _isInstallingCodex, value))
            {
                OnPropertyChanged(nameof(CodexInstallButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                (InstallCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (OpenCodexLoginCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCodexAuthenticated
    {
        get => _isCodexAuthenticated;
        private set
        {
            if (SetProperty(ref _isCodexAuthenticated, value))
            {
                OnPropertyChanged(nameof(CodexLoginButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(CodexSetupStatusKind));
                OnPropertyChanged(nameof(IsCodexPromptAuthBlocked));
                OnPropertyChanged(nameof(IsPromptInputReadOnly));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
                if (IsCodexPromptAuthBlocked)
                {
                    ShowCodexPromptAuthRequired();
                }

                RaiseCommandStates();
            }
        }
    }

    public bool IsCodexLoginRequired
    {
        get => _isCodexLoginRequired;
        private set
        {
            if (SetProperty(ref _isCodexLoginRequired, value))
            {
                OnPropertyChanged(nameof(CodexLoginButtonLabel));
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(CodexSetupStatusKind));
                OnPropertyChanged(nameof(IsCodexPromptAuthBlocked));
                OnPropertyChanged(nameof(IsPromptInputReadOnly));
                OnPropertyChanged(nameof(PromptWatermark));
                OnPropertyChanged(nameof(PromptSendButtonLabel));
                OnPropertyChanged(nameof(PromptSendButtonToolTip));
                RaiseCommandStates();
            }
        }
    }

    public bool IsRefreshingCodexStatus
    {
        get => _isRefreshingCodexStatus;
        private set
        {
            if (SetProperty(ref _isRefreshingCodexStatus, value))
            {
                OnPropertyChanged(nameof(CodexSetupSummary));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                (RefreshCodexStatusCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (RunCodexDoctorCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
                (LogoutCodexCommand as RelayCommand<object>)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CodexLoginButtonLabel => IsCodexAuthenticated ? "Relogin" : "Login";

    public string CodexInstallButtonLabel => IsInstallingCodex ? "Installing" : IsCodexCliInstalled ? "Reinstall" : "Install";

    public string CodexSetupSummary => IsRefreshingCodexStatus
        ? "Checking Codex CLI login..."
        : IsInstallingCodex
            ? "Installing Codex CLI..."
        : IsCodexAuthenticated
            ? "Codex login ready"
            : !IsCodexCliInstalled
                ? "Codex CLI install required"
            : IsCodexLoginRequired
                ? "Codex login required"
                : "Codex setup pending";

    public string CodexSetupStatusKind => IsCodexAuthenticated ? "ready" : !IsCodexCliInstalled ? "install" : IsCodexLoginRequired ? "login" : "pending";

    public string CodexStatus
    {
        get => _codexStatus;
        private set
        {
            if (SetProperty(ref _codexStatus, string.IsNullOrWhiteSpace(value) ? "Codex CLI read-only CC capsule" : value))
            {
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
            }
        }
    }

    public string TerminalOutputText
    {
        get => _terminalOutputText;
        private set => SetProperty(ref _terminalOutputText, value ?? "");
    }

    private static string NormalizePromptModeKey(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "codex" => "codex",
            "imagegen" or "image-gen" or "image" => "imagegen",
            "terminal" => "terminal",
            _ => "context"
        };
    }

}
