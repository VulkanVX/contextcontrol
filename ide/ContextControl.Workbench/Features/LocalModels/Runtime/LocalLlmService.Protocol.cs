// CC-DESC: Local LLM service slice extracted from LocalLlmService.cs.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private sealed record InstalledModelsResult(
        bool Installed,
        bool Reachable,
        IReadOnlySet<string> ModelIds,
        IReadOnlyDictionary<string, long> ModelSizes,
        string? ExecutablePath,
        string Status);

    private sealed record ProcessResult(bool Started, int ExitCode, string StandardOutput, string StandardError, bool TimedOut = false)
    {
        public static ProcessResult NotStarted() => new(false, -1, "", "");
    }

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelTag>? Models);

    private sealed record OllamaModelTag(string? Name, string? Model, long? Size);

    private sealed record OllamaShowRequest(string Model);

    private sealed record OllamaShowResponse(IReadOnlyList<string>? Capabilities);

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaChatMessage> Messages,
        bool Stream,
        OllamaChatOptions? Options = null,
        bool? Think = null);

    private sealed record OllamaChatOptions(
        [property: JsonPropertyName("num_ctx")] int? NumContext,
        [property: JsonPropertyName("num_predict")] int? MaxOutputTokens = null,
        [property: JsonPropertyName("num_thread")] int? CpuThreads = null,
        [property: JsonPropertyName("num_gpu")] int? GpuLayers = null);

    private sealed record OllamaChatMessage(
        string Role,
        string Content,
        IReadOnlyList<string>? Images = null,
        string? Thinking = null);

    private sealed record OllamaChatResponse(
        OllamaChatMessage? Message,
        bool? Done,
        [property: JsonPropertyName("total_duration")] long? TotalDuration,
        [property: JsonPropertyName("load_duration")] long? LoadDuration,
        [property: JsonPropertyName("prompt_eval_count")] long? PromptEvalCount,
        [property: JsonPropertyName("prompt_eval_duration")] long? PromptEvalDuration,
        [property: JsonPropertyName("eval_count")] long? EvalCount,
        [property: JsonPropertyName("eval_duration")] long? EvalDuration,
        [property: JsonPropertyName("done_reason")] string? DoneReason = null,
        string? Error = null);
}
