using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ContextControl.Workbench.Services;

internal static class LocalLlmChatTests
{
    private static int _checks;
    private const string Answer = "{\"message\":{\"content\":\"Pizza answer [1].\"},\"done\":false}\n";
    private const string Thinking = "{\"message\":{\"thinking\":\"Review the evidence.\"},\"done\":false}\n";
    private const string Done = "{\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":2200,\"eval_count\":120,\"total_duration\":3000000000,\"load_duration\":100000000,\"prompt_eval_duration\":500000000,\"eval_duration\":2400000000}\n";
    private static LocalLlmRequest Request(bool? think = false) => new("qwen3.5:4b-q4_K_M", "Search for pizza places in Vilnius.", "raw", [], 4096, Think: think, MaxOutputTokens: 512);

    internal static async Task Run()
    {
        using var handler = new ReplyHandler(_ => new StringContent(Answer + Done));
        var service = new LocalLlmService(handler);
        var result = await service.SendChatAsync(Request(), null, null);
        Check(result.Succeeded && result.Message == "Pizza answer [1].", "A completed visible answer must succeed.");
        using (var body = JsonDocument.Parse(handler.Bodies[0]))
        {
            Check(!body.RootElement.GetProperty("think").GetBoolean(), "Thinking off must serialize as false, not disappear.");
            Check(body.RootElement.GetProperty("options").GetProperty("num_ctx").GetInt32() == 4096, "Keep the requested context.");
            Check(body.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32() == 512, "Keep the requested output limit.");
        }
        Check(result.Stats?.PromptTokens == 2200 && result.Stats.OutputTokens == 120, "Parse Ollama's snake_case usage counts.");
        Check(result.Stats?.TokensPerSecond == 50 && result.Stats.TotalDurationNanoseconds == 3000000000, "Parse durations and token speed.");
        await service.SendChatAsync(Request(null), null, null);
        Check(!JsonDocument.Parse(handler.Bodies[1]).RootElement.TryGetProperty("think", out _), "An unspecified thinking setting must remain unspecified.");

        foreach (var trace in new[] { Thinking, "{\"message\":{\"content\":\"<think>Only reasoning.</think>\"},\"done\":false}\n", "{\"message\":{\"content\":\"<think>Unfinished reasoning\"},\"done\":false}\n" })
        {
            using var thinkingHandler = new ReplyHandler(_ => new StringContent(trace + Done));
            var failure = await new LocalLlmService(thinkingHandler).SendChatAsync(Request(), null, null);
            Check(!failure.Succeeded && failure.Status.Contains("thinking but no answer"), "Reasoning alone must never count as an answer.");
            Check(thinkingHandler.Bodies.Count == 1, "Do not retry a model already asked to disable thinking.");
        }
        using (var retryHandler = new ReplyHandler(i => new StringContent(i == 1 ? Thinking + Done.Replace("stop", "length") : Answer + Done)))
        {
            var retried = await new LocalLlmService(retryHandler).SendChatAsync(Request(true), null, null);
            Check(retried.Succeeded && retryHandler.Bodies.Count == 2, "Recover a thinking-only response exactly once.");
            using var body = JsonDocument.Parse(retryHandler.Bodies[1]);
            Check(!body.RootElement.GetProperty("think").GetBoolean(), "Recovery must explicitly disable thinking.");
            Check(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString() == Request().Prompt, "Recovery must preserve the original prompt and source evidence.");
        }
        using (var retryHandler = new ReplyHandler(_ => new StringContent(Thinking + Done)))
        {
            var retried = await new LocalLlmService(retryHandler).SendChatAsync(Request(true), null, null);
            Check(!retried.Succeeded && retryHandler.Bodies.Count == 2, "A failed recovery must stop with a visible error.");
        }
        foreach (var text in new[] { "", Done, Answer, Answer + "{\"error\":\"runner ran out of memory\"}\n", "{broken json}\n" })
        {
            using var badHandler = new ReplyHandler(_ => new StringContent(text));
            var events = new List<LocalLlmGenerationProgress>();
            var bad = await new LocalLlmService(badHandler).SendChatAsync(Request(), new ImmediateProgress<LocalLlmGenerationProgress>(events.Add), null);
            Check(!bad.Succeeded, "Empty, incomplete, malformed, or failed streams must not succeed.");
            Check(events.All(e => !e.Done), "Do not report completion before validating the final answer.");
        }
        using (var lengthHandler = new ReplyHandler(_ => new StringContent(Answer + Done.Replace("stop", "length"))))
        {
            var partial = await new LocalLlmService(lengthHandler).SendChatAsync(Request(), null, null);
            Check(!partial.Succeeded && partial.Message!.Contains("Response incomplete") && partial.Message.Contains("Pizza answer"), "Keep partial text but explicitly flag truncation.");
        }
        await CheckStreamingAndCancellation();
        Console.WriteLine($"Local chat regression passed: {_checks} checks.");
    }

