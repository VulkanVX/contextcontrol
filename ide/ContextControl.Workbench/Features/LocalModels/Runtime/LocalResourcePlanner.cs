using System.Globalization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

/// <summary>Saved preferences only. Effective values never replace manual runtime settings.</summary>
public sealed record LocalResourceSettings(bool Enabled = false, bool AutoGpuLayers = true,
    bool AutoContext = true, bool AutoThreads = true, int MaxContextTokens = 8192,
    double RamReserveGiB = 4, double VramReserveGiB = 0.75, string ContextMode = "Custom")
{
    public static IReadOnlyList<string> ContextModes { get; } = ["Adaptive", "Fast · 8K", "Balanced · 16K", "Long · 32K", "Maximum fit", "Custom"];
    public int ContextCeiling => ContextMode switch
    {
        "Fast · 8K" => 8192, "Balanced · 16K" => 16384, "Long · 32K" => 32768,
        "Maximum fit" => 1048576, _ => MaxContextTokens
    };
    public LocalResourceSettings Normalize() => this with
    {
        MaxContextTokens = Math.Clamp(MaxContextTokens, 1024, 1048576),
        ContextMode = ContextModes.Contains(ContextMode) ? ContextMode : "Custom",
        RamReserveGiB = double.IsFinite(RamReserveGiB) ? Math.Clamp(RamReserveGiB, 1, 64) : 4,
        VramReserveGiB = double.IsFinite(VramReserveGiB) ? Math.Clamp(VramReserveGiB, 0.25, 32) : 0.75
    };
}

public sealed record LocalModelMemory(double? WeightGiB, int? Layers = null, double? KvBytesPerToken = null,
    int? MaxContext = null, bool CpuSupported = true, bool EstimateSplitWithoutLayers = false);

public sealed record LocalResourcePlan(bool Fits, string Label, string Detail, int ContextTokens,
    int CpuThreads, int GpuLayers, double? RamGiB, double? VramGiB,
    int? MaximumContextTokens = null, string MaximumContextDetail = "");

public sealed record LocalContextCapacity(int? Tokens, string Detail);

/// <summary>Conservative estimates, not a benchmark or a promise of runtime/architecture support.</summary>
public static class LocalResourcePlanner
{
    public static LocalLlmHardwareProfile CreditLoadedAllocation(LocalLlmHardwareProfile hardware, LocalRuntimeAllocation? allocation)
    {
        if (allocation?.Running != true) return hardware;
        // Only credit the candidate's own allocation; other loaded models still consume the budget.
        var host = allocation.TotalBytes is >= 0 && allocation.VramBytes is >= 0
            && allocation.VramBytes <= allocation.TotalBytes ? allocation.TotalBytes - allocation.VramBytes : null;
        long? Add(long? free, long? total, long? credit) => free is >= 0 && total is > 0 && credit is > 0
            ? (long)Math.Min(total.Value, (double)free.Value + credit.Value) : free;
        return hardware with
        {
            AvailableRamBytes = Add(hardware.AvailableRamBytes, hardware.TotalRamBytes, host),
            // Ollama reports aggregate VRAM without device attribution. Do not assign it to the wrong GPU.
            Gpus = hardware.Gpus.Count == 1 ? hardware.Gpus.Select(g => g with {
                AvailableRamBytes = Add(g.AvailableRamBytes, g.AdapterRamBytes, allocation.VramBytes)
            }).ToArray() : hardware.Gpus
        };
    }

