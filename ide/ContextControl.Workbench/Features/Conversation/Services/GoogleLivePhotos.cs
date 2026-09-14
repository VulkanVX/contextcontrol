namespace ContextControl.Workbench.Services;

/// <summary>One bounded photo worker per answer, shared by streaming and completion.</summary>
public sealed class GoogleLivePhotos : IDisposable
{
    private readonly GoogleResearchResult _research;
    private readonly IGoogleResearchBrowser _browser;
    private readonly Action<GoogleEntryPhoto> _found;
    private readonly CancellationTokenSource _lifetime;
    private readonly Queue<string> _pending = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _images = new(StringComparer.Ordinal);
    private readonly Action<string>? _status;
    private Task _worker = Task.CompletedTask;
    private bool _running, _disposed;
    private int _lastLineEnd = -1;
    private string _markdown = "";
    public int PhotoCount { get; private set; }

    public GoogleLivePhotos(GoogleResearchResult research, IGoogleResearchBrowser browser,
        Action<GoogleEntryPhoto> found, CancellationToken token, Action<string>? status = null)
    {
        _research = research; _browser = browser; _found = found; _status = status;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (!string.IsNullOrWhiteSpace(research.PhotoSubject)) Enqueue(research.PhotoSubject);
    }

    public void Observe(string markdown, bool complete = false)
    {
        if (_disposed || _research.PhotoSubject is not null) return;
        // Never search a partly streamed name. Parsing only at newline boundaries also
        // avoids doing Markdown work on every incoming token.
        var end = complete ? markdown.Length : markdown.LastIndexOf('\n');
        if (end < 0 || !complete && end <= _lastLineEnd) return;
        _lastLineEnd = end;
        _markdown = markdown[..end];
        foreach (var name in GoogleEntryPhotoService.EntryNames(_markdown)) Enqueue(name);
    }

    private void Enqueue(string name)
    {
        if (_disposed || _seen.Count >= GoogleEntryPhotoService.MaxEntries || !_seen.Add(GoogleEntryPhotoService.NormalizeName(name))) return;
        _pending.Enqueue(name);
        if (!_running) _worker = DrainAsync();
    }

    private async Task DrainAsync()
    {
        _running = true;
        try
        {
            while (!_disposed && !_lifetime.IsCancellationRequested && _pending.TryDequeue(out var name))
            {
                try
                {
                await GoogleEntryPhotoService.LoadAsync(_markdown, _research, _browser, photo =>
                {
                    if (_disposed || !_images.Add(photo.PreviewPath)) return;
                    PhotoCount++; _found(photo);
                }, _lifetime.Token, status: _status, onlyEntries: [name]);
                }
                catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException or InvalidOperationException
                    or System.Text.Json.JsonException or TimeoutException or System.Runtime.InteropServices.COMException) { }
            }
        }
        finally { _running = false; }
    }

    public async Task CompleteAsync(string markdown)
    {
        Observe(markdown, complete: true);
        await _worker;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _pending.Clear(); _lifetime.Cancel();
        if (_worker.IsCompleted) _lifetime.Dispose();
        else _ = _worker.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
    }
}
