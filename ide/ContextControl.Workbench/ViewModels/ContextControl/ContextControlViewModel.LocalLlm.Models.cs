// CC-DESC: Extracted ContextControlViewModel system slice.
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
    private async Task PullLocalModelAsync(LocalLlmModelViewModel? model)
    {
        if (model is null || IsBusy)
        {
            return;
        }

        if (model.CanUninstall)
        {
            await UninstallLocalModelAsync(model);
            return;
        }

        if (!model.CanPull && model.CanInstallDependency)
        {
            await InstallBackendDependencyForModelAsync(model);
            return;
        }

        if (model.CanDownloadBackendModel)
        {
            await DownloadBackendModelAsync(model);
            return;
        }

        if (!model.CanPull)
        {
            PhaseTitle = "Model download unavailable";
            PhaseDetail = $"{model.DisplayName} does not have a direct downloader for {model.BackendRequirementLabel} yet.";
            Log("warn", PhaseDetail);
            return;
        }

        model.IsPulling = true;
        RaiseCommandStates();
        try
        {
            await RunBusyAsync($"Pull {model.Id}", async () =>
            {
                LocalLlmStatus = $"Downloading {model.Id}...";
                PhaseTitle = "Downloading model";
                PhaseDetail = model.PullCommand;
                var pullCancellation = new CancellationTokenSource();
                var progress = CreateTransferProgress($"Downloading {model.Id}", pullCancellation);
                var terminal = CreateTerminalProgress();
                try
                {
                    var result = await _localLlmService.PullModelAsync(model.Id, progress, terminal, pullCancellation.Token);
                    LocalLlmStatus = result.Status;
                    PhaseTitle = result.Succeeded ? "Model ready" : "Pull failed";
                    PhaseDetail = result.Status;
                    CompleteTransferProgress(result.Status, result.Succeeded);
                    Log(result.Succeeded ? "ok" : "warn", result.Status);

                    if (result.Succeeded)
                    {
                        await RefreshLocalModelsAsync(LocalModelRefreshDepth.Fast);
                    }
                }
                catch (OperationCanceledException)
                {
                    LocalLlmStatus = $"Download canceled: {model.Id}";
                    PhaseTitle = "Download canceled";
                    PhaseDetail = model.Id;
                    CompleteTransferProgress($"Download canceled: {model.Id}", succeeded: false);
                    Log("warn", $"Download canceled: {model.Id}");
                }
                finally
                {
                    pullCancellation.Dispose();
                }
            });
        }
        finally
        {
            model.IsPulling = false;
            RaiseCommandStates();
        }
    }

    private async Task UninstallLocalModelAsync(LocalLlmModelViewModel model)
    {
        model.IsPulling = true;
        RaiseCommandStates();
        try
        {
            await RunBusyAsync($"Uninstall {model.Id}", async () =>
            {
                LocalLlmStatus = $"Uninstalling {model.Id}...";
                PhaseTitle = "Uninstalling model";
                PhaseDetail = $"ollama rm {model.Id}";
                var terminal = CreateTerminalProgress();
                var result = await _localLlmService.UninstallModelAsync(model.Id, terminal);
                LocalLlmStatus = result.Status;
                PhaseTitle = result.Succeeded ? "Model uninstalled" : "Uninstall failed";
                PhaseDetail = result.Status;
                Log(result.Succeeded ? "ok" : "warn", result.Status);

                if (result.Succeeded)
                {
                    model.MarkUninstalled();
                    RefreshInstalledLocalModels();
                    ApplyLocalLlmFilters();
                    RaiseCommandStates();
                    await RefreshLocalModelInstallStateAsync();
                }
            });
        }
        finally
        {
            model.IsPulling = false;
            RaiseCommandStates();
        }
    }

    private async Task DownloadBackendModelAsync(LocalLlmModelViewModel model)
    {
        model.IsPulling = true;
        RaiseCommandStates();
        try
        {
            await RunBusyAsync($"Download {model.Id}", async () =>
            {
                LocalLlmStatus = $"Downloading {model.Id}...";
                PhaseTitle = "Downloading image model";
                PhaseDetail = $"{model.Id} through {model.BackendRequirementLabel}";
                var pullCancellation = new CancellationTokenSource();
                var progress = CreateTransferProgress($"Downloading {model.Id}", pullCancellation);
                var terminal = CreateTerminalProgress();
                if (model.UsesHuggingFaceHubDownload && !HasHuggingFaceToken)
                {
                    terminal.Report(model.HuggingFaceTokenWarning);
                }

                try
                {
                    var result = await _localLlmService.DownloadImageModelAsync(model.Id, progress, terminal, pullCancellation.Token);
                    LocalLlmStatus = result.Status;
                    PhaseTitle = result.Succeeded ? "Image model ready" : "Model download failed";
                    PhaseDetail = result.Status;
                    CompleteTransferProgress(result.Status, result.Succeeded);
                    Log(result.Succeeded ? "ok" : "warn", result.Status);

                    if (result.Succeeded)
                    {
                        model.ApplyBackendModelState(true);
                        RefreshInstalledLocalModels();
                        ApplyLocalLlmFilters();
                        RaiseCommandStates();
                    }
                }
                catch (OperationCanceledException)
                {
                    LocalLlmStatus = $"Download canceled: {model.Id}";
                    PhaseTitle = "Download canceled";
                    PhaseDetail = model.Id;
                    CompleteTransferProgress($"Download canceled: {model.Id}", succeeded: false);
                    Log("warn", $"Download canceled: {model.Id}");
                }
                finally
                {
                    pullCancellation.Dispose();
                }
            });
        }
        finally
        {
            model.IsPulling = false;
            RaiseCommandStates();
        }
    }

    private async Task InstallBackendDependencyForModelAsync(LocalLlmModelViewModel model)
    {
        var dependency = LlmBackendDependencies.FirstOrDefault(item =>
            item.Id.Equals(model.DependencyId, StringComparison.OrdinalIgnoreCase));
        if (dependency is null)
        {
            PhaseTitle = "Dependency unknown";
            PhaseDetail = $"{model.DisplayName} requires {model.BackendRequirementLabel}, but no installer mapping exists yet.";
            Log("warn", PhaseDetail);
            return;
        }

        await InstallBackendDependencyAsync(dependency);
    }

    private void ApplyLocalModelRefresh(LocalLlmRefreshResult result, bool preserveBackendModelStates = false)
    {
        foreach (var unknownModelId in result.UnknownInstalledModelIds)
        {
            if (LocalLlmModels.Any(model => string.Equals(model.Id, unknownModelId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            LocalLlmModels.Add(new LocalLlmModelViewModel(CreateUnknownInstalledModel(unknownModelId)));
            if (!LocalModelIdOptions.Contains(unknownModelId, StringComparer.OrdinalIgnoreCase))
            {
                LocalModelIdOptions.Add(unknownModelId);
            }
        }

        foreach (var model in LocalLlmModels)
        {
            var isInstalled = result.InstalledModelIds.Contains(model.Id);
            var isBackendDependencyReady = IsModelBackendDependencyReady(model);
            var isBackendModelReady = preserveBackendModelStates && model.UsesDownloadableBackendModel
                ? model.IsBackendModelReady
                : false;
            var isAvailable = isInstalled
                || (model.IsCloudModel && result.OllamaReachable)
                || (model.IsImageGenerationModel && model.RequiresManualBackend && isBackendDependencyReady);
            model.ApplyState(isInstalled, isAvailable, result.Hardware, isBackendDependencyReady, isBackendModelReady);
        }

        RefreshLocalLlmProviderFilters();
        RefreshLocalLlmPurposeFilters();
        RefreshLocalLlmBaseFilters();
        RefreshInstalledLocalModels();
        ApplyLocalLlmFilters();
        RaiseCommandStates();
    }

    private bool IsModelBackendDependencyReady(LocalLlmModelViewModel model)
    {
        return model.RequiresManualBackend
            && !string.IsNullOrWhiteSpace(model.DependencyId)
            && LlmBackendDependencies.Any(dependency =>
                dependency.IsReady
                && dependency.Id.Equals(model.DependencyId, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyBackendDependencyStatesToModels()
    {
        foreach (var model in LocalLlmModels)
        {
            model.ApplyBackendDependencyState(IsModelBackendDependencyReady(model));
        }

        RefreshInstalledLocalModels();
        ApplyLocalLlmFilters();
        RaiseCommandStates();
    }

    private async Task ApplyBackendModelCacheStatesAsync(
        CancellationToken cancellationToken,
        IProgress<LocalLlmTransferProgress>? progress = null)
    {
        var candidates = LocalLlmModels
            .Where(model => model.UsesDownloadableBackendModel && model.IsBackendDependencyReady)
            .Select(model => model.Id)
            .ToArray();

        if (candidates.Length == 0)
        {
            foreach (var model in LocalLlmModels.Where(model => model.UsesDownloadableBackendModel))
            {
                model.ApplyBackendModelState(false);
            }

            return;
        }

        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Checking cached Diffusers image model files.",
            3,
            4,
            null,
            92));

        var cachedModelIds = await _localLlmService
            .DetectCachedImageModelIdsAsync(candidates, cancellationToken);

        foreach (var model in LocalLlmModels.Where(model => model.UsesDownloadableBackendModel))
        {
            model.ApplyBackendModelState(cachedModelIds.Contains(model.Id));
        }

        RefreshInstalledLocalModels();
        ApplyLocalLlmFilters();
        RaiseCommandStates();
    }

    private void RefreshLocalLlmProviderFilters()
    {
        var selected = SelectedLocalLlmProviderFilter;
        LocalLlmProviderFilters.Clear();
        LocalLlmProviderFilters.Add(LlmProviderAll);
        foreach (var provider in LocalLlmModels
            .Select(model => model.Provider)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase))
        {
            LocalLlmProviderFilters.Add(provider);
        }

        if (!LocalLlmProviderFilters.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            _selectedLocalLlmProviderFilter = LlmProviderAll;
            OnPropertyChanged(nameof(SelectedLocalLlmProviderFilter));
            PersistLocalLlmFilterSettings();
        }
    }

    private void RefreshLocalLlmPurposeFilters()
    {
        var selected = SelectedLocalLlmPurposeFilter;
        LocalLlmPurposeFilters.Clear();
        LocalLlmPurposeFilters.Add(LlmPurposeAll);
        foreach (var purpose in LocalLlmModels
            .SelectMany(model => model.PurposeTags)
            .Where(purpose => !string.IsNullOrWhiteSpace(purpose))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(purpose => purpose, StringComparer.OrdinalIgnoreCase))
        {
            LocalLlmPurposeFilters.Add(purpose);
        }

        if (!LocalLlmPurposeFilters.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            _selectedLocalLlmPurposeFilter = LlmPurposeAll;
            OnPropertyChanged(nameof(SelectedLocalLlmPurposeFilter));
            PersistLocalLlmFilterSettings();
        }
    }

    private void RefreshLocalLlmBaseFilters()
    {
        var selected = SelectedLocalLlmBaseFilter;
        LocalLlmBaseFilters.Clear();
        LocalLlmBaseFilters.Add(LlmBaseAll);
        foreach (var modelBase in LocalLlmModels
            .Select(model => model.ModelBaseLabel)
            .Where(modelBase => !string.IsNullOrWhiteSpace(modelBase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(modelBase => modelBase, StringComparer.OrdinalIgnoreCase))
        {
            LocalLlmBaseFilters.Add(modelBase);
        }

        if (!LocalLlmBaseFilters.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            _selectedLocalLlmBaseFilter = LlmBaseAll;
            OnPropertyChanged(nameof(SelectedLocalLlmBaseFilter));
            PersistLocalLlmFilterSettings();
        }
    }

    private void PersistLocalLlmFilterSettings()
    {
        _settings.LocalLlmSortOption = _selectedLocalLlmSortOption;
        _settings.LocalLlmProviderFilter = _selectedLocalLlmProviderFilter;
        _settings.LocalLlmSourceFilter = _selectedLocalLlmSourceFilter;
        _settings.LocalLlmPurposeFilter = _selectedLocalLlmPurposeFilter;
        _settings.LocalLlmBaseFilter = _selectedLocalLlmBaseFilter;
        _settings.LocalLlmContextFilter = _selectedLocalLlmContextFilter;
        _settings.LocalLlmRequirementFilter = _selectedLocalLlmRequirementFilter;
        SaveSettingsQuietly();
    }

    private static string CleanLocalLlmFilter(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeLocalLlmSortOption(string? value)
    {
        var clean = CleanLocalLlmFilter(value, LlmSortNewest);
        if (clean.Equals(LlmSortOldest, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortOldest;
        }

        if (clean.Equals(LlmSortProvider, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortProvider;
        }

        if (clean.Equals(LlmSortName, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortName;
        }

        if (clean.Equals(LlmSortFit, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortFit;
        }

        if (clean.Equals(LlmSortInstalled, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortInstalled;
        }

        if (clean.Equals(LlmSortContext, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSortContext;
        }

        return LlmSortNewest;
    }

    private static string NormalizeLocalLlmSourceFilter(string? value)
    {
        var clean = CleanLocalLlmFilter(value, LlmSourceAll);
        if (clean.Equals(LlmSourceLocalOnly, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSourceLocalOnly;
        }

        if (clean.Equals(LlmSourceCloudOnly, StringComparison.OrdinalIgnoreCase))
        {
            return LlmSourceCloudOnly;
        }

        return LlmSourceAll;
    }

    private static string NormalizeLocalLlmContextFilter(string? value)
    {
        var clean = CleanLocalLlmFilter(value, LlmContextAny);
        return clean.ToUpperInvariant() switch
        {
            "4K+" => "4K+",
            "16K+" => "16K+",
            "32K+" => "32K+",
            "128K+" => "128K+",
            "256K+" => "256K+",
            "1M+" => "1M+",
            "10M+" => "10M+",
            _ => LlmContextAny
        };
    }

    private static string NormalizeLocalLlmRequirementFilter(string? value)
    {
        var clean = CleanLocalLlmFilter(value, LlmRequirementAny);
        if (clean.Equals("CPU-safe", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU-safe";
        }

        if (clean.Equals("4 GB VRAM or less", StringComparison.OrdinalIgnoreCase))
        {
            return "4 GB VRAM or less";
        }

        if (clean.Equals("8 GB VRAM or less", StringComparison.OrdinalIgnoreCase))
        {
            return "8 GB VRAM or less";
        }

        if (clean.Equals("16 GB VRAM or less", StringComparison.OrdinalIgnoreCase))
        {
            return "16 GB VRAM or less";
        }

        if (clean.Equals("24 GB VRAM or less", StringComparison.OrdinalIgnoreCase))
        {
            return "24 GB VRAM or less";
        }

        return clean.Equals("Workstation/server", StringComparison.OrdinalIgnoreCase)
            ? "Workstation/server"
            : LlmRequirementAny;
    }

    private void ApplyLocalLlmFilters()
    {
        var filtered = LocalLlmModels
            .Where(model => MatchesSearchFilter(model, LocalLlmSearchText))
            .Where(model => MatchesOwnershipFilter(model, _selectedLocalLlmOwnershipFilter))
            .Where(model => MatchesProviderFilter(model, SelectedLocalLlmProviderFilter))
            .Where(model => MatchesSourceFilter(model, SelectedLocalLlmSourceFilter))
            .Where(model => MatchesPurposeFilter(model, SelectedLocalLlmPurposeFilter))
            .Where(model => MatchesBaseFilter(model, SelectedLocalLlmBaseFilter))
            .Where(model => MatchesContextFilter(model, SelectedLocalLlmContextFilter))
            .Where(model => MatchesRequirementFilter(model, SelectedLocalLlmRequirementFilter));
        var visible = SortLocalLlmModels(filtered, SelectedLocalLlmSortOption).ToList();

        VisibleLocalLlmModels.Clear();
        if (visible.Count > 0)
        {
            VisibleLocalLlmModels.AddRange(visible);
        }

        OnPropertyChanged(nameof(HasVisibleLocalLlmModels));
        OnPropertyChanged(nameof(LocalLlmVisibleSummary));
        OnPropertyChanged(nameof(LocalLlmVisibleCountLabel));
    }

    private static bool MatchesSearchFilter(LocalLlmModelViewModel model, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var text = $"{model.DisplayName} {model.Id} {model.Provider} {model.ModelBaseLabel} {model.BackendRequirementLabel} {model.PracticalUse} {model.PurposeTagsLabel}";
        return text.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void CycleLocalLlmOwnershipFilter()
    {
        _selectedLocalLlmOwnershipFilter = _selectedLocalLlmOwnershipFilter switch
        {
            LlmOwnershipAll => LlmOwnershipOwned,
            LlmOwnershipOwned => LlmOwnershipNotOwned,
            _ => LlmOwnershipAll
        };
        OnPropertyChanged(nameof(LocalLlmOwnershipFilterLabel));
        ApplyLocalLlmFilters();
    }

    private static bool MatchesOwnershipFilter(LocalLlmModelViewModel model, string filter)
    {
        return filter switch
        {
            LlmOwnershipOwned => model.IsInstalled,
            LlmOwnershipNotOwned => !model.IsInstalled,
            _ => true
        };
    }

    private void ApplyDependencyFilters()
    {
        if (VisibleLlmBackendDependencies is null)
        {
            return;
        }

        var filter = DependencySearchText.Trim();
        var visible = string.IsNullOrWhiteSpace(filter)
            ? LlmBackendDependencies.ToList()
            : LlmBackendDependencies
                .Where(dependency =>
                    $"{dependency.DisplayName} {dependency.Id} {dependency.Category} {dependency.ApiStyle} {dependency.Platforms} {dependency.Purpose} {dependency.StatusLabel}"
                        .Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        VisibleLlmBackendDependencies.Clear();
        if (visible.Count > 0)
        {
            VisibleLlmBackendDependencies.AddRange(visible);
        }
    }

    private static IEnumerable<LocalLlmModelViewModel> SortLocalLlmModels(
        IEnumerable<LocalLlmModelViewModel> models,
        string sortOption)
    {
        return sortOption switch
        {
            LlmSortOldest => models
                .OrderBy(model => model.ReleaseDateValue ?? DateTime.MaxValue)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            LlmSortProvider => models
                .OrderBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(model => model.ReleaseDateValue ?? DateTime.MinValue)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            LlmSortName => models
                .OrderBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            LlmSortFit => models
                .OrderByDescending(model => model.IsRecommended)
                .ThenBy(model => model.RecommendedVramGiB)
                .ThenByDescending(model => model.ReleaseDateValue ?? DateTime.MinValue)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            LlmSortInstalled => models
                .OrderByDescending(model => model.IsInstalled)
                .ThenByDescending(model => model.IsRecommended)
                .ThenByDescending(model => model.ReleaseDateValue ?? DateTime.MinValue)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            LlmSortContext => models
                .OrderByDescending(model => model.AdvertisedContextTokens)
                .ThenByDescending(model => model.ReleaseDateValue ?? DateTime.MinValue)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase),
            _ => models
                .OrderByDescending(model => model.ReleaseDateValue ?? DateTime.MinValue)
                .ThenBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesProviderFilter(LocalLlmModelViewModel model, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || filter.Equals(LlmProviderAll, StringComparison.OrdinalIgnoreCase)
            || model.Provider.Equals(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesSourceFilter(LocalLlmModelViewModel model, string filter)
    {
        return filter switch
        {
            LlmSourceLocalOnly => !model.IsCloudModel,
            LlmSourceCloudOnly => model.IsCloudModel,
            _ => true
        };
    }

    private static bool MatchesPurposeFilter(LocalLlmModelViewModel model, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || filter.Equals(LlmPurposeAll, StringComparison.OrdinalIgnoreCase)
            || model.PurposeTags.Any(tag => tag.Equals(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesBaseFilter(LocalLlmModelViewModel model, string filter)
    {
        return string.IsNullOrWhiteSpace(filter)
            || filter.Equals(LlmBaseAll, StringComparison.OrdinalIgnoreCase)
            || model.ModelBaseLabel.Equals(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesContextFilter(LocalLlmModelViewModel model, string filter)
    {
        var threshold = filter switch
        {
            "4K+" => 4 * 1024,
            "16K+" => 16 * 1024,
            "32K+" => 32 * 1024,
            "128K+" => 128 * 1024,
            "256K+" => 256 * 1024,
            "1M+" => 1024 * 1024,
            "10M+" => 10 * 1024 * 1024,
            _ => 0
        };
        return threshold <= 0 || model.AdvertisedContextTokens >= threshold;
    }

    private static bool MatchesRequirementFilter(LocalLlmModelViewModel model, string filter)
    {
        return filter switch
        {
            "CPU-safe" => model.WorksOnCpu,
            "4 GB VRAM or less" => model.RecommendedVramGiB <= 4,
            "8 GB VRAM or less" => model.RecommendedVramGiB <= 8,
            "16 GB VRAM or less" => model.RecommendedVramGiB <= 16,
            "24 GB VRAM or less" => model.RecommendedVramGiB <= 24,
            "Workstation/server" => model.RecommendedVramGiB > 24,
            _ => true
        };
    }

    private void RefreshInstalledLocalModels()
    {
        InstalledLocalModels.Clear();
        foreach (var model in LocalLlmModels
            .Where(model => model.IsInstalled && model.CanUseInLocalChat)
            .OrderByDescending(model => model.IsRecommended)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            InstalledLocalModels.Add(model);
        }

        InstalledImageGenerationModels.Clear();
        foreach (var model in LocalLlmModels
            .Where(model => model.IsImageGenerationModel
                && model.IsBackendPlatformSupported
                && (model.IsInstalled || model.CanUseManualBackend))
            .OrderByDescending(model => model.IsRecommended)
            .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            InstalledImageGenerationModels.Add(model);
        }

        OnPropertyChanged(nameof(HasInstalledLocalModels));
        OnPropertyChanged(nameof(HasInstalledImageGenerationModels));
        OnPropertyChanged(nameof(InstalledLocalModelCount));
        OnPropertyChanged(nameof(LlmCompactInfoLabel));
        OnPropertyChanged(nameof(ImageGenerationModelSummary));
        OnPropertyChanged(nameof(ChatWorkspaceSubtitle));
        OnPropertyChanged(nameof(ActiveInstalledLocalModels));

        var preferred = InstalledLocalModels.FirstOrDefault(model =>
                string.Equals(model.Id, _settings.SelectedLocalModel, StringComparison.OrdinalIgnoreCase))
            ?? InstalledLocalModels.FirstOrDefault(model => model.IsRecommended)
            ?? InstalledLocalModels.FirstOrDefault();
        var preferredImage = InstalledImageGenerationModels.FirstOrDefault(model =>
                string.Equals(model.Id, _settings.SelectedImageModel, StringComparison.OrdinalIgnoreCase))
            ?? InstalledImageGenerationModels.FirstOrDefault(model => model.IsRecommended)
            ?? InstalledImageGenerationModels.FirstOrDefault();

        if (preferred is null)
        {
            SelectedLocalModel = null;
        }
        else if (SelectedLocalModel is null
            || !InstalledLocalModels.Contains(SelectedLocalModel))
        {
            SelectedLocalModel = preferred;
        }

        if (preferredImage is null)
        {
            SelectedImageGenerationModel = null;
        }
        else if (SelectedImageGenerationModel is null
            || !InstalledImageGenerationModels.Contains(SelectedImageGenerationModel))
        {
            SelectedImageGenerationModel = preferredImage;
        }

        OnPropertyChanged(nameof(SelectedActiveLocalModel));
    }

    private static LocalLlmCatalogModel CreateUnknownInstalledModel(string modelId)
    {
        return new LocalLlmCatalogModel(
            modelId,
            modelId,
            "Unknown",
            "",
            "Installed",
            "See model card",
            "Detected from local Ollama; requirements unknown",
            "Unknown",
            "Start with 4K",
            "Keep snippets small until tested.",
            "Unknown",
            "Existing local Ollama model.",
            0,
            4,
            true,
            $"ollama pull {modelId}");
    }

    private double CalculatePromptBarBaseHeight()
    {
        if (IsLargePrompt)
        {
            return MaximumPromptBarHeight;
        }

        return IsPromptTypingActive ? CalculatePromptBarHeight() : CompactPromptBarHeight;
    }

    private double CalculatePromptBarHeight()
    {
        var visualLines = EstimatePromptVisualLineCount(PromptText);
        var overflowLines = Math.Max(0, visualLines - CompactPromptLines);
        var desiredHeight = CompactPromptBarHeight + overflowLines * PromptLineHeight;
        return Math.Min(MaximumPromptBarHeight, desiredHeight);
    }

    private static int EstimatePromptVisualLineCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var count = 0;
        var lineLength = 0;
        var processedLines = 0;
        var index = 0;

        for (; index < text.Length
            && index < PromptHeightEstimationCharacterLimit
            && processedLines < PromptHeightEstimationLineLimit;
            index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                count += EstimateWrappedLines(lineLength);
                processedLines++;
                lineLength = 0;

                if (character == '\r'
                    && index + 1 < text.Length
                    && text[index + 1] == '\n')
                {
                    index++;
                }

                continue;
            }

            lineLength++;
        }

        if (index < text.Length)
        {
            return Math.Max(count, PromptVisualLinesForMaximumHeight);
        }

        count += EstimateWrappedLines(lineLength);
        return count;
    }

    private static int EstimateWrappedLines(int lineLength)
    {
        return Math.Max(1, (int)Math.Ceiling(lineLength / (double)EstimatedPromptWrapColumn));
    }

    private static bool IsLargePromptText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Length >= LargePromptCharacterThreshold)
        {
            return true;
        }

        var lineBreaks = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is not ('\r' or '\n'))
            {
                continue;
            }

            if (++lineBreaks >= LargePromptLineThreshold)
            {
                return true;
            }

            if (character == '\r'
                && index + 1 < text.Length
                && text[index + 1] == '\n')
            {
                index++;
            }
        }

        return false;
    }

}
