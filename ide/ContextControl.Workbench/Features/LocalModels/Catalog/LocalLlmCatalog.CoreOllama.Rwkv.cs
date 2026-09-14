// CC-DESC: RWKV local model slice.

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private static IReadOnlyList<LocalLlmCatalogModel> CoreOllamaRwkvCatalog { get; } =
    [
        CatalogModel(
            "mollysama/rwkv-7-g1f:1.5b",
            "RWKV-7 G1F 1.5B",
            "Apr 2026",
            "",
            "~1.3 GB Q4",
            "Apache 2.0",
            "6 GB RAM; CPU works",
            "1M",
            "8K-16K for responsive local use",
            LargeSourceBudget,
            "Alternative architecture local model",
            "Research lane for attention-free/RWKV-style local chat, long-context experiments, and weak-PC comparisons.",
            0,
            2,
            true),
        CatalogModel(
            "mollysama/rwkv-7-g1f:2.9b",
            "RWKV-7 G1F 2.9B",
            "Apr 2026",
            "",
            "~1.9 GB Q4",
            "Apache 2.0",
            "8 GB RAM; CPU works",
            "1M",
            "8K-16K for responsive local use",
            LargeSourceBudget,
            "Alternative architecture local model",
            "Niche RWKV chat route for long-context, constant-memory-style experiments, and local research comparisons.",
            0,
            4,
            true),
        CatalogModel(
            "mollysama/rwkv-7-g1f:7.2b",
            "RWKV-7 G1F 7.2B",
            "Apr 2026",
            "",
            "~4.6 GB Q4",
            "Apache 2.0",
            "16 GB RAM; 8 GB+ VRAM recommended",
            "1M",
            "8K-16K on 8 GB+",
            LargeSourceBudget,
            "Mid-size RWKV local model",
            "Alternative architecture route for local chat and long-context research beyond transformer-only baselines.",
            6.5,
            8,
            false),
        CatalogModel(
            "mollysama/rwkv-7-g1f:13.3b",
            "RWKV-7 G1F 13.3B",
            "Apr 2026",
            "",
            "~8.5 GB Q4",
            "Apache 2.0",
            "32 GB RAM; 16 GB+ VRAM recommended",
            "1M",
            "8K-16K on 16 GB+",
            LargeSourceBudget,
            "Large RWKV local model",
            "Higher-capacity RWKV route for alternative-architecture chat, long context, and efficiency research.",
            10,
            16,
            false)
    ];
}
