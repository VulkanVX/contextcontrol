using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.Services;

internal static class LocalPerformanceTests
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; }
    private static LocalLlmRequest Request(int context = 8192) => new("test:2b", "Keep my actual question", "raw", [], context, Think: true, MaxOutputTokens: 345);
    private sealed class Progress(Action<string> action) : IProgress<string> { public void Report(string value) => action(value); }
    internal static async Task Run()
    {
        var hardware = LocalResourceTests.Hardware();
        using var handler = new Fixture();
        using var client = new HttpClient(handler);
        var tuner = new LocalPerformanceTuner(client);
        var result = await tuner.TuneAsync("test:2b", 8192, hardware, null, default);
        var profile = result.Profile!;
        Check(profile is { IsValid: true } && profile.Options.CpuThreads == 6, "Select a repeatable measured CPU gain.");
        Check(profile.Options.DraftTokens is null, "Keep unsupported draft options absent.");
        Check(result.Samples.Count >= 8 && profile.TokensPerSecond > profile.BaselineTokensPerSecond * 1.05, "Repeat on two paired prompts.");
        Check(handler.Payloads.All(p => !p.Contains("keep_alive")), "Preserve server keep-alive policy.");
        foreach (var mode in new[] { "mtp", "flat", "missing", "short", "truncated", "allocation", "answer", "identity" })
        {
            using var fixture = new Fixture { Mode = mode };
            using var fixtureClient = new HttpClient(fixture);
            var rejected = false;
            LocalPerformanceResult? measured = null;
            try { measured = await new LocalPerformanceTuner(fixtureClient).TuneAsync("test:2b", 8192, hardware, null, default); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException) { rejected = true; }
            Check(mode == "mtp" ? measured?.Profile?.Options.DraftTokens == 2 : mode == "flat" ? measured is { Profile: null } : rejected,
                "Validate benchmark outcome: " + mode);
        }
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            var stopped = false;
            try { await tuner.TuneAsync("test:2b", 8192, hardware, null, canceled.Token); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "Honor pre-cancellation.");
        }
        using (LocalPerformanceActivity.Chat())
        {
            var stopped = false;
            try { await tuner.TuneAsync("test:2b", 8192, hardware, null, default); }
            catch (InvalidOperationException) { stopped = true; }
            Check(stopped, "Active chats prevent tuning.");
        }
        IDisposable? chat = null;
        try
        {
            var stopped = false;
            try { await tuner.TuneAsync("test:2b", 8192, hardware, new Progress(_ => chat ??= LocalPerformanceActivity.Chat()), default); }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "A new chat immediately cancels tuning.");
        }
        finally { chat?.Dispose(); }
        var baseline = new LocalPerformanceSample(new(5), 96, 9.6, 0.1, "text");
        var faster = baseline with { Seconds = 8 };
        Check(!LocalPerformanceTuner.IsImprovement([baseline, baseline], [faster, baseline]), "Reject one lucky round.");
        Check(!LocalPerformanceTuner.IsImprovement([baseline, baseline], [faster, faster with { FirstTokenSeconds = 2 }]), "Reject first-token regressions.");
        Check(!LocalPerformanceTuner.IsImprovement([baseline, baseline], [faster, faster with { Seconds = double.NaN }]), "Reject invalid timing.");
        Check(!LocalPerformanceTuner.IsLocalModel("runtime:llama-cpp") && !LocalPerformanceTuner.IsLocalModel("qwen:cloud"), "Exclude hosted and unmanaged runtimes.");
        foreach (var mode in new[] { "apply", "disabled", "manual", "threads", "gpu", "context", "digest", "version", "hardware", "placement", "cold" })
        {
            using var route = new Fixture { Mode = mode };
            var service = new LocalLlmService(route);
            service.ConfigureResources(new(Enabled: mode != "manual", AutoThreads: mode != "threads", AutoGpuLayers: mode != "gpu"),
                mode == "hardware" ? hardware with { CpuName = "Different CPU" } : hardware);
            service.ConfigurePerformance([profile], mode != "disabled");
            var reply = await service.SendChatAsync(Request(mode == "context" ? 4096 : 8192), null, null);
            Check(reply.Succeeded, "Real chat works: " + mode);
            using var payload = JsonDocument.Parse(route.Payloads.Last());
            var options = payload.RootElement.GetProperty("options");
            Check((options.TryGetProperty("num_thread", out var thread) && thread.GetInt32() == 6) == (mode == "apply"), "Apply only a matching profile: " + mode);
            Check(options.GetProperty("num_predict").GetInt32() == 345 && payload.RootElement.GetProperty("think").GetBoolean()
                && payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() == Request().Prompt,
                "Preserve output budget, thinking and actual prompt.");
            Check(!options.TryGetProperty("temperature", out _) && !options.TryGetProperty("seed", out _), "Never leak benchmark sampling.");
        }
        var root = Path.Combine(Path.GetTempPath(), "ContextControlSpeedTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preferences = WorkbenchSettings.Load(root);
        preferences.LocalPerformanceProfiles = [profile, profile with { Options = new(999) }];
        preferences.UseMeasuredLocalPerformance = false;
        preferences.Save();
        var restored = WorkbenchSettings.Load(root);
        Check(restored.LocalPerformanceProfiles.Count == 1 && restored.LocalPerformanceProfiles[0] == profile && !restored.UseMeasuredLocalPerformance, "Persist valid profiles and opt-out.");
        Check(LocalPerformanceProfile.HardwareFingerprint(hardware) == LocalPerformanceProfile.HardwareFingerprint(hardware with { AvailableRamBytes = 1 }), "Stable hardware identity excludes current free RAM.");
        Console.WriteLine($"PERFORMANCE_REGRESSION_PASS {_checks} checks");
    }
    internal static async Task Live(string model, string output)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var hardware = await LocalLlmService.DetectHardwareAsync(cancellation.Token);
        var result = await new LocalPerformanceTuner(http).TuneAsync(model, 8192, hardware, new Progress(Console.WriteLine), cancellation.Token);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var identity = await new LocalPerformanceTuner(http).IdentityAsync(model, cancellation.Token);
        var allocation = await new LocalPerformanceTuner(http).AllocationAsync(model, 8192, cancellation.Token);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { Model = model, ContextTokens = 8192, Hardware = hardware,
            Identity = identity, VramBytes = allocation, MeasuredUtc = DateTime.UtcNow, Result = result }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(result.Status);
        Console.WriteLine("PERFORMANCE_LIVE_PASS: " + output);
    }
    private sealed class Fixture : HttpMessageHandler
    {
        public List<string> Payloads { get; } = [];
        public string Mode { get; init; } = "";
        private int _tags;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string json;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/api/tags":
                    _tags++;
                    json = JsonSerializer.Serialize(new { models = Mode == "missing" ? Array.Empty<object>() : new object[] {
                        new { name = "test:2b", digest = Mode == "digest" || Mode == "identity" && _tags > 1 ? "changed" : "sha256:abc" } } }); break;
                case "/api/version": json = JsonSerializer.Serialize(new { version = Mode == "version" ? "0.35.0" : "0.34.0" }); break;
                case "/api/show": json = JsonSerializer.Serialize(new { capabilities = new[] { "completion", "thinking" }, parameters = Mode == "mtp" ? "draft_num_predict    4\n" : "" }); break;
                case "/api/ps":
                    json = JsonSerializer.Serialize(new { models = Mode == "cold" ? Array.Empty<object>() : new object[] {
                        new { name = "test:2b", size_vram = Mode == "placement" || Mode == "allocation" && Payloads.Count > 3 ? 1 : 1024, context_length = 8192 } } }); break;
                default:
                    var body = await request.Content!.ReadAsStringAsync(token);
                    Payloads.Add(body);
                    using (var parsed = JsonDocument.Parse(body))
                    {
                        var options = parsed.RootElement.GetProperty("options");
                        var threads = options.TryGetProperty("num_thread", out var thread) ? thread.GetInt32() : 0;
                        var tokens = options.GetProperty("num_predict").GetInt32();
                        var speed = Mode == "flat" ? 10d : threads == 6 ? 20d : threads == 12 ? 12d : 10d;
                        if (options.TryGetProperty("draft_num_predict", out var draft) && draft.GetInt32() == 2) speed *= 1.5;
                        var prompt = parsed.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
                        var text = prompt.Contains("2 + 2") ? Mode == "answer" ? "5" : "4" : "A sufficiently long, readable answer for the benchmark or real chat.";
                        json = JsonSerializer.Serialize(new { message = new { content = text }, done = false }) + "\n";
                        if (Mode != "truncated") json += JsonSerializer.Serialize(new { done = true, done_reason = prompt == Request().Prompt ? "stop" : "length",
                            eval_count = Mode == "short" ? 2 : tokens, eval_duration = (long)(tokens / speed * 1e9) }) + "\n";
                    }
                    break;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
