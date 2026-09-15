using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class LocalResourceTests
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private static long GiB(double n) => (long)(n * 1073741824d);
    internal static LocalLlmHardwareProfile Hardware(double freeRam = 42, double gpu = 4, double freeGpu = 3)
        => new(gpu > 0 ? [new("Test GPU", GiB(gpu), GiB(freeGpu))] : [], GiB(64), GiB(freeRam), "Six core CPU", 6, 12);

    internal static async Task Run()
    {
        var auto = new LocalResourceSettings(Enabled: true);
        var hardware = Hardware();
        var large = new LocalModelMemory(10, 40, 262144, 32768);
        var plan = LocalResourcePlanner.Plan(large, hardware, auto);
        Check(plan.Fits && plan.Label == "CPU + GPU" && plan.GpuLayers > 0 && plan.GpuLayers < 40, "A 10 GiB model should use RAM plus partial offload on a 4 GiB GPU.");
        Check(plan.RamGiB <= 38 && plan.VramGiB <= 2.25 && plan.CpuThreads == 5, "Honor free memory, reserves and leave one physical core available.");
        var cpu = LocalResourcePlanner.Plan(large, Hardware(gpu: 0), auto);
        Check(cpu.Fits && cpu.GpuLayers == 0 && cpu.Label == "CPU / RAM", "CPU-only hardware is a valid model host.");
        var full = LocalResourcePlanner.Plan(new(1, 24, 32768), Hardware(gpu: 8, freeGpu: 7), auto);
        Check(full.Fits && full.GpuLayers == 25 && full.Label == "GPU fit", "Small model should fit all layers on a roomy GPU.");
        var low = LocalResourcePlanner.Plan(new(2, 24, 524288), Hardware(freeRam: 9, gpu: 0), auto);
        Check(low.Fits && low.ContextTokens < 8192 && low.ContextTokens >= 1024, "Reduce context when KV cache leaves insufficient RAM.");
        var impossible = LocalResourcePlanner.Plan(new(50, 60), hardware, auto);
        Check(!impossible.Fits && impossible.ContextTokens == 1024 && impossible.Label == "Memory short", "Do not promise oversized models will fit.");
        Check(!LocalResourcePlanner.Plan(large, new([]), auto).Fits, "Unknown free RAM must not become a positive fit.");
        Check(!LocalResourcePlanner.Plan(new(null), hardware, auto).Fits, "Unknown weights must remain unknown.");
        Check(LocalResourcePlanner.Plan(new(null), hardware, auto).ContextTokens == 2048, "Unknown metadata uses bounded context.");
        Check(LocalResourcePlanner.Plan(large, hardware, auto with { AutoContext = false }, manualContext: 4096).ContextTokens == 4096, "Context opt-out preserves manual context.");
        Check(LocalResourcePlanner.Plan(large, hardware, auto with { AutoGpuLayers = false }, manualGpuLayers: 3).GpuLayers == 3, "GPU opt-out preserves manual layers.");
        Check(LocalResourcePlanner.Plan(large, hardware, auto with { AutoThreads = false }, manualThreads: 2).CpuThreads == 2, "Thread opt-out preserves manual threads.");
        var manual = LocalResourcePlanner.Plan(large, hardware, auto with { Enabled = false }, 3072, 7, 3);
        Check(manual.ContextTokens == 3072 && manual.GpuLayers == 7 && manual.CpuThreads == 3, "Master switch off preserves all manual values.");
        Check(LocalResourcePlanner.Plan(large with { MaxContext = 4096 }, hardware, auto).ContextTokens <= 4096, "Respect trained context maximum.");
        Check(LocalResourcePlanner.Plan(large, Hardware(freeGpu: 0.1), auto).GpuLayers == 0, "A busy GPU should fall back to CPU.");
        Check(!LocalResourcePlanner.Plan(large with { CpuSupported = false }, hardware, auto).Fits, "Do not invent CPU support for GPU-only runtimes.");
        Check(LocalResourcePlanner.Plan(large with { Layers = null }, hardware, auto).GpuLayers == 0, "Missing layer count must not invent launch layers.");
        foreach (var bad in new[] { "", "Unknown", "1 GB / 40 GB", "Runtime managed", "0 GB", "1 GB MB", "3", "5 GB + projector" })
            Check(LocalResourcePlanner.ParseWeightGiB(bad) is null, "Ambiguous size must not produce a positive fit.");
        Check(Math.Abs(LocalResourcePlanner.ParseWeightGiB("1024 MiB")!.Value - 1) < 0.0001, "Binary units parse correctly.");
        Check(Math.Abs(LocalResourcePlanner.ParseWeightGiB("1 GB")!.Value - 0.9313226) < 0.0001, "Decimal GB differs from GiB.");
        var normalized = new LocalResourceSettings(MaxContextTokens: int.MaxValue, RamReserveGiB: double.NaN, VramReserveGiB: -8).Normalize();
        Check(normalized.MaxContextTokens == 32768 && normalized.RamReserveGiB == 4 && normalized.VramReserveGiB == 0.25, "Clamp invalid settings safely.");

        var root = Path.Combine(Path.GetTempPath(), "ContextControlResourceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "fixture.gguf");
        WriteGguf(path);
        var metadata = GgufResourceMetadata.Read(path);
        Check(metadata.Layers == 32 && metadata.MaxContext == 16384 && metadata.KvBytesPerToken == 131072, "Read GGUF architecture and KV layout without weight loading.");
        var profile = LocalRuntimeProfile.Defaults[0] with { ModelPath = path, GpuLayers = 6, ContextTokens = 4096, CpuThreads = 2 };
        var adapted = ManagedLocalRuntimeService.AdaptProfile(profile, auto, hardware);
        Check(adapted.Profile.CpuThreads == 5 && adapted.Profile.ContextTokens == 8192, "Managed allocation uses hardware and metadata.");
        Check(profile.CpuThreads == 2 && profile.GpuLayers == 6 && profile.ContextTokens == 4096, "Effective allocation must not mutate saved settings.");
        Check(ManagedLocalRuntimeService.AdaptProfile(profile with { AdaptToHardware = false }, auto, hardware).Profile.CpuThreads == 2, "Per-runtime opt-out takes precedence.");
        var tf = ManagedLocalRuntimeService.AdaptProfile(profile with { Id = "transformers", ModelPath = "not-downloaded" }, auto, hardware);
        Check(tf.Profile.ContextTokens == 2048 && tf.Profile.CpuThreads == 5 && !File.Exists(Path.Combine(root, "not-downloaded")), "Transformers preview stays on CPU and never downloads.");
        var active = profile with { ContextTokens = 8192 };
        Check(ManagedLocalRuntimeService.ConnectionProfile(profile, active).ContextTokens == 8192, "Connection budgets track the actual running context.");
        Check(!ManagedLocalRuntimeService.ConnectionProfile(profile with { Enabled = false }, active).Enabled, "Disabling a connection must stay disabled even while its process runs.");
        Check(ManagedLocalRuntimeService.ConnectionProfile(profile with { Endpoint = "http://127.0.0.1:9999/v1" }, active).ContextTokens == 4096, "A new endpoint must not inherit another server's active context.");
        var external = profile with { Id = "custom" };
        Check(ManagedLocalRuntimeService.AdaptProfile(external, auto, hardware).Profile == external, "External server allocation is not rewritten.");
        File.WriteAllBytes(path, "GGUF"u8.ToArray());
        try { GgufResourceMetadata.Read(path); throw new Exception("Accepted a truncated file."); }
        catch (EndOfStreamException) { _checks++; }
        var settings = WorkbenchSettings.Load(root);
        settings.LocalResources = auto with { MaxContextTokens = 4096, AutoThreads = false };
        settings.LocalRuntimeProfiles = [profile];
        settings.Save();
        var restored = WorkbenchSettings.Load(root);
        Check(restored.LocalResources == settings.LocalResources && restored.LocalRuntimeProfiles.Single() == profile, "Save/reload preserves master, individual toggles and manual runtime values.");
        Check(WorkbenchSettings.Load(Path.Combine(root, "old-install")).LocalResources.Enabled == false, "Existing installations keep manual behavior until enabled.");

        var model = new LocalLlmModelViewModel(LocalLlmService.Catalog.First(m => m.Id == "granite3.3:2b") with { DownloadSize = "10 GiB", MinimumVramGiB = 10, RecommendedVramGiB = 12, WorksOnCpu = false });
        model.ApplyState(false, false, hardware);
        Check(!model.CanRunOnDetectedHardware, "Legacy VRAM requirements fail this 4 GiB GPU fixture.");
        model.ConfigureResources(auto);
        Check(model.CanRunOnDetectedHardware && model.FitLabel == "CPU + GPU", "Adapted catalog filter includes a RAM-capable larger Ollama model.");
        var filter = typeof(ContextControlViewModel).GetMethod("MatchesRequirementFilter", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Check((bool)filter.Invoke(null, [model, "4 GB VRAM or less"])!, "VRAM threshold filters must use the adapted allocation, not the original 12 GiB recommendation.");
        Check((bool)filter.Invoke(null, [model, "Adapted CPU / RAM"])!, "Offload filter includes the adapted CPU/GPU split.");
        model.ConfigureResources(auto with { Enabled = false });
        Check(!model.CanRunOnDetectedHardware && model.ResourcePlan is null, "Toggle off restores original filter and labels.");
        model.ConfigureResources(auto);
        model.ApplyState(false, false, Hardware(freeRam: 5, freeGpu: 0));
        Check(!model.CanRunOnDetectedHardware, "Resource pressure should remove an impossible adapted fit.");

        using var handler = new Handler();
        var service = new LocalLlmService(handler);
        service.ConfigureResources(auto, hardware);
        var request = new LocalLlmRequest("granite3.3:2b", "Reply hello.", "test", [], 4096, MaxOutputTokens: 32);
        Check((await service.SendChatAsync(request, null, null)).Succeeded, "Adapted Ollama request completes.");
        using (var sent = JsonDocument.Parse(handler.Body))
        {
            var options = sent.RootElement.GetProperty("options");
            Check(options.GetProperty("num_thread").GetInt32() == 5 && options.GetProperty("num_gpu").GetInt32() == -1, "Send thread budget and native dynamic GPU allocation.");
            Check(options.GetProperty("num_ctx").GetInt32() == 4096 && options.GetProperty("num_predict").GetInt32() == 32, "Never shrink an already prepared prompt's context or output budget in transport.");
        }
        service.ConfigureResources(auto with { Enabled = false }, hardware);
        await service.SendChatAsync(request, null, null);
        using (var sent = JsonDocument.Parse(handler.Body))
            Check(!sent.RootElement.GetProperty("options").TryGetProperty("num_thread", out _) && !sent.RootElement.GetProperty("options").TryGetProperty("num_gpu", out _), "Turning Auto off removes resource overrides from Ollama.");
        service.ConfigureResources(auto, hardware);
        await service.SendChatAsync(request with { ModelId = "model:cloud" }, null, null);
        using (var sent = JsonDocument.Parse(handler.Body))
            Check(!sent.RootElement.GetProperty("options").TryGetProperty("num_thread", out _), "Cloud models do not inherit local CPU limits.");
        await ResourceStatusAndCatalog();
        Console.WriteLine($"Resource adaptation regression passed: {_checks} checks.");
    }

    internal static async Task Live(string runtime, string modelPath)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = deadline.Token;
        var hardware = await LocalLlmService.DetectHardwareAsync(token);
        var settings = new LocalResourceSettings(Enabled: true);
        Console.WriteLine(hardware.Summary);
        using var managed = new ManagedLocalRuntimeService();
        var service = new LocalLlmService();
        service.ConfigureResources(settings, hardware);
        string modelId;
        var context = 4096;
        if (runtime == "ollama") modelId = modelPath;
        else
        {
            var profile = LocalRuntimeProfile.Defaults.Single(p => p.Id == runtime) with
            { ModelPath = modelPath, Endpoint = "http://127.0.0.1:18984/v1", Enabled = true };
            var status = await managed.StartAsync(profile, new ProgressNow<string>(Console.WriteLine), token, settings);
            Console.WriteLine(status);
            Check(managed.Owns(runtime), "Adapted managed runtime did not start.");
            Check(managed.ActiveProfile!.CpuThreads == LocalResourcePlanner.Threads(hardware), "Running profile uses the selected CPU budget.");
            context = managed.ActiveProfile.ContextTokens;
            Console.WriteLine(JsonSerializer.Serialize(managed.LastPlan));
            service.ConfigureRuntimes([managed.ActiveProfile]);
            var models = await service.DiscoverRuntimeModelsAsync(token);
            Check(models.Count == 1, "Discover exactly one model from the isolated managed server.");
            modelId = models[0].Id;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var prompt = runtime == "transformers" ? "Repeat the following word and nothing else: pineapple." : "What is two plus two? Give only the number.";
        var reply = await service.SendChatAsync(new LocalLlmRequest(modelId, prompt, "test", [], context, Think: false, MaxOutputTokens: 64),
            null, new ProgressNow<string>(Console.WriteLine), token);
        Console.WriteLine(JsonSerializer.Serialize(new { runtime, reply.Succeeded, reply.Message, reply.Status, reply.Stats, Seconds = watch.Elapsed.TotalSeconds }));
        Check(reply.Succeeded && (runtime == "transformers" ? reply.Message?.Contains("pineapple", StringComparison.OrdinalIgnoreCase) == true : reply.Message?.Contains('4') == true), "Live adapted model must return the correct answer, not merely load.");
        if (runtime == "ollama")
        {
            var reported = await service.ReadOllamaResourcesAsync(modelId, 1.44, token);
            Console.WriteLine(JsonSerializer.Serialize(reported));
            Check(reported.Allocation.Running && reported.Allocation.TotalBytes is > 0
                && reported.Allocation.ContextTokens == context, "Runtime report matches actual loaded context and memory.");
        }
        else
        {
            var reported = managed.ReadAllocation(runtime);
            Console.WriteLine(JsonSerializer.Serialize(reported));
            Check(reported.Running && reported.ResidentRamBytes is > 0 && reported.ContextTokens == context,
                "Managed resource report matches actual process and loaded context.");
        }
        Console.WriteLine("LIVE RESOURCE PASS: " + runtime);
    }

    internal static async Task Benchmark(string modelPath)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        foreach (var enabled in new[] { false, true })
        {
            using var managed = new ManagedLocalRuntimeService();
            var profile = LocalRuntimeProfile.Defaults[0] with { ModelPath = modelPath, Endpoint = "http://127.0.0.1:18985/v1" };
            var status = await managed.StartAsync(profile, new ProgressNow<string>(Console.WriteLine), deadline.Token, new(Enabled: enabled));
            Check(managed.Owns(profile.Id), status);
            var service = new LocalLlmService(); service.ConfigureRuntimes([managed.ActiveProfile!]);
            var models = await service.DiscoverRuntimeModelsAsync(deadline.Token);
            var request = new LocalLlmRequest(models.Single().Id, "What is two plus two?", "benchmark", [], managed.ActiveProfile!.ContextTokens, Think: false, MaxOutputTokens: 64);
            await service.SendChatAsync(request, null, null, deadline.Token); // warm shader/model paths before timing
            var prompt = "List ten practical benefits of reading books. Explain each in one sentence. Start immediately with the list.";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await service.SendChatAsync(request with { Prompt = prompt }, null, null, deadline.Token);
            var seconds = watch.Elapsed.TotalSeconds;
            Check(result.Stats?.OutputTokens is > 0 && !string.IsNullOrWhiteSpace(result.Message), "Benchmark must generate output.");
            Console.WriteLine(JsonSerializer.Serialize(new { Auto = enabled, managed.ActiveProfile.ContextTokens, managed.ActiveProfile.GpuLayers,
                managed.ActiveProfile.CpuThreads, result.Stats!.OutputTokens, Seconds = seconds, EndToEndTokensPerSecond = result.Stats!.OutputTokens / seconds }));
        }
    }

    private static void WriteGguf(string path)
    {
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(0ul); writer.Write(8ul);
        void Text(string s) { var bytes = Encoding.UTF8.GetBytes(s); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
        void Num(string key, uint n) { Text(key); writer.Write(4u); writer.Write(n); }
        Text("general.architecture"); writer.Write(8u); Text("llama");
        Num("llama.block_count", 32); Num("llama.context_length", 16384); Num("llama.embedding_length", 4096);
        Num("llama.attention.head_count", 32); Num("llama.attention.head_count_kv", 8);
        Text("tokenizer.ggml.tokens"); writer.Write(9u); writer.Write(8u); writer.Write(2ul); Text("a"); Text("b");
        Text("tokenizer.ggml.scores"); writer.Write(9u); writer.Write(6u); writer.Write(2ul); writer.Write(0f); writer.Write(1f);
    }
    private sealed class ProgressNow<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class Handler : HttpMessageHandler
    {
        internal string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"Hello\"},\"done\":true,\"done_reason\":\"stop\"}\n", Encoding.UTF8, "application/x-ndjson") };
        }
    }
}
