using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class GoogleResearchWindow : Window
{
    private readonly WebView2Host _browser;
    private readonly TextBlock _status;
    private readonly TextBlock _address;
    private CancellationTokenSource? _operation;
    private bool _promptedForConsent;
    public bool IsClosed { get; private set; }

    public GoogleResearchWindow(Action<string>? diagnostic = null)
    {
        Title = "Google research · ContextControl";
        Width = 980;
        Height = 720;
        MinWidth = 540;
        MinHeight = 360;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.Parse("#181E2B"));
        _status = new TextBlock { Text = "Searching Google…", FontSize = 14, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        _address = new TextBlock { FontSize = 11, Foreground = Brushes.LightSteelBlue, TextTrimming = TextTrimming.CharacterEllipsis };
        var cancel = new Button { Content = "Stop research", HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => _operation?.Cancel();
        var instructions = new TextBlock
        {
            Text = "If Google asks for consent or verification, complete it here. Research continues automatically. Closing this window stops the current request.",
            FontSize = 12, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap
        };
        var header = new StackPanel { Margin = new Thickness(14, 10), Spacing = 7 };
        header.Children.Add(_status);
        header.Children.Add(_address);
        header.Children.Add(instructions);
        header.Children.Add(cancel);
        _browser = new WebView2Host
        {
            UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextControl", "GoogleResearch"),
            AllowDownloads = false,
            NavigationFilter = GoogleSearchContext.IsPublicWebUrl
        };
        _browser.Navigate("https://www.google.com/");
        _browser.NavigationCompleted += (_, e) => diagnostic?.Invoke($"Browser navigation: {(e.Succeeded ? "ready" : "failed")} {e.Url}");
        _browser.InitializationFailed += (_, e) => diagnostic?.Invoke(e.Message);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(header);
        Grid.SetRow(_browser, 1);
        layout.Children.Add(_browser);
        Content = layout;
        Closed += (_, _) => { IsClosed = true; _operation?.Cancel(); };
    }

    public async Task<GoogleSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        using var operation = BeginOperation(cancellationToken);
        var token = operation.Token;
        _status.Text = $"Searching Google: {query}";
        _address.Text = GoogleSearchContext.SearchUrl(query);
        var previousNavigation = _browser.StartedNavigationId;
        _browser.Navigate(_address.Text!);
        var timer = Stopwatch.StartNew();
        try
        {
            while (timer.Elapsed < TimeSpan.FromMinutes(3))
            {
                token.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (HasCompletedNavigation(previousNavigation) && GoogleSearchContext.IsMatchingSearchPage(_browser.Source, query))
                {
                    var json = await _browser.ExecuteScriptAsync(GoogleResearchScripts.SearchResults).WaitAsync(TimeSpan.FromSeconds(5), token);
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.TryGetProperty("needsConsent", out var consent) && consent.ValueKind == JsonValueKind.True) RequestBrowserAttention();
                    var result = GoogleSearchContext.ParseBrowserResult(query, json);
                    if (result.Sources.Count > 0) { _status.Text = $"Found {result.Sources.Count} sources. The model is choosing pages to read…"; return result; }
                }
                if (timer.Elapsed > TimeSpan.FromSeconds(10))
                {
                    _status.Text = "Waiting for readable Google results. Complete any consent or verification below, or stop research.";
                    RequestBrowserAttention();
                }
                await Task.Delay(500, token);
            }
            throw new InvalidOperationException("Google did not provide readable results within 3 minutes. Complete consent/verification in the Google window and retry, or switch Google off.");
        }
        finally { _browser.Stop(); _operation = null; }
    }

    public async Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken cancellationToken)
    {
        if (!GoogleSearchContext.IsPublicWebUrl(source.Url)) throw new InvalidOperationException("Only public HTTP(S) source pages can be read.");
        using var operation = BeginOperation(cancellationToken);
        var token = operation.Token;
        _status.Text = $"Reading {source.Title}";
        _address.Text = source.Url;
        var previousNavigation = _browser.StartedNavigationId;
        _browser.Navigate(source.Url);
        var timer = Stopwatch.StartNew();
        try
        {
            while (timer.Elapsed < TimeSpan.FromSeconds(25))
            {
                token.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (HasFinishedNavigation(previousNavigation))
                {
                    GooglePageReader.ThrowIfHttpError(_browser.Source, _browser.LastHttpStatusCode);
                    if (!_browser.LastNavigationSucceeded) throw new GooglePageUnavailableException(_browser.Source, "Navigation to this source failed.");
                }
                if (HasCompletedNavigation(previousNavigation))
                {
                    var page = GooglePageReader.Parse(await _browser.ExecuteScriptAsync(GoogleResearchScripts.PageText).WaitAsync(TimeSpan.FromSeconds(5), token));
                    // Do not accidentally return a previous page or a redirect to a login/consent provider.
                    if (MatchesSourcePage(source.Url, page.Url) && page.Text.Length >= 120) return page;
                }
                await Task.Delay(400, token);
            }
            throw new InvalidOperationException("No readable text was available from this source (blocked, redirected, non-HTML, or timed out).");
        }
        finally { _browser.Stop(); _operation = null; }
    }

    public static bool MatchesSourcePage(string requested, string? actual)
    {
        if (!GoogleSearchContext.IsPublicWebUrl(actual)) return false;
        var expected = new Uri(requested);
        var found = new Uri(actual!);
        if (GoogleSearchContext.IsGoogleResultRedirect(requested))
            return !found.Host.Equals("www.google.com", StringComparison.OrdinalIgnoreCase) && !found.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase);
        return expected.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase).Equals(found.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
            && expected.AbsolutePath.TrimEnd('/').Equals(found.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && expected.Query == found.Query;
    }

    private bool HasFinishedNavigation(ulong previous) => _browser.IsReady && !_browser.IsNavigating
        && _browser.StartedNavigationId != previous && _browser.StartedNavigationId == _browser.CompletedNavigationId;

    private bool HasCompletedNavigation(ulong previous) => HasFinishedNavigation(previous) && _browser.LastNavigationSucceeded;

    private void RequestBrowserAttention()
    {
        if (_promptedForConsent) return;
        _promptedForConsent = true;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _promptedForConsent = false;
        if (IsClosed) throw new OperationCanceledException("Google research window was closed.", token);
        return _operation = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    private void ThrowIfUnavailable()
    {
        if (_browser.InitializationError is { } error) throw new InvalidOperationException(error);
    }
}
