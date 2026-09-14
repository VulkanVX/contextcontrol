// CC-DESC: Composes the core Ollama catalog slices.

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private static IReadOnlyList<LocalLlmCatalogModel> CoreOllamaCatalog =>
    [
        ..CoreOllamaMetaCatalog,
        ..CoreOllamaGemmaDeepSeekCatalog,
        ..CoreOllamaMistralOpenAiCatalog,
        ..CoreOllamaResearchSmallCodingCatalog,
        ..CoreOllamaPhiGraniteCohereCatalog,
        ..CoreOllamaGlobalSpecializedCatalog,
        ..CoreOllamaRwkvCatalog,
    ];
}
