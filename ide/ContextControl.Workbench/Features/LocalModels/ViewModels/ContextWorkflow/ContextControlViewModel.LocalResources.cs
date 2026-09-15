using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class ContextControlViewModel
{
    private LocalLlmHardwareProfile _resourceHardware = new([], LogicalProcessors: Environment.ProcessorCount);
    public bool AutoAdaptLocalModels { get => _settings.LocalResources.Enabled; set => SetLocalResources(_settings.LocalResources with { Enabled = value }); }
    public bool AutoModelGpuLayers { get => _settings.LocalResources.AutoGpuLayers; set => SetLocalResources(_settings.LocalResources with { AutoGpuLayers = value }); }
    public bool AutoModelContext { get => _settings.LocalResources.AutoContext; set => SetLocalResources(_settings.LocalResources with { AutoContext = value }); }
    public bool AutoModelThreads { get => _settings.LocalResources.AutoThreads; set => SetLocalResources(_settings.LocalResources with { AutoThreads = value }); }
    public int AutoModelMaxContext { get => _settings.LocalResources.MaxContextTokens; set => SetLocalResources(_settings.LocalResources with { MaxContextTokens = value }); }
    public double AutoModelRamReserve { get => _settings.LocalResources.RamReserveGiB; set => SetLocalResources(_settings.LocalResources with { RamReserveGiB = value }); }
    public double AutoModelVramReserve { get => _settings.LocalResources.VramReserveGiB; set => SetLocalResources(_settings.LocalResources with { VramReserveGiB = value }); }
    public string HardwareFitFilterLabel => AutoAdaptLocalModels ? "Adapted fit" : "Usable";
    public string HardwareFitFilterDescription => AutoAdaptLocalModels
        ? "Show models estimated to fit available RAM and VRAM after reserves. CPU offload can be slow. Unknown, hosted and externally managed memory is not a verified local fit."
        : "Use the catalog's original hardware requirements. Enable Auto adapt to include estimates for CPU/RAM offload.";
    public string ResourceModeSummary => AutoAdaptLocalModels
        ? $"Auto · up to {AutoModelMaxContext:N0} context · reserve {AutoModelRamReserve:0.#} GiB RAM / {AutoModelVramReserve:0.##} GiB VRAM. Changes apply to new requests and the next managed model start."
        : "Manual mode. Saved runtime values are preserved. Enable Auto adapt to estimate CPU/RAM fallback and suitable context.";

    private void SetLocalResources(LocalResourceSettings resources)
    {
        resources = resources.Normalize();
        if (_settings.LocalResources == resources) return;
        _settings.LocalResources = resources;
        CancelPerformanceTuning();
        SaveSettingsQuietly();
        foreach (var name in new[] { nameof(AutoAdaptLocalModels), nameof(AutoModelGpuLayers), nameof(AutoModelContext),
            nameof(AutoModelThreads), nameof(AutoModelMaxContext), nameof(AutoModelRamReserve), nameof(AutoModelVramReserve),
            nameof(HardwareFitFilterLabel), nameof(HardwareFitFilterDescription), nameof(ResourceModeSummary) }) OnPropertyChanged(name);
        ApplyResourceMode();
    }

    private void ApplyResourceMode()
    {
        _localLlmService.ConfigureResources(_settings.LocalResources, _resourceHardware);
        _localLlmService.ConfigurePerformance(_settings.LocalPerformanceProfiles, _settings.UseMeasuredLocalPerformance);
        foreach (var runtime in LocalRuntimeProfiles) runtime.SetResourceMode(_settings.LocalResources);
        foreach (var model in LocalLlmModels) model.ConfigureResources(_settings.LocalResources);
        ApplyLocalLlmFilters();
        RefreshPerformanceDisplay();
    }
}
