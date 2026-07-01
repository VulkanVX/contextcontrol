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

        await Task.WhenAll(hardwareTask, installedTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var hardware = await hardwareTask.ConfigureAwait(false);
        var installed = await installedTask.ConfigureAwait(false);
        progress?.Report(new LocalLlmTransferProgress(
            "Refreshing Models",
            "Resolving installed tags.",
            2,
            4,
            null,
            46));
        var catalogIds = Catalog.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installedAliases = installed.ModelIds
            .SelectMany(ExpandModelIdAliases)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = installed.ModelIds
            .Where(modelId => !ExpandModelIdAliases(modelId).Any(catalogIds.Contains))
            .OrderBy(modelId => modelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = installed.Reachable
            ? $"Ollama ready. {installed.ModelIds.Count} local model(s) installed."
            : installed.Status;

        return new LocalLlmRefreshResult(
            Catalog,
            installedAliases,
            unknown,
            hardware,
            installed.Installed,
            installed.Reachable,
            installed.ExecutablePath,
            status);
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
