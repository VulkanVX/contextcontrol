// CC-DESC: Provides the static local LLM catalog and shared service constants.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    public const string OllamaModelsEnvironmentVariable = "OLLAMA_MODELS";
    public const string OllamaDownloadPageUrl = "https://ollama.com/download/windows";
    private static readonly Regex SizeProgressRegex = new(
        @"(?<current>\d+(?:\.\d+)?)\s*(?<currentUnit>[KMGT]?i?B)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>[KMGT]?i?B)(?:\s+(?<speed>\d+(?:\.\d+)?)\s*(?<speedUnit>[KMGT]?i?B/s))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PercentRegex = new(
        @"(?<percent>\d+(?:\.\d+)?)%",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex AnsiRegex = new(
        @"\x1B\[[0-?]*[ -/]*[@-~]",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Uri OllamaBaseUri = new("http://127.0.0.1:11434");
    private static readonly HashSet<string> ImageOutputExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

}
