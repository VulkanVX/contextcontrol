using System.Net.Http;
using System.Text.Json;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private CancellationTokenSource? _performanceCancellation;
    private string _performanceStatus = "";
    public bool IsPerformanceTuning => _performanceCancellation is not null;
    public bool UseMeasuredLocalPerformance
    {
        get => _settings.UseMeasuredLocalPerformance;
        set
        {
            if (_settings.UseMeasuredLocalPerformance == value) return;
            _settings.UseMeasuredLocalPerformance = value;
            _localLlmService.ConfigurePerformance(_settings.LocalPerformanceProfiles, value);
            SaveSettingsQuietly();
            OnPropertyChanged();
        }
    }
    public string PerformanceStatus { get => _performanceStatus; private set => SetProperty(ref _performanceStatus, value); }
    public string SavedPerformanceSummary
    {
        get
        {
            var profile = _settings.LocalPerformanceProfiles.LastOrDefault(p => p.ModelId == SelectedResourceModel?.Id);
            return profile is null ? "No measured override for this model. Tune an installed Ollama chat model while chats and downloads are idle."
                : $"Saved {profile.MeasuredUtc.ToLocalTime():g}: {profile.BaselineTokensPerSecond:0.0} → {profile.TokensPerSecond:0.0} tok/s · "
                    + $"{profile.Options.CpuThreads} threads · draft {profile.Options.DraftTokens?.ToString() ?? "default"} · {profile.ContextTokens:N0} context. "
                    + "Applies with Auto adapt, Auto threads and Auto GPU enabled, when model, runtime, hardware, context and warm GPU placement match.";
        }
    }
    private void RefreshPerformanceDisplay()
    {
        OnPropertyChanged(nameof(IsPerformanceTuning));
        OnPropertyChanged(nameof(SavedPerformanceSummary));
    }
    internal void CancelPerformanceTuning() => _performanceCancellation?.Cancel();

    internal async Task TuneSelectedModelAsync(CancellationToken token)
    {
        if (IsPerformanceTuning) return;
        if (ChatRequestProgressItems.Count > 0 || LocalLlmModels.Any(m => m.IsPulling) || IsTransferProgressLoading)
        { PerformanceStatus = "Wait for active chats and downloads to finish before measuring performance."; return; }
        var model = SelectedResourceModel;
        if (model is not { UsesOllamaPull: true, IsInstalled: true, IsCloudModel: false, CanUseInLocalChat: true })
        { PerformanceStatus = "Select a fully installed local Ollama chat model. No models are downloaded by tuning."; return; }
        if (!AutoAdaptLocalModels || !AutoModelThreads || !AutoModelGpuLayers)
        { PerformanceStatus = "Enable Auto adapt, Auto threads and Auto GPU before tuning. Your manual settings are preserved."; return; }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _performanceCancellation = cancellation;
        RefreshPerformanceDisplay();
        try
        {
            var hardware = await LocalLlmService.DetectHardwareAsync(cancellation.Token);
            var resources = await _localLlmService.ReadOllamaResourcesAsync(model.Id, model.MemoryEstimate.WeightGiB, cancellation.Token);
            model.UpdateResourceHardware(hardware, resources.Allocation, resources.Memory);
            var plan = model.ResourcePlan;
            if (plan is not { Fits: true }) throw new InvalidOperationException("The selected model does not currently fit within the configured memory headroom.");
            var context = plan.ContextTokens;
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var result = await new LocalPerformanceTuner(client).TuneAsync(model.Id, context, hardware,
                new Progress<string>(status => { if (ReferenceEquals(_performanceCancellation, cancellation)) PerformanceStatus = status; }), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            // Retuning replaces the old experiment, including removing an override that no longer beats the baseline.
            var profiles = _settings.LocalPerformanceProfiles.Where(p => p.ModelId != model.Id).ToList();
            if (result.Profile is { IsValid: true } profile) profiles.Add(profile);
            _settings.LocalPerformanceProfiles = profiles.TakeLast(64).ToArray();
            _localLlmService.ConfigurePerformance(_settings.LocalPerformanceProfiles, UseMeasuredLocalPerformance);
            SaveSettingsQuietly();
            PerformanceStatus = result.Status;
        }
        catch (OperationCanceledException) { PerformanceStatus = "Tuning stopped or reached its time limit. Saved settings were preserved."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { PerformanceStatus = "Tuning stopped: " + ex.Message; }
        finally
        {
            _performanceCancellation = null;
            cancellation.Dispose();
            RefreshPerformanceDisplay();
        }
    }
}
