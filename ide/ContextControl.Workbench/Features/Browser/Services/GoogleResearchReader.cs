using System.Diagnostics;
using System.Text.Json;
using ContextControl.Workbench.Controls;

namespace ContextControl.Workbench.Services;

/// <summary>Read-only research operations on one tab. Navigation never crosses chat sessions.</summary>
public sealed class GoogleResearchReader(WebView2Host browser, Action<string> setStatus, Action requestAttention, Func<bool> isClosed)
{
    private readonly WebView2Host _browser = browser;
    private readonly Action<string> _setStatus = setStatus;
    private readonly Action _requestAttention = requestAttention;
    private readonly Func<bool> _isClosed = isClosed;
    private CancellationTokenSource? _operation;
    private bool _promptedForConsent;
    public void Cancel() => _operation?.Cancel();
    public async Task<GoogleSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        using var operation = BeginOperation(cancellationToken);
        var token = operation.Token;
        _setStatus($"Searching Google: {query}");
        var searchUrl = GoogleSearchContext.SearchUrl(query);
        var previousNavigation = _browser.StartedNavigationId;
        _browser.Navigate(searchUrl);
        var timer = Stopwatch.StartNew();
        try
        {
            while (timer.Elapsed < TimeSpan.FromMinutes(3))
            {
                token.ThrowIfCancellationRequested();
                ThrowIfUnavailable();
                if (HasCompletedNavigation(previousNavigation) && await HandlePageGateAsync(token))
                {
                    await Task.Delay(500, token);
                    continue;
                }
                if (HasCompletedNavigation(previousNavigation) && GoogleSearchContext.IsMatchingSearchPage(_browser.Source, query))
                {
                    var json = await _browser.ExecuteScriptAsync(GoogleResearchScripts.SearchResults).WaitAsync(TimeSpan.FromSeconds(5), token);
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.TryGetProperty("needsConsent", out var consent) && consent.ValueKind == JsonValueKind.True) RequestBrowserAttention();
                    var result = GoogleSearchContext.ParseBrowserResult(query, json);
                    if (result.Sources.Count > 0) { _setStatus($"Found {result.Sources.Count} sources. The model is choosing pages to read…"); return result; }
                }
                if (timer.Elapsed > TimeSpan.FromSeconds(10))
                {
                    _setStatus("Waiting for readable Google results. Complete any consent or verification below, or stop research.");
                    RequestBrowserAttention();
                }
                await Task.Delay(500, token);
            }
            throw new InvalidOperationException("Google did not provide readable results within 3 minutes. Complete consent/verification in the Browser research tab and retry, or switch Google off.");
        }
        finally { _browser.Stop(); _operation = null; }
    }

    public async Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken cancellationToken)
    {
        if (!GoogleSearchContext.IsPublicWebUrl(source.Url)) throw new InvalidOperationException("Only public HTTP(S) source pages can be read.");
        using var operation = BeginOperation(cancellationToken);
        var token = operation.Token;
        _setStatus($"Reading {source.Title}");
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
                    if (await HandlePageGateAsync(token))
                    {
                        await Task.Delay(400, token);
                        continue;
                    }
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

    private async Task<bool> HandlePageGateAsync(CancellationToken token)
    {
        var json = await _browser.ExecuteScriptAsync(BrowserPageGate.InspectAndRejectOptionalCookies).WaitAsync(TimeSpan.FromSeconds(5), token);
        using var document = JsonDocument.Parse(json);
        var kind = document.RootElement.TryGetProperty("kind", out var value) ? value.GetString() : "none";
        if (kind == "login")
            throw new GooglePageUnavailableException(_browser.Source, "This source is behind a login screen. Its post or article could not be read.");
        if (kind == "cookie-choice")
        {
            _setStatus("Rejecting optional cookies · continuing research…");
            return true;
        }
        if (kind == "consent")
        {
            _setStatus("Waiting for cookie preference confirmation in the Browser tab…");
            RequestBrowserAttention();
            return true;
        }
        if (BrowserPageGate.IsAuthenticationUrl(_browser.Source))
            throw new GooglePageUnavailableException(_browser.Source, "This source requires a login. No private content was read; continuing with public sources.");
        return false;
    }

    private bool HasFinishedNavigation(ulong previous) => _browser.IsReady && !_browser.IsNavigating
        && _browser.StartedNavigationId != previous && _browser.StartedNavigationId == _browser.CompletedNavigationId;

    private bool HasCompletedNavigation(ulong previous) => HasFinishedNavigation(previous) && _browser.LastNavigationSucceeded;

    private void RequestBrowserAttention()
    {
        if (_promptedForConsent) return;
        _promptedForConsent = true;
        _requestAttention();
    }

    private CancellationTokenSource BeginOperation(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _promptedForConsent = false;
        if (_isClosed()) throw new OperationCanceledException("Research tab was closed.", token);
        return _operation = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    private void ThrowIfUnavailable()
    {
        if (_browser.InitializationError is { } error) throw new InvalidOperationException(error);
    }
}
