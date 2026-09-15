using System.Net;
using System.Net.Http;
using System.Text;
using ContextControl.Workbench.Services;

internal static class OllamaDownloadTests
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    private sealed class Progress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }
    private const string Start = "{\"status\":\"pulling manifest\"}\n";
    private const string Success = "{\"status\":\"success\"}\n";
    private static string Bytes(long current) => $"{{\"status\":\"pulling layer\",\"digest\":\"sha256:abc\",\"total\":1000,\"completed\":{current}}}\n";

    internal static async Task Run()
    {
        var tracker = new OllamaDownloadProgress("test");
        Check(tracker.Update("pulling manifest", "", null, null, TimeSpan.Zero).Advanced, "A new stage counts as progress.");
        Check(!tracker.Update("pulling manifest", "", null, null, TimeSpan.FromSeconds(1)).Advanced, "Repeated status is not progress.");
        var resumed = tracker.Update("pulling layer", "sha256:abc", 800, 1000, TimeSpan.FromSeconds(2));
        Check(resumed.Progress.Percent == 80 && resumed.Progress.BytesPerSecond is null, "Resumed bytes count toward percentage, not current download speed.");
        var next = tracker.Update("pulling layer", "sha256:abc", 900, 1000, TimeSpan.FromSeconds(4));
        Check(next.Advanced && next.Progress.BytesPerSecond == 50, "Measure speed from newly received bytes.");
        Check(!tracker.Update("pulling layer", "sha256:abc", 900, 1000, TimeSpan.FromSeconds(5)).Advanced, "Unchanged byte counters do not reset the stall timer.");
        Check(tracker.Update("verifying sha256 digest", "", null, null, TimeSpan.FromSeconds(6)).Advanced, "Verification starts a fresh inactivity window.");

        using (var handler = new Handler(_ => Reply(Start + Bytes(800) + Success)))
        {
            var updates = new List<LocalLlmTransferProgress>();
            var result = await new LocalLlmService(handler).PullModelAsync("qwen3.8:27b", new Progress<LocalLlmTransferProgress>(updates.Add), null);
            Check(result.Succeeded && updates.Last().Percent == 100, "The real service uses structured progress and requires terminal success.");
            Check(handler.Bodies.Single().Contains("qwen3.8:27b") && handler.Bodies.Single().Contains("\"stream\":true"), "Pull the requested model through the resumable API.");
        }
        foreach (var transient in new[] { "disconnect", "http", "error" })
        {
            using var handler = new Handler(i => i > 1 ? Reply(Success) : transient == "disconnect" ? Reply(Start + Bytes(800))
                : transient == "http" ? Reply("{\"error\":\"registry unavailable\"}", HttpStatusCode.ServiceUnavailable)
                : Reply(Start + "{\"error\":\"connection reset by peer\"}\n"));
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var result = await new OllamaModelDownloader(client, retryDelay: TimeSpan.Zero).PullAsync("test", null, null, default);
            Check(result.Succeeded && handler.Bodies.Count == 2, "Resume a transient failure: " + transient);
        }
        foreach (var permanent in new[] { "manifest", "unauthorized", "invalid" })
        {
            using var handler = new Handler(_ => permanent == "invalid" ? Reply("broken JSON\n") : permanent == "unauthorized"
                ? Reply("{\"error\":\"authentication required\"}", HttpStatusCode.Unauthorized)
                : Reply(Start + "{\"error\":\"model manifest does not exist\"}\n"));
            using var client = new HttpClient(handler);
            var result = await new OllamaModelDownloader(client, retryDelay: TimeSpan.Zero).PullAsync("test", null, null, default);
            Check(!result.Succeeded && handler.Bodies.Count == 1 && !result.Status.Contains("pulling manifest"), "Report the real error without spinner text: " + permanent);
        }
        using (var handler = new Handler(_ => Reply(Start)))
        using (var client = new HttpClient(handler))
        {
            var result = await new OllamaModelDownloader(client, retryDelay: TimeSpan.Zero).PullAsync("test", null, null, default);
            Check(!result.Succeeded && handler.Bodies.Count == 3 && result.Status.Contains("Partial downloads are kept"), "Bound retries and preserve resumable data.");
        }
        // The full transfer lasts several inactivity windows, but advances within each window.
        using (var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream(
            Enumerable.Range(0, 9).Select(i => Bytes(i * 100)).Append(Success), TimeSpan.FromMilliseconds(80))) }))
        using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
        {
            var result = await new OllamaModelDownloader(client, TimeSpan.FromMilliseconds(300), TimeSpan.Zero).PullAsync("test", null, null, default);
            Check(result.Succeeded && handler.Bodies.Count == 1, "Progressing transfers outlive the timeout window without being killed or restarted.");
        }
        using (var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream([Start, Success], TimeSpan.FromSeconds(2))) }))
        using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
        {
            var result = await new OllamaModelDownloader(client, TimeSpan.FromMilliseconds(60), TimeSpan.Zero).PullAsync("test", null, null, default);
            Check(!result.Succeeded && handler.Bodies.Count == 3 && result.Status.Contains("no progress"), "A genuinely stalled stream is stopped and retried.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream([Start, Success], TimeSpan.FromSeconds(2))) }))
        using (var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
        {
            cancellation.CancelAfter(60);
            var canceled = false;
            try { await new OllamaModelDownloader(client, retryDelay: TimeSpan.Zero).PullAsync("test", null, null, cancellation.Token); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled && handler.Bodies.Count == 1, "User cancellation stops without retrying.");
        }
        Console.WriteLine($"OLLAMA_DOWNLOAD_REGRESSION_PASS {_checks} checks");
    }
    private static HttpResponseMessage Reply(string text, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(text, Encoding.UTF8, "application/x-ndjson") };
    private sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        internal List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Bodies.Add(await request.Content!.ReadAsStringAsync(token)); return response(Bodies.Count); }
    }
    private sealed class DelayedStream(IEnumerable<string> lines, TimeSpan delay) : Stream
    {
        private readonly Queue<byte[]> _lines = new(lines.Select(Encoding.UTF8.GetBytes));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_lines.Count == 0) return 0;
            await Task.Delay(delay, token);
            var line = _lines.Dequeue();
            line.CopyTo(buffer);
            return line.Length;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
