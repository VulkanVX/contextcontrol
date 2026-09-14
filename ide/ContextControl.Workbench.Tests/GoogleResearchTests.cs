using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using ContextControl.Workbench;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views;

internal static class GoogleResearchTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    public static async Task Run()
    {
        _checks += await GoogleBlockedSourceTests.Run();
        Check(GoogleResearchService.ParseQuery("{\"search\":false}", "hello") is null, "Greetings must not search.");
        Check(GoogleResearchService.ParseQuery("{\"search\":true,\"query\":\"Google capabilities\"}", "Can you use google?") is null, "Capability questions must not become searches.");
        Check(GoogleResearchService.ParseQuery("```json\n{\"search\":true,\"query\":\"  Avalonia   release  \"}\n```", "latest version") == "Avalonia release", "Use the model's normalized query.");
        Check(GoogleResearchService.ParseQuery("I cannot browse", "Search Google for Avalonia") == "Search Google for Avalonia", "A small model's refusal must retain explicit search intent.");
        Check(GoogleResearchService.ParseQuery("bad JSON", "what is the current weather in Vilnius?") is not null, "Current requests need a fallback query.");
        Check(GoogleResearchService.ParseQuery("bad JSON", "Rewrite this text") is null, "Malformed planning must not search ordinary editing requests.");
        Check(GoogleResearchService.ParseQuery("{\"search\":false}", "search for C# examples") is not null, "Explicit searches override a mistaken no-search plan.");
        Check(GoogleResearchService.ParseQuery("<think>{\"search\":false}</think>{\"search\":true,\"query\":\"release date\"}", "latest release") == "release date", "Reasoning JSON must not override the visible plan.");
        Check(GoogleResearchService.ParseSelection("{\"open\":[6,0,2,2,999,1,3]}", 6).SequenceEqual([6,2,1]), "Selections must be bounded, unique result IDs.");
        Check(GoogleResearchService.ParseSelection("{\"open\":[\"https://evil.example\",-1,999]}", 3).SequenceEqual([1,2]), "The model cannot invent a destination URL.");
        Check(GoogleResearchService.ParseSelection("bad", 0).Count == 0, "Empty result sets must stay empty.");
        Check(GoogleResearchService.ParseSelection("{\"id\":[3,1]}", 6).SequenceEqual([3,1]), "Accept the numeric id variant observed from granite3.3:2b without changing its chosen order.");
        foreach (var url in new[] { "file:///C:/secret", "javascript:alert(1)", "http://localhost/a", "http://127.0.0.1/a", "http://10.0.0.1/a", "http://192.168.1.2/a", "https://user:pass@example.com/a", "http://example.com:11434/", "http://[::1]/", "http://[fd00::1]/", "http://service.local/" })
            Check(!GoogleSearchContext.IsPublicWebUrl(url), "Invalid research URL accepted: " + url);
        Check(GoogleSearchContext.IsPublicWebUrl("https://docs.avaloniaui.net/docs/overview"), "Public HTTPS sources must be allowed.");
        Check(GoogleSearchContext.NormalizeQuery(new string('x', 900)).Length == 400, "Query length must be bounded.");
        Check(GoogleSearchContext.IsMatchingSearchPage(GoogleSearchContext.SearchUrl("C# & Vulkan"), "C# & Vulkan"), "Google query encoding must round trip.");
        Check(!GoogleSearchContext.IsMatchingSearchPage("https://consent.google.com/", "test"), "Consent pages are not results.");
        Check(!GoogleSearchContext.IsMatchingSearchPage(GoogleSearchContext.SearchUrl("other chat"), "this chat"), "Concurrent queries must not cross-contaminate.");
        Check(!GoogleResearchWindow.MatchesSourcePage("https://example.com/article", "https://example.com/login"), "A login redirect is not the chosen page.");
        Check(GoogleResearchWindow.MatchesSourcePage("https://example.com/article", "https://www.example.com/article/"), "Canonical www/trailing slash redirects must work.");
        Check(GoogleResearchWindow.MatchesSourcePage("https://www.google.com/goto?url=encoded", "https://docs.avaloniaui.net/docs/overview"), "Encoded Google result redirects must resolve to the public destination.");
        Check(!GoogleResearchWindow.MatchesSourcePage("https://www.google.com/goto?url=encoded", "http://127.0.0.1/"), "Google redirects cannot reach local services.");

        var query = "Avalonia release";
        var search = GoogleSearchContext.ParseBrowserResult(query, JsonSerializer.Serialize(new
        {
            url = GoogleSearchContext.SearchUrl(query),
            results = new[]
            {
                new { title = "Avalonia documentation", url = "https://docs.avaloniaui.net/docs/overview", snippet = "Official docs." },
                new { title = "Release announcement", url = "https://avaloniaui.net/blog/release", snippet = "Release details." },
                new { title = "duplicate", url = "https://avaloniaui.net/blog/release", snippet = "" },
                new { title = "invalid", url = "file:///C:/secret", snippet = "" }
            }
        }));
        Check(search.Sources.Count == 2, "Browser results must filter invalid URLs and duplicates.");
        var browser = new FakeBrowser(search);
        var calls = new List<string>();
        Task<string> Ask(string prompt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            calls.Add(prompt);
            return Task.FromResult(calls.Count == 1 ? "{\"search\":true,\"query\":\"Avalonia release\"}" : "{\"open\":[2,1]}");
        }
        var statuses = new List<string>();
        var research = await GoogleResearchService.ResearchAsync("What is the latest Avalonia release?", Ask, browser, statuses.Add, default);
        Check(browser.Queries.SequenceEqual([query]) && browser.Reads.SequenceEqual([search.Sources[1].Url, search.Sources[0].Url]), "Research must execute the model's query and read its chosen order.");
        Check(calls.Count == 2 && calls[1].Contains(search.Sources[0].Url), "The selector must receive the real ranked search list.");
        Check(research.Pages.Count == 2 && research.Pages.All(page => page.FullPageRead), "Readable page bodies must be preserved.");
        Check(statuses.Any(status => status.StartsWith("Searching")) && statuses.Any(status => status.StartsWith("Reading [2]")), "Research must expose searchable/readable progress.");
        var prompt = GoogleSearchContext.AugmentPrompt("ORIGINAL USER REQUEST", research, 4096);
        Check(prompt.Contains("PAGE BODY") && prompt.Contains("UNTRUSTED REFERENCE DATA") && prompt.EndsWith("ORIGINAL USER REQUEST"), "Final context must include page evidence, its trust boundary, and the unchanged user request.");
        Check(prompt.Contains(search.Sources[1].Url) && prompt.Contains("\"source\":2"), "Citations must keep their original numbers and exact URLs.");
        var budgetResearch = new GoogleResearchResult(search, [new(1, new string('a', 24000), true), new(2, new string('b', 24000), true)]);
        Check(GoogleSearchContext.AugmentPrompt("short", budgetResearch, 4096).Length < 11000, "Web excerpts must fit a bounded input budget.");
        Check(GoogleSearchContext.AugmentPrompt("short", new GoogleResearchResult(search, [new(1, new string('界', 24000), true)]), 4096).Length < 11000, "Escaped non-ASCII page text must obey the same serialized context budget.");
        try { GoogleSearchContext.AugmentPrompt(new string('x', 9000), research, 2048); Check(false, "Oversized prompts must report context pressure."); } catch (InvalidOperationException) { _checks++; }
        browser.FailRead = true;
        calls.Clear();
        var partial = await GoogleResearchService.ResearchAsync("latest release", Ask, browser, _ => { }, default);
        Check(partial.Pages.All(page => !page.FullPageRead), "Failed pages must not be reported as reads.");
        Check(GoogleSearchContext.AugmentPrompt("latest release", partial).Contains("search snippet only"), "Snippet-only evidence must be labelled.");
        var unusedBrowser = new FakeBrowser(search);
        var noResearch = await GoogleResearchService.ResearchAsync("hello", (_, _) => Task.FromResult("{\"search\":false}"), unusedBrowser, _ => { }, default);
        Check(!noResearch.DidSearch && unusedBrowser.Queries.Count == 0 && unusedBrowser.Reads.Count == 0, "No-search plans must never touch the browser.");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try { await GoogleResearchService.ResearchAsync("search now", Ask, browser, _ => { }, cancellation.Token); Check(false, "Cancellation must stop planning."); } catch (OperationCanceledException) { _checks++; }

        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ContextControlGoogleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = WorkbenchSettings.Load(root);
        Check(settings.GoogleSearchEnabled, "Google auto must default on.");
        var vm = new ContextControlViewModel(settings, refreshProviders: false);
        vm.ToggleGoogleSearchCommand.Execute(null);
        Check(SpinWait.SpinUntil(() => !WorkbenchSettings.Load(root).GoogleSearchEnabled, 2000), "Google off must persist.");
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        vm.SwitchPromptToCodexCommand.Execute(null);
        Check(!vm.CanUseGoogleSearch && notifications.Contains(nameof(vm.CanUseGoogleSearch)), "Non-local prompt modes must hide the Google control.");
        var sourceAttachment = new ContextControlAttachmentViewModel("[1] Official source", "https://example.com/" + new string('a', 650), "web");
        var assistant = new LocalLlmChatMessageViewModel("assistant", "Verified answer [1].");
        var attachmentChanged = false;
        assistant.PropertyChanged += (_, e) => attachmentChanged |= e.PropertyName == nameof(assistant.HasAttachments);
        assistant.AttachedFiles.Add(sourceAttachment);
        Check(attachmentChanged && sourceAttachment.DisplayTitle == "[1] Official source", "Source chips must show the citation title and invalidate layout.");
        var session = ChatSessionViewModel.CreateNew();
        session.Append(assistant);
        var history = new ChatHistoryService(root);
        history.Save(new ChatHistoryDocument { Sessions = [session.ToData()] }, root, "chat", mirrorDefaultScope: false);
        var restored = new ChatSessionViewModel(history.Load(root).Sessions.Single()).CreateMessageAt(0);
        Check(restored.AttachedFiles.Single().Path == sourceAttachment.Path && restored.AttachedFiles.Single().Kind == "web", "Exact source links must survive history persistence, including long URLs.");
        vm.ChatMonitor.Dispose();
        Console.WriteLine($"Google research regression passed: {_checks} checks.");
    }

    private sealed class FakeBrowser(GoogleSearchResult search) : IGoogleResearchBrowser
    {
        public List<string> Queries { get; } = [];
        public List<string> Reads { get; } = [];
        public bool FailRead { get; set; }
        public Task<GoogleSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Queries.Add(query); return Task.FromResult(search);
        }
        public Task<GooglePageContent> ReadPageAsync(GoogleSearchSource source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Reads.Add(source.Url);
            if (FailRead) throw new InvalidOperationException("test page blocked");
            return Task.FromResult(new GooglePageContent(source.Url, "PAGE BODY for " + source.Title));
        }
    }
}
