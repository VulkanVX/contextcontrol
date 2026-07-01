using System.Diagnostics;
using System.Text.Json.Nodes;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class SmokeTestHelpers
{
internal static string? FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "ccDir.ps1"))
            && File.Exists(Path.Combine(directory.FullName, "cc.ps1")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

internal static void RequirePhase1Valid(ContextPromptBuilder promptBuilder, ContextDirManifest manifest, string text, string kind)
{
    var result = ContextPhase1RequestValidator.Validate(promptBuilder.ParsePhase1RequestLines(text), manifest, "");
    if (!result.IsValid || !result.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected valid Phase 1 {kind} request. Error: {result.Error}");
    }
}

internal static void RequirePhase1Valid(string root, ContextPromptBuilder promptBuilder, ContextDirManifest manifest, string text, string kind)
{
    var result = ContextPhase1RequestValidator.Validate(promptBuilder.ParsePhase1RequestLines(text), manifest, root);
    if (!result.IsValid || !result.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected valid Phase 1 {kind} request. Error: {result.Error}");
    }
}

internal static void RequirePhase1Invalid(string root, ContextPromptBuilder promptBuilder, ContextDirManifest manifest, string text, string expectedErrorPart)
{
    var result = ContextPhase1RequestValidator.Validate(promptBuilder.ParsePhase1RequestLines(text), manifest, root);
    if (result.IsValid || !result.Error.Contains(expectedErrorPart, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected invalid Phase 1 request containing '{expectedErrorPart}'. Actual: {result.Error}");
    }
}

internal static void RequireContains(ContextFileResolveResult result, string requestLine)
{
    if (!result.RequestLines.Contains(requestLine, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Expected resolver output to contain '{requestLine}'. Actual: {result.RequestText}");
    }
}

internal static void RequireNotContains(ContextFileResolveResult result, string requestLine)
{
    if (result.RequestLines.Contains(requestLine, StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Resolver output unexpectedly contained '{requestLine}'. Actual: {result.RequestText}");
    }
}

internal static void RequireTextContains(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text to contain '{expected}'. Actual: {text}");
    }
}

internal static void RequireTextNotContains(string text, string unexpected)
{
    if (text.Contains(unexpected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected text not to contain '{unexpected}'. Actual: {text}");
    }
}

internal static bool HasAdjacentArguments(IReadOnlyList<string> arguments, string name, string value)
{
    for (var index = 0; index < arguments.Count - 1; index++)
    {
        if (arguments[index].Equals(name, StringComparison.Ordinal)
            && arguments[index + 1].Equals(value, StringComparison.Ordinal))
        {
            return true;
        }
    }

    return false;
}

internal static double NodeNumberByPath(IReadOnlyList<JsonObject> nodes, string path, string propertyName)
{
    var node = nodes.FirstOrDefault(item => string.Equals(item["path"]?.GetValue<string>(), path, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Expected graph JSON to contain node path '{path}'.");
    return node[propertyName]?.GetValue<double>()
        ?? throw new InvalidOperationException($"Expected graph JSON node '{path}' to contain numeric '{propertyName}'.");
}

internal static LocalLlmModelViewModel ModelView(string modelId)
{
    var model = LocalLlmService.Catalog.First(item => item.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    var viewModel = new LocalLlmModelViewModel(model);
    viewModel.ApplyState(
        isInstalled: false,
        isAvailable: false,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: false);
    return viewModel;
}

internal static void RequireImageDependency(string modelId, string dependencyId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.IsImageGenerationModel
        || !viewModel.RequiresManualBackend
        || !viewModel.DependencyId.Equals(dependencyId, StringComparison.OrdinalIgnoreCase)
        || !viewModel.CanInstallDependency
        || !viewModel.PullButtonLabel.Equals("Install dep", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should install the {dependencyId} image backend, not try an Ollama model pull.");
    }
}

internal static void RequireOllamaDownload(string modelId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.UsesOllamaPull
        || viewModel.RequiresManualBackend
        || !viewModel.CanPull
        || !viewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should remain a one-click Ollama download.");
    }
}

internal static void RequireOllamaImagePlatformGate(string modelId)
{
    var viewModel = ModelView(modelId);
    if (!viewModel.IsImageGenerationModel
        || !viewModel.IsOllamaImageRoute
        || !viewModel.UsesOllamaPull)
    {
        throw new InvalidOperationException($"{modelId} should remain classified as an Ollama image-generation route.");
    }

    if (OperatingSystem.IsMacOS())
    {
        if (!viewModel.IsBackendPlatformSupported
            || !viewModel.CanPull
            || !viewModel.PullButtonLabel.Equals("Download", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{modelId} should stay downloadable on macOS where Ollama image generation is supported.");
        }

        return;
    }

    if (viewModel.IsBackendPlatformSupported
        || viewModel.CanPull
        || !viewModel.InstallLabel.Equals("Mac only", StringComparison.OrdinalIgnoreCase)
        || !viewModel.PullButtonLabel.Equals("Mac only", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should be disabled on non-macOS hosts to avoid Ollama HTTP 500/EOF image-generation failures.");
    }

    viewModel.ApplyState(
        isInstalled: true,
        isAvailable: true,
        new LocalLlmHardwareProfile(Array.Empty<LocalLlmGpuInfo>()),
        isBackendDependencyReady: true);
    if (!viewModel.CanUninstall
        || !viewModel.PullButtonLabel.Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{modelId} should still be removable when it was already pulled on an unsupported host.");
    }
}

internal static void RequireBackendDependenciesAutoinstallable()
{
    string[] dependencyIds =
    [
        "ollama",
        "llama_cpp_server",
        "lm_studio",
        "koboldcpp",
        "mlx_lm",
        "mlc_llm",
        "transformers",
        "diffusers",
        "stable_diffusion_cpp",
        "vllm",
        "sglang",
        "onnxruntime_genai",
        "openvino_genai",
        "tensorrt_llm",
        "exllamav2_tabbyapi",
        "bitnet_cpp",
        "rwkv_runner"
    ];

    var missing = dependencyIds
        .Select(CreateDependency)
        .Where(dependency => !dependency.HasSafeAutomaticInstaller)
        .Select(dependency => dependency.Id)
        .ToArray();

    if (missing.Length > 0)
    {
        throw new InvalidOperationException($"Dependencies should expose one-click installers on this platform: {string.Join(", ", missing)}");
    }
}

internal static LlmBackendDependencyViewModel CreateDependency(string id)
{
    return new LlmBackendDependencyViewModel(
        id,
        id,
        "test",
        "test",
        "test",
        "test",
        "test",
        isRequired: false,
        isRecommended: false);
}
}
