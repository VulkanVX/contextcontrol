using Avalonia.Controls;
using ContextControl.Workbench.Services;
using ContextControl.Workbench.ViewModels;
using ContextControl.Workbench.Views.MainWindowParts;

internal static class WorkspaceBrowserTests
{
    internal static async Task RunAsync(CancellationToken token, string? liveModel = null, bool sourceOnly = false)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        var root = Path.Combine(Path.GetTempPath(), "ContextControlWorkspaceBrowser", Guid.NewGuid().ToString("N"));
        var pane = new BrowserPaneViewModel(root, null, "default");
        var personal = pane.SelectedTab!; personal.Url = "about:blank";
        var surface = new BrowserSurface(); surface.BindPane(pane);
        var window = new Window { Title = "ContextControl independent browser tabs check", Width = 1080, Height = 700, Content = surface, ShowActivated = false };
        var stoppedA = 0; var stoppedB = 0;
        using var manager = new WorkspaceResearchBrowser(pane, surface.GetHost, tab => { pane.SelectTab(tab); });
        try
        {
            window.Show();
            var tabA = pane.OpenResearchTab("A", "Game research");
            var tabB = pane.OpenResearchTab("B", "Place research");
            using var sessionA = manager.CreateSession("A", "Game research", () => stoppedA++, token);
            using var sessionB = manager.CreateSession("B", "Place research", () => stoppedB++, token);
            Check(pane.ActiveAgentCount == 2 && ReferenceEquals(pane.SelectedTab, personal), "Research must create two actual working tabs without changing the personal tab.");
            var hosts = new[] { surface.GetHost(personal), surface.GetHost(tabA), surface.GetHost(tabB) };
            Check(hosts.Distinct().Count() == 3, "Every tab needs an independent WebView2 instance.");
            Check(hosts[1].NavigationFilter?.Invoke("http://127.0.0.1/") == false && !hosts[1].AllowDownloads, "Research restrictions must apply to its own tab.");
            // Isolated HTML fixtures replace network navigation only inside this test.
            foreach (var (host, index) in hosts.Select((host, index) => (host, index)))
            {
                host.NavigationFilter = null;
                host.NavigateHtml($"<!doctype html><html><head><title>Fixture {index}</title></head><body style='background:#e8f0ed'><h1>Tab {index}</h1><script>window.identity='tab-{index}'</script></body></html>");
            }
            async Task WaitUntil(Func<Task<bool>> condition)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
                while (!await condition()) await Task.Delay(100, timeout.Token);
            }
            await WaitUntil(() => Task.FromResult(hosts.All(host => host.IsReady)));
            for (var i = 0; i < hosts.Length; i++)
            {
                var index = i;
                await WaitUntil(async () => await hosts[index].ExecuteScriptAsync("window.identity") == $"\"tab-{index}\"");
            }
            Check(await hosts[1].ExecuteScriptAsync("innerWidth >= 800 && innerHeight >= 500") == "true", "Hidden research pages need a usable viewport.");
            await hosts[0].ExecuteScriptAsync("window.personalDraft='keep my work'");
            pane.SelectTab(tabA); pane.SelectTab(tabB); pane.SelectTab(personal);
            Check(await hosts[0].ExecuteScriptAsync("window.personalDraft") == "\"keep my work\"", "Tab switching must not reload or lose personal page state.");
            Check(await hosts[1].ExecuteScriptAsync("window.identity") == "\"tab-1\"" && await hosts[2].ExecuteScriptAsync("window.identity") == "\"tab-2\"", "Background tabs retain independent page state.");
            sessionA.SetStatus("Searching Google: World of Warcraft racials");
            sessionA.SetStatus("Reading [1] Official source");
            Check(tabA.ActionPreview.Contains("Searching Google") && tabA.ActionPreview.Contains("Reading [1]"), "Action previews must contain the real navigation stages.");
            pane.IsActionPreviewEnabled = true;
            await WaitUntil(() => Task.FromResult(tabA.ActionPreviewImage is not null && tabB.ActionPreviewImage is not null));
            Check(tabA.ActionPreviewImage!.PixelSize.Width == 360, "Page preview capture is a bounded thumbnail.");
            pane.IsActionPreviewEnabled = false;
            Check(tabA.ActionPreviewImage is null && tabB.ActionPreviewImage is null, "Disabling previews must release their images without stopping research.");
            pane.CloseTab(tabA);
            Check(stoppedA == 1 && stoppedB == 0 && pane.ActiveAgentCount == 1, "Closing one working tab cancels only its own request and updates the agent count.");
            sessionA.Dispose(); sessionB.Dispose();
            Check(pane.ActiveAgentCount == 0 && tabB.AgentStatus == "Done", "Completing research clears the working indicator.");
            pane.CloseTab(personal); pane.CloseTab(tabB);
            Check(pane.Tabs.Count == 1 && pane.SelectedTab is not null && !pane.SelectedTab.IsResearch, "Closing the final research tab leaves a usable browser tab.");

