// CC-DESC: Extracted ContextControlViewModel system slice.
// CC-DESC: Owns Context Control workflow state, prompt bar state, and DIR/CC/GO commands.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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
        if (model is null)
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
            await RunTransferAsync($"Pull {model.Id}", async () =>
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
            await RunTransferAsync($"Uninstall {model.Id}", async () =>
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
                    model.ApplyStorageLocation(ResolveModelStorageLocation(model));
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
            await RunTransferAsync($"Download {model.Id}", async () =>
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
                        model.ApplyStorageLocation(ResolveModelStorageLocation(model));
                        RefreshInstalledLocalModels();
                        ApplyLocalLlmFilters();
                        RaiseCommandStates();
                    }
                    else if (IsManagedDiffusersRuntimeFailure(model, result.Status))
                    {
                        MarkBackendDependencyNeedsRepair(model.DependencyId, result.Status);
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

    private void MarkBackendDependencyNeedsRepair(string dependencyId, string detail)
    {
        if (string.IsNullOrWhiteSpace(dependencyId))
        {
            return;
        }

        var dependency = LlmBackendDependencies.FirstOrDefault(item =>
            item.Id.Equals(dependencyId, StringComparison.OrdinalIgnoreCase));
        if (dependency is null)
        {
            return;
        }

        dependency.ApplyStatus(false, "Repair needed", detail);
        OnPropertyChanged(nameof(DependencySummary));
        OnPropertyChanged(nameof(LlmCompactInfoLabel));
        ApplyDependencyFilters();
        ApplyBackendDependencyStatesToModels();
    }

    private static bool IsManagedDiffusersRuntimeFailure(LocalLlmModelViewModel model, string status)
    {
        if (!model.DependencyId.Equals("diffusers", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lower = (status ?? string.Empty).ToLowerInvariant();
        return lower.Contains("managed diffusers runtime", StringComparison.Ordinal)
            || lower.Contains("c10.dll", StringComparison.Ordinal)
            || lower.Contains("winerror 1114", StringComparison.Ordinal)
            || (lower.Contains("torch", StringComparison.Ordinal) && lower.Contains("dll", StringComparison.Ordinal));
    }

    private void ApplyLocalModelRefresh(LocalLlmRefreshResult result, bool preserveBackendModelStates = false)
    {
        _isOllamaReachable = result.OllamaReachable;
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
            model.ApplyDetectedDownloadSize(isInstalled
                ? ResolveInstalledModelSize(result.InstalledModelSizes, model.Id)
                : null);
            var isBackendDependencyReady = IsModelBackendDependencyReady(model);
            var isBackendModelReady = preserveBackendModelStates && model.UsesDownloadableBackendModel
                ? model.IsBackendModelReady
                : false;
            if (!isBackendModelReady
                && preserveBackendModelStates
                && model.UsesDownloadableBackendModel
                && isBackendDependencyReady
                && string.Equals(model.Id, _settings.SelectedImageModel, StringComparison.OrdinalIgnoreCase))
            {
                isBackendModelReady = true;
            }

            var isAvailable = isInstalled
                || (model.IsCloudModel && result.OllamaReachable)
                || (model.IsImageGenerationModel && model.RequiresManualBackend && isBackendDependencyReady);
            model.ApplyState(isInstalled, isAvailable, result.Hardware, isBackendDependencyReady, isBackendModelReady);
            model.ApplyOllamaCapabilities(ResolveInstalledModelCapabilities(result.InstalledModelCapabilities, model.Id));
            model.ApplyStorageLocation(ResolveModelStorageLocation(model));
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
            model.ApplyStorageLocation(ResolveModelStorageLocation(model));
        }

        RefreshInstalledLocalModels();
        ApplyLocalLlmFilters();
        RaiseCommandStates();
    }

    private async Task ApplyBackendModelCacheStatesAsync(
        CancellationToken cancellationToken,
        IProgress<LocalLlmTransferProgress>? progress = null,
        bool allowPythonProbe = true)
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
                model.ApplyStorageLocation(ResolveModelStorageLocation(model));
            }

            RefreshInstalledLocalModels();
            ApplyLocalLlmFilters();
            RaiseCommandStates();
            return;
        }

        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            allowPythonProbe
                ? "Checking cached Diffusers image model files."
                : "Checking cached Diffusers image model files from disk.",
            3,
            4,
            null,
            92));

        var cachedModelIds = await _localLlmService
            .DetectCachedImageModelIdsAsync(candidates, cancellationToken, allowPythonProbe);

        foreach (var model in LocalLlmModels.Where(model => model.UsesDownloadableBackendModel))
        {
            model.ApplyBackendModelState(cachedModelIds.Contains(model.Id));
            model.ApplyStorageLocation(ResolveModelStorageLocation(model));
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
        var filtered = ApplySharedLocalLlmFilters(LocalLlmModels)
            .Where(model => MatchesSearchFilter(model, LocalLlmSearchText))
            .Where(model => MatchesOwnershipFilter(model, _selectedLocalLlmOwnershipFilter));
        var visible = SortLocalLlmModels(filtered, SelectedLocalLlmSortOption).ToList();
        var stackVisible = SortLocalLlmModels(
                ApplySharedLocalLlmFilters(LocalLlmModels.Where(IsModelInUserStack))
                    .Where(model => MatchesSearchFilter(model, LocalLlmSearchText)),
                SelectedLocalLlmSortOption)
            .ToList();

        VisibleLocalLlmModels.Clear();
        if (visible.Count > 0)
        {
            VisibleLocalLlmModels.AddRange(visible);
        }

        VisibleStackLocalLlmModels.Clear();
        if (stackVisible.Count > 0)
        {
            VisibleStackLocalLlmModels.AddRange(stackVisible);
        }

        OnPropertyChanged(nameof(HasVisibleLocalLlmModels));
        OnPropertyChanged(nameof(HasVisibleStackLocalLlmModels));
        OnPropertyChanged(nameof(LocalLlmVisibleSummary));
        OnPropertyChanged(nameof(LocalLlmVisibleCountLabel));
        OnPropertyChanged(nameof(StackLocalLlmModelCount));
        OnPropertyChanged(nameof(MyStackSummary));
        OnPropertyChanged(nameof(MyStackModelSummary));
    }

    private IEnumerable<LocalLlmModelViewModel> ApplySharedLocalLlmFilters(IEnumerable<LocalLlmModelViewModel> models)
    {
        return models
            .Where(model => MatchesProviderFilter(model, SelectedLocalLlmProviderFilter))
            .Where(model => MatchesSourceFilter(model, SelectedLocalLlmSourceFilter))
            .Where(model => MatchesPurposeFilter(model, SelectedLocalLlmPurposeFilter))
            .Where(model => MatchesBaseFilter(model, SelectedLocalLlmBaseFilter))
            .Where(model => MatchesContextFilter(model, SelectedLocalLlmContextFilter))
            .Where(model => MatchesRequirementFilter(model, SelectedLocalLlmRequirementFilter))
            .Where(model => !ShowOnlyHardwareUsableLocalLlms || model.CanRunOnDetectedHardware);
    }

    private static bool MatchesSearchFilter(LocalLlmModelViewModel model, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var text = $"{model.DisplayName} {model.Id} {model.Provider} {model.ModelBaseLabel} {model.BackendRequirementLabel} {model.PracticalUse} {model.PurposeTagsLabel} {model.StorageLocationLabel} {model.EffectiveDownloadSizeLabel} {model.BigVramSummaryLabel}";
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

    private static bool IsModelInUserStack(LocalLlmModelViewModel model)
    {
        return model.IsInstalled || model.CanUseManualBackend;
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
                    $"{dependency.DisplayName} {dependency.Id} {dependency.Category} {dependency.ApiStyle} {dependency.Platforms} {dependency.Purpose} {dependency.StatusLabel} {dependency.SizeLabel}"
                        .Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        VisibleLlmBackendDependencies.Clear();
        if (visible.Count > 0)
        {
            VisibleLlmBackendDependencies.AddRange(visible);
        }

        var stackDependencies = LlmBackendDependencies
            .Where(IsDependencyInUserStack)
            .Where(dependency => string.IsNullOrWhiteSpace(filter)
                || $"{dependency.DisplayName} {dependency.Id} {dependency.Category} {dependency.ApiStyle} {dependency.Platforms} {dependency.Purpose} {dependency.StatusLabel} {dependency.SizeLabel}"
                    .Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dependency => dependency.IsRecommended)
            .ThenByDescending(dependency => dependency.IsReady)
            .ThenBy(dependency => dependency.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        VisibleStackBackendDependencies.Clear();
        if (stackDependencies.Count > 0)
        {
            VisibleStackBackendDependencies.AddRange(stackDependencies);
        }

        OnPropertyChanged(nameof(HasVisibleStackBackendDependencies));
        OnPropertyChanged(nameof(MyStackSummary));
    }

    private static bool IsDependencyInUserStack(LlmBackendDependencyViewModel dependency)
    {
        return dependency.IsReady || dependency.CanRepairManaged;
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
        if (_isOllamaReachable)
        {
            foreach (var model in LocalLlmModels
                .Where(model => model.IsInstalled && model.CanUseInLocalChat)
                .OrderByDescending(model => model.IsRecommended)
                .ThenBy(model => model.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                InstalledLocalModels.Add(model);
            }
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
        OnPropertyChanged(nameof(StackLocalLlmModelCount));
        OnPropertyChanged(nameof(LlmCompactInfoLabel));
        OnPropertyChanged(nameof(ImageGenerationModelSummary));
        OnPropertyChanged(nameof(MyStackSummary));
        OnPropertyChanged(nameof(MyStackModelSummary));
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

        OnPropertyChanged(nameof(SelectedLocalModel));
        OnPropertyChanged(nameof(SelectedImageGenerationModel));
        OnPropertyChanged(nameof(SelectedActiveLocalModel));
    }

    private static LocalLlmCatalogModel CreateUnknownInstalledModel(string modelId)
    {
        var estimated = EstimateUnknownModelVram(modelId);
        return new LocalLlmCatalogModel(
            modelId,
            modelId,
            "Unknown",
            "",
            "Installed",
            "See model card",
            estimated.HasValue
                ? $"Estimated from tag: {FormatUnknownVram(estimated.Value.Minimum)} min, {FormatUnknownVram(estimated.Value.Recommended)} stable"
                : "Detected from local Ollama; requirements unknown",
            "Unknown",
            "Start with 4K",
            "Keep snippets small until tested.",
            "Unknown",
            "Existing local Ollama model.",
            estimated?.Minimum ?? 0,
            estimated?.Recommended ?? 4,
            true,
            $"ollama pull {modelId}");
    }

    private static (double Minimum, double Recommended)? EstimateUnknownModelVram(string modelId)
    {
        var clean = (modelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        var paramsMatch = Regex.Match(clean, @"(?<params>\d+(?:\.\d+)?)\s*b\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!paramsMatch.Success
            || !double.TryParse(paramsMatch.Groups["params"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var billionParams)
            || billionParams <= 0)
        {
            return null;
        }

        var quantMatch = Regex.Match(clean, @"(?:^|[-_:])q(?<bits>[2-8])(?:[_\-.]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var bits = quantMatch.Success
            && double.TryParse(quantMatch.Groups["bits"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedBits)
                ? parsedBits
                : 4.0;
        var weightsGiB = billionParams * bits / 8.0;
        var minimum = RoundHalfGiB(Math.Max(1.5, weightsGiB + 0.75));
        var recommended = RoundHalfGiB(Math.Max(minimum + 1.5, weightsGiB * 1.25 + 1.5));
        return (minimum, recommended);
    }

    private static double RoundHalfGiB(double value)
    {
        return Math.Ceiling(value * 2.0) / 2.0;
    }

    private static string FormatUnknownVram(double value)
    {
        if (value <= 0)
        {
            return "CPU";
        }

        return Math.Abs(value - Math.Round(value)) < 0.05
            ? $"{value:0} GB"
            : $"{value:0.#} GB";
    }

    private static long? ResolveInstalledModelSize(IReadOnlyDictionary<string, long> sizes, string modelId)
    {
        foreach (var alias in ExpandLocalModelIdAliases(modelId))
        {
            if (sizes.TryGetValue(alias, out var size) && size > 0)
            {
                return size;
            }
        }

        return null;
    }

    private static IReadOnlySet<string>? ResolveInstalledModelCapabilities(
        IReadOnlyDictionary<string, IReadOnlySet<string>> capabilities,
        string modelId)
    {
        foreach (var alias in ExpandLocalModelIdAliases(modelId))
        {
            if (capabilities.TryGetValue(alias, out var modelCapabilities))
            {
                return modelCapabilities;
            }
        }

        return null;
    }

    private static IEnumerable<string> ExpandLocalModelIdAliases(string modelId)
    {
        var clean = (modelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            yield break;
        }

        yield return clean;
        const string latestSuffix = ":latest";
        if (clean.EndsWith(latestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            yield return clean[..^latestSuffix.Length];
        }
        else if (!clean.Contains(':', StringComparison.Ordinal))
        {
            yield return $"{clean}{latestSuffix}";
        }
    }

    private string ResolveModelStorageLocation(LocalLlmModelViewModel model)
    {
        if (model.IsInstalled
            && (model.UsesOllamaPull
                || model.DependencyId.Equals("ollama", StringComparison.OrdinalIgnoreCase)
                || !model.RequiresManualBackend))
        {
            return $"Ollama models: {_ollamaModelsDirectory}";
        }

        if (model.IsBackendModelReady && model.UsesHuggingFaceHubDownload)
        {
            return $"Hugging Face cache: {LocalLlmService.ResolveHuggingFaceHubCacheDirectory()}";
        }

        if (model.CanUseManualBackend || model.HasBackendOnlyReadyState)
        {
            var dependencyLocation = ResolveBackendDependencyLocation(model.DependencyId);
            if (!string.IsNullOrWhiteSpace(dependencyLocation))
            {
                return dependencyLocation;
            }
        }

        if (model.IsCloudModel && model.IsAvailable)
        {
            return "Ollama Cloud through local Ollama";
        }

        return "";
    }

    private string ResolveBackendDependencyLocation(string dependencyId)
    {
        if (string.IsNullOrWhiteSpace(dependencyId))
        {
            return "";
        }

        return LlmBackendDependencies
            .FirstOrDefault(dependency => dependency.Id.Equals(dependencyId, StringComparison.OrdinalIgnoreCase))
            ?.DetailLabel ?? "";
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
