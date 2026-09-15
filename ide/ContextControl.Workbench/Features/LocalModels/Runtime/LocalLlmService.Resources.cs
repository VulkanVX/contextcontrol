namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private LocalResourceSettings _resourceSettings = new();
    private LocalLlmHardwareProfile _resourceHardware = new([], LogicalProcessors: Environment.ProcessorCount);
    public void ConfigureResources(LocalResourceSettings settings, LocalLlmHardwareProfile hardware)
    {
        _resourceSettings = settings.Normalize();
        _resourceHardware = hardware;
    }
    private OllamaChatOptions? ResourceOptions(LocalLlmRequest request)
    {
        var settings = _resourceSettings;
        var local = !(request.ModelId.Contains(":cloud", StringComparison.OrdinalIgnoreCase) || request.ModelId.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase));
        var context = request.ContextWindowTokens;
        int? threads = settings.Enabled && settings.AutoThreads && local ? LocalResourcePlanner.Threads(_resourceHardware) : null;
        // Ollama knows the exact model layout and active allocations. Its own dynamic GPU placement is preferable to a catalog guess.
        int? gpu = settings.Enabled && settings.AutoGpuLayers && local ? -1 : null;
        if (settings.Enabled && settings.AutoContext && local && context is null)
        {
            var model = Catalog.FirstOrDefault(m => m.Id.Equals(request.ModelId, StringComparison.OrdinalIgnoreCase));
            context = LocalResourcePlanner.Plan(new(LocalResourcePlanner.ParseWeightGiB(model?.DownloadSize), EstimateSplitWithoutLayers: true),
                _resourceHardware.WithCurrentMemory(), settings).ContextTokens;
        }
        return context is > 0 || request.MaxOutputTokens is > 0 || threads is not null || gpu is not null
            ? new(context, request.MaxOutputTokens, threads, gpu) : null;
    }
}
