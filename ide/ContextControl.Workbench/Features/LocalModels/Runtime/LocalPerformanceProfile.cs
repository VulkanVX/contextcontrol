using System.Security.Cryptography;
using System.Text;

namespace ContextControl.Workbench.Services;

public sealed record LocalPerformanceOptions(int CpuThreads, int? DraftTokens = null);

public sealed record LocalPerformanceProfile(
    string ModelId, string Digest, string RuntimeVersion, string HardwareKey, int ContextTokens,
    LocalPerformanceOptions Options, double BaselineTokensPerSecond, double TokensPerSecond,
    long VramBytes, DateTime MeasuredUtc)
{
    public double Improvement => TokensPerSecond / BaselineTokensPerSecond - 1;
    public bool IsValid => !string.IsNullOrWhiteSpace(ModelId) && !string.IsNullOrWhiteSpace(Digest)
        && !string.IsNullOrWhiteSpace(RuntimeVersion) && !string.IsNullOrWhiteSpace(HardwareKey)
        && ContextTokens is >= 1024 and <= 32768 && Options is { CpuThreads: >= 1 and <= 64 }
        && (Options.DraftTokens is null or 0 or 2 or 4 or 8)
        && double.IsFinite(TokensPerSecond) && double.IsFinite(BaselineTokensPerSecond)
        && BaselineTokensPerSecond > 0 && TokensPerSecond >= BaselineTokensPerSecond * 1.05 && VramBytes >= 0;

    public static string HardwareFingerprint(LocalLlmHardwareProfile hardware)
    {
        var description = $"{hardware.CpuName}|{hardware.PhysicalCores}|{hardware.LogicalProcessors}|{hardware.TotalRamBytes}|"
            + string.Join(";", hardware.Gpus.Select(g => $"{g.Name}:{g.AdapterRamBytes}").Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(description)));
    }
}

// A real request takes priority over tuning, including requests made by another service instance.
internal static class LocalPerformanceActivity
{
    private static readonly object Gate = new();
    private static int _chats;
    private static CancellationTokenSource? _tuning;
    internal static IDisposable Chat()
    {
        lock (Gate) { _chats++; _tuning?.Cancel(); }
        return new Release(() => { lock (Gate) _chats--; });
    }
    internal static IDisposable Tune(CancellationTokenSource cancellation)
    {
        lock (Gate)
        {
            if (_chats != 0 || _tuning is not null) throw new InvalidOperationException("Wait for the current chat or tuning run to finish.");
            _tuning = cancellation;
        }
        return new Release(() => { lock (Gate) { _tuning = null; cancellation.Dispose(); } });
    }
    private sealed class Release(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
