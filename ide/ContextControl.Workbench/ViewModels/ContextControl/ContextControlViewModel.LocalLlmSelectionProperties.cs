// CC-DESC: Route, model selection, local LLM filters, model IDs, and Ollama storage bindable properties.

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
    public string SelectedRoute
    {
        get => _selectedRoute;
        set
        {
            var clean = string.IsNullOrWhiteSpace(value) ? RouteOptions[0] : value;
            if (SetProperty(ref _selectedRoute, clean))
            {
                _settings.SelectedAiRoute = clean;
                ProviderStatus = clean.StartsWith("Local:", StringComparison.OrdinalIgnoreCase)
                    ? "Local Ollama route selected"
                    : clean.StartsWith("API:", StringComparison.OrdinalIgnoreCase)
                    ? "API profile selected"
                    : "Browser profile selected";
                SaveSettingsQuietly();
            }
        }
    }

    public LocalLlmModelViewModel? SelectedLocalModel
    {
        get => _selectedLocalModel;
        set
        {
            if (value is null
                && _selectedLocalModel is not null
                && InstalledLocalModels.Contains(_selectedLocalModel))
            {
                OnPropertyChanged(nameof(SelectedLocalModel));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                return;
            }

            if (value is not null && !value.CanUseInLocalChat)
            {
                return;
            }

            if (SetProperty(ref _selectedLocalModel, value))
            {
                if (value is not null)
                {
                    _settings.SelectedLocalModel = value.Id;
                    SaveSettingsQuietly();
                    SaveChatHistory();
                }

                OnPropertyChanged(nameof(SelectedLocalModelLabel));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
                OnPropertyChanged(nameof(PromptFooterSummary));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
            }
        }
    }

    public LocalLlmModelViewModel? SelectedImageGenerationModel
    {
        get => _selectedImageGenerationModel;
        set
        {
            if (value is null
                && _selectedImageGenerationModel is not null
                && InstalledImageGenerationModels.Contains(_selectedImageGenerationModel))
            {
                OnPropertyChanged(nameof(SelectedImageGenerationModel));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                return;
            }

            if (value is not null
                && (!value.IsImageGenerationModel
                    || !value.IsBackendPlatformSupported
                    || (!value.IsInstalled && !value.CanUseManualBackend)))
            {
                return;
            }

            if (SetProperty(ref _selectedImageGenerationModel, value))
            {
                if (value is not null)
                {
                    _settings.SelectedImageModel = value.Id;
                    SaveSettingsQuietly();
                    SaveChatHistory();
                    WarnIfHuggingFaceTokenMissing(value);
                }

                OnPropertyChanged(nameof(SelectedLocalModelLabel));
                OnPropertyChanged(nameof(SelectedActiveLocalModel));
                OnPropertyChanged(nameof(PromptContextPressureLabel));
                OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
                OnPropertyChanged(nameof(PromptFooterSummary));
                OnPropertyChanged(nameof(PromptModelCapabilityHint));
                OnPropertyChanged(nameof(HasPromptModelCapabilityHint));
            }
        }
    }

    public LocalLlmModelViewModel? SelectedActiveLocalModel
    {
        get => IsImageGenPromptMode ? SelectedImageGenerationModel : SelectedLocalModel;
        set
        {
            if (value is null)
            {
                return;
            }

            if (IsImageGenPromptMode)
            {
                if (value.IsImageGenerationModel)
                {
                    SelectedImageGenerationModel = value;
                }
            }
            else if (value.CanUseInLocalChat)
            {
                SelectedLocalModel = value;
            }
        }
    }

    public IEnumerable<LocalLlmModelViewModel> ActiveInstalledLocalModels =>
        IsImageGenPromptMode ? InstalledImageGenerationModels : InstalledLocalModels;

    public string SelectedLocalModelLabel => SelectedActiveLocalModel?.DisplayName ?? "No installed local model";

    public string SelectedLocalLlmSortOption
    {
        get => _selectedLocalLlmSortOption;
        set
        {
            var clean = NormalizeLocalLlmSortOption(value);
            if (SetProperty(ref _selectedLocalLlmSortOption, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string LocalLlmSearchText
    {
        get => _localLlmSearchText;
        set
        {
            if (SetProperty(ref _localLlmSearchText, value ?? ""))
            {
                ApplyLocalLlmFilters();
            }
        }
    }

    public string DependencySearchText
    {
        get => _dependencySearchText;
        set
        {
            if (SetProperty(ref _dependencySearchText, value ?? ""))
            {
                ApplyDependencyFilters();
            }
        }
    }

    public string SelectedLocalLlmProviderFilter
    {
        get => _selectedLocalLlmProviderFilter;
        set
        {
            var clean = CleanLocalLlmFilter(value, LlmProviderAll);
            if (SetProperty(ref _selectedLocalLlmProviderFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string SelectedLocalLlmSourceFilter
    {
        get => _selectedLocalLlmSourceFilter;
        set
        {
            var clean = NormalizeLocalLlmSourceFilter(value);
            if (SetProperty(ref _selectedLocalLlmSourceFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string SelectedLocalLlmPurposeFilter
    {
        get => _selectedLocalLlmPurposeFilter;
        set
        {
            var clean = CleanLocalLlmFilter(value, LlmPurposeAll);
            if (SetProperty(ref _selectedLocalLlmPurposeFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string SelectedLocalLlmBaseFilter
    {
        get => _selectedLocalLlmBaseFilter;
        set
        {
            var clean = CleanLocalLlmFilter(value, LlmBaseAll);
            if (SetProperty(ref _selectedLocalLlmBaseFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string SelectedLocalLlmContextFilter
    {
        get => _selectedLocalLlmContextFilter;
        set
        {
            var clean = NormalizeLocalLlmContextFilter(value);
            if (SetProperty(ref _selectedLocalLlmContextFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string SelectedLocalLlmRequirementFilter
    {
        get => _selectedLocalLlmRequirementFilter;
        set
        {
            var clean = NormalizeLocalLlmRequirementFilter(value);
            if (SetProperty(ref _selectedLocalLlmRequirementFilter, clean))
            {
                PersistLocalLlmFilterSettings();
                ApplyLocalLlmFilters();
            }
        }
    }

    public string FileRequestModelId
    {
        get => _fileRequestModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:1.5b");
            if (SetProperty(ref _fileRequestModelId, clean))
            {
                _settings.FileRequestModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string PatchWriteModelId
    {
        get => _patchWriteModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:3b");
            if (SetProperty(ref _patchWriteModelId, clean))
            {
                _settings.PatchWriteModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string PatchReviewModelId
    {
        get => _patchReviewModelId;
        set
        {
            var clean = CleanModelId(value, "phi4-mini");
            if (SetProperty(ref _patchReviewModelId, clean))
            {
                _settings.PatchReviewModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string ChatModelId
    {
        get => _chatModelId;
        set
        {
            var clean = CleanModelId(value, "qwen2.5-coder:3b");
            if (SetProperty(ref _chatModelId, clean))
            {
                _settings.ChatModel = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string CodexModelId
    {
        get => string.IsNullOrWhiteSpace(_codexModelId) ? DefaultCodexModelId : _codexModelId;
        set
        {
            var clean = CleanCodexOption(value);
            if (SetProperty(ref _codexModelId, clean))
            {
                _settings.CodexModel = clean;
                OnPropertyChanged(nameof(CodexModelStatus));
                OnPropertyChanged(nameof(PromptFooterSummary));
                SaveSettingsQuietly();
            }
        }
    }

    public string CodexReasoningEffort
    {
        get => string.IsNullOrWhiteSpace(_codexReasoningEffort) ? DefaultCodexReasoningEffort : _codexReasoningEffort;
        set
        {
            var clean = NormalizeCodexReasoningOption(value);
            if (SetProperty(ref _codexReasoningEffort, clean))
            {
                _settings.CodexReasoningEffort = clean;
                OnPropertyChanged(nameof(CodexModelStatus));
                OnPropertyChanged(nameof(PromptFooterSummary));
                SaveSettingsQuietly();
            }
        }
    }

    public string CodexModelStatus
    {
        get
        {
            var model = string.IsNullOrWhiteSpace(_codexModelId) ? DefaultCodexModelId : _codexModelId;
            var reasoning = string.IsNullOrWhiteSpace(_codexReasoningEffort) ? DefaultCodexReasoningEffort : _codexReasoningEffort;
            reasoning = $"{reasoning} reasoning";
            return $"{model}; {reasoning}";
        }
    }

    private (string Model, string ReasoningEffort) ResolveCodexExecutionSettings(ContextCapsulePhase phase)
    {
        var model = string.IsNullOrWhiteSpace(_codexModelId) ? DefaultCodexModelId : _codexModelId;
        var reasoning = string.IsNullOrWhiteSpace(_codexReasoningEffort) ? DefaultCodexReasoningEffort : _codexReasoningEffort;
        if (string.Equals(model, CustomFlowCodexModelId, StringComparison.OrdinalIgnoreCase))
        {
            var flowSettings = _skillbookService.ResolveCodexFlowSettings(phase);
            var flowModel = CleanCodexOption(flowSettings.Model);
            var flowReasoning = NormalizeCodexReasoningOption(flowSettings.ReasoningEffort);
            return (
                string.IsNullOrWhiteSpace(flowModel) ? DefaultCodexModelId : flowModel,
                string.IsNullOrWhiteSpace(flowReasoning) ? reasoning : flowReasoning);
        }

        return (model, reasoning);
    }

    private string BuildCodexExecutionStatus(ContextCapsulePhase phase)
    {
        var settings = ResolveCodexExecutionSettings(phase);
        if (string.Equals(CodexModelId, CustomFlowCodexModelId, StringComparison.OrdinalIgnoreCase))
        {
            return $"{CustomFlowCodexModelId} -> {settings.Model}; {settings.ReasoningEffort} reasoning";
        }

        return $"{settings.Model}; {settings.ReasoningEffort} reasoning";
    }

    public string OllamaModelsDirectory
    {
        get => _ollamaModelsDirectory;
        set
        {
            var clean = string.IsNullOrWhiteSpace(value)
                ? LocalLlmService.ResolveOllamaModelsDirectory(null)
                : value.Trim();
            if (SetProperty(ref _ollamaModelsDirectory, clean))
            {
                _settings.OllamaModelsDirectory = clean;
                SaveSettingsQuietly();
            }
        }
    }

    public string OllamaModelsDirectoryStatus
    {
        get => _ollamaModelsDirectoryStatus;
        private set
        {
            if (SetProperty(ref _ollamaModelsDirectoryStatus, value ?? ""))
            {
                UpdateOllamaBackendDependency();
            }
        }
    }

    public string HuggingFaceToken
    {
        get => _huggingFaceToken;
        set
        {
            var clean = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (SetProperty(ref _huggingFaceToken, clean))
            {
                _settings.HuggingFaceToken = clean;
                LocalLlmService.ApplyHuggingFaceTokenToProcess(clean);
                OnPropertyChanged(nameof(HuggingFaceTokenStatus));
                OnPropertyChanged(nameof(HasHuggingFaceToken));
                SaveSettingsQuietly();
            }
        }
    }

    public string HuggingFaceTokenStatus => LocalLlmService.ResolveHuggingFaceTokenStatus(_huggingFaceToken);

    public bool HasHuggingFaceToken => LocalLlmService.HasHuggingFaceToken(_huggingFaceToken);

    private void WarnIfHuggingFaceTokenMissing(LocalLlmModelViewModel model)
    {
        if (!model.UsesHuggingFaceHubDownload || HasHuggingFaceToken)
        {
            return;
        }

        PhaseTitle = "HF token recommended";
        PhaseDetail = model.HuggingFaceTokenWarning;
        Log("warn", $"{model.DisplayName}: {model.HuggingFaceTokenWarning}");
    }

    private static string CleanCodexOption(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        return string.Equals(clean, CodexDefaultOptionLabel, StringComparison.OrdinalIgnoreCase) ? "" : clean;
    }

    private static string NormalizeCodexReasoningOption(string? value)
    {
        return CleanCodexOption(value).ToLowerInvariant() switch
        {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            "xhigh" => "xhigh",
            _ => ""
        };
    }

}
