using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed record LocalRuntimeAllocation(bool Running, string Status, long? TotalBytes = null,
    long? VramBytes = null, long? ResidentRamBytes = null, int? ContextTokens = null,
    int? CpuThreads = null, int? GpuLayers = null, string RamSource = "", string VramSource = "");

internal sealed record OllamaResourceReading(LocalRuntimeAllocation Allocation, LocalModelMemory? Memory,
    IReadOnlyDictionary<string, LocalRuntimeAllocation>? Loaded = null);

public sealed partial class LocalLlmService
{
    private readonly Dictionary<string, (DateTime Time, LocalModelMemory Memory)> _resourceMetadataCache = new(StringComparer.OrdinalIgnoreCase);

    internal async Task<OllamaResourceReading> ReadOllamaResourcesAsync(string modelId, double? weightGiB, CancellationToken token)
    {
        using var http = _chatHandler is null ? CreateHttpClient(TimeSpan.FromSeconds(3)) : new HttpClient(_chatHandler, false);
        LocalRuntimeAllocation allocation;
        Dictionary<string, LocalRuntimeAllocation> loaded = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var response = await http.GetAsync(new Uri(OllamaBaseUri, "/api/ps"), token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            allocation = ParseOllamaAllocation(document.RootElement, modelId);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
                foreach (var model in models.EnumerateArray())
                    if (model.ValueKind == JsonValueKind.Object && (JsonString(model, "name") ?? JsonString(model, "model")) is { Length: > 0 } id)
                        loaded[id] = ParseOllamaAllocation(document.RootElement, id);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            token.ThrowIfCancellationRequested();
            return new(new(false, "Ollama resource report unavailable"), null);
        }
        if (_resourceMetadataCache.TryGetValue(modelId, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromMinutes(5))
            return new(allocation, cached.Memory with { WeightGiB = weightGiB ?? cached.Memory.WeightGiB }, loaded);
        if (string.IsNullOrWhiteSpace(modelId)) return new(allocation, null, loaded);
        try
        {
            using var body = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { model = modelId }), System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(new Uri(OllamaBaseUri, "/api/show"), body, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("model_info", out var info)
                && info.ValueKind == JsonValueKind.Object)
            {
                var metadata = ReadOllamaMemory(info, weightGiB);
                _resourceMetadataCache[modelId] = (DateTime.UtcNow, metadata);
                return new(allocation, metadata, loaded);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { token.ThrowIfCancellationRequested(); }
        return new(allocation, null, loaded);
    }

    internal static LocalRuntimeAllocation ParseOllamaAllocation(JsonElement root, string modelId)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return new(false, "Ollama resource report unavailable");
        static string Normalize(string id) => id.Contains(':') ? id : id + ":latest";
        foreach (var model in models.EnumerateArray())
        {
            if (model.ValueKind != JsonValueKind.Object) continue;
            var id = JsonString(model, "name") ?? JsonString(model, "model") ?? "";
            if (!Normalize(id).Equals(Normalize(modelId), StringComparison.OrdinalIgnoreCase)) continue;
            long? Bytes(string key) => model.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out var bytes) && bytes >= 0 ? bytes : null;
            var context = Bytes("context_length");
            return new(true, "Reported by Ollama", Bytes("size"), Bytes("size_vram"),
                ContextTokens: context is > 0 and <= int.MaxValue ? (int)context.Value : null,
                VramSource: "Ollama model VRAM");
        }
        return new(false, "Model is not loaded");
    }

    internal static LocalModelMemory ReadOllamaMemory(JsonElement info, double? weight)
    {
        if (info.ValueKind != JsonValueKind.Object) return new(weight);
        var architecture = JsonString(info, "general.architecture") ?? "";
        double? Number(string key) => info.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var n) && double.IsFinite(n) && n > 0 ? n : null;
        int? Integer(string key) => Number(key) is { } value && value <= int.MaxValue ? (int)value : null;
        var layers = Integer(architecture + ".block_count");
        var heads = Number(architecture + ".attention.head_count");
        var kvHeads = Number(architecture + ".attention.head_count_kv") ?? heads;
        var length = Number(architecture + ".attention.key_length")
            ?? (heads is > 0 ? Number(architecture + ".embedding_length") / heads : null);
        var valueLength = Number(architecture + ".attention.value_length") ?? length;
        return new(weight, layers, layers * kvHeads * (length + valueLength) * 2,
            Integer(architecture + ".context_length"), EstimateSplitWithoutLayers: true);
    }

    internal static async Task<LocalLlmHardwareProfile> SampleResourceHardwareAsync(LocalLlmHardwareProfile previous, CancellationToken token)
    {
        var gpus = await DetectNvidiaGpusAsync(token).ConfigureAwait(false);
        var memory = LocalSystemMemory.Read();
        token.ThrowIfCancellationRequested();
        return previous with { Gpus = gpus.Count > 0 ? gpus : previous.Gpus.Select(g => g with { AvailableRamBytes = null }).ToArray(),
            TotalRamBytes = memory.Total, AvailableRamBytes = memory.Available };
    }
}
