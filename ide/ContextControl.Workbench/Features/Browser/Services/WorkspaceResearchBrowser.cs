using Avalonia.Threading;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Services;

public interface IGoogleResearchSession : IGoogleResearchBrowser, IDisposable
{
    void SetStatus(string status);
}
public interface IGoogleResearchSessionFactory
{
    IGoogleResearchSession CreateSession(string chatId, string title, Action cancelRequest, CancellationToken token);
}

/// <summary>One independently navigable workbench tab per research chat; operations serialize only within that chat.</summary>
public sealed class WorkspaceResearchBrowser(BrowserPaneViewModel pane, Func<BrowserTabViewModel, WebView2Host> getHost,
    Action<BrowserTabViewModel> showTab) : IGoogleResearchBrowser, IGoogleResearchSessionFactory, IDisposable
{
    private readonly HashSet<Session> _sessions = [];
    private readonly BrowserPaneViewModel _pane = pane;
    private readonly Action<BrowserTabViewModel> _showTab = showTab;
    private bool _disposed;
    public IGoogleResearchSession CreateSession(string chatId, string title, Action cancelRequest, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); token.ThrowIfCancellationRequested();
        if (_disposed) throw new ObjectDisposedException(nameof(WorkspaceResearchBrowser));
        var tab = _pane.OpenResearchTab(chatId, title);
        if (tab.IsAgentWorking) throw new InvalidOperationException("This chat already has an active research session.");
        var session = new Session(this, tab, getHost(tab), cancelRequest, token);
        _sessions.Add(session); return session;
    }
    public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken token) => throw new InvalidOperationException("Research requires its chat session.");
    public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken token) => throw new InvalidOperationException("Research requires its chat session.");
    public void Dispose()
    {
        _disposed = true;
        foreach (var session in _sessions.ToArray()) { session.Cancel(); session.Dispose(); }
    }
    private sealed class Session : IGoogleResearchSession
    {
        private readonly WorkspaceResearchBrowser _owner;
        private readonly BrowserTabViewModel _tab;
        private readonly GoogleResearchReader _reader;
        private readonly WebView2Host _host;
        private readonly Action _cancelRequest;
        private readonly CancellationTokenSource _lifetime;
        private readonly SemaphoreSlim _queue = new(1, 1);
        private bool _disposed;
        private bool _failed;
        public Session(WorkspaceResearchBrowser owner, BrowserTabViewModel tab, WebView2Host host, Action cancelRequest, CancellationToken token)
        {
            _owner = owner; _tab = tab; _host = host; _cancelRequest = cancelRequest;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _reader = new(host, SetStatus, () => { SetStatus("Needs attention"); owner._showTab(tab); }, () => _disposed || !owner._pane.Tabs.Contains(tab));
            tab.CancelResearch = Cancel; tab.IsAgentWorking = true; SetStatus("Planning");
        }
        public void SetStatus(string status)
        {
            void Apply() { if (!_disposed) _tab.AgentStatus = status; }
            if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Post(Apply);
        }
        public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken token) => Run(ct => _reader.SearchAsync(query, ct), token);
        public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken token) => Run(ct => _reader.ReadPageAsync(source, ct), token);
        private async Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        {
            if (_disposed) throw new OperationCanceledException("Research tab was closed.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            await _queue.WaitAsync(linked.Token);
            try
            {
                return await Dispatcher.UIThread.InvokeAsync(() => { linked.Token.ThrowIfCancellationRequested(); return action(linked.Token); });
            }
            catch (OperationCanceledException) { throw; }
            catch { _failed = true; throw; }
            finally { _queue.Release(); }
        }
        public void Cancel()
        {
            if (_disposed) return;
            _lifetime.Cancel(); _reader.Cancel(); _host.Stop(); _cancelRequest();
        }
        public void Dispose()
        {
            if (_disposed) return;
            var stopped = _lifetime.IsCancellationRequested;
            _disposed = true; _lifetime.Cancel(); _host.Stop();
            _tab.CancelResearch = null; _tab.IsAgentWorking = false; _tab.AgentStatus = stopped ? "Stopped" : _failed ? "Finished · some sources unavailable" : "Done";
            _owner._sessions.Remove(this);
            // Operation tokens may still be unwinding after a cancellation.
        }
    }
}
