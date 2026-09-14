using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private LocalRuntimeProfile[] _runtimeProfiles = LocalRuntimeProfile.Defaults.ToArray();
    private IReadOnlyDictionary<string, RuntimeBinding> _runtimeBindings = new Dictionary<string, RuntimeBinding>();
    public IReadOnlyList<LocalRuntimeStatus> RuntimeStatuses { get; private set; } = [];
    private sealed record RuntimeBinding(LocalRuntimeProfile Profile, string ModelId);
    private sealed record RuntimeDiscovery(LocalRuntimeProfile Profile, IReadOnlyList<string> ModelIds, string Status, bool Reachable);

    public void ConfigureRuntimes(IEnumerable<LocalRuntimeProfile> profiles) => _runtimeProfiles = profiles
        .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
        .Select(profile => profile with { Id = profile.Id.Trim(), Name = string.IsNullOrWhiteSpace(profile.Name) ? profile.Id : profile.Name.Trim(),
            Endpoint = profile.Endpoint ?? "", ApiKeyEnvironmentVariable = profile.ApiKeyEnvironmentVariable ?? "",
            ContextTokens = Math.Clamp(profile.ContextTokens, 1024, 1048576), ModelPath = profile.ModelPath ?? "" })
        .DistinctBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase).Take(16).ToArray();

    internal static string RuntimeModelId(string runtime, string model) => "runtime:" + Uri.EscapeDataString(runtime) + ":" + Uri.EscapeDataString(model);

    private static void AddRuntimeAuthorization(HttpRequestMessage request, LocalRuntimeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable)) return;
        var key = Environment.GetEnvironmentVariable(profile.ApiKeyEnvironmentVariable.Trim());
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException($"Set the {profile.ApiKeyEnvironmentVariable.Trim()} environment variable for {profile.Name}.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
    }

    internal async Task<IReadOnlyList<LocalLlmCatalogModel>> DiscoverRuntimeModelsAsync(CancellationToken cancellationToken)
    {
        var profiles = _runtimeProfiles.ToArray();
        var discoveries = await Task.WhenAll(profiles.Select(async profile =>
        {
            if (!profile.Enabled) return new RuntimeDiscovery(profile, [], "Disabled", false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                using var http = _chatHandler is null ? new HttpClient() : new HttpClient(_chatHandler, disposeHandler: false);
                using var request = new HttpRequestMessage(HttpMethod.Get, profile.ApiUri("models"));
                AddRuntimeAuthorization(request, profile);
                using var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return new RuntimeDiscovery(profile, [], $"API returned {(int)response.StatusCode}", false);
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false));
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    return new RuntimeDiscovery(profile, [], "Not a compatible models API", false);
                var ids = data.EnumerateArray().Select(item => JsonString(item, "id")).Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!).Distinct(StringComparer.Ordinal).Take(500).ToArray();
                return new RuntimeDiscovery(profile, ids, ids.Length == 0 ? "Connected · load a chat model in this runtime" : $"Connected · {ids.Length} model(s)", true);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                return new RuntimeDiscovery(profile, [], ex is OperationCanceledException ? "Not responding" : ex is HttpRequestException ? "Not running or unreachable" : ex.Message, false);
            }
        })).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        RuntimeStatuses = discoveries.Select(result => new LocalRuntimeStatus(result.Profile.Id, result.Profile.Name, result.Reachable, result.ModelIds.Count, result.Status)).ToArray();
        var bindings = new Dictionary<string, RuntimeBinding>(StringComparer.Ordinal);
        var catalog = new List<LocalLlmCatalogModel>();
        foreach (var discovery in discoveries)
            foreach (var modelId in discovery.ModelIds)
            {
                var id = RuntimeModelId(discovery.Profile.Id, modelId);
                bindings[id] = new RuntimeBinding(discovery.Profile, modelId);
                var context = Math.Clamp(discovery.Profile.ContextTokens, 1024, 1048576).ToString(System.Globalization.CultureInfo.InvariantCulture);
                catalog.Add(new LocalLlmCatalogModel(id, modelId + " · " + discovery.Profile.Name, "Unknown", "", "Runtime managed", "See model card",
                    "Memory and architecture support depend on the connected runtime", context, context, "Depends on context", "Measured while generating",
                    $"Discovered from {discovery.Profile.Name}. Select this entry to chat through that server. Model loading and weights are managed by its runtime.",
                    0, 0, true, "", discovery.Profile.Id, modelId, discovery.Profile.Name));
            }
        _runtimeBindings = bindings;
        return catalog;
    }

    private static string? JsonString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var child) && child.ValueKind == JsonValueKind.String ? child.GetString() : null;

    private async Task<LocalLlmChatResult> SendCompatibleChatAsync(LocalLlmRequest request, IProgress<LocalLlmGenerationProgress>? progress,
        IProgress<string>? terminal, CancellationToken cancellationToken)
    {
        if (!_runtimeBindings.TryGetValue(request.ModelId, out var binding))
            return new(false, "This model's runtime is unavailable. Start its server and refresh LLMs.");
        if (string.IsNullOrWhiteSpace(request.Prompt)) return new(false, "Write a chat message first.");
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var profile = binding.Profile;
            object userContent = request.Prompt;
            if (request.ImagePaths is { Count: > 0 })
            {
                var encoded = await EncodeImageAttachmentsAsync(request.ImagePaths, cancellationToken).ConfigureAwait(false);
                if (encoded.Count == 0) return new(false, "No readable image attachments were found.");
                var parts = new List<object> { new { type = "text", text = request.Prompt } };
                parts.AddRange(encoded.Select(value => (object)new { type = "image_url", image_url = new { url = "data:" + ImageMimeType(value) + ";base64," + value } }));
                userContent = parts;
            }
            var payload = new Dictionary<string, object?>
            {
                ["model"] = binding.ModelId,
                ["messages"] = new[] { new { role = "user", content = userContent } },
                ["stream"] = true,
                ["stream_options"] = new { include_usage = true },
                ["max_tokens"] = request.MaxOutputTokens is > 0 ? request.MaxOutputTokens.Value : Math.Clamp(profile.ContextTokens / 3, 256, 4096)
            };
            if (request.Think is { } think) payload["chat_template_kwargs"] = new { enable_thinking = think };
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            var token = deadline.Token;
            using var http = _chatHandler is null ? new HttpClient { Timeout = Timeout.InfiniteTimeSpan } : new HttpClient(_chatHandler, disposeHandler: false);
            using var outgoing = new HttpRequestMessage(HttpMethod.Post, profile.ApiUri("chat/completions"))
            { Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json") };
            AddRuntimeAuthorization(outgoing, profile);
            terminal?.Report($"> {profile.Name} chat {binding.ModelId}");
            progress?.Report(new("Loading model and preparing prompt…", null, null, null, null, null, null, null, false));
            using var response = await http.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, $"{profile.Name} returned {(int)response.StatusCode}: {FirstLine(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false))}");
            var answer = new StringBuilder();
            var thinking = new StringBuilder();
            string? finishReason = null;
            long? inputTokens = null, outputTokens = null, cachedTokens = null, reasoningTokens = null;
            var completed = false;
            var terminalDone = false;
            void Consume(string json)
            {
                if (json.Trim() == "[DONE]") { completed = true; terminalDone = true; return; }
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException("Generation failed: " +
                    (error.ValueKind == JsonValueKind.String ? error.GetString() : JsonString(error, "message") ?? "server error"));
                static long? Count(JsonElement value, string key) => value.TryGetProperty(key, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out var count) && count >= 0 ? count : null;
                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    inputTokens = Count(usage, "prompt_tokens") ?? inputTokens;
                    outputTokens = Count(usage, "completion_tokens") ?? outputTokens;
                    if (usage.TryGetProperty("prompt_tokens_details", out var details) && details.ValueKind == JsonValueKind.Object) cachedTokens = Count(details, "cached_tokens");
                    if (usage.TryGetProperty("completion_tokens_details", out details) && details.ValueKind == JsonValueKind.Object) reasoningTokens = Count(details, "reasoning_tokens");
                }
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return;
                foreach (var choice in choices.EnumerateArray().Take(1))
                {
                    finishReason = JsonString(choice, "finish_reason") ?? finishReason;
                    if (finishReason is not null) completed = true;
                    if (!choice.TryGetProperty("delta", out var delta) && !choice.TryGetProperty("message", out delta)) continue;
                    var text = JsonString(delta, "content");
                    if (text is null && delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        text = string.Concat(content.EnumerateArray().Select(part => JsonString(part, "text")));
                    var trace = JsonString(delta, "reasoning_content") ?? JsonString(delta, "reasoning") ?? JsonString(delta, "thinking");
                    if (!string.IsNullOrEmpty(text)) answer.Append(text);
                    if (!string.IsNullOrEmpty(trace)) thinking.Append(trace);
                    progress?.Report(new(!string.IsNullOrEmpty(trace) && answer.Length == 0 ? "Model is thinking…" : "Generating local response…",
                        text, inputTokens, outputTokens, null, null, null, null, false, trace));
                }
            }
            if (response.Content.Headers.ContentType?.MediaType == "application/json")
                Consume(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            else
            {
                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var data = new StringBuilder();
                while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                    {
                        if (data.Length > 0) { Consume(data.ToString()); data.Clear(); }
                        if (terminalDone) break;
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (data.Length > 0) data.Append('\n');
                        data.Append(line.AsSpan(5).TrimStart());
                    }
                }
                if (data.Length > 0) Consume(data.ToString());
            }
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            // Compatible servers may buffer several tokens into one chunk. Timing from the first
            // visible chunk would claim impossible speeds; report conservative end-to-end throughput.
            var stats = new LocalLlmUsageStats(inputTokens, outputTokens, (long)(elapsed * 1e9), null, null,
                (long)(elapsed * 1e9), cachedTokens, reasoningTokens);
            if (!completed) return new(false, "The runtime's response stream ended before completion. Please retry.", Stats: stats);
            var visible = System.Text.RegularExpressions.Regex.Replace(answer.ToString(), @"<think>.*?(?:</think>|$)", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (visible.Length == 0) return new(false, thinking.Length > 0 || answer.Length > 0 ? ThinkingOnlyStatus + " Try thinking off or a larger context window." : "The runtime returned no answer.", Stats: stats);
            var message = BuildFinalChatAnswer(answer.ToString(), thinking.ToString());
            if (finishReason == "length") return new(false, "The runtime reached its output or context limit.", message + "\n\n**Response incomplete.** Increase the output/context limit or shorten the prompt, then retry.", stats, OutputLimited: true);
            progress?.Report(new("Generation complete.", null, inputTokens, outputTokens, stats.TotalDurationNanoseconds, null, null, stats.EvalDurationNanoseconds, true));
            terminal?.Report($"done: {stats.Summary}");
            return new(true, $"Local chat completed with {profile.Name} / {binding.ModelId}.", message, stats);
        }
        catch (OperationCanceledException) { return new(false, cancellationToken.IsCancellationRequested ? "Response was stopped by the user." : "Local chat timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or NotSupportedException)
        { return new(false, "The local runtime could not complete this request: " + ex.Message); }
    }

    internal static string ImageMimeType(string value) => value.StartsWith("/9j/", StringComparison.Ordinal) ? "image/jpeg"
        : value.StartsWith("iVBOR", StringComparison.Ordinal) ? "image/png"
        : value.StartsWith("UklG", StringComparison.Ordinal) ? "image/webp"
        : value.StartsWith("R0lG", StringComparison.Ordinal) ? "image/gif"
        : throw new NotSupportedException("This image format is not supported by the chat server. Use PNG, JPEG, WebP or GIF.");
}
