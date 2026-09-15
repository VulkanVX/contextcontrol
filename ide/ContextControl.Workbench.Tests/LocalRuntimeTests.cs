using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static class LocalRuntimeTests
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static LocalLlmRequest Request(string id, bool? think = false) => new(id, "Say hello in one short sentence.", "raw", [], 4096, Think: think, MaxOutputTokens: 128);
    private static string Event(string content, string? finish = null) => "data: " + JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content }, finish_reason = finish } } }) + "\n\n";

    internal static async Task Run()
    {
        Check(new LocalRuntimeProfile("a", "A", "http://localhost:1234").ApiUri("models").AbsolutePath == "/v1/models", "Normalize API base URLs");
        Check(new LocalRuntimeProfile("a", "A", "https://localhost/base/v1/").ApiUri("models").AbsolutePath == "/base/v1/models", "Keep an existing v1 path");
        foreach (var bad in new[] { "file:///test", "ftp://host", "https://user:pass@host", "https://host?key=secret", "" })
        {
            try { new LocalRuntimeProfile("a", "A", bad).ApiUri("models"); throw new Exception("Accepted bad URL"); }
            catch (InvalidOperationException) { _checks++; }
        }
        using var handler = new Handler(Event("Hello ") + Event("from the runtime.", "stop") + "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":8}}\n\ndata: [DONE]\n\n");
        var service = new LocalLlmService(handler);
        service.ConfigureRuntimes([new("test", "Test", "http://127.0.0.1:18111/v1"), new("disabled", "Disabled", "http://127.0.0.1:18112", false)]);
        var models = await service.DiscoverRuntimeModelsAsync(default);
        Check(models.Count == 1 && models[0].BackendModelId == "model/one", "Discover and namespace model IDs");
        Check(service.RuntimeStatuses.Count == 2 && !service.RuntimeStatuses[1].Reachable, "Do not call disabled servers");
        var events = new List<LocalLlmGenerationProgress>();
        var answer = await service.SendChatAsync(Request(models[0].Id), new ProgressNow<LocalLlmGenerationProgress>(events.Add), null);
        Check(answer.Succeeded && answer.Message == "Hello from the runtime.", "Accumulate streamed answer");
        Check(answer.Stats?.PromptTokens == 12 && answer.Stats.OutputTokens == 8, "Keep usage after finish chunk");
        Check(events.Count(e => !string.IsNullOrEmpty(e.Delta)) == 2 && events.Last().Done, "Forward deltas and valid completion");
        using (var request = JsonDocument.Parse(handler.Bodies.Last()))
        {
            Check(request.RootElement.GetProperty("model").GetString() == "model/one", "Send actual backend ID");
            Check(!request.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean(), "Explicit thinking-off survives compatible transport");
            Check(request.RootElement.GetProperty("max_tokens").GetInt32() == 128, "Respect output budget");
        }
        service.ConfigureRuntimes([new("test", "Test", "http://127.0.0.1:18111/v1", ContextTokens: 16384)]);
        await service.DiscoverRuntimeModelsAsync(default);
        await service.SendChatAsync(Request(models[0].Id) with { ContextWindowTokens = 8192, MaxOutputTokens = null }, null, null);
        using (var request = JsonDocument.Parse(handler.Bodies.Last()))
            Check(request.RootElement.GetProperty("max_tokens").GetInt32() is > 4096 and < 8192, "Compatible output uses available request space without the old hidden 4K cutoff.");
        foreach (var data in new[] { "", Event("incomplete"), "data: broken\n\n", "data: {\"error\":{\"message\":\"OOM\"}}\n\n", Event("", "stop"), Event("<THINK>no answer</THINK>", "stop") })
        {
            handler.Response = data;
            var failed = await service.SendChatAsync(Request(models[0].Id), null, null);
            Check(!failed.Succeeded, "Reject incomplete, malformed, empty, error and reasoning-only responses");
        }
        handler.Response = Event("Partial reply", "length");
        var partial = await service.SendChatAsync(Request(models[0].Id), null, null);
        Check(!partial.Succeeded && partial.OutputLimited && partial.Message!.Contains("Partial reply") && partial.Message.Contains("Response incomplete"), "Preserve truncated text with a typed limit and visible notice");
        handler.Response = "data: {\"choices\":[{\"delta\":{\"content\":[{\"type\":\"text\",\"text\":\"Array text\"}]},\"finish_reason\":\"stop\"}]}\n\n";
        Check((await service.SendChatAsync(Request(models[0].Id), null, null)).Message == "Array text", "Support text content arrays");
        handler.Response = "{\"choices\":[{\"message\":{\"content\":\"JSON reply\"},\"finish_reason\":\"stop\"}]}";
        handler.ContentType = "application/json";
        Check((await service.SendChatAsync(Request(models[0].Id), null, null)).Message == "JSON reply", "Accept compatible non-stream JSON fallback");
        handler.StatusCode = HttpStatusCode.Unauthorized;
        Check(!(await service.SendChatAsync(Request(models[0].Id), null, null)).Succeeded, "Surface HTTP failure");
        handler.StatusCode = HttpStatusCode.OK;
        handler.Unreachable = true;
        Check((await service.DiscoverRuntimeModelsAsync(default)).Count == 0, "Offline server has no available models");
        Check(!(await service.SendChatAsync(Request(models[0].Id), null, null)).Succeeded, "Do not route stale runtime IDs to Ollama");
        Check(LocalLlmService.ImageMimeType("/9j/test") == "image/jpeg" && LocalLlmService.ImageMimeType("iVBORtest") == "image/png"
            && LocalLlmService.ImageMimeType("UklGtest") == "image/webp", "Use actual image media types");
        var profile = new LocalRuntimeProfile("llama-cpp", "llama.cpp", "http://127.0.0.1:8080/v1", ModelPath: "a path", GpuLayers: 12);
        var restored = JsonSerializer.Deserialize<LocalRuntimeProfile>(JsonSerializer.Serialize(profile));
        Check(restored == profile, "Persist runtime connection and model configuration");
        var vm = new LocalLlmModelViewModel(models[0]);
        Check(vm.IsConnectedRuntime && !vm.UsesOllamaPull && !vm.CanUninstall, "Runtime catalog must never remove weights through Ollama");
        Check(LocalLlmService.Catalog.Select(m => m.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == LocalLlmService.Catalog.Count, "Catalog IDs are unique");
        Check(ContextCapsuleBuilder.EstimateContextTokens("8192") == 8192, "Honor an exact server context budget instead of falling back to 4K");
        handler.Unreachable = false;
        handler.ContentType = "text/event-stream";
        await service.DiscoverRuntimeModelsAsync(default);
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            handler.ResponseStream = new HeldStream(Event("A live chunk"));
            var cancelled = await service.SendChatAsync(Request(models[0].Id), new ProgressNow<LocalLlmGenerationProgress>(p => { if (p.Delta is not null) stop.Cancel(); }), null, stop.Token);
            Check(!cancelled.Succeeded && cancelled.Status.Contains("stopped by the user"), "Stop must cancel an open compatible response stream");
        }
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            handler.ResponseStream = new HeldStream(Event("Done", "stop") + "data: [DONE]\n\n");
            var done = await service.SendChatAsync(Request(models[0].Id), null, null, stop.Token);
            Check(done.Succeeded, "A DONE sentinel must finish even when the server keeps its connection open");
        }
        handler.ResponseStream = null;
        var keyVariable = "CC_RUNTIME_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(keyVariable, "test-only-key");
        try
        {
            service.ConfigureRuntimes([new("test", "Test", "http://127.0.0.1:18111/v1", ApiKeyEnvironmentVariable: keyVariable)]);
            await service.DiscoverRuntimeModelsAsync(default);
            Check(handler.Authorization == "Bearer test-only-key", "Resolve server credentials from the configured environment variable");
        }
        finally { Environment.SetEnvironmentVariable(keyVariable, null); }
        Check((await service.DiscoverRuntimeModelsAsync(default)).Count == 0, "A missing credential variable must not make unauthenticated requests");
        Console.WriteLine($"Runtime regression passed: {_checks} checks; catalog {LocalLlmService.Catalog.Count} models.");
    }

    internal static async Task Install(string dependency)
    {
        if (!NativeDependencyEnvironment.TryGetSpec(dependency, out var spec)) throw new Exception("Unknown native runtime");
        var result = await NativeDependencyEnvironment.InstallLatestReleaseAsync(spec, new ProgressNow<string>(Console.WriteLine),
            new ProgressNow<LocalLlmTransferProgress>(_ => { }));
        Console.WriteLine(JsonSerializer.Serialize(result));
        Check(result.Succeeded && File.Exists(result.ExecutablePath), "Native runtime installer did not produce an executable");
    }

    internal static async Task Live(string runtime, string modelPath)
    {
        var profile = LocalRuntimeProfile.Defaults.Single(p => p.Id == runtime) with { Enabled = true, ModelPath = modelPath };
        using var managed = new ManagedLocalRuntimeService();
        try
        {
            if (runtime != "lm-studio") Console.WriteLine(await managed.StartAsync(profile, new ProgressNow<string>(Console.WriteLine), default));
            var service = new LocalLlmService();
            service.ConfigureRuntimes([profile]);
            var models = await service.DiscoverRuntimeModelsAsync(default);
            Check(models.Count > 0, "Runtime model discovery failed");
            var deltas = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await service.SendChatAsync(Request(models[0].Id), new ProgressNow<LocalLlmGenerationProgress>(p => { if (!string.IsNullOrEmpty(p.Delta)) deltas++; }), null);
            Console.WriteLine(JsonSerializer.Serialize(new { runtime, model = models[0].BackendModelId, seconds = watch.Elapsed.TotalSeconds, deltas, result }));
            Check(result.Succeeded && !string.IsNullOrWhiteSpace(result.Message) && deltas > 0, "No visible streamed runtime answer");
            Check(result.Stats?.PromptTokens > 0 && result.Stats.OutputTokens > 0, "Runtime usage missing");
        }
        finally { managed.Stop(runtime); }
    }

    private sealed class ProgressNow<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Response = response;
        public string ContentType = "text/event-stream";
        public HttpStatusCode StatusCode = HttpStatusCode.OK;
        public bool Unreachable;
        public Stream? ResponseStream;
        public string? Authorization;
        public List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Unreachable) throw new HttpRequestException("Offline");
            Authorization = request.Headers.Authorization?.ToString();
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = new StringContent("{\"data\":[{\"id\":\"model/one\"},{\"id\":\"model/one\"}]}") };
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var content = ResponseStream is null ? (HttpContent)new StringContent(Response, Encoding.UTF8, ContentType) : new StreamContent(ResponseStream);
            content.Headers.ContentType = new(ContentType);
            return new(StatusCode) { Content = content };
        }
    }
    private sealed class HeldStream(string first) : MemoryStream(Encoding.UTF8.GetBytes(first))
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position < Length) return await base.ReadAsync(buffer, cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
