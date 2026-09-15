using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmModelViewModel
{
    private LocalResourceSettings _resourceSettings = new();
    private LocalLlmHardwareProfile _hardware = new([]);
    private long? _weightBytes;
    private int? _serverContext;
    private ModelFit? _nonAdaptiveFit;
    private LocalModelMemory? _runtimeMemory;
    private LocalRuntimeAllocation? _allocation;
    public LocalResourcePlan? ResourcePlan { get; private set; }
    public LocalResourcePlan? AdaptedFitPlan { get; private set; }
    public bool FitsOnCpuWithAdaptation => SupportsResourceAdaptation && LocalResourcePlanner.Plan(
        MemoryEstimate, PlanningHardware with { Gpus = [] }, _resourceSettings with { Enabled = true, AutoGpuLayers = true },
        ContextCapsuleBuilder.EstimateContextTokens(Model.ComfortableContext)).Fits;
    public LocalModelMemory MemoryEstimate => _runtimeMemory ?? new(
        _weightBytes is > 0 ? _weightBytes / 1073741824d : LocalResourcePlanner.ParseWeightGiB(Model.DownloadSize),
        MaxContext: _advertisedContextTokens > 0 ? _advertisedContextTokens : null, EstimateSplitWithoutLayers: true);
    public LocalLlmHardwareProfile PlanningHardware => LocalResourcePlanner.CreditLoadedAllocation(_hardware, _allocation);
    public bool AdaptsContext => _resourceSettings.Enabled && _resourceSettings.AutoContext && SupportsResourceAdaptation;
    public void RefreshAvailableMemory()
    {
        if (!_resourceSettings.Enabled) return;
        _hardware = _hardware.WithCurrentMemory();
        ApplyResourceEstimate();
    }
    public bool UsesAdaptedFit => _resourceSettings.Enabled;
    public bool SupportsResourceAdaptation => !IsCloudModel && CanUseInLocalChat
        && (_runtimeMemory?.WeightGiB is > 0 || !IsConnectedRuntime && (UsesOllamaPull || Model.PullCommand.Contains("gguf", StringComparison.OrdinalIgnoreCase)
            || Model.MinimumRequirement.Contains("GGUF", StringComparison.OrdinalIgnoreCase)));
    public string ResourceAllocationLabel => ResourcePlan is { } plan ? $"{plan.Label} · {plan.ContextTokens:N0} ctx" : FitLabel;

    public void ConfigureResources(LocalResourceSettings settings)
    {
        _resourceSettings = settings;
        ApplyResourceEstimate();
    }
    public void UpdateResourceHardware(LocalLlmHardwareProfile hardware, LocalRuntimeAllocation? allocation = null, LocalModelMemory? metadata = null)
    {
        _hardware = hardware;
        _detectedGpuVramGiB = hardware.MaxGpuMemoryGiB;
        _allocation = allocation;
        // Connected runtime identity can outlive its loaded model.
        if (IsConnectedRuntime || metadata is not null) _runtimeMemory = metadata;
        ApplyResourceEstimate();
    }
    public void ApplyServerContext(int? context)
    {
        _serverContext = context;
        OnPropertyChanged(nameof(ComfortableContext)); OnPropertyChanged(nameof(AdvertisedContext)); OnPropertyChanged(nameof(AdvertisedContextTokens));
    }
    private void ApplyResourceEstimate()
    {
        AdaptedFitPlan = SupportsResourceAdaptation
            ? LocalResourcePlanner.Plan(MemoryEstimate, PlanningHardware, _resourceSettings with { Enabled = true },
                ContextCapsuleBuilder.EstimateContextTokens(Model.ComfortableContext))
            : null;
        ResourcePlan = _resourceSettings.Enabled ? AdaptedFitPlan : null;
        if (_resourceSettings.Enabled)
        {
            FitLabel = ResourcePlan?.Label ?? (IsCloudModel ? "Cloud" : IsConnectedRuntime ? "Server managed" : "Runtime specific");
            FitDetail = ResourcePlan?.Detail ?? (IsCloudModel ? "Hosted model; local resources do not determine its capacity."
                : "This runtime controls its own memory. Local fit is not verified; use Show all to include it.");
            IsRecommended = ResourcePlan?.Fits == true && ResourcePlan.Label == "GPU fit";
        }
        else if (_nonAdaptiveFit is not null || !IsCloudModel && !IsConnectedRuntime)
        {
            var fit = _nonAdaptiveFit ?? CalculateFit(Model, _hardware);
            FitLabel = fit.Label; FitDetail = fit.Detail; IsRecommended = fit.IsRecommended;
        }
        OnPropertyChanged(nameof(ResourcePlan)); OnPropertyChanged(nameof(AdaptedFitPlan)); OnPropertyChanged(nameof(UsesAdaptedFit)); OnPropertyChanged(nameof(ResourceAllocationLabel));
        OnPropertyChanged(nameof(ComfortableContext));
        NotifyHardwareFitChanged();
    }
}
