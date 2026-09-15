using System.Net;
using System.Net.Http;
using System.Text.Json;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

internal static partial class LocalResourceTests
{
    private static async Task ResourceStatusAndCatalog()
    {
        foreach (var size in new[] { "~4.4 GB Q4", "~14 GB Q4_K_M", "4.4 GB (Q4)", "3–7 GB", "3 GB - 7 GB", "~335 MB Q4", "1 TB BF16",
            "~1.19 GB I2_S", "~14 GB MXFP4", "6.6GB published download" })
            Check(LocalResourcePlanner.ParseWeightGiB(size) is > 0, "Recognize catalog size: " + size);
        Check(LocalResourcePlanner.ParseWeightGiB("3-7 GB") == LocalResourcePlanner.ParseWeightGiB("7 GB"), "Ranges use upper bound.");
        Check(LocalResourcePlanner.ParseWeightGiB("2 GiB - 100 MB") == 2, "Mixed-unit ranges use largest end.");
        var auto = new LocalResourceSettings(Enabled: true, MaxContextTokens: 20480, RamReserveGiB: 16);
        var hardware = Hardware();
        var catalog = LocalLlmService.Catalog.Select(m => new LocalLlmModelViewModel(m)).ToArray();
        foreach (var m in catalog) { m.ApplyState(false, false, hardware); m.ConfigureResources(auto); }
        var unknown = catalog.Where(m => m.SupportsResourceAdaptation && m.AdaptedFitPlan?.Label == "Size unknown").ToArray();
        Console.WriteLine($"CATALOG: {catalog.Length} rows; {catalog.Count(m => m.SupportsResourceAdaptation)} adaptive routes; "
            + $"{catalog.Count(m => m.CanRunOnDetectedHardware)} fits; {unknown.Length} unknown sizes.");
        foreach (var group in unknown.GroupBy(m => m.DownloadSize)) Console.WriteLine($"UNKNOWN SIZE: {group.Key} ({group.Count()})");
        Check(unknown.Length == 0, "Every bundled adaptive catalog size must be recognized.");
        foreach (var id in new[] { "hf.co/QuantFactory/SeaLLMs-v3-7B-Chat-GGUF:Q4_K_M", "hf.co/MaziyarPanahi/solar-pro-preview-instruct-GGUF:Q4_K_M" })
        {
            var model = catalog.Single(m => m.Id == id);
            Check(model.CanRunOnDetectedHardware && model.AdaptedFitPlan!.Label == "CPU + GPU", "Real catalog offload entry survives filter: " + id);
            Check(model.FitsOnCpuWithAdaptation, "A GPU-assisted fit can also fit on CPU alone: " + id);
        }
        Check(!catalog.Single(m => m.Id == "hf.co/bartowski/zai-org_GLM-4.5-Air-GGUF:Q4_K_M").CanRunOnDetectedHardware,
            "Real oversized catalog model remains excluded with 16 GiB headroom.");
        var filter = typeof(ContextControlViewModel).GetMethod("MatchesRequirementFilter", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        foreach (var m in catalog.Where(m => m.SupportsResourceAdaptation))
        {
            var adaptedMatch = (bool)filter.Invoke(null, [m, "Adapted CPU / RAM"])!;
            m.ConfigureResources(auto with { Enabled = false });
            Check(adaptedMatch == (bool)filter.Invoke(null, [m, "Adapted CPU / RAM"])!, "Explicit adapted filters work independently of run mode: " + m.Id);
        }
        var memory = new LocalModelMemory(2, 40, 81920, 131072);
        var maximum = LocalResourcePlanner.EstimateMaximumContext(memory, hardware, auto, 1048576);
        Check(maximum.Tokens > auto.MaxContextTokens && maximum.Tokens <= 131072, "Capacity is independent of Auto target and respects model limit.");
        Check(LocalResourcePlanner.EstimateMaximumContext(memory, hardware, auto with { MaxContextTokens = 1024 }, 1048576).Tokens == maximum.Tokens,
            "Auto cap changes do not change hardware capacity.");
        Check(LocalResourcePlanner.EstimateMaximumContext(memory, Hardware(freeRam: 19), auto, 1048576).Tokens < maximum.Tokens,
            "Free RAM pressure reduces supportable context.");
        Check(LocalResourcePlanner.EstimateMaximumContext(memory, hardware, auto, 32768).Tokens <= 32768, "Managed runtime ceiling is respected.");
        Check(LocalResourcePlanner.EstimateMaximumContext(new(null), hardware, auto).Tokens is null, "Unknown weights have unknown capacity.");

        using var json = JsonDocument.Parse("""{"models":[{"name":"sample:latest","size":10737418240,"size_vram":2147483648,"context_length":8192}]}""");
        var allocation = LocalLlmService.ParseOllamaAllocation(json.RootElement, "sample");
        Check(allocation.Running && allocation.ContextTokens == 8192 && allocation.VramBytes == GiB(2), "Read actual loaded context and allocation including latest alias.");
        var credited = LocalResourcePlanner.CreditLoadedAllocation(Hardware(freeRam: 10, freeGpu: 1), allocation);
        Check(credited.AvailableRamGiB == 18 && credited.Gpus[0].AvailableMemoryGiB == 3, "Credit only candidate's existing CPU/VRAM allocation.");
        Check(LocalResourcePlanner.CreditLoadedAllocation(hardware, allocation with { TotalBytes = long.MaxValue, VramBytes = GiB(200) }).AvailableRamGiB <= 64,
            "Credit cannot exceed installed RAM.");
        Check(LocalResourcePlanner.CreditLoadedAllocation(hardware, allocation with { Running = false }) == hardware, "Unloaded model gets no credit.");
        Check(LocalResourcePlanner.CreditLoadedAllocation(hardware, allocation with { TotalBytes = null }).AvailableRamBytes == hardware.AvailableRamBytes,
            "Unreported total must not invent host allocation.");
        var busy = Hardware(freeRam: 10, freeGpu: 1);
        var loadedModel = new LocalLlmModelViewModel(LocalLlmService.Catalog.First(m => m.Id == "granite3.3:2b") with { DownloadSize = "10 GiB" });
        loadedModel.ApplyState(true, true, busy);
        loadedModel.ConfigureResources(auto with { RamReserveGiB = 4 });
        Check(!loadedModel.CanRunOnDetectedHardware, "Busy machine cannot fit an additional large model.");
        loadedModel.UpdateResourceHardware(busy, allocation);
        Check(loadedModel.CanRunOnDetectedHardware, "Same already-loaded model is not counted twice.");
        loadedModel.UpdateResourceHardware(busy);
        Check(!loadedModel.CanRunOnDetectedHardware, "Stale allocation credit is removed on next reading.");
        foreach (var payload in new[] { "null", "[]", "{}", """{"models":[null,{"name":"sample:latest","size":"bad","size_vram":null,"context_length":-1}]}""" })
        {
            using var malformed = JsonDocument.Parse(payload);
            var reading = LocalLlmService.ParseOllamaAllocation(malformed.RootElement, "sample");
            Check(reading.TotalBytes is null && reading.ContextTokens is null, "Malformed readings remain unreported without crashing.");
        }
        using var info = JsonDocument.Parse("""{"general.architecture":"granite","granite.block_count":40,"granite.context_length":131072,"granite.embedding_length":2048,"granite.attention.head_count":32,"granite.attention.head_count_kv":8}""");
        Check(LocalLlmService.ReadOllamaMemory(info.RootElement, 2).KvBytesPerToken == 81920, "Derive f16 KV capacity from actual architecture metadata.");
        using var handler = new ResourceHandler();
        var service = new LocalLlmService(handler);
        var report = await service.ReadOllamaResourcesAsync("sample", 2, CancellationToken.None);
        Check(report.Allocation.Running && report.Loaded?.Count == 1 && report.Memory?.MaxContext == 131072, "Poll transport returns current allocations and architecture metadata.");
        await service.ReadOllamaResourcesAsync("sample", 2, CancellationToken.None);
        Check(handler.Shows == 1 && handler.Reads == 2, "Cache metadata, but refresh live allocation each time.");
    }

    private sealed class ResourceHandler : HttpMessageHandler
    {
        internal int Reads, Shows;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string content;
            if (request.RequestUri!.AbsolutePath == "/api/ps") { Reads++; content = """{"models":[{"name":"sample:latest","size":100000,"size_vram":50000,"context_length":8192}]}"""; }
            else { Shows++; content = """{"model_info":{"general.architecture":"test","test.context_length":131072}}"""; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
}