    public static double? ParseWeightGiB(string? size)
    {
        // Accept explicit quantization labels; use the larger end of ranges. Never guess from arbitrary prose.
        var match = Regex.Match(size ?? "", @"^\s*[~≈]?\s*(\d+(?:\.\d+)?)\s*(?:(GiB|GB|MiB|MB|TiB|TB)\s*)?(?:[-–—]\s*(\d+(?:\.\d+)?)\s*)?(GiB|GB|MiB|MB|TiB|TB)?\s*(?:\(?\s*(?:I?Q\d[\w.]*|I\d[\w.]*|MXFP4|F(?:P)?(?:16|32)|BF16|published download)\s*\)?)?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return null;
        static double Convert(double value, string unit) => unit.ToUpperInvariant() switch
        {
            "MIB" => value / 1024, "MB" => value * 1e6 / 1073741824d,
            "TB" => value * 1e12 / 1073741824d, "TIB" => value * 1024,
            "GB" => value * 1e9 / 1073741824d, _ => value
        };
        var unit = match.Groups[4].Success ? match.Groups[4].Value : match.Groups[2].Value;
        if (unit.Length == 0 || match.Groups[2].Success && match.Groups[4].Success && !match.Groups[3].Success) return null;
        var first = Convert(value, match.Groups[2].Success ? match.Groups[2].Value : unit);
        if (!match.Groups[3].Success) return first;
        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var last) || last <= 0) return null;
        return Math.Max(first, Convert(last, unit));
    }

    public static int Threads(LocalLlmHardwareProfile hardware) =>
        Math.Clamp(hardware.PhysicalCores is > 0 ? hardware.PhysicalCores.Value - 1 : hardware.LogicalProcessors / 2, 1, 64);

    public static LocalResourcePlan Plan(LocalModelMemory model, LocalLlmHardwareProfile hardware,
        LocalResourceSettings preferences, int manualContext = 4096, int manualGpuLayers = 0, int manualThreads = 0,
        bool includeCapacity = false, int runtimeContextLimit = 32768)
    {
        LocalContextCapacity? maximum = null;
        var effective = preferences;
        if (preferences is { Enabled: true, AutoContext: true, ContextMode: "Maximum fit" })
        {
            maximum = EstimateMaximumContext(model, hardware, preferences, model.MaxContext ?? 32768, manualGpuLayers, manualThreads);
            effective = preferences with { ContextMode = "Custom", MaxContextTokens = Math.Max(1024, maximum.Tokens ?? 2048) };
        }
        var plan = PlanCore(model, hardware, effective, manualContext, manualGpuLayers, manualThreads);
        if (!includeCapacity) return plan;
        var capacity = maximum ?? EstimateMaximumContext(model, hardware, preferences, runtimeContextLimit, manualGpuLayers, manualThreads);
        return plan with { MaximumContextTokens = capacity.Tokens, MaximumContextDetail = capacity.Detail };
    }

    public static LocalContextCapacity EstimateMaximumContext(LocalModelMemory model, LocalLlmHardwareProfile hardware,
        LocalResourceSettings preferences, int runtimeContextLimit = 32768, int manualGpuLayers = 0, int manualThreads = 0)
    {
        if (model.WeightGiB is not > 0 || hardware.AvailableRamGiB is null)
            return new(null, "Maximum context is unknown until model size and available RAM are known.");
        var ceiling = Math.Clamp(runtimeContextLimit, 1024, 1048576);
        if (model.MaxContext is > 0) ceiling = Math.Min(ceiling, model.MaxContext.Value);
        // Search independently of the user's Auto target, in 1K increments, under the same offload policy.
        var low = 1; var high = ceiling / 1024; var best = 0;
        var settings = preferences with { AutoContext = false };
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (PlanCore(model, hardware, settings, middle * 1024, manualGpuLayers, manualThreads).Fits)
            { best = middle * 1024; low = middle + 1; }
            else high = middle - 1;
        }
        var limit = model.MaxContext is { } maximum ? $"Model limit {maximum:N0}; checked up to {ceiling:N0} tokens."
            : $"Model limit unknown; checked up to {ceiling:N0} tokens.";
        return new(best, best == 0 ? "Even 1,024 tokens do not fit this memory estimate. " + limit
            : $"Estimated maximum {best:N0} tokens after reserves, in 1K steps. {limit} Larger contexts may move more work onto CPU. This is not a runtime allocation guarantee.");
    }

    private static LocalResourcePlan PlanCore(LocalModelMemory model, LocalLlmHardwareProfile hardware,
        LocalResourceSettings preferences, int manualContext, int manualGpuLayers, int manualThreads)
    {
        var settings = preferences.Normalize();
        var threads = settings.Enabled && settings.AutoThreads ? Threads(hardware) : Math.Clamp(manualThreads, 0, 1024);
        var automaticContext = settings.Enabled && settings.AutoContext;
        var ceiling = automaticContext ? settings.ContextCeiling : Math.Clamp(manualContext, 1024, 1048576);
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
