using System.Collections.ObjectModel;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.ViewModels;

public sealed class BrowserPaneViewModel : ObservableObject
{
    private const string DefaultUrl = "https://chatgpt.com";

    private string _urlText = "https://chatgpt.com";
    private string _status = "Ready.";
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;
    private BrowserExternalTargetViewModel? _selectedExternalBrowser;
    private BrowserTabViewModel? _selectedTab;

    public BrowserPaneViewModel(
        string contextControlRoot,
        IReadOnlyList<ExternalBrowserTarget>? externalBrowsers,
        string selectedExternalBrowserKey)
    {
        NativeUserDataFolder = Path.Combine(contextControlRoot, ".ccWorkbench.browser-data", "WebView2");
        ExternalBrowsers = new ObservableCollection<BrowserExternalTargetViewModel>(
            (externalBrowsers is { Count: > 0 } ? externalBrowsers : [ExternalBrowserService.DefaultTarget])
            .Select(target => new BrowserExternalTargetViewModel(target)));
        _selectedExternalBrowser = ExternalBrowsers.FirstOrDefault(browser =>
            string.Equals(browser.Key, selectedExternalBrowserKey, StringComparison.OrdinalIgnoreCase))
            ?? ExternalBrowsers.FirstOrDefault(browser => browser.Key == "default")
            ?? ExternalBrowsers.FirstOrDefault();

        Tabs = [new BrowserTabViewModel(Guid.NewGuid().ToString("N"), DefaultUrl, CreateTabTitle(DefaultUrl))];
        _selectedTab = Tabs[0];
        _selectedTab.IsActive = true;
        Tabs.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null) foreach (BrowserTabViewModel tab in e.OldItems) tab.PropertyChanged -= OnTabChanged;
            if (e.NewItems is not null) foreach (BrowserTabViewModel tab in e.NewItems) tab.PropertyChanged += OnTabChanged;
            NotifyAgents();
        };
        _selectedTab.PropertyChanged += OnTabChanged;
    }

    public event EventHandler<string>? ExternalBrowserSelectionChanged;

    public string NativeUserDataFolder { get; }

    public ObservableCollection<BrowserExternalTargetViewModel> ExternalBrowsers { get; }

    public bool HasExternalBrowsers => ExternalBrowsers.Count > 0;

    public ObservableCollection<BrowserTabViewModel> Tabs { get; }

    public bool HasMultipleTabs => Tabs.Count > 1;
    public bool HasResearchTabs => Tabs.Any(tab => tab.IsResearch);
    public IReadOnlyList<BrowserTabViewModel> ResearchTabs => Tabs.Reverse().Where(tab => tab.IsResearch).OrderByDescending(tab => tab.IsAgentWorking).Take(4).ToArray();
    private System.Windows.Input.ICommand? _openResearchTabCommand;
    public System.Windows.Input.ICommand? OpenResearchTabCommand { get => _openResearchTabCommand; set => SetProperty(ref _openResearchTabCommand, value); }
    private bool _isActionPreviewEnabled;
    public bool IsActionPreviewEnabled { get => _isActionPreviewEnabled; set => SetProperty(ref _isActionPreviewEnabled, value); }
    public int ActiveAgentCount => Tabs.Count(tab => tab.IsAgentWorking);
    public bool HasActiveAgents => ActiveAgentCount > 0;
    public string AgentActivityLabel => $"{ActiveAgentCount} {(ActiveAgentCount == 1 ? "agent" : "agents")} working";
    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserTabViewModel.IsAgentWorking)) NotifyAgents();
    }
    private void NotifyAgents()
    {
        OnPropertyChanged(nameof(ActiveAgentCount)); OnPropertyChanged(nameof(HasActiveAgents)); OnPropertyChanged(nameof(AgentActivityLabel));
        OnPropertyChanged(nameof(HasResearchTabs)); OnPropertyChanged(nameof(ResearchTabs));
    }
    public BrowserTabViewModel OpenResearchTab(string id, string title)
    {
        var tab = Tabs.FirstOrDefault(item => item.Id == "research-" + id);
        if (tab is null) { tab = new("research-" + id, "https://www.google.com/", title) { IsResearch = true }; Tabs.Add(tab); }
        tab.Title = title;
        OnPropertyChanged(nameof(HasMultipleTabs));
        return tab;
    }
    public BrowserTabViewModel OpenArticle(string title, string html)
    {
        var tab = new BrowserTabViewModel(Guid.NewGuid().ToString("N"), "about:blank", title) { DocumentHtml = html };
        Tabs.Add(tab); OnPropertyChanged(nameof(HasMultipleTabs)); SelectTab(tab); return tab;
    }
    public void TabNavigating(BrowserTabViewModel tab, string url)
    {
        tab.Url = url;
        if (!tab.IsResearch && tab.DocumentHtml is null) tab.Title = CreateTabTitle(url);
        if (ReferenceEquals(tab, SelectedTab)) { UrlText = url; Status = url; IsLoading = true; }
    }
    public void TabNavigated(BrowserTabViewModel tab, string? url, bool succeeded, bool back, bool forward)
    {
        if (!string.IsNullOrWhiteSpace(url)) tab.Url = url;
        if (!tab.IsResearch && tab.DocumentHtml is null) tab.Title = CreateTabTitle(tab.Url);
        if (ReferenceEquals(tab, SelectedTab)) { UrlText = tab.Url; Status = succeeded ? tab.Url : "Navigation failed."; IsLoading = false; CanGoBack = back; CanGoForward = forward; }
    }

    public BrowserTabViewModel? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (ReferenceEquals(_selectedTab, value))
            {
                return;
            }

            if (_selectedTab is not null)
            {
                _selectedTab.IsActive = false;
            }

            if (value is not null)
            {
                value.IsActive = true;
            }

            SetProperty(ref _selectedTab, value);
        }
    }

    public BrowserExternalTargetViewModel? SelectedExternalBrowser
    {
        get => _selectedExternalBrowser;
        set
        {
            if (value is null || ReferenceEquals(_selectedExternalBrowser, value))
            {
                return;
            }

            if (SetProperty(ref _selectedExternalBrowser, value))
            {
                ExternalBrowserSelectionChanged?.Invoke(this, value.Key);
            }
        }
    }

    public string UrlText
    {
        get => _urlText;
        set => SetProperty(ref _urlText, value ?? "");
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(LoadingLabel));
            }
        }
    }

    public string LoadingLabel => IsLoading ? "loading" : "";

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => SetProperty(ref _canGoForward, value);
    }

    public string NormalizeUrl()
    {
        var text = (UrlText ?? "").Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "https://chatgpt.com";
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            return text;
        }

        if (text.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("127.", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return $"http://{text}";
        }

        return $"https://{text}";
    }

    public void BeginNavigation(string url)
    {
        UrlText = url;
        Status = url;
        IsLoading = true;
        UpdateSelectedTab(url);
    }

    public void CompleteNavigation(string? url, bool succeeded, bool canGoBack, bool canGoForward)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            UrlText = url;
        }

        Status = succeeded ? UrlText : "Navigation failed.";
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
        IsLoading = false;
        UpdateSelectedTab(UrlText);
    }

    public void SetBrowserUnavailable(string detail)
    {
        Status = string.IsNullOrWhiteSpace(detail)
            ? "Embedded browser unavailable."
            : detail;
        IsLoading = false;
    }

    public void CompleteExternalOpen(string browserName, string url)
    {
        UrlText = url;
        Status = $"Opened in {browserName}.";
        IsLoading = false;
        UpdateSelectedTab(url);
    }

    public BrowserTabViewModel AddTab()
    {
        var tab = new BrowserTabViewModel(Guid.NewGuid().ToString("N"), DefaultUrl, CreateTabTitle(DefaultUrl));
        Tabs.Add(tab);
        OnPropertyChanged(nameof(HasMultipleTabs));
        SelectTab(tab);
        return tab;
    }

    public bool SelectTab(BrowserTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return false;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            return false;
        }

        SelectedTab = tab;
        UrlText = tab.Url;
        Status = tab.Url;
        CanGoBack = false;
        CanGoForward = false;
        IsLoading = false;
        return true;
    }

    public BrowserTabViewModel? CloseTab(BrowserTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
        {
            return null;
        }

        if (Tabs.Count == 1) AddTab();

        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return null;
        }

        var wasActive = ReferenceEquals(SelectedTab, tab);
        tab.CancelResearch?.Invoke();
        tab.ActionPreviewImage = null;
        Tabs.RemoveAt(index);
        OnPropertyChanged(nameof(HasMultipleTabs));

        if (!wasActive)
        {
            return null;
        }

        var nextIndex = Math.Min(index, Tabs.Count - 1);
        var nextTab = Tabs[nextIndex];
        SelectedTab = null;
        SelectTab(nextTab);
        return nextTab;
    }

    private void UpdateSelectedTab(string url)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        SelectedTab.Url = url;
        if (!SelectedTab.IsResearch && SelectedTab.DocumentHtml is null) SelectedTab.Title = CreateTabTitle(url);
    }

    private static string CreateTabTitle(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    ? uri.Host[4..]
                    : uri.Host;
            }
        }

        return string.IsNullOrWhiteSpace(url) ? "New tab" : url;
    }
}
