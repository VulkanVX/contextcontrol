using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Views.MainWindowParts;

/// <summary>Real browser tabs remain attached while hidden, preserving page and navigation state.</summary>
public sealed partial class BrowserSurface : UserControl
{
    private BrowserPaneViewModel? _pane;
    private readonly Dictionary<string, WebView2Host> _hosts = [];
    private readonly HashSet<string> _capturing = [];
    public BrowserSurface()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BindPane((DataContext as WorkbenchViewModel)?.BrowserPane);
    }
    internal WebView2Host BrowserWebViewControl => GetHost(_pane?.SelectedTab ?? throw new InvalidOperationException("Select a browser tab first."));
    public void BindPane(BrowserPaneViewModel? pane)
    {
        if (ReferenceEquals(_pane, pane)) return;
        if (_pane is not null) { _pane.PropertyChanged -= OnPaneChanged; _pane.Tabs.CollectionChanged -= OnTabsChanged; }
        foreach (var host in _hosts.Values) host.Stop();
        TabHosts.Children.Clear(); _hosts.Clear(); _pane = pane;
        if (_pane is null) return;
        _pane.PropertyChanged += OnPaneChanged; _pane.Tabs.CollectionChanged += OnTabsChanged;
        Synchronize();
    }
    public WebView2Host GetHost(BrowserTabViewModel tab)
    {
        if (_hosts.TryGetValue(tab.Id, out var existing)) return existing;
        if (_pane is null || !_pane.Tabs.Contains(tab)) throw new InvalidOperationException("Browser tab was closed.");
        var pane = _pane;
        var host = new WebView2Host { UserDataFolder = pane.NativeUserDataFolder, IsVisible = ReferenceEquals(pane.SelectedTab, tab),
            AllowDownloads = !tab.IsResearch && tab.DocumentHtml is null,
            NavigationFilter = tab.IsResearch ? GoogleSearchContext.IsPublicWebUrl : tab.DocumentHtml is not null
                ? url => url.StartsWith("about:blank", StringComparison.Ordinal) || GoogleSearchContext.IsPublicWebUrl(url) : null };
        host.NavigationStarted += (_, e) => pane.TabNavigating(tab, e.Url);
        host.NavigationCompleted += (_, e) =>
        {
            pane.TabNavigated(tab, e.Url, e.Succeeded, e.CanGoBack, e.CanGoForward);
            if (e.Succeeded) _ = CapturePreviewAsync(tab, host);
        };
        host.InitializationFailed += (_, e) => { tab.AgentStatus = e.Message; if (ReferenceEquals(pane.SelectedTab, tab)) pane.SetBrowserUnavailable(e.Message); };
        if (tab.DocumentHtml is { } html) host.NavigateHtml(html); else host.Navigate(tab.Url);
        _hosts.Add(tab.Id, host); TabHosts.Children.Add(host);
        return host;
    }
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Synchronize();
    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserPaneViewModel.SelectedTab)) Synchronize();
        if (e.PropertyName == nameof(BrowserPaneViewModel.IsActionPreviewEnabled) && _pane is not null)
            foreach (var tab in _pane.Tabs.Where(tab => tab.IsResearch))
                if (_pane.IsActionPreviewEnabled) _ = CapturePreviewAsync(tab, GetHost(tab)); else tab.ActionPreviewImage = null;
    }
    private async Task CapturePreviewAsync(BrowserTabViewModel tab, WebView2Host host)
    {
        if (_pane is not { IsActionPreviewEnabled: true } || !tab.IsResearch || !_capturing.Add(tab.Id)) return;
        try
        {
            var preview = await host.CaptureActionPreviewAsync();
            if (_pane is { IsActionPreviewEnabled: true } pane && pane.Tabs.Contains(tab)) tab.ActionPreviewImage = preview; else preview?.Dispose();
        }
        finally { _capturing.Remove(tab.Id); }
    }
    private void Synchronize()
    {
        if (_pane is null) return;
        foreach (var id in _hosts.Keys.Where(id => !_pane.Tabs.Any(tab => tab.Id == id)).ToArray())
        {
            var host = _hosts[id]; host.Stop(); TabHosts.Children.Remove(host); _hosts.Remove(id);
        }
        foreach (var tab in _pane.Tabs) GetHost(tab).IsVisible = ReferenceEquals(tab, _pane.SelectedTab);
        if (_pane.SelectedTab is { } selected && _hosts.TryGetValue(selected.Id, out var active))
            _pane.TabNavigated(selected, active.Source, active.LastNavigationSucceeded, active.CanGoBack, active.CanGoForward);
    }
}
