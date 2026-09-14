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
    public async Task<LocalLlmRefreshResult> RefreshAsync(CancellationToken cancellationToken = default)
    {
        return await RefreshAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LocalLlmRefreshResult> RefreshAsync(
        IProgress<LocalLlmTransferProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Detecting GPU and Ollama state.",
            1,
            4,
            null,
            18));
        var hardwareTask = DetectHardwareAsync(cancellationToken);
        var installedTask = DetectInstalledModelsAsync(cancellationToken);
        var runtimesTask = DiscoverRuntimeModelsAsync(cancellationToken);

        await Task.WhenAll(hardwareTask, installedTask, runtimesTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var hardware = await hardwareTask.ConfigureAwait(false);
        var installed = await installedTask.ConfigureAwait(false);
        var runtimeModels = await runtimesTask.ConfigureAwait(false);
        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Resolving installed tags and capabilities.",
            2,
            4,
            null,
            46));
        var capabilities = installed.Reachable
            ? await DetectInstalledModelCapabilitiesAsync(installed.ModelIds, cancellationToken).ConfigureAwait(false)
            : EmptyModelCapabilities;
        var catalogIds = Catalog.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedAliases = installed.ModelIds
            .SelectMany(ExpandModelIdAliases)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        installedAliases.UnionWith(runtimeModels.Select(model => model.Id));
        var unknown = installed.ModelIds
            .Where(modelId => !ExpandModelIdAliases(modelId).Any(catalogIds.Contains))
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = installed.Reachable
            ? $"Ollama ready. {installed.ModelIds.Count} local model(s) installed."
            : installed.Status;
        if (runtimeModels.Count > 0) status += $" {runtimeModels.Count} model(s) available through other runtimes.";

        return new LocalLlmRefreshResult(
            Catalog.Concat(runtimeModels).ToArray(),
            installedAliases,
            ExpandInstalledModelSizes(installed.ModelSizes),
            ExpandInstalledModelCapabilities(capabilities),
            unknown,
            hardware,
            installed.Installed,
            installed.Reachable,
            installed.ExecutablePath,
            status);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyModelCapabilities =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

    private static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> DetectInstalledModelCapabilitiesAsync(
        IReadOnlySet<string> modelIds,
        CancellationToken cancellationToken)
    {
        if (modelIds.Count == 0)
        {
            return EmptyModelCapabilities;
        }

        var capabilities = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        using var http = CreateHttpClient(TimeSpan.FromSeconds(6));
        foreach (var modelId in modelIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var content = new StringContent(
                    JsonSerializer.Serialize(new OllamaShowRequest(modelId), JsonOptions),
                    Encoding.UTF8,
                    "application/json");
                using var response = await http.PostAsync(new Uri(OllamaBaseUri, "/api/show"), content, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var show = await JsonSerializer.DeserializeAsync<OllamaShowResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (show?.Capabilities is not { Count: > 0 })
                {
                    continue;
                }

                capabilities[modelId] = show.Capabilities
                    .Where(capability => !string.IsNullOrWhiteSpace(capability))
                    .Select(capability => capability.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
            }
        }

        return capabilities;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ExpandInstalledModelCapabilities(
        IReadOnlyDictionary<string, IReadOnlySet<string>> modelCapabilities)
    {
        if (modelCapabilities.Count == 0)
        {
            return EmptyModelCapabilities;
        }

        var expanded = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (modelId, capabilities) in modelCapabilities)
        {
            if (string.IsNullOrWhiteSpace(modelId) || capabilities.Count == 0)
            {
                continue;
            }

            foreach (var alias in ExpandModelIdAliases(modelId))
            {
                expanded.TryAdd(alias, capabilities);
            }
        }

        return expanded;
    }

    private static IReadOnlyDictionary<string, long> ExpandInstalledModelSizes(IReadOnlyDictionary<string, long> modelSizes)
    {
        if (modelSizes.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var expanded = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (modelId, size) in modelSizes)
        {
            if (string.IsNullOrWhiteSpace(modelId) || size <= 0)
            {
                continue;
            }

            foreach (var alias in ExpandModelIdAliases(modelId))
            {
                expanded.TryAdd(alias, size);
            }
        }

        return expanded;
    }

    private static IEnumerable<string> ExpandModelIdAliases(string modelId)
    {
        var clean = (modelId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            yield break;
        }

        yield return clean;
        const string latestSuffix = ":latest";
        if (clean.EndsWith(latestSuffix, StringComparison.OrdinalIgnoreCase))
        {
            yield return clean[..^latestSuffix.Length];
        }
        else if (!clean.Contains(':', StringComparison.Ordinal))
        {
            yield return $"{clean}{latestSuffix}";
        }
    }

}