            var message = new LocalLlmChatMessageViewModel("assistant", "# A sourced subject\n\nA useful **overview**. [1]\n\n## Details\n\n<script>window.injected=true</script>\n\n[unsafe](javascript:alert(1))", "fixture",
                attachments: [new("[1] Official source", "https://example.com/source", "web")]);
            for (var i = 0; i < 4; i++)
            {
                var path = Path.Combine(root, "photo-" + i + ".png"); File.WriteAllBytes(path, GooglePhotoPreviewTests.Photo(640 + i, 360));
                message.AttachedFiles.Add(new("Illustration source", "https://example.com/photo-" + i, "web", path, "A sourced subject") { IsSubjectPhoto = true, PhotoCaption = "Source illustration " + (i + 1) });
            }
            var article = ResearchArticlePage.Build(message);
            var articleTab = pane.OpenArticle(article.Title, article.Html);
            var articleHost = surface.GetHost(articleTab);
            await WaitUntil(() => Task.FromResult(articleHost.IsReady));
            await WaitUntil(async () => await articleHost.ExecuteScriptAsync("!!document.querySelector('main h1')") == "true");
            Check(await articleHost.ExecuteScriptAsync("document.querySelector('h1').innerText") == "\"A sourced subject\"", "The generated article must open in the existing Browser workspace.");
            Check(await articleHost.ExecuteScriptAsync("!window.injected && !document.querySelector('script') && !document.querySelector('a[href^=\"javascript:\"]')") == "true", "Article content must not execute model-supplied markup or links.");
            Check(await articleHost.ExecuteScriptAsync("!!document.querySelector('nav a[href^=\"#section-\"]') && !!document.querySelector('#ref-1')") == "true", "Article section navigation and source anchors must exist.");
            Check(await articleHost.ExecuteScriptAsync("document.querySelectorAll('.collage img').length === 4 && Array.from(document.images).every(i=>i.complete&&i.naturalWidth>0)") == "true", "All four embedded collage images must load in the real Browser page.");
            Directory.CreateDirectory(".tmp");
            var core = (Microsoft.Web.WebView2.Core.CoreWebView2)typeof(ContextControl.Workbench.Controls.WebView2Host).GetField("_webView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(articleHost)!;
            await using (var capture = File.Create(".tmp/native-article.png")) await core.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture);
            await articleHost.ExecuteScriptAsync("document.querySelector('nav a[href=\"#references\"]').click()");
            await WaitUntil(async () => await articleHost.ExecuteScriptAsync("location.hash") == "\"#references\"");
            Check(true, "Clicking the contents actually navigates to the matching article section.");
            Console.WriteLine($"Native workspace browser passed: {checks} checks; independent hidden tabs, retained state, cancellation, thumbnails, and article navigation.");
            if (liveModel is not null)
            {
                using var liveRequest = CancellationTokenSource.CreateLinkedTokenSource(token);
                using var researchTab = manager.CreateSession("live", "WoW Forever · live research check", () => liveRequest.Cancel(), liveRequest.Token);
                var local = new LocalLlmService();
                async Task<string> Ask(string prompt, CancellationToken ct)
                {
                    var result = await local.SendChatAsync(new LocalLlmRequest(liveModel, prompt, "research-test", [], 8192, Think: false, MaxOutputTokens: 256), null, null, ct);
                    return GoogleResearchService.PlannerText(result);
                }
                const string question = "What is World of Warcraft Forever? Explain in under 150 words using sources and show photos of the game.";
                GoogleResearchResult research;
                if (sourceOnly)
                {
                    var source = new GoogleSearchSource("World of Warcraft: Forever Found Photos Panel Recap", "https://worldofwarcraft.blizzard.com/en-us/news/24304071/world-of-warcraft-forever-found-photos-panel-recap", "Previously retrieved official Blizzard source");
                    var pageContent = await researchTab.ReadPageAsync(source, liveRequest.Token);
                    research = new(new GoogleSearchResult("World of Warcraft Forever", source.Url, [source with { ImageUrl = pageContent.ImageUrl }]),
                        [new(1, pageContent.Text, true, pageContent.Images)], "World of Warcraft Forever", question);
                    Console.WriteLine("Direct-source check: previously retrieved Blizzard page; this run does not repeat Google search.");
                }
                else research = await GoogleResearchService.ResearchAsync(question, Ask, researchTab, status => { Console.WriteLine(status); researchTab.SetStatus(status); }, liveRequest.Token);
                Check(research.DidSearch && research.Pages.Any(page => page.FullPageRead), "Live research must search and read real pages through its workbench tab.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var found = new List<GoogleEntryPhoto>();
                long firstPhoto = -1;
                using var photos = new GoogleLivePhotos(research, researchTab, photo =>
                {
                    if (firstPhoto < 0) firstPhoto = timer.ElapsedMilliseconds;
                    found.Add(photo); Console.WriteLine($"LIVE PHOTO at {timer.Elapsed.TotalSeconds:0.0}s: {photo.EntryTitle} <- {photo.SourceTitle} ({photo.SourceUrl}); {photo.PreviewPath}");
                }, liveRequest.Token);
                var answer = await local.SendChatAsync(new LocalLlmRequest(liveModel, GoogleSearchContext.AugmentPrompt(question, research, 8192), "research-test", [], 8192, Think: false, MaxOutputTokens: 1024), null, null, liveRequest.Token);
                var answerMs = timer.ElapsedMilliseconds;
                await photos.CompleteAsync(answer.Message ?? "");
                Console.WriteLine($"LIVE MODEL RESULT: succeeded={answer.Succeeded}; limited={answer.OutputLimited}; status={answer.Status}; message={answer.Message}");
                Check(answer.Succeeded && !string.IsNullOrWhiteSpace(answer.Message) && found.Count >= 2, "The real model must produce visible text and at least two sourced collage photos.");
                Console.WriteLine($"LIVE ANSWER at {answerMs / 1000d:0.0}s: {answer.Message}");
                Console.WriteLine($"Live workspace {(sourceOnly ? "page+model" : "Google")} check passed: {research.Search!.Sources.Count} sources, {research.Pages.Count(page => page.FullPageRead)} pages, {found.Count} photos; first photo before answer: {firstPhoto < answerMs}.");
                File.WriteAllText(".tmp/live-workspace-media.json", System.Text.Json.JsonSerializer.Serialize(new { sourceOnly, research, answer, photos = found, firstPhotoMs = firstPhoto, answerMs }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        }
        finally { window.Close(); surface.BindPane(null); }
    }
}
