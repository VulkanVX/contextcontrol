using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using ContextControl.Workbench.Controls;
using ContextControl.Workbench.Services;
using Microsoft.Web.WebView2.Core;

internal static class GoogleBrowserTests
{
    public static void Run(string? model, bool pizza = false, bool photo = false, bool knowledge = false, string media = "", bool workspace = false, bool sourceOnly = false)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("This opt-in browser check requires Windows.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            GoogleBrowserTestApp.Model = model;
            GoogleBrowserTestApp.Pizza = pizza;
            GoogleBrowserTestApp.Photo = photo;
            GoogleBrowserTestApp.Knowledge = knowledge;
            GoogleBrowserTestApp.Media = media;
            GoogleBrowserTestApp.Workspace = workspace;
            GoogleBrowserTestApp.SourceOnly = sourceOnly;
            GoogleBrowserTestApp.Failed = ex => failure = ex;
            try { AppBuilder.Configure<GoogleBrowserTestApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Google browser check failed.", failure);
    }
}

public sealed class GoogleBrowserTestApp : Application
{
    internal static string? Model;
    internal static bool Pizza;
    internal static bool Photo;
    internal static bool Knowledge;
    internal static string Media = "";
    internal static bool Workspace;
    internal static bool SourceOnly;
    internal static Action<Exception>? Failed;
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime) return;
        var profile = Path.Combine(Path.GetTempPath(), "ContextControlBrowserTests", Guid.NewGuid().ToString("N"));
        var browser = new WebView2Host { UserDataFolder = profile, AllowDownloads = false };
        browser.Navigate("about:blank");
        var fixture = new Window { Title = "ContextControl browser extraction check", Width = 800, Height = 520, Content = browser, ShowActivated = false };
        lifetime.MainWindow = fixture;
        fixture.Opened += async (_, _) =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            GoogleResearchBrowser? researchBrowser = null;
            try
            {
                while (!browser.IsReady)
                {
                    if (browser.InitializationError is { } error) throw new InvalidOperationException(error);
                    await Task.Delay(100, deadline.Token);
                }
                var core = (CoreWebView2)typeof(WebView2Host).GetField("_webView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(browser)!;
                core.NavigateToString("""
                    <!doctype html><html><head><meta property="og:image" content="https://example.com/hero.jpg"></head><body id="fixture">
                    <main><div class="MjjYud"><a href="https://docs.avaloniaui.net/docs/overview"><h3>Avalonia documentation</h3></a><img src="data:image/png;base64,AAAA" width="120" height="80"><p>Official documentation for desktop applications.</p></div>
                    <div class="MjjYud"><a href="https://avaloniaui.net/blog"><h3>Release notes</h3></a><p>The newest release announcements.</p></div>
                    <a href="https://translate.google.com/example"><h3>Išversti ŠĢ Puslapī</h3></a>
                    <a href="javascript:void(0)"><h3>Invalid result</h3></a>
                    <article>Readable public article text with facts. <h2>Riverglades</h2><aside><figure><img src="https://example.com/riverglades.jpg" width="320" height="180" alt="Riverglades landscape"><figcaption>Riverglades in World of Warcraft Forever</figcaption></figure></aside><div class="newsletter-promo"><img src="https://example.com/subscribe.jpg" width="320" height="180" alt="Subscribe to our newsletters!"></div><span hidden>HIDDEN_MARKER</span><script type="text/plain">SCRIPT_MARKER</script><nav>NAV_MARKER</nav></article></main>
                    </body></html>
                    """);
                while (await core.ExecuteScriptAsync("!!document.getElementById('fixture')") != "true") await Task.Delay(100, deadline.Token);
                using var results = JsonDocument.Parse(await browser.ExecuteScriptAsync(GoogleResearchScripts.SearchResults));
                if (results.RootElement.GetProperty("results").GetArrayLength() != 2) throw new InvalidOperationException("Google result DOM extraction did not retain exactly two valid fixture links.");
                var cards = results.RootElement.GetProperty("results");
                if (cards[0].GetProperty("imageUrl").GetString() != "data:image/png;base64,AAAA" || cards[1].GetProperty("imageUrl").GetString() != "")
                    throw new InvalidOperationException("Search thumbnails must belong to their own result, never a neighboring card.");
                using var page = JsonDocument.Parse(await browser.ExecuteScriptAsync(GoogleResearchScripts.PageText));
                if (page.RootElement.GetProperty("images").EnumerateArray().Any(item => item.GetProperty("url").GetString()!.Contains("subscribe")))
                    throw new InvalidOperationException("Page photo extraction must exclude newsletter promotions.");
                if (page.RootElement.GetProperty("imageUrl").GetString() != "https://example.com/hero.jpg") throw new InvalidOperationException("The page reader must extract the source's preview image metadata.");
                if (!page.RootElement.GetProperty("images").EnumerateArray().Any(item => item.GetProperty("section").GetString() == "Riverglades" && item.GetProperty("caption").GetString()!.Contains("World of Warcraft")))
                    throw new InvalidOperationException("Article image captions and section ownership were not extracted.");
                var body = page.RootElement.GetProperty("text").GetString()!;
                if (!body.Contains("Readable public article") || body.Contains("HIDDEN_MARKER") || body.Contains("SCRIPT_MARKER") || body.Contains("NAV_MARKER"))
                    throw new InvalidOperationException("Page reader must include visible article text and exclude hidden/navigation/script text.");
                Console.WriteLine("Native WebView2 extraction passed: result links, per-source thumbnails, page photo metadata, and visible article text.");
                core.NavigateToString("""
                    <!doctype html><html><head><title>Reddit</title></head><body id="blocked-fixture"><main>
                    <h1>You've been blocked by network security</h1>
                    <p>To continue, log in to your Reddit account or use your developer token. If you think you've been blocked by mistake, contact support.</p>
                    </main></body></html>
                    """);
                while (await core.ExecuteScriptAsync("!!document.getElementById('blocked-fixture')") != "true") await Task.Delay(100, deadline.Token);
                var blockedJson = await browser.ExecuteScriptAsync(GoogleResearchScripts.PageText);
                try { GooglePageReader.Parse(blockedJson); throw new Exception("The reader accepted the Reddit block screen as article text."); }
                catch (GooglePageUnavailableException) { Console.WriteLine("Native WebView2 block-screen detection passed."); }
                fixture.Close();
                if (Workspace) { await WorkspaceBrowserTests.RunAsync(deadline.Token, Model, SourceOnly); return; }
                if (!string.IsNullOrWhiteSpace(Model))
                {
                    researchBrowser = new GoogleResearchBrowser(Console.WriteLine);
                    var local = new LocalLlmService();
                    async Task<string> Ask(string prompt, CancellationToken token)
                    {
                        var response = await local.SendChatAsync(new LocalLlmRequest(Model!, prompt, "research-test", [], Pizza ? 4096 : 8192, Think: false, MaxOutputTokens: 256), null, null, token);
                        token.ThrowIfCancellationRequested();
                        Console.WriteLine("MODEL PLAN: " + response.Message);
                        return GoogleResearchService.PlannerText(response);
                    }
                    if (Knowledge)
                    {
                        await CheckKnowledgeRecoveryAsync(local, researchBrowser, Ask, deadline.Token);
                        return;
                    }
                    if (Media.Length > 0)
                    {
                        await CheckMediaAsync(local, researchBrowser, Ask, deadline.Token);
                        return;
                    }
                    var question = Photo ? "Show me a photo of Nvidia 5090"
                        : Pizza ? "Search for pizza places in Vilnius. Suggest three places with citations in under 150 words."
                        : "Search Google for the official Avalonia UI documentation. Open a result and briefly explain what Avalonia is, citing the source.";
                    var researchTask = GoogleResearchService.ResearchAsync(question, Ask, researchBrowser, Console.WriteLine, deadline.Token);
                    for (var tick = 0; tick < 15 && !researchTask.IsCompleted; tick++) await Task.Delay(1000, deadline.Token);
                    if (!researchTask.IsCompleted)
                    {
                        var researchWindow = typeof(GoogleResearchBrowser).GetField("_window", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(researchBrowser);
                        if (researchWindow is not null)
                        {
                            var host = (WebView2Host)researchWindow.GetType().GetField("_browser", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(researchWindow)!;
                            if (host.IsReady) Console.WriteLine("BROWSER DIAGNOSTIC: " + await host.ExecuteScriptAsync("({url:location.href,title:document.title,h3:document.querySelectorAll('h3').length,body:document.body.innerText.slice(0,1200),headings:Array.from(document.querySelectorAll('h3')).slice(0,2).map(h=>h.outerHTML+' PARENT: '+h.parentElement.outerHTML.slice(0,1200))})"));
                        }
                    }
                    var research = await researchTask;
                    if (!research.DidSearch || !research.Pages.Any(page => page.FullPageRead)) throw new InvalidOperationException("Live research must search and read at least one real page.");
                    foreach (var source in research.Search!.Sources) Console.WriteLine("SOURCE: " + source.Url);
                    var previews = await GooglePhotoPreviewService.Shared.LoadManyAsync(research.Search.Sources, deadline.Token);
                    var photoCount = previews.Count(File.Exists);
                    if (photoCount == 0) throw new InvalidOperationException("Live research did not produce any cached source photo previews.");
                    Console.WriteLine($"Live source photo previews passed: {photoCount} decoded and cached images.");
                    var context = Pizza ? 4096 : 8192;
                    var response = await local.SendChatAsync(new LocalLlmRequest(Model, GoogleSearchContext.AugmentPrompt(question, research, context), "research-test", [], context, Think: false, MaxOutputTokens: 600), null, null, deadline.Token);
                    if (!response.Succeeded || string.IsNullOrWhiteSpace(response.Message)) throw new InvalidOperationException(response.Status);
                    var message = new ContextControl.Workbench.ViewModels.LocalLlmChatMessageViewModel("assistant", response.Message);
                    if (string.IsNullOrWhiteSpace(message.VisibleText) || response.Stats?.OutputTokens is not > 0)
                        throw new InvalidOperationException("A research answer must have visible text and populated token counts.");
                    Console.WriteLine("FINAL ANSWER: " + response.Message);
                    if (Pizza || Photo)
                    {
                        var entryPhotos = new List<GoogleEntryPhoto>();
                        await GoogleEntryPhotoService.LoadAsync(response.Message!, research, researchBrowser, photo =>
                        {
                            entryPhotos.Add(photo);
                            Console.WriteLine($"ENTRY PHOTO: {photo.EntryTitle} <- {photo.SourceTitle} ({photo.SourceUrl})");
                            Console.WriteLine("PHOTO FILE: " + photo.PreviewPath);
                        }, deadline.Token);
                        if (entryPhotos.Count == 0) throw new InvalidOperationException("The live entry-photo check did not find an individual place photo within its budget.");
                        Console.WriteLine($"Live per-entry photos passed: {entryPhotos.Count} matched and cached photos.");
                    }
                    Console.WriteLine($"Live Google research passed with {Model}: {research.Search.Sources.Count} results, {research.Pages.Count(page => page.FullPageRead)} pages read.");
                }
            }
            catch (Exception ex) { Failed?.Invoke(ex); }
            finally { researchBrowser?.Dispose(); fixture.Close(); lifetime.Shutdown(); }
        };
    }

    private static async Task CheckKnowledgeRecoveryAsync(LocalLlmService local, IGoogleResearchBrowser browser,
        Func<string, CancellationToken, Task<string>> ask, CancellationToken token)
    {
        const string question = "Show me a photo of Nvidia 5090";
        GoogleResearchResult? research = null;
        var generations = 0;
        // Reproduce the user's already-observed knowledge gap, then use real Google pages and local generation.
        var draft = new LocalLlmChatResult(true, "seeded unknown draft", "I don't have information about that product in my training data.");
        Console.WriteLine("Knowledge recovery live check: seeded unknown draft; real Google, pages, model answer and photo follow.");
        var answer = await GoogleKnowledgeRecovery.RecoverAsync(question, draft, true, false, async cancellation =>
        {
            research = await GoogleResearchService.ResearchAsync(question, ask, browser, Console.WriteLine, cancellation, knowledgeGap: true);
            if (!research.DidSearch || !research.Pages.Any(page => page.FullPageRead)) throw new InvalidOperationException("No readable live evidence.");
            return GoogleSearchContext.AugmentPrompt(question, research, 8192);
        }, async (prompt, cancellation) =>
        {
            generations++;
            return await local.SendChatAsync(new LocalLlmRequest(Model!, prompt, "research-test", [], 8192, Think: false, MaxOutputTokens: 600), null, null, cancellation);
        }, token);
        Console.WriteLine("RECOVERED ANSWER: " + answer.Message);
        if (System.Text.RegularExpressions.Regex.IsMatch(answer.Message ?? "", @"(?:unable|cannot|can['’]t)\s+(?:to\s+)?(?:display|show|provide)\s+(?:the\s+|any\s+)?images?", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new InvalidOperationException("The model still incorrectly denies the host's image display capability.");
        if (!answer.Succeeded || generations != 1 || research is null || GoogleKnowledgeRecovery.NeedsLookup(question, answer.Message)
            || string.IsNullOrWhiteSpace(answer.Message) || answer.Stats?.OutputTokens is not > 0)
            throw new InvalidOperationException("Recovery did not produce one visible answer from real evidence.");
        var photos = new List<GoogleEntryPhoto>();
        await GoogleEntryPhotoService.LoadAsync(answer.Message!, research, browser, photo =>
        {
            photos.Add(photo);
            Console.WriteLine($"RECOVERED PHOTO: {photo.EntryTitle} <- {photo.SourceTitle} ({photo.SourceUrl}); {photo.PreviewPath}");
        }, token);
        if (photos.Count == 0) throw new InvalidOperationException("Recovery did not produce a matched subject photo.");
        Console.WriteLine($"Live knowledge recovery passed: {research.Search!.Sources.Count} results, {research.Pages.Count(page => page.FullPageRead)} pages read, {photos.Count} photos, one final generation.");
    }

    private static async Task CheckMediaAsync(LocalLlmService local, IGoogleResearchBrowser browser,
        Func<string, CancellationToken, Task<string>> ask, CancellationToken token)
    {
        var question = Media == "tallinn" ? "Search for the best bars in Tallinn. Recommend three actual named venues with citations and photos."
            : "What is WoW Forever? Explain the update and show me photos of it.";
        var research = await GoogleResearchService.ResearchAsync(question, ask, browser, Console.WriteLine, token);
        foreach (var (source, index) in research.Search!.Sources.Select((source, index) => (source, index)))
            Console.WriteLine($"SOURCE {index + 1}: {source.Title} ({source.Url}); page images: {research.Pages.FirstOrDefault(page => page.SourceNumber == index + 1)?.Images?.Count ?? 0}");
        var prompt = GoogleSearchContext.AugmentPrompt(question, research, 8192);
        Task<LocalLlmChatResult> Generate(string text, CancellationToken cancellation) => local.SendChatAsync(new LocalLlmRequest(Model!, text, "research-test", [], 8192, Think: false, MaxOutputTokens: 1000), null, null, cancellation);
        var answer = await GoogleEvidenceText.ReviewAsync(await Generate(prompt, token), prompt, Generate, token);
        if (!answer.Succeeded || string.IsNullOrWhiteSpace(answer.Message) || GoogleEvidenceText.HasInterfaceEntry(answer.Message)) throw new InvalidOperationException("No usable researched answer.");
        Console.WriteLine("FINAL ANSWER: " + answer.Message);
        var photos = new List<GoogleEntryPhoto>();
        await GoogleEntryPhotoService.LoadAsync(answer.Message, research, browser, photo =>
        {
            photos.Add(photo); Console.WriteLine("MATCHED PHOTO: " + JsonSerializer.Serialize(photo));
        }, token, status: Console.WriteLine);
        var minimum = 2;
        if (photos.Count < minimum) throw new InvalidOperationException($"Expected at least {minimum} distinct matched photos, got {photos.Count}.");
        if (Media == "tallinn" && photos.Select(photo => photo.EntryTitle).Distinct().Count() < 2) throw new InvalidOperationException("Venue photos did not cover distinct bars.");
        var proof = Path.Combine(".tmp", "media-" + Media + ".json");
        answer = answer with { Message = GoogleEvidenceText.WithoutPhotoCapabilityClaims(answer.Message!) };
        Directory.CreateDirectory(".tmp");
        await File.WriteAllTextAsync(proof, JsonSerializer.Serialize(new { Question = question, Answer = answer.Message, Photos = photos }, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine($"Live {Media} research/media passed: {photos.Count} matched photos. Proof: {Path.GetFullPath(proof)}");
    }
}
