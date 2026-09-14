using System.Reflection;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private static IReadOnlyList<LocalLlmCatalogModel> ReadDiscoverySnapshot()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ContextControl.DiscoveredModels.json");
        if (stream is null) return [];
        using var document = JsonDocument.Parse(stream);
        var checkedAt = document.RootElement.GetProperty("checkedAt").GetString();
        var models = new List<LocalLlmCatalogModel>();
        foreach (var row in document.RootElement.GetProperty("models").EnumerateArray())
        {
            var id = row.GetProperty("id").GetString()!;
            var source = row.GetProperty("source").GetString()!;
            var size = row.GetProperty("size").GetString()!;
            var gb = row.GetProperty("sizeGb").GetDouble();
            var context = row.GetProperty("context").GetString()!;
            var cloud = id.EndsWith(":cloud", StringComparison.Ordinal) || id.EndsWith("-cloud", StringComparison.Ordinal);
            var knownSize = gb > 0;
            var fitsSmall = knownSize && gb <= 3;
            // Full weight size matters even for MoE; activated parameters do not determine memory use.
            var recommended = knownSize ? Math.Ceiling(gb * 1.15 + 2) : 0;
            models.Add(new(id, id.Replace(':', ' '), "Unknown", "", cloud ? "Hosted only" : size.Length > 0 ? size + " published download" : "See model page",
                "See upstream model license", cloud ? "Hosted service; not a local download" : knownSize ? $"Allow at least {Math.Ceiling(gb * 1.3 + 4):0} GB RAM for CPU/offload; GPU needs context memory too" : "Check weight size and runtime support before downloading",
                context.Length > 0 ? context : "See model page", fitsSmall ? "4K to start" : "4K with CPU/offload if RAM permits",
                FourKSourceBudget, "Not benchmarked on this machine",
                $"Official library tag verified {checkedAt}. {(row.GetProperty("vision").GetBoolean() ? "Vision model. " : "")}" +
                "Download size is the published weight size, not total working memory. Check the source for architecture, license and minimum Ollama version. " + source,
                cloud || !knownSize ? 0 : Math.Ceiling(gb + 1), cloud ? 0 : recommended, !cloud && knownSize, cloud ? "" : $"ollama pull {id}", SourceUrl: source));
        }
        return models;
    }
}