    private static async Task CheckStreamingAndCancellation()
    {
        using var stream = new GatedStream(Answer, Done);
        using var handler = new ReplyHandler(_ => new StreamContent(stream));
        var firstDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = new LocalLlmService(handler).SendChatAsync(Request(), new ImmediateProgress<LocalLlmGenerationProgress>(p =>
        {
            if (!string.IsNullOrEmpty(p.Delta)) firstDelta.TrySetResult();
        }), null, deadline.Token);
        await firstDelta.Task.WaitAsync(deadline.Token);
        Check(!pending.IsCompleted, "Deliver the first delta before the server finishes the response body.");
        stream.Release.TrySetResult();
        Check((await pending).Succeeded, "Finish after the remaining stream arrives.");

        using var stalled = new GatedStream(Thinking, Done);
        using var stalledHandler = new ReplyHandler(_ => new StreamContent(stalled));
        using var stop = new CancellationTokenSource();
        var waiting = new LocalLlmService(stalledHandler).SendChatAsync(Request(), new ImmediateProgress<LocalLlmGenerationProgress>(p =>
        {
            if (p.ThinkingDelta is not null) stop.Cancel();
        }), null, stop.Token);
        var stopped = await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!stopped.Succeeded && stopped.Status.Contains("stopped by the user"), "Cancellation must interrupt a stalled body read.");
    }

    internal static async Task RunLive(string model)
    {
        // Fixture evidence keeps this runtime check repeatable and avoids relying on
        // a particular external search response. Browser integration is tested separately.
        var sources = Enumerable.Range(1, 6).Select(i => new GoogleSearchSource($"Fixture pizza source {i}", $"https://example.com/pizza/{i}", "Test evidence for a pizza list.")).ToArray();
        var pages = Enumerable.Range(1, 3).Select(i => new GooglePageEvidence(i,
            string.Concat(Enumerable.Repeat($"Fixture Place {i} is a pizza restaurant in Vilnius. This is synthetic test evidence; do not invent ratings or addresses. ", 60)), true)).ToArray();
        var research = new GoogleResearchResult(new GoogleSearchResult("pizza Vilnius", GoogleSearchContext.SearchUrl("pizza Vilnius"), sources), pages);
        var prompt = GoogleSearchContext.AugmentPrompt("Using this synthetic evidence, list three pizza places with source citations in under 150 words.", research, 4096);
        var first = new System.Diagnostics.Stopwatch();
        first.Start();
        double? firstDelta = null;
        var chunks = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await new LocalLlmService().SendChatAsync(new LocalLlmRequest(model, prompt, "regression", [], 4096, Think: false, MaxOutputTokens: 600),
            new ImmediateProgress<LocalLlmGenerationProgress>(p => { if (!string.IsNullOrEmpty(p.Delta)) { firstDelta ??= first.Elapsed.TotalSeconds; chunks++; } }),
            new ImmediateProgress<string>(Console.WriteLine), deadline.Token);
        var visible = new ContextControl.Workbench.ViewModels.LocalLlmChatMessageViewModel("assistant", result.Message ?? "").VisibleText;
        Console.WriteLine($"LIVE RESULT succeeded={result.Succeeded}, status={result.Status}, numberedCitations={System.Text.RegularExpressions.Regex.IsMatch(visible, @"\[[1-6]\]")}");
        Console.WriteLine(result.Message);
        Check(result.Succeeded && !string.IsNullOrWhiteSpace(visible), "The live local model must produce a visible answer in 4096 context with research evidence.");
        Check(result.Stats?.PromptTokens > 0 && result.Stats.OutputTokens > 0, "Live token counts must be populated.");
        Check(chunks > 1 && firstDelta < first.Elapsed.TotalSeconds, "Live output must stream before generation completes.");
        Console.WriteLine($"LIVE PASS model={model}, inputChars={prompt.Length}, firstDeltaSeconds={firstDelta:0.00}, elapsedSeconds={first.Elapsed.TotalSeconds:0.00}, chunks={chunks}, {result.Stats!.Summary}");
    }

    private static void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private sealed class ReplyHandler(Func<int, HttpContent> reply) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = reply(Bodies.Count) };
        }
    }
    private sealed class GatedStream(string first, string last) : Stream
    {
        private readonly byte[] _first = Encoding.UTF8.GetBytes(first);
        private readonly byte[] _last = Encoding.UTF8.GetBytes(last);
        private int _position;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _first.Length) await Release.Task.WaitAsync(cancellationToken);
            var data = _position < _first.Length ? _first : _last;
            var offset = _position < _first.Length ? _position : _position - _first.Length;
            var count = Math.Min(buffer.Length, data.Length - offset);
            data.AsMemory(offset, count).CopyTo(buffer); _position += count; return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Stream reads must be asynchronous and cancellable.");
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
