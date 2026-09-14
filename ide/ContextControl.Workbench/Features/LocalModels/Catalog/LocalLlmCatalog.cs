// CC-DESC: Static local LLM catalog composition.

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private const string QwenIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/qwen.png";
    private const string PhiIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/phi4.png";
    private const string GraniteIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/granite3.png";
    private const string StabilityIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/provider-stability.png";
    private const string HuggingFaceIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/provider-huggingface.png";
    private const string BlackForestIconSource = "avares://ContextControl.Workbench/Assets/ModelIcons/blackforestlabs.png";
    private const string FourKSourceBudget = "4K: 150-300 LOC / 8k-12k chars; 8K: 350-700 LOC / 18k-28k chars.";
    private const string EightKSourceBudget = "8K: 350-700 LOC / 18k-28k chars; 16K+ only when the model stays fully in memory.";
    private const string LargeSourceBudget = "Use 8K-16K for CC work; larger advertised windows are for roomy GPUs and slower batch analysis.";
    private const string ImageGenContext = "Prompt-only image generation";
    private const string ImageGenBudget = "Prompt-only image generation; no chat context window.";

    private static readonly Lazy<IReadOnlyList<LocalLlmCatalogModel>> CatalogSource = new(BuildCatalog);

    public static IReadOnlyList<LocalLlmCatalogModel> Catalog => CatalogSource.Value;

    private static IReadOnlyList<LocalLlmCatalogModel> BuildCatalog()
    {
        return
        [
            ..ImageGenerationCatalog,
            ..QwenModernCatalog,
            ..DeepSeekAndCodingCatalog,
            ..CloudAndFrontierCatalog,
            ..ResearchAndMultilingualCatalog,
            ..HuggingFaceGgufCatalog,
            ..SmallAndEfficientCatalog,
            ..QwenOllamaCatalog,
            ..CoreOllamaCatalog,
        ];
    }
}
