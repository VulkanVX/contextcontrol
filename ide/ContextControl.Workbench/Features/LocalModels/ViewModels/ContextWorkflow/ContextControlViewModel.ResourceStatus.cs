using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private LocalLlmModelViewModel? _resourceModel;
    private readonly SemaphoreSlim _resourceRefreshGate = new(1);
    private DateTime _lastResourceRefresh;
    private string _resourceBudget = "Refresh to read available memory.";
    private string _resourceAllocation = "Select a model to inspect its allocation.";
    private string _resourceContext = "";
    private string _resourceEstimate = "";
    public LocalLlmModelViewModel? SelectedResourceModel
    {
        get => _resourceModel ?? SelectedLocalModel;
        set
        {
            if (!SetProperty(ref _resourceModel, value)) return;
            RefreshPerformanceDisplay();
            ResourceAllocationSummary = "Reading model resources…";
            ResourceContextSummary = ResourceEstimateSummary = "";
        }
    }
    public string ResourceBudgetSummary { get => _resourceBudget; private set => SetProperty(ref _resourceBudget, value); }
    public string ResourceAllocationSummary { get => _resourceAllocation; private set => SetProperty(ref _resourceAllocation, value); }
    public string ResourceContextSummary { get => _resourceContext; private set => SetProperty(ref _resourceContext, value); }
    public string ResourceEstimateSummary { get => _resourceEstimate; private set => SetProperty(ref _resourceEstimate, value); }

    internal async Task RefreshResourceStatusAsync(CancellationToken cancellationToken, bool force = false)
    {
        if (!force && DateTime.UtcNow - _lastResourceRefresh < TimeSpan.FromSeconds(4)) return;
        try
        {
            if (force) await _resourceRefreshGate.WaitAsync(cancellationToken);
            else if (!_resourceRefreshGate.Wait(0)) return;
        }
        catch (OperationCanceledException) { return; }
        var selected = SelectedResourceModel;
        var settings = _settings.LocalResources;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var token = deadline.Token;
            var hardware = _resourceHardware.TotalRamBytes is null || string.IsNullOrEmpty(_resourceHardware.CpuName)
                ? await LocalLlmService.DetectHardwareAsync(token)
                : await LocalLlmService.SampleResourceHardwareAsync(_resourceHardware, token);
            var ollama = await _localLlmService.ReadOllamaResourcesAsync(
                selected is { UsesOllamaPull: true, IsInstalled: true } ? selected.Id : "",
                selected?.MemoryEstimate.WeightGiB, token);
            LocalModelMemory? managedMemory = null;
            var active = _managedRuntimes.ActiveProfile;
            if (active is { Id: "llama-cpp" or "koboldcpp" } && _managedRuntimes.Owns(active.Id))
                managedMemory = await Task.Run(() => GgufResourceMetadata.Read(active.ModelPath), token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(selected, SelectedResourceModel) || settings != _settings.LocalResources) return;
            _resourceHardware = hardware;
            RefreshPerformanceDisplay();
            HardwareSummary = hardware.Summary;
            _localLlmService.ConfigureResources(settings, hardware);
            LocalRuntimeAllocation? selectedAllocation = null;
            foreach (var model in LocalLlmModels)
            {
                LocalRuntimeAllocation? allocation = null;
                LocalModelMemory? metadata = null;
                if (model.UsesOllamaPull && !model.IsCloudModel)
                {
                    if (ollama.Loaded?.TryGetValue(model.Id, out allocation) != true)
                        ollama.Loaded?.TryGetValue(model.Id + ":latest", out allocation);
                    if (ReferenceEquals(model, selected)) metadata = ollama.Memory;
                }
                if (model.IsConnectedRuntime && active is not null && active.Id == model.Model.RuntimeId && _managedRuntimes.Owns(active.Id))
                {
                    allocation = _managedRuntimes.ReadAllocation(active.Id);
                    metadata = managedMemory;
                }
                model.UpdateResourceHardware(hardware, allocation, metadata);
                if (ReferenceEquals(model, selected)) selectedAllocation = allocation;
            }
            ApplyLocalLlmFilters();
            var gpu = hardware.Gpus.OrderByDescending(g => g.MemoryGiB ?? 0).FirstOrDefault();
            static string GiB(double? value) => value is { } n ? $"{n:0.0} GiB" : "unreported";
            ResourceBudgetSummary = $"Available now: RAM {GiB(hardware.AvailableRamGiB)} / {GiB(hardware.TotalRamGiB)} · "
                + $"VRAM {GiB(gpu?.AvailableMemoryGiB)} / {GiB(gpu?.MemoryGiB)}. "
                + $"After headroom: RAM {GiB(hardware.AvailableRamGiB is { } ram ? Math.Max(0, ram - settings.RamReserveGiB) : null)} · "
                + $"VRAM {GiB(gpu?.AvailableMemoryGiB is { } vram ? Math.Max(0, vram - settings.VramReserveGiB) : null)}.";
            if (selected is null)
            {
                ResourceAllocationSummary = "Select a catalog model to inspect its allocation and context capacity.";
                ResourceContextSummary = ResourceEstimateSummary = "";
                return;
            }
            var report = selectedAllocation ?? (selected.UsesOllamaPull && !selected.IsCloudModel
                ? ollama.Allocation : new(false, selected.IsCloudModel ? "Hosted model" : "Allocation is not reported by this runtime"));
            ResourceAllocationSummary = report.Running
                ? $"Loaded: {report.Status} · " + (report.ResidentRamBytes is { } resident
                    ? $"Process RAM {GiB(resident / 1073741824d)}"
                    : report.TotalBytes is { } total && report.VramBytes is { } device && device <= total
                        ? $"CPU allocation {GiB((total - device) / 1073741824d)} (total minus VRAM)" : "RAM unreported")
                    + $" · VRAM {GiB(report.VramBytes / 1073741824d)} · Active context {report.ContextTokens?.ToString("N0") ?? "unreported"}"
                    + (report.CpuThreads is { } threads ? $" · {threads} CPU threads" : "")
                    + (report.GpuLayers is { } layers ? $" · {layers} GPU layers" : "")
                : report.Status + ".";
            var plan = selected.AdaptedFitPlan;
            if (plan is not null)
            {
                var capacity = LocalResourcePlanner.EstimateMaximumContext(selected.MemoryEstimate, selected.PlanningHardware,
                    settings with { Enabled = true }, selected.UsesOllamaPull ? 1048576 : 32768);
                ResourceEstimateSummary = $"With adaptation: {plan.Label} · RAM {GiB(plan.RamGiB)} + VRAM {GiB(plan.VramGiB)} · "
                    + $"{plan.ContextTokens:N0} context target · {plan.CpuThreads} CPU threads. "
                    + (report.Running && report.TotalBytes is not null ? "Includes this model's reclaimable allocation. " : "")
                    + (settings.Enabled ? "Applies on the next request or managed start." : "Preview only; Auto is off.");
                ResourceContextSummary = capacity.Detail;
            }
            else
            {
                ResourceEstimateSummary = "Adapted allocation is unknown for this runtime or model type.";
                ResourceContextSummary = $"Context limit: {selected.AdvertisedContext}. Inspect the runtime's own memory controls.";
            }
            _lastResourceRefresh = DateTime.UtcNow;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Http.HttpRequestException
            or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!cancellationToken.IsCancellationRequested) ResourceAllocationSummary = "Resource readings unavailable. Refresh to retry.";
        }
        finally { _resourceRefreshGate.Release(); }
    }
}
