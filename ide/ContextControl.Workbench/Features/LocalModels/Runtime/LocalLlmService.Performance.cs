using System.Net.Http;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

public sealed partial class LocalLlmService
{
    private IReadOnlyList<LocalPerformanceProfile> _performanceProfiles = [];
    public void ConfigurePerformance(IEnumerable<LocalPerformanceProfile> profiles, bool enabled)
        => _performanceProfiles = enabled ? profiles.Where(p => p is { IsValid: true }).Take(64).ToArray() : [];

    private async Task<OllamaChatOptions?> PerformanceOptionsAsync(LocalLlmRequest request, CancellationToken token)
    {
        var options = ResourceOptions(request);
        if (!_resourceSettings.Enabled || !_resourceSettings.AutoThreads || !_resourceSettings.AutoGpuLayers
            || options?.NumContext is not > 0 || request.ImagePaths is { Count: > 0 }) return options;
        var hardware = LocalPerformanceProfile.HardwareFingerprint(_resourceHardware);
        var profile = _performanceProfiles.FirstOrDefault(p => p.ModelId == request.ModelId && p.ContextTokens == options.NumContext
            && p.HardwareKey == hardware);
        if (profile is null) return options;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            using var client = _chatHandler is null ? new HttpClient() : new HttpClient(_chatHandler, false);
            var tuner = new LocalPerformanceTuner(client);
            var identity = await tuner.IdentityAsync(request.ModelId, timeout.Token).ConfigureAwait(false);
            if (profile.Digest != identity.Digest || profile.RuntimeVersion != identity.Version) return options;
            // A cold model or changed split uses the ordinary planner. Apply measurements only to the matching warm allocation.
            if (!profile.MatchesAllocation(await tuner.AllocationAsync(request.ModelId, profile.ContextTokens, timeout.Token).ConfigureAwait(false))) return options;
            return options with { CpuThreads = profile.Options.CpuThreads, DraftTokens = profile.Options.DraftTokens };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or IOException or KeyNotFoundException)
        { return options; }
    }
}
