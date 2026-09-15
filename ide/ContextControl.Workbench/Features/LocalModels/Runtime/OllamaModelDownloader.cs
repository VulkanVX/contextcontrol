using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ContextControl.Workbench.Services;

// The local daemon owns resumable blobs. Keep one streaming request alive while work advances,
// rather than terminating a healthy CLI process after a fixed wall-clock duration.
internal sealed class OllamaModelDownloader(HttpClient http, TimeSpan? idleLimit = null, TimeSpan? retryDelay = null)
{
    private static readonly Uri PullUri = new("http://127.0.0.1:11434/api/pull");
    private readonly TimeSpan _idleLimit = idleLimit ?? TimeSpan.FromMinutes(5);
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    internal async Task<LocalLlmChatResult> PullAsync(string model, IProgress<LocalLlmTransferProgress>? progress,
        IProgress<string>? terminal, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model)) return new(false, "No model selected.");
        var operation = $"Downloading {model}";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (attempt > 0)
            {
                var status = $"Resuming {model} from cached data (retry {attempt}/2)…";
                progress?.Report(new(operation, status, null, null, null, null));
                terminal?.Report(status);
                await Task.Delay(_retryDelay * attempt, token).ConfigureAwait(false);
            }
            var outcome = await AttemptAsync(model, operation, progress, terminal, token).ConfigureAwait(false);
            if (outcome.Success) return new(true, $"Downloaded {model}.");
            terminal?.Report(outcome.Error);
            if (!outcome.Retry || attempt == 2)
                return new(false, outcome.Error + " Partial downloads are kept; retry resumes available data.");
        }
        throw new InvalidOperationException("Unreachable retry state.");
    }
    private async Task<(bool Success, bool Retry, string Error)> AttemptAsync(string model, string operation,
        IProgress<LocalLlmTransferProgress>? progress, IProgress<string>? terminal, CancellationToken token)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(_idleLimit);
        var clock = Stopwatch.StartNew();
        var tracker = new OllamaDownloadProgress(operation);
        var lastLog = TimeSpan.MinValue;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PullUri) { Content = JsonContent.Create(new { model, stream = true }) };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token).ConfigureAwait(false);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(idle.Token).ConfigureAwait(false));
            if (!response.IsSuccessStatusCode)
            {
                var chars = new char[8192];
                var count = await reader.ReadBlockAsync(chars.AsMemory(), idle.Token).ConfigureAwait(false);
                var error = new string(chars, 0, count).Trim();
                try { using var body = JsonDocument.Parse(error); error = body.RootElement.GetProperty("error").GetString() ?? error; }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
                return (false, response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500,
                    $"Ollama download failed (HTTP {(int)response.StatusCode}): {error}");
            }
            while (await reader.ReadLineAsync(idle.Token).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length > 65536) return (false, false, "Ollama sent an oversized download status.");
                using var document = JsonDocument.Parse(line);
                var frame = document.RootElement;
                if (frame.TryGetProperty("error", out var failure))
                {
                    var error = failure.GetString() ?? "Unknown download error.";
                    return (false, Transient(error), "Ollama download failed: " + error);
                }
                var status = frame.TryGetProperty("status", out var state) ? state.GetString() ?? "" : "";
                if (status == "success")
                {
                    progress?.Report(new(operation, "Download complete", null, null, null, 100));
                    terminal?.Report($"Downloaded {model}.");
                    return (true, false, "");
                }
                var digest = frame.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
                long? ReadSize(string name) => frame.TryGetProperty(name, out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var n) && n >= 0 ? n : null;
                var update = tracker.Update(status, digest, ReadSize("completed"), ReadSize("total"), clock.Elapsed);
                if (update.Advanced) idle.CancelAfter(_idleLimit);
                progress?.Report(update.Progress);
                if (lastLog == TimeSpan.MinValue || clock.Elapsed - lastLog >= TimeSpan.FromSeconds(1) || update.StageChanged)
                {
                    var p = update.Progress;
                    terminal?.Report(p.TotalBytes is > 0 ? $"{p.Status}: {p.CurrentBytes / 1e9:0.00} / {p.TotalBytes / 1e9:0.00} GB ({p.Percent:0.0}%)"
                        + (p.BytesPerSecond is { } speed ? $" · {speed / 1e6:0.0} MB/s" : "") : p.Status);
                    lastLog = clock.Elapsed;
                }
            }
            return (false, true, "Ollama closed the download stream before confirming success.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return (false, true, $"Ollama download made no progress for {_idleLimit.TotalMinutes:0.##} minutes."); }
        catch (HttpRequestException ex) { return (false, true, "Cannot reach Ollama's download service. Ensure Ollama is running: " + ex.Message); }
        catch (IOException ex) { return (false, true, "Ollama download connection was interrupted: " + ex.Message); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return (false, false, "Ollama sent invalid download status: " + ex.Message); }
    }
    private static bool Transient(string error) => new[] { "timeout", "timed out", "connection reset", "unexpected eof", "temporarily", "max retries exceeded", "tls handshake", "connection refused" }
        .Any(part => error.Contains(part, StringComparison.OrdinalIgnoreCase));
}

internal sealed class OllamaDownloadProgress(string operation)
{
    private readonly Dictionary<string, (long Current, TimeSpan At)> _layers = new();
    private string _stage = "";
    internal (LocalLlmTransferProgress Progress, bool Advanced, bool StageChanged) Update(string status, string digest,
        long? current, long? total, TimeSpan elapsed)
    {
        var stageChanged = status != _stage;
        _stage = status;
        var advanced = stageChanged && digest.Length == 0;
        double? speed = null;
        if (digest.Length > 0 && current is >= 0 && total is > 0)
        {
            current = Math.Min(current.Value, total.Value);
            if (_layers.TryGetValue(digest, out var previous))
            {
                advanced |= current > previous.Current;
                if (current >= previous.Current && elapsed > previous.At)
                    speed = (current.Value - previous.Current) / (elapsed - previous.At).TotalSeconds;
            }
            else advanced = true;
            if (!_layers.TryGetValue(digest, out var stored) || stored.Current != current.Value)
                _layers[digest] = (current.Value, elapsed);
        }
        return (new(operation, status.Length > 0 ? status : "Waiting for download progress…", current, total, speed,
            current is >= 0 && total is > 0 ? Math.Clamp(current.Value * 100d / total.Value, 0, 100) : null), advanced, stageChanged);
    }
}
