using System.Globalization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

/// <summary>Saved preferences only. Effective values never replace manual runtime settings.</summary>
public sealed record LocalResourceSettings(bool Enabled = false, bool AutoGpuLayers = true,
    bool AutoContext = true, bool AutoThreads = true, int MaxContextTokens = 8192,
    double RamReserveGiB = 4, double VramReserveGiB = 0.75)
{
    public LocalResourceSettings Normalize() => this with
    {
        MaxContextTokens = Math.Clamp(MaxContextTokens, 1024, 32768),
        RamReserveGiB = double.IsFinite(RamReserveGiB) ? Math.Clamp(RamReserveGiB, 1, 64) : 4,
        VramReserveGiB = double.IsFinite(VramReserveGiB) ? Math.Clamp(VramReserveGiB, 0.25, 32) : 0.75
    };
}

public sealed record LocalModelMemory(double? WeightGiB, int? Layers = null, double? KvBytesPerToken = null,
    int? MaxContext = null, bool CpuSupported = true, bool EstimateSplitWithoutLayers = false);

public sealed record LocalResourcePlan(bool Fits, string Label, string Detail, int ContextTokens,
    int CpuThreads, int GpuLayers, double? RamGiB, double? VramGiB);

/// <summary>Conservative estimates, not a benchmark or a promise of runtime/architecture support.</summary>
public static class LocalResourcePlanner
{
    public static double? ParseWeightGiB(string? size)
    {
        // Ranges and unknown sizes must not turn into a confident fit for the smallest variant.
        var match = Regex.Match(size ?? "", @"^\s*[~≈]?\s*(\d+(?:\.\d+)?)\s*(GiB|GB|MiB|MB|TiB|TB)\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "MIB" => value / 1024, "MB" => value * 1e6 / 1073741824d,
            "TB" => value * 1e12 / 1073741824d, "TIB" => value * 1024,
            "GB" => value * 1e9 / 1073741824d, _ => value
        };
    }

    public static int Threads(LocalLlmHardwareProfile hardware) =>
        Math.Clamp(hardware.PhysicalCores is > 0 ? hardware.PhysicalCores.Value - 1 : hardware.LogicalProcessors / 2, 1, 64);

    public static LocalResourcePlan Plan(LocalModelMemory model, LocalLlmHardwareProfile hardware,
        LocalResourceSettings preferences, int manualContext = 4096, int manualGpuLayers = 0, int manualThreads = 0)
    {
        var settings = preferences.Normalize();
        var threads = settings.Enabled && settings.AutoThreads ? Threads(hardware) : Math.Clamp(manualThreads, 0, 1024);
        var automaticContext = settings.Enabled && settings.AutoContext;
        var ceiling = automaticContext ? settings.MaxContextTokens : Math.Clamp(manualContext, 1024, 1048576);
        if (model.MaxContext is > 0) ceiling = Math.Min(ceiling, model.MaxContext.Value);
        var context = Math.Max(1, ceiling);
        if (model.WeightGiB is not > 0)
            return new(false, "Size unknown", "Model memory is unknown. Auto uses CPU and a small context until metadata is available.",
                automaticContext ? Math.Min(context, 2048) : context, threads,
                settings.Enabled && settings.AutoGpuLayers ? 0 : manualGpuLayers, null, null);

        var weight = model.WeightGiB.Value;
        var ramBudget = hardware.AvailableRamGiB is { } available
            ? Math.Max(0, available - settings.RamReserveGiB) : (double?)null;
        var gpu = hardware.Gpus.OrderByDescending(g => g.MemoryGiB ?? 0).FirstOrDefault();
        // Unknown free VRAM is treated conservatively; never count shared system memory as VRAM.
        var gpuBudget = gpu?.MemoryGiB is { } total
            ? Math.Max(0, (gpu.AvailableMemoryGiB ?? total * 0.7) - settings.VramReserveGiB) : 0;
        var layers = Math.Clamp(model.Layers ?? 0, 0, 999);
        var kvPerToken = model.KvBytesPerToken is > 0 ? model.KvBytesPerToken.Value
            : Math.Max(131072, weight * 65536); // unknown architectures require a deliberately generous allowance
        int selectedLayers = 0;
        double ram = 0, vram = 0, selectedShare = 0;
        bool fits = false;
        while (true)
        {
            var kv = kvPerToken * context / 1073741824d;
            var working = weight * 1.15 + kv;
            if (settings.Enabled && settings.AutoGpuLayers)
                selectedLayers = layers > 0 ? Math.Clamp((int)Math.Floor(Math.Max(0, gpuBudget - 0.4) / working * (layers + 1)), 0, layers + 1) : 0;
            else selectedLayers = Math.Clamp(manualGpuLayers, 0, layers > 0 ? layers + 1 : 999);
            // For the catalog, no layer count is available: estimate a possible split, but don't invent a launch count.
            var share = layers > 0 ? (double)selectedLayers / (layers + 1)
                : model.EstimateSplitWithoutLayers && settings.Enabled && settings.AutoGpuLayers ? Math.Clamp((gpuBudget - 0.4) / working, 0, 1) : 0;
            if (!model.CpuSupported) share = 1;
            selectedShare = share;
            vram = share > 0 ? working * share + 0.4 : 0;
            // Extra host space covers mappings, input preparation and temporary buffers, even for full offload.
            ram = working * (1 - share) + 0.75 + weight * 0.1;
            fits = ramBudget is { } budget && ram <= budget && vram <= gpuBudget
                && (model.CpuSupported || share >= 1);
            if (fits || !automaticContext || context <= 1024) break;
            context = Math.Max(1024, context / 2 / 1024 * 1024);
        }
        var label = ramBudget is null ? "RAM unknown" : !fits ? "Memory short" : vram < 0.01 ? "CPU / RAM"
            : selectedShare >= 0.999 ? "GPU fit" : "CPU + GPU";
        var detail = $"Estimated {ram:0.0} GiB RAM + {vram:0.0} GiB VRAM at {context:N0} context; {threads} CPU threads. ";
        detail += ramBudget is { } b ? $"Budgets after reserves: {b:0.0} GiB RAM / {gpuBudget:0.0} GiB VRAM. " : "Free system RAM could not be measured. ";
        detail += fits ? "CPU offload can be slow; actual allocations depend on the model and runtime."
            : "Not enough verified free memory for this estimate. Close other models/apps or choose a smaller model.";
        return new(fits, label, detail, context, threads, selectedLayers, ram, vram);
    }
}
