using System.Text.Json;
using ContextControl.Workbench.Services;

internal static class GoogleBlockedSourceTests
{
    public static async Task<int> Run()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        const string redditBlock = "You've been blocked by network security. To continue, log in to your Reddit account or use your developer token.";
        string Page(string title, string text, bool article = false, string gate = "") => JsonSerializer.Serialize(new
        {
            url = "https://www.reddit.com/r/example/comments/test/", title, text, heading = title, hasArticle = article, gateText = gate
        });
        foreach (var status in new[] { 401, 403, 404, 429, 451, 503 })
        {
            try { GooglePageReader.ThrowIfHttpError("https://www.reddit.com/", status); Check(false, "Failed HTTP responses must be rejected immediately."); }
            catch (GooglePageUnavailableException ex) { Check(ex.Message.Contains(status.ToString()) && ex.PageUrl == "https://www.reddit.com/", "Failures must preserve status and the actual destination."); }
        }
        GooglePageReader.ThrowIfHttpError("https://example.com/", 200);
        foreach (var payload in new[]
        {
            JsonSerializer.Serialize(new { url = "https://consent.google.com/m", title = "Prieš pereinant į „Google“", text = "Naudojame slapukus ir duomenis. " + new string('a', 6000) }),
            JsonSerializer.Serialize(new { url = "https://www.google.com/search?q=test", title = "Prieš pereinant į „Google“", text = "Slapukų nuostatos" }),
            JsonSerializer.Serialize(new { url = "https://www.facebook.com/login/?next=post", title = "Facebook", text = new string('a', 6000) }),
            JsonSerializer.Serialize(new { url = "https://www.facebook.com/posts/42", title = "Facebook", text = new string('a', 6000), hasPasswordField = true }),
            JsonSerializer.Serialize(new { url = "https://www.facebook.com/posts/42", title = "Facebook", text = new string('a', 6000), needsConsent = true })
        })
        {
            try { GooglePageReader.Parse(payload); Check(false, "Consent/login screens must never reach model evidence."); }
            catch (GooglePageUnavailableException) { checks++; }
        }
        Check(!BrowserPageGate.IsAuthenticationUrl("https://www.facebook.com/public-page/posts/123"), "Public Facebook post URLs must remain eligible.");
        Check(GooglePageReader.Parse(JsonSerializer.Serialize(new { url = "https://example.com/article", title = "Why Google's consent screen says before you continue to Google", text = "An article about privacy settings.", hasArticle = true })).Text.Length > 0,
            "Articles discussing consent must not be mistaken for a consent screen.");
        foreach (var json in new[]
        {
            Page("Reddit", redditBlock), Page("Just a moment...", "Checking your browser before continuing."),
            Page("Access denied", "You cannot access this page."), Page("Article", new string('a', 6000), true, "Sign in to continue")
        })
        {
            try { GooglePageReader.Parse(json); Check(false, "Access screens must not be passed to the model as article text."); }
            catch (GooglePageUnavailableException) { checks++; }
        }
        Check(GooglePageReader.Parse(Page("Discussing Reddit blocks", "The error reads: " + redditBlock, article: true)).Text.Contains(redditBlock), "An article discussing a block must remain readable.");
        Check(GooglePageReader.Parse(Page("Normal article", "Use sign in to continue in the navigation menu. " + new string('x', 6000))).Text.Length > 6000, "Long content must not be rejected for a passing mention of sign-in.");
        Check(GoogleResearchService.ParseSelection("{\"id\":4}", 6, [3,4,5]).SequenceEqual([4]), "Small models' single numeric selections must retain their choice.");
        Check(GoogleResearchService.ParseSelection("{\"open\":[1,2,6,6,4]}", 6, [3,4,5,6]).SequenceEqual([6,4]), "Replacement choices must exclude already attempted IDs.");
        Check(GoogleResearchService.ParseSelection("bad JSON", 6, [4,5,6]).SequenceEqual([4,5]), "Invalid replacements must fall back to untried results only.");

