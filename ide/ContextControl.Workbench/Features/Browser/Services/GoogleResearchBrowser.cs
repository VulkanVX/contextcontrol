using Avalonia.Threading;
using ContextControl.Workbench.Views;

namespace ContextControl.Workbench.Services;

/// <summary>Serializes browser operations across concurrent chats; each operation checks its requested URL.</summary>
public sealed class GoogleResearchBrowser(Action<string>? diagnostic = null) : IGoogleResearchBrowser, IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private GoogleResearchWindow? _window;
    public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken token) =>
        RunAsync((window, ct) => window.SearchAsync(query, ct), token);
    public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken token) =>
        RunAsync((window, ct) => window.ReadPageAsync(source, ct), token);

    private async Task<T> RunAsync<T>(Func<GoogleResearchWindow, CancellationToken, Task<T>> action, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _queue.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            var operation = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Google research currently requires Windows and the WebView2 Runtime.");
                if (_window is null || _window.IsClosed) _window = new GoogleResearchWindow(diagnostic);
                if (!_window.IsVisible) _window.Show();
                return action(_window, linked.Token);
            });
            return operation;
        }
        finally { _queue.Release(); }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _window?.Close();
        _window = null;
    }
}
