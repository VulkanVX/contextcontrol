// CC-DESC: Local LLM release-date, icon loading, and hardware-fit helpers.

// CC-DESC: Presents a local Ollama model candidate with fit, install, and pull state.

using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed partial class LocalLlmModelViewModel
{
    private static DateTime? ParseReleaseDate(string releaseDate)
    {
        if (string.IsNullOrWhiteSpace(releaseDate)
            || releaseDate.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTime.TryParse(
            releaseDate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.Date
            : null;
    }

    private static Bitmap? LoadIcon(string iconSource)
    {
        if (string.IsNullOrWhiteSpace(iconSource))
        {
            return null;
        }

        lock (IconCacheGate)
        {
            if (IconCache.TryGetValue(iconSource, out var cached))
            {
                return cached;
            }

            try
            {
                using var stream = AssetLoader.Open(new Uri(iconSource, UriKind.Absolute));
                var bitmap = new Bitmap(stream);
                IconCache[iconSource] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }

    private static ModelFit CalculateFit(LocalLlmCatalogModel model, LocalLlmHardwareProfile hardware)
    {
        if (hardware.MaxGpuMemoryGiB is not { } vram)
        {
            return model.WorksOnCpu
                ? new ModelFit(true, "CPU-safe", "GPU memory is unknown; this model can still run locally on CPU.")
                : new ModelFit(false, "GPU unknown", "GPU memory is unknown; install smaller CPU-safe models first.");
        }

        if (vram >= model.RecommendedVramGiB)
        {
            return new ModelFit(true, "Recommended", $"Detected GPU has about {vram:0.#} GB VRAM.");
        }

        if (vram >= model.MinimumVramGiB)
        {
            return new ModelFit(model.RecommendedVramGiB <= 4, "Usable", $"Detected GPU has about {vram:0.#} GB VRAM; keep context near 4K.");
        }

        if (model.WorksOnCpu && model.MinimumVramGiB <= 0)
        {
            return new ModelFit(true, "CPU fallback", $"Detected GPU has about {vram:0.#} GB VRAM; CPU mode is still practical.");
        }

        return new ModelFit(false, "Not ideal", $"Detected GPU has about {vram:0.#} GB VRAM; this model may offload to CPU.");
    }

    private sealed record ModelFit(bool IsRecommended, string Label, string Detail);
}