        var browser = new Browser([1]);
        var prompts = new List<string>();
        var progress = new List<string>();
        Task<string> Ask(string prompt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); prompts.Add(prompt);
            return Task.FromResult(prompts.Count switch { 1 => "{\"search\":true,\"query\":\"test\"}", 2 => "{\"open\":[1,2]}", _ => "{\"id\":4}" });
        }
        var result = await GoogleResearchService.ResearchAsync("Search test", Ask, browser, progress.Add, default);
        Check(browser.Reads.SequenceEqual([1,2,4]), "After a block, read the model's replacement without re-reading the failed page.");
        Check(result.Pages.Single(page => page.SourceNumber == 1).FullPageRead == false && result.Pages.Count(page => page.FullPageRead) == 2, "Keep both the failed evidence and successful replacement pages.");
        Check(result.Search!.Sources[0].Url == "https://www.reddit.com/r/example/comments/test/", "Resolve a blocked Google redirect to its real destination for attribution.");
        Check(prompts.Count == 3 && prompts[2].Contains("UNAVAILABLE") && prompts[2].Contains("HTTP 403"), "The model must receive failure evidence when selecting replacements.");
        Check(progress.Any(line => line.Contains("unavailable")), "Blocked sources must appear in progress.");
        var final = GoogleSearchContext.AugmentPrompt("Read the Reddit source", result);
        Check(final.Contains("could not be read") && !final.Contains(redditBlock), "Final evidence must state limitations without treating the block screen as content.");

        prompts.Clear(); browser = new Browser([1,2,3,4,5,6]);
        Task<string> AllBlocked(string prompt, CancellationToken token)
        {
            prompts.Add(prompt);
            return Task.FromResult(prompts.Count == 1 ? "{\"search\":true,\"query\":\"test\"}" : prompts.Count == 2 ? "{\"open\":[1,2,3]}" : "{\"open\":[4,5,6]}");
        }
        var blocked = await GoogleResearchService.ResearchAsync("Search test", AllBlocked, browser, _ => { }, default);
        Check(browser.Reads.Count == GoogleResearchService.MaxReadAttempts && browser.Reads.Distinct().Count() == browser.Reads.Count, "Repeated blocks must stop after five distinct attempts.");
        Check(prompts.Count == 3 && blocked.Pages.All(page => !page.FullPageRead), "Only one replacement model call is allowed, with no fabricated readable pages.");
        Check(GoogleSearchContext.AugmentPrompt("Read the sources", blocked).Contains("Google snippets only"), "All-blocked answers must explicitly disclose snippet-only evidence.");

        using var cancellation = new CancellationTokenSource();
        prompts.Clear(); browser = new Browser([1]);
        Task<string> CancelReplacement(string prompt, CancellationToken token)
        {
            prompts.Add(prompt);
            if (prompts.Count == 3) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(prompts.Count == 1 ? "{\"search\":true,\"query\":\"test\"}" : "{\"open\":[1,2]}");
        }
        try { await GoogleResearchService.ResearchAsync("Search test", CancelReplacement, browser, _ => { }, cancellation.Token); Check(false, "Cancellation must stop replacement selection."); }
        catch (OperationCanceledException) { Check(browser.Reads.SequenceEqual([1,2]), "Cancelling before fallback must prevent replacement visits."); }
        return checks;
    }

    private sealed class Browser(int[] blocked) : IGoogleResearchBrowser
    {
        public List<int> Reads { get; } = [];
        public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken cancellationToken) => Task.FromResult(new GoogleSearchResult(query, GoogleSearchContext.SearchUrl(query),
            Enumerable.Range(1,6).Select(number => new GoogleSearchSource("Source " + number, $"https://source{number}.example/{number}", "Search snippet")).ToArray()));
        public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = int.Parse(new Uri(source.Url).AbsolutePath.Trim('/')); Reads.Add(number);
            if (blocked.Contains(number)) throw new GooglePageUnavailableException("https://www.reddit.com/r/example/comments/test/", "This source blocked access (HTTP 403).");
            return Task.FromResult(new GooglePageContent(source.Url, "Public page body for " + source.Title));
        }
    }
}
