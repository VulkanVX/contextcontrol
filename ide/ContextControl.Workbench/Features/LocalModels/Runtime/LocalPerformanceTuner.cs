using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ContextControl.Workbench.Services;

internal sealed record LocalPerformanceIdentity(string ModelId, string Digest, string Version);
internal sealed record LocalPerformanceSample(LocalPerformanceOptions Options, int Tokens, double Seconds,
    double FirstTokenSeconds, string Text)
{
    public double TokensPerSecond => Tokens / Seconds;
}
internal sealed record LocalPerformanceResult(LocalPerformanceProfile? Profile, string Status,
    IReadOnlyList<LocalPerformanceSample> Samples);

// Measures warm, single-stream decoding. It never downloads weights or changes server-wide options.
internal sealed class LocalPerformanceTuner(HttpClient http)
{
    private static readonly Uri BaseUri = new("http://127.0.0.1:11434");
    internal static bool IsLocalModel(string id) => !string.IsNullOrWhiteSpace(id)
        && !id.StartsWith("runtime:", StringComparison.OrdinalIgnoreCase)
        && !id.Contains(":cloud", StringComparison.OrdinalIgnoreCase) && !id.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);

    internal async Task<LocalPerformanceIdentity> IdentityAsync(string id, CancellationToken token)
    {
        if (!IsLocalModel(id)) throw new InvalidOperationException("Speed tuning supports installed local Ollama chat models.");
        var tagsTask = ReadAsync("/api/tags", token);
        var versionTask = ReadAsync("/api/version", token);
        // Dispose both documents even if one request fails.
        try { await Task.WhenAll(tagsTask, versionTask).ConfigureAwait(false); }
        catch { if (tagsTask.IsCompletedSuccessfully) tagsTask.Result.Dispose(); if (versionTask.IsCompletedSuccessfully) versionTask.Result.Dispose(); throw; }
        using var tags = tagsTask.Result;
        using var version = versionTask.Result;
        var match = tags.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m =>
            Text(m, "name") == id || Text(m, "model") == id || Text(m, "name") == id + ":latest");
        var digest = Text(match, "digest");
        var runtimeVersion = Text(version.RootElement, "version");
        if (digest.Length == 0 || runtimeVersion.Length == 0) throw new InvalidOperationException("The selected model is not fully installed, or its runtime identity is unavailable.");
        return new(id, digest, runtimeVersion);
    }

    internal async Task<long?> AllocationAsync(string id, int context, CancellationToken token)
    {
        using var ps = await ReadAsync("/api/ps", token).ConfigureAwait(false);
        var match = ps.RootElement.GetProperty("models").EnumerateArray().FirstOrDefault(m =>
            (Text(m, "name") == id || Text(m, "model") == id || Text(m, "name") == id + ":latest")
            && Number(m, "context_length") == context);
        return match.ValueKind == JsonValueKind.Undefined ? null : Number(match, "size_vram");
    }

    internal async Task<LocalPerformanceResult> TuneAsync(string id, int context, LocalLlmHardwareProfile hardware,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (context is < 1024 or > 32768) throw new InvalidOperationException("Choose a context between 1,024 and 32,768 tokens for tuning.");
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IDisposable lease;
        try { lease = LocalPerformanceActivity.Tune(deadline); }
        catch { deadline.Dispose(); throw; }
        using (lease)
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(15));
            var token = deadline.Token;
            var identity = await IdentityAsync(id, token).ConfigureAwait(false);
            using var showResponse = await http.PostAsJsonAsync(new Uri(BaseUri, "/api/show"), new { model = id }, token).ConfigureAwait(false);
            showResponse.EnsureSuccessStatusCode();
            using var show = await JsonDocument.ParseAsync(await showResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
            var capabilities = show.RootElement.TryGetProperty("capabilities", out var caps) ? caps.EnumerateArray().Select(c => c.GetString()).ToArray() : [];
            if (!capabilities.Contains("completion")) throw new InvalidOperationException("This model does not advertise text completion support.");
            var thinking = capabilities.Contains("thinking");
            // Probe draft lengths only when the installed model already opts into MTP. Unsupported models keep their defaults.
            var draft = Regex.Match(Text(show.RootElement, "parameters"), @"(?m)^draft_num_predict\s+(\d+)\s*$");
            var hasMtp = draft.Success && int.TryParse(draft.Groups[1].Value, out var count) && count > 0;
            var baseline = new LocalPerformanceOptions(LocalResourcePlanner.Threads(hardware));
            var samples = new List<LocalPerformanceSample>();
            var physical = Math.Clamp(hardware.PhysicalCores ?? hardware.LogicalProcessors / 2, 1, 64);
            var candidates = new[] { Math.Max(1, physical / 2), physical, Math.Clamp(hardware.LogicalProcessors, 1, 64) }
                .Distinct().Where(t => t != baseline.CpuThreads).Select(t => new LocalPerformanceOptions(t)).ToList();
            progress?.Report($"Tuning {id}: warming the model at {context:N0} context…");
            await SampleAsync(id, context, baseline, thinking, 8, 0, token).ConfigureAwait(false);
            var allocation = await AllocationAsync(id, context, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Ollama did not report the requested context allocation.");
            var allocations = new Dictionary<LocalPerformanceOptions, long> { [baseline] = allocation };
            var initial = await Measure(baseline, 48, 0, "Baseline").ConfigureAwait(false);
            var best = initial;
            foreach (var candidate in candidates)
            {
                var sample = await Measure(candidate, 48, 0, "CPU sweep").ConfigureAwait(false);
                if (sample.TokensPerSecond > best.TokensPerSecond) best = sample;
            }
            if (hasMtp)
            {
                foreach (var length in new[] { 0, 2, 8 })
                {
                    var sample = await Measure(best.Options with { DraftTokens = length }, 48, 0, "Draft sweep").ConfigureAwait(false);
                    if (sample.TokensPerSecond > best.TokensPerSecond) best = sample;
                }
            }
            if (best.Options == baseline)
                return new(null, $"Baseline was fastest in the sweep ({initial.TokensPerSecond:0.0} tok/s). No override saved.", samples);

            // Alternate order and use two prompts. A single lucky sample must not replace defaults.
            var bases = new List<LocalPerformanceSample>();
            var winners = new List<LocalPerformanceSample>();
            for (var round = 0; round < 2; round++)
            {
                if (round == 0) bases.Add(await Measure(baseline, 96, round + 1, "Verify baseline").ConfigureAwait(false));
                winners.Add(await Measure(best.Options, 96, round + 1, "Verify candidate").ConfigureAwait(false));
                if (round != 0) bases.Add(await Measure(baseline, 96, round + 1, "Verify baseline").ConfigureAwait(false));
            }
            var baseSpeed = bases.Sum(s => s.Tokens) / bases.Sum(s => s.Seconds);
            var speed = winners.Sum(s => s.Tokens) / winners.Sum(s => s.Seconds);
            if (!IsImprovement(bases, winners))
                return new(null, $"No repeatable gain: baseline {baseSpeed:0.0}, candidate {speed:0.0} tok/s. No override saved.", samples);

            // Check an ordinary answer, independently of synthetic throughput prompts.
            var answer = await SampleAsync(id, context, best.Options, thinking, 16, 3, token).ConfigureAwait(false);
            if (!Regex.IsMatch(answer.Text.Trim(), @"^4[.!]?\s*$"))
                throw new InvalidOperationException("The candidate failed the answer check (2 + 2). No override saved.");
            var finalIdentity = await IdentityAsync(id, token).ConfigureAwait(false);
            if (identity != finalIdentity || await AllocationAsync(id, context, token).ConfigureAwait(false) != allocations[best.Options])
                throw new InvalidOperationException("Model identity or GPU placement changed during measurement. Run tuning again when resources are stable.");
            var profile = new LocalPerformanceProfile(id, identity.Digest, identity.Version,
                LocalPerformanceProfile.HardwareFingerprint(hardware), context, best.Options, baseSpeed, speed,
                allocations[best.Options], DateTime.UtcNow, allocation);
            return new(profile, $"{id}: {baseSpeed:0.0} → {speed:0.0} tok/s ({profile.Improvement:P0} faster) in warm test prompts. "
                + $"{best.Options.CpuThreads} threads; draft {best.Options.DraftTokens?.ToString() ?? "model default"}; {context:N0} context. Other workloads can differ.", samples);

            async Task<LocalPerformanceSample> Measure(LocalPerformanceOptions options, int tokens, int prompt, string phase)
            {
                progress?.Report($"{phase}: {options.CpuThreads} threads · draft {options.DraftTokens?.ToString() ?? "default"} · {tokens} tokens. Cancel anytime; up to 15 minutes.");
                // Thread changes may reload the runner. Warm each configuration; don't time loading as decoding.
                await SampleAsync(id, context, options, thinking, 8, prompt, token).ConfigureAwait(false);
                var before = await AllocationAsync(id, context, token).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Ollama stopped reporting the requested context allocation.");
                // Draft length can legitimately change GPU placement. Compare repeated measurements
                // of the same configuration, not different configurations with different memory needs.
                if (allocations.TryGetValue(options, out var expected) && before != expected)
                    throw new InvalidOperationException("GPU placement changed for the same configuration. No override saved.");
                allocations[options] = before;
                var sample = await SampleAsync(id, context, options, thinking, tokens, prompt, token).ConfigureAwait(false);
                if (sample.Tokens < tokens / 2 || sample.Text.Trim().Length < 24 || !double.IsFinite(sample.TokensPerSecond) || sample.Seconds <= 0)
                    throw new InvalidOperationException("The model stopped too early for a reliable speed measurement. No override saved.");
                if (await AllocationAsync(id, context, token).ConfigureAwait(false) != before)
                    throw new InvalidOperationException("GPU placement changed during tuning. No override saved.");
                samples.Add(sample);
                progress?.Report($"{phase}: {sample.TokensPerSecond:0.0} tok/s · first text {sample.FirstTokenSeconds:0.00}s.");
                return sample;
            }
        }
    }

    internal static bool IsImprovement(IReadOnlyList<LocalPerformanceSample> baseline, IReadOnlyList<LocalPerformanceSample> candidate)
        => baseline.Count == 2 && candidate.Count == 2
        && baseline.Concat(candidate).All(s => s.Tokens >= 48 && s.Seconds > 0 && double.IsFinite(s.TokensPerSecond))
        && candidate.Zip(baseline).All(pair => pair.First.TokensPerSecond >= pair.Second.TokensPerSecond * 1.03
            && pair.First.FirstTokenSeconds <= pair.Second.FirstTokenSeconds * 1.2 + 0.1)
        && candidate.Sum(s => s.Tokens) / candidate.Sum(s => s.Seconds)
            >= 1.05 * baseline.Sum(s => s.Tokens) / baseline.Sum(s => s.Seconds);

    internal async Task<LocalPerformanceSample> SampleAsync(string id, int context, LocalPerformanceOptions options,
        bool thinking, int tokens, int prompt, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        var token = deadline.Token;
        var prompts = new[] {
            "Write a detailed explanation of how a computer uses RAM and its CPU to run a program. Use at least 400 words.",
            "Write Python code implementing merge sort, with detailed comments explaining each step. Include examples and tests.",
            "Explain in at least 400 words how a library organizes, lends, and tracks its books. Use clear paragraphs.",
            "What is 2 + 2? Reply with only the single digit answer." };
        var settings = new Dictionary<string, object> { ["num_ctx"] = context, ["num_predict"] = tokens,
            ["num_thread"] = options.CpuThreads, ["num_gpu"] = -1, ["seed"] = 42, ["temperature"] = 0 };
        if (options.DraftTokens is { } draftTokens) settings["draft_num_predict"] = draftTokens;
        var payload = new Dictionary<string, object> { ["model"] = id, ["messages"] = new[] { new { role = "user", content = prompts[prompt] } },
            ["stream"] = true, ["options"] = settings };
        if (thinking) payload["think"] = false;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "/api/chat")) { Content = JsonContent.Create(payload) };
        var clock = Stopwatch.StartNew();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false));
        var text = new StringBuilder();
        double? first = null;
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0) continue;
            using var frame = JsonDocument.Parse(line);
            var root = frame.RootElement;
            if (Text(root, "error").Length > 0) throw new InvalidOperationException("Ollama rejected the benchmark: " + Text(root, "error"));
            if (root.TryGetProperty("message", out var message))
            {
                var delta = Text(message, "content");
                if (delta.Length > 0) { first ??= clock.Elapsed.TotalSeconds; text.Append(delta); }
            }
            if (text.Length > 100_000) throw new InvalidOperationException("The benchmark exceeded its output limit.");
            if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
                return new(options, (int)(Number(root, "eval_count") ?? 0), (Number(root, "eval_duration") ?? 0) / 1e9,
                    first ?? clock.Elapsed.TotalSeconds, text.ToString());
        }
        throw new IOException("Ollama ended the benchmark without final generation statistics.");
    }

    private async Task<JsonDocument> ReadAsync(string path, CancellationToken token)
    {
        using var response = await http.GetAsync(new Uri(BaseUri, path), token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), cancellationToken: token).ConfigureAwait(false);
    }
    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static long? Number(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
}
